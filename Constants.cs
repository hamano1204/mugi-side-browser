namespace MugiSideBrowser
{
    public static class Constants
    {
        // Window dimensions
        public const double FullWidthDefault = 460;
        public const double TriggerWidth = 2;
        public const double TriggerZonePixel = 5;
        public const double OffScreenPosition = -30000;
        public const double MinPaneHeight = 200;
        public const double MinPaneWidth = 300;
        public const double MaxPaneWidth = 800;
        public const double WorkAreaMargin = 80;
        public const double TopMargin = 20;

        // Timing
        public const int AutoHideTimerIntervalMs = 100;
        public const int AnimationDurationMs = 200;

        // Drag threshold
        public const double DragThreshold = 5;

        // Data formats
        public const string BookmarkDataFormat = "MugiSideBrowser.BookmarkItem";

        // Window messages
        public const string ShowWindowMessageName = "MugiSideBrowser_ShowWindowMessage";
        public const string AppBarMessageName = "AppBarMessage";

        // User agent
        public const string MobileUserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 15_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/15.0 Mobile/15E148 Safari/604.1";

        // File I/O
        public const int MaxRetries = 3;
        public const int DelayMilliseconds = 100;

        // Mutex
        public const string SingleInstanceMutexName = "Global\\MugiSideBrowser_SingleInstanceMutex";
    }
}
