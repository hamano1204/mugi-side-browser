using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;

namespace MugiSideBrowser
{
    public partial class MainWindow : Window
    {
        private AppBarHelper _appBarHelper;
        private ObservableCollection<BookmarkItem> _bookmarks = new();
        private Point _dragStartPoint;
        private const string BookmarkDataFormat = "MugiSideBrowser.BookmarkItem";

        public MainWindow()
        {
            InitializeComponent();
            _appBarHelper = new AppBarHelper(this);
            
            this.SourceInitialized += MainWindow_SourceInitialized;
            this.Closing += MainWindow_Closing;
            this.StateChanged += MainWindow_StateChanged;
            
            InitializeWebView();
            LoadBookmarks();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                _appBarHelper.Unregister();
            }
            else if (this.WindowState == WindowState.Normal)
            {
                _appBarHelper.Register();
            }
        }

        private void LoadBookmarks()
        {
            var list = BookmarkManager.Load();
            _bookmarks = new ObservableCollection<BookmarkItem>(list);
            BookmarkList.ItemsSource = _bookmarks;
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

        private void Star_Click(object sender, RoutedEventArgs e)
        {
            if (webView.CoreWebView2 == null) return;

            string url = webView.Source.ToString();
            string title = webView.CoreWebView2.DocumentTitle;

            if (string.IsNullOrEmpty(title)) title = url;

            // すでに登録されているか確認
            if (_bookmarks.Any(b => b.Url == url))
            {
                return;
            }

            var newItem = new BookmarkItem { Title = title, Url = url };
            _bookmarks.Add(newItem);
            BookmarkManager.Save(_bookmarks.ToList());
        }

        private void Bookmark_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is BookmarkItem item)
            {
                webView.Source = new Uri(item.Url);
            }
        }

        private void Bookmark_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void Bookmark_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _dragStartPoint - mousePos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is FrameworkElement element && element.DataContext is BookmarkItem item)
                    {
                        DataObject dragData = new DataObject(BookmarkDataFormat, item);
                        DragDrop.DoDragDrop(element, dragData, DragDropEffects.Move);
                    }
                }
            }
        }

        private void Bookmark_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(BookmarkDataFormat))
            {
                e.Effects = DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.Move;
            }
            e.Handled = true;
        }

        private void Bookmark_Drop(object sender, DragEventArgs e)
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
            if (sender is MenuItem menuItem && menuItem.DataContext is BookmarkItem item)
            {
                _bookmarks.Remove(item);
                BookmarkManager.Save(_bookmarks.ToList());
            }
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