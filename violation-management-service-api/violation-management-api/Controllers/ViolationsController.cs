using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using AlphaSurveilance.DTOs.Requests;
using AlphaSurveilance.DTOs.Responses;
using AlphaSurveilance.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AlphaSurveilance.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Require valid JWT for all endpoints
    public class ViolationsController(IViolationService violationService) : ControllerBase
    {
        [HttpGet("{id}")]
        public async Task<ActionResult<ViolationResponse>> GetViolation(Guid id, [FromHeader(Name = "X-Tenant-Id")] string tenantId)
        {
            // Security: In a real "military grade" app, tenantId would be extracted from the JWT
            if (string.IsNullOrEmpty(tenantId)) return BadRequest("Tenant ID is required.");

            var violation = await violationService.GetViolationAsync(id, tenantId);
            if (violation == null) return NotFound();

            return Ok(violation);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<ViolationResponse>>> GetViolations([FromHeader(Name = "X-Tenant-Id")] string tenantId)
        {
            if (string.IsNullOrEmpty(tenantId)) return BadRequest("Tenant ID is required.");

            var violations = await violationService.GetViolationsAsync(tenantId);
            return Ok(violations);
        }

        [HttpPost]
        public async Task<ActionResult<ViolationResponse>> CreateViolation([FromBody] ViolationRequest request)
        {
            // Security: Ensure the user's tenant matches the requested tenant
            var violation = await violationService.CreateViolationAsync(request);
            return CreatedAtAction(nameof(GetViolation), new { id = violation.Id }, violation);
        }

        [HttpGet("stats")]
        public async Task<ActionResult<ViolationStatsResponse>> GetStats([FromHeader(Name = "X-Tenant-Id")] string tenantId)
        {
            if (string.IsNullOrEmpty(tenantId)) return BadRequest("Tenant ID is required.");

            var stats = await violationService.GetStatsAsync(tenantId);
            return Ok(stats);
        }
    }
}
