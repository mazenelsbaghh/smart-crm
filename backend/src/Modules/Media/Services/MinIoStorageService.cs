using System;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace Modules.Media.Services
{
    public interface IMinIoStorageService
    {
        Task<string> UploadFileAsync(string objectKey, Stream fileStream, string contentType);
        Task<Stream> DownloadFileAsync(string objectKey);
        Task<string> GetSignedUrlAsync(string objectKey, TimeSpan expiry);
        Task DeleteFileAsync(string objectKey);
        Task EnsureBucketExistsAsync();
    }

    public class MinIoStorageService : IMinIoStorageService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;

        public MinIoStorageService(IConfiguration configuration)
        {
            var endpoint = configuration["MinIO:Endpoint"] ?? "http://localhost:9000";
            var accessKey = configuration["MinIO:AccessKey"] ?? "minioadmin";
            var secretKey = configuration["MinIO:SecretKey"] ?? "changeme_minio";
            _bucketName = configuration["MinIO:BucketName"] ?? "smartcore-media";

            var config = new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true // Required for MinIO
            };

            _s3Client = new AmazonS3Client(accessKey, secretKey, config);
        }

        public async Task EnsureBucketExistsAsync()
        {
            try
            {
                var bucketExists = await Amazon.S3.Util.AmazonS3Util.DoesS3BucketExistV2Async(_s3Client, _bucketName);
                if (!bucketExists)
                {
                    var putBucketRequest = new PutBucketRequest
                    {
                        BucketName = _bucketName,
                        UseClientRegion = false
                    };
                    await _s3Client.PutBucketAsync(putBucketRequest);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to ensure MinIO bucket exists: {ex.Message}");
            }
        }

        public async Task<string> UploadFileAsync(string objectKey, Stream fileStream, string contentType)
        {
            await EnsureBucketExistsAsync();

            var putRequest = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = objectKey,
                InputStream = fileStream,
                ContentType = contentType
            };

            await _s3Client.PutObjectAsync(putRequest);
            return objectKey;
        }

        public async Task<Stream> DownloadFileAsync(string objectKey)
        {
            var getRequest = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = objectKey
            };

            var response = await _s3Client.GetObjectAsync(getRequest);
            return response.ResponseStream;
        }

        public async Task<string> GetSignedUrlAsync(string objectKey, TimeSpan expiry)
        {
            // AWS S3 GetPreSignedURLRequest requires an expiration date/time
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = objectKey,
                Expires = DateTime.UtcNow.Add(expiry)
            };

            return await Task.Run(() => _s3Client.GetPreSignedURL(request));
        }

        public async Task DeleteFileAsync(string objectKey)
        {
            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = objectKey
            };

            await _s3Client.DeleteObjectAsync(deleteRequest);
        }
    }
}
