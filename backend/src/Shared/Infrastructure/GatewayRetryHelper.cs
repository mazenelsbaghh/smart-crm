using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Shared.Infrastructure
{
    public static class GatewayRetryHelper
    {
        public static async Task<HttpResponseMessage> PostWithRetryAsync(HttpClient httpClient, string url, string jsonPayload, int maxRetries = 3, int delayMs = 2000)
        {
            HttpResponseMessage response = null;
            for (int attempt = 1; attempt <= maxRetries + 1; attempt++)
            {
                try
                {
                    var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                    response = await httpClient.PostAsync(url, content);
                    if (response.IsSuccessStatusCode)
                    {
                        return response;
                    }
                    
                    var responseBody = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[GatewayRetryHelper] Attempt {attempt} failed with status code {response.StatusCode}: {responseBody}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GatewayRetryHelper] Attempt {attempt} failed with exception: {ex.Message}");
                }

                if (attempt <= maxRetries)
                {
                    Console.WriteLine($"[GatewayRetryHelper] Retrying in {delayMs}ms (Attempt {attempt} of {maxRetries})...");
                    await Task.Delay(delayMs);
                }
            }
            
            if (response == null)
            {
                throw new HttpRequestException($"Failed to connect to gateway after {maxRetries} retries.");
            }
            return response;
        }
    }
}
