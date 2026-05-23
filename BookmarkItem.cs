using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MugiSideBrowser
{
    public class BookmarkItem : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _url = string.Empty;
        private string _faviconUrl = string.Empty;
        private bool _isLoaded = false;
        private bool _isActive = false;
        private bool _isOpen = false;
        private bool _isSeparator = false;

        public string Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }

        public string Url
        {
            get => _url;
            set { if (_url != value) { _url = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayFaviconUrl)); } }
        }

        public string FaviconUrl
        {
            get => _faviconUrl;
            set { if (_faviconUrl != value) { _faviconUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayFaviconUrl)); } }
        }

        [JsonIgnore]
        public bool IsLoaded
        {
            get => _isLoaded;
            set { if (_isLoaded != value) { _isLoaded = value; OnPropertyChanged(); } }
        }

        [JsonIgnore]
        public bool IsActive
        {
            get => _isActive;
            set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
        }

        [JsonIgnore]
        public bool IsOpen
        {
            get => _isOpen;
            set { if (_isOpen != value) { _isOpen = value; OnPropertyChanged(); } }
        }


        public bool IsSeparator
        {
            get => _isSeparator;
            set { if (_isSeparator != value) { _isSeparator = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// アイコンの表示用URLを取得します。FaviconUrlが空の場合はフォールバックを生成します。
        /// </summary>
        public string DisplayFaviconUrl
        {
            get
            {
                if (!string.IsNullOrEmpty(FaviconUrl)) return FaviconUrl;

                try
                {
                    var uri = new Uri(Url);
                    // DuckDuckGoのサービスはサブドメインの識別精度が高い傾向にあります
                    return $"https://icons.duckduckgo.com/ip3/{uri.Host}.ico";
                }
                catch
                {
                    return "pack://application:,,,/globe.png";
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
