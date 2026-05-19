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

namespace MugiSideBrowser
{
    public partial class MainWindow : Window
    {
        private AppBarHelper _appBarHelper;
        private BookmarkService _bookmarkService;
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private bool _isExitingFromTray = false;
        private System.Windows.Point _dragStartPoint;
        private const string BookmarkDataFormat = "MugiSideBrowser.BookmarkItem";
        private Microsoft.Web.WebView2.Wpf.WebView2 _activeWebView;
        private bool _isBottomInitialized = false;
        private bool _isMobileMode = false;
        private string? _defaultUserAgent = null;
        private bool _useExternalBrowserOnCtrlClick = true;
        private const string MobileUserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 15_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/15.0 Mobile/15E148 Safari/604.1";
        private bool _isFakeMinimized = false;
        private double _savedNormalLeft;
        private double _savedNormalTop;
        private double _resizeStartHeight;
        private System.Threading.Mutex? _appBarLockMutex;
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

            _bookmarkService = new BookmarkService();
            BookmarkList.ItemsSource = _bookmarkService.Bookmarks;
            InitializeBookmarksAsync();
            InitializeNotifyIcon();
        }

        private async void InitializeBookmarksAsync()
        {
            await _bookmarkService.InitializeAsync();
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
                
                AlwaysVisibleMenuItem.IsChecked = true;
                AutoHideMenuItem.IsChecked = false;
                NormalWindowMenuItem.IsChecked = false;
                this.ShowInTaskbar = false;

                _appBarHelper.Register();
                UpdateWindowTitle();
            }
            else
            {
                // 2番目以降：通常ウィンドウとして起動
                SetToNormalMode();
            }
        }

        private void SetToNormalMode()
        {
            _currentMode = DisplayMode.Normal;
            AlwaysVisibleMenuItem.IsChecked = false;
            AutoHideMenuItem.IsChecked = false;
            NormalWindowMenuItem.IsChecked = true;
            
            _appBarHelper.Unregister();
            ReleaseAppBarLock();
            StopAutoHideTimer();
            
            this.Topmost = false;
            this.ShowInTaskbar = true;
            LeftResizeColumn.Width = new GridLength(4);
            RightResizeColumn.Width = new GridLength(4);
            BottomResizeRow.Height = new GridLength(4);
            
            UpdateWindowTitle();
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

                // AppBarの利用権（鍵）が「自分にある」か「誰にもない」かを確認
                bool isAppBarAvailable = false;
                if (_appBarLockMutex != null)
                {
                    isAppBarAvailable = true; // 自分が持っている
                }
                else
                {
                    try
                    {
                        var mutex = new System.Threading.Mutex(false, "Global\\MugiSideBrowser_AppBarGlobalLock");
                        if (mutex.WaitOne(0))
                        {
                            isAppBarAvailable = true; // 誰も持っていない（すぐ手放す）
                            mutex.ReleaseMutex();
                        }
                        mutex.Dispose();
                    }
                    catch { }
                }

                // 他の誰かが使っている場合はグレーアウト
                AlwaysVisibleMenuItem.IsEnabled = isAppBarAvailable;
                AutoHideMenuItem.IsEnabled = isAppBarAvailable;

                element.ContextMenu.IsOpen = true;
            }
        }

        private void PageTools_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
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
                if (!TryAcquireAppBarLock())
                {
                    // 鍵が取れなければ切り替えを阻止
                    NormalWindowMenuItem.IsChecked = true;
                    AlwaysVisibleMenuItem.IsChecked = false;
                    return;
                }

                _currentMode = DisplayMode.AppBar;
                AlwaysVisibleMenuItem.IsChecked = true;
                AutoHideMenuItem.IsChecked = false;
                NormalWindowMenuItem.IsChecked = false;
                this.ShowInTaskbar = false;
                StopAutoHideTimer();

                // 1. アニメーションを強制停止
                this.BeginAnimation(Window.WidthProperty, null);
                this.BeginAnimation(Window.LeftProperty, null);
                this.BeginAnimation(Window.TopProperty, null);

                // 2. 一旦モニターの中央付近へワープさせて「きれいな状態」にする
                // これにより、端っこにいた際の中途半端な座標による誤判定を防ぐ
                var mi_safe = _appBarHelper.CurrentMonitorRect;
                double dpi_safe = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                this.Width = _currentFullWidth;
                this.Left = (mi_safe.Left + (mi_safe.Right - mi_safe.Left) / 2) / dpi_safe - (this.Width / 2);
                this.Top = (mi_safe.Top + (mi_safe.Bottom - mi_safe.Top) / 2) / dpi_safe - (this.Height / 2);

                // 3. 改めて登録
                _appBarHelper.Register();
                this.Topmost = true;

                // 上下位置と高さをリセット（Register内でも行われるが、念のため）
                this.Top = mi_safe.Top / dpi_safe;
                this.Height = (mi_safe.Bottom - mi_safe.Top) / dpi_safe;
                
                // モニター番号を再判定して更新
                UpdateWindowTitle();
            }
            else if (sender == NormalWindowMenuItem)
            {
                SetToNormalMode();
            }
            else
            {
                if (!TryAcquireAppBarLock())
                {
                    NormalWindowMenuItem.IsChecked = true;
                    AutoHideMenuItem.IsChecked = false;
                    return;
                }

                _currentMode = DisplayMode.AutoHide;
                AlwaysVisibleMenuItem.IsChecked = false;
                AutoHideMenuItem.IsChecked = true;
                NormalWindowMenuItem.IsChecked = false;
                _appBarHelper.Unregister();
                this.Topmost = true;
                this.ShowInTaskbar = false;
                BottomResizeRow.Height = new GridLength(0);
                StartAutoHideTimer();
                UpdateWindowTitle();
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
            if (_currentMode != DisplayMode.AutoHide || _isFakeMinimized || _isDragging) return;

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
                if (!ToolsMenu.IsOpen && !PageToolsMenu.IsOpen)
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
            var mi = _appBarHelper.CurrentMonitorRect;

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
                if (webView == null || webView.CoreWebView2 == null) return;

                // 初回時にデフォルトのUserAgentを保存
                if (_defaultUserAgent == null)
                {
                    _defaultUserAgent = webView.CoreWebView2.Settings.UserAgent;
                }

                string targetUA = _isMobileMode ? MobileUserAgent : _defaultUserAgent;

                // メインのWebViewに適用
                webView.CoreWebView2.Settings.UserAgent = targetUA;
                
                // 下部のWebView（初期化済みなら）にも適用
                if (_isBottomInitialized && webViewBottom != null && webViewBottom.CoreWebView2 != null)
                {
                    webViewBottom.CoreWebView2.Settings.UserAgent = targetUA;
                }

                // メニュー項目のテキストとアイコンを更新
                if (UserAgentMenuIcon != null) UserAgentMenuIcon.Text = _isMobileMode ? "" : "";
                if (UserAgentMenuItem != null) UserAgentMenuItem.Header = _isMobileMode ? "デスクトップ表示に切替" : "モバイル表示に切替";

                // 現在のページがあればリロードして反映
                if (_activeWebView.Source != null && !string.IsNullOrEmpty(_activeWebView.Source.ToString()))
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
        }


        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExitingFromTray)
            {
                e.Cancel = true;
                HideToTray();
            }
            else
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }
                _appBarHelper.Unregister();
                ReleaseAppBarLock();
            }
        }

        private async void InitializeWebView()
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
                UpdateUserAgent();
                webView.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
                webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
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

        private async void InitializeBottomWebView()
        {
            if (_isBottomInitialized) return;
            try
            {
                await webViewBottom.EnsureCoreWebView2Async();
                webViewBottom.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
                webViewBottom.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                
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

            _ = _bookmarkService.AddBookmarkAsync(newItem);
        }

        private void CopyUrl_Click(object sender, RoutedEventArgs e)
        {
            if (_activeWebView.Source != null)
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
        }

        private void OpenExternal_Click(object sender, RoutedEventArgs e)
        {
            if (_activeWebView.Source != null)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _activeWebView.Source.ToString(),
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"標準ブラウザで開けませんでした: {ex.Message}", "エラー");
                }
            }
        }

        private void Bookmark_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is BookmarkItem item)
            {
                webView.Source = new Uri(item.Url);
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
                    _ = _bookmarkService.MoveBookmarkAsync(droppedData, targetData);
                }
            }
            e.Handled = true;
        }

        private void DeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is BookmarkItem item)
            {
                _ = _bookmarkService.RemoveBookmarkAsync(item);
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

        // --- システムトレイ機能 ---
        private void InitializeNotifyIcon()
        {
            try
            {
                var contextMenu = new System.Windows.Forms.ContextMenuStrip();
                
                var openItem = new System.Windows.Forms.ToolStripMenuItem("表示 / 復帰");
                openItem.Click += (s, e) => ShowFromTray();
                contextMenu.Items.Add(openItem);

                contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

                var exitItem = new System.Windows.Forms.ToolStripMenuItem("終了");
                exitItem.Click += (s, e) => {
                    _isExitingFromTray = true;
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
                    if (this.Visibility == Visibility.Visible && !_isFakeMinimized)
                    {
                        HideToTray();
                    }
                    else
                    {
                        ShowFromTray();
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"NotifyIcon Init Error: {ex.Message}");
            }
        }

        private void HideToTray()
        {
            FakeMinimize();
            this.Hide();
        }

        private void ShowFromTray()
        {
            this.Show();
            this.Activate();
            RestoreFromFakeMinimize();
        }
    }
}