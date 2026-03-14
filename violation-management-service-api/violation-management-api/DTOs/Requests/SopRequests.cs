namespace violation_management_api.DTOs.Requests;

public class CreateSopRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class UpdateSopRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class CreateSopViolationTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public string ModelIdentifier { get; set; } = string.Empty;
    public string TriggerLabels { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class UpdateSopViolationTypeRequest
{
    public string Name { get; set; } = string.Empty;
    public string ModelIdentifier { get; set; } = string.Empty;
    public string TriggerLabels { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
