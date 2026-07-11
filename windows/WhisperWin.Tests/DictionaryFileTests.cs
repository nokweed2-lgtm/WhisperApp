using System.Text.Json;
using WhisperWin.Core;
using Xunit;

namespace WhisperWin.Tests
{
    public class DictionaryFileTests
    {
        [Fact]
        public void Deserialize_LegacyFile_WithoutPairs_DefaultsPairsToEmptyList()
        {
            // shared/dictionary.json as it exists today — 7 words in "entries", no "pairs" key.
            const string json = "{ \"entries\": [\"Tar Sawang\", \"Claude Code\"] }";

            var file = JsonSerializer.Deserialize<DictionaryFile>(json);

            Assert.NotNull(file);
            Assert.Equal(new[] { "Tar Sawang", "Claude Code" }, file!.Entries);
            Assert.Empty(file.Pairs);
        }

        [Fact]
        public void Deserialize_NewFile_WithPairs_ReadsBothEntriesAndPairs()
        {
            const string json = @"{
                ""entries"": [""Tar Sawang""],
                ""pairs"": [
                    { ""to_replace"": ""ติมสแกน"", ""replace_with"": ""Theme Scan"" },
                    { ""to_replace"": ""ฟอลเดชั่น"", ""replace_with"": ""foundation"" }
                ]
            }";

            var file = JsonSerializer.Deserialize<DictionaryFile>(json);

            Assert.NotNull(file);
            Assert.Single(file!.Entries);
            Assert.Equal(2, file.Pairs.Count);
            Assert.Equal("ติมสแกน", file.Pairs[0].ToReplace);
            Assert.Equal("Theme Scan", file.Pairs[0].ReplaceWith);
            Assert.Equal("ฟอลเดชั่น", file.Pairs[1].ToReplace);
            Assert.Equal("foundation", file.Pairs[1].ReplaceWith);
        }

        [Fact]
        public void Deserialize_EmptyPairsArray_ReturnsEmptyList()
        {
            const string json = "{ \"entries\": [\"X\"], \"pairs\": [] }";

            var file = JsonSerializer.Deserialize<DictionaryFile>(json);

            Assert.NotNull(file);
            Assert.Empty(file!.Pairs);
        }

        [Fact]
        public void Serialize_RoundTripsPairsWithSnakeCaseKeys()
        {
            var file = new DictionaryFile
            {
                Entries = { "Claude Code" },
                Pairs = { new DictionaryPair { ToReplace = "คลอดโค้ด", ReplaceWith = "Claude Code" } },
            };

            var json = JsonSerializer.Serialize(file);

            Assert.Contains("\"to_replace\"", json);
            Assert.Contains("\"replace_with\"", json);

            var roundTripped = JsonSerializer.Deserialize<DictionaryFile>(json);
            Assert.Equal("คลอดโค้ด", roundTripped!.Pairs[0].ToReplace);
            Assert.Equal("Claude Code", roundTripped.Pairs[0].ReplaceWith);
        }

        // ── Edge cases P1 did not cover ──

        [Fact]
        public void Deserialize_EmptyObject_DefaultsBothEntriesAndPairsToEmpty()
        {
            // Degenerate but possible if a future write path serializes an empty DictionaryFile.
            const string json = "{}";

            var file = JsonSerializer.Deserialize<DictionaryFile>(json);

            Assert.NotNull(file);
            Assert.Empty(file!.Entries);
            Assert.Empty(file.Pairs);
        }

        [Fact]
        public void Deserialize_PairsPresentButEntriesMissing_EntriesDefaultsEmpty()
        {
            // Asymmetric legacy case: a hand-edited file with "pairs" but no "entries" key.
            const string json = @"{ ""pairs"": [{ ""to_replace"": ""a"", ""replace_with"": ""b"" }] }";

            var file = JsonSerializer.Deserialize<DictionaryFile>(json);

            Assert.NotNull(file);
            Assert.Empty(file!.Entries);
            Assert.Single(file.Pairs);
        }

        [Fact]
        public void Deserialize_PairMissingReplaceWith_DefaultsToEmptyString()
        {
            // A malformed pair (only one of the two required fields present) should not throw —
            // DictionaryPair.ReplaceWith has a "" default, so this deserializes rather than crashing
            // the whole correction pipeline over one bad entry.
            const string json = @"{ ""entries"": [], ""pairs"": [{ ""to_replace"": ""a"" }] }";

            var file = JsonSerializer.Deserialize<DictionaryFile>(json);

            Assert.NotNull(file);
            Assert.Equal("a", file!.Pairs[0].ToReplace);
            Assert.Equal("", file.Pairs[0].ReplaceWith);
        }

        [Fact]
        public void Deserialize_UnknownExtraFields_AreIgnored()
        {
            // Forward-compat: a newer schema version (e.g. Phase 3's source/starred fields)
            // must not break today's deserializer.
            const string json = @"{
                ""entries"": [""X""],
                ""pairs"": [{ ""to_replace"": ""a"", ""replace_with"": ""b"", ""starred"": true, ""source"": ""manual"" }],
                ""future_key"": 123
            }";

            var file = JsonSerializer.Deserialize<DictionaryFile>(json);

            Assert.NotNull(file);
            Assert.Single(file!.Pairs);
            Assert.Equal("a", file.Pairs[0].ToReplace);
            Assert.Equal("b", file.Pairs[0].ReplaceWith);
        }

        [Fact]
        public void Deserialize_MalformedJson_ThrowsJsonException()
        {
            // App.xaml.cs.BuildSystemPrompt does not currently catch JsonException around this
            // deserialize call — confirm the exact failure mode so a corrupt shared/dictionary.json
            // (e.g. truncated by a crashed write, or a bad manual edit) has a documented behavior:
            // dictation breaks loudly rather than silently losing pairs.
            const string malformed = @"{ ""entries"": [""X"" ""pairs"": [] }"; // missing comma

            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DictionaryFile>(malformed));
        }

        [Fact]
        public void Deserialize_EntriesWithThaiUnicodeAndPairs_BothDecodeCorrectly()
        {
            const string json = @"{
                ""entries"": [""Tar Sawang"", ""คำไทยล้วน""],
                ""pairs"": [
                    { ""to_replace"": ""ทำติมสแกนทีหนึ่งถึงทีสิบห้าฟอลเดชั่น"", ""replace_with"": ""Theme Scan T1–T15 foundation"" }
                ]
            }";

            var file = JsonSerializer.Deserialize<DictionaryFile>(json);

            Assert.NotNull(file);
            Assert.Contains("คำไทยล้วน", file!.Entries);
            Assert.Equal("ทำติมสแกนทีหนึ่งถึงทีสิบห้าฟอลเดชั่น", file.Pairs[0].ToReplace);
            Assert.Equal("Theme Scan T1–T15 foundation", file.Pairs[0].ReplaceWith);
        }

        [Fact]
        public void Deserialize_NullJson_ReturnsNull()
        {
            // System.Text.Json deserializes the literal "null" to a null reference rather than
            // throwing — App.xaml.cs relies on `?? new DictionaryFile()` to guard against this.
            var file = JsonSerializer.Deserialize<DictionaryFile>("null");

            Assert.Null(file);
        }
    }
}
