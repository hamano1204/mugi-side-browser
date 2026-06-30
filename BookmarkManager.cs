using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace MugiSideBrowser
{
    public static class BookmarkManager
    {
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MugiSideBrowser");
        
        private static readonly string FilePath = Path.Combine(AppDataPath, "bookmarks.json");

        public static async Task<List<BookmarkItem>> LoadAsync()
        {
            if (!File.Exists(FilePath)) return new List<BookmarkItem>();

            for (int i = 0; i < Constants.MaxRetries; i++)
            {
                try
                {
                    using FileStream stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var bookmarks = await JsonSerializer.DeserializeAsync<List<BookmarkItem>>(stream);
                    return bookmarks ?? new List<BookmarkItem>();
                }
                catch (IOException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"BookmarkManager LoadAsync (attempt {i + 1}): {ex.Message}");
                    if (i == Constants.MaxRetries - 1) return new List<BookmarkItem>();
                    await Task.Delay(Constants.DelayMilliseconds);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"BookmarkManager LoadAsync failed: {ex.Message}");
                    return new List<BookmarkItem>();
                }
            }
            return new List<BookmarkItem>();
        }

        public static async Task SaveAsync(List<BookmarkItem> bookmarks)
        {
            try
            {
                if (!Directory.Exists(AppDataPath))
                {
                    Directory.CreateDirectory(AppDataPath);
                }

                string tempPath = FilePath + ".tmp";

                for (int i = 0; i < Constants.MaxRetries; i++)
                {
                    try
                    {
                        using (FileStream stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await JsonSerializer.SerializeAsync(stream, bookmarks, new JsonSerializerOptions { WriteIndented = true });
                        }
                        File.Move(tempPath, FilePath, overwrite: true);
                        return;
                    }
                    catch (IOException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"BookmarkManager SaveAsync (attempt {i + 1}): {ex.Message}");
                        if (i == Constants.MaxRetries - 1) 
                        {
                            System.Diagnostics.Debug.WriteLine("Failed to save bookmarks after max retries due to file lock.");
                            return;
                        }
                        await Task.Delay(Constants.DelayMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"BookmarkManager SaveAsync failed: {ex.Message}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"BookmarkManager SaveAsync directory creation failed: {ex.Message}");
            }
        }
    }
}
