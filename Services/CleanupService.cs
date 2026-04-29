#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CodeCraftOptimizer.Services
{
    /// <summary>
    /// Resultado acumulado de una operación de escaneo o limpieza.
    /// </summary>
    public sealed class CleanupResult
    {
        public int FilesScanned { get; set; }
        public int FilesDeleted { get; set; }
        public int FilesSkipped { get; set; }
        public int FilesFailed { get; set; }
        public long BytesFreed { get; set; }
        public long BytesRecoverable { get; set; }
        public List<string> Errors { get; } = new();

        public string BytesFreedFormatted => FormatBytes(BytesFreed);
        public string BytesRecoverableFormatted => FormatBytes(BytesRecoverable);

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }

    /// <summary>
    /// Servicio de limpieza segura del sistema.
    /// 
    /// Características de seguridad:
    /// - Lista blanca de extensiones críticas protegidas
    /// - Verificación de archivos en uso antes de eliminar
    /// - No toca subdirectorios del sistema protegidos
    /// - Soporte de cancelación en cualquier punto
    /// - Reporte detallado de cada archivo procesado
    /// </summary>
    public sealed class CleanupService
    {
        // ─── P/Invoke: Vaciar papelera de reciclaje ───
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI   = 0x00000002;
        private const uint SHERB_NOSOUND        = 0x00000004;

        // P/Invoke: Obtener tamaño de la papelera
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        // ─── Extensiones protegidas (nunca eliminar) ───
        private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".sys", ".dll", ".exe", ".drv", ".ocx",
            ".msi", ".msp", ".mst", ".cat", ".inf",
            ".mui", ".manifest", ".policy"
        };

        // ─── Nombres de archivo protegidos ───
        private static readonly HashSet<string> ProtectedFileNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "desktop.ini", "thumbs.db", "ntuser.dat", "ntuser.dat.log",
            "pagefile.sys", "hiberfil.sys", "swapfile.sys",
            "bootmgr", "bootmgr.efi"
        };

        // ─── Subdirectorios protegidos dentro de temp ───
        private static readonly HashSet<string> ProtectedSubDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            "microsoft", "windowsapps", "systemprofile",
            "perflog", "diagnostics"
        };

        /// <summary>
        /// Evento disparado por cada archivo procesado.
        /// El string indica el mensaje de log a mostrar.
        /// </summary>
        public event Action<string>? OnLog;

        /// <summary>
        /// Evento de progreso: valor entre 0.0 y 100.0
        /// </summary>
        public event Action<double>? OnProgress;

        // ═══════════════════════════════════════════════════════════════
        //  ESCANEO REAL
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Escanea las carpetas temporales y calcula el espacio recuperable
        /// sin eliminar nada.
        /// </summary>
        public Task<CleanupResult> ScanAsync(CancellationToken ct)
        {
            return Task.Run(() => ScanInternal(ct));
        }

        private CleanupResult ScanInternal(CancellationToken ct)
        {
            var result = new CleanupResult();
            var dirs = GetTempDirectories();

            Log("═══ Iniciando escaneo real del sistema ═══");
            int totalDirs = dirs.Count;

            for (int d = 0; d < totalDirs; d++)
            {
                ct.ThrowIfCancellationRequested();
                string dir = dirs[d];
                Log($"  ▸ Escaneando: {dir}");

                if (!Directory.Exists(dir))
                {
                    Log($"    ⚠ Directorio no encontrado: {dir}");
                    continue;
                }

            int processedFiles = 0;
            var files = SafeEnumerateFiles(dir);

            foreach (var filePath in files)
            {
                ct.ThrowIfCancellationRequested();
                processedFiles++;
                result.FilesScanned++;

                try
                {
                    var fi = new FileInfo(filePath);
                    if (fi.Exists)
                    {
                        if (IsProtectedFile(fi))
                        {
                            result.FilesSkipped++;
                        }
                        else
                        {
                            result.BytesRecoverable += fi.Length;
                        }
                    }
                }
                catch { result.FilesSkipped++; }

                // Heartbeat log cada 1000 archivos para que el usuario vea vida
                if (processedFiles % 1000 == 0)
                {
                    Log($"    ... analizados {processedFiles} archivos en {Path.GetFileName(dir)}");
                }

                // Actualización de progreso suave (estimada)
                if (processedFiles % 100 == 0)
                {
                    ReportProgress(Math.Min(1.0 + (processedFiles / 500.0), 99));
                }
            }
            }

            // Agregar tamaño de la papelera
            long recycleBinSize = GetRecycleBinSize();
            if (recycleBinSize > 0)
            {
                result.BytesRecoverable += recycleBinSize;
                Log($"  ▸ Papelera de reciclaje: {FormatBytesStatic(recycleBinSize)}");
            }

            ReportProgress(100);
            Log($"  ✓ Escaneo completado: {result.FilesScanned} archivos analizados");
            Log($"    Espacio recuperable: {result.BytesRecoverableFormatted}");
            Log($"    Archivos omitidos (protegidos): {result.FilesSkipped}");

            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIMPIEZA RÁPIDA (solo %TEMP%)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Limpieza rápida: elimina archivos de %TEMP% del usuario
        /// y vacía la papelera.
        /// </summary>
        public async Task<CleanupResult> QuickCleanAsync(CancellationToken ct)
        {
            var result = new CleanupResult();
            Log("═══ Limpieza rápida iniciada ═══");

            // Fase 1: Limpiar %TEMP% del usuario (70% del progreso)
            string userTemp = Path.GetTempPath();
            Log($"  ▸ Limpiando: {userTemp}");
            await CleanDirectoryAsync(userTemp, result, ct, 0, 70);

            // Fase 2: Vaciar papelera (30% del progreso)
            ct.ThrowIfCancellationRequested();
            Log("  ▸ Vaciando papelera de reciclaje…");
            await Task.Run(() => EmptyRecycleBin(result), ct);
            ReportProgress(100);

            Log($"  ✓ Limpieza rápida completada: {result.FilesDeleted} eliminados, {result.FilesSkipped} omitidos");
            Log($"    Espacio liberado: {result.BytesFreedFormatted}");
            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIMPIEZA PROFUNDA (%TEMP% + Windows\Temp + Papelera)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Limpieza profunda: elimina archivos de %TEMP%, C:\Windows\Temp
        /// y vacía la papelera de reciclaje.
        /// </summary>
        public async Task<CleanupResult> DeepCleanAsync(CancellationToken ct)
        {
            var result = new CleanupResult();
            Log("═══ Limpieza profunda iniciada ═══");

            var dirs = GetTempDirectories();
            int totalPhases = dirs.Count + 1; // +1 por la papelera
            double phaseWeight = 100.0 / totalPhases;

            for (int d = 0; d < dirs.Count; d++)
            {
                ct.ThrowIfCancellationRequested();
                string dir = dirs[d];
                Log($"  ▸ Limpiando: {dir}");

                if (!Directory.Exists(dir))
                {
                    Log($"    ⚠ Directorio no encontrado: {dir}");
                    continue;
                }

                double progressStart = d * phaseWeight;
                double progressEnd = progressStart + phaseWeight;
                await CleanDirectoryAsync(dir, result, ct, progressStart, progressEnd);
            }

            // Última fase: papelera
            ct.ThrowIfCancellationRequested();
            Log("  ▸ Vaciando papelera de reciclaje…");
            await Task.Run(() => EmptyRecycleBin(result), ct);
            ReportProgress(100);

            Log($"  ✓ Limpieza profunda completada: {result.FilesDeleted} eliminados, {result.FilesSkipped} omitidos");
            Log($"    Espacio liberado: {result.BytesFreedFormatted}");
            return result;
        }

        // ═══════════════════════════════════════════════════════════════
        //  CORE: Limpieza de un directorio
        // ═══════════════════════════════════════════════════════════════

        private async Task CleanDirectoryAsync(
            string directory,
            CleanupResult result,
            CancellationToken ct,
            double progressStart,
            double progressEnd)
        {
            var files = SafeEnumerateFiles(directory);
            int processed = 0;

            foreach (var filePath in files)
            {
                ct.ThrowIfCancellationRequested();
                processed++;
                result.FilesScanned++;

                try
                {
                    var fi = new FileInfo(filePath);
                    if (!fi.Exists) continue;

                    if (IsProtectedFile(fi))
                    {
                        result.FilesSkipped++;
                        continue;
                    }

                    if (IsFileLocked(filePath))
                    {
                        result.FilesSkipped++;
                        continue;
                    }

                    long size = fi.Length;
                    if (fi.IsReadOnly) fi.IsReadOnly = false;
                    fi.Delete();

                    result.FilesDeleted++;
                    result.BytesFreed += size;
                }
                catch (UnauthorizedAccessException) { result.FilesFailed++; }
                catch (IOException) { result.FilesFailed++; }
                catch (Exception ex) { result.FilesFailed++; result.Errors.Add($"{filePath}: {ex.Message}"); }

                // Log de actividad cada 500 archivos para dar feedback al usuario
                if (processed % 500 == 0)
                {
                    Log($"    ... procesados {processed} archivos");
                }

                // Reportar progreso estimado
                double pct = progressStart + Math.Min((processed / 1000.0), (progressEnd - progressStart) * 0.9);
                ReportProgress(Math.Min(pct, progressEnd));

                // Ceder el hilo de forma más eficiente
                if (processed % 200 == 0)
                    await Task.Yield();
            }

            // Intentar eliminar subdirectorios vacíos
            await Task.Run(() => CleanEmptySubDirectories(directory, result), ct);
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Devuelve las rutas de las carpetas temporales a procesar.
        /// </summary>
        private static List<string> GetTempDirectories()
        {
            var dirs = new List<string>();

            // %TEMP% del usuario actual
            string userTemp = Path.GetTempPath();
            if (Directory.Exists(userTemp))
                dirs.Add(userTemp);

            // C:\Windows\Temp
            string winTemp = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
            if (Directory.Exists(winTemp) && !dirs.Contains(winTemp, StringComparer.OrdinalIgnoreCase))
                dirs.Add(winTemp);

            return dirs;
        }

        /// <summary>
        /// Enumera archivos de forma segura, capturando errores de acceso.
        /// </summary>
        private static IEnumerable<string> SafeEnumerateFiles(string directory)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", 
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.System
                    });
            }
            catch (UnauthorizedAccessException) { return Enumerable.Empty<string>(); }
            catch (IOException) { return Enumerable.Empty<string>(); }

            return files;
        }

        /// <summary>
        /// Verifica si un archivo es crítico del sistema y no debe eliminarse.
        /// </summary>
        private static bool IsProtectedFile(FileInfo fi)
        {
            // 1. Extensiones protegidas
            if (ProtectedExtensions.Contains(fi.Extension))
                return true;

            // 2. Nombres protegidos
            if (ProtectedFileNames.Contains(fi.Name))
                return true;

            // 3. Archivos con atributo System
            if (fi.Attributes.HasFlag(FileAttributes.System))
                return true;

            // 4. Subdirectorios protegidos
            string? parentName = fi.Directory?.Name;
            if (parentName != null && ProtectedSubDirs.Contains(parentName))
                return true;

            // 5. Archivos creados hace menos de 1 hora (podrían estar en uso)
            if (fi.CreationTimeUtc > DateTime.UtcNow.AddHours(-1))
                return true;

            return false;
        }

        /// <summary>
        /// Verifica si un archivo está siendo usado por otro proceso.
        /// </summary>
        private static bool IsFileLocked(string filePath)
        {
            try
            {
                using var stream = File.Open(filePath, FileMode.Open,
                    FileAccess.ReadWrite, FileShare.None);
                return false; // No está bloqueado
            }
            catch (IOException)
            {
                return true; // Está en uso
            }
            catch (UnauthorizedAccessException)
            {
                return true; // Sin permisos = tratarlo como bloqueado
            }
        }

        /// <summary>
        /// Elimina subdirectorios vacíos después de la limpieza.
        /// </summary>
        private static void CleanEmptySubDirectories(string directory, CleanupResult result)
        {
            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(directory, "*",
                    new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true }))
                {
                    try
                    {
                        // Solo eliminar si el directorio está completamente vacío
                        if (!Directory.EnumerateFileSystemEntries(subDir).Any())
                        {
                            Directory.Delete(subDir, false);
                        }
                    }
                    catch { /* Ignorar directorios que no se pueden eliminar */ }
                }
            }
            catch { }
        }

        /// <summary>
        /// Vacía la papelera de reciclaje de todas las unidades.
        /// </summary>
        private void EmptyRecycleBin(CleanupResult result)
        {
            try
            {
                long sizeBefore = GetRecycleBinSize();
                int hr = SHEmptyRecycleBin(IntPtr.Zero, null,
                    SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

                if (hr == 0 || hr == -2147418113) // S_OK o "papelera ya vacía"
                {
                    if (sizeBefore > 0)
                    {
                        result.BytesFreed += sizeBefore;
                        Log($"    ✓ Papelera vaciada: {FormatBytesStatic(sizeBefore)} liberados");
                    }
                    else
                    {
                        Log("    ✓ La papelera ya estaba vacía");
                    }
                }
                else
                {
                    Log($"    ⚠ No se pudo vaciar la papelera (código: 0x{hr:X8})");
                }
            }
            catch (Exception ex)
            {
                Log($"    ✕ Error al vaciar papelera: {ex.Message}");
                result.Errors.Add($"Papelera: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtiene el tamaño total de la papelera de reciclaje.
        /// </summary>
        private static long GetRecycleBinSize()
        {
            try
            {
                var info = new SHQUERYRBINFO();
                info.cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO));
                int hr = SHQueryRecycleBin(null, ref info);
                return hr == 0 ? info.i64Size : 0;
            }
            catch
            {
                return 0;
            }
        }

        private void Log(string message) => OnLog?.Invoke(message);
        private void ReportProgress(double value) => OnProgress?.Invoke(value);

        private static string FormatBytesStatic(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }
    }
}
