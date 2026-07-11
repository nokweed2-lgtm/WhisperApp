using System;
using System.Collections.Generic;
using WhisperWin.Core;
using Xunit;

namespace WhisperWin.Tests
{
    public class PromptBuilderTests
    {
        private const string Template = "Rules go here.\n\nDictionary:\n{{DICTIONARY}}\n\n{{REPLACEMENTS}}\n\nReturn only text.\n{{LANG_HINT}}";

        private static readonly List<DictionaryPair> NoPairs = new();

        [Fact]
        public void BuildSystemPrompt_InjectsDictionaryAsBulletList()
        {
            var entries = new[] { "Tar Sawang", "Claude Code" };

            var result = PromptBuilder.BuildSystemPrompt(Template, entries, NoPairs, "th");

            Assert.Contains("- Tar Sawang", result);
            Assert.Contains("- Claude Code", result);
            Assert.DoesNotContain("{{DICTIONARY}}", result);
        }

        [Fact]
        public void BuildSystemPrompt_InjectsThaiLanguageHint()
        {
            var result = PromptBuilder.BuildSystemPrompt(Template, new List<string>(), NoPairs, "th");

            Assert.Contains("primarily Thai", result);
            Assert.DoesNotContain("{{LANG_HINT}}", result);
        }

        [Fact]
        public void BuildSystemPrompt_InjectsEnglishLanguageHint()
        {
            var result = PromptBuilder.BuildSystemPrompt(Template, new List<string>(), NoPairs, "en");

            Assert.Contains("The text is in English", result);
        }

        [Fact]
        public void BuildSystemPrompt_UnknownLanguage_UsesMixedHint()
        {
            var result = PromptBuilder.BuildSystemPrompt(Template, new List<string>(), NoPairs, "fr");

            Assert.Contains("Thai, English, or mixed", result);
        }

        [Fact]
        public void BuildSystemPrompt_NullLanguage_UsesMixedHint()
        {
            var result = PromptBuilder.BuildSystemPrompt(Template, new List<string>(), NoPairs, null);

            Assert.Contains("Thai, English, or mixed", result);
        }

        [Fact]
        public void RenderDictionaryList_EmptyList_ProducesEmptyString()
        {
            var result = PromptBuilder.RenderDictionaryList(new List<string>());

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void BuildSystemPrompt_PreservesRestOfTemplate()
        {
            var result = PromptBuilder.BuildSystemPrompt(Template, new List<string> { "X" }, NoPairs, "th");

            Assert.StartsWith("Rules go here.", result);
            Assert.Contains("Return only text.", result);
        }

        // ── RenderReplacements — must stay byte-identical to the Mac renderer
        // (Sources/TextCorrectionService.swift renderReplacements) ──

        [Fact]
        public void RenderReplacements_EmptyPairs_ProducesEmptyString()
        {
            var result = PromptBuilder.RenderReplacements(new List<DictionaryPair>());

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        public void RenderReplacements_MatchesExpectedBlockExactly()
        {
            var pairs = new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "ติมสแกน", ReplaceWith = "Theme Scan" },
                new DictionaryPair { ToReplace = "ฟอลเดชั่น", ReplaceWith = "foundation" },
            };

            var expected =
                "Word replacements — apply these exact substitutions when the left-hand phrase appears (use context; do not replace inside unrelated words):\n" +
                "- \"ติมสแกน\" → \"Theme Scan\"\n" +
                "- \"ฟอลเดชั่น\" → \"foundation\"";

            var result = PromptBuilder.RenderReplacements(pairs);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void BuildSystemPrompt_IncludesReplacementsBlock_WhenPairsPresent()
        {
            var pairs = new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "ติมสแกน", ReplaceWith = "Theme Scan" },
            };

            var result = PromptBuilder.BuildSystemPrompt(Template, new List<string>(), pairs, "th");

            Assert.Contains("- \"ติมสแกน\" → \"Theme Scan\"", result);
            Assert.DoesNotContain("{{REPLACEMENTS}}", result);
        }

        [Fact]
        public void BuildSystemPrompt_ReplacementsPlaceholder_BecomesEmpty_WhenNoPairs()
        {
            var result = PromptBuilder.BuildSystemPrompt(Template, new List<string>(), NoPairs, "th");

            Assert.DoesNotContain("{{REPLACEMENTS}}", result);
            Assert.DoesNotContain("Word replacements —", result);
        }

        // ── Edge cases P1 did not cover ──

        [Fact]
        public void RenderReplacements_ValuesContainQuotesAndArrow_RenderedVerbatimNoEscaping()
        {
            // A pair whose own text contains a literal `"` and the U+2192 arrow itself.
            // The renderer must not crash and must not attempt any escaping — it's a plain
            // string template, so the output legitimately contains nested quotes.
            var pairs = new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "say \"hi\"", ReplaceWith = "a → b" },
            };

            var result = PromptBuilder.RenderReplacements(pairs);

            Assert.Equal(
                "Word replacements — apply these exact substitutions when the left-hand phrase appears (use context; do not replace inside unrelated words):\n" +
                "- \"say \"hi\"\" → \"a → b\"",
                result);
        }

        [Fact]
        public void RenderReplacements_DuplicatePairs_AreNotDeduplicated()
        {
            // Matches the Mac renderer: no dedup logic, each pair becomes its own line even
            // if identical to another.
            var pairs = new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "x", ReplaceWith = "y" },
                new DictionaryPair { ToReplace = "x", ReplaceWith = "y" },
            };

            var result = PromptBuilder.RenderReplacements(pairs);

            var lineCount = result.Split('\n').Length;
            Assert.Equal(3, lineCount); // header + 2 identical lines
        }

        [Fact]
        public void RenderReplacements_EmptyStringValues_DoesNotCrash()
        {
            var pairs = new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "", ReplaceWith = "" },
            };

            var result = PromptBuilder.RenderReplacements(pairs);

            Assert.Contains("- \"\" → \"\"", result);
        }

        [Fact]
        public void RenderReplacements_PreservesInputOrder()
        {
            var pairs = new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "one", ReplaceWith = "1" },
                new DictionaryPair { ToReplace = "two", ReplaceWith = "2" },
                new DictionaryPair { ToReplace = "three", ReplaceWith = "3" },
            };

            var result = PromptBuilder.RenderReplacements(pairs);
            var lines = result.Split('\n');

            Assert.Equal("- \"one\" → \"1\"", lines[1]);
            Assert.Equal("- \"two\" → \"2\"", lines[2]);
            Assert.Equal("- \"three\" → \"3\"", lines[3]);
        }

        [Fact]
        public void BuildSystemPrompt_NullTemplate_ThrowsArgumentNullException()
        {
            // App.xaml.cs reads the template via File.ReadAllText, which throws its own
            // exception on a missing file before ever calling BuildSystemPrompt — but if a
            // caller ever passes null directly, this should fail loudly, not silently
            // NullReferenceException deep inside string.Replace.
            Assert.Throws<ArgumentNullException>(() =>
                PromptBuilder.BuildSystemPrompt(null!, new List<string>(), NoPairs, "th"));
        }

        [Fact]
        public void BuildSystemPrompt_EntriesAndPairsBothPresent_BothRendered()
        {
            var entries = new[] { "Claude Code", "Tar Sawang" };
            var pairs = new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "คลอดโค้ด", ReplaceWith = "Claude Code" },
            };

            var result = PromptBuilder.BuildSystemPrompt(Template, entries, pairs, "th");

            Assert.Contains("- Claude Code", result);
            Assert.Contains("- Tar Sawang", result);
            Assert.Contains("- \"คลอดโค้ด\" → \"Claude Code\"", result);
        }

        [Fact]
        public void RenderReplacements_ThaiUnicodePairs_RoundTripCorrectly()
        {
            // Mixed Thai/English pair values, including Thai combining vowel/tone marks,
            // must not be mangled by the renderer (no normalization, no byte truncation).
            var pairs = new List<DictionaryPair>
            {
                new DictionaryPair { ToReplace = "เด็สต์ทอป", ReplaceWith = "desktop" },
                new DictionaryPair { ToReplace = "ด็สบอร์ต", ReplaceWith = "dashboard" },
            };

            var result = PromptBuilder.RenderReplacements(pairs);

            Assert.Contains("- \"เด็สต์ทอป\" → \"desktop\"", result);
            Assert.Contains("- \"ด็สบอร์ต\" → \"dashboard\"", result);
        }
    }
}
