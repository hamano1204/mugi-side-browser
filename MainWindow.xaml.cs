using System;
using System.Collections.Generic;
using System.Text;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using System.IO;
using MugiSideBrowser.Services;
using WinDragEventArgs = System.Windows.DragEventArgs;
using WinDragDropEffects = System.Windows.DragDropEffects;
using WinDataFormats = System.Windows.DataFormats;

namespace MugiSideBrowser
{
    public partial class MainWindow : Window
    {
        private AppBarHelper _appBarHelper;
        private BookmarkService _bookmarkService;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private System.Windows.Point _dragStartPoint;
        private const string BookmarkDataFormat = "MugiSideBrowser.BookmarkItem";
        private static readonly uint ShowWindowMessage = NativeMethods.RegisterWindowMessage("MugiSideBrowser_ShowWindowMessage");
        private Microsoft.Web.WebView2.Wpf.WebView2? _activeWebView;
        private BookmarkItem? _activeBookmarkTop;
        private BookmarkItem? _activeBookmarkMiddle;
        private BookmarkItem? _activeBookmarkBottom;
        private TargetWindow _activePane = TargetWindow.Top;
        private readonly Dictionary<BookmarkItem, Microsoft.Web.WebView2.Wpf.WebView2> _bookmarkWebViews = new();
        private bool _isMobileMode = false;
        private string? _defaultUserAgent = null;
        private bool _useExternalBrowserOnCtrlClick = true;
        private const string MobileUserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 15_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/15.0 Mobile/15E148 Safari/604.1";
        private double _resizeStartHeight;
        private System.Threading.Mutex? _appBarLockMutex;
        private System.Windows.Point _headerDragStartPoint;
        private bool _isHeaderMouseDown = false;

        private enum SplitMode
        {
            Single,
            Double,
            Triple
        }
        private bool _isMiddlePaneOpen = false;
        private bool _isBottomPaneOpen = false;
        private SplitMode _currentSplitMode
        {
            get
            {
                if (_isMiddlePaneOpen && _isBottomPaneOpen) return SplitMode.Triple;
                if (_isMiddlePaneOpen || _isBottomPaneOpen) return SplitMode.Double;
                return SplitMode.Single;
            }
        }

        public enum TargetWindow
        {
            Top,
            Middle,
            Bottom
        }
        private enum DisplayMode
        {
            AppBar,
            AutoHide,
            Normal
        }
        private DisplayMode _currentMode = DisplayMode.AppBar;
        private System.Windows.Threading.DispatcherTimer? _mouseTimer;
        private bool _isSlidOut = false;
        private bool _isDragging = false;
        
        // マニュアルドラッグ用変数
        private bool _isManualDragging = false;
        private System.Windows.Point _dragStartMousePos;
        private System.Windows.Point _dragStartWindowPos;
        private DisplayMode _dragOriginalMode;

        private const double FullWidthDefault = 460;
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
            this.Width = _currentFullWidth; // 設定したデフォルトの幅を適用
            
            this.SourceInitialized += MainWindow_SourceInitialized;
            this.Closing += MainWindow_Closing;
            this.StateChanged += MainWindow_StateChanged;
            
            _bookmarkService = new BookmarkService();
            BookmarkList.ItemsSource = _bookmarkService.Bookmarks;
            InitializeBookmarksAsync();
            InitializeNotifyIcon();
            UpdateMinimizeButtonState();
        }

        private Microsoft.Web.WebView2.Wpf.WebView2 CreateNewWebView()
        {
            var wv = new Microsoft.Web.WebView2.Wpf.WebView2
            {
                Margin = new Thickness(0),
                Visibility = Visibility.Visible
            };
            wv.GotFocus += WebView_GotFocus;
            return wv;
        }

        private async void InitializeBookmarksAsync()
        {
            await _bookmarkService.InitializeAsync();

            // 起動時に全項目のロード/アクティブ状態を初期化
            foreach (var item in _bookmarkService.Bookmarks)
            {
                item.IsLoaded = false;
                item.IsActive = false;
            }

            // お気に入りが空の場合、Googleのお気に入りを作成して追加
            if (!_bookmarkService.Bookmarks.Any(b => !b.IsSeparator))
            {
                var defaultGoogle = new BookmarkItem
                {
                    Title = "Google",
                    Url = "https://www.google.com",
                    FaviconUrl = "https://www.google.com/s2/favicons?domain=google.com&sz=64"
                };
                await _bookmarkService.AddBookmarkAsync(defaultGoogle);
            }

            // 最初のお気に入り項目を開く
            var first = _bookmarkService.Bookmarks.FirstOrDefault(b => !b.IsSeparator);
            if (first != null)
            {
                ShowBookmarkWebView(first, TargetWindow.Top);
            }
        }

        private void UpdateWindowTitle()
        {
            string modeName = _currentMode switch
            {
                DisplayMode.AppBar => "常時表示",
                DisplayMode.AutoHide => "自動隠し",
                DisplayMode.Normal => "自由配置",
                _ => "不明"
            };

            this.Title = $"MugiSideBrowser [{modeName}]";
        }

        private bool TryAcquireAppBarLock()
        {
            if (_appBarLockMutex != null) return true;

            try
            {
                bool createdNew;
                var mutex = new System.Threading.Mutex(false, "Global\\MugiSideBrowser_AppBarGlobalLock", out createdNew);
                if (mutex.WaitOne(0))
                {
                    _appBarLockMutex = mutex;
                    return true;
                }
                mutex.Dispose();
            }
            catch { }
            return false;
        }

        private void ReleaseAppBarLock()
        {
            if (_appBarLockMutex != null)
            {
                try { _appBarLockMutex.ReleaseMutex(); } catch { }
                _appBarLockMutex.Dispose();
                _appBarLockMutex = null;
            }
        }

        private void AutoAllocatePosition()
        {
            // AppBar の利用権（鍵）の取得を試みる
            if (TryAcquireAppBarLock())
            {
                _appBarHelper.ResetMonitorInfo();
                // 1番目のインスタンス：メインモニターの右端へ
                _currentMode = DisplayMode.AppBar;
                _appBarHelper.Edge = NativeMethods.AppBarEdges.Right;
                
                this.ShowInTaskbar = false;

                _appBarHelper.Register();
                UpdateWindowTitle();
                UpdateMinimizeButtonState();
            }
            else
            {
                // 2番目以降：通常ウィンドウとして起動
                SetToNormalMode();
            }
        }

        private void ApplyToolWindowStyle(bool enable)
        {
            var helper = new System.Windows.Interop.WindowInteropHelper(this);
            IntPtr hWnd = helper.Handle;
            if (hWnd == IntPtr.Zero) return;

            int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
            if (enable)
            {
                exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            }
            else
            {
                exStyle &= ~NativeMethods.WS_EX_TOOLWINDOW;
            }
            NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE, exStyle);
        }

        private void SetToNormalMode()
        {
            _currentMode = DisplayMode.Normal;
            
            _appBarHelper.Unregister();
            ReleaseAppBarLock();
            StopAutoHideTimer();
            
            this.Topmost = false;
            this.ShowInTaskbar = true;
            ApplyToolWindowStyle(false);
            LeftResizeColumn.Width = new GridLength(4);
            RightResizeColumn.Width = new GridLength(4);
            BottomResizeRow.Height = new GridLength(4);
            
            // 現在のモニターの作業領域(rcWork)を取得し、高さを少し狭める
            try
            {
                var helper = new WindowInteropHelper(this);
                IntPtr hMonitor = NativeMethods.MonitorFromWindow(helper.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
                var mi = new NativeMethods.MONITORINFO();
                mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
                if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                {
                    double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                    double workAreaHeight = (mi.rcWork.Bottom - mi.rcWork.Top) / dpi;
                    
                    // 作業領域の高さより 80px 狭くする。ただし最小値は400px
                    double targetHeight = workAreaHeight - 80;
                    if (targetHeight < 400) targetHeight = 400;
                    
                    this.Height = targetHeight;
                    // 位置を作業領域のTopから20px下に配置し、上下に余白を作る
                    this.Top = (mi.rcWork.Top / dpi) + 20;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adjusting window size: {ex.Message}");
            }

            UpdateWindowTitle();
            UpdateMinimizeButtonState();
        }



        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            // 本当の最小化が呼ばれたら、モードに応じて切り替える
            if (this.WindowState == WindowState.Minimized)
            {
                if (_currentMode == DisplayMode.AppBar || _currentMode == DisplayMode.AutoHide)
                {
                    // AppBar または AutoHide (Hot Corner) モードの時は最小化を無効にする
                    this.WindowState = WindowState.Normal;
                }
            }
        }






        private void BookmarkScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                if (e.Delta > 0) scrollViewer.LineUp();
                else scrollViewer.LineDown();
                e.Handled = true;
            }
        }

        private async void AddSeparator_Click(object sender, RoutedEventArgs e)
        {
            await _bookmarkService.AddBookmarkAsync(new BookmarkItem 
            { 
                IsSeparator = true, 
                Title = "区切り線" 
            });
        }

        private void Tools_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                element.ContextMenu.IsOpen = true;
            }
        }




        private bool _hasActuallyMoved = false;

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                if (_currentMode == DisplayMode.Normal)
                {
                    this.DragMove();
                }
                else
                {
                    // AppBar / AutoHide の完全手動ドラッグ移動を開始
                    _isDragging = true;
                    _isManualDragging = true;
                    _hasActuallyMoved = false;
                    _dragOriginalMode = _currentMode;

                    // ドラッグ開始時のマウス座標（スクリーン座標）
                    var mousePoint = new System.Drawing.Point();
                    NativeMethods.GetCursorPos(ref mousePoint);
                    _dragStartMousePos = new System.Windows.Point(mousePoint.X, mousePoint.Y);

                    // ドラッグ開始時のウィンドウ座標（WPF論理座標）
                    _dragStartWindowPos = new System.Windows.Point(this.Left, this.Top);

                    // アニメーションを停止して競合を防ぐ
                    this.BeginAnimation(Window.LeftProperty, null);
                    this.BeginAnimation(Window.TopProperty, null);

                    // イベントを捕捉（ウィンドウ外に出ても追従するように）
                    if (sender is UIElement element)
                    {
                        element.CaptureMouse();
                    }
                }
            }
        }

        private void TitleBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isManualDragging)
            {
                double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

                // マウスの現在位置（スクリーン座標）
                var mousePoint = new System.Drawing.Point();
                NativeMethods.GetCursorPos(ref mousePoint);
                var currentMousePos = new System.Windows.Point(mousePoint.X, mousePoint.Y);

                // 閾値チェック：一定以上動いていない場合は何もしない（クリック時の誤爆防止）
                if (!_hasActuallyMoved && 
                    Math.Abs(currentMousePos.X - _dragStartMousePos.X) < 5 && 
                    Math.Abs(currentMousePos.Y - _dragStartMousePos.Y) < 5)
                {
                    return;
                }

                _hasActuallyMoved = true;

                // 実際に動き出してから配置を解除
                if (_appBarHelper.IsRegistered)
                {
                    var helper = new System.Windows.Interop.WindowInteropHelper(this);

                    // 解除「直前」にWin32レベルで現在の位置（ドラッグ開始位置）に叩き込む
                    // これにより、OSが解除時に以前の場所（右側など）を想起する隙を与えない
                    NativeMethods.MoveWindow(helper.Handle, 
                        (int)(_dragStartWindowPos.X * dpi), 
                        (int)(_dragStartWindowPos.Y * dpi), 
                        (int)(this.Width * dpi), 
                        (int)(this.Height * dpi), 
                        true);

                    _appBarHelper.Unregister();

                    // WPFの状態も同期
                    this.Left = _dragStartWindowPos.X;
                    this.Top = _dragStartWindowPos.Y;
                }

                // 移動量（WPF論理座標に変換）
                double deltaX = (mousePoint.X - _dragStartMousePos.X) / dpi;
                double deltaY = (mousePoint.Y - _dragStartMousePos.Y) / dpi;

                // ウィンドウを移動
                this.Left = _dragStartWindowPos.X + deltaX;
                this.Top = _dragStartWindowPos.Y + deltaY;
            }
        }

        private void TitleBar_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_isManualDragging && e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                _isManualDragging = false;

                if (sender is UIElement element)
                {
                    element.ReleaseMouseCapture();
                }

                // 実際に移動していた場合のみ、最寄りの端にスナップして再登録する
                if (_hasActuallyMoved)
                {
                    SnapToNearestEdge(_dragOriginalMode);
                }
                
                _isDragging = false;
                _hasActuallyMoved = false;
            }
        }

        private void SnapToNearestEdge(DisplayMode mode)
        {
            _appBarHelper.ResetMonitorInfo();
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            
            // 現在のウィンドウ中心座標（ピクセル単位）
            double centerX = (this.Left + this.Width / 2) * dpi;
            double centerY = (this.Top + this.Height / 2) * dpi;
            
            // 最寄りのモニターを探す
            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)centerX, (int)centerY));

            // モニターの中心から見て左右どちらに近いか
            double screenCenterX = screen.Bounds.Left + (screen.Bounds.Width / 2);
            var edge = (centerX > screenCenterX) ? NativeMethods.AppBarEdges.Right : NativeMethods.AppBarEdges.Left;

            // 状態を更新
            _currentMode = mode;
            _appBarHelper.Edge = edge;

            // リサイズ方向を調整
            if (edge == NativeMethods.AppBarEdges.Left)
            {
                // 左固定時は右端(Column 2)をリサイズ可能にする
                LeftResizeColumn.Width = new GridLength(0);
                RightResizeColumn.Width = new GridLength(4);
            }
            else
            {
                // 右固定時は左端(Column 0)をリサイズ可能にする
                LeftResizeColumn.Width = new GridLength(4);
                RightResizeColumn.Width = new GridLength(0);
            }
            
            if (mode == DisplayMode.AppBar)
            {
                _appBarHelper.Register();
                this.Topmost = true;
            }
            else if (mode == DisplayMode.AutoHide)
            {
                // AutoHide の場合は再度隠した状態からスタート
                _isSlidOut = true; 
                StartAutoHideTimer();
                this.Topmost = true;
            }
            
            UpdateWindowTitle();
            UpdateMinimizeButtonState();
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
            if (_currentMode != DisplayMode.AutoHide || _isDragging) return;

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
                var pageToolsMenu = this.Resources["PageToolsMenu"] as System.Windows.Controls.ContextMenu;
                if (!ToolsMenu.IsOpen && (pageToolsMenu == null || !pageToolsMenu.IsOpen))
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
            var mi = _appBarHelper.CurrentWorkAreaRect;

            double targetLeft;
            if (_appBarHelper.Edge == NativeMethods.AppBarEdges.Right)
            {
                targetLeft = (mi.Right / dpi) - targetWidth;
            }
            else
            {
                targetLeft = mi.Left / dpi;
            }

            // ホットコーナーモードでは上下を画面いっぱいにリセットする
            this.Top = mi.Top / dpi;
            this.Height = (mi.Bottom - mi.Top) / dpi;

            var duration = TimeSpan.FromMilliseconds(200);
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };

            var widthAnim = new System.Windows.Media.Animation.DoubleAnimation(targetWidth, duration) { EasingFunction = ease };
            var leftAnim = new System.Windows.Media.Animation.DoubleAnimation(targetLeft, duration) { EasingFunction = ease };

            this.BeginAnimation(Window.WidthProperty, widthAnim);
            this.BeginAnimation(Window.LeftProperty, leftAnim);
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
            _resizeStartHeight = this.Height;

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

            if (_currentMode == DisplayMode.Normal)
            {
                // 自由配置モード：掴んだグリップによって計算を分ける
                if (((FrameworkElement)sender).Name == "BottomResizeGrip")
                {
                    double diffY = currentPoint.Y - _resizeStartPoint.Y;
                    double newHeight = _resizeStartHeight + diffY;

                    // 最小高さ 200px, 最大高さはモニターに合わせる
                    if (newHeight < 200) newHeight = 200;
                    
                    var helper_h = new WindowInteropHelper(this);
                    IntPtr hMonitor_h = NativeMethods.MonitorFromWindow(helper_h.Handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
                    var mi_h = new NativeMethods.MONITORINFO();
                    mi_h.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFO));
                    if (NativeMethods.GetMonitorInfo(hMonitor_h, ref mi_h))
                    {
                        double maxHeight = (mi_h.rcMonitor.Bottom - mi_h.rcMonitor.Top) / dpi;
                        if (newHeight > maxHeight) newHeight = maxHeight;
                    }

                    this.Height = newHeight;
                    return;
                }

                if (((FrameworkElement)sender).Name == "LeftResizeGrip")
                {
                    newWidth = _resizeStartWidth - diff; // 左に動かす（diffマイナス）と幅が増える
                }
                else
                {
                    newWidth = _resizeStartWidth + diff; // 右に動かす（diffプラス）と幅が増える
                }
            }
            else
            {
                // AppBar/ホットコーナーモード：配置位置によって計算を分ける
                if (_appBarHelper.Edge == NativeMethods.AppBarEdges.Right)
                {
                    newWidth = _resizeStartWidth - diff;
                }
                else
                {
                    newWidth = _resizeStartWidth + diff;
                }
            }

            if (newWidth < 300) newWidth = 300;
            if (newWidth > 800) newWidth = 800;

            if (_currentMode == DisplayMode.Normal)
            {
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
                else if (_currentMode == DisplayMode.AutoHide)
                {
                    // 自動隠しモードならタイマーを再開
                    StartAutoHideTimer();
                    _isSlidOut = true; // 現在は「出ている」状態
                }
                // Normal モードの時は何もしない
            }
        }






        private void UserAgent_Click(object sender, RoutedEventArgs e)
        {
            _isMobileMode = !_isMobileMode;
            UpdateUserAgent();
        }

        private void UpdateUserAgent()
        {
            try
            {
                string targetUA = _isMobileMode ? MobileUserAgent : (_defaultUserAgent ?? "");
                if (string.IsNullOrEmpty(targetUA)) return;

                // ブックマーク用にキャッシュされているすべてのWebViewに適用
                foreach (var kvp in _bookmarkWebViews)
                {
                    if (kvp.Value.CoreWebView2 != null)
                    {
                        kvp.Value.CoreWebView2.Settings.UserAgent = targetUA;
                    }
                }

                // 現在のページがあればリロードして反映
                if (_activeWebView != null && _activeWebView.Source != null && !string.IsNullOrEmpty(_activeWebView.Source.ToString()))
                {
                    _activeWebView.Reload();
                }
            }
            catch (ObjectDisposedException) { /* 破棄済みの場合は無視 */ }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"UpdateUserAgent Error: {ex.Message}"); }
        }

        private void ExternalBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem)
            {
                _useExternalBrowserOnCtrlClick = menuItem.IsChecked;
            }
        }


        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            // 起動時の自動配置（ハンドルが生成された後に行う）
            AutoAllocatePosition();
            ApplyToolWindowStyle(this.ShowInTaskbar == false);

            // ウィンドウメッセージのフックを登録（多重起動時のウィンドウ復元用）
            var helper = new WindowInteropHelper(this);
            var source = HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == ShowWindowMessage)
            {
                // トレイまたは擬似最小化からウィンドウを最前面に復帰させる
                ShowFromTray();
                handled = true;
            }
            return IntPtr.Zero;
        }


        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            _appBarHelper.Unregister();
            ReleaseAppBarLock();

            // すべてのBookmark用WebView2インスタンスを破棄する
            foreach (var wv in _bookmarkWebViews.Values)
            {
                try
                {
                    wv.GotFocus -= WebView_GotFocus;
                    if (wv.CoreWebView2 != null)
                    {
                        wv.CoreWebView2.SourceChanged -= CoreWebView2_SourceChanged;
                        wv.CoreWebView2.HistoryChanged -= CoreWebView2_HistoryChanged;
                        wv.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
                    }
                    wv.Dispose();
                }
                catch { }
            }
            _bookmarkWebViews.Clear();
        }

        private void CoreWebView2_SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        {
            if (sender is CoreWebView2 senderEvt && _activeWebView != null && _activeWebView.CoreWebView2 == senderEvt)
            {
                UrlTextBox.Text = _activeWebView.Source.ToString();
            }
        }

        private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            if (_useExternalBrowserOnCtrlClick)
            {
                // オプションが有効なら、すべての新しいウィンドウリクエストを標準ブラウザで開く
                // (Ctrl+クリック、ミドルクリック、target="_blank" など)
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = e.Uri,
                        UseShellExecute = true
                    });
                    // WebView2内での遷移をキャンセル
                    e.Handled = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to open external browser: {ex.Message}");
                }
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

                // フォーカスされたWebViewのコンテナに応じてアクティブペインを更新
                if (WebViewTopHolder.Children.Contains(senderWebView))
                {
                    _activePane = TargetWindow.Top;
                }
                else if (WebViewMiddleHolder.Children.Contains(senderWebView))
                {
                    _activePane = TargetWindow.Middle;
                }
                else if (WebViewBottomHolder.Children.Contains(senderWebView))
                {
                    _activePane = TargetWindow.Bottom;
                }
                UpdateBookmarkActiveState();
            }
        }

        private void ApplySplitLayout()
        {
            TopRow.MinHeight = 100;

            if (_isMiddlePaneOpen && _isBottomPaneOpen)
            {
                // 3分割
                MiddleRow.MinHeight = 100;
                BottomRow.MinHeight = 100;

                TopRow.Height = new GridLength(1, GridUnitType.Star);
                MiddleRow.Height = new GridLength(1, GridUnitType.Star);
                BottomRow.Height = new GridLength(1, GridUnitType.Star);

                Grid.SetRow(WebViewBottomContainer, 4);

                VerticalSplitter1.Visibility = Visibility.Visible;
                VerticalSplitter2.Visibility = Visibility.Visible;

                WebViewMiddleContainer.Visibility = Visibility.Visible;
                WebViewBottomContainer.Visibility = Visibility.Visible;
            }
            else if (_isMiddlePaneOpen)
            {
                // 上・中
                MiddleRow.MinHeight = 100;
                BottomRow.MinHeight = 0;

                TopRow.Height = new GridLength(1, GridUnitType.Star);
                MiddleRow.Height = new GridLength(1, GridUnitType.Star);
                BottomRow.Height = new GridLength(0);

                Grid.SetRow(WebViewBottomContainer, 4);

                VerticalSplitter1.Visibility = Visibility.Visible;
                VerticalSplitter2.Visibility = Visibility.Collapsed;

                WebViewMiddleContainer.Visibility = Visibility.Visible;
                WebViewBottomContainer.Visibility = Visibility.Collapsed;
            }
            else if (_isBottomPaneOpen)
            {
                // 上・下 (中ペインが閉じているので、下ペインをGridの第2行(MiddleRow)に配置してスプリッターでリサイズできるようにする)
                MiddleRow.MinHeight = 100;
                BottomRow.MinHeight = 0;

                TopRow.Height = new GridLength(1, GridUnitType.Star);
                MiddleRow.Height = new GridLength(1, GridUnitType.Star);
                BottomRow.Height = new GridLength(0);

                Grid.SetRow(WebViewBottomContainer, 2);

                VerticalSplitter1.Visibility = Visibility.Visible;
                VerticalSplitter2.Visibility = Visibility.Collapsed;

                WebViewMiddleContainer.Visibility = Visibility.Collapsed;
                WebViewBottomContainer.Visibility = Visibility.Visible;
            }
            else
            {
                // 1画面
                MiddleRow.MinHeight = 0;
                BottomRow.MinHeight = 0;

                TopRow.Height = new GridLength(1, GridUnitType.Star);
                MiddleRow.Height = new GridLength(0);
                BottomRow.Height = new GridLength(0);

                Grid.SetRow(WebViewBottomContainer, 4);

                VerticalSplitter1.Visibility = Visibility.Collapsed;
                VerticalSplitter2.Visibility = Visibility.Collapsed;

                WebViewMiddleContainer.Visibility = Visibility.Collapsed;
                WebViewBottomContainer.Visibility = Visibility.Collapsed;
            }
        }

        private BookmarkItem? GetActiveBookmarkForPane(TargetWindow pane)
        {
            return pane switch
            {
                TargetWindow.Top => _activeBookmarkTop,
                TargetWindow.Middle => _activeBookmarkMiddle,
                TargetWindow.Bottom => _activeBookmarkBottom,
                _ => null
            };
        }

        private void UpdateActiveWebViewAfterSplitChange()
        {
            if (_currentSplitMode == SplitMode.Single)
            {
                var visibleWebView = WebViewTopHolder.Children.OfType<Microsoft.Web.WebView2.Wpf.WebView2>()
                                      .FirstOrDefault(w => w.Visibility == Visibility.Visible);
                if (visibleWebView != null)
                {
                    _activeWebView = visibleWebView;
                }
                else if (_activeBookmarkTop != null && _bookmarkWebViews.TryGetValue(_activeBookmarkTop, out var activeWv))
                {
                    _activeWebView = activeWv;
                }
                else
                {
                    _activeWebView = null;
                }
            }
            else if (_currentSplitMode == SplitMode.Double)
            {
                // もしアクティブが中コンテナなら、非表示になったため上部に切り替える
                if (_activeWebView != null && WebViewMiddleHolder.Children.Contains(_activeWebView))
                {
                    var visibleWebView = WebViewTopHolder.Children.OfType<Microsoft.Web.WebView2.Wpf.WebView2>()
                                          .FirstOrDefault(w => w.Visibility == Visibility.Visible);
                    if (visibleWebView != null)
                    {
                        _activeWebView = visibleWebView;
                    }
                    else if (_activeBookmarkTop != null && _bookmarkWebViews.TryGetValue(_activeBookmarkTop, out var activeWv))
                    {
                        _activeWebView = activeWv;
                    }
                    else
                    {
                        _activeWebView = null;
                    }
                }
            }

            if (_activeWebView != null && _activeWebView.Source != null)
            {
                UrlTextBox.Text = _activeWebView.Source.ToString();
            }
            else
            {
                var activeB = GetActiveBookmarkForPane(_activePane);
                UrlTextBox.Text = activeB?.Url ?? "";
            }
            UpdateBookmarkActiveState();
        }

        private void ResumeTop_Click(object sender, RoutedEventArgs e)
        {
            if (_activeBookmarkTop != null) ShowBookmarkWebView(_activeBookmarkTop, TargetWindow.Top);
        }

        private void ResumeMiddle_Click(object sender, RoutedEventArgs e)
        {
            if (_activeBookmarkMiddle != null) ShowBookmarkWebView(_activeBookmarkMiddle, TargetWindow.Middle);
        }

        private void ResumeBottom_Click(object sender, RoutedEventArgs e)
        {
            if (_activeBookmarkBottom != null) ShowBookmarkWebView(_activeBookmarkBottom, TargetWindow.Bottom);
        }

        private void TopSleepPlaceholder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_activeBookmarkTop != null) ShowBookmarkWebView(_activeBookmarkTop, TargetWindow.Top);
        }

        private void MiddleSleepPlaceholder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_activeBookmarkMiddle != null) ShowBookmarkWebView(_activeBookmarkMiddle, TargetWindow.Middle);
        }

        private void BottomSleepPlaceholder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_activeBookmarkBottom != null) ShowBookmarkWebView(_activeBookmarkBottom, TargetWindow.Bottom);
        }

        // 各ペインのPreviewMouseDownを共通ヘルパーで処理
        private void WebViewContainer_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => ActivatePaneOnMouseDown(TargetWindow.Top);

        private void WebViewMiddleContainer_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => ActivatePaneOnMouseDown(TargetWindow.Middle);

        private void WebViewBottomContainer_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => ActivatePaneOnMouseDown(TargetWindow.Bottom);

        private void ActivatePaneOnMouseDown(TargetWindow pane)
        {
            _activePane = pane;
            var holder = pane switch
            {
                TargetWindow.Top    => WebViewTopHolder,
                TargetWindow.Middle => WebViewMiddleHolder,
                _                   => WebViewBottomHolder
            };
            _activeWebView = holder.Children.OfType<Microsoft.Web.WebView2.Wpf.WebView2>()
                                   .FirstOrDefault(w => w.Visibility == Visibility.Visible);
            if (_activeWebView != null && _activeWebView.Source != null)
            {
                UrlTextBox.Text = _activeWebView.Source.ToString();
            }
            else
            {
                var activeB = GetActiveBookmarkForPane(_activePane);
                UrlTextBox.Text = activeB?.Url ?? "";
            }
            UpdateBookmarkActiveState();
        }

        private void HeaderBack_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string tag)
            {
                ActivatePaneByName(tag);
                if (_activeWebView != null && _activeWebView.CanGoBack)
                {
                    _activeWebView.GoBack();
                }
            }
            e.Handled = true;
        }

        private void HeaderForward_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string tag)
            {
                ActivatePaneByName(tag);
                if (_activeWebView != null && _activeWebView.CanGoForward)
                {
                    _activeWebView.GoForward();
                }
            }
            e.Handled = true;
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            if (_activeWebView != null)
            {
                _activeWebView.Reload();
            }
            else
            {
                var activeB = GetActiveBookmarkForPane(_activePane);
                if (activeB != null)
                {
                    ShowBookmarkWebView(activeB, _activePane);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode == DisplayMode.AppBar || _currentMode == DisplayMode.AutoHide)
            {
                return;
            }
            this.WindowState = WindowState.Minimized;
        }

        private void ToggleNormalAppBar_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode == DisplayMode.Normal)
            {
                TransitionToDisplayMode(DisplayMode.AppBar);
            }
            else
            {
                TransitionToDisplayMode(DisplayMode.Normal);
            }
        }

        private void ToggleAppBarAutoHide_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode == DisplayMode.AppBar)
            {
                TransitionToDisplayMode(DisplayMode.AutoHide);
            }
            else if (_currentMode == DisplayMode.AutoHide)
            {
                TransitionToDisplayMode(DisplayMode.AppBar);
            }
        }

        private void TransitionToDisplayMode(DisplayMode mode)
        {
            // モード切替前にアニメーションを完全に停止させる
            this.BeginAnimation(Window.LeftProperty, null);
            this.BeginAnimation(Window.WidthProperty, null);
            this.BeginAnimation(Window.TopProperty, null);

            if (mode == DisplayMode.AppBar)
            {
                if (!TryAcquireAppBarLock())
                {
                    // 鍵が取れなければ切り替えを阻止
                    UpdateWindowControlsState();
                    return;
                }

                _currentMode = DisplayMode.AppBar;
                this.ShowInTaskbar = false;
                ApplyToolWindowStyle(true);
                StopAutoHideTimer();

                // 1. AppBarへの安全な初期位置を設定
                var mi_safe = _appBarHelper.CurrentWorkAreaRect;

                // 2. 一旦モニターの中央付近へワープさせて「きれいな状態」にする
                double dpi_safe = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                this.Width = _currentFullWidth;
                this.Left = (mi_safe.Left + (mi_safe.Right - mi_safe.Left) / 2) / dpi_safe - (this.Width / 2);
                this.Top = (mi_safe.Top + (mi_safe.Bottom - mi_safe.Top) / 2) / dpi_safe - (this.Height / 2);

                // 3. 改めて登録
                _appBarHelper.Register();
                this.Topmost = true;

                // 上下位置と高さをリセット
                this.Top = mi_safe.Top / dpi_safe;
                this.Height = (mi_safe.Bottom - mi_safe.Top) / dpi_safe;
                
                UpdateWindowTitle();
            }
            else if (mode == DisplayMode.Normal)
            {
                SetToNormalMode();
            }
            else if (mode == DisplayMode.AutoHide)
            {
                if (!TryAcquireAppBarLock())
                {
                    UpdateWindowControlsState();
                    return;
                }

                _currentMode = DisplayMode.AutoHide;
                _appBarHelper.Unregister();
                this.Topmost = true;
                this.ShowInTaskbar = false;
                ApplyToolWindowStyle(true);
                BottomResizeRow.Height = new GridLength(0);
                StartAutoHideTimer();
                UpdateWindowTitle();
            }

            UpdateWindowControlsState();
        }

        private void UpdateMinimizeButtonState()
        {
            UpdateWindowControlsState();
        }

        private void UpdateWindowControlsState()
        {
            if (_currentMode == DisplayMode.Normal)
            {
                if (MinimizeButton != null)
                {
                    MinimizeButton.Visibility = Visibility.Visible;
                    MinimizeButton.IsEnabled = true;
                }
                if (ToggleAppBarAutoHideButton != null)
                {
                    ToggleAppBarAutoHideButton.Visibility = Visibility.Collapsed;
                }
                if (ToggleNormalAppBarButton != null)
                {
                    ToggleNormalAppBarButton.Visibility = Visibility.Visible;
                    ToggleNormalAppBarButton.Content = "\uE90D"; // DockRight
                    ToggleNormalAppBarButton.ToolTip = "サイドバー表示 (AppBar)";
                }
            }
            else
            {
                if (MinimizeButton != null)
                {
                    MinimizeButton.Visibility = Visibility.Collapsed;
                    MinimizeButton.IsEnabled = false;
                }
                if (ToggleAppBarAutoHideButton != null)
                {
                    ToggleAppBarAutoHideButton.Visibility = Visibility.Visible;
                    if (_currentMode == DisplayMode.AppBar)
                    {
                        ToggleAppBarAutoHideButton.Content = ""; // Unpin (E77A)
                        ToggleAppBarAutoHideButton.ToolTip = "自動的に隠す (AutoHide)";
                    }
                    else // AutoHide
                    {
                        ToggleAppBarAutoHideButton.Content = ""; // Pin (E718)
                        ToggleAppBarAutoHideButton.ToolTip = "常時表示 (AppBar)";
                    }
                }
                if (ToggleNormalAppBarButton != null)
                {
                    ToggleNormalAppBarButton.Visibility = Visibility.Visible;
                    ToggleNormalAppBarButton.Content = ""; // Window/ChromeRestore (E827)
                    ToggleNormalAppBarButton.ToolTip = "自由配置ウィンドウ";
                }
            }
        }

        private async void Star_Click(object sender, RoutedEventArgs e)
        {
            if (_activeWebView == null || _activeWebView.CoreWebView2 == null) return;

            string url = _activeWebView.Source.ToString();
            string title = _activeWebView.CoreWebView2.DocumentTitle;

            if (string.IsNullOrEmpty(title)) title = url;

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

            try
            {
                await _bookmarkService.AddBookmarkAsync(newItem);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"ブックマークの追加に失敗しました: {ex.Message}", "エラー");
            }
        }

        private void CopyUrl_Click(object sender, RoutedEventArgs e)
        {
            if (_activeWebView != null && _activeWebView.Source != null)
            {
                try
                {
                    System.Windows.Clipboard.SetText(_activeWebView.Source.ToString());
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"コピーに失敗しました: {ex.Message}", "エラー");
                }
            }
            else
            {
                var activeB = GetActiveBookmarkForPane(_activePane);
                if (activeB != null)
                {
                    try
                    {
                        System.Windows.Clipboard.SetText(activeB.Url);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"コピーに失敗しました: {ex.Message}", "エラー");
                    }
                }
            }
        }

        private void OpenExternal_Click(object sender, RoutedEventArgs e)
        {
            string? url = _activeWebView?.Source?.ToString();
            if (url == null)
            {
                url = GetActiveBookmarkForPane(_activePane)?.Url;
            }

            if (url != null)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"標準ブラウザで開けませんでした: {ex.Message}", "エラー");
                }
            }
        }

        private void Bookmark_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                Bookmark_MouseLeftButtonUp(sender, e);
            }
            else if (e.ChangedButton == System.Windows.Input.MouseButton.Middle)
            {
                Bookmark_MouseMiddleButtonUp(sender, e);
            }
        }

        private void Bookmark_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is BookmarkItem item)
            {
                if (item.IsSeparator)
                {
                    e.Handled = true;
                    return;
                }

                // 通常クリック時は、現在アクティブ（フォーカス）なペインでお気に入りを開く
                ShowBookmarkWebView(item, _activePane);
            }
            e.Handled = true;
        }

        private void Bookmark_MouseMiddleButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is BookmarkItem item)
            {
                if (item.IsSeparator)
                {
                    e.Handled = true;
                    return;
                }

                if (TryActivatePaneWithBookmark(item))
                {
                    e.Handled = true;
                    return;
                }

                // マウスの中ボタン（ホイールクリック）で画面分割を自動で増やしながら開く
                switch (_currentSplitMode)
                {
                    case SplitMode.Single:
                        // 1画面時は2分割（下ペイン）にして開く
                        OpenBookmarkInBottomWindow(item);
                        break;
                    case SplitMode.Double:
                        // 2画面時は3分割（中ペイン）にして開く
                        OpenBookmarkInMiddleWindow(item);
                        break;
                    case SplitMode.Triple:
                        // 既に3分割されている場合は、現在アクティブなペインで開く
                        ShowBookmarkWebView(item, _activePane);
                        break;
                }
            }
            e.Handled = true;
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

        private async void Bookmark_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(BookmarkDataFormat))
            {
                var droppedData = e.Data.GetData(BookmarkDataFormat) as BookmarkItem;
                var targetData = (sender as FrameworkElement)?.DataContext as BookmarkItem;

                if (droppedData != null && targetData != null && droppedData != targetData)
                {
                    try
                    {
                        await _bookmarkService.MoveBookmarkAsync(droppedData, targetData);
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"ブックマークの移動に失敗しました: {ex.Message}", "エラー");
                    }
                }
            }
            e.Handled = true;
        }

        private async void ShowBookmarkWebView(BookmarkItem item, TargetWindow target)
        {
            Microsoft.Web.WebView2.Wpf.WebView2 targetWebView;

            // 1. WebViewの取得または生成
            if (_bookmarkWebViews.TryGetValue(item, out var cachedWebView))
            {
                targetWebView = cachedWebView;
            }
            else
            {
                targetWebView = CreateNewWebView();
                _bookmarkWebViews[item] = targetWebView;
                item.IsLoaded = true;

                // 先に目的のコンテナに追加する
                switch (target)
                {
                    case TargetWindow.Top:
                        if (!WebViewTopHolder.Children.Contains(targetWebView))
                            WebViewTopHolder.Children.Add(targetWebView);
                        break;
                    case TargetWindow.Middle:
                        if (!WebViewMiddleHolder.Children.Contains(targetWebView))
                            WebViewMiddleHolder.Children.Add(targetWebView);
                        break;
                    case TargetWindow.Bottom:
                        if (!WebViewBottomHolder.Children.Contains(targetWebView))
                            WebViewBottomHolder.Children.Add(targetWebView);
                        break;
                }

                // 初期化とロード
                await targetWebView.EnsureCoreWebView2Async();
                if (_defaultUserAgent == null)
                {
                    _defaultUserAgent = targetWebView.CoreWebView2.Settings.UserAgent;
                }
                if (_defaultUserAgent != null)
                {
                    targetWebView.CoreWebView2.Settings.UserAgent = _isMobileMode ? MobileUserAgent : _defaultUserAgent;
                }
                targetWebView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
                targetWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                targetWebView.CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;
                targetWebView.Source = new Uri(item.Url);
            }

            // 2. 引っ越し処理と親の制御
            // 他のコンテナにあれば取り外す
            if (target != TargetWindow.Top && WebViewTopHolder.Children.Contains(targetWebView))
            {
                WebViewTopHolder.Children.Remove(targetWebView);
                ResetToDefaultWebView();
            }
            if (target != TargetWindow.Middle && WebViewMiddleHolder.Children.Contains(targetWebView))
            {
                WebViewMiddleHolder.Children.Remove(targetWebView);
                ResetToDefaultMiddleWebView();
            }
            if (target != TargetWindow.Bottom && WebViewBottomHolder.Children.Contains(targetWebView))
            {
                WebViewBottomHolder.Children.Remove(targetWebView);
                ResetToDefaultBottomWebView();
            }

            // 目的地コンテナへの追加
            System.Windows.Controls.Panel destinationContainer = target switch
            {
                TargetWindow.Top => WebViewTopHolder,
                TargetWindow.Middle => WebViewMiddleHolder,
                TargetWindow.Bottom => WebViewBottomHolder,
                _ => throw new ArgumentOutOfRangeException(nameof(target))
            };

            if (!destinationContainer.Children.Contains(targetWebView))
            {
                destinationContainer.Children.Add(targetWebView);
            }

            // 目的地コンテナ内の表示制御（対象のみを Visible、他を Collapsed）
            // プレースホルダーの制御とアクティブブックマークの更新
            if (target == TargetWindow.Top)
            {
                TopSleepPlaceholder.Visibility = Visibility.Collapsed;
                _activeBookmarkTop = item;
                _activePane = TargetWindow.Top;
            }
            else if (target == TargetWindow.Middle)
            {
                MiddleSleepPlaceholder.Visibility = Visibility.Collapsed;
                MiddleEmptyPlaceholder.Visibility = Visibility.Collapsed;
                _activeBookmarkMiddle = item;
                _activePane = TargetWindow.Middle;
            }
            else if (target == TargetWindow.Bottom)
            {
                BottomSleepPlaceholder.Visibility = Visibility.Collapsed;
                BottomEmptyPlaceholder.Visibility = Visibility.Collapsed;
                _activeBookmarkBottom = item;
                _activePane = TargetWindow.Bottom;
            }

            foreach (var kvp in _bookmarkWebViews)
            {
                if (destinationContainer.Children.Contains(kvp.Value))
                {
                    kvp.Value.Visibility = (kvp.Key == item) ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            _activeWebView = targetWebView;
            UrlTextBox.Text = _activeWebView.Source?.ToString() ?? "";
            UpdateBookmarkActiveState();
        }

        private void ResetToDefaultMiddleWebView()
        {
            _activeBookmarkMiddle = null;
            if (_activePane == TargetWindow.Middle) _activeWebView = null;
            MiddleSleepPlaceholder.Visibility = Visibility.Collapsed;
            MiddleEmptyPlaceholder.Visibility = Visibility.Visible;
            UpdateBookmarkActiveState();
        }

        private void UpdateBookmarkActiveState()
        {
            if (_bookmarkService == null || _bookmarkService.Bookmarks == null) return;

            var activeB = GetActiveBookmarkForPane(_activePane);
            foreach (var b in _bookmarkService.Bookmarks)
            {
                b.IsActive = (b == activeB);
            }
            UpdatePaneHeadersUI();
            UpdateNavigationButtonsState();
        }

        private void CoreWebView2_HistoryChanged(object? sender, object e)
        {
            UpdateNavigationButtonsState();
        }

        private void UpdateNavigationButtonsState()
        {
            UpdatePaneNavigationButtons(TargetWindow.Top, _activeBookmarkTop, TopBackButton, TopForwardButton);
            UpdatePaneNavigationButtons(TargetWindow.Middle, _activeBookmarkMiddle, MiddleBackButton, MiddleForwardButton);
            UpdatePaneNavigationButtons(TargetWindow.Bottom, _activeBookmarkBottom, BottomBackButton, BottomForwardButton);
        }

        private void UpdatePaneNavigationButtons(TargetWindow pane, BookmarkItem? activeBookmark, System.Windows.Controls.Button backButton, System.Windows.Controls.Button forwardButton)
        {
            if (backButton == null || forwardButton == null) return;

            if (activeBookmark != null && _bookmarkWebViews.TryGetValue(activeBookmark, out var wv) && wv.CoreWebView2 != null)
            {
                backButton.IsEnabled = wv.CoreWebView2.CanGoBack;
                forwardButton.IsEnabled = wv.CoreWebView2.CanGoForward;
            }
            else
            {
                backButton.IsEnabled = false;
                forwardButton.IsEnabled = false;
            }
        }

        private void UpdatePaneHeadersUI()
        {
            if (TopHeader == null || TopActiveIndicator == null || TopHeaderText == null) return;
            if (MiddleHeader == null || MiddleActiveIndicator == null || MiddleHeaderText == null) return;
            if (BottomHeader == null || BottomActiveIndicator == null || BottomHeaderText == null) return;

            // テーマ色ブラシの取得（見つからない場合は標準色をフォールバック）
            var activeBg = (System.Windows.Media.Brush)TryFindResource("HoverBackground") ?? System.Windows.Media.Brushes.DimGray;
            var inactiveBg = (System.Windows.Media.Brush)TryFindResource("TitleBarBackground") ?? System.Windows.Media.Brushes.Transparent;
            var activeText = (System.Windows.Media.Brush)TryFindResource("PrimaryText") ?? System.Windows.Media.Brushes.White;
            var inactiveText = (System.Windows.Media.Brush)TryFindResource("SecondaryText") ?? System.Windows.Media.Brushes.Gray;

            // メイン（上）ペインの更新
            bool isTopActive = (_activePane == TargetWindow.Top);
            TopActiveIndicator.Visibility = isTopActive ? Visibility.Visible : Visibility.Collapsed;
            TopHeaderText.Foreground = isTopActive ? activeText : inactiveText;
            TopHeaderText.FontWeight = isTopActive ? FontWeights.SemiBold : FontWeights.Normal;
            TopHeader.Background = isTopActive ? activeBg : inactiveBg;
            TopHeaderText.Text = string.IsNullOrEmpty(_activeBookmarkTop?.Title) ? "メイン画面" : _activeBookmarkTop.Title;

            // 中ペインの更新
            bool isMiddleActive = (_activePane == TargetWindow.Middle);
            MiddleActiveIndicator.Visibility = isMiddleActive ? Visibility.Visible : Visibility.Collapsed;
            MiddleHeaderText.Foreground = isMiddleActive ? activeText : inactiveText;
            MiddleHeaderText.FontWeight = isMiddleActive ? FontWeights.SemiBold : FontWeights.Normal;
            MiddleHeader.Background = isMiddleActive ? activeBg : inactiveBg;
            MiddleHeaderText.Text = string.IsNullOrEmpty(_activeBookmarkMiddle?.Title) ? "サブ画面 (中)" : _activeBookmarkMiddle.Title;

            // 下ペインの更新
            bool isBottomActive = (_activePane == TargetWindow.Bottom);
            BottomActiveIndicator.Visibility = isBottomActive ? Visibility.Visible : Visibility.Collapsed;
            BottomHeaderText.Foreground = isBottomActive ? activeText : inactiveText;
            BottomHeaderText.FontWeight = isBottomActive ? FontWeights.SemiBold : FontWeights.Normal;
            BottomHeader.Background = isBottomActive ? activeBg : inactiveBg;
            BottomHeaderText.Text = string.IsNullOrEmpty(_activeBookmarkBottom?.Title) ? "サブ画面 (下)" : _activeBookmarkBottom.Title;
        }

        private void ResetToDefaultBottomWebView()
        {
            _activeBookmarkBottom = null;
            if (_activePane == TargetWindow.Bottom) _activeWebView = null;
            BottomSleepPlaceholder.Visibility = Visibility.Collapsed;
            BottomEmptyPlaceholder.Visibility = Visibility.Visible;
            UpdateBookmarkActiveState();
        }

        private void OpenInMiddleWindow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is BookmarkItem item)
            {
                OpenBookmarkInMiddleWindow(item);
            }
        }

        private void OpenInSplitScreen_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is BookmarkItem item)
            {
                OpenBookmarkInSplitScreen(item);
            }
        }

        private void BookmarkContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.ContextMenu menu && menu.DataContext is BookmarkItem item)
            {
                bool isOpen = (_activeBookmarkTop == item) ||
                              (_isMiddlePaneOpen && _activeBookmarkMiddle == item) ||
                              (_isBottomPaneOpen && _activeBookmarkBottom == item);

                foreach (var mItem in menu.Items)
                {
                    if (mItem is System.Windows.Controls.MenuItem menuItem)
                    {
                        if (menuItem.Header?.ToString() == "分割画面で開く")
                        {
                            menuItem.IsEnabled = (_currentSplitMode != SplitMode.Triple) && !isOpen;
                        }
                        else if (menuItem.Header?.ToString() == "タブを終了")
                        {
                            menuItem.IsEnabled = item.IsLoaded;
                        }
                    }
                }
            }
        }

        private void PageToolsMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.ContextMenu menu)
            {
                var activeBookmark = GetActiveBookmarkForPane(_activePane);

                // 「初期ページに戻る」メニューの有効/無効化
                System.Windows.Controls.MenuItem? resetItem = menu.Items.OfType<System.Windows.Controls.MenuItem>().FirstOrDefault(item => item.Tag?.ToString() == "ResetToInitialPage");
                if (resetItem != null)
                {
                    resetItem.IsEnabled = (activeBookmark != null);
                }

                // 「モバイル表示に切替」のヘッダーとアイコンの動的更新
                System.Windows.Controls.MenuItem? uaItem = menu.Items.OfType<System.Windows.Controls.MenuItem>().FirstOrDefault(item => item.Tag?.ToString() == "UserAgent");
                if (uaItem != null)
                {
                    uaItem.Header = _isMobileMode ? "デスクトップ表示に切替" : "モバイル表示に切替";
                    if (uaItem.Icon is TextBlock iconText)
                    {
                        iconText.Text = _isMobileMode ? "" : "";
                    }
                }

                // 「画面を閉じる」メニューの有効/無効化（1画面のときはグレーアウト）
                System.Windows.Controls.MenuItem? closeItem = menu.Items.OfType<System.Windows.Controls.MenuItem>().FirstOrDefault(item => item.Tag?.ToString() == "CloseActivePane");
                if (closeItem != null)
                {
                    closeItem.IsEnabled = (_currentSplitMode != SplitMode.Single);
                }
            }
        }

        private void ResetToInitialPage_Click(object sender, RoutedEventArgs e)
        {
            var activeBookmark = GetActiveBookmarkForPane(_activePane);
            if (activeBookmark != null)
            {
                // 初期URLに戻す
                if (_bookmarkWebViews.TryGetValue(activeBookmark, out var webView))
                {
                    webView.Source = new Uri(activeBookmark.Url);
                }
            }
        }

        private bool TryActivatePaneWithBookmark(BookmarkItem item)
        {
            if (_activeBookmarkTop == item)
            {
                ShowBookmarkWebView(item, TargetWindow.Top);
                return true;
            }
            if (_isMiddlePaneOpen && _activeBookmarkMiddle == item)
            {
                ShowBookmarkWebView(item, TargetWindow.Middle);
                return true;
            }
            if (_isBottomPaneOpen && _activeBookmarkBottom == item)
            {
                ShowBookmarkWebView(item, TargetWindow.Bottom);
                return true;
            }
            return false;
        }

        private void OpenBookmarkInSplitScreen(BookmarkItem item)
        {
            if (TryActivatePaneWithBookmark(item))
            {
                return;
            }

            switch (_currentSplitMode)
            {
                case SplitMode.Single:
                    OpenBookmarkInBottomWindow(item);
                    break;
                case SplitMode.Double:
                    OpenBookmarkInMiddleWindow(item);
                    break;
                case SplitMode.Triple:
                    ShowBookmarkWebView(item, _activePane);
                    break;
            }
        }

        private void OpenBookmarkInMiddleWindow(BookmarkItem item)
        {
            _isMiddlePaneOpen = true;
            ApplySplitLayout();
            ShowBookmarkWebView(item, TargetWindow.Middle);
        }

        private void OpenInBottomWindow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is BookmarkItem item)
            {
                OpenBookmarkInBottomWindow(item);
            }
        }

        private void OpenBookmarkInBottomWindow(BookmarkItem item)
        {
            _isBottomPaneOpen = true;
            ApplySplitLayout();
            ShowBookmarkWebView(item, TargetWindow.Bottom);
        }

        private void PaneHeader_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isHeaderMouseDown && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed &&
                sender is FrameworkElement element && element.Tag is string dragSource)
            {
                System.Windows.Point mousePos = e.GetPosition(this);
                Vector diff = _headerDragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isHeaderMouseDown = false;
                    // ドラッグ開始
                    DragDrop.DoDragDrop(element, dragSource, WinDragDropEffects.Move);
                }
            }
            else if (e.LeftButton == System.Windows.Input.MouseButtonState.Released)
            {
                _isHeaderMouseDown = false;
            }
        }

        private void PaneHeader_DragOver(object sender, WinDragEventArgs e)
        {
            if (e.Data.GetDataPresent(WinDataFormats.StringFormat) &&
                sender is Border targetBorder && targetBorder.Tag is string dragTarget)
            {
                string? dragSource = e.Data.GetData(WinDataFormats.StringFormat) as string;
                if (dragSource != null && dragSource != dragTarget)
                {
                    e.Effects = WinDragDropEffects.Move;
                    // ハイライト表示
                    targetBorder.Background = (System.Windows.Media.Brush)FindResource("SelectionBackground");
                    e.Handled = true;
                    return;
                }
            }
            e.Effects = WinDragDropEffects.None;
            e.Handled = true;
        }

        private void PaneHeader_DragLeave(object sender, WinDragEventArgs e)
        {
            if (sender is Border targetBorder)
            {
                targetBorder.Background = (System.Windows.Media.Brush)FindResource("TitleBarBackground");
            }
        }

        private void PaneHeader_Drop(object sender, WinDragEventArgs e)
        {
            if (sender is Border targetBorder && targetBorder.Tag is string dragTarget)
            {
                targetBorder.Background = (System.Windows.Media.Brush)FindResource("TitleBarBackground");

                if (e.Data.GetDataPresent(WinDataFormats.StringFormat))
                {
                    string? dragSource = e.Data.GetData(WinDataFormats.StringFormat) as string;
                    if (dragSource != null && dragSource != dragTarget)
                    {
                        SwapPanes(dragSource, dragTarget);
                    }
                }
            }
            e.Handled = true;
        }

        private TargetWindow ParseTargetWindow(string name)
        {
            return name switch
            {
                "Top" => TargetWindow.Top,
                "Middle" => TargetWindow.Middle,
                "Bottom" => TargetWindow.Bottom,
                _ => throw new ArgumentException($"Invalid pane name: {name}")
            };
        }

        private void RemoveWebViewFromParent(Microsoft.Web.WebView2.Wpf.WebView2 wv, TargetWindow pane)
        {
            switch (pane)
            {
                case TargetWindow.Top:
                    if (WebViewTopHolder.Children.Contains(wv))
                        WebViewTopHolder.Children.Remove(wv);
                    break;
                case TargetWindow.Middle:
                    if (WebViewMiddleHolder.Children.Contains(wv))
                        WebViewMiddleHolder.Children.Remove(wv);
                    break;
                case TargetWindow.Bottom:
                    if (WebViewBottomHolder.Children.Contains(wv))
                        WebViewBottomHolder.Children.Remove(wv);
                    break;
            }
        }

        private void SetActiveBookmarkForPane(TargetWindow pane, BookmarkItem? item)
        {
            switch (pane)
            {
                case TargetWindow.Top:
                    _activeBookmarkTop = item;
                    break;
                case TargetWindow.Middle:
                    _activeBookmarkMiddle = item;
                    break;
                case TargetWindow.Bottom:
                    _activeBookmarkBottom = item;
                    break;
            }
        }

        private void ResetPaneToDefault(TargetWindow pane)
        {
            switch (pane)
            {
                case TargetWindow.Top:
                    ResetToDefaultWebView();
                    break;
                case TargetWindow.Middle:
                    ResetToDefaultMiddleWebView();
                    break;
                case TargetWindow.Bottom:
                    ResetToDefaultBottomWebView();
                    break;
            }
        }

        private System.Windows.Controls.Panel GetContainerForPane(TargetWindow pane) => pane switch
        {
            TargetWindow.Top => WebViewTopHolder,
            TargetWindow.Middle => WebViewMiddleHolder,
            TargetWindow.Bottom => WebViewBottomHolder,
            _ => throw new ArgumentOutOfRangeException(nameof(pane), pane, null)
        };

        private void SwapPanes(string source, string target)
        {
            TargetWindow sourceWindow = ParseTargetWindow(source);
            TargetWindow targetWindow = ParseTargetWindow(target);

            BookmarkItem? sourceBookmark = GetActiveBookmarkForPane(sourceWindow);
            BookmarkItem? targetBookmark = GetActiveBookmarkForPane(targetWindow);

            Microsoft.Web.WebView2.Wpf.WebView2? sourceWv = sourceBookmark != null && _bookmarkWebViews.TryGetValue(sourceBookmark, out var sWv) ? sWv : null;
            Microsoft.Web.WebView2.Wpf.WebView2? targetWv = targetBookmark != null && _bookmarkWebViews.TryGetValue(targetBookmark, out var tWv) ? tWv : null;

            // 1. 親コンテナから取り外す
            if (sourceWv != null)
            {
                RemoveWebViewFromParent(sourceWv, sourceWindow);
            }
            if (targetWv != null)
            {
                RemoveWebViewFromParent(targetWv, targetWindow);
            }

            // Bookmarks assignments are updated first to ensure proper exclusion filtering in default reset methods.
            SetActiveBookmarkForPane(sourceWindow, targetBookmark);
            SetActiveBookmarkForPane(targetWindow, sourceBookmark);

            // 2. 移動先に配置
            if (sourceBookmark != null)
            {
                var destContainer = GetContainerForPane(targetWindow);

                if (sourceWv != null)
                {
                    if (!destContainer.Children.Contains(sourceWv))
                        destContainer.Children.Add(sourceWv);
                    sourceWv.Visibility = Visibility.Visible;

                    switch (targetWindow)
                    {
                        case TargetWindow.Top:
                            TopSleepPlaceholder.Visibility = Visibility.Collapsed;
                            break;
                        case TargetWindow.Middle:
                            MiddleSleepPlaceholder.Visibility = Visibility.Collapsed;
                            MiddleEmptyPlaceholder.Visibility = Visibility.Collapsed;
                            break;
                        case TargetWindow.Bottom:
                            BottomSleepPlaceholder.Visibility = Visibility.Collapsed;
                            BottomEmptyPlaceholder.Visibility = Visibility.Collapsed;
                            break;
                    }
                }
                else
                {
                    // スリープ中のブックマークを移動する場合、移動先にスリープ画面を表示
                    switch (targetWindow)
                    {
                        case TargetWindow.Top:
                            TopSleepTitle.Text = $"「{sourceBookmark.Title}」はスリープ状態です";
                            TopSleepPlaceholder.Visibility = Visibility.Visible;
                            break;
                        case TargetWindow.Middle:
                            MiddleSleepTitle.Text = $"「{sourceBookmark.Title}」はスリープ状態です";
                            MiddleSleepPlaceholder.Visibility = Visibility.Visible;
                            MiddleEmptyPlaceholder.Visibility = Visibility.Collapsed;
                            break;
                        case TargetWindow.Bottom:
                            BottomSleepTitle.Text = $"「{sourceBookmark.Title}」はスリープ状態です";
                            BottomSleepPlaceholder.Visibility = Visibility.Visible;
                            BottomEmptyPlaceholder.Visibility = Visibility.Collapsed;
                            break;
                    }
                }
            }
            else
            {
                ResetPaneToDefault(targetWindow);
            }

            if (targetBookmark != null)
            {
                var destContainer = GetContainerForPane(sourceWindow);

                if (targetWv != null)
                {
                    if (!destContainer.Children.Contains(targetWv))
                        destContainer.Children.Add(targetWv);
                    targetWv.Visibility = Visibility.Visible;

                    switch (sourceWindow)
                    {
                        case TargetWindow.Top:
                            TopSleepPlaceholder.Visibility = Visibility.Collapsed;
                            break;
                        case TargetWindow.Middle:
                            MiddleSleepPlaceholder.Visibility = Visibility.Collapsed;
                            MiddleEmptyPlaceholder.Visibility = Visibility.Collapsed;
                            break;
                        case TargetWindow.Bottom:
                            BottomSleepPlaceholder.Visibility = Visibility.Collapsed;
                            BottomEmptyPlaceholder.Visibility = Visibility.Collapsed;
                            break;
                    }
                }
                else
                {
                    // スリープ中のブックマークを移動する場合、移動先にスリープ画面を表示
                    switch (sourceWindow)
                    {
                        case TargetWindow.Top:
                            TopSleepTitle.Text = $"「{targetBookmark.Title}」はスリープ状態です";
                            TopSleepPlaceholder.Visibility = Visibility.Visible;
                            break;
                        case TargetWindow.Middle:
                            MiddleSleepTitle.Text = $"「{targetBookmark.Title}」はスリープ状態です";
                            MiddleSleepPlaceholder.Visibility = Visibility.Visible;
                            MiddleEmptyPlaceholder.Visibility = Visibility.Collapsed;
                            break;
                        case TargetWindow.Bottom:
                            BottomSleepTitle.Text = $"「{targetBookmark.Title}」はスリープ状態です";
                            BottomSleepPlaceholder.Visibility = Visibility.Visible;
                            BottomEmptyPlaceholder.Visibility = Visibility.Collapsed;
                            break;
                    }
                }
            }
            else
            {
                ResetPaneToDefault(sourceWindow);
            }

            // 3. アクティブWebViewの更新
            if (_activePane == sourceWindow)
            {
                _activeWebView = targetWv;
            }
            else if (_activePane == targetWindow)
            {
                _activeWebView = sourceWv;
            }

            UpdateActiveWebViewAfterSplitChange();
        }

        private void ActivatePaneByName(string tag)
        {
            switch (tag)
            {
                case "Top":
                    _activePane = TargetWindow.Top;
                    break;
                case "Middle":
                    if (_isMiddlePaneOpen) _activePane = TargetWindow.Middle;
                    break;
                case "Bottom":
                    if (_isBottomPaneOpen) _activePane = TargetWindow.Bottom;
                    break;
            }

            // アクティブペイン内の表示中WebViewを_activeWebViewに設定
            var visibleWv = _activePane switch
            {
                TargetWindow.Top => WebViewTopHolder.Children.OfType<Microsoft.Web.WebView2.Wpf.WebView2>().FirstOrDefault(w => w.Visibility == Visibility.Visible),
                TargetWindow.Middle => WebViewMiddleHolder.Children.OfType<Microsoft.Web.WebView2.Wpf.WebView2>().FirstOrDefault(w => w.Visibility == Visibility.Visible),
                TargetWindow.Bottom => WebViewBottomHolder.Children.OfType<Microsoft.Web.WebView2.Wpf.WebView2>().FirstOrDefault(w => w.Visibility == Visibility.Visible),
                _ => null
            };

            _activeWebView = visibleWv;
            if (_activeWebView != null && _activeWebView.Source != null)
            {
                UrlTextBox.Text = _activeWebView.Source.ToString();
            }
            else
            {
                var activeB = GetActiveBookmarkForPane(_activePane);
                UrlTextBox.Text = activeB?.Url ?? "";
            }

            UpdateBookmarkActiveState();
        }

        private void PaneHeader_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                _headerDragStartPoint = e.GetPosition(this);
                _isHeaderMouseDown = true;
            }

            if (sender is FrameworkElement element && element.Tag is string tag)
            {
                ActivatePaneByName(tag);
            }
        }

        private void HeaderPageTools_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is string tag)
            {
                ActivatePaneByName(tag);

                var menu = this.Resources["PageToolsMenu"] as System.Windows.Controls.ContextMenu;
                if (menu != null)
                {
                    menu.PlacementTarget = button;
                    menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                    menu.IsOpen = true;
                }
                e.Handled = true;
            }
        }

        private void CloseMiddlePane_Click(object sender, RoutedEventArgs e)
        {
            CloseMiddlePane();
            e.Handled = true;
        }

        private void CloseMiddlePane()
        {
            _isMiddlePaneOpen = false;
            _activeBookmarkMiddle = null;
            WebViewMiddleHolder.Children.Clear(); // WebViewをコンテナからクリアして解放
            ApplySplitLayout();
            if (_activePane == TargetWindow.Middle)
            {
                _activePane = TargetWindow.Top;
            }
            UpdateActiveWebViewAfterSplitChange();
        }

        private void CloseBottomPane_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomPane();
            e.Handled = true;
        }

        private void CloseBottomPane()
        {
            _isBottomPaneOpen = false;
            _activeBookmarkBottom = null;
            WebViewBottomHolder.Children.Clear(); // WebViewをコンテナからクリアして解放
            ApplySplitLayout();
            if (_activePane == TargetWindow.Bottom)
            {
                _activePane = TargetWindow.Top;
            }
            UpdateActiveWebViewAfterSplitChange();
        }

        private void CloseTopPane()
        {
            if (_isMiddlePaneOpen && _isBottomPaneOpen)
            {
                var midBookmark = _activeBookmarkMiddle;
                var botBookmark = _activeBookmarkBottom;

                // Middleの中身をTopに移動
                if (midBookmark != null)
                {
                    ShowBookmarkWebView(midBookmark, TargetWindow.Top);
                }
                else
                {
                    ResetToDefaultWebView();
                }

                // Bottomの中身をMiddleに移動
                if (botBookmark != null)
                {
                    ShowBookmarkWebView(botBookmark, TargetWindow.Middle);
                }
                else
                {
                    ResetToDefaultMiddleWebView();
                }

                // Bottomを閉じる
                _isBottomPaneOpen = false;
                _activeBookmarkBottom = null;
                WebViewBottomHolder.Children.Clear();

                _activePane = TargetWindow.Top;
            }
            else if (_isMiddlePaneOpen)
            {
                var midBookmark = _activeBookmarkMiddle;

                if (midBookmark != null)
                {
                    ShowBookmarkWebView(midBookmark, TargetWindow.Top);
                }
                else
                {
                    ResetToDefaultWebView();
                }

                _isMiddlePaneOpen = false;
                _activeBookmarkMiddle = null;
                WebViewMiddleHolder.Children.Clear();

                _activePane = TargetWindow.Top;
            }
            else if (_isBottomPaneOpen)
            {
                var botBookmark = _activeBookmarkBottom;

                if (botBookmark != null)
                {
                    ShowBookmarkWebView(botBookmark, TargetWindow.Top);
                }
                else
                {
                    ResetToDefaultWebView();
                }

                _isBottomPaneOpen = false;
                _activeBookmarkBottom = null;
                WebViewBottomHolder.Children.Clear();

                _activePane = TargetWindow.Top;
            }

            ApplySplitLayout();
            UpdateActiveWebViewAfterSplitChange();
        }

        private void CloseActivePane_Click(object sender, RoutedEventArgs e)
        {
            switch (_activePane)
            {
                case TargetWindow.Top:
                    CloseTopPane();
                    break;
                case TargetWindow.Middle:
                    CloseMiddlePane();
                    break;
                case TargetWindow.Bottom:
                    CloseBottomPane();
                    break;
            }
            e.Handled = true;
        }

        private async void DeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is BookmarkItem item)
            {
                var result = System.Windows.MessageBox.Show(
                    $"「{item.Title}」を削除しますか？",
                    "削除の確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;

                DisposeBookmarkWebView(item);
                try
                {
                    await _bookmarkService.RemoveBookmarkAsync(item);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"ブックマークの削除に失敗しました: {ex.Message}", "エラー");
                }

                // 削除対象のブックマークがアクティブな場合は参照をクリアし適切なプレースホルダー表示に戻す
                if (_activeBookmarkTop == item)
                {
                    _activeBookmarkTop = null;
                    TopSleepPlaceholder.Visibility = Visibility.Collapsed;
                    ResetToDefaultWebView();
                }
                if (_activeBookmarkMiddle == item)
                {
                    _activeBookmarkMiddle = null;
                    MiddleSleepPlaceholder.Visibility = Visibility.Collapsed;
                    MiddleEmptyPlaceholder.Visibility = Visibility.Visible;
                }
                if (_activeBookmarkBottom == item)
                {
                    _activeBookmarkBottom = null;
                    BottomSleepPlaceholder.Visibility = Visibility.Collapsed;
                    BottomEmptyPlaceholder.Visibility = Visibility.Visible;
                }
            }
        }

        private void ClearBookmarkState_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is BookmarkItem item)
            {
                DisposeBookmarkWebView(item);

                // Topペインは常時表示のため閉じない（スリープ画面が表示される）
                // メモリ解放したお気に入りが現在分割画面に表示されている場合、そのペインを閉じる
                bool layoutChanged = false;
                if (_activeBookmarkMiddle == item && _isMiddlePaneOpen)
                {
                    _isMiddlePaneOpen = false;
                    _activeBookmarkMiddle = null;
                    WebViewMiddleHolder.Children.Clear();
                    if (_activePane == TargetWindow.Middle) _activePane = TargetWindow.Top;
                    layoutChanged = true;
                }
                if (_activeBookmarkBottom == item && _isBottomPaneOpen)
                {
                    _isBottomPaneOpen = false;
                    _activeBookmarkBottom = null;
                    WebViewBottomHolder.Children.Clear();
                    if (_activePane == TargetWindow.Bottom) _activePane = TargetWindow.Top;
                    layoutChanged = true;
                }

                if (layoutChanged)
                {
                    ApplySplitLayout();
                    UpdateActiveWebViewAfterSplitChange();
                }
            }
        }

        private void DisposeBookmarkWebView(BookmarkItem item)
        {
            if (_bookmarkWebViews.TryGetValue(item, out var wv))
            {
                // すべてのコンテナから確実に削除
                WebViewTopHolder.Children.Remove(wv);
                WebViewMiddleHolder.Children.Remove(wv);
                WebViewBottomHolder.Children.Remove(wv);
                
                try 
                {
                    wv.GotFocus -= WebView_GotFocus;
                    if (wv.CoreWebView2 != null)
                    {
                        wv.CoreWebView2.SourceChanged -= CoreWebView2_SourceChanged;
                        wv.CoreWebView2.HistoryChanged -= CoreWebView2_HistoryChanged;
                        wv.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
                    }
                    wv.Dispose(); 
                } 
                catch { }
                _bookmarkWebViews.Remove(item);
            }

            // WebViewインスタンスが存在するかどうかに関わらず、常にロード状態を解除する
            item.IsLoaded = false;
            
            // もし破棄するブックマークがアクティブだったら対応するペインにスリープ画面を表示
            if (_activeBookmarkTop == item)
            {
                TopSleepTitle.Text = $"「{item.Title}」はスリープ状態です";
                TopSleepPlaceholder.Visibility = Visibility.Visible;
                if (_activePane == TargetWindow.Top) _activeWebView = null;
            }
            if (_activeBookmarkMiddle == item)
            {
                MiddleSleepTitle.Text = $"「{item.Title}」はスリープ状態です";
                MiddleSleepPlaceholder.Visibility = Visibility.Visible;
                MiddleEmptyPlaceholder.Visibility = Visibility.Collapsed;
                if (_activePane == TargetWindow.Middle) _activeWebView = null;
            }
            if (_activeBookmarkBottom == item)
            {
                BottomSleepTitle.Text = $"「{item.Title}」はスリープ状態です";
                BottomSleepPlaceholder.Visibility = Visibility.Visible;
                BottomEmptyPlaceholder.Visibility = Visibility.Collapsed;
                if (_activePane == TargetWindow.Bottom) _activeWebView = null;
            }

            UpdateBookmarkActiveState();
        }

        private void ResetToDefaultWebView()
        {
            var candidate = _bookmarkService.Bookmarks.FirstOrDefault(b => !b.IsSeparator && b != _activeBookmarkMiddle && b != _activeBookmarkBottom);
            if (candidate != null)
            {
                ShowBookmarkWebView(candidate, TargetWindow.Top);
            }
            else
            {
                _activeBookmarkTop = null;
                if (_activePane == TargetWindow.Top) _activeWebView = null;
                TopSleepPlaceholder.Visibility = Visibility.Collapsed;
                // 万が一お気に入りが他にない場合は、最初の項目を上部で開く
                var first = _bookmarkService.Bookmarks.FirstOrDefault(b => !b.IsSeparator);
                if (first != null)
                {
                    ShowBookmarkWebView(first, TargetWindow.Top);
                }
            }
            UpdateBookmarkActiveState();
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

            var activeB = GetActiveBookmarkForPane(_activePane);
            if (_activeWebView == null && activeB != null)
            {
                ShowBookmarkWebView(activeB, _activePane);
                if (_activeWebView != null)
                {
                    _activeWebView.Source = new Uri(url);
                }
            }
            else if (_activeWebView != null)
            {
                _activeWebView.Source = new Uri(url);
            }
        }

        // --- システムトレイ機能 ---
        private void InitializeNotifyIcon()
        {
            try
            {
                var contextMenu = new System.Windows.Forms.ContextMenuStrip();

                var exitItem = new System.Windows.Forms.ToolStripMenuItem("終了");
                exitItem.Click += (s, e) => {
                    this.Close();
                };
                contextMenu.Items.Add(exitItem);

                string exePath = System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";
                System.Drawing.Icon? appIcon = null;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    try { appIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath); } catch { }
                }

                _notifyIcon = new System.Windows.Forms.NotifyIcon
                {
                    Text = "MugiSideBrowser",
                    Icon = appIcon ?? System.Drawing.SystemIcons.Application,
                    ContextMenuStrip = contextMenu,
                    Visible = true
                };

                _notifyIcon.DoubleClick += (s, e) => {
                    ShowFromTray();
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NotifyIcon Init Error: {ex.Message}");
            }
        }

        private void ShowFromTray()
        {
            this.WindowState = WindowState.Normal;
            this.Show();
            this.Activate();
        }
    }
}