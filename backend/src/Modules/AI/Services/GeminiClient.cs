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
        Task<string> GenerateReplyAsync(string messageContent, string apiKeyOverride = null, string modelOverride = null, string cachedContentId = null);
        Task<string> GenerateReplyAsync(string messageContent, byte[] fileBytes, string mimeType, string apiKeyOverride = null, string modelOverride = null, string cachedContentId = null);
        Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null);
        Task<int> CountTokensAsync(string messageContent, string apiKeyOverride = null, string modelOverride = null);
        Task<string> CreateContextCacheAsync(string staticContent, string model, int ttlSeconds, string apiKeyOverride = null);
    }

    public class GeminiClient : IGeminiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _defaultApiKey;
        private readonly string _defaultModel;
        private readonly IGeminiMockHandler _mockHandler;
        private readonly ILogger<GeminiClient> _logger;

        public GeminiClient(IConfiguration configuration, IGeminiMockHandler mockHandler, ILogger<GeminiClient> logger, HttpClient httpClient)
        {
            _httpClient = httpClient;
            _logger = logger;
            _defaultApiKey = configuration["Gemini:ApiKey"];
            _defaultModel = NormalizeModel(configuration["Gemini:Model"]);
            _mockHandler = mockHandler;
        }

        private static string NormalizeModel(string model)
        {
            return model switch
            {
                "gemini-2.5-flash-lite" => "gemini-2.5-flash-lite",
                "gemini-3.1-flash-lite" => "gemini-3.1-flash-lite",
                "gemini-3.5-flash" => "gemini-3.5-flash",
                "gemini-3.5-flash-lite" => "gemini-3.5-flash-lite",
                "gemini-3.6-flash" => "gemini-3.6-flash",
                "gemini-flash-latest" => "gemini-flash-latest",
                "gemini-flash-lite-latest" => "gemini-flash-lite-latest",
                _ => "gemini-3.5-flash"
            };
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null)
        {
            var apiKey = apiKeyOverride ?? _defaultApiKey;

            if (_mockHandler.IsMockKey(apiKey))
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

        public async Task<string> GenerateReplyAsync(string messageContent, string apiKeyOverride = null, string modelOverride = null, string cachedContentId = null)
        {
            var apiKey = apiKeyOverride ?? _defaultApiKey;
            var model = NormalizeModel(modelOverride ?? _defaultModel);

            // Fallback for testing environments when a real Google AI key is absent
            if (_mockHandler.IsMockKey(apiKey))
            {
                return _mockHandler.GenerateMockReply(messageContent, apiKey);
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            object requestBody;
            if (!string.IsNullOrEmpty(cachedContentId))
            {
                requestBody = new
                {
                    cachedContent = cachedContentId,
                    generationConfig = CreateGenerationConfig(model),
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
            }
            else
            {
                requestBody = new
                {
                    generationConfig = CreateGenerationConfig(model),
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
            }

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseString);
                LogUsage(doc.RootElement, model);
                return ReadReply(doc.RootElement);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling Gemini API: {ex.Message}");
                return "[AI_ERROR] Unable to reach AI engine.";
            }
        }

        public async Task<string> GenerateReplyAsync(string messageContent, byte[] fileBytes, string mimeType, string apiKeyOverride = null, string modelOverride = null, string cachedContentId = null)
        {
            var apiKey = apiKeyOverride ?? _defaultApiKey;
            var model = NormalizeModel(modelOverride ?? _defaultModel);

            // Fallback for testing environments / mock keys
            if (_mockHandler.IsMockKey(apiKey))
            {
                return _mockHandler.GenerateMockReply(messageContent, fileBytes, mimeType, apiKey, model);
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            object requestBody;
            if (!string.IsNullOrEmpty(cachedContentId))
            {
                requestBody = new
                {
                    cachedContent = cachedContentId,
                    generationConfig = CreateGenerationConfig(model),
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = messageContent },
                                new
                                {
                                    inlineData = new
                                    {
                                        mimeType = mimeType,
                                        data = Convert.ToBase64String(fileBytes)
                                    }
                                }
                            }
                        }
                    }
                };
            }
            else
            {
                requestBody = new
                {
                    generationConfig = CreateGenerationConfig(model),
                    contents = new[]
                    {
                        new
                        {
                            parts = new object[]
                            {
                                new { text = messageContent },
                                new
                                {
                                    inlineData = new
                                    {
                                        mimeType = mimeType,
                                        data = Convert.ToBase64String(fileBytes)
                                    }
                                }
                            }
                        }
                    }
                };
            }

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseString);
                LogUsage(doc.RootElement, model);
                return ReadReply(doc.RootElement);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling Gemini Multimodal API: {ex.Message}");
                return "[AI_ERROR] Unable to reach AI engine.";
            }
        }

        private object CreateGenerationConfig(string model)
        {
            var config = new System.Collections.Generic.Dictionary<string, object>
            {
                ["responseMimeType"] = "application/json"
            };
            if (model.StartsWith("gemini-3.", StringComparison.Ordinal))
                config["thinkingConfig"] = new { thinkingLevel = model.EndsWith("-lite", StringComparison.Ordinal) ? "minimal" : "low" };
            else if (model == "gemini-2.5-flash-lite")
                config["thinkingConfig"] = new { thinkingBudget = 0 };
            return config;
        }

        private void LogUsage(JsonElement root, string model)
        {
            if (!root.TryGetProperty("usageMetadata", out var usage)) return;
            _logger.LogInformation(
                "Gemini usage Model={Model} InputTokens={InputTokens} OutputTokens={OutputTokens} ThinkingTokens={ThinkingTokens} CachedTokens={CachedTokens} TotalTokens={TotalTokens}",
                model, TokenCount(usage, "promptTokenCount"), TokenCount(usage, "candidatesTokenCount"),
                TokenCount(usage, "thoughtsTokenCount"), TokenCount(usage, "cachedContentTokenCount"),
                TokenCount(usage, "totalTokenCount"));
        }

        private static int? TokenCount(JsonElement usage, string name) =>
            usage.TryGetProperty(name, out var count) && count.TryGetInt32(out var tokens) ? tokens : null;

        private string ReadReply(JsonElement root)
        {
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
                return "[AI_ERROR] Gemini returned no candidate.";
            var candidate = candidates[0];
            if (candidate.TryGetProperty("finishReason", out var reason) && reason.GetString() != "STOP")
            {
                _logger.LogWarning("Gemini response rejected: FinishReason={FinishReason}", reason.GetString());
                return "[AI_ERROR] Gemini response was incomplete or blocked.";
            }
            var text = new StringBuilder();
            foreach (var part in candidate.GetProperty("content").GetProperty("parts").EnumerateArray())
                if ((!part.TryGetProperty("thought", out var thought) || !thought.GetBoolean())
                    && part.TryGetProperty("text", out var partText))
                    text.Append(partText.GetString());
            return text.Length == 0 ? "[AI_ERROR] Gemini returned no text." : text.ToString().Trim();
        }

        public async Task<int> CountTokensAsync(string messageContent, string apiKeyOverride = null, string modelOverride = null)
        {
            var apiKey = apiKeyOverride ?? _defaultApiKey;
            var model = NormalizeModel(modelOverride ?? _defaultModel);

            if (_mockHandler.IsMockKey(apiKey))
            {
                // Simple approximation for mock key: 1 token ≈ 4 characters in mixed text
                return messageContent.Length / 4;
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:countTokens?key={apiKey}";

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
                if (doc.RootElement.TryGetProperty("totalTokens", out var totalTokensProp))
                {
                    return totalTokensProp.GetInt32();
                }
                return messageContent.Length / 4;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling Gemini CountTokens API: {ex.Message}");
                return messageContent.Length / 4;
            }
        }

        public async Task<string> CreateContextCacheAsync(string staticContent, string model, int ttlSeconds, string apiKeyOverride = null)
        {
            var apiKey = apiKeyOverride ?? _defaultApiKey;
            var normalizedModel = NormalizeModel(model);

            if (_mockHandler.IsMockKey(apiKey))
            {
                // Return a deterministic mock cache ID
                return $"cachedContents/mock_cache_{Guid.NewGuid():N}";
            }

            var url = $"https://generativelanguage.googleapis.com/v1beta/cachedContents?key={apiKey}";

            var requestBody = new
            {
                model = $"models/{normalizedModel}",
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[]
                        {
                            new { text = staticContent }
                        }
                    }
                },
                ttl = $"{ttlSeconds}s",
                displayName = "project_kb_cache"
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseString);
                if (doc.RootElement.TryGetProperty("name", out var nameProp))
                {
                    return nameProp.GetString() ?? throw new Exception("Cache creation response did not contain a name.");
                }
                throw new Exception("Cache creation response missing 'name' field.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating Gemini Context Cache: {ex.Message}");
                throw;
            }
        }
    }
}
