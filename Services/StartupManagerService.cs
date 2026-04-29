using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;

namespace CodeCraftOptimizer.Services
{
    public class StartupItem : INotifyPropertyChanged
    {
        private bool _isEnabled;

        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string Publisher { get; set; } = "Desconocido";
        public string Source { get; set; } = "Registro"; // Registro o Carpeta
        
        public bool IsEnabled 
        { 
            get => _isEnabled; 
            set 
            { 
                if (_isEnabled != value)
                {
                    _isEnabled = value; 
                    OnPropertyChanged(); 
                }
            } 
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public static class StartupManagerService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string DisabledRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run_Disabled";

        public static List<StartupItem> GetStartupItems()
        {
            var list = new List<StartupItem>();

            // 1. REGISTRO - CURRENT USER
            ScanRegistry(list, Registry.CurrentUser, RunKeyPath, true);
            ScanRegistry(list, Registry.CurrentUser, DisabledRunKeyPath, false);

            // 2. REGISTRO - LOCAL MACHINE
            ScanRegistry(list, Registry.LocalMachine, RunKeyPath, true);

            // 3. CARPETAS DE INICIO (Startup Folders)
            ScanStartupFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Usuario");
            ScanStartupFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Sistema");

            return list;
        }

        private static void ScanRegistry(List<StartupItem> list, RegistryKey root, string path, bool enabled)
        {
            using (var key = root.OpenSubKey(path))
            {
                if (key != null)
                {
                    foreach (var name in key.GetValueNames())
                    {
                        if (list.Exists(x => x.Name == name)) continue;
                        
                        string cmd = key.GetValue(name)?.ToString() ?? "";
                        list.Add(new StartupItem
                        {
                            Name = name,
                            Command = cmd,
                            IsEnabled = enabled,
                            Publisher = GetPublisher(cmd),
                            Source = "Registro"
                        });
                    }
                }
            }
        }

        private static void ScanStartupFolder(List<StartupItem> list, string folderPath, string sourceLabel)
        {
            if (!Directory.Exists(folderPath)) return;

            foreach (var file in Directory.GetFiles(folderPath))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (list.Exists(x => x.Name == name)) continue;

                list.Add(new StartupItem
                {
                    Name = name,
                    Command = file,
                    IsEnabled = true, // Los archivos en estas carpetas están activos por defecto
                    Publisher = GetPublisher(file),
                    Source = $"Carpeta ({sourceLabel})"
                });
            }
        }

        private static string GetPublisher(string command)
        {
            try
            {
                // Limpiar la ruta (quitar comillas y argumentos)
                string path = command.Trim().Replace("\"", "");
                if (path.Contains(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    path = path.Substring(0, path.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) + 4);
                }

                if (File.Exists(path))
                {
                    var info = FileVersionInfo.GetVersionInfo(path);
                    return info.CompanyName ?? "Desconocido";
                }
            }
            catch { }
            return "Desconocido";
        }

        public static void ToggleItem(StartupItem item)
        {
            try
            {
                if (item.Source.StartsWith("Carpeta"))
                {
                    // Para carpetas de inicio, "deshabilitar" significa mover el archivo a una carpeta temporal
                    // Por ahora, solo manejamos el Registro que es el 90% de los casos complejos.
                    return; 
                }

                if (item.IsEnabled)
                {
                    using var destKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
                    destKey.SetValue(item.Name, item.Command);
                    using var srcKey = Registry.CurrentUser.OpenSubKey(DisabledRunKeyPath, true);
                    srcKey?.DeleteValue(item.Name, false);
                }
                else
                {
                    using var destKey = Registry.CurrentUser.CreateSubKey(DisabledRunKeyPath);
                    destKey.SetValue(item.Name, item.Command);
                    using var srcKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
                    srcKey?.DeleteValue(item.Name, false);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error al modificar el registro: {ex.Message}");
            }
        }
    }
}
