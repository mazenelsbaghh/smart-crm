using StackExchange.Redis;
using Shared.Queue;
using Shared.Events;
using System;
using System.Threading.Tasks;

namespace Modules.Conversations.Services
{
    public class MessageAggregator : IMessageAggregator
    {
        private readonly IDatabase _redis;
        private readonly IEventBus _eventBus;

        public MessageAggregator(IConnectionMultiplexer redis, IEventBus eventBus)
        {
            _redis = redis.GetDatabase();
            _eventBus = eventBus;
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

            // Trigger a 5-second delay check
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
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
