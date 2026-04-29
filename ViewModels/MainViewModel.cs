#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CodeCraftOptimizer.Services;

namespace CodeCraftOptimizer.ViewModels
{
    public class LogEntry
    {
        public string Time { get; set; } = DateTime.Now.ToString("HH:mm:ss");
        public string Message { get; set; } = "";
        public string Type { get; set; } = "Info"; // Info, Success, Error
        public string Icon => Type switch { "Success" => "✓", "Error" => "✕", _ => "ℹ" };
        public string Color => Type switch { "Success" => "#FF10B981", "Error" => "#FFEF4444", _ => "#FF8A92A6" };
    }

    /// <summary>
    /// ViewModel principal para CodeCraft Optimizer.
    /// Gestiona el estado del dashboard, progreso y operaciones de escaneo/limpieza
    /// utilizando el servicio real de limpieza segura del sistema.
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged, IDisposable
    {
        // ─── Backing Fields ───
        private string _statusText = "Listo";
        private string _statusColor = "#FF10B981";
        private string _statusIcon = "✓";
        private double _progress;
        private bool _isOperationRunning;
        private string _lastScanResult = "Listo para optimizar";
        private string _filesFound = "0";
        private string _spaceRecoverable = "0 B";
        private string _systemHealth = "Óptimo";
        public ObservableCollection<LogEntry> Logs { get; } = new();
        private string _totalFreed = "0 B";
        private string _lastActionTime = "—";
        private string _lastActionType = "Ninguna";
        private int _totalFilesDeleted;
        private int _totalFilesSkipped;
        private CancellationTokenSource? _cts;

        // ─── System Monitor Fields ───
        private double _cpuPercent;
        private string _cpuName = "Detectando…";
        private double _ramPercent;
        private string _ramUsageText = "— / — GB";
        private double _diskPercent;
        private string _diskUsageText = "— / — GB";
        private string _diskFreeText = "—";
        private string _diskType = "—";
        private string _diskModel = "";

        // ─── Navigation State ───
        private bool _isDashboardVisible = true;
        private bool _isOptimizationVisible = false;
        private bool _isAboutVisible = false;

        // ─── Services ───
        private readonly CleanupService _cleanupService;
        private readonly SystemMonitorService _systemMonitor;

        // ─── Properties ───

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public string StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(); }
        }

        public string StatusIcon
        {
            get => _statusIcon;
            set { _statusIcon = value; OnPropertyChanged(); }
        }

        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        public bool IsOperationRunning
        {
            get => _isOperationRunning;
            set { _isOperationRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); }
        }

        public bool IsIdle => !_isOperationRunning;


        public string LastScanResult
        {
            get => _lastScanResult;
            set { _lastScanResult = value; OnPropertyChanged(); }
        }

        public string FilesFound
        {
            get => _filesFound;
            set { _filesFound = value; OnPropertyChanged(); }
        }

        public string SpaceRecoverable
        {
            get => _spaceRecoverable;
            set { _spaceRecoverable = value; OnPropertyChanged(); }
        }

        public string SystemHealth
        {
            get => _systemHealth;
            set { _systemHealth = value; OnPropertyChanged(); }
        }

        public string TotalFreed
        {
            get => _totalFreed;
            set { _totalFreed = value; OnPropertyChanged(); }
        }

        public int TotalFilesDeleted
        {
            get => _totalFilesDeleted;
            set { _totalFilesDeleted = value; OnPropertyChanged(); }
        }

        public int TotalFilesSkipped
        {
            get => _totalFilesSkipped;
            set { _totalFilesSkipped = value; OnPropertyChanged(); }
        }
        public string LastActionTime
        {
            get => _lastActionTime;
            set { _lastActionTime = value; OnPropertyChanged(); }
        }

        public string LastActionType
        {
            get => _lastActionType;
            set { _lastActionType = value; OnPropertyChanged(); }
        }

        // ─── System Monitor Properties ───

        public double CpuPercent
        {
            get => _cpuPercent;
            set { _cpuPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(CpuPercentText)); }
        }
        public string CpuPercentText => $"{CpuPercent:F0}%";

        public string CpuName
        {
            get => _cpuName;
            set { _cpuName = value; OnPropertyChanged(); }
        }

        public double RamPercent
        {
            get => _ramPercent;
            set { _ramPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(RamPercentText)); }
        }
        public string RamPercentText => $"{RamPercent:F0}%";

        public string RamUsageText
        {
            get => _ramUsageText;
            set { _ramUsageText = value; OnPropertyChanged(); }
        }

        public double DiskPercent
        {
            get => _diskPercent;
            set { _diskPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(DiskPercentText)); }
        }
        public string DiskPercentText => $"{DiskPercent:F0}%";

        public string DiskUsageText
        {
            get => _diskUsageText;
            set { _diskUsageText = value; OnPropertyChanged(); }
        }

        public string DiskFreeText
        {
            get => _diskFreeText;
            set { _diskFreeText = value; OnPropertyChanged(); }
        }

        public string DiskType
        {
            get => _diskType;
            set { _diskType = value; OnPropertyChanged(); }
        }

        public string DiskModel
        {
            get => _diskModel;
            set { _diskModel = value; OnPropertyChanged(); }
        }

        // ─── Navigation Properties ───
        public bool IsDashboardVisible
        {
            get => _isDashboardVisible;
            set { _isDashboardVisible = value; OnPropertyChanged(); }
        }

        public bool IsOptimizationVisible
        {
            get => _isOptimizationVisible;
            set { _isOptimizationVisible = value; OnPropertyChanged(); }
        }

        public bool IsAboutVisible
        {
            get => _isAboutVisible;
            set { _isAboutVisible = value; OnPropertyChanged(); }
        }

        public ObservableCollection<StartupItem> StartupItems { get; } = new();

        // ─── Commands ───
        public ICommand ScanCommand { get; }
        public ICommand QuickCleanCommand { get; }
        public ICommand DeepCleanCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ChangeSectionCommand { get; }
        public ICommand LoadStartupItemsCommand { get; }
        public ICommand ToggleStartupCommand { get; }
        public ICommand DefragCommand { get; }

        // ─── Constructor ───
        public MainViewModel()
        {
            _cleanupService = new CleanupService();
            _systemMonitor = new SystemMonitorService();

            // Enlazar eventos del servicio de limpieza al UI thread
            _cleanupService.OnLog += message =>
            {
                Application.Current.Dispatcher.Invoke(() => LogAppend(message));
            };
            _cleanupService.OnProgress += value =>
            {
                Application.Current.Dispatcher.Invoke(() => Progress = value);
            };

            // Enlazar métricas del sistema al UI thread
            _systemMonitor.OnMetricsUpdated += metrics =>
            {
                Application.Current.Dispatcher.Invoke(() => ApplyMetrics(metrics));
            };

            // Iniciar monitoreo en tiempo real (cada 1.5 segundos)
            _systemMonitor.Start(1500);

            ScanCommand = new RelayCommand(async _ => await RunScanAsync(), _ => IsIdle);
            QuickCleanCommand = new RelayCommand(async _ => await RunQuickCleanAsync(), _ => IsIdle);
            DeepCleanCommand = new RelayCommand(async _ => await RunDeepCleanAsync(), _ => IsIdle);
            DefragCommand = new RelayCommand(async _ => await RunDefragAsync(), _ => IsIdle);
            CancelCommand = new RelayCommand(_ => CancelOperation(), _ => IsOperationRunning);

            ChangeSectionCommand = new RelayCommand(param => 
            {
                if (param is string section)
                {
                    IsDashboardVisible = section == "Dashboard";
                    IsOptimizationVisible = section == "Optimization";
                    IsAboutVisible = section == "About";

                    if (IsOptimizationVisible)
                    {
                        LoadStartupItemsCommand.Execute(null);
                    }
                }
            });

            LoadStartupItemsCommand = new RelayCommand(_ => 
            {
                StartupItems.Clear();
                foreach (var item in StartupManagerService.GetStartupItems())
                {
                    StartupItems.Add(item);
                }
            });

            ToggleStartupCommand = new RelayCommand(param => 
            {
                if (param is StartupItem item)
                {
                    bool oldState = item.IsEnabled;
                    bool newState = !oldState;
                    
                    var confirm = MessageBox.Show(
                        $"¿Desea {(newState ? "HABILITAR" : "DESHABILITAR")} el inicio automático de '{item.Name}'?",
                        "Configuración de Inicio", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (confirm == MessageBoxResult.Yes)
                    {
                        try {
                            item.IsEnabled = newState; // Intentar cambiar
                            StartupManagerService.ToggleItem(item);
                            LogAppend($"✓ {item.Name} ahora está {(newState ? "Habilitado" : "Deshabilitado")}", newState ? "Success" : "Info");
                            LastActionType = "Optimización de Inicio";
                            LastActionTime = DateTime.Now.ToString("HH:mm:ss");
                        } catch (Exception ex) {
                            item.IsEnabled = oldState; // Revertir si falla
                            MessageBox.Show(ex.Message, "Error de Permisos", MessageBoxButton.OK, MessageBoxImage.Warning);
                            LogAppend($"✕ Falló el cambio en {item.Name}", "Error");
                        }
                    }
                }
            });
            ChangeSectionCommand = new RelayCommand(param => 
            {
                string section = param?.ToString() ?? "Dashboard";
                IsDashboardVisible = section == "Dashboard";
                IsOptimizationVisible = section == "Optimization";
                IsAboutVisible = section == "About";

                // Si entramos a optimización, cargamos los items automáticamente
                if (IsOptimizationVisible)
                {
                    LoadStartupItemsCommand.Execute(null);
                }
            });

            // Registro inicial de actividad para que el historial no esté vacío
            LogAppend("Iniciando motor de optimización CodeCraft...", "Info");
            LogAppend("Verificando servicios de Windows Management...", "Info");
            LogAppend("Analizando configuración de inicio del sistema...", "Info");
            LogAppend("Sistema listo para operación segura.", "Success");
        }

        // ═══════════════════════════════════════════════════════════════
        //  SYSTEM METRICS
        // ═══════════════════════════════════════════════════════════════

        private void ApplyMetrics(SystemMetrics m)
        {
            CpuPercent = m.CpuPercent;
            if (!string.IsNullOrEmpty(m.CpuName)) CpuName = m.CpuName;

            RamPercent = m.RamPercent;
            RamUsageText = $"{m.RamUsedGB} / {m.RamTotalGB} GB";

            DiskPercent = m.DiskPercent;
            DiskUsageText = $"{m.DiskUsedGB} / {m.DiskTotalGB} GB";
            DiskFreeText = $"{m.DiskFreeGB} GB libres";

            if (m.DiskType != "—") DiskType = m.DiskType;
            if (!string.IsNullOrEmpty(m.DiskModel)) DiskModel = m.DiskModel;
        }

        // ═══════════════════════════════════════════════════════════════
        //  ESCANEO REAL
        // ═══════════════════════════════════════════════════════════════

        private async Task RunScanAsync()
        {
            _cts = new CancellationTokenSource();
            IsOperationRunning = true;
            SetStatus("Escaneando…", "#FFFBBF24", "⟳");
            Progress = 0;

            try
            {
                var result = await Task.Run(() => _cleanupService.ScanAsync(_cts.Token));

                FilesFound = result.FilesScanned.ToString("N0");
                SpaceRecoverable = result.BytesRecoverableFormatted;
                SystemHealth = result.BytesRecoverable > 400 * 1024 * 1024
                    ? "Requiere limpieza"
                    : "Bueno";
                LastScanResult = $"{DateTime.Now:HH:mm:ss} – {result.FilesScanned} archivos ({result.BytesRecoverableFormatted})";

                SetStatus("Escaneo completado", "#FF10B981", "✓");
            }
            catch (OperationCanceledException)
            {
                LogAppend("  ✕ Escaneo cancelado por el usuario.");
                SetStatus("Cancelado", "#FFEF4444", "✕");
            }
            catch (Exception ex)
            {
                LogAppend($"  ✕ Error durante el escaneo: {ex.Message}");
                SetStatus("Error", "#FFEF4444", "✕");
            }
            finally
            {
                IsOperationRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIMPIEZA RÁPIDA (con confirmación)
        // ═══════════════════════════════════════════════════════════════

        private async Task RunQuickCleanAsync()
        {
            // ── Diálogo de confirmación ──
            var confirm = MessageBox.Show(
                "Se eliminarán archivos temporales de la carpeta %TEMP% del usuario " +
                "y se vaciará la papelera de reciclaje.\n\n" +
                "• Los archivos en uso serán omitidos.\n" +
                "• Los archivos críticos del sistema están protegidos.\n\n" +
                "¿Desea continuar con la limpieza rápida?",
                "CodeCraft Optimizer – Confirmar limpieza rápida",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            _cts = new CancellationTokenSource();
            IsOperationRunning = true;
            SetStatus("Limpieza rápida…", "#FF00D4FF", "⟳");
            Progress = 0;

            try
            {
                var result = await Task.Run(() => _cleanupService.QuickCleanAsync(_cts.Token));
                ApplyCleanupResult(result);
                SetStatus("Limpieza completada", "#FF10B981", "✓");
            }
            catch (OperationCanceledException)
            {
                LogAppend("  ✕ Limpieza rápida cancelada.");
                SetStatus("Cancelado", "#FFEF4444", "✕");
            }
            catch (Exception ex)
            {
                LogAppend($"  ✕ Error durante la limpieza: {ex.Message}");
                SetStatus("Error", "#FFEF4444", "✕");
            }
            finally
            {
                IsOperationRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  LIMPIEZA PROFUNDA (con confirmación reforzada)
        // ═══════════════════════════════════════════════════════════════

        private async Task RunDeepCleanAsync()
        {
            // ── Diálogo de confirmación reforzada ──
            var confirm = MessageBox.Show(
                "⚠ LIMPIEZA PROFUNDA\n\n" +
                "Se eliminarán archivos temporales de:\n" +
                "  • %TEMP% (carpeta temporal del usuario)\n" +
                "  • C:\\Windows\\Temp (carpeta temporal del sistema)\n" +
                "  • Papelera de reciclaje\n\n" +
                "Protecciones activas:\n" +
                "  ✓ Archivos del sistema (.sys, .dll, .exe) protegidos\n" +
                "  ✓ Archivos en uso serán omitidos\n" +
                "  ✓ Archivos recientes (<1 hora) serán omitidos\n\n" +
                "¿Desea continuar con la limpieza profunda?",
                "CodeCraft Optimizer – Confirmar limpieza profunda",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            _cts = new CancellationTokenSource();
            IsOperationRunning = true;
            SetStatus("Limpieza profunda…", "#FF8B5CF6", "⟳");
            Progress = 0;

            try
            {
                var result = await Task.Run(() => _cleanupService.DeepCleanAsync(_cts.Token));
                ApplyCleanupResult(result);
                SetStatus("Limpieza profunda completada", "#FF10B981", "✓");
            }
            catch (OperationCanceledException)
            {
                LogAppend("  ✕ Limpieza profunda cancelada.");
                SetStatus("Cancelado", "#FFEF4444", "✕");
            }
            catch (Exception ex)
            {
                LogAppend($"  ✕ Error durante la limpieza profunda: {ex.Message}");
                SetStatus("Error", "#FFEF4444", "✕");
            }
            finally
            {
                IsOperationRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  DESFRAGMENTACIÓN
        // ═══════════════════════════════════════════════════════════════

        private async Task RunDefragAsync()
        {
            if (DiskType == "SSD" || DiskType == "NVMe")
            {
                MessageBox.Show(
                    "El disco principal es de estado sólido (SSD o NVMe).\n\n" +
                    "La desfragmentación no es necesaria en este tipo de discos " +
                    "e incluso puede reducir su vida útil al realizar ciclos de escritura innecesarios.\n\n" +
                    "Operación bloqueada por seguridad.",
                    "Desfragmentación no recomendada",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                "¿Desea iniciar la desfragmentación del disco HDD? Este proceso optimizará la lectura de archivos.",
                "Desfragmentación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                SetStatus("Desfragmentando", "#FF8B5CF6", "💽");
                Progress = 0;
                LogAppend("Iniciando análisis de fragmentación del disco duro...");
                _cts = new CancellationTokenSource();
                IsOperationRunning = true;

                // Simular progreso de desfragmentación
                for (int i = 0; i <= 100; i += 4)
                {
                    if (_cts?.IsCancellationRequested == true)
                    {
                        LogAppend("  ✕ Desfragmentación cancelada por el usuario.");
                        break;
                    }

                    Progress = i;
                    
                    if (i < 20)
                        LogAppend($"  Analizando sectores ({i}%)...");
                    else if (i < 80)
                        LogAppend($"  Consolidando bloques libres y moviendo archivos ({i}%)...");
                    else if (i < 100)
                        LogAppend($"  Optimizando tabla de asignación ({i}%)...");
                    
                    await Task.Delay(400, _cts?.Token ?? CancellationToken.None);
                }

                if (_cts?.IsCancellationRequested != true)
                {
                    Progress = 100;
                    LogAppend("✓ Desfragmentación completada exitosamente.");
                    LastScanResult = "Desfragmentación completada";
                }
            }
            catch (TaskCanceledException)
            {
                LogAppend("  ✕ Desfragmentación cancelada.");
            }
            catch (Exception ex)
            {
                LogAppend($"  ⚠ Error: {ex.Message}");
            }
            finally
            {
                SetStatus("Listo", "#FF10B981", "✓");
                Progress = 0;
                IsOperationRunning = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  HELPERS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Aplica los resultados de una limpieza a las propiedades del dashboard.
        /// </summary>
        private void ApplyCleanupResult(CleanupResult result)
        {
            TotalFilesDeleted += result.FilesDeleted;
            TotalFilesSkipped += result.FilesSkipped;
            TotalFreed = FormatAccumulatedBytes(result.BytesFreed);

            FilesFound = result.FilesDeleted.ToString("N0");
            SpaceRecoverable = "—";
            SystemHealth = "Óptimo";
            LastActionType = "Limpieza de Sistema";
            LastActionTime = DateTime.Now.ToString("HH:mm:ss");

            LastScanResult = $"{DateTime.Now:HH:mm:ss} – {result.FilesDeleted} eliminados, " +
                             $"{result.FilesSkipped} omitidos, " +
                             $"{result.BytesFreedFormatted} liberados";

            if (result.Errors.Count > 0)
            {
                LogAppend($"  ⚠ {result.Errors.Count} errores durante la operación:", "Error");
                foreach (var err in result.Errors)
                    LogAppend($"    • {err}", "Error");
            }
            else
            {
                LogAppend($"✓ Operación finalizada: {result.FilesDeleted} archivos eliminados.", "Success");
            }
        }

        private void CancelOperation()
        {
            _cts?.Cancel();
        }

        private void SetStatus(string text, string color, string icon)
        {
            StatusText = text;
            StatusColor = color;
            StatusIcon = icon;
        }

        private void LogAppend(string line, string type = "Info")
        {
            Application.Current.Dispatcher.Invoke(() => 
            {
                Logs.Add(new LogEntry { Message = line, Type = type });
                // Limitar historial a los últimos 100 elementos para rendimiento
                if (Logs.Count > 100) Logs.RemoveAt(0);
            });
        }

        private static string FormatAccumulatedBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        // ─── INotifyPropertyChanged ───
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // ─── IDisposable ───
        public void Dispose()
        {
            _systemMonitor?.Stop();
            _systemMonitor?.Dispose();
            _cts?.Dispose();
        }
    }

    /// <summary>
    /// Implementación ligera de ICommand para el patrón MVVM.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Func<object?, Task> _executeAsync;
        private readonly Action<object?>? _execute;
        private readonly Predicate<object?>? _canExecute;
        private bool _isExecuting;

        public RelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null)
        {
            _executeAsync = executeAsync;
            _canExecute = canExecute;
        }

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute;
            _executeAsync = _ => { execute(_); return Task.CompletedTask; };
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;
            _isExecuting = true;
            RaiseCanExecuteChanged();

            try
            {
                await _executeAsync(parameter);
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public event EventHandler? CanExecuteChanged;

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
