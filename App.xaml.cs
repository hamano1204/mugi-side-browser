using System;
using System.Threading;
using System.Windows;

namespace MugiSideBrowser
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _mutex;
        private static readonly uint ShowWindowMessage = NativeMethods.RegisterWindowMessage("MugiSideBrowser_ShowWindowMessage");

        protected override void OnStartup(StartupEventArgs e)
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            // Mutex による多重起動防止
            bool createdNew;
            _mutex = new Mutex(true, "Global\\MugiSideBrowser_SingleInstanceMutex", out createdNew);

            if (!createdNew)
            {
                // すでに起動しているインスタンスへ表示シグナルを送信（ブロードキャスト）
                NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
                
                // ミューテックスを解放して即終了
                _mutex.Dispose();
                _mutex = null;
                System.Windows.Application.Current.Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_mutex != null)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (ObjectDisposedException) { }
                _mutex.Dispose();
            }
            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            System.Windows.MessageBox.Show($"致命的なエラーが発生しました:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            System.Windows.Application.Current.Shutdown();
        }
    }
}
