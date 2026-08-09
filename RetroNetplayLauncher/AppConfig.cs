using System;
using System.IO;
using System.Text.Json;

namespace RetroNetplayLauncher
{
    public class AppConfig
    {
        // Default to a fallback loopback or your common current IP address
        public string LastUsedServerIP { get; set; } = "127.0.0.1";

        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher_settings.json");
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        public static AppConfig Load()
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppConfig();
            }

            try
            {
                string json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig(); // Revert to defaults if the file becomes corrupted
            }
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, Options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save launcher settings: {ex.Message}");
            }
        }
    }
}
