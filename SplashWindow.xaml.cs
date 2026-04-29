using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace CodeCraftOptimizer
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            StartLoading();
        }

        private async void StartLoading()
        {
            // Animamos la barra de carga manualmente para control total
            for (int i = 0; i <= 100; i += 2)
            {
                ProgressBar.Width = (300.0 * i) / 100.0;
                await Task.Delay(30); // Duración total aprox: 1.5 - 2 segundos
            }

            // Esperar un momento extra para que se sienta fluido
            await Task.Delay(500);

            // Iniciar MainWindow
            MainWindow main = new MainWindow();
            main.Show();

            // Cerrar Splash con un pequeño desvanecimiento (opcional, aquí directo)
            this.Close();
        }
    }
}
