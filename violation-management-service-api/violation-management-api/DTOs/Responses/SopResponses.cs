using violation_management_api.Core.Entities;

namespace violation_management_api.DTOs.Responses;

public class SopResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    
    // Optionally include violations if needed in the UI out of the box
    public List<SopViolationTypeResponse> ViolationTypes { get; set; } = new();

    public static SopResponse FromEntity(Sop sop)
    {
        return new SopResponse
        {
            Id = sop.Id,
            Name = sop.Name,
            Description = sop.Description,
            CreatedAt = sop.CreatedAt,
            ViolationTypes = sop.ViolationTypes?.Select(SopViolationTypeResponse.FromEntity).ToList() ?? new()
        };
    }
}

public class SopViolationTypeResponse
{
    public Guid Id { get; set; }
    public Guid SopId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ModelIdentifier { get; set; } = string.Empty;
    public string TriggerLabels { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public static SopViolationTypeResponse FromEntity(SopViolationType type)
    {
        return new SopViolationTypeResponse
        {
            Id = type.Id,
            SopId = type.SopId,
            Name = type.Name,
            ModelIdentifier = type.ModelIdentifier,
            TriggerLabels = type.TriggerLabels,
            Description = type.Description
        };
    }
}
