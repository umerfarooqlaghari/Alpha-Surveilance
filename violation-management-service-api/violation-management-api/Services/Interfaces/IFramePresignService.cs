namespace AlphaSurveilance.Services.Interfaces
{
    /// <summary>
    /// Resolves a stored frame path into a URL the browser can actually open.
    /// S3 object URLs are converted to time-limited pre-signed URLs; anything
    /// else (Cloudinary, local paths, already-signed URLs) passes through as-is.
    /// </summary>
    public interface IFramePresignService
    {
        /// <summary>
        /// Returns a pre-signed URL when <paramref name="framePath"/> points at an
        /// S3 object; otherwise returns the input unchanged. Never throws — on any
        /// presign failure the raw path is returned so evidence links keep working
        /// (the bucket may still be public) instead of the whole list request failing.
        /// </summary>
        string? GetPresignedUrl(string? framePath);
    }
}
