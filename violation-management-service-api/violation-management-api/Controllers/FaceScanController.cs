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
using System.Net.Http.Json;
using System.Linq;

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

        /// <summary>
        /// Enrollment now submits raw face images (one per scan angle) instead
        /// of a client-computed embedding. The browser previously computed a
        /// face-api.js (TensorFlow.js) descriptor, which happens to also be
        /// 128-d but is NOT the same embedding space dlib/face_recognition
        /// produces at live-recognition time in vision-inference-service — so
        /// an enrolled face could never reliably match against the camera
        /// feed. Submit() now sends each image to vision-inference-service's
        /// /internal/face-embedding endpoint, which computes the embedding
        /// with the exact same pipeline identify_person() uses.
        /// </summary>
        public class SubmitFaceImagesRequest
        {
            public string Token { get; set; } = string.Empty;
            public List<string> ImagesBase64 { get; set; } = new();
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
            var frontendUrl = _configuration["FrontendUrl"] ?? "https://alpha-surveilance.vercel.app";

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
        public async Task<IActionResult> Submit([FromBody] SubmitFaceImagesRequest request)
        {
            var principal = ValidateEnrollmentToken(request.Token);
            if (principal == null) return Unauthorized("Invalid or expired token.");

            var tenantIdStr = principal.FindFirst("tenantId")?.Value;
            var employeeId = principal.FindFirst("employeeId")?.Value;

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.TenantId == tenantIdStr && e.EmployeeId == employeeId);

            if (employee == null) return NotFound("Employee not found.");

            if (request.ImagesBase64 == null || request.ImagesBase64.Count == 0)
            {
                return BadRequest("At least one face image is required.");
            }

            var visionBaseUrl = _configuration.GetValue<string>("VisionService:BaseUrl");
            if (string.IsNullOrWhiteSpace(visionBaseUrl))
            {
                _logger.LogError("VisionService:BaseUrl is not configured; cannot compute face embeddings.");
                return StatusCode(503, "Face enrollment is temporarily unavailable.");
            }
            var internalApiKey = _configuration["InternalApi:ApiKey"];
            var reidUrl = _configuration["Services:Reid:HttpUrl"] ?? "http://localhost:8001";
            var client = _httpClientFactory.CreateClient();

            var storedCount = 0;
            for (var i = 0; i < request.ImagesBase64.Count; i++)
            {
                var imageBase64 = request.ImagesBase64[i];
                if (string.IsNullOrWhiteSpace(imageBase64)) continue;

                // 1. Compute the embedding SERVER-SIDE using the same
                //    dlib/face_recognition pipeline live camera recognition
                //    uses, instead of trusting a client-computed face-api.js
                //    vector (a different, incompatible embedding space).
                List<float>? embedding;
                try
                {
                    using var embedRequest = new HttpRequestMessage(
                        HttpMethod.Post, $"{visionBaseUrl.TrimEnd('/')}/internal/face-embedding");
                    if (!string.IsNullOrWhiteSpace(internalApiKey))
                    {
                        embedRequest.Headers.TryAddWithoutValidation("X-Internal-Api-Key", internalApiKey);
                    }
                    embedRequest.Content = new StringContent(
                        JsonSerializer.Serialize(new { image_base64 = imageBase64 }), Encoding.UTF8, "application/json");

                    using var embedResponse = await client.SendAsync(embedRequest);
                    if (!embedResponse.IsSuccessStatusCode)
                    {
                        var err = await embedResponse.Content.ReadAsStringAsync();
                        _logger.LogWarning(
                            "Vision service rejected face image {Index} for employee {EmployeeId}: HTTP {Status} {Err}",
                            i, employeeId, (int)embedResponse.StatusCode, err);
                        continue; // skip this angle; try the remaining ones
                    }

                    var payload = await embedResponse.Content.ReadFromJsonAsync<JsonElement>();
                    embedding = payload.GetProperty("embedding").EnumerateArray()
                        .Select(e => e.GetSingle()).ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed calling vision service face-embedding endpoint for employee {EmployeeId}", employeeId);
                    continue;
                }

                if (embedding == null || embedding.Count == 0) continue;

                // 2. Store the resulting (dlib-space) embedding in the ReID vector store.
                var storePayload = new
                {
                    tenant_id = tenantIdStr,
                    person_id = employeeId,
                    embedding,
                    metadata_json = new { source = "mobile_enrollment", angle = i }
                };
                var storeContent = new StringContent(JsonSerializer.Serialize(storePayload), Encoding.UTF8, "application/json");
                var storeResponse = await client.PostAsync($"{reidUrl}/embeddings", storeContent);

                if (storeResponse.IsSuccessStatusCode)
                {
                    storedCount++;
                }
                else
                {
                    var error = await storeResponse.Content.ReadAsStringAsync();
                    _logger.LogError("Failed to save embedding to ReID service: {Error}", error);
                }
            }

            if (storedCount == 0)
            {
                return StatusCode(422, "Could not detect a usable face in any submitted image. Please retry the scan.");
            }

            // Update employee status
            employee.FaceScanStatus = FaceScanStatus.Completed;
            employee.FaceScanCompletedAt = DateTime.UtcNow;
            
            await _context.SaveChangesAsync();

            return Ok(new { success = true, anglesStored = storedCount });
        }

        /// <summary>
        /// Resets a completed face-scan enrollment so the employee can re-enroll.
        /// Deletes all stored reid embeddings for the employee and resets
        /// FaceScanStatus back to NotStarted. Requires TenantAdmin.
        /// </summary>
        [Authorize]
        [HttpPost("reset/{employeeId}")]
        public async Task<IActionResult> ResetFaceScan(string employeeId)
        {
            var tenantId = _currentTenantService.TenantId;
            if (tenantId == null) return Unauthorized();

            var employee = await _context.Employees
                .FirstOrDefaultAsync(e =>
                    e.TenantId == tenantId.ToString() &&
                    e.EmployeeId == employeeId);

            if (employee == null) return NotFound("Employee not found.");

            // Delete stored embeddings from the reid service
            var reidUrl = _configuration["Services:Reid:HttpUrl"] ?? "http://localhost:8001";
            var client = _httpClientFactory.CreateClient();

            var deleteResponse = await client.DeleteAsync(
                $"{reidUrl}/embeddings/person/{Uri.EscapeDataString(employeeId)}?tenant_id={tenantId}");

            if (!deleteResponse.IsSuccessStatusCode)
            {
                var err = await deleteResponse.Content.ReadAsStringAsync();
                _logger.LogWarning("Reid service returned {Status} while deleting embeddings for {EmployeeId}: {Err}",
                    deleteResponse.StatusCode, employeeId, err);
                // Non-fatal: continue and reset the DB status anyway
            }

            // Reset enrollment status
            employee.FaceScanStatus = FaceScanStatus.NotAssigned;
            employee.FaceScanInviteSentAt = null;
            employee.FaceScanCompletedAt = null;

            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Face scan data cleared. You may now re-send the enrollment invite." });
        }
    }
}
