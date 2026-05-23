using System;

namespace violation_management_api.DTO.Requests;

public class UpdateTenantModuleRequest
{
    public string ModuleKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

public class TenantModuleResponse
{
    public string ModuleKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}
