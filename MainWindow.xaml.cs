using System;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace MugiSideBrowser
{
    public partial class MainWindow : Window
    {
        private AppBarHelper _appBarHelper;

        public MainWindow()
        {
            InitializeComponent();
            _appBarHelper = new AppBarHelper(this);
            
            this.SourceInitialized += MainWindow_SourceInitialized;
            this.Closing += MainWindow_Closing;
            
            InitializeWebView();
        }

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                _appBarHelper.Register();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"AppBar の登録に失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _appBarHelper.Unregister();
        }

        private async void InitializeWebView()
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
                webView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
                webView.Source = new Uri("https://www.google.com");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2 の初期化に失敗しました。WebView2 ランタイムがインストールされているか確認してください。\n\n詳細: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        {
            UrlTextBox.Text = webView.Source.ToString();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (webView.CanGoBack) webView.GoBack();
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            if (webView.CanGoForward) webView.GoForward();
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            webView.Reload();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NavigateToUrl();
            }
        }

        private void NavigateToUrl()
        {
            string url = UrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url)) return;

            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                if (url.Contains(".") && !url.Contains(" "))
                {
                    url = "https://" + url;
                }
                else
                {
                    url = "https://www.google.com/search?q=" + Uri.EscapeDataString(url);
                }
            }

            webView.Source = new Uri(url);
        }
    }
}