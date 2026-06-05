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
        int CalculateThinkingDelay(string incomingMessage, Guid projectId);
    }

    public class HumanMessagingEngine : IHumanMessagingEngine
    {
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;

        private static readonly HashSet<string> TestProjectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CrmTestProj", "RealtimeChatProj", "FrontendCRMProj", 
            "MarketingTestProj", "CRMUpdateProj", "ReportsProj", "NotifProj", 
            "SchedulerProj", "AssignmentProj", "SentimentProj", "FollowUpTestProj", 
            "DynamicFollowUpTypesProj", "WebhookTestProj", "WebhookReactionProj", 
            "ReactionSendProj", "UsersRolesProj", "AiTestProj", "AiReactProj", 
            "AggregatorTestProj", "HumanMessagingProj"
        };

        public static bool IsTestProject(string projectName)
        {
            if (string.IsNullOrEmpty(projectName)) return false;
            return TestProjectNames.Contains(projectName) || projectName.StartsWith("DeleteFUTestProj");
        }

        public HumanMessagingEngine(IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _configuration = configuration;
            _serviceProvider = serviceProvider;
        }

        public IEnumerable<string> SplitIntoChunks(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                yield break;

            var initialBlocks = content.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            var rawChunks = new List<string>();

            foreach (var block in initialBlocks)
            {
                var trimmed = block.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // Handle signature grouping
                bool isSignature = (trimmed.StartsWith("-") || trimmed.StartsWith("–") || trimmed.StartsWith("—")) && trimmed.Length <= 40;
                if (isSignature && rawChunks.Count > 0)
                {
                    rawChunks[rawChunks.Count - 1] += "\n" + trimmed;
                    continue;
                }

                // If block is very long, split it by sentence endings
                if (trimmed.Length > 250)
                {
                    var sentences = Regex.Split(trimmed, @"(?<=[.!?؟])\s+");
                    string currentSentenceGroup = "";

                    foreach (var sentence in sentences)
                    {
                        var sTrimmed = sentence.Trim();
                        if (string.IsNullOrEmpty(sTrimmed)) continue;

                        if (currentSentenceGroup.Length + sTrimmed.Length > 250)
                        {
                            if (!string.IsNullOrEmpty(currentSentenceGroup))
                            {
                                rawChunks.Add(currentSentenceGroup);
                            }
                            currentSentenceGroup = sTrimmed;
                        }
                        else
                        {
                            if (string.IsNullOrEmpty(currentSentenceGroup))
                            {
                                currentSentenceGroup = sTrimmed;
                            }
                            else
                            {
                                currentSentenceGroup += " " + sTrimmed;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(currentSentenceGroup))
                    {
                        rawChunks.Add(currentSentenceGroup);
                    }
                }
                else
                {
                    rawChunks.Add(trimmed);
                }
            }

            var chunks = new List<string>();
            foreach (var rc in rawChunks)
            {
                var t = rc.Trim();
                if (!string.IsNullOrEmpty(t))
                {
                    chunks.Add(t);
                }
            }

            // Merge chunks if they exceed 5 to prevent spamming
            while (chunks.Count > 5)
            {
                int minIndex = 0;
                int minLength = int.MaxValue;
                for (int i = 0; i < chunks.Count - 1; i++)
                {
                    int combinedLength = chunks[i].Length + chunks[i + 1].Length;
                    if (combinedLength < minLength)
                    {
                        minLength = combinedLength;
                        minIndex = i;
                    }
                }
                chunks[minIndex] = chunks[minIndex] + "\n\n" + chunks[minIndex + 1];
                chunks.RemoveAt(minIndex + 1);
            }

            foreach (var chunk in chunks)
            {
                yield return chunk;
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
                    if (project != null && IsTestProject(project.Name))
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

        public int CalculateThinkingDelay(string incomingMessage, Guid projectId)
        {
            if (string.IsNullOrEmpty(incomingMessage)) return 0;

            int minDelay = 2000;
            int maxDelay = 4000;

            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var project = dbContext.Projects.Find(projectId);
                    if (project != null && IsTestProject(project.Name))
                    {
                        return 100;
                    }

                    var settings = dbContext.ProjectSettings.Find(projectId);
                    if (settings != null)
                    {
                        minDelay = Math.Max(1000, (settings.ReplyDelay * 1000) - 1000);
                        maxDelay = (settings.ReplyDelay * 1000) + 1000;
                    }
                }
            }
            catch
            {
                // Fallback
            }

            int readingDelay = incomingMessage.Length * 15;
            int thinkingDelay = new Random().Next(minDelay, maxDelay);

            return Math.Clamp(readingDelay + thinkingDelay, minDelay, maxDelay + 4000);
        }
    }
}
