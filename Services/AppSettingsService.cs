using System.IO;
using System.Text.Json;
using iRacing_Spotter_Generator.Models;

namespace iRacing_Spotter_Generator.Services
{
    /// <summary>
    /// Loads and saves local application settings (e.g. the Google Cloud API key)
    /// to %AppData%\iRacingSpotterGenerator\settings.json.
    /// </summary>
    public static class AppSettingsService
    {
        private static readonly string SettingsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "iRacingSpotterGenerator");

        private static readonly string SettingsFilePath = Path.Combine(SettingsFolder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings is not null)
                    {
                        return settings;
                    }
                }
            }
            catch (IOException)
            {
                // Fall back to defaults if the settings file could not be read.
            }
            catch (JsonException)
            {
                // Fall back to defaults if the settings file is corrupted.
            }

            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            Directory.CreateDirectory(SettingsFolder);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
    }
}
