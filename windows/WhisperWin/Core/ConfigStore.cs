using System;
using System.IO;
using System.Text.Json;

namespace WhisperWin.Core
{
    /// <summary>
    /// Loads/saves <see cref="AppConfig"/> as JSON. Takes the target file path explicitly (rather
    /// than resolving %APPDATA% itself) so it is trivially unit-testable against a temp file.
    /// </summary>
    public sealed class ConfigStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
        };

        public string FilePath { get; }

        public ConfigStore(string filePath)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        /// <summary>Default production path: %APPDATA%\WhisperWin\config.json.</summary>
        public static string DefaultFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "WhisperWin", "config.json");
        }

        public AppConfig Load()
        {
            if (!File.Exists(FilePath))
            {
                return AppConfig.CreateDefault();
            }

            try
            {
                var json = File.ReadAllText(FilePath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                return config ?? AppConfig.CreateDefault();
            }
            catch (JsonException)
            {
                // Corrupt config file — fall back to defaults rather than crashing startup.
                return AppConfig.CreateDefault();
            }
            catch (IOException)
            {
                return AppConfig.CreateDefault();
            }
        }

        public void Save(AppConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
    }
}
