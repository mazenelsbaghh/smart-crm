using System.Threading.Tasks;

namespace Modules.Facebook.Services
{
    public interface IFacebookOAuthService
    {
        string GetLoginUrl(string projectId, string csrfToken);
        Task<string> ExchangeCodeForTokenAsync(string code);
        Task<string> ExchangeForLongLivedTokenAsync(string shortLivedToken);
    }
}
