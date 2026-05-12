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
        private double _savedNormalLeft;
        private double _savedNormalTop;
        private enum DisplayMode
        {
            AppBar,
            AutoHide,
            Normal
        }
        private DisplayMode _currentMode = DisplayMode.AppBar;
        private System.Windows.Threading.DispatcherTimer? _mouseTimer;
        private bool _isSlidOut = false;
        private const double FullWidthDefault = 400;
        private double _currentFullWidth = FullWidthDefault;
        private const double TriggerWidth = 2;

        private bool _isResizing = false;
        private System.Windows.Point _resizeStartPoint;
        private double _resizeStartWidth;
        private DateTime _lastResizeTime = DateTime.MinValue;





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

            // 通常モードの場合は現在の位置を記憶しておく
            if (_currentMode == DisplayMode.Normal)
            {
                _savedNormalLeft = this.Left;
                _savedNormalTop = this.Top;
            }

            // アニメーションを停止（これをしないとLeftの上書きが効かない）
            this.BeginAnimation(Window.LeftProperty, null);
            this.BeginAnimation(Window.WidthProperty, null);

            // 自動隠しタイマーを停止
            _mouseTimer?.Stop();
            
            _appBarHelper.Unregister();

            // 状態をNormalに戻してから画面外へ飛ばす
            this.WindowState = WindowState.Normal;
            this.Left = -30000;
            this.Top = -30000;

            // 重要：自分自身のアクティブ状態を解除する。
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
            
            if (_currentMode == DisplayMode.Normal)
            {
                // 通常モードなら記憶していた位置に戻す
                this.Left = _savedNormalLeft;
                this.Top = _savedNormalTop;
                this.Topmost = false;
            }
            else if (_currentMode == DisplayMode.AppBar)
            {
                // AppBarモードなら予約して右端へ
                _appBarHelper.Register();
            }
            else if (_currentMode == DisplayMode.AutoHide)
            {
                // 自動隠しモードなら予約せず「隠れた状態」として復帰
                _isSlidOut = true; // SlideOut()を確実に動かすため
                SlideOut(); 
                StartAutoHideTimer();
            }
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

        private void DisplayMode_Click(object sender, RoutedEventArgs e)
        {
            // モード切替前にアニメーションを完全に停止させる
            this.BeginAnimation(Window.LeftProperty, null);
            this.BeginAnimation(Window.WidthProperty, null);

            if (sender == AlwaysVisibleMenuItem)
            {
                _currentMode = DisplayMode.AppBar;
                AlwaysVisibleMenuItem.IsChecked = true;
                AutoHideMenuItem.IsChecked = false;
                NormalWindowMenuItem.IsChecked = false;
                StopAutoHideTimer();
                _appBarHelper.Register();
                this.Topmost = true;
                
                // 上下位置と高さをリセット
                var helper = new WindowInteropHelper(this);
                IntPtr hMonitor = NativeMethods.MonitorFromWindow(helper.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
                var mi = new NativeMethods.MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
                if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                {
                    double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                    this.Top = mi.rcMonitor.Top / dpi;
                    this.Height = (mi.rcMonitor.Bottom - mi.rcMonitor.Top) / dpi;
                }
            }
            else if (sender == NormalWindowMenuItem)
            {
                _currentMode = DisplayMode.Normal;
                AlwaysVisibleMenuItem.IsChecked = false;
                AutoHideMenuItem.IsChecked = false;
                NormalWindowMenuItem.IsChecked = true;

                _appBarHelper.Unregister();
                StopAutoHideTimer();
                this.Topmost = false;

                // 自由配置モードでは左右両方の端でリサイズできるようにする
                LeftResizeColumn.Width = new GridLength(4);
                RightResizeColumn.Width = new GridLength(4);
                
                // 位置を少し中央寄りに移動（端に張り付いていると分かりにくいため）
                this.Left += (_appBarHelper.Edge == NativeMethods.AppBarEdges.Left ? 20 : -20);
            }
            else
            {
                _currentMode = DisplayMode.AutoHide;
                AlwaysVisibleMenuItem.IsChecked = false;
                AutoHideMenuItem.IsChecked = true;
                NormalWindowMenuItem.IsChecked = false;
                _appBarHelper.Unregister();
                this.Topmost = true;
                StartAutoHideTimer();
            }
        }



        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                // 自由配置モードの時だけドラッグ移動を許可する
                if (_currentMode == DisplayMode.Normal)
                {
                    this.DragMove();
                }
            }
        }

        private void StartAutoHideTimer()
        {
            if (_mouseTimer == null)
            {
                _mouseTimer = new System.Windows.Threading.DispatcherTimer();
                _mouseTimer.Interval = TimeSpan.FromMilliseconds(100);
                _mouseTimer.Tick += MouseTimer_Tick;
            }
            _mouseTimer.Start();
            
            // 確実に隠すために、現在は「出ている」ことにしてから SlideOut を呼ぶ
            _isSlidOut = true;
            SlideOut();
        }

        private void StopAutoHideTimer()
        {
            _mouseTimer?.Stop();
            // 常時表示に戻す際は現在の設定幅に戻す
            this.Width = _currentFullWidth;
        }


        private void MouseTimer_Tick(object? sender, EventArgs e)
        {
            if (_currentMode != DisplayMode.AutoHide || _isFakeMinimized) return;

            // マウスの物理座標を取得
            var point = new System.Drawing.Point();
            NativeMethods.GetCursorPos(ref point);

            // 現在のモニター情報を取得
            var helper = new WindowInteropHelper(this);
            IntPtr hMonitor = NativeMethods.MonitorFromWindow(helper.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return;

            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            bool isMouseInTriggerZone = false;
            bool isMouseInWindow = false;

            if (_appBarHelper.Edge == NativeMethods.AppBarEdges.Right)
            {
                // 右端のトリガーゾーン（端から5ピクセル以内）
                isMouseInTriggerZone = (point.X >= mi.rcMonitor.Right - 5 && point.Y >= mi.rcMonitor.Top && point.Y <= mi.rcMonitor.Bottom);
                // ウィンドウ内かチェック
                isMouseInWindow = (point.X >= mi.rcMonitor.Right - (_currentFullWidth * dpi) && point.X <= mi.rcMonitor.Right && 
                                   point.Y >= mi.rcMonitor.Top && point.Y <= mi.rcMonitor.Bottom);
            }
            else
            {
                // 左端のトリガーゾーン
                isMouseInTriggerZone = (point.X <= mi.rcMonitor.Left + 5 && point.Y >= mi.rcMonitor.Top && point.Y <= mi.rcMonitor.Bottom);
                // ウィンドウ内かチェック
                isMouseInWindow = (point.X >= mi.rcMonitor.Left && point.X <= mi.rcMonitor.Left + (_currentFullWidth * dpi) && 
                                   point.Y >= mi.rcMonitor.Top && point.Y <= mi.rcMonitor.Bottom);
            }


            if (isMouseInTriggerZone && !_isSlidOut)
            {
                SlideIn();
            }
            else if (!isMouseInWindow && _isSlidOut)
            {
                // メニューやコンテキストメニューが開いている間は閉じない
                bool isMenuVisible = (LeftDockMenuItem.Parent is FrameworkElement parent && parent.IsVisible);
                if (!MonitorSelectMenuItem.IsSubmenuOpen && !isMenuVisible)
                {
                     SlideOut();
                }
            }
        }

        private void SlideIn()
        {
            if (_isSlidOut) return;
            _isSlidOut = true;
            this.Topmost = true;

            // 表示されたのでリサイズを許可
            LeftResizeGrip.IsHitTestVisible = true;
            RightResizeGrip.IsHitTestVisible = true;

            AnimateWindow(_currentFullWidth);
        }



        private void SlideOut()
        {
            if (!_isSlidOut) return;
            _isSlidOut = false;

            // 隠れる時はリサイズを禁止（マウスカーソルの誤変化を防ぐ）
            LeftResizeGrip.IsHitTestVisible = false;
            RightResizeGrip.IsHitTestVisible = false;

            AnimateWindow(TriggerWidth);

            // 隠れる際、もし自分がアクティブならフォーカスを他に譲る
            // これをしないと、隠れた後のマウス接近検知が不安定になることがある
            var helper = new WindowInteropHelper(this);
            if (NativeMethods.GetForegroundWindow() == helper.Handle)
            {
                IntPtr taskbarHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
                if (taskbarHwnd != IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(taskbarHwnd);
                }
            }
        }


        private void AnimateWindow(double targetWidth)
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var helper = new WindowInteropHelper(this);
            IntPtr hMonitor = NativeMethods.MonitorFromWindow(helper.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi)) return;

            double targetLeft;
            if (_appBarHelper.Edge == NativeMethods.AppBarEdges.Right)
            {
                targetLeft = (mi.rcMonitor.Right / dpi) - targetWidth;
            }
            else
            {
                targetLeft = mi.rcMonitor.Left / dpi;
            }

            // ホットコーナーモードでは上下を画面いっぱいにリセットする
            this.Top = mi.rcMonitor.Top / dpi;
            this.Height = (mi.rcMonitor.Bottom - mi.rcMonitor.Top) / dpi;

            var duration = TimeSpan.FromMilliseconds(200);
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

            var widthAnim = new System.Windows.Media.Animation.DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
            var leftAnim = new System.Windows.Media.Animation.DoubleAnimation(targetLeft, duration) { EasingFunction = ease };

            this.BeginAnimation(Window.WidthProperty, widthAnim);
            this.BeginAnimation(Window.LeftProperty, leftAnim);
        }


        private void Side_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode == DisplayMode.Normal)
            {
                // 自由配置モード：AppBarを解除し、両方のグリップを有効にする
                _appBarHelper.Unregister();
                StopAutoHideTimer();
                this.Topmost = false;
                LeftResizeColumn.Width = new GridLength(4);
                RightResizeColumn.Width = new GridLength(4);
                return;
            }

            if (sender == LeftDockMenuItem)
            {
                _appBarHelper.Edge = NativeMethods.AppBarEdges.Left;
                LeftDockMenuItem.IsChecked = true;
                RightDockMenuItem.IsChecked = false;
                
                // 左固定時は右端をリサイズ可能にする
                LeftResizeColumn.Width = new GridLength(0);
                RightResizeColumn.Width = new GridLength(4);
            }
            else
            {
                _appBarHelper.Edge = NativeMethods.AppBarEdges.Right;
                LeftDockMenuItem.IsChecked = false;
                RightDockMenuItem.IsChecked = true;

                // 右固定時は左端をリサイズ可能にする
                LeftResizeColumn.Width = new GridLength(4);
                RightResizeColumn.Width = new GridLength(0);
            }

            if (_currentMode == DisplayMode.AppBar)
            {
                _appBarHelper.SetPosition();
            }
            else
            {
                SlideIn();
            }
        }

        private void ResizeGrip_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _isResizing = true;

            // スクリーン座標（絶対座標）を取得して開始点とする
            var point = new System.Drawing.Point();
            NativeMethods.GetCursorPos(ref point);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            _resizeStartPoint = new System.Windows.Point(point.X / dpi, point.Y / dpi);
            
            _resizeStartWidth = this.Width;

            // アニメーションをクリア（これをしないとリサイズが効かない）
            this.BeginAnimation(Window.LeftProperty, null);
            this.BeginAnimation(Window.WidthProperty, null);

            // リサイズ中は勝手に隠れないようにタイマーを止める
            _mouseTimer?.Stop();

            ((UIElement)sender).CaptureMouse();
        }


        private void ResizeGrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isResizing) return;

            // 現在のスクリーン座標（絶対座標）を取得
            var point = new System.Drawing.Point();
            NativeMethods.GetCursorPos(ref point);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var currentPoint = new System.Windows.Point(point.X / dpi, point.Y / dpi);

            double diff = currentPoint.X - _resizeStartPoint.X;
            double newWidth;

            if (_appBarHelper.Edge == NativeMethods.AppBarEdges.Right)
            {
                newWidth = _resizeStartWidth - diff;
            }
            else
            {
                newWidth = _resizeStartWidth + diff;
            }

            if (newWidth < 300) newWidth = 300;
            if (newWidth > 800) newWidth = 800;

            if (_currentMode == DisplayMode.Normal)
            {
                // 自由配置モード：掴んだ方に応じて Left と Width を調整
                if (((FrameworkElement)sender).Name == "LeftResizeGrip")
                {
                    double oldRight = this.Left + this.Width;
                    this.Width = newWidth;
                    this.Left = oldRight - newWidth;
                }
                else
                {
                    this.Width = newWidth;
                }
                _currentFullWidth = newWidth;
                return;
            }

            this.Width = newWidth;
            _currentFullWidth = newWidth;

            var helper = new WindowInteropHelper(this);
            IntPtr hMonitor = NativeMethods.MonitorFromWindow(helper.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
            var mi = new NativeMethods.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                if (_appBarHelper.Edge == NativeMethods.AppBarEdges.Right)
                {
                    // 右端固定の場合：右端の位置をキープしたまま、左端（Left）を動かす
                    this.Left = (mi.rcMonitor.Right / dpi) - newWidth;
                }
                else
                {
                    // 左端固定の場合：左端の位置（Left）を常にモニター左端に固定する
                    this.Left = mi.rcMonitor.Left / dpi;
                }
            }
        }



        private void ResizeGrip_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isResizing)
            {
                _isResizing = false;
                ((UIElement)sender).ReleaseMouseCapture();

                // マウスを離した瞬間に、他のアプリを押し広げる（AppBarモードの場合のみ）
                if (_currentMode == DisplayMode.AppBar)
                {
                    // 重要：自分自身との衝突（隙間）を防ぐため、一度画面外へ飛ばしてから確定させる
                    this.Left = -30000;
                    _appBarHelper.SetPosition();
                }
                else
                {
                    // 自動隠しモードならタイマーを再開
                    StartAutoHideTimer();
                    _isSlidOut = true; // 現在は「出ている」状態
                }
            }
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