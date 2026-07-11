using System;
using System.IO;
using WhisperWin.Core;
using Xunit;

namespace WhisperWin.Tests
{
    /// <summary>
    /// Exercises the REAL shared/correction-prompt.md and shared/dictionary.json — the files P1
    /// actually edits — through the production resolution path (SharedPaths.ResolveSharedDirectory
    /// + PromptBuilder.BuildSystemPrompt), instead of the inline template constant used by
    /// PromptBuilderTests.cs. That inline constant means a broken placeholder, stale wording, or a
    /// Mac/Windows drift in the real file would never fail CI — this closes that gap (flagged by
    /// P3 in Phase 1 as a drift risk).
    ///
    /// Relies on running from windows/WhisperWin.Tests/bin/&lt;config&gt;/net8.0-windows/ inside the
    /// repo checkout, so SharedPaths' walk-up finds the real top-level shared/ folder (not the
    /// embedded build-output copy) — see SharedPathsTests for the walk-up algorithm itself.
    /// </summary>
    public class PromptRealFileIntegrationTests
    {
        private static string ResolveRealSharedDirectory()
        {
            var resolved = SharedPaths.ResolveSharedDirectory(AppContext.BaseDirectory);
            Assert.True(
                resolved != null,
                $"Could not resolve the repo's shared/ directory from {AppContext.BaseDirectory}. " +
                "This test expects to run from inside the repo checkout.");
            return resolved!;
        }

        [Fact]
        public void RealDictionaryFile_ContainsEightEntriesIncludingTierList()
        {
            var sharedDir = ResolveRealSharedDirectory();
            var file = DictionaryStore.Load(SharedPaths.DictionaryFilePath(sharedDir));

            Assert.Equal(8, file.Entries.Count);
            Assert.Contains("tier list", file.Entries);
            // Original 7 seed entries must still be present and untouched.
            Assert.Contains("Tar Sawang", file.Entries);
            Assert.Contains("Coffee for Worker", file.Entries);
            Assert.Contains("Claude Code", file.Entries);
            Assert.Contains("Wispr Flow", file.Entries);
            Assert.Contains("Take Home", file.Entries);
            Assert.Contains("Micro Level", file.Entries);
            Assert.Contains("Content Direction", file.Entries);
        }

        [Fact]
        public void RealPromptTemplate_HasAllThreePlaceholders()
        {
            var sharedDir = ResolveRealSharedDirectory();
            var template = File.ReadAllText(SharedPaths.PromptFilePath(sharedDir));

            Assert.Contains(PromptBuilder.DictionaryPlaceholder, template);
            Assert.Contains(PromptBuilder.ReplacementsPlaceholder, template);
            Assert.Contains(PromptBuilder.LangHintPlaceholder, template);
        }

        [Fact]
        public void RealPromptTemplate_RendersWithNoStrayPlaceholders()
        {
            var sharedDir = ResolveRealSharedDirectory();
            var template = File.ReadAllText(SharedPaths.PromptFilePath(sharedDir));
            var dictionary = DictionaryStore.Load(SharedPaths.DictionaryFilePath(sharedDir));

            var result = PromptBuilder.BuildSystemPrompt(template, dictionary.Entries, dictionary.Pairs, "th");

            Assert.DoesNotContain("{{", result);
            Assert.DoesNotContain("}}", result);
        }

        [Fact]
        public void RealPromptTemplate_DictionaryEntriesAppearInRenderedOutput()
        {
            var sharedDir = ResolveRealSharedDirectory();
            var template = File.ReadAllText(SharedPaths.PromptFilePath(sharedDir));
            var dictionary = DictionaryStore.Load(SharedPaths.DictionaryFilePath(sharedDir));

            var result = PromptBuilder.BuildSystemPrompt(template, dictionary.Entries, dictionary.Pairs, "th");

            Assert.Contains("- tier list", result);
            Assert.Contains("- Wispr Flow", result);
        }

        [Fact]
        public void RealPromptTemplate_ContainsTierListOverCorrectionGuardAndFewShot()
        {
            // Confirms the ACTUAL shared/correction-prompt.md — the file Windows loads at
            // runtime — carries P1's Rule 3 sound-match guard and the
            // "เชียริต" -> "tier list" (NOT "Wispr Flow") few-shot example added for the
            // over-correction fix. A future edit that touches Rule 3's wording without updating
            // this file (or vice versa on the Mac side) should be caught here.
            var sharedDir = ResolveRealSharedDirectory();
            var template = File.ReadAllText(SharedPaths.PromptFilePath(sharedDir));

            Assert.Contains("SOUND genuinely matches", template);
            Assert.Contains("เชียริต", template);
            Assert.Contains("tier list", template);
            Assert.Contains("NOT \"Wispr Flow\"", template);
            // The few-shot annotation must teach WHY "tier list" wins and "Wispr Flow" loses:
            // sound-match, not topical adjacency. Guards the reworded note against regressing to
            // the earlier (self-contradictory) "does not sound like any dictionary name" wording.
            Assert.Contains("topically adjacent but the sound does not match", template);
        }
    }
}
