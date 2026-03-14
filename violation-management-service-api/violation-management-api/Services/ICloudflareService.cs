using System.Text.Json.Serialization;

namespace violation_management_api.Services;

public class CloudflareLiveInputResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("result")]
    public CloudflareResult? Result { get; set; }
}

public class CloudflareResult
{
    [JsonPropertyName("uid")]
    public string Uid { get; set; } = string.Empty;

    [JsonPropertyName("webRTC")]
    public CloudflareWebRtc? WebRTC { get; set; }

    [JsonPropertyName("webRTCPlayback")]
    public CloudflareWebRtcPlayback? WebRTCPlayback { get; set; }
}

public class CloudflareWebRtc
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

public class CloudflareWebRtcPlayback
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
}

public interface ICloudflareService
{
    Task<(string uid, string whipUrl, string whepUrl)?> CreateLiveInputAsync(string cameraName);
}
