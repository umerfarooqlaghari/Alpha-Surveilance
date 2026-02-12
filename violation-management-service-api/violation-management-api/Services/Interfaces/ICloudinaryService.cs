using Microsoft.AspNetCore.Http;

namespace violation_management_api.Services.Interfaces;

public interface ICloudinaryService
{
    Task<(string Url, string PublicId)> UploadImageAsync(IFormFile file, string folder);
    Task DeleteImageAsync(string publicId);
}
