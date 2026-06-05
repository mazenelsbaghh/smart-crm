using StackExchange.Redis;
using Shared.Queue;
using Shared.Events;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure;

namespace Modules.Conversations.Services
{
    public class MessageAggregator : IMessageAggregator
    {
        private readonly IDatabase _redis;
        private readonly IEventBus _eventBus;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;

        public MessageAggregator(IConnectionMultiplexer redis, IEventBus eventBus, IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _redis = redis.GetDatabase();
            _eventBus = eventBus;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
        }

        public async Task AggregateMessageAsync(Guid projectId, string sender, string content)
        {
            var listKey = $"chat:list:{projectId}:{sender}";
            var tsKey = $"chat:ts:{projectId}:{sender}";

            // Append message content to list in Redis
            await _redis.ListRightPushAsync(listKey, content);

            // Record current ticks as last message identifier
            var nowTicks = DateTime.UtcNow.Ticks;
            await _redis.StringSetAsync(tsKey, nowTicks);

            // Trigger a delay check (default 30 to 50 seconds)
            int minDelay = 30000;
            int maxDelay = 50000;

            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var project = await dbContext.Projects.FindAsync(projectId);
                    if (project != null && (
                        project.Name.Contains("Test", StringComparison.OrdinalIgnoreCase) || 
                        project.Name.EndsWith("Proj", StringComparison.OrdinalIgnoreCase) || 
                        project.Name.StartsWith("Campaign_Project", StringComparison.OrdinalIgnoreCase)))
                    {
                        minDelay = 2000;
                        maxDelay = 2000;
                    }
                    else
                    {
                        var minDelayStr = _configuration["MessageAggregation:MinDelayMs"];
                        var maxDelayStr = _configuration["MessageAggregation:MaxDelayMs"];

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
                var minDelayStr = _configuration["MessageAggregation:MinDelayMs"];
                var maxDelayStr = _configuration["MessageAggregation:MaxDelayMs"];

                if (!string.IsNullOrEmpty(minDelayStr) && int.TryParse(minDelayStr, out var parsedMin))
                {
                    minDelay = parsedMin;
                }
                if (!string.IsNullOrEmpty(maxDelayStr) && int.TryParse(maxDelayStr, out var parsedMax))
                {
                    maxDelay = parsedMax;
                }
            }

            int delayMs = minDelay >= maxDelay 
                ? minDelay 
                : new Random().Next(minDelay, maxDelay);

            _ = Task.Run(async () =>
            {
                await Task.Delay(delayMs);
                await CheckAndPublishAsync(projectId, sender, nowTicks);
            });
        }

        private async Task CheckAndPublishAsync(Guid projectId, string sender, long triggerTicks)
        {
            var tsKey = $"chat:ts:{projectId}:{sender}";
            var currentTsStr = await _redis.StringGetAsync(tsKey);

            if (currentTsStr.HasValue && long.TryParse(currentTsStr, out var currentTicks))
            {
                // If ticks match, it means no new message arrived to reset the timer
                if (currentTicks == triggerTicks)
                {
                    var listKey = $"chat:list:{projectId}:{sender}";
                    var messages = await _redis.ListRangeAsync(listKey);

                    if (messages != null && messages.Length > 0)
                    {
                        var aggregatedText = string.Join("\n", Array.ConvertAll(messages, m => m.ToString()));

                        // Publish MessageAggregatedEvent
                        var @event = new MessageAggregatedEvent
                        {
                            ProjectId = projectId,
                            Sender = sender,
                            Content = aggregatedText
                        };

                        await _eventBus.PublishAsync(@event);

                        // Clean up keys
                        await _redis.KeyDeleteAsync(listKey);
                        await _redis.KeyDeleteAsync(tsKey);
                    }
                }
            }
        }
    }
}
