$file = "MainWindow.xaml.cs"
$content = Get-Content $file -Raw

# Replace field references with manager properties
$content = $content -replace '\b_currentMode\b', '_displayModeManager.CurrentMode'
$content = $content -replace '\b_isSlidOut\b', '_displayModeManager.IsSlidOut'
$content = $content -replace '\b_isDragging\b', '_displayModeManager.IsDragging'
$content = $content -replace '\b_activePane\b', '_paneManager.ActivePane'
$content = $content -replace '\b_activeBookmarkTop\b', '_paneManager.ActiveBookmarkTop'
$content = $content -replace '\b_activeBookmarkMiddle\b', '_paneManager.ActiveBookmarkMiddle'
$content = $content -replace '\b_activeBookmarkBottom\b', '_paneManager.ActiveBookmarkBottom'
$content = $content -replace '\b_currentFullWidth\b', '_displayModeManager.CurrentFullWidth'
$content = $content -replace '\b_currentSplitMode\b', '_paneManager.CurrentSplitMode'
$content = $content -replace '\b_isMiddlePaneOpen\b', '_paneManager.IsMiddlePaneOpen'
$content = $content -replace '\b_isBottomPaneOpen\b', '_paneManager.IsBottomPaneOpen'

# Replace magic numbers with Constants
$content = $content -replace '\bTriggerZonePixel\b', 'Constants.TriggerZonePixel'
$content = $content -replace '\bTriggerWidth\b', 'Constants.TriggerWidth'
$content = $content -replace '\bOffScreenPosition\b', 'Constants.OffScreenPosition'
$content = $content -replace '\bMinPaneHeight\b', 'Constants.MinPaneHeight'
$content = $content -replace '\bMinPaneWidth\b', 'Constants.MinPaneWidth'
$content = $content -replace '\bMaxPaneWidth\b', 'Constants.MaxPaneWidth'
$content = $content -replace '\bAnimationDurationMs\b', 'Constants.AnimationDurationMs'
$content = $content -replace '\bAutoHideTimerIntervalMs\b', 'Constants.AutoHideTimerIntervalMs'
$content = $content -replace '\bBookmarkDataFormat\b', 'Constants.BookmarkDataFormat'
$content = $content -replace '\bMobileUserAgent\b', 'Constants.MobileUserAgent'
$content = $content -replace '\bShowWindowMessageName\b', 'Constants.ShowWindowMessageName'
$content = $content -replace '\bAppBarMessageName\b', 'Constants.AppBarMessageName'
$content = $content -replace '\bSingleInstanceMutexName\b', 'Constants.SingleInstanceMutexName'
$content = $content -replace '\bMaxRetries\b', 'Constants.MaxRetries'
$content = $content -replace '\bDelayMilliseconds\b', 'Constants.DelayMilliseconds'
$content = $content -replace '\bFullWidthDefault\b', 'Constants.FullWidthDefault'

# Fix event subscription
$content = $content -replace '_displayModeManager\.ModeChanged \+= UpdateWindowTitle;', '_displayModeManager.ModeChanged += (mode) => UpdateWindowTitle();'

Set-Content $file $content -NoNewline
Write-Host "Replacements completed"
