using Microsoft.Extensions.Configuration;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Modules.AI.Services
{
    public interface IGeminiClient
    {
        Task<string> GenerateReplyAsync(string messageContent, string apiKeyOverride = null);
        Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null);
    }

    public class GeminiClient : IGeminiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _defaultApiKey;

        public GeminiClient(IConfiguration configuration)
        {
            _httpClient = new HttpClient();
            _defaultApiKey = configuration["Gemini:ApiKey"];
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null)
        {
            var apiKey = apiKeyOverride ?? _defaultApiKey;

            if (string.IsNullOrEmpty(apiKey) || apiKey == "your_gemini_api_key_here" || apiKey.StartsWith("mock_"))
            {
                var mockEmbedding = new float[768];
                for (int i = 0; i < mockEmbedding.Length; i++)
                {
                    // Generate deterministic floats based on text hash
                    mockEmbedding[i] = (float)Math.Sin(text.GetHashCode() + i);
                }
                return mockEmbedding;
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/text-embedding-004:embedContent?key={apiKey}";

            var requestBody = new
            {
                model = "models/text-embedding-004",
                content = new
                {
                    parts = new[]
                    {
                        new { text = text }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseString);
                var valuesElement = doc.RootElement
                    .GetProperty("embedding")
                    .GetProperty("values");

                var embedding = new float[valuesElement.GetArrayLength()];
                int index = 0;
                foreach (var val in valuesElement.EnumerateArray())
                {
                    embedding[index++] = (float)val.GetDouble();
                }

                return embedding;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling Gemini Embedding API: {ex.Message}");
                return new float[768];
            }
        }

        public async Task<string> GenerateReplyAsync(string messageContent, string apiKeyOverride = null)
        {
            var apiKey = apiKeyOverride ?? _defaultApiKey;

            // Fallback for testing environments when a real Google AI key is absent
            if (string.IsNullOrEmpty(apiKey) || apiKey == "your_gemini_api_key_here" || apiKey.StartsWith("mock_"))
            {
                if (apiKey != null && apiKey.StartsWith("mock_json_"))
                {
                    return apiKey.Substring("mock_json_".Length);
                }
                return $"[Mock Gemini Reply] Re: {messageContent}";
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash:generateContent?key={apiKey}";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = messageContent }
                        }
                    }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseString);
                var reply = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                return reply?.Trim();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling Gemini API: {ex.Message}");
                return "[AI_ERROR] Unable to reach AI engine.";
            }
        }
    }
}
