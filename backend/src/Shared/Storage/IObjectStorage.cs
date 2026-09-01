namespace Shared.Storage;

public interface IObjectStorage
{
    Task<string> UploadAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(string objectKey, CancellationToken cancellationToken = default);
    Task<string> GetSignedUrlAsync(string objectKey, TimeSpan expiry, CancellationToken cancellationToken = default);
    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);
}
