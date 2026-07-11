using System.Text.Json.Serialization;

namespace WhisperWin.Core
{
    /// <summary>
    /// Non-secret application settings persisted as JSON at %APPDATA%\WhisperWin\config.json.
    /// The Groq API key itself is NEVER stored here — see <see cref="CredentialStore"/>.
    /// </summary>
    public sealed class AppConfig
    {
        [JsonPropertyName("useCorrection")]
        public bool UseCorrection { get; set; } = true;

        [JsonPropertyName("launchAtLogin")]
        public bool LaunchAtLogin { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; } = "th";

        [JsonPropertyName("hotkeyVirtualKey")]
        public int HotkeyVirtualKey { get; set; } = HotkeyManager.VK_RCONTROL;

        public static AppConfig CreateDefault() => new();
    }
}
