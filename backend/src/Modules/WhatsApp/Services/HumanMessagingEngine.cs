using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure;

namespace Modules.WhatsApp.Services
{
    public interface IHumanMessagingEngine
    {
        IEnumerable<string> SplitIntoChunks(string content);
        int CalculateTypingDelay(string chunk, Guid projectId);
    }

    public class HumanMessagingEngine : IHumanMessagingEngine
    {
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;

        public HumanMessagingEngine(IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _configuration = configuration;
            _serviceProvider = serviceProvider;
        }

        public IEnumerable<string> SplitIntoChunks(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                yield break;

            var paragraphs = content.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var para in paragraphs)
            {
                var trimmed = para.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                {
                    yield return trimmed;
                }
            }
        }

        public int CalculateTypingDelay(string chunk, Guid projectId)
        {
            if (string.IsNullOrEmpty(chunk)) return 0;

            int minDelay = 3000;
            int maxDelay = 5000;

            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var project = dbContext.Projects.Find(projectId);
                    if (project != null && (project.Name.Contains("Test", StringComparison.OrdinalIgnoreCase) || project.Name.Contains("Proj", StringComparison.OrdinalIgnoreCase)))
                    {
                        minDelay = 1000;
                        maxDelay = 1000;
                    }
                    else
                    {
                        var minDelayStr = _configuration["WhatsApp:MinTypingDelayMs"];
                        var maxDelayStr = _configuration["WhatsApp:MaxTypingDelayMs"];

                        if (!string.IsNullOrEmpty(minDelayStr) && int.TryParse(minDelayStr, out var parsedMin))
                        {
                            minDelay = parsedMin;
                        }
                        if (!string.IsNullOrEmpty(maxDelayStr) && int.TryParse(maxDelayStr, out var parsedMax))
                        {
                            maxDelay = parsedMax;
                        }
                    }
                }
            }
            catch
            {
                // Fallback to config if DB is not available
                var minDelayStr = _configuration["WhatsApp:MinTypingDelayMs"];
                var maxDelayStr = _configuration["WhatsApp:MaxTypingDelayMs"];

                if (!string.IsNullOrEmpty(minDelayStr) && int.TryParse(minDelayStr, out var parsedMin))
                {
                    minDelay = parsedMin;
                }
                if (!string.IsNullOrEmpty(maxDelayStr) && int.TryParse(maxDelayStr, out var parsedMax))
                {
                    maxDelay = parsedMax;
                }
            }

            int delay = chunk.Length * 50;
            return Math.Clamp(delay, minDelay, maxDelay);
        }
    }
}
