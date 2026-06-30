using System;
using System.Windows;
using System.Windows.Controls;

namespace MugiSideBrowser.Managers
{
    public enum TargetWindow
    {
        Top,
        Middle,
        Bottom
    }

    public enum SplitMode
    {
        Single,
        Double,
        Triple
    }

    public class PaneManager
    {
        private bool _isMiddlePaneOpen = false;
        private bool _isBottomPaneOpen = false;
        private BookmarkItem? _activeBookmarkTop;
        private BookmarkItem? _activeBookmarkMiddle;
        private BookmarkItem? _activeBookmarkBottom;
        private TargetWindow _activePane = TargetWindow.Top;

        public SplitMode CurrentSplitMode
        {
            get
            {
                if (_isMiddlePaneOpen && _isBottomPaneOpen) return SplitMode.Triple;
                if (_isMiddlePaneOpen || _isBottomPaneOpen) return SplitMode.Double;
                return SplitMode.Single;
            }
        }

        public bool IsMiddlePaneOpen => _isMiddlePaneOpen;
        public bool IsBottomPaneOpen => _isBottomPaneOpen;
        public TargetWindow ActivePane => _activePane;
        public BookmarkItem? ActiveBookmarkTop => _activeBookmarkTop;
        public BookmarkItem? ActiveBookmarkMiddle => _activeBookmarkMiddle;
        public BookmarkItem? ActiveBookmarkBottom => _activeBookmarkBottom;

        public event Action? SplitLayoutChanged;
        public event Action? ActivePaneChanged;

        public void SetActivePane(TargetWindow pane)
        {
            _activePane = pane;
            ActivePaneChanged?.Invoke();
        }

        public void SetActiveBookmarkForPane(TargetWindow pane, BookmarkItem? item)
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

        public BookmarkItem? GetActiveBookmarkForPane(TargetWindow pane)
        {
            return pane switch
            {
                TargetWindow.Top => _activeBookmarkTop,
                TargetWindow.Middle => _activeBookmarkMiddle,
                TargetWindow.Bottom => _activeBookmarkBottom,
                _ => null
            };
        }

        public void OpenMiddlePane()
        {
            _isMiddlePaneOpen = true;
            SplitLayoutChanged?.Invoke();
        }

        public void OpenBottomPane()
        {
            _isBottomPaneOpen = true;
            SplitLayoutChanged?.Invoke();
        }

        public void CloseMiddlePane()
        {
            _isMiddlePaneOpen = false;
            _activeBookmarkMiddle = null;
            if (_activePane == TargetWindow.Middle)
            {
                _activePane = TargetWindow.Top;
                ActivePaneChanged?.Invoke();
            }
            SplitLayoutChanged?.Invoke();
        }

        public void CloseBottomPane()
        {
            _isBottomPaneOpen = false;
            _activeBookmarkBottom = null;
            if (_activePane == TargetWindow.Bottom)
            {
                _activePane = TargetWindow.Top;
                ActivePaneChanged?.Invoke();
            }
            SplitLayoutChanged?.Invoke();
        }

        public void CloseTopPane()
        {
            if (_isMiddlePaneOpen && _isBottomPaneOpen)
            {
                var midBookmark = _activeBookmarkMiddle;
                var botBookmark = _activeBookmarkBottom;

                _activeBookmarkTop = midBookmark;
                _activeBookmarkMiddle = botBookmark;
                _activeBookmarkBottom = null;

                _isBottomPaneOpen = false;
                _activePane = TargetWindow.Top;
            }
            else if (_isMiddlePaneOpen)
            {
                _activeBookmarkTop = _activeBookmarkMiddle;
                _activeBookmarkMiddle = null;
                _isMiddlePaneOpen = false;
                _activePane = TargetWindow.Top;
            }
            else if (_isBottomPaneOpen)
            {
                _activeBookmarkTop = _activeBookmarkBottom;
                _activeBookmarkBottom = null;
                _isBottomPaneOpen = false;
                _activePane = TargetWindow.Top;
            }

            SplitLayoutChanged?.Invoke();
        }

        public void CloseActivePane()
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
            ActivePaneChanged?.Invoke();
        }

        public void SwapPanes(TargetWindow source, TargetWindow target)
        {
            BookmarkItem? sourceBookmark = GetActiveBookmarkForPane(source);
            BookmarkItem? targetBookmark = GetActiveBookmarkForPane(target);

            SetActiveBookmarkForPane(source, targetBookmark);
            SetActiveBookmarkForPane(target, sourceBookmark);

            if (_activePane == source)
                _activePane = target;
            else if (_activePane == target)
                _activePane = source;

            ActivePaneChanged?.Invoke();
            SplitLayoutChanged?.Invoke();
        }

        public void Reset()
        {
            _isMiddlePaneOpen = false;
            _isBottomPaneOpen = false;
            _activeBookmarkTop = null;
            _activeBookmarkMiddle = null;
            _activeBookmarkBottom = null;
            _activePane = TargetWindow.Top;
        }
    }
}
