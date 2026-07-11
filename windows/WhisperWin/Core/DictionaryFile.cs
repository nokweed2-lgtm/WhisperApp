using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WhisperWin.Core
{
    /// <summary>
    /// Deserialization target for shared/dictionary.json —
    /// { "entries": [...], "pairs": [{ "to_replace", "replace_with" }, ...] }.
    /// "pairs" defaults to an empty list so files written before this feature (just "entries")
    /// still deserialize cleanly.
    /// </summary>
    public sealed class DictionaryFile
    {
        [JsonPropertyName("entries")]
        public List<string> Entries { get; set; } = new();

        [JsonPropertyName("pairs")]
        public List<DictionaryPair> Pairs { get; set; } = new();
    }

    /// <summary>A user-defined "wrong word → right word" replacement pair (see DictionaryFile.Pairs).</summary>
    public sealed class DictionaryPair
    {
        [JsonPropertyName("to_replace")]
        public string ToReplace { get; set; } = "";

        [JsonPropertyName("replace_with")]
        public string ReplaceWith { get; set; } = "";
    }
}
