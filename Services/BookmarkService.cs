using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MugiSideBrowser.Services
{
    public class BookmarkService
    {
        private ObservableCollection<BookmarkItem> _bookmarks;
        public ObservableCollection<BookmarkItem> Bookmarks => _bookmarks;
        private readonly System.Threading.SemaphoreSlim _saveSemaphore = new System.Threading.SemaphoreSlim(1, 1);

        public BookmarkService()
        {
            _bookmarks = new ObservableCollection<BookmarkItem>();
        }

        public async Task InitializeAsync()
        {
            var items = await BookmarkManager.LoadAsync();
            _bookmarks.Clear();
            foreach (var item in items)
            {
                _bookmarks.Add(item);
            }
        }

        public async Task AddBookmarkAsync(BookmarkItem item)
        {
            if (item.IsSeparator || !_bookmarks.Any(b => b.Url == item.Url))
            {
                _bookmarks.Add(item);
                await SaveAsync();
            }
        }

        public async Task RemoveBookmarkAsync(BookmarkItem item)
        {
            if (_bookmarks.Contains(item))
            {
                _bookmarks.Remove(item);
                await SaveAsync();
            }
        }

        public async Task MoveBookmarkAsync(BookmarkItem source, BookmarkItem target)
        {
            if (source == target) return;

            int sourceIndex = _bookmarks.IndexOf(source);
            int targetIndex = _bookmarks.IndexOf(target);

            if (sourceIndex < 0 || targetIndex < 0) return;

            _bookmarks.Move(sourceIndex, targetIndex);
            await SaveAsync();
        }

        private async Task SaveAsync()
        {
            await _saveSemaphore.WaitAsync();
            try
            {
                await BookmarkManager.SaveAsync(_bookmarks.ToList());
            }
            finally
            {
                _saveSemaphore.Release();
            }
        }
    }
}
