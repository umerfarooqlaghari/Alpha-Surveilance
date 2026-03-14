using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using violation_management_api.Services.Interfaces;

namespace violation_management_api.Services;

public class CloudinaryService : ICloudinaryService
{
    private readonly Cloudinary _cloudinary;
    private readonly ILogger<CloudinaryService> _logger;

    public CloudinaryService(IConfiguration configuration, ILogger<CloudinaryService> logger)
    {
        _logger = logger;
        
        var cloudName = configuration["Cloudinary:CloudName"];
        var apiKey = configuration["Cloudinary:ApiKey"];
        var apiSecret = configuration["Cloudinary:ApiSecret"];

        if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
        {
            throw new InvalidOperationException("Cloudinary configuration is missing. Please check appsettings.json");
        }

        var account = new Account(cloudName, apiKey, apiSecret);
        _cloudinary = new Cloudinary(account);
    }

    public async Task<(string Url, string PublicId)> UploadImageAsync(IFormFile file, string folder)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("File is empty or null");
        }

        // Validate file type
        var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType.ToLower()))
        {
            throw new ArgumentException($"Invalid file type. Allowed types: {string.Join(", ", allowedTypes)}");
        }

        // Validate file size (max 5MB)
        if (file.Length > 5 * 1024 * 1024)
        {
            throw new ArgumentException("File size exceeds 5MB limit");
        }

        try
        {
            using var stream = file.OpenReadStream();
            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = folder,
                Transformation = new Transformation().Width(500).Height(500).Crop("limit").Quality("auto")
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            if (uploadResult.Error != null)
            {
                _logger.LogError("Cloudinary upload error: {Error}", uploadResult.Error.Message);
                throw new Exception($"Cloudinary upload failed: {uploadResult.Error.Message}");
            }

            return (uploadResult.SecureUrl.ToString(), uploadResult.PublicId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading image to Cloudinary");
            throw;
        }
    }

    public async Task<(string Url, string PublicId, string ContentType, long SizeBytes)> UploadFileAsync(IFormFile file, string folder)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("File is empty or null");

        if (file.Length > 50L * 1024 * 1024)
            throw new ArgumentException("File size exceeds 50 MB limit");

        try
        {
            using var stream = file.OpenReadStream();
            // Use "auto" resource_type so Cloudinary handles images, video, and raw automatically
            var uploadParams = new RawUploadParams
            {
                File = new FileDescription(file.FileName, stream),
                Folder = folder,
                UseFilename = true,
                UniqueFilename = true,
                Overwrite = false
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);

            if (uploadResult.Error != null)
            {
                _logger.LogError("Cloudinary file upload error: {Error}", uploadResult.Error.Message);
                throw new Exception($"Cloudinary upload failed: {uploadResult.Error.Message}");
            }

            return (uploadResult.SecureUrl.ToString(), uploadResult.PublicId, file.ContentType, file.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading file to Cloudinary");
            throw;
        }
    }

    public async Task DeleteFileAsync(string publicId, string resourceType = "raw")
    {
        if (string.IsNullOrEmpty(publicId)) return;
        try
        {
            var deletionParams = new DeletionParams(publicId)
            {
                ResourceType = resourceType == "image" ? ResourceType.Image : ResourceType.Raw
            };
            await _cloudinary.DestroyAsync(deletionParams);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file from Cloudinary");
        }
    }

    public async Task DeleteImageAsync(string publicId)
    {
        if (string.IsNullOrEmpty(publicId))
        {
            return;
        }

        try
        {
            var deletionParams = new DeletionParams(publicId);
            var result = await _cloudinary.DestroyAsync(deletionParams);

            if (result.Error != null)
            {
                _logger.LogWarning("Cloudinary deletion warning: {Error}", result.Error.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting image from Cloudinary");
            // Don't throw - deletion failures shouldn't break the flow
        }
    }
}
