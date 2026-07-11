using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WhisperWin.Core
{
    /// <summary>Thrown when the Groq chat-completion (correction) API errors or returns garbage.</summary>
    public sealed class CorrectionException : Exception
    {
        public CorrectionException(string message, Exception? inner = null) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Sends raw transcription text to Groq's LLM chat-completions endpoint for cleanup, using the
    /// shared prompt template assembled by <see cref="PromptBuilder"/>. Async all the way through —
    /// callers should catch <see cref="CorrectionException"/> and fall back to the raw transcript
    /// (see DictationController: "on correction failure, use raw text").
    /// </summary>
    public sealed class CorrectionService
    {
        private const string Endpoint = "https://api.groq.com/openai/v1/chat/completions";
        private const string Model = "llama-3.3-70b-versatile";
        private const double Temperature = 0.2;

        private readonly HttpClient _httpClient;

        public CorrectionService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<string> CorrectAsync(
            string text,
            string systemPrompt,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Text must not be empty.", nameof(text));
            }
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API key must not be empty.", nameof(apiKey));
            }

            var payload = new Dictionary<string, object>
            {
                ["model"] = Model,
                ["temperature"] = Temperature,
                ["messages"] = new object[]
                {
                    new Dictionary<string, string> { ["role"] = "system", ["content"] = systemPrompt },
                    new Dictionary<string, string> { ["role"] = "user", ["content"] = text },
                },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            var json = JsonSerializer.Serialize(payload);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new CorrectionException("Network error while contacting Groq correction API.", ex);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    throw new CorrectionException($"Groq correction API returned {(int)response.StatusCode}: {body}");
                }

                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var choices = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() == 0)
                    {
                        throw new CorrectionException("Groq correction response had no choices.");
                    }

                    var content = choices[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();

                    if (content == null)
                    {
                        throw new CorrectionException("Groq correction response had a null content field.");
                    }

                    return content.Trim().Trim('"', '\'');
                }
                catch (JsonException ex)
                {
                    throw new CorrectionException("Could not parse Groq correction response.", ex);
                }
                catch (InvalidOperationException ex)
                {
                    throw new CorrectionException("Groq correction response was missing expected fields.", ex);
                }
            }
        }
    }
}
