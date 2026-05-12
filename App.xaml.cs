using System.Windows;

namespace MugiSideBrowser
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            base.OnStartup(e);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            System.Windows.MessageBox.Show($"致命的なエラーが発生しました:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            System.Windows.Application.Current.Shutdown();
        }
    }
}

