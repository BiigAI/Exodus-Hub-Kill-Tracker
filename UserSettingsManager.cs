using System;
using System.IO;
using System.Text.Json;

namespace ExodusHubKillTrackerWPF
{
    public class UserSettings
    {
        public string GameLogPath { get; set; }
        public string Username { get; set; }
        public string Token { get; set; }
        public bool PlayKillSound { get; set; } = true; // Add this property
    }

    public static class UserSettingsManager
    {
        private static readonly string SettingsFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExodusHub_Kill_Tracker", "user_settings.json");

        public static UserSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    return JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                }
            }
            catch
            {
                // Ignore errors, return default
            }
            return new UserSettings();
        }

        public static void Save(UserSettings settings)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFilePath));
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // Ignore errors
            }
        }
    }
}