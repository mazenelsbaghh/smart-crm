using System;
using System.Threading.Tasks;
using Modules.AI.Services;

namespace Modules.Campaigns.Application.Services
{
    public interface ICampaignAIService
    {
        Task<string> GenerateCampaignCopyAsync(string prompt, string baseTemplate, string targetContext);
    }

    public class CampaignAIService : ICampaignAIService
    {
        private readonly IGeminiClient _geminiClient;

        public CampaignAIService(IGeminiClient geminiClient)
        {
            _geminiClient = geminiClient;
        }

        public async Task<string> GenerateCampaignCopyAsync(string prompt, string baseTemplate, string targetContext)
        {
            var systemPrompt = $"You are a professional marketing copywriter. Create a personalized marketing campaign message for WhatsApp.\n" +
                               $"Guidelines:\n" +
                               $"- Use a friendly, high-energy tone.\n" +
                               $"- The base message template/reference is: '{baseTemplate}'\n" +
                               $"- Keep dynamic placeholders like {{CustomerName}} intact.\n" +
                               $"- Here is additional prompt details from the user: '{prompt}'\n" +
                               $"- Incorporate the target context: '{targetContext}'\n\n" +
                               $"Output only the final message copy, with no surrounding explanations or quotes.";

            return await _geminiClient.GenerateReplyAsync(systemPrompt);
        }
    }
}
