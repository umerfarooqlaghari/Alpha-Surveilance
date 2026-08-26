using System.Text.Json.Serialization;

namespace violation_management_api.DTOs.Responses;

public class IdentifyResponse
{
    [JsonPropertyName("matched")]
    public bool Matched { get; set; }

    [JsonPropertyName("person_tag")]
    public string? PersonTag { get; set; }

    [JsonPropertyName("similarity")]
    public float Similarity { get; set; }
}
