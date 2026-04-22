namespace alpha_surveilance_bff.Controllers
{
    using alpha_surveilance_bff.DTOs;
    using AlphaSurveilance.Audit.Grpc;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Authorization;
    using System.Text.Json;

    [ApiController]
    [Route("api/dashboard")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class DashboardController(
        IHttpClientFactory httpClientFactory,
        AuditService.AuditServiceClient auditClient,
        ILogger<DashboardController> logger) : ProxyControllerBase
    {
        private string? GetTenantId() => User.FindFirst("tenantId")?.Value;

        [HttpGet("violations/{id}")]
        public async Task<IActionResult> GetViolationDetails(Guid id)
        {
            var tenantId = GetTenantId();
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant context is missing from your session.");

            // Aggregation Pattern: Fetch from multiple services in parallel
            
            // 1. Task: Fetch core violation data from Violation Management Service (REST)
            var violationTask = FetchViolationFromApi(id, tenantId);

            // 2. Task: Fetch audit trail from Audit Service (gRPC)
            var auditTask = FetchAuditLogsFromGrpc(id);

            await Task.WhenAll(violationTask, auditTask);

            var violation = await violationTask;
            var auditLogs = await auditTask;

            if (violation == null)
                return NotFound("Violation not found");

            // 3. Assemble the "God Object" for the UI
            var dashboardDto = new DashboardViolationDetailDto
            {
                Id = violation.Id,
                Type = violation.Type.ToString(), // Assuming Enum or String
                Severity = violation.Severity?.ToString() ?? "Unknown",
                Timestamp = violation.Timestamp,
                FramePath = violation.FramePath,
                CameraId = violation.CameraId,
                
                AuditHistory = auditLogs
            };

            return Ok(dashboardDto);
        }

        [HttpGet("violations/recent")]
        public async Task<IActionResult> GetRecentViolations()
        {
            var tenantId = GetTenantId();
            if (string.IsNullOrEmpty(tenantId)) return Unauthorized("Tenant context is missing from your session.");

            try
            {
                var client = httpClientFactory.CreateClient("ViolationApi");
                var request = new HttpRequestMessage(HttpMethod.Get, "api/violations");
                
                // CRITICAL: We inject the tenant ID from the AUTHENTICATED context only.
                // This prevents users from spoofing other tenant's data via headers.
                request.Headers.Add("X-Tenant-Id", tenantId); 

                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode) return StatusCode((int)response.StatusCode);

                var violations = await response.Content.ReadFromJsonAsync<List<ExternalViolationDto>>();
                
                // Map to match the SignalR Notification format expected by UI
                var uiViolations = violations?.Select(v => new
                {
                    id = v.Id,
                    type = ((ViolationType)v.Type).ToString(), 
                    severity = ((ViolationSeverity)(v.Severity ?? 0)).ToString(),
                    timestamp = v.Timestamp,
                    framePath = v.FramePath,
                    cameraId = v.CameraId
                });

                return Ok(uiViolations);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch recent violations");
                return StatusCode(500, "Internal BFF Error");
            }
        }

        private async Task<ExternalViolationDto?> FetchViolationFromApi(Guid id, string tenantId)
        {
            try
            {
                var client = httpClientFactory.CreateClient("ViolationApi");
                var request = new HttpRequestMessage(HttpMethod.Get, $"api/violations/{id}");
                request.Headers.Add("X-Tenant-Id", tenantId);

                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;

                return await response.Content.ReadFromJsonAsync<ExternalViolationDto>();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch violation {Id} from VMS", id);
                return null;
            }
        }

        private async Task<List<AuditLogDto>> FetchAuditLogsFromGrpc(Guid id)
        {
            try
            {
                var request = new GetViolationLogsRequest { ViolationId = id.ToString() };
                var response = await auditClient.GetViolationLogsAsync(request);

                return response.Logs.Select(l => new AuditLogDto
                {
                    AuditId = l.AuditId,
                    Type = l.Type,
                    Timestamp = DateTime.Parse(l.Timestamp)
                }).ToList();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch audit logs for {Id}", id);
                return new List<AuditLogDto>(); // Return empty list gracefully so dashboard still works
            }
        }

        // Internal DTO to map the VMS response
        private class ExternalViolationDto
        {
            public Guid Id { get; set; }
            public int Type { get; set; } // Enums often come as ints by default
            public int? Severity { get; set; }
            public DateTime Timestamp { get; set; }
            public string FramePath { get; set; } = string.Empty;
            public string CameraId { get; set; } = string.Empty;
        }

        private enum ViolationType
        {
            Unknown = 0,
            Safety = 1,
            Security = 2,
            Operational = 3,
            Compliance = 4
        }

        private enum ViolationSeverity
        {
            Low = 0,
            Medium = 1,
            High = 2,
            Critical = 3
        }
    }
}
