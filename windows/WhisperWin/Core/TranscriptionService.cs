using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WhisperWin.Core
{
    /// <summary>Thrown when the Groq transcription API returns an error or an unparseable response.</summary>
    public sealed class TranscriptionException : Exception
    {
        public TranscriptionException(string message, Exception? inner = null) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Sends recorded WAV audio to Groq's Whisper endpoint for speech-to-text.
    /// All calls are async; never blocks with .Result/.Wait() (would deadlock a WPF/STA context).
    /// </summary>
    public sealed class TranscriptionService
    {
        private const string Endpoint = "https://api.groq.com/openai/v1/audio/transcriptions";
        // Full large-v3 (not turbo): turbo is pruned for speed and mis-hears short Thai clips
        // more often (e.g. เรียบร้อย→เรียบล่อย). v3 is the accuracy benchmark; the extra latency
        // is sub-second for typical dictation clips. See docs/DAILY-LOG.md 2026-07-07.
        private const string Model = "whisper-large-v3";

        private readonly HttpClient _httpClient;

        public TranscriptionService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<string> TranscribeAsync(byte[] wavBytes, string apiKey, string? language, CancellationToken cancellationToken = default)
        {
            if (wavBytes == null || wavBytes.Length == 0)
            {
                throw new ArgumentException("Audio bytes must not be empty.", nameof(wavBytes));
            }
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API key must not be empty.", nameof(apiKey));
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            using var content = new MultipartFormDataContent();
            using var audioContent = new ByteArrayContent(wavBytes);
            audioContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
            content.Add(audioContent, "file", "audio.wav");
            content.Add(new StringContent(Model), "model");
            if (!string.IsNullOrEmpty(language) && language != "auto")
            {
                content.Add(new StringContent(language!), "language");
            }
            request.Content = content;

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new TranscriptionException("Network error while contacting Groq transcription API.", ex);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw new TranscriptionException($"Groq transcription API returned {(int)response.StatusCode}: {body}");
                }

                try
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("text", out var textProp))
                    {
                        return textProp.GetString() ?? string.Empty;
                    }
                    throw new TranscriptionException("Groq transcription response did not contain a 'text' field.");
                }
                catch (JsonException ex)
                {
                    throw new TranscriptionException("Could not parse Groq transcription response.", ex);
                }
            }
        }
    }
}
