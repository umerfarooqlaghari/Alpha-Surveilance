using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using violation_management_api.Core.Entities;
using System.Security.Claims;

namespace violation_management_api.Controllers;

[ApiController]
[Route("api/notification-emails")]
[Authorize]
public class NotificationEmailsController(AppViolationDbContext db, ILogger<NotificationEmailsController> logger) : ControllerBase
{
    private Guid? GetCurrentTenantId()
    {
        var tenantIdClaim = User.FindFirstValue("tenantId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(tenantIdClaim, out var tid)) return tid;
        return null;
    }

    /// <summary>GET all notification emails for the calling tenant.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tenantId = GetCurrentTenantId();
        if (tenantId == null) return Unauthorized();

        var emails = await db.TenantNotificationEmails
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new { e.Id, e.Email, e.Label, e.IsActive, e.CreatedAt })
            .ToListAsync();

        return Ok(emails);
    }

    /// <summary>GET notification emails for a specific tenant (SuperAdmin only).</summary>
    [HttpGet("tenant/{tenantId:guid}")]
    [Authorize(Policy = "SuperAdminOnly")]
    public async Task<IActionResult> GetForTenant(Guid tenantId)
    {
        var emails = await db.TenantNotificationEmails
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new { e.Id, e.Email, e.Label, e.IsActive, e.CreatedAt })
            .ToListAsync();

        return Ok(emails);
    }

    /// <summary>Add a new notification email for the calling tenant.</summary>
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddNotificationEmailRequest request)
    {
        var tenantId = GetCurrentTenantId();
        if (tenantId == null) return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
            return BadRequest(new { error = "A valid email address is required." });

        var normalised = request.Email.Trim().ToLowerInvariant();

        var exists = await db.TenantNotificationEmails
            .AnyAsync(e => e.TenantId == tenantId && e.Email == normalised);

        if (exists)
            return Conflict(new { error = "This email address is already registered for notifications." });

        var entry = new TenantNotificationEmail
        {
            TenantId = tenantId.Value,
            Email = normalised,
            Label = request.Label?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        db.TenantNotificationEmails.Add(entry);
        await db.SaveChangesAsync();

        logger.LogInformation("Tenant {TenantId} added notification email {Email}", tenantId, normalised);
        return Ok(new { entry.Id, entry.Email, entry.Label, entry.IsActive, entry.CreatedAt });
    }

    /// <summary>Toggle active/inactive for a notification email.</summary>
    [HttpPatch("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid id)
    {
        var tenantId = GetCurrentTenantId();
        if (tenantId == null) return Unauthorized();

        var entry = await db.TenantNotificationEmails
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);

        if (entry == null) return NotFound();

        entry.IsActive = !entry.IsActive;
        await db.SaveChangesAsync();
        return Ok(new { entry.Id, entry.Email, entry.IsActive });
    }

    /// <summary>Remove a notification email.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantId = GetCurrentTenantId();
        if (tenantId == null) return Unauthorized();

        var entry = await db.TenantNotificationEmails
            .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);

        if (entry == null) return NotFound();

        db.TenantNotificationEmails.Remove(entry);
        await db.SaveChangesAsync();

        logger.LogInformation("Tenant {TenantId} removed notification email {Email}", tenantId, entry.Email);
        return NoContent();
    }
}

public record AddNotificationEmailRequest(string Email, string? Label);
