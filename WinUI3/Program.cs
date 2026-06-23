using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace MugiSideBrowser.WinUI3
{
    public static class Program
    {
        [STAThread]
        static async Task<int> Main(string[] args)
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

            bool isRedirect = await DecideRedirection();
            if (isRedirect)
            {
                return 0;
            }

            Microsoft.UI.Xaml.Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                System.Threading.SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });

            return 0;
        }

        private static async Task<bool> DecideRedirection()
        {
            var mainInstance = AppInstance.FindOrRegisterForKey("MugiSideBrowserKey");
            if (mainInstance.IsCurrent)
            {
                mainInstance.Activated += OnAppInstanceActivated;
                return false;
            }

            var activeArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            await mainInstance.RedirectActivationToAsync(activeArgs);
            return true;
        }

        private static void OnAppInstanceActivated(object? sender, AppActivationArguments args)
        {
            App.OnRedirectActivated();
        }
    }
}
