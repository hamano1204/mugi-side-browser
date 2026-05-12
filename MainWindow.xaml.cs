using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace MugiSideBrowser
{
    public partial class MainWindow : Window
    {
        private AppBarHelper _appBarHelper;
        private ObservableCollection<BookmarkItem> _bookmarks = new();
        private System.Windows.Point _dragStartPoint;
        private const string BookmarkDataFormat = "MugiSideBrowser.BookmarkItem";
        private Microsoft.Web.WebView2.Wpf.WebView2 _activeWebView;
        private bool _isBottomInitialized = false;
        private bool _isMobileMode = false;
        private string? _defaultUserAgent = null;
        private const string MobileUserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 15_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/15.0 Mobile/15E148 Safari/604.1";
        private bool _isFakeMinimized = false;




        public MainWindow()
        {
            InitializeComponent();
            _appBarHelper = new AppBarHelper(this);
            _activeWebView = webView;
            
            this.SourceInitialized += MainWindow_SourceInitialized;
            this.Closing += MainWindow_Closing;
            this.StateChanged += MainWindow_StateChanged;
            this.Activated += MainWindow_Activated;
            
            InitializeWebView();

            BookmarkList.ItemsSource = _bookmarks;
            LoadBookmarks();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            // 本当の最小化が呼ばれたら、擬似最小化に切り替える
            if (this.WindowState == WindowState.Minimized)
            {
                FakeMinimize();
            }
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            // 擬似最小化中にタスクバーからクリックされたら復帰
            if (_isFakeMinimized)
            {
                RestoreFromFakeMinimize();
            }
        }

        private void FakeMinimize()
        {
            if (_isFakeMinimized) return;
            
            _isFakeMinimized = true;
            _appBarHelper.Unregister();

            // 状態をNormalに戻してから画面外へ飛ばす
            this.WindowState = WindowState.Normal;
            this.Left = -30000;

            // 重要：自分自身のアクティブ状態を解除する。
            // これをしないと、次にタスクバーをクリックした時に Activated イベントが発生しない。
            IntPtr taskbarHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(taskbarHwnd);
            }
        }

        private void RestoreFromFakeMinimize()
        {
            if (!_isFakeMinimized) return;

            _isFakeMinimized = false;
            
            // Register内でSetPositionが呼ばれ、正しい位置（右端）に戻る
            _appBarHelper.Register();
        }


        private void LoadBookmarks()
        {
            var list = BookmarkManager.Load();
            _bookmarks.Clear();
            foreach (var item in list) _bookmarks.Add(item);
        }

        private void BookmarkScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                if (e.Delta > 0) scrollViewer.LineLeft();
                else scrollViewer.LineRight();
                e.Handled = true;
            }
        }

        private void Tools_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                RefreshMonitorMenu();
                element.ContextMenu.IsOpen = true;
            }
        }

        private void Side_Click(object sender, RoutedEventArgs e)
        {
            if (sender == LeftDockMenuItem)
            {
                _appBarHelper.Edge = NativeMethods.AppBarEdges.Left;
                LeftDockMenuItem.IsChecked = true;
                RightDockMenuItem.IsChecked = false;
            }
            else
            {
                _appBarHelper.Edge = NativeMethods.AppBarEdges.Right;
                LeftDockMenuItem.IsChecked = false;
                RightDockMenuItem.IsChecked = true;
            }
            _appBarHelper.SetPosition();
        }

        private void RefreshMonitorMenu()
        {
            MonitorSelectMenuItem.Items.Clear();
            
            // 全モニターを列挙してメニューに追加
            int index = 1;
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var mi = new System.Windows.Controls.MenuItem
                {
                    Header = $"モニター {index} {(screen.Primary ? "(メイン)" : "")}",
                    Tag = screen,
                    IsChecked = IsWindowOnScreen(screen)
                };
                mi.Click += Monitor_Click;
                MonitorSelectMenuItem.Items.Add(mi);
                index++;
            }
        }

        private bool IsWindowOnScreen(System.Windows.Forms.Screen screen)
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hMonitor = NativeMethods.MonitorFromWindow(helper.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
            
            var mi = new NativeMethods.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                // 物理座標で比較
                return mi.rcMonitor.Left == screen.Bounds.Left && mi.rcMonitor.Top == screen.Bounds.Top;
            }
            return false;
        }

        private void Monitor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem mi && mi.Tag is System.Windows.Forms.Screen screen)
            {
                // 一旦解除
                _appBarHelper.Unregister();

                // ウィンドウを対象モニターの作業領域内に移動（AppBar予約前の一時的な配置）
                double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                this.Left = screen.WorkingArea.Left / dpi;
                this.Top = screen.WorkingArea.Top / dpi;

                // 再登録（新しいモニターの座標でSetPositionが走る）
                _appBarHelper.Register();
            }
        }


        private void UserAgent_Click(object sender, RoutedEventArgs e)
        {
            _isMobileMode = !_isMobileMode;
            UpdateUserAgent();
        }

        private void UpdateUserAgent()
        {
            if (webView.CoreWebView2 == null) return;

            // 初回時にデフォルトのUserAgentを保存
            if (_defaultUserAgent == null)
            {
                _defaultUserAgent = webView.CoreWebView2.Settings.UserAgent;
            }

            string targetUA = _isMobileMode ? MobileUserAgent : _defaultUserAgent;

            // メインのWebViewに適用
            webView.CoreWebView2.Settings.UserAgent = targetUA;
            
            // 下部のWebView（初期化済みなら）にも適用
            if (_isBottomInitialized && webViewBottom.CoreWebView2 != null)
            {
                webViewBottom.CoreWebView2.Settings.UserAgent = targetUA;
            }

            // メニュー項目のテキストとアイコンを更新
            UserAgentMenuIcon.Text = _isMobileMode ? "" : "";
            UserAgentMenuItem.Header = _isMobileMode ? "デスクトップ表示に切替" : "モバイル表示に切替";

            // 現在のページをリロードして反映
            _activeWebView.Reload();
        }


        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                _appBarHelper.Register();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"AppBar の登録に失敗しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
                System.Windows.MessageBox.Show($"WebView2 の初期化に失敗しました。WebView2 ランタイムがインストールされているか確認してください。\n\n詳細: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        {
            if (sender is CoreWebView2 senderEvt && _activeWebView.CoreWebView2 == senderEvt)
            {
                UrlTextBox.Text = _activeWebView.Source.ToString();
            }
        }

        private async void InitializeBottomWebView()
        {
            if (_isBottomInitialized) return;
            try
            {
                await webViewBottom.EnsureCoreWebView2Async();
                webViewBottom.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
                
                // 現在のUserAgent設定を適用
                if (_defaultUserAgent != null)
                {
                    webViewBottom.CoreWebView2.Settings.UserAgent = _isMobileMode ? MobileUserAgent : _defaultUserAgent;
                }

                webViewBottom.Source = new Uri("https://www.google.com");
                _isBottomInitialized = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"下部 WebView2 の初期化に失敗しました:\n{ex.Message}", "エラー");
            }
        }

        private void WebView_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is Microsoft.Web.WebView2.Wpf.WebView2 senderWebView)
            {
                _activeWebView = senderWebView;
                if (_activeWebView.Source != null)
                {
                    UrlTextBox.Text = _activeWebView.Source.ToString();
                }
            }
        }

        private void SplitView_Click(object sender, RoutedEventArgs e)
        {
            bool isSplit = BottomRow.Height.Value > 0;

            if (!isSplit)
            {
                // 分割表示にする (50:50)
                TopRow.Height = new GridLength(1, GridUnitType.Star);
                BottomRow.Height = new GridLength(1, GridUnitType.Star);
                VerticalSplitter.Visibility = Visibility.Visible;
                webViewBottom.Visibility = Visibility.Visible;
                InitializeBottomWebView();
            }
            else
            {
                // 単一表示に戻す
                TopRow.Height = new GridLength(1, GridUnitType.Star);
                BottomRow.Height = new GridLength(0);
                VerticalSplitter.Visibility = Visibility.Collapsed;
                webViewBottom.Visibility = Visibility.Collapsed;
                _activeWebView = webView;
                UrlTextBox.Text = _activeWebView.Source?.ToString() ?? "";
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_activeWebView.CanGoBack) _activeWebView.GoBack();
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            if (_activeWebView.CanGoForward) _activeWebView.GoForward();
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            _activeWebView.Reload();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            FakeMinimize();
        }

        private void Star_Click(object sender, RoutedEventArgs e)
        {
            if (_activeWebView.CoreWebView2 == null) return;

            string url = _activeWebView.Source.ToString();
            string title = _activeWebView.CoreWebView2.DocumentTitle;

            if (string.IsNullOrEmpty(title)) title = url;

            // すでに登録されているか確認
            if (_bookmarks.Any(b => b.Url == url))
            {
                return;
            }

            var newItem = new BookmarkItem { Title = title, Url = url };

            // WebView2からアイコンURLの取得を試みる
            try
            {
                // CoreWebView2.FaviconUri は最新のSDKで利用可能
                string faviconUri = _activeWebView.CoreWebView2.FaviconUri;
                if (!string.IsNullOrEmpty(faviconUri))
                {
                    newItem.FaviconUrl = faviconUri;
                }
                else
                {
                    // フォールバックとしてDuckDuckGo経由のアイコンを使用
                    var uri = new Uri(url);
                    newItem.FaviconUrl = $"https://icons.duckduckgo.com/ip3/{uri.Host}.ico";
                }
            }
            catch
            {
                // SDKが古い場合などはフォールバック
            }

            _bookmarks.Add(newItem);
            BookmarkManager.Save(_bookmarks.ToList());
        }

        private void Bookmark_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is BookmarkItem item)
            {
                _activeWebView.Source = new Uri(item.Url);
            }
        }

        private void Bookmark_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void Bookmark_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                System.Windows.Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is FrameworkElement element && element.DataContext is BookmarkItem item)
                    {
                        System.Windows.DataObject dragData = new System.Windows.DataObject(BookmarkDataFormat, item);
                        DragDrop.DoDragDrop(element, dragData, System.Windows.DragDropEffects.Move);
                    }
                }
            }
        }

        private void Bookmark_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(BookmarkDataFormat))
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.Move;
            }
            e.Handled = true;
        }

        private void Bookmark_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(BookmarkDataFormat))
            {
                var droppedData = e.Data.GetData(BookmarkDataFormat) as BookmarkItem;
                var targetData = (sender as FrameworkElement)?.DataContext as BookmarkItem;

                if (droppedData != null && targetData != null && droppedData != targetData)
                {
                    int oldIndex = _bookmarks.IndexOf(droppedData);
                    int newIndex = _bookmarks.IndexOf(targetData);

                    if (oldIndex != -1 && newIndex != -1)
                    {
                        _bookmarks.Move(oldIndex, newIndex);
                        BookmarkManager.Save(_bookmarks.ToList());
                    }
                }
            }
            e.Handled = true;
        }

        private void DeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is BookmarkItem item)
            {
                _bookmarks.Remove(item);
                BookmarkManager.Save(_bookmarks.ToList());
            }
        }


        private void UrlTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
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

            _activeWebView.Source = new Uri(url);
        }
    }
}