using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MugiSideBrowser.Managers
{
    public class WebViewManager
    {
        private readonly Dictionary<BookmarkItem, WebView2> _bookmarkWebViews = new();
        private readonly Func<TargetWindow, System.Windows.Controls.Panel> _getContainerForPane;
        private readonly Action<WebView2> _onWebViewCreated;
        private string? _defaultUserAgent;
        private bool _isNavigating = false;

        public WebViewManager(
            Func<TargetWindow, System.Windows.Controls.Panel> getContainerForPane,
            Action<WebView2>? onWebViewCreated = null)
        {
            _getContainerForPane = getContainerForPane;
            _onWebViewCreated = onWebViewCreated ?? (_ => { });
        }

        public bool IsNavigating => _isNavigating;

        public string? DefaultUserAgent => _defaultUserAgent;

        public async Task<WebView2> GetOrCreateWebViewAsync(BookmarkItem item, TargetWindow target, bool isMobileMode)
        {
            if (_bookmarkWebViews.TryGetValue(item, out var cachedWebView))
            {
                return cachedWebView;
            }

            _isNavigating = true;
            try
            {
                var webView = CreateNewWebView();
                _bookmarkWebViews[item] = webView;
                item.IsLoaded = true;

                var initContainer = _getContainerForPane(target);
                if (!initContainer.Children.Contains(webView))
                    initContainer.Children.Add(webView);

                await webView.EnsureCoreWebView2Async();
                
                if (_defaultUserAgent == null)
                    _defaultUserAgent = webView.CoreWebView2.Settings.UserAgent;
                
                if (_defaultUserAgent != null)
                    webView.CoreWebView2.Settings.UserAgent = isMobileMode ? Constants.MobileUserAgent : _defaultUserAgent;

                _onWebViewCreated(webView);
                webView.Source = new Uri(item.Url);
                return webView;
            }
            finally
            {
                _isNavigating = false;
            }
        }

        public WebView2? GetWebView(BookmarkItem item)
        {
            return _bookmarkWebViews.TryGetValue(item, out var webView) ? webView : null;
        }

        public bool HasWebView(BookmarkItem item)
        {
            return _bookmarkWebViews.ContainsKey(item);
        }

        public void RemoveWebViewFromContainer(WebView2 webView, System.Windows.Controls.Panel topHolder, System.Windows.Controls.Panel middleHolder, System.Windows.Controls.Panel bottomHolder)
        {
            if (topHolder.Children.Contains(webView))
                topHolder.Children.Remove(webView);
            if (middleHolder.Children.Contains(webView))
                middleHolder.Children.Remove(webView);
            if (bottomHolder.Children.Contains(webView))
                bottomHolder.Children.Remove(webView);
        }

        public void AddWebViewToContainer(WebView2 webView, System.Windows.Controls.Panel container)
        {
            if (!container.Children.Contains(webView))
                container.Children.Add(webView);
        }

        public void DisposeBookmarkWebView(BookmarkItem item, System.Windows.Controls.Panel topHolder, System.Windows.Controls.Panel middleHolder, System.Windows.Controls.Panel bottomHolder)
        {
            if (_bookmarkWebViews.TryGetValue(item, out var wv))
            {
                RemoveWebViewFromContainer(wv, topHolder, middleHolder, bottomHolder);

                try
                {
                    wv.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error disposing WebView for bookmark: {ex.Message}");
                }
                _bookmarkWebViews.Remove(item);
            }

            item.IsLoaded = false;
        }

        public void DisposeAll(System.Windows.Controls.Panel topHolder, System.Windows.Controls.Panel middleHolder, System.Windows.Controls.Panel bottomHolder)
        {
            foreach (var wv in _bookmarkWebViews.Values)
            {
                try
                {
                    RemoveWebViewFromContainer(wv, topHolder, middleHolder, bottomHolder);
                    wv.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error disposing WebView: {ex.Message}");
                }
            }
            _bookmarkWebViews.Clear();
        }

        public void UpdateUserAgentForAll(bool isMobileMode)
        {
            string targetUA = isMobileMode ? Constants.MobileUserAgent : (_defaultUserAgent ?? "");
            if (string.IsNullOrEmpty(targetUA)) return;

            foreach (var kvp in _bookmarkWebViews)
            {
                if (kvp.Value.CoreWebView2 != null)
                {
                    kvp.Value.CoreWebView2.Settings.UserAgent = targetUA;
                }
            }
        }

        private WebView2 CreateNewWebView()
        {
            var wv = new WebView2
            {
                Margin = new Thickness(0),
                Visibility = Visibility.Visible
            };
            return wv;
        }
    }
}
