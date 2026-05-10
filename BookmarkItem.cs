using System;

namespace MugiSideBrowser
{
    public class BookmarkItem
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string FaviconUrl { get; set; } = string.Empty;

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
                    return "https://www.google.com/s2/favicons?domain=google.com&sz=64";
                }
            }
        }
    }
}
