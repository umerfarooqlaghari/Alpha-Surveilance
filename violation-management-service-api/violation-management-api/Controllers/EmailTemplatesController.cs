using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AlphaSurveilance.Data;
using AlphaSurveilance.Models;
using AlphaSurveilance.Services.Interfaces; // Updated namespace
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlphaSurveilance.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class EmailTemplatesController(
        AppViolationDbContext context,
        ICurrentTenantService currentTenantService) : ControllerBase
    {
        private Guid GetTenantId()
        {
            var tenantId = currentTenantService.TenantId;
            if (!tenantId.HasValue)
            {
                throw new UnauthorizedAccessException("User is not associated with a tenant.");
            }
            return tenantId.Value;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<EmailTemplate>>> GetTemplates()
        {
            var tenantId = GetTenantId();
            if (tenantId == Guid.Empty) return Unauthorized("Tenant ID not found in token");

            return await context.EmailTemplates
                .Where(t => t.TenantId == tenantId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<EmailTemplate>> GetTemplate(Guid id)
        {
            var tenantId = GetTenantId();
            var template = await context.EmailTemplates.FindAsync(id);

            if (template == null) return NotFound();
            if (template.TenantId != tenantId) return Forbid();

            return template;
        }

        [HttpPost]
        public async Task<ActionResult<EmailTemplate>> CreateTemplate(EmailTemplate template)
        {
            var tenantId = GetTenantId();
            if (tenantId == Guid.Empty) return Unauthorized("Tenant ID not found in token");

            template.Id = Guid.NewGuid();
            template.TenantId = tenantId;
            template.CreatedAt = DateTime.UtcNow;
            template.UpdatedAt = null;

            context.EmailTemplates.Add(template);
            await context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetTemplate), new { id = template.Id }, template);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTemplate(Guid id, EmailTemplate template)
        {
            if (id != template.Id) return BadRequest();

            var tenantId = GetTenantId();
            var existingTemplate = await context.EmailTemplates.FindAsync(id);

            if (existingTemplate == null) return NotFound();
            if (existingTemplate.TenantId != tenantId) return Forbid();

            existingTemplate.Name = template.Name;
            existingTemplate.Subject = template.Subject;
            existingTemplate.Body = template.Body;
            existingTemplate.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTemplate(Guid id)
        {
            var tenantId = GetTenantId();
            var template = await context.EmailTemplates.FindAsync(id);

            if (template == null) return NotFound();
            if (template.TenantId != tenantId) return Forbid();

            context.EmailTemplates.Remove(template);
            await context.SaveChangesAsync();

            return NoContent();
        }
    }
}
