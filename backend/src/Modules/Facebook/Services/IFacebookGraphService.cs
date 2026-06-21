using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Modules.Facebook.Services
{
    public class FacebookPageInfo
    {
        public string PageId { get; set; } = string.Empty;
        public string PageName { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
    }

    public interface IFacebookGraphService
    {
        Task SendMessageAsync(string pageId, string pageAccessToken, string recipientPSID, string message);
        Task ReplyToCommentAsync(string pageAccessToken, string commentId, string message);
        Task ReactToCommentAsync(string pageAccessToken, string commentId, string reactionType = "LOVE");
        Task SendPrivateReplyAsync(string pageId, string pageAccessToken, string commentId, string message);
        Task SubscribePageToAppAsync(string pageId, string pageAccessToken);
        Task<List<FacebookPageInfo>> GetUserPagesAsync(string userAccessToken);
    }
}
