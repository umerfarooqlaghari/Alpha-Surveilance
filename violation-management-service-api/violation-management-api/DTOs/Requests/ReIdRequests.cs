using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace violation_management_api.DTOs.Requests;

public class IdentifyRequest
{
    [JsonPropertyName("embedding")]
    public List<float> Embedding { get; set; } = new();

    [JsonPropertyName("threshold")]
    public float? Threshold { get; set; } = 0.80f;
}
