using System;
using System.IO;
using WhisperWin.Core;
using Xunit;

namespace WhisperWin.Tests
{
    public class ConfigStoreTests : IDisposable
    {
        private readonly string _tempFile;

        public ConfigStoreTests()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), $"whisperwin-test-{Guid.NewGuid():N}.json");
        }

        public void Dispose()
        {
            if (File.Exists(_tempFile))
            {
                File.Delete(_tempFile);
            }
        }

        [Fact]
        public void Load_MissingFile_ReturnsDefaults()
        {
            var store = new ConfigStore(_tempFile);

            var config = store.Load();

            Assert.True(config.UseCorrection);
            Assert.False(config.LaunchAtLogin);
            Assert.Equal("th", config.Language);
        }

        [Fact]
        public void SaveThenLoad_RoundTripsValues()
        {
            var store = new ConfigStore(_tempFile);
            var config = new AppConfig
            {
                UseCorrection = false,
                LaunchAtLogin = true,
                Language = "en",
            };

            store.Save(config);
            var loaded = store.Load();

            Assert.False(loaded.UseCorrection);
            Assert.True(loaded.LaunchAtLogin);
            Assert.Equal("en", loaded.Language);
        }

        [Fact]
        public void Save_CreatesParentDirectoryIfMissing()
        {
            var nestedPath = Path.Combine(Path.GetTempPath(), $"whisperwin-test-dir-{Guid.NewGuid():N}", "config.json");
            var store = new ConfigStore(nestedPath);

            store.Save(AppConfig.CreateDefault());

            Assert.True(File.Exists(nestedPath));

            File.Delete(nestedPath);
            Directory.Delete(Path.GetDirectoryName(nestedPath)!);
        }

        [Fact]
        public void Load_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
        {
            File.WriteAllText(_tempFile, "{ not valid json ");
            var store = new ConfigStore(_tempFile);

            var config = store.Load();

            Assert.True(config.UseCorrection);
        }
    }
}
