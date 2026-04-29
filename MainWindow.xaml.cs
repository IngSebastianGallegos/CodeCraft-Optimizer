#nullable enable
using CodeCraftOptimizer.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;

namespace CodeCraftOptimizer
{
    /// <summary>
    /// Code-behind para MainWindow.
    /// Gestiona la ventana cromeless (arrastrar, minimizar, cerrar)
    /// y el auto-scroll del registro de actividad.
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            // Register BooleanToVisibility converter before InitializeComponent
            Resources.Add("BoolToVis", new BooleanToVisibilityConverter());

            InitializeComponent();

            // Auto-scroll logs to bottom when a new entry is added
            if (DataContext is MainViewModel vm)
            {
                vm.Logs.CollectionChanged += (s, e) =>
                {
                    if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                    {
                        if (LogList.Items.Count > 0)
                        {
                            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
                        }
                    }
                };
            }
        }

        /// <summary>
        /// Permite arrastrar la ventana desde la barra de título personalizada.
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                e.Handled = true;
            }
            catch { }
        }
    }
}
