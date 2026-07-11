using System;
using System.Collections.Generic;
using System.IO;
using WhisperWin.Core;
using Xunit;

namespace WhisperWin.Tests
{
    public class DictionaryStoreTests : IDisposable
    {
        private readonly string _tempFile;

        public DictionaryStoreTests()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), $"whisperwin-dict-{Guid.NewGuid():N}.json");
        }

        public void Dispose()
        {
            if (File.Exists(_tempFile))
            {
                File.Delete(_tempFile);
            }
        }

        [Fact]
        public void Load_MissingFile_ReturnsEmptyDictionaryFile()
        {
            var file = DictionaryStore.Load(_tempFile);

            Assert.Empty(file.Entries);
            Assert.Empty(file.Pairs);
        }

        [Fact]
        public void SaveEntries_MissingFile_CreatesNewFileWithEntriesAndEmptyPairs()
        {
            DictionaryStore.SaveEntries(_tempFile, new List<string> { "Claude Code" });

            var file = DictionaryStore.Load(_tempFile);
            Assert.Equal(new[] { "Claude Code" }, file.Entries);
            Assert.Empty(file.Pairs);
        }

        [Fact]
        public void SavePairs_MissingFile_CreatesNewFileWithPairsAndEmptyEntries()
        {
            DictionaryStore.SavePairs(_tempFile, new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "คลอดโค้ด", ReplaceWith = "Claude Code" },
            });

            var file = DictionaryStore.Load(_tempFile);
            Assert.Empty(file.Entries);
            Assert.Single(file.Pairs);
            Assert.Equal("คลอดโค้ด", file.Pairs[0].ToReplace);
        }

        [Fact]
        public void SaveEntries_PreservesExistingPairsOnDisk()
        {
            DictionaryStore.SavePairs(_tempFile, new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "a", ReplaceWith = "b" },
            });

            DictionaryStore.SaveEntries(_tempFile, new List<string> { "New Entry" });

            var file = DictionaryStore.Load(_tempFile);
            Assert.Equal(new[] { "New Entry" }, file.Entries);
            Assert.Single(file.Pairs);
            Assert.Equal("a", file.Pairs[0].ToReplace);
        }

        [Fact]
        public void SavePairs_PreservesExistingEntriesOnDisk()
        {
            DictionaryStore.SaveEntries(_tempFile, new List<string> { "Tar Sawang" });

            DictionaryStore.SavePairs(_tempFile, new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "x", ReplaceWith = "y" },
            });

            var file = DictionaryStore.Load(_tempFile);
            Assert.Equal(new[] { "Tar Sawang" }, file.Entries);
            Assert.Single(file.Pairs);
            Assert.Equal("x", file.Pairs[0].ToReplace);
        }

        [Fact]
        public void SaveEntries_UnreadableExistingFile_IsNoOp()
        {
            // Simulates a corrupt/partial write (or a file locked mid-sync) — must not clobber
            // whatever is actually on disk with a fresh file built from just the new entries.
            File.WriteAllText(_tempFile, "{ not valid json ");

            DictionaryStore.SaveEntries(_tempFile, new List<string> { "Should Not Be Written" });

            var raw = File.ReadAllText(_tempFile);
            Assert.Equal("{ not valid json ", raw);
        }

        [Fact]
        public void SavePairs_UnreadableExistingFile_IsNoOp()
        {
            File.WriteAllText(_tempFile, "{ not valid json ");

            DictionaryStore.SavePairs(_tempFile, new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "a", ReplaceWith = "b" },
            });

            var raw = File.ReadAllText(_tempFile);
            Assert.Equal("{ not valid json ", raw);
        }

        [Fact]
        public void SaveEntries_ThaiUnicode_IsNotEscapedAsUnicodeSequences()
        {
            DictionaryStore.SaveEntries(_tempFile, new List<string> { "คำไทยล้วน" });

            var raw = File.ReadAllText(_tempFile);
            Assert.Contains("คำไทยล้วน", raw);
            Assert.DoesNotContain("\\u", raw);
        }

        [Fact]
        public void SavePairs_ThaiUnicode_IsNotEscapedAsUnicodeSequences()
        {
            DictionaryStore.SavePairs(_tempFile, new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "ติมสแกน", ReplaceWith = "Theme Scan" },
            });

            var raw = File.ReadAllText(_tempFile);
            Assert.Contains("ติมสแกน", raw);
            Assert.DoesNotContain("\\u", raw);
        }

        [Fact]
        public void SaveEntries_WritesIndentedJson()
        {
            DictionaryStore.SaveEntries(_tempFile, new List<string> { "X" });

            var raw = File.ReadAllText(_tempFile);
            Assert.Contains("\n", raw);
            Assert.Contains("  ", raw); // indentation present
        }

        [Fact]
        public void SaveEntries_CreatesParentDirectoryIfMissing()
        {
            var nestedPath = Path.Combine(Path.GetTempPath(), $"whisperwin-dict-dir-{Guid.NewGuid():N}", "dictionary.json");

            DictionaryStore.SaveEntries(nestedPath, new List<string> { "X" });

            Assert.True(File.Exists(nestedPath));

            File.Delete(nestedPath);
            Directory.Delete(Path.GetDirectoryName(nestedPath)!);
        }

        [Fact]
        public void SaveEntries_UsesSnakeCasePropertyNamesForPairs()
        {
            DictionaryStore.SavePairs(_tempFile, new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "a", ReplaceWith = "b" },
            });

            var raw = File.ReadAllText(_tempFile);
            Assert.Contains("\"to_replace\"", raw);
            Assert.Contains("\"replace_with\"", raw);
        }

        // ── Edge cases: cross-platform file, degenerate JSON shapes, locked-file safety ──

        [Fact]
        public void Load_FileWrittenByMacApp_ReadsEntriesAndPairsCorrectly()
        {
            // Mimics Sources/DictionaryStore.swift's writeFile(): JSONEncoder with
            // [.prettyPrinted, .withoutEscapingSlashes] — 2-space indent via "  ", raw UTF-8 Thai
            // text (no \uXXXX escaping), and slashes left unescaped. Confirms the Windows reader
            // can consume a real file the Mac app produced, not just its own output.
            const string macWritten = "{\n  \"entries\" : [\n    \"Tar Sawang\",\n    \"คำไทยล้วน\"\n  ],\n  \"pairs\" : [\n    {\n      \"to_replace\" : \"ติมสแกน\",\n      \"replace_with\" : \"Theme Scan\"\n    }\n  ]\n}";
            File.WriteAllText(_tempFile, macWritten);

            var file = DictionaryStore.Load(_tempFile);

            Assert.Equal(new[] { "Tar Sawang", "คำไทยล้วน" }, file.Entries);
            Assert.Single(file.Pairs);
            Assert.Equal("ติมสแกน", file.Pairs[0].ToReplace);
            Assert.Equal("Theme Scan", file.Pairs[0].ReplaceWith);
        }

        [Fact]
        public void Load_EmptyObjectFile_ReturnsEmptyEntriesAndPairs()
        {
            File.WriteAllText(_tempFile, "{}");

            var file = DictionaryStore.Load(_tempFile);

            Assert.Empty(file.Entries);
            Assert.Empty(file.Pairs);
        }

        [Fact]
        public void Load_PairsOnlyFile_NoEntriesKey_ReturnsEmptyEntriesAndParsedPairs()
        {
            File.WriteAllText(_tempFile, "{ \"pairs\": [{ \"to_replace\": \"a\", \"replace_with\": \"b\" }] }");

            var file = DictionaryStore.Load(_tempFile);

            Assert.Empty(file.Entries);
            Assert.Single(file.Pairs);
            Assert.Equal("a", file.Pairs[0].ToReplace);
        }

        [Fact]
        public void SaveEntries_OverwritingLegacySevenWordFile_NewEntriesReplaceOldButPairsUntouched()
        {
            // Reproduces the real shared/dictionary.json shape (7 seed words, no "pairs" key) and
            // confirms a save from the new Settings UI cleanly replaces the word list without
            // losing anything, and without requiring a "pairs" key to already exist.
            const string legacy = "{\n  \"entries\": [\"Tar Sawang\", \"Coffee for Worker\", \"Claude Code\", \"Wispr Flow\", \"Take Home\", \"Micro Level\", \"Content Direction\"]\n}";
            File.WriteAllText(_tempFile, legacy);

            DictionaryStore.SaveEntries(_tempFile, new List<string> { "Tar Sawang", "New Word" });

            var file = DictionaryStore.Load(_tempFile);
            Assert.Equal(new[] { "Tar Sawang", "New Word" }, file.Entries);
            Assert.Empty(file.Pairs);
        }

        [Fact]
        public void SavePairs_OverwritingLegacySevenWordFile_EntriesSurviveUntouched()
        {
            const string legacy = "{\n  \"entries\": [\"Tar Sawang\", \"Coffee for Worker\", \"Claude Code\", \"Wispr Flow\", \"Take Home\", \"Micro Level\", \"Content Direction\"]\n}";
            File.WriteAllText(_tempFile, legacy);

            DictionaryStore.SavePairs(_tempFile, new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "a", ReplaceWith = "b" },
            });

            var file = DictionaryStore.Load(_tempFile);
            Assert.Equal(
                new[] { "Tar Sawang", "Coffee for Worker", "Claude Code", "Wispr Flow", "Take Home", "Micro Level", "Content Direction" },
                file.Entries);
            Assert.Single(file.Pairs);
        }

        [Fact]
        public void Load_FileLockedExclusively_ReturnsEmptyInsteadOfThrowing()
        {
            // Simulates OneDrive mid-sync (or another process) holding an exclusive handle —
            // File.ReadAllText must throw IOException here, and Load must swallow it rather than
            // crashing the Settings window.
            File.WriteAllText(_tempFile, "{ \"entries\": [\"Should Not Be Visible\"] }");

            using (new FileStream(_tempFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var file = DictionaryStore.Load(_tempFile);

                Assert.Empty(file.Entries);
                Assert.Empty(file.Pairs);
            }
        }

        [Fact]
        public void SaveEntries_FileLockedExclusively_IsNoOpAndDoesNotThrow()
        {
            const string original = "{ \"entries\": [\"Untouched\"] }";
            File.WriteAllText(_tempFile, original);

            using (new FileStream(_tempFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var exception = Record.Exception(() =>
                    DictionaryStore.SaveEntries(_tempFile, new List<string> { "Should Not Be Written" }));

                Assert.Null(exception);
            }

            var raw = File.ReadAllText(_tempFile);
            Assert.Equal(original, raw);
        }

        [Fact]
        public void SavePairs_FileLockedExclusively_IsNoOpAndDoesNotThrow()
        {
            const string original = "{ \"entries\": [\"Untouched\"] }";
            File.WriteAllText(_tempFile, original);

            using (new FileStream(_tempFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var exception = Record.Exception(() =>
                    DictionaryStore.SavePairs(_tempFile, new List<DictionaryPair>
                    {
                        new DictionaryPair { ToReplace = "a", ReplaceWith = "b" },
                    }));

                Assert.Null(exception);
            }

            var raw = File.ReadAllText(_tempFile);
            Assert.Equal(original, raw);
        }
    }
}
