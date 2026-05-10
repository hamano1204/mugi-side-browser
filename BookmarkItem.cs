using System;

namespace MugiSideBrowser
{
    public class BookmarkItem
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        
        public string FaviconUrl 
        {
            get
            {
                try
                {
                    var uri = new Uri(Url);
                    return $"https://www.google.com/s2/favicons?domain={uri.Host}&sz=64";
                }
                catch
                {
                    return "https://www.google.com/s2/favicons?domain=google.com&sz=64";
                }
            }
        }
    }
}
