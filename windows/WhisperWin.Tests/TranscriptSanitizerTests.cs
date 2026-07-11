using WhisperWin.Core;
using Xunit;

namespace WhisperWin.Tests
{
    public class TranscriptSanitizerTests
    {
        [Theory]
        [InlineData("Hello (wind noise) world", "Hello world")]
        [InlineData("[applause] Great talk", "Great talk")]
        [InlineData("*laughs* that's funny", "that's funny")]
        [InlineData("no annotations here", "no annotations here")]
        public void StripSoundAnnotations_RemovesBracketedAnnotations(string input, string expected)
        {
            var result = TranscriptSanitizer.StripSoundAnnotations(input);

            Assert.Equal(expected, result);
        }

        [Fact]
        public void StripSoundAnnotations_CollapsesDoubleSpaces()
        {
            var result = TranscriptSanitizer.StripSoundAnnotations("Hello   world");

            Assert.Equal("Hello world", result);
        }

        [Fact]
        public void StripSoundAnnotations_RemovesSpaceBeforePunctuation()
        {
            var result = TranscriptSanitizer.StripSoundAnnotations("Hello (noise) , world");

            Assert.Equal("Hello, world", result);
        }
    }
}
