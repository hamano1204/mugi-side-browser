using Microsoft.UI.Xaml;

namespace MugiSideBrowser.WinUI3
{
    public partial class App : Application
    {
        private Window? _mainWindow;

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Activate();
        }
    }
}
