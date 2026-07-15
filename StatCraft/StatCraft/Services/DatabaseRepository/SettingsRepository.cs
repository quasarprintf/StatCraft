using System.IO;
using System.Text.Json;
using StatCraft.Models;

namespace StatCraft.Services.DatabaseRepository
{
    public class SettingsRepository
    {
        private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

        private readonly string _filePath;

        public SettingsRepository(string filePath)
        {
            _filePath = filePath;
        }

        public AppSettingsData Load()
        {
            if (!File.Exists(_filePath))
            {
                AppSettingsData defaultSettings = new AppSettingsData { BaseReplayFolderPath = null };
                Save(defaultSettings);
                return defaultSettings;
            }

            string json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettingsData>(json) ?? new AppSettingsData();
        }

        public void Save(AppSettingsData settings)
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(_filePath, json);
        }
    }
}
