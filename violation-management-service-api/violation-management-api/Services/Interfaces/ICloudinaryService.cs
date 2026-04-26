using Microsoft.AspNetCore.Http;

namespace violation_management_api.Services.Interfaces;

public interface ICloudinaryService
{
    Task<(string Url, string PublicId)> UploadImageAsync(IFormFile file, string folder);
    Task<(string Url, string PublicId, string ContentType, long SizeBytes)> UploadFileAsync(IFormFile file, string folder);
    Task DeleteImageAsync(string publicId);
    Task DeleteFileAsync(string publicId, string resourceType = "raw");
}
