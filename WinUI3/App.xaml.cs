using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;

namespace MugiSideBrowser.WinUI3
{
    public partial class App : Application
    {
        private MainWindow? _mainWindow;
        private static App? _instance;

        public App()
        {
            _instance = this;
            this.InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Activate();

            // Initialize and create the Tray Icon from resources
            _ = Resources["TrayIcon"] as H.NotifyIcon.TaskbarIcon;
        }

        public static void OnRedirectActivated()
        {
            if (_instance?._mainWindow != null)
            {
                var dispatcher = _instance._mainWindow.DispatcherQueue;
                dispatcher.TryEnqueue(() =>
                {
                    _instance._mainWindow.RestoreWindow();
                });
            }
        }

        private void TrayOpen_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow?.RestoreWindow();
        }

        private void TrayExit_Click(object sender, RoutedEventArgs e)
        {
            if (Resources["TrayIcon"] is H.NotifyIcon.TaskbarIcon trayIcon)
            {
                trayIcon.Dispose();
            }
            _mainWindow?.Close();
            Exit();
        }

        private void TrayIcon_TrayLeftMouseDown(object sender, EventArgs e)
        {
            _mainWindow?.RestoreWindow();
        }
    }
}
