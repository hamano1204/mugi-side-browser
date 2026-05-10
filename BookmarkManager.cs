using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MugiSideBrowser
{
    public static class BookmarkManager
    {
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MugiSideBrowser");
        
        private static readonly string FilePath = Path.Combine(AppDataPath, "bookmarks.json");

        public static List<BookmarkItem> Load()
        {
            if (!File.Exists(FilePath)) return new List<BookmarkItem>();

            try
            {
                string json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<List<BookmarkItem>>(json) ?? new List<BookmarkItem>();
            }
            catch
            {
                return new List<BookmarkItem>();
            }
        }

        public static void Save(List<BookmarkItem> bookmarks)
        {
            try
            {
                if (!Directory.Exists(AppDataPath))
                {
                    Directory.CreateDirectory(AppDataPath);
                }
                string json = JsonSerializer.Serialize(bookmarks, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save bookmarks: {ex.Message}");
            }
        }
    }
}
