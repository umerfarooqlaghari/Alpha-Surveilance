using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AlphaSurveilance.Data;
using AlphaSurveilance.Core.Domain;
using AlphaSurveilance.Core.Enums;
using AlphaSurveilance.Services.Interfaces;
using violation_management_api.Services.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Text.Json;

namespace AlphaSurveilance.Controllers
{
    [ApiController]
    [Route("api/face-scan")]
    public class FaceScanController : ControllerBase
    {
        private readonly AppViolationDbContext _context;
        private readonly ILogger<FaceScanController> _logger;
        private readonly ICurrentTenantService _currentTenantService;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IEmailService _emailService;

        public FaceScanController(
            AppViolationDbContext context,
            ILogger<FaceScanController> logger,
            ICurrentTenantService currentTenantService,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            IEmailService emailService)
        {
            _context = context;
            _logger = logger;
            _currentTenantService = currentTenantService;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _emailService = emailService;
        }

        public class SendInvitesRequest { public List<string> EmployeeIds { get; set; } = new(); }
        
        public class SubmitEmbeddingRequest 
        { 
            public string Token { get; set; } = string.Empty; 
            public List<float> Embedding { get; set; } = new(); 
            public string? PhotoUrl { get; set; } 
        }

        private string GenerateEnrollmentToken(Guid tenantId, string employeeId)
        {
            var claims = new List<Claim>
            {
                new Claim("tenantId", tenantId.ToString()),
                new Claim("employeeId", employeeId),
                new Claim("purpose", "face_scan_enrollment"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:SecretKey"]!));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            
            // Enrollment token valid for 24 hours
            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(24),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private ClaimsPrincipal? ValidateEnrollmentToken(string token)
        {
            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.UTF8.GetBytes(_configuration["Jwt:SecretKey"]!);

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = _configuration["Jwt:Issuer"],
                    ValidAudience = _configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ClockSkew = TimeSpan.Zero
                };

                var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
                
                // Ensure it's specifically an enrollment token
                if (principal.FindFirst("purpose")?.Value != "face_scan_enrollment")
                {
                    return null;
                }
                
                return principal;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Enrollment token validation failed");
                return null;
            }
        }

        [Authorize]
        [HttpPost("send-invites")]
        public async Task<IActionResult> SendInvites([FromBody] SendInvitesRequest request)
        {
            var tenantId = _currentTenantService.TenantId;
            if (tenantId == null) return Unauthorized();

            var employees = await _context.Employees
                .Where(e => e.TenantId == tenantId.ToString() && request.EmployeeIds.Contains(e.Id.ToString()))
                .ToListAsync();

            int sentCount = 0;
            var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:3000";

            foreach (var employee in employees)
            {
                var token = GenerateEnrollmentToken(tenantId.Value, employee.EmployeeId);
                var enrollLink = $"{frontendUrl}/enroll/{token}";

                string subject = "Alpha Surveillance - Face Scan Enrollment Required";
                string body = $@"
                    <h2>Face Scan Enrollment</h2>
                    <p>Dear {employee.FirstName},</p>
                    <p>You have been requested to complete your face scan enrollment for site access.</p>
                    <p>Please click the link below on your smartphone to complete the scan. The link is valid for 24 hours.</p>
                    <a href='{enrollLink}' style='display:inline-block;padding:10px 20px;background:#0066cc;color:#fff;text-decoration:none;border-radius:5px;'>Complete Enrollment</a>
                ";

                var success = await _emailService.SendEmailAsync(new List<string> { employee.Email }, subject, body);
                
                if (success)
                {
                    employee.FaceScanStatus = FaceScanStatus.Pending;
                    employee.FaceScanInviteSentAt = DateTime.UtcNow;
                    sentCount++;
                }
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = $"Sent {sentCount} invites successfully." });
        }

        [AllowAnonymous]
        [HttpGet("verify-token")]
        public async Task<IActionResult> VerifyToken([FromQuery] string token)
        {
            var principal = ValidateEnrollmentToken(token);
            if (principal == null) return Unauthorized("Invalid or expired token.");

            var tenantIdStr = principal.FindFirst("tenantId")?.Value;
            var employeeId = principal.FindFirst("employeeId")?.Value;

            if (string.IsNullOrEmpty(tenantIdStr) || string.IsNullOrEmpty(employeeId))
                return Unauthorized("Token missing required claims.");

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.TenantId == tenantIdStr && e.EmployeeId == employeeId);

            if (employee == null) return NotFound("Employee not found.");

            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id.ToString() == tenantIdStr);

            return Ok(new
            {
                employeeName = $"{employee.FirstName} {employee.LastName}",
                tenantName = tenant?.TenantName ?? "Your Organization",
                status = employee.FaceScanStatus.ToString()
            });
        }

        [AllowAnonymous]
        [HttpPost("submit")]
        public async Task<IActionResult> Submit([FromBody] SubmitEmbeddingRequest request)
        {
            var principal = ValidateEnrollmentToken(request.Token);
            if (principal == null) return Unauthorized("Invalid or expired token.");

            var tenantIdStr = principal.FindFirst("tenantId")?.Value;
            var employeeId = principal.FindFirst("employeeId")?.Value;

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.TenantId == tenantIdStr && e.EmployeeId == employeeId);

            if (employee == null) return NotFound("Employee not found.");

            if (request.Embedding == null || request.Embedding.Count != 128)
            {
                return BadRequest("Invalid embedding vector. Expected 128 dimensions.");
            }

            // Call ReID Service to store embedding
            var reidUrl = _configuration["Services:Reid:HttpUrl"] ?? "http://localhost:8001";
            var client = _httpClientFactory.CreateClient();
            
            var payload = new
            {
                tenant_id = tenantIdStr,
                person_id = employeeId,
                embedding = request.Embedding,
                metadata_json = new { source = "mobile_enrollment", photoUrl = request.PhotoUrl }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{reidUrl}/embeddings", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to save embedding to ReID service: {Error}", error);
                return StatusCode(500, "Failed to store face scan.");
            }

            // Update employee status
            employee.FaceScanStatus = FaceScanStatus.Completed;
            employee.FaceScanCompletedAt = DateTime.UtcNow;
            
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }
    }
}
