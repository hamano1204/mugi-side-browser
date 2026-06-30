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
using MugiSideBrowser.Managers;
using MugiSideBrowser.Helpers;
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
        private static readonly uint ShowWindowMessage = NativeMethods.RegisterWindowMessage(Constants.ShowWindowMessageName);
        private Microsoft.Web.WebView2.Wpf.WebView2? _activeWebView;
        private bool _isMobileMode = false;
        private bool _useExternalBrowserOnCtrlClick = true;
        private double _resizeStartHeight;

        private System.Windows.Point _headerDragStartPoint;
        private bool _isHeaderMouseDown = false;

        // Manager classes
        private WebViewManager _webViewManager;
        private PaneManager _paneManager;
        private DisplayModeManager _displayModeManager;

        // マニュアルドラッグ用変数
        private bool _isManualDragging = false;
        private System.Windows.Point _dragStartMousePos;
        private System.Windows.Point _dragStartWindowPos;
        private DisplayMode _dragOriginalMode;

        private bool _isResizing = false;
        private System.Windows.Point _resizeStartPoint;
        private double _resizeStartWidth;
        private DateTime _lastResizeTime = DateTime.MinValue;





        public MainWindow()
        {
            InitializeComponent();
            _appBarHelper = new AppBarHelper(this);
            
            // Initialize managers
            _displayModeManager = new DisplayModeManager(this, _appBarHelper);
            _paneManager = new PaneManager();
            _webViewManager = new WebViewManager(
                target => GetContainerForPane(target),
                wv =>
                {
                    wv.GotFocus += WebView_GotFocus;
                    if (wv.CoreWebView2 != null)
                    {
                        wv.CoreWebView2.SourceChanged += CoreWebView2_SourceChanged;
                        wv.CoreWebView2.HistoryChanged += CoreWebView2_HistoryChanged;
                        wv.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                    }
                });

            this.Width = _displayModeManager.CurrentFullWidth;
            
            this.SourceInitialized += MainWindow_SourceInitialized;
            this.Closing += MainWindow_Closing;
            this.StateChanged += MainWindow_StateChanged;
            
            _bookmarkService = new BookmarkService();
            BookmarkList.ItemsSource = _bookmarkService.Bookmarks;
            InitializeBookmarksAsync();
            InitializeNotifyIcon();
            UpdateMinimizeButtonState();

            // Subscribe to manager events
            _paneManager.SplitLayoutChanged += ApplySplitLayout;
            _paneManager.ActivePaneChanged += () => UpdateActiveWebViewAfterSplitChange();
            _displayModeManager.ModeChanged += UpdateWindowTitle;
            _displayModeManager.WindowControlsStateChanged += UpdateWindowControlsState;

            // レイアウトの初期適用
            bool isRight = SettingsManager.Settings.SidebarPosition == "right";
            SetSidebarPosition(isRight);
        }


        private async void InitializeBookmarksAsync()
        {
            await _bookmarkService.InitializeAsync();

            // 起動時に全項目のロード/アクティブ状態を初期化
            foreach (var item in _bookmarkService.Bookmarks)
            {
                item.IsLoaded = false;
                item.IsActive = false;
                item.IsOpen = false;
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

        private string GetText(string key, string defaultValue = "")
        {
            if (System.Windows.Application.Current.TryFindResource(key) is string text)
            {
                return text;
            }
            return defaultValue;
        }

        private void UpdateWindowTitle()
        {
            string modeName = _displayModeManager.CurrentMode switch
            {
                DisplayMode.AppBar => GetText("Mode_AppBar", "常時表示"),
                DisplayMode.AutoHide => GetText("Mode_AutoHide", "自動隠し"),
                DisplayMode.Normal => GetText("Mode_Floating", "自由配置"),
                _ => GetText("Mode_Unknown", "不明")
            };

            this.Title = $"MugiSideBrowser [{modeName}]";
        }


        private void AutoAllocatePosition()
        {
            _appBarHelper.ResetMonitorInfo();
            _displayModeManager.TransitionToMode(DisplayMode.AppBar);
            _appBarHelper.Edge = NativeMethods.AppBarEdges.Right;
            this.ShowInTaskbar = false;
            _appBarHelper.Register();
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
            _displayModeManager.TransitionToMode(DisplayMode.Normal);
            LeftResizeColumn.Width = new GridLength(4);
            RightResizeColumn.Width = new GridLength(4);
            BottomResizeRow.Height = new GridLength(4);
        }



        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            _displayModeManager.HandleStateChanged(this.WindowState);
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
                UpdateThemeMenuItem();
                UpdateSidebarMenuItem();
                element.ContextMenu.IsOpen = true;
            }
        }

        private void UpdateThemeMenuItem()
        {
            if (ThemeToggleMenuItem != null && ThemeIcon != null)
            {
                string currentTheme = SettingsManager.Settings.Theme;
                bool isDark = currentTheme == "dark";
                ThemeToggleMenuItem.Header = GetText(isDark ? "Menu_SwitchToLightMode" : "Menu_SwitchToDarkMode");
                ThemeIcon.Text = isDark ? "" : "";
            }
        }

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            string currentTheme = SettingsManager.Settings.Theme;
            bool isDark = currentTheme == "dark";
            bool newIsDark = !isDark;

            SettingsManager.Settings.Theme = newIsDark ? "dark" : "light";
            SettingsManager.Save();
            App.ApplyTheme(newIsDark);
            UpdateThemeMenuItem();
        }

        private void UpdateSidebarMenuItem()
        {
            if (SidebarPositionToggleMenuItem != null && SidebarPositionIcon != null)
            {
                bool isRight = SettingsManager.Settings.SidebarPosition == "right";
                SidebarPositionToggleMenuItem.Header = GetText(isRight ? "Menu_MoveFavoritesLeft" : "Menu_MoveFavoritesRight");
                SidebarPositionIcon.Text = isRight ? "" : ""; //  (U+E76C: DockLeft),  (U+E76A: DockRight)
            }
        }

        private void SidebarPositionToggle_Click(object sender, RoutedEventArgs e)
        {
            bool isRight = SettingsManager.Settings.SidebarPosition == "right";
            bool newIsRight = !isRight;

            SettingsManager.Settings.SidebarPosition = newIsRight ? "right" : "left";
            SettingsManager.Save();
            SetSidebarPosition(newIsRight);
            UpdateSidebarMenuItem();
        }

        private void SetSidebarPosition(bool isRight)
        {
            if (MainContentGrid != null && SidebarColumn != null && BrowserColumn != null && SidebarBorder != null && BrowserGroupGrid != null)
            {
                if (isRight)
                {
                    // 右側配置
                    SidebarColumn.Width = new GridLength(1, GridUnitType.Star);
                    BrowserColumn.Width = GridLength.Auto;

                    Grid.SetColumn(SidebarBorder, 1);
                    Grid.SetColumn(BrowserGroupGrid, 0);

                    SidebarBorder.BorderThickness = new Thickness(1, 0, 0, 0); // 左側に枠線
                }
                else
                {
                    // 左側配置
                    SidebarColumn.Width = GridLength.Auto;
                    BrowserColumn.Width = new GridLength(1, GridUnitType.Star);

                    Grid.SetColumn(SidebarBorder, 0);
                    Grid.SetColumn(BrowserGroupGrid, 1);

                    SidebarBorder.BorderThickness = new Thickness(0, 0, 1, 0); // 右側に枠線
                }
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
                _mouseTimer.Interval = TimeSpan.FromMilliseconds(AutoHideTimerIntervalMs);
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
                isMouseInTriggerZone = (point.X >= mi.rcMonitor.Right - TriggerZonePixel && point.Y >= mi.rcMonitor.Top && point.Y <= mi.rcMonitor.Bottom);
                // ウィンドウ内かチェック
                isMouseInWindow = (point.X >= mi.rcMonitor.Right - (_currentFullWidth * dpi) && point.X <= mi.rcMonitor.Right && 
                                   point.Y >= mi.rcMonitor.Top && point.Y <= mi.rcMonitor.Bottom);
            }
            else
            {
                // 左端のトリガーゾーン
                isMouseInTriggerZone = (point.X <= mi.rcMonitor.Left + TriggerZonePixel && point.Y >= mi.rcMonitor.Top && point.Y <= mi.rcMonitor.Bottom);
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

            var duration = TimeSpan.FromMilliseconds(AnimationDurationMs);
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
                    if (newHeight < MinPaneHeight) newHeight = MinPaneHeight;
                    
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

            if (newWidth < MinPaneWidth) newWidth = MinPaneWidth;
            if (newWidth > MaxPaneWidth) newWidth = MaxPaneWidth;

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
                    this.Left = OffScreenPosition;
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
                _webViewManager.UpdateUserAgentForAll(_isMobileMode);

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

            _webViewManager.DisposeAll(WebViewTopHolder, WebViewMiddleHolder, WebViewBottomHolder);

            // Unsubscribe from manager events
            _paneManager.SplitLayoutChanged -= ApplySplitLayout;
            _paneManager.ActivePaneChanged -= () => UpdateActiveWebViewAfterSplitChange();
            _displayModeManager.ModeChanged -= UpdateWindowTitle;
            _displayModeManager.WindowControlsStateChanged -= UpdateWindowControlsState;
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

            if (_paneManager.IsMiddlePaneOpen && _paneManager.IsBottomPaneOpen)
            {
                MiddleRow.MinHeight = 100;
                BottomRow.MinHeight = 100;

                TopRow.Height = new GridLength(1, GridUnitType.Star);
                MiddleRow.Height = new GridLength(1, GridUnitType.Star);
                BottomRow.Height = new GridLength(1, GridUnitType.Star);

                Grid.SetRow(WebViewBottomContainer, 4);

                RowSplitter1.Visibility = Visibility.Visible;
                RowSplitter2.Visibility = Visibility.Visible;

                WebViewMiddleContainer.Visibility = Visibility.Visible;
                WebViewBottomContainer.Visibility = Visibility.Visible;
            }
            else if (_paneManager.IsMiddlePaneOpen)
            {
                MiddleRow.MinHeight = 100;
                BottomRow.MinHeight = 0;

                TopRow.Height = new GridLength(1, GridUnitType.Star);
                MiddleRow.Height = new GridLength(1, GridUnitType.Star);
                BottomRow.Height = new GridLength(0);

                Grid.SetRow(WebViewBottomContainer, 4);

                RowSplitter1.Visibility = Visibility.Visible;
                RowSplitter2.Visibility = Visibility.Collapsed;

                WebViewMiddleContainer.Visibility = Visibility.Visible;
                WebViewBottomContainer.Visibility = Visibility.Collapsed;
            }
            else if (_paneManager.IsBottomPaneOpen)
            {
                MiddleRow.MinHeight = 100;
                BottomRow.MinHeight = 0;

                TopRow.Height = new GridLength(1, GridUnitType.Star);
                MiddleRow.Height = new GridLength(1, GridUnitType.Star);
                BottomRow.Height = new GridLength(0);

                Grid.SetRow(WebViewBottomContainer, 2);

                RowSplitter1.Visibility = Visibility.Visible;
                RowSplitter2.Visibility = Visibility.Collapsed;

                WebViewMiddleContainer.Visibility = Visibility.Collapsed;
                WebViewBottomContainer.Visibility = Visibility.Visible;
            }
            else
            {
                MiddleRow.MinHeight = 0;
                BottomRow.MinHeight = 0;

                TopRow.Height = new GridLength(1, GridUnitType.Star);
                MiddleRow.Height = new GridLength(0);
                BottomRow.Height = new GridLength(0);

                Grid.SetRow(WebViewBottomContainer, 4);

                RowSplitter1.Visibility = Visibility.Collapsed;
                RowSplitter2.Visibility = Visibility.Collapsed;

                WebViewMiddleContainer.Visibility = Visibility.Collapsed;
                WebViewBottomContainer.Visibility = Visibility.Collapsed;
            }
        }

        private BookmarkItem? GetActiveBookmarkForPane(TargetWindow pane)
        {
            return _paneManager.GetActiveBookmarkForPane(pane);
        }

        private void UpdateActiveWebViewAfterSplitChange()
        {
            var activeB = GetActiveBookmarkForPane(_paneManager.ActivePane);
            if (activeB != null)
            {
                var activeWv = _webViewManager.GetWebView(activeB);
                _activeWebView = activeWv;
            }
            else
            {
                _activeWebView = null;
            }

            if (_activeWebView != null && _activeWebView.Source != null)
            {
                UrlTextBox.Text = _activeWebView.Source.ToString();
            }
            else
            {
                UrlTextBox.Text = activeB?.Url ?? "";
            }
            UpdateBookmarkActiveState();
        }

        // Resume ボタン / スリーププレースホルダーのクリックを共通ヘルパーで処理
        private void ResumePane(TargetWindow pane)
        {
            var bookmark = _paneManager.GetActiveBookmarkForPane(pane);
            if (bookmark != null) ShowBookmarkWebView(bookmark, pane);
        }

        private void ResumeTop_Click(object sender, RoutedEventArgs e) => ResumePane(TargetWindow.Top);
        private void ResumeMiddle_Click(object sender, RoutedEventArgs e) => ResumePane(TargetWindow.Middle);
        private void ResumeBottom_Click(object sender, RoutedEventArgs e) => ResumePane(TargetWindow.Bottom);

        private void TopSleepPlaceholder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ResumePane(TargetWindow.Top);
        private void MiddleSleepPlaceholder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ResumePane(TargetWindow.Middle);
        private void BottomSleepPlaceholder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => ResumePane(TargetWindow.Bottom);

        // 各ペインのPreviewMouseDownを共通ヘルパーで処理
        private void WebViewContainer_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => ActivatePaneOnMouseDown(TargetWindow.Top);

        private void WebViewMiddleContainer_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => ActivatePaneOnMouseDown(TargetWindow.Middle);

        private void WebViewBottomContainer_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => ActivatePaneOnMouseDown(TargetWindow.Bottom);

        // ActivatePaneOnMouseDown / ActivatePaneByName の共通実装
        private void SetActivePaneCore(TargetWindow pane)
        {
            _paneManager.SetActivePane(pane);
            var holder = pane switch
            {
                TargetWindow.Top    => WebViewTopHolder,
                TargetWindow.Middle => WebViewMiddleHolder,
                _                   => WebViewBottomHolder
            };
            _activeWebView = holder.Children.OfType<Microsoft.Web.WebView2.Wpf.WebView2>()
                                   .FirstOrDefault(w => w.Visibility == Visibility.Visible);
            if (_activeWebView != null && _activeWebView.Source != null)
                UrlTextBox.Text = _activeWebView.Source.ToString();
            else
                UrlTextBox.Text = GetActiveBookmarkForPane(_paneManager.ActivePane)?.Url ?? "";
            UpdateBookmarkActiveState();
        }

        private void ActivatePaneOnMouseDown(TargetWindow pane) => SetActivePaneCore(pane);

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
            if (_displayModeManager.CurrentMode == DisplayMode.AppBar || _displayModeManager.CurrentMode == DisplayMode.AutoHide)
            {
                return;
            }
            this.WindowState = WindowState.Minimized;
        }

        private void ToggleNormalAppBar_Click(object sender, RoutedEventArgs e)
        {
            if (_displayModeManager.CurrentMode == DisplayMode.Normal)
            {
                _displayModeManager.TransitionToMode(DisplayMode.AppBar);
            }
            else
            {
                _displayModeManager.TransitionToMode(DisplayMode.Normal);
            }
        }

        private void ToggleAppBarAutoHide_Click(object sender, RoutedEventArgs e)
        {
            if (_displayModeManager.CurrentMode == DisplayMode.AppBar)
            {
                _displayModeManager.TransitionToMode(DisplayMode.AutoHide);
            }
            else if (_displayModeManager.CurrentMode == DisplayMode.AutoHide)
            {
                _displayModeManager.TransitionToMode(DisplayMode.AppBar);
            }
        }


        private void UpdateMinimizeButtonState()
        {
            UpdateWindowControlsState();
        }

        private void UpdateWindowControlsState()
        {
            if (_displayModeManager.CurrentMode == DisplayMode.Normal)
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
                    ToggleNormalAppBarButton.ToolTip = GetText("Tooltip_SidebarDisplay", "サイドバー表示 (AppBar)");
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
                    if (_displayModeManager.CurrentMode == DisplayMode.AppBar)
                    {
                        ToggleAppBarAutoHideButton.Content = ""; // Unpin (E77A)
                        ToggleAppBarAutoHideButton.ToolTip = GetText("Tooltip_AutoHide", "自動的に隠す (AutoHide)");
                    }
                    else // AutoHide
                    {
                        ToggleAppBarAutoHideButton.Content = ""; // Pin (E718)
                        ToggleAppBarAutoHideButton.ToolTip = GetText("Tooltip_AlwaysShow", "常時表示 (AppBar)");
                    }
                }
                if (ToggleNormalAppBarButton != null)
                {
                    ToggleNormalAppBarButton.Visibility = Visibility.Visible;
                    ToggleNormalAppBarButton.Content = "\uE90D"; // DockRight
                    ToggleNormalAppBarButton.ToolTip = GetText("Tooltip_FloatingWindow", "自由配置ウィンドウ");
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
                System.Windows.MessageBox.Show(string.Format(GetText("Msg_AddBookmarkFailed", "ブックマークの追加に失敗しました: {0}"), ex.Message), GetText("Msg_ErrorTitle", "エラー"));
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
                    System.Windows.MessageBox.Show(string.Format(GetText("Msg_CopyFailed", "コピーに失敗しました: {0}"), ex.Message), GetText("Msg_ErrorTitle", "エラー"));
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
                        System.Windows.MessageBox.Show(string.Format(GetText("Msg_CopyFailed", "コピーに失敗しました: {0}"), ex.Message), GetText("Msg_ErrorTitle", "エラー"));
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
                    System.Windows.MessageBox.Show(string.Format(GetText("Msg_OpenExternalFailed", "標準ブラウザで開けませんでした: {0}"), ex.Message), GetText("Msg_ErrorTitle", "エラー"));
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

                ShowBookmarkWebView(item, _paneManager.ActivePane);
            }
            e.Handled = true;
        }

        private void Bookmark_MouseMiddleButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is BookmarkItem item)
            {
                if (item.IsSeparator) { e.Handled = true; return; }
                // OpenBookmarkInSplitScreen が TryActivatePaneWithBookmark を含む switch ロジックを統括する
                OpenBookmarkInSplitScreen(item);
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
                        System.Windows.MessageBox.Show(string.Format(GetText("Msg_MoveBookmarkFailed", "ブックマークの移動に失敗しました: {0}"), ex.Message), GetText("Msg_ErrorTitle", "エラー"));
                    }
                }
            }
            e.Handled = true;
        }

        private async void ShowBookmarkWebView(BookmarkItem item, TargetWindow target)
        {
            if (_webViewManager.IsNavigating) return;

            var targetWebView = await _webViewManager.GetOrCreateWebViewAsync(item, target, _isMobileMode);
            MoveWebViewToContainer(targetWebView, target);
            UpdatePaneContent(item, targetWebView, target);
        }

        private void MoveWebViewToContainer(Microsoft.Web.WebView2.Wpf.WebView2 webView, TargetWindow target)
        {
            _webViewManager.RemoveWebViewFromContainer(webView, WebViewTopHolder, WebViewMiddleHolder, WebViewBottomHolder);
            _webViewManager.AddWebViewToContainer(webView, GetContainerForPane(target));
        }

        private void UpdatePaneContent(BookmarkItem item, Microsoft.Web.WebView2.Wpf.WebView2 webView, TargetWindow target)
        {
            if (target == TargetWindow.Top)
            {
                TopSleepPlaceholder.Visibility = Visibility.Collapsed;
                _paneManager.SetActiveBookmarkForPane(TargetWindow.Top, item);
                _paneManager.SetActivePane(TargetWindow.Top);
            }
            else if (target == TargetWindow.Middle)
            {
                MiddleSleepPlaceholder.Visibility = Visibility.Collapsed;
                MiddleEmptyPlaceholder.Visibility = Visibility.Collapsed;
                _paneManager.SetActiveBookmarkForPane(TargetWindow.Middle, item);
                _paneManager.SetActivePane(TargetWindow.Middle);
            }
            else if (target == TargetWindow.Bottom)
            {
                BottomSleepPlaceholder.Visibility = Visibility.Collapsed;
                BottomEmptyPlaceholder.Visibility = Visibility.Collapsed;
                _paneManager.SetActiveBookmarkForPane(TargetWindow.Bottom, item);
                _paneManager.SetActivePane(TargetWindow.Bottom);
            }

            var destinationContainer = GetContainerForPane(target);
            foreach (var b in _bookmarkService.Bookmarks)
            {
                var wv = _webViewManager.GetWebView(b);
                if (wv != null && destinationContainer.Children.Contains(wv))
                {
                    wv.Visibility = (b == item) ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            _activeWebView = webView;
            UrlTextBox.Text = _activeWebView.Source?.ToString() ?? "";
            UpdateBookmarkActiveState();
        }

        private void ResetToDefaultMiddleWebView()
        {
            _paneManager.SetActiveBookmarkForPane(TargetWindow.Middle, null);
            if (_paneManager.ActivePane == TargetWindow.Middle) _activeWebView = null;
            MiddleSleepPlaceholder.Visibility = Visibility.Collapsed;
            MiddleEmptyPlaceholder.Visibility = Visibility.Visible;
            UpdateBookmarkActiveState();
        }

        private void UpdateBookmarkActiveState()
        {
            if (_bookmarkService == null || _bookmarkService.Bookmarks == null) return;

            var activeB = GetActiveBookmarkForPane(_paneManager.ActivePane);
            
            var openBookmarks = new System.Collections.Generic.HashSet<BookmarkItem>();
            if (_paneManager.ActiveBookmarkTop != null) openBookmarks.Add(_paneManager.ActiveBookmarkTop);
            if (_paneManager.IsMiddlePaneOpen && _paneManager.ActiveBookmarkMiddle != null) openBookmarks.Add(_paneManager.ActiveBookmarkMiddle);
            if (_paneManager.IsBottomPaneOpen && _paneManager.ActiveBookmarkBottom != null) openBookmarks.Add(_paneManager.ActiveBookmarkBottom);

            foreach (var b in _bookmarkService.Bookmarks)
            {
                b.IsActive = (b == activeB);
                b.IsOpen = openBookmarks.Contains(b);
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

            if (activeBookmark != null)
            {
                var wv = _webViewManager.GetWebView(activeBookmark);
                if (wv != null && wv.CoreWebView2 != null)
                {
                    backButton.IsEnabled = wv.CoreWebView2.CanGoBack;
                    forwardButton.IsEnabled = wv.CoreWebView2.CanGoForward;
                    return;
                }
            }
            backButton.IsEnabled = false;
            forwardButton.IsEnabled = false;
        }

        private void UpdatePaneHeadersUI()
        {
            var activeBg = (System.Windows.Media.Brush)TryFindResource("HoverBackground") ?? System.Windows.Media.Brushes.DimGray;
            var inactiveBg = (System.Windows.Media.Brush)TryFindResource("TitleBarBackground") ?? System.Windows.Media.Brushes.Transparent;
            var activeText = (System.Windows.Media.Brush)TryFindResource("PrimaryText") ?? System.Windows.Media.Brushes.White;
            var inactiveText = (System.Windows.Media.Brush)TryFindResource("SecondaryText") ?? System.Windows.Media.Brushes.Gray;

            UpdateSinglePaneHeader(TopHeader, TopActiveIndicator, TopHeaderText, _paneManager.ActiveBookmarkTop, TargetWindow.Top, GetText("Title_MainPane", "メイン画面"), activeBg, inactiveBg, activeText, inactiveText);
            UpdateSinglePaneHeader(MiddleHeader, MiddleActiveIndicator, MiddleHeaderText, _paneManager.ActiveBookmarkMiddle, TargetWindow.Middle, GetText("Title_SubPaneMiddle", "サブ画面 (中)"), activeBg, inactiveBg, activeText, inactiveText);
            UpdateSinglePaneHeader(BottomHeader, BottomActiveIndicator, BottomHeaderText, _paneManager.ActiveBookmarkBottom, TargetWindow.Bottom, GetText("Title_SubPaneBottom", "サブ画面 (下)"), activeBg, inactiveBg, activeText, inactiveText);
        }

        private void UpdateSinglePaneHeader(System.Windows.Controls.Border header, System.Windows.Shapes.Ellipse indicator, System.Windows.Controls.TextBlock text, BookmarkItem? bookmark, TargetWindow pane, string defaultTitle, System.Windows.Media.Brush activeBg, System.Windows.Media.Brush inactiveBg, System.Windows.Media.Brush activeText, System.Windows.Media.Brush inactiveText)
        {
            if (header == null || indicator == null || text == null) return;

            bool isActive = (_paneManager.ActivePane == pane);
            indicator.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
            text.Foreground = isActive ? activeText : inactiveText;
            text.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
            header.Background = isActive ? activeBg : inactiveBg;
            text.Text = string.IsNullOrEmpty(bookmark?.Title) ? defaultTitle : bookmark.Title;
        }

        private void ResetToDefaultBottomWebView()
        {
            _paneManager.SetActiveBookmarkForPane(TargetWindow.Bottom, null);
            if (_paneManager.ActivePane == TargetWindow.Bottom) _activeWebView = null;
            BottomSleepPlaceholder.Visibility = Visibility.Collapsed;
            BottomEmptyPlaceholder.Visibility = Visibility.Visible;
            UpdateBookmarkActiveState();
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
                bool isOpen = (_paneManager.ActiveBookmarkTop == item) ||
                              (_paneManager.IsMiddlePaneOpen && _paneManager.ActiveBookmarkMiddle == item) ||
                              (_paneManager.IsBottomPaneOpen && _paneManager.ActiveBookmarkBottom == item);

                foreach (var mItem in menu.Items)
                {
                    if (mItem is System.Windows.Controls.MenuItem menuItem)
                    {
                        string? tagVal = menuItem.Tag?.ToString();
                        if (tagVal == "OpenInSplitScreen")
                        {
                            menuItem.IsEnabled = (_paneManager.CurrentSplitMode != SplitMode.Triple) && !isOpen;
                        }
                        else if (tagVal == "ClearBookmarkState")
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
                var activeBookmark = GetActiveBookmarkForPane(_paneManager.ActivePane);

                System.Windows.Controls.MenuItem? resetItem = menu.Items.OfType<System.Windows.Controls.MenuItem>().FirstOrDefault(item => item.Tag?.ToString() == "ResetToInitialPage");
                if (resetItem != null)
                {
                    resetItem.IsEnabled = (activeBookmark != null);
                }

                System.Windows.Controls.MenuItem? uaItem = menu.Items.OfType<System.Windows.Controls.MenuItem>().FirstOrDefault(item => item.Tag?.ToString() == "UserAgent");
                if (uaItem != null)
                {
                    uaItem.Header = _isMobileMode ? GetText("Menu_SwitchToDesktop", "デスクトップ表示に切替") : GetText("Menu_SwitchToMobile", "モバイル表示に切替");
                    if (uaItem.Icon is TextBlock iconText)
                    {
                        iconText.Text = _isMobileMode ? "" : "";
                    }
                }

                System.Windows.Controls.MenuItem? closeItem = menu.Items.OfType<System.Windows.Controls.MenuItem>().FirstOrDefault(item => item.Tag?.ToString() == "CloseActivePane");
                if (closeItem != null)
                {
                    closeItem.IsEnabled = (_paneManager.CurrentSplitMode != SplitMode.Single);
                }
            }
        }

        private void ResetToInitialPage_Click(object sender, RoutedEventArgs e)
        {
            var activeBookmark = GetActiveBookmarkForPane(_paneManager.ActivePane);
            if (activeBookmark != null)
            {
                var webView = _webViewManager.GetWebView(activeBookmark);
                if (webView != null)
                {
                    webView.Source = new Uri(activeBookmark.Url);
                }
            }
        }

        private bool TryActivatePaneWithBookmark(BookmarkItem item)
        {
            if (_paneManager.ActiveBookmarkTop == item)
            {
                ShowBookmarkWebView(item, TargetWindow.Top);
                return true;
            }
            if (_paneManager.IsMiddlePaneOpen && _paneManager.ActiveBookmarkMiddle == item)
            {
                ShowBookmarkWebView(item, TargetWindow.Middle);
                return true;
            }
            if (_paneManager.IsBottomPaneOpen && _paneManager.ActiveBookmarkBottom == item)
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

            switch (_paneManager.CurrentSplitMode)
            {
                case SplitMode.Single:
                    OpenBookmarkInBottomWindow(item);
                    break;
                case SplitMode.Double:
                    OpenBookmarkInSplitScreenFromDouble(item);
                    break;
                case SplitMode.Triple:
                    ShowBookmarkWebView(item, _paneManager.ActivePane);
                    break;
            }
        }

        private void OpenBookmarkInSplitScreenFromDouble(BookmarkItem item)
        {
            if (!_paneManager.IsBottomPaneOpen)
            {
                OpenBookmarkInBottomWindow(item);
            }
            else
            {
                var prevBottom = _paneManager.ActiveBookmarkBottom;
                
                _paneManager.OpenMiddlePane();
                _paneManager.OpenBottomPane();
                
                if (prevBottom != null)
                {
                    ShowBookmarkWebView(prevBottom, TargetWindow.Middle);
                }
                
                ShowBookmarkWebView(item, TargetWindow.Bottom);
            }
        }

        private void OpenBookmarkInMiddleWindow(BookmarkItem item)
        {
            _paneManager.OpenMiddlePane();
            ShowBookmarkWebView(item, TargetWindow.Middle);
        }

        private void OpenBookmarkInBottomWindow(BookmarkItem item)
        {
            _paneManager.OpenBottomPane();
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




        private System.Windows.Controls.Panel GetContainerForPane(TargetWindow pane) => pane switch
        {
            TargetWindow.Top => WebViewTopHolder,
            TargetWindow.Middle => WebViewMiddleHolder,
            TargetWindow.Bottom => WebViewBottomHolder,
            _ => throw new ArgumentOutOfRangeException(nameof(pane), pane, null)
        };

        // SwapPanes 内でWebViewとプレースホルダーをペインに配置する共通ヘルパー

        private void SwapPanes(string source, string target)
        {
            TargetWindow sourceWindow = ParseTargetWindow(source);
            TargetWindow targetWindow = ParseTargetWindow(target);

            BookmarkItem? sourceBookmark = _paneManager.GetActiveBookmarkForPane(sourceWindow);
            BookmarkItem? targetBookmark = _paneManager.GetActiveBookmarkForPane(targetWindow);

            Microsoft.Web.WebView2.Wpf.WebView2? sourceWv = sourceBookmark != null ? _webViewManager.GetWebView(sourceBookmark) : null;
            Microsoft.Web.WebView2.Wpf.WebView2? targetWv = targetBookmark != null ? _webViewManager.GetWebView(targetBookmark) : null;

            if (sourceWv != null) _webViewManager.RemoveWebViewFromContainer(sourceWv, WebViewTopHolder, WebViewMiddleHolder, WebViewBottomHolder);
            if (targetWv != null) _webViewManager.RemoveWebViewFromContainer(targetWv, WebViewTopHolder, WebViewMiddleHolder, WebViewBottomHolder);

            _paneManager.SwapPanes(sourceWindow, targetWindow);

            ApplyPaneContent(sourceBookmark, sourceWv, targetWindow);
            ApplyPaneContent(targetBookmark, targetWv, sourceWindow);

            if (_paneManager.ActivePane == sourceWindow)
                _activeWebView = targetWv;
            else if (_paneManager.ActivePane == targetWindow)
                _activeWebView = sourceWv;

            UpdateActiveWebViewAfterSplitChange();
        }

        private void ActivatePaneByName(string tag)
        {
            TargetWindow pane = tag switch
            {
                "Middle" when _paneManager.IsMiddlePaneOpen => TargetWindow.Middle,
                "Bottom" when _paneManager.IsBottomPaneOpen => TargetWindow.Bottom,
                _ => TargetWindow.Top
            };
            SetActivePaneCore(pane);
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
            _paneManager.CloseMiddlePane();
            WebViewMiddleHolder.Children.Clear();
            UpdateActiveWebViewAfterSplitChange();
        }

        private void CloseBottomPane_Click(object sender, RoutedEventArgs e)
        {
            CloseBottomPane();
            e.Handled = true;
        }

        private void CloseBottomPane()
        {
            _paneManager.CloseBottomPane();
            WebViewBottomHolder.Children.Clear();
            UpdateActiveWebViewAfterSplitChange();
        }

        private void CloseTopPane()
        {
            _paneManager.CloseTopPane();
            if (_paneManager.ActiveBookmarkTop != null)
            {
                ShowBookmarkWebView(_paneManager.ActiveBookmarkTop, TargetWindow.Top);
            }
            else
            {
                ResetToDefaultWebView();
            }
            if (_paneManager.ActiveBookmarkMiddle != null)
            {
                ShowBookmarkWebView(_paneManager.ActiveBookmarkMiddle, TargetWindow.Middle);
            }
            else
            {
                ResetToDefaultMiddleWebView();
            }
            if (_paneManager.IsBottomPaneOpen)
            {
                WebViewBottomHolder.Children.Clear();
            }
            if (_paneManager.IsMiddlePaneOpen)
            {
                WebViewMiddleHolder.Children.Clear();
            }
            UpdateActiveWebViewAfterSplitChange();
        }

        private void CloseActivePane_Click(object sender, RoutedEventArgs e)
        {
            _paneManager.CloseActivePane();
            switch (_paneManager.ActivePane)
            {
                case TargetWindow.Top:
                    WebViewTopHolder.Children.Clear();
                    break;
                case TargetWindow.Middle:
                    WebViewMiddleHolder.Children.Clear();
                    break;
                case TargetWindow.Bottom:
                    WebViewBottomHolder.Children.Clear();
                    break;
            }
            UpdateActiveWebViewAfterSplitChange();
            e.Handled = true;
        }

        private async void DeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.MenuItem menuItem && menuItem.DataContext is BookmarkItem item)
            {
                var result = System.Windows.MessageBox.Show(
                    string.Format(GetText("Msg_ConfirmDelete", "「{0}」を削除しますか？"), item.Title),
                    GetText("Msg_ConfirmDeleteTitle", "削除の確認"),
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
                    System.Windows.MessageBox.Show(string.Format(GetText("Msg_DeleteBookmarkFailed", "ブックマークの削除に失敗しました: {0}"), ex.Message), GetText("Msg_ErrorTitle", "エラー"));
                }

                if (_paneManager.ActiveBookmarkTop == item)
                {
                    _paneManager.SetActiveBookmarkForPane(TargetWindow.Top, null);
                    TopSleepPlaceholder.Visibility = Visibility.Collapsed;
                    ResetToDefaultWebView(item);
                }
                if (_paneManager.ActiveBookmarkMiddle == item)
                {
                    _paneManager.SetActiveBookmarkForPane(TargetWindow.Middle, null);
                    MiddleSleepPlaceholder.Visibility = Visibility.Collapsed;
                    MiddleEmptyPlaceholder.Visibility = Visibility.Visible;
                }
                if (_paneManager.ActiveBookmarkBottom == item)
                {
                    _paneManager.SetActiveBookmarkForPane(TargetWindow.Bottom, null);
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

                bool layoutChanged = false;
                if (_paneManager.ActiveBookmarkMiddle == item && _paneManager.IsMiddlePaneOpen)
                {
                    _paneManager.CloseMiddlePane();
                    WebViewMiddleHolder.Children.Clear();
                    layoutChanged = true;
                }
                if (_paneManager.ActiveBookmarkBottom == item && _paneManager.IsBottomPaneOpen)
                {
                    _paneManager.CloseBottomPane();
                    WebViewBottomHolder.Children.Clear();
                    layoutChanged = true;
                }

                if (layoutChanged)
                {
                    UpdateActiveWebViewAfterSplitChange();
                }
            }
        }

        private void DisposeBookmarkWebView(BookmarkItem item)
        {
            _webViewManager.DisposeBookmarkWebView(item, WebViewTopHolder, WebViewMiddleHolder, WebViewBottomHolder);

            string sleepFormat = GetText("Sleep_TitleFormat", "「{0}」はスリープ状態です");
            if (_paneManager.ActiveBookmarkTop == item)
            {
                TopSleepTitle.Text = string.Format(sleepFormat, item.Title);
                TopSleepPlaceholder.Visibility = Visibility.Visible;
                if (_paneManager.ActivePane == TargetWindow.Top) _activeWebView = null;
            }
            if (_paneManager.ActiveBookmarkMiddle == item)
            {
                MiddleSleepTitle.Text = string.Format(sleepFormat, item.Title);
                MiddleSleepPlaceholder.Visibility = Visibility.Visible;
                MiddleEmptyPlaceholder.Visibility = Visibility.Collapsed;
                if (_paneManager.ActivePane == TargetWindow.Middle) _activeWebView = null;
            }
            if (_paneManager.ActiveBookmarkBottom == item)
            {
                BottomSleepTitle.Text = string.Format(sleepFormat, item.Title);
                BottomSleepPlaceholder.Visibility = Visibility.Visible;
                BottomEmptyPlaceholder.Visibility = Visibility.Collapsed;
                if (_paneManager.ActivePane == TargetWindow.Bottom) _activeWebView = null;
            }

            UpdateBookmarkActiveState();
        }

        private void ResetToDefaultWebView(BookmarkItem? excludeItem = null)
        {
            var candidate = _bookmarkService.Bookmarks.FirstOrDefault(
                b => !b.IsSeparator && b != _paneManager.ActiveBookmarkMiddle && b != _paneManager.ActiveBookmarkBottom && b != excludeItem);
            if (candidate != null)
            {
                ShowBookmarkWebView(candidate, TargetWindow.Top);
            }
            else
            {
                _paneManager.SetActiveBookmarkForPane(TargetWindow.Top, null);
                if (_paneManager.ActivePane == TargetWindow.Top) _activeWebView = null;
                TopSleepPlaceholder.Visibility = Visibility.Collapsed;
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

            var activeB = GetActiveBookmarkForPane(_paneManager.ActivePane);
            if (_activeWebView == null && activeB != null)
            {
                ShowBookmarkWebView(activeB, _paneManager.ActivePane);
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

                var exitItem = new System.Windows.Forms.ToolStripMenuItem(GetText("Tray_Exit", "終了"));
                exitItem.Click += (s, e) => {
                    this.Close();
                };
                contextMenu.Items.Add(exitItem);

                System.Drawing.Icon? appIcon = null;
                try
                {
                    var iconUri = new Uri("pack://application:,,,/app_icon.png");
                    var iconStreamInfo = System.Windows.Application.GetResourceStream(iconUri);
                    if (iconStreamInfo != null)
                    {
                        using (var stream = iconStreamInfo.Stream)
                        using (var bitmap = new System.Drawing.Bitmap(stream))
                        {
                            IntPtr hIcon = bitmap.GetHicon();
                            // Icon.FromHandleはhIconの所有権を引き継ぐ
                            // IconがDisposeされるときにhIconも破棄される
                            appIcon = System.Drawing.Icon.FromHandle(hIcon);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load tray icon from png: {ex.Message}");
                }

                // フォールバック処理
                if (appIcon == null)
                {
                    string friendlyName = AppDomain.CurrentDomain.FriendlyName ?? "MugiSideBrowser";
                    string exePath = Path.Combine(AppContext.BaseDirectory, friendlyName + ".exe");
                    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    {
                        try { appIcon = System.Drawing.Icon.ExtractAssociatedIcon(exePath); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to extract icon: {ex.Message}"); }
                    }
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