using Modules.QuranChallenge.Domain;

namespace Modules.QuranChallenge.Services;

public sealed class YouTubePublishingClient
{
    private readonly YouTubeOAuthClient _oauthClient;
    private readonly YouTubeUploadClient _uploadClient;
    private readonly YouTubeTokenVault _tokenVault;

    public YouTubePublishingClient(
        YouTubeOAuthClient oauthClient,
        YouTubeUploadClient uploadClient,
        YouTubeTokenVault tokenVault)
    {
        _oauthClient = oauthClient;
        _uploadClient = uploadClient;
        _tokenVault = tokenVault;
    }

    public async Task<string> UploadAsync(
        QuranYouTubeSettings settings,
        YouTubeVideoUpload upload,
        CancellationToken cancellationToken)
    {
        var protectedToken = settings.ProtectedRefreshToken
            ?? throw new InvalidOperationException("قناة YouTube غير مرتبطة.");
        var refreshToken = _tokenVault.Unprotect(protectedToken);
        var accessToken = await _oauthClient.RefreshAccessTokenAsync(refreshToken, cancellationToken);
        return await _uploadClient.UploadAsync(upload, accessToken, cancellationToken);
    }
}
