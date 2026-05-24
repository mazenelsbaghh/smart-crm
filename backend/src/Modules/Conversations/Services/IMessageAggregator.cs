using System;
using System.Threading.Tasks;

namespace Modules.Conversations.Services
{
    public interface IMessageAggregator
    {
        Task AggregateMessageAsync(Guid projectId, string sender, string content);
    }
}
