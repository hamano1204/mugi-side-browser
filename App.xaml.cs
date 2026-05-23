using System;
using System.Linq;
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

            // 保存されたテーマを適用
            string savedTheme = SettingsManager.Settings.Theme;
            ApplyTheme(savedTheme == "dark");

            // 言語リソースを適用
            ApplyLanguage();

            base.OnStartup(e);

            // StartupUri を削除したため、ここで MainWindow を生成して表示する
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        public static void ApplyTheme(bool isDark)
        {
            var dicts = System.Windows.Application.Current.Resources.MergedDictionaries;
            var existingTheme = dicts.FirstOrDefault(d => d.Source != null && 
                (d.Source.OriginalString.Contains("DarkTheme.xaml") || d.Source.OriginalString.Contains("LightTheme.xaml")));
            
            if (existingTheme != null)
            {
                dicts.Remove(existingTheme);
            }
            
            var newTheme = new ResourceDictionary
            {
                Source = new Uri(isDark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml", UriKind.Relative)
            };
            dicts.Add(newTheme);
        }

        public static void ApplyLanguage()
        {
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            bool isJapanese = culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase);
            
            var dicts = System.Windows.Application.Current.Resources.MergedDictionaries;
            var existingLang = dicts.FirstOrDefault(d => d.Source != null && 
                (d.Source.OriginalString.Contains("StringResources.xaml") || d.Source.OriginalString.Contains("StringResources.ja.xaml")));
            
            if (existingLang != null)
            {
                dicts.Remove(existingLang);
            }
            
            var newLang = new ResourceDictionary
            {
                Source = new Uri(isJapanese ? "Themes/StringResources.ja.xaml" : "Themes/StringResources.xaml", UriKind.Relative)
            };
            dicts.Add(newLang);
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
            string errorMsgFormat = System.Windows.Application.Current.TryFindResource("Msg_FatalError") as string 
                ?? "致命的なエラーが発生しました:\n\n{0}\n\n{1}";
            string errorTitle = System.Windows.Application.Current.TryFindResource("Msg_ErrorTitle") as string 
                ?? "エラー";
            System.Windows.MessageBox.Show(string.Format(errorMsgFormat, e.Exception.Message, e.Exception.StackTrace), errorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            System.Windows.Application.Current.Shutdown();
        }
    }
}
