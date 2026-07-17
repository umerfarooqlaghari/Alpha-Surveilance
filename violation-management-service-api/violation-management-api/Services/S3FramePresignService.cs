using System;
using Amazon.S3;
using Amazon.S3.Model;
using AlphaSurveilance.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AlphaSurveilance.Services
{
    /// <summary>
    /// Pre-signs S3 frame URLs for the violation read path.
    ///
    /// ViolationResponse.FrameUrl is documented as "Pre-signed S3 URL valid for
    /// 24 h" but the service historically copied FramePath verbatim, which 403s
    /// as soon as the bucket blocks public reads. This service restores the
    /// documented contract:
    ///  - virtual-hosted style URLs  https://{bucket}.s3.{region}.amazonaws.com/{key}
    ///  - path style URLs            https://s3.{region}.amazonaws.com/{bucket}/{key}
    /// are pre-signed with the configured expiry; every other value (Cloudinary,
    /// relative paths, empty) is returned untouched. Presign failures degrade to
    /// the raw path instead of throwing.
    /// </summary>
    public class S3FramePresignService(
        IAmazonS3 s3Client,
        IConfiguration configuration,
        ILogger<S3FramePresignService> logger) : IFramePresignService
    {
        private const double DefaultExpiryHours = 24;

        public string? GetPresignedUrl(string? framePath)
        {
            if (string.IsNullOrWhiteSpace(framePath))
                return framePath;

            if (!TryParseS3Url(framePath, out var bucket, out var key))
                return framePath; // Not an S3 object URL — pass through (e.g. Cloudinary).

            try
            {
                var expiryHours = configuration.GetValue<double?>("S3Config:PresignExpiryHours") ?? DefaultExpiryHours;
                var request = new GetPreSignedUrlRequest
                {
                    BucketName = bucket,
                    Key = key,
                    Verb = HttpVerb.GET,
                    Expires = DateTime.UtcNow.AddHours(expiryHours)
                };
                return s3Client.GetPreSignedURL(request);
            }
            catch (Exception ex)
            {
                // Resilience over strictness: a presign failure (bad credentials,
                // clock skew, SDK misconfig) must not break the violations list.
                logger.LogWarning(ex, "Failed to pre-sign S3 frame URL for {FramePath}; returning raw path", framePath);
                return framePath;
            }
        }

        /// <summary>
        /// Extracts (bucket, key) from virtual-hosted or path-style S3 URLs.
        /// Returns false for anything that is not an amazonaws.com S3 object URL.
        /// </summary>
        public static bool TryParseS3Url(string url, out string bucket, out string key)
        {
            bucket = string.Empty;
            key = string.Empty;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
                return false;

            var host = uri.Host;
            if (!host.EndsWith(".amazonaws.com", StringComparison.OrdinalIgnoreCase))
                return false;

            var path = uri.AbsolutePath.TrimStart('/');

            // Path style: s3.amazonaws.com/{bucket}/{key} or s3.{region}.amazonaws.com/{bucket}/{key}
            if (host.StartsWith("s3.", StringComparison.OrdinalIgnoreCase) ||
                host.StartsWith("s3-", StringComparison.OrdinalIgnoreCase))
            {
                var slash = path.IndexOf('/');
                if (slash <= 0 || slash == path.Length - 1)
                    return false;
                bucket = path[..slash];
                key = Uri.UnescapeDataString(path[(slash + 1)..]);
                return true;
            }

            // Virtual-hosted style: {bucket}.s3.{region}.amazonaws.com/{key} or {bucket}.s3.amazonaws.com/{key}
            var s3Marker = host.IndexOf(".s3.", StringComparison.OrdinalIgnoreCase);
            if (s3Marker < 0)
                s3Marker = host.IndexOf(".s3-", StringComparison.OrdinalIgnoreCase);
            if (s3Marker <= 0 || string.IsNullOrEmpty(path))
                return false;

            bucket = host[..s3Marker];
            key = Uri.UnescapeDataString(path);
            return true;
        }
    }
}
