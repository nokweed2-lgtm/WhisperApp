using System;
using System.Collections.Generic;
using System.Linq;

namespace WhisperWin.Core
{
    /// <summary>
    /// Assembles the correction system prompt from the shared prompt template, the custom
    /// dictionary entries, and a language hint — mirroring
    /// Sources/TextCorrectionService.swift so both apps behave identically. Pure string logic,
    /// no I/O, so it is fully unit-testable.
    /// </summary>
    public static class PromptBuilder
    {
        public const string DictionaryPlaceholder = "{{DICTIONARY}}";
        public const string LangHintPlaceholder = "{{LANG_HINT}}";
        public const string ReplacementsPlaceholder = "{{REPLACEMENTS}}";

        /// <summary>
        /// Builds the language hint line for the given language code, matching the Mac app's
        /// switch in TextCorrectionService.correct(text:language:).
        /// </summary>
        public static string LanguageHint(string? language)
        {
            return language switch
            {
                "th" => "The text is primarily Thai, possibly mixed with English terms",
                "en" => "The text is in English",
                _ => "The text may be Thai, English, or mixed — keep each part in its original language",
            };
        }

        /// <summary>Renders the dictionary entries as a "- Entry" bullet list, one per line.</summary>
        public static string RenderDictionaryList(IEnumerable<string> entries)
        {
            return string.Join("\n", entries.Select(e => $"- {e}"));
        }

        /// <summary>
        /// Renders user-defined "wrong → right" pairs as a prompt block. Must stay byte-identical
        /// to TextCorrectionService.renderReplacements on the Mac side (Sources/TextCorrectionService.swift).
        /// Empty pairs → empty string, so the {{REPLACEMENTS}} slot leaves no dangling header.
        /// </summary>
        public static string RenderReplacements(IEnumerable<DictionaryPair> pairs)
        {
            var list = pairs.ToList();
            if (list.Count == 0)
            {
                return string.Empty;
            }

            const string header = "Word replacements — apply these exact substitutions when the left-hand phrase appears (use context; do not replace inside unrelated words):";
            var lines = list.Select(p => $"- \"{p.ToReplace}\" → \"{p.ReplaceWith}\"");
            return string.Join("\n", new[] { header }.Concat(lines));
        }

        /// <summary>
        /// Substitutes {{DICTIONARY}}, {{REPLACEMENTS}}, and {{LANG_HINT}} into the raw prompt
        /// template loaded from shared/correction-prompt.md.
        /// </summary>
        public static string BuildSystemPrompt(
            string template,
            IEnumerable<string> dictionaryEntries,
            IEnumerable<DictionaryPair> pairs,
            string? language)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            var dictionaryList = RenderDictionaryList(dictionaryEntries);
            var replacements = RenderReplacements(pairs);
            var langHint = LanguageHint(language);

            return template
                .Replace(DictionaryPlaceholder, dictionaryList)
                .Replace(ReplacementsPlaceholder, replacements)
                .Replace(LangHintPlaceholder, langHint);
        }
    }
}
