using System.Text.RegularExpressions;

namespace WhisperWin.Core
{
    /// <summary>
    /// Strips sound/event annotations some STT engines insert (e.g. "(wind noise)", "[applause]",
    /// "*laughs*") — mirrors Sources/DictationController.swift's stripSoundAnnotations.
    /// </summary>
    public static class TranscriptSanitizer
    {
        private static readonly Regex[] AnnotationPatterns =
        {
            new(@"\([^\)]*\)", RegexOptions.Compiled),      // ( ... ) ASCII
            new(@"（[^）]*）", RegexOptions.Compiled),          // （ ... ） fullwidth
            new(@"\[[^\]]*\]", RegexOptions.Compiled),      // [ ... ]
            new(@"【[^】]*】", RegexOptions.Compiled),          // 【 ... 】
            new(@"\*[^*]*\*", RegexOptions.Compiled),       // * ... *
            new(@"‹[^›]*›", RegexOptions.Compiled),          // ‹ ... ›
            new(@"«[^»]*»", RegexOptions.Compiled),          // « ... »
        };

        private static readonly Regex CollapseWhitespace = new(@"\s{2,}", RegexOptions.Compiled);
        private static readonly Regex SpaceBeforePunctuation = new(@"\s+([,.!?])", RegexOptions.Compiled);

        public static string StripSoundAnnotations(string text)
        {
            var result = text;
            foreach (var pattern in AnnotationPatterns)
            {
                result = pattern.Replace(result, " ");
            }

            result = CollapseWhitespace.Replace(result, " ");
            result = SpaceBeforePunctuation.Replace(result, "$1");
            return result.Trim();
        }
    }
}
