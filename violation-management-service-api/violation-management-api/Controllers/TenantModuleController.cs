using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using violation_management_api.Core.Entities;
using violation_management_api.DTO.Requests;
using AlphaSurveilance.Data;
using System.Security.Claims;

namespace violation_management_api.Controllers;

[ApiController]
[Route("api/tenants")]
public class TenantModuleController : ControllerBase
{
    private readonly AppViolationDbContext _context;
    private readonly ILogger<TenantModuleController> _logger;

    public TenantModuleController(AppViolationDbContext context, ILogger<TenantModuleController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all modules for a specific tenant (SuperAdmin only)
    /// </summary>
    [HttpGet("{tenantId}/modules")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> GetTenantModules(Guid tenantId)
    {
        var modules = await _context.TenantSidebarModules
            .Where(m => m.TenantId == tenantId)
            .Select(m => new TenantModuleResponse
            {
                ModuleKey = m.ModuleKey,
                IsEnabled = m.IsEnabled
            })
            .ToListAsync();

        return Ok(modules);
    }

    /// <summary>
    /// Get active modules for the current logged-in tenant
    /// </summary>
    [HttpGet("my-modules")]
    [Authorize]
    public async Task<IActionResult> GetMyModules()
    {
        var tenantIdStr = User.FindFirstValue("tenantId");
        if (string.IsNullOrEmpty(tenantIdStr))
            return BadRequest("Tenant ID not found in token");

        if (!Guid.TryParse(tenantIdStr, out var tenantId))
            return BadRequest("Invalid Tenant ID format");

        var modules = await _context.TenantSidebarModules
            .Where(m => m.TenantId == tenantId && m.IsEnabled)
            .Select(m => m.ModuleKey)
            .ToListAsync();

        return Ok(modules);
    }

    /// <summary>
    /// Update or create a module toggle for a tenant (SuperAdmin only)
    /// </summary>
    [HttpPost("{tenantId}/modules")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> UpdateTenantModule(Guid tenantId, [FromBody] UpdateTenantModuleRequest request)
    {
        var module = await _context.TenantSidebarModules
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.ModuleKey == request.ModuleKey);

        if (module == null)
        {
            module = new TenantSidebarModule
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                ModuleKey = request.ModuleKey,
                IsEnabled = request.IsEnabled
            };
            _context.TenantSidebarModules.Add(module);
        }
        else
        {
            module.IsEnabled = request.IsEnabled;
            module.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return Ok(new TenantModuleResponse { ModuleKey = module.ModuleKey, IsEnabled = module.IsEnabled });
    }
}
