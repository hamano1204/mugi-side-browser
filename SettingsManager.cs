using System;
using System.IO;
using System.Text.Json;

namespace MugiSideBrowser
{
    public class UserSettings
    {
        public string Theme { get; set; } = "dark";
        public string SidebarPosition { get; set; } = "left";
    }

    public static class SettingsManager
    {
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MugiSideBrowser");
        
        private static readonly string FilePath = Path.Combine(AppDataPath, "settings.json");
        private static UserSettings _settings = new();

        static SettingsManager()
        {
            Load();
        }

        public static UserSettings Settings => _settings;

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    var loaded = JsonSerializer.Deserialize<UserSettings>(json);
                    if (loaded != null)
                    {
                        _settings = loaded;
                        return;
                    }
                }
            }
            catch { }
            _settings = new UserSettings();
        }

        public static void Save()
        {
            try
            {
                if (!Directory.Exists(AppDataPath))
                {
                    Directory.CreateDirectory(AppDataPath);
                }
                string json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }
    }
}
