using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace violation_management_api.Services;

public class CloudflareService : ICloudflareService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CloudflareService> _logger;

    public CloudflareService(HttpClient httpClient, IConfiguration configuration, ILogger<CloudflareService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<(string uid, string whipUrl, string whepUrl)?> CreateLiveInputAsync(string cameraName)
    {
        try
        {
            var accountId = _configuration["Cloudflare:AccountId"];
            var apiToken = _configuration["Cloudflare:ApiToken"];

            if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(apiToken))
            {
                _logger.LogWarning("Cloudflare settings are missing or incomplete. Skipping Live Input Creation.");
                return null;
            }

            var url = $"https://api.cloudflare.com/client/v4/accounts/{accountId}/stream/live_inputs";
            
            var requestBody = new
            {
                meta = new { name = cameraName },
                recording = new { mode = "automatic", timeoutSeconds = 0, requireSignedURLs = false }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = content
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            request.Headers.Add("User-Agent", "Alpha-Surveillance/1.0");

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create Cloudflare Live Input. Status: {StatusCode}, Error: {ErrorContent}", response.StatusCode, errorContent);
                return null; // Return null if creating the input fails so the camera creation still succeeds (just without WebRTC)
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var parsedResponse = JsonSerializer.Deserialize<CloudflareLiveInputResponse>(responseContent);

            if (parsedResponse?.Success == true && parsedResponse.Result != null)
            {
                var uid = parsedResponse.Result.Uid;
                var whipUrl = parsedResponse.Result.WebRTC?.Url;
                var whepUrl = parsedResponse.Result.WebRTCPlayback?.Url;

                if (!string.IsNullOrEmpty(uid) && !string.IsNullOrEmpty(whipUrl) && !string.IsNullOrEmpty(whepUrl))
                {
                    _logger.LogInformation("Successfully created Cloudflare Live Input: {Uid} for Camera: {CameraName}", uid, cameraName);
                    return (uid, whipUrl, whepUrl);
                }
            }
            
            _logger.LogError("Cloudflare Live Input response was successful but missing essential fields. Payload: {ResponseContent}", responseContent);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while calling Cloudflare API to create a live input for Camera: {CameraName}", cameraName);
            return null;
        }
    }
}
