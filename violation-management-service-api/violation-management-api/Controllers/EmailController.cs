using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AlphaSurveilance.Data;
using AlphaSurveilance.Models;
using AlphaSurveilance.Services.Interfaces;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AlphaSurveilance.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class EmailController(
        IEmailService emailService,
        AppViolationDbContext context,
        IAmazonS3 s3Client,
        IConfiguration config,
        ICurrentTenantService currentTenantService) : ControllerBase
    {
        private Guid GetTenantId()
        {
            var tenantId = currentTenantService.TenantId;
            if (!tenantId.HasValue)
            {
                // For EmailController, maybe we want to allow SuperAdmin to send emails on behalf of a tenant?
                // But the current implementation filters by tenantId.
                // If SuperAdmin, we might need adjustments, but let's enforce tenant context for now
                // or return Guid.Empty which will likely fail lookups (safe fail).
                throw new UnauthorizedAccessException("User is not associated with a tenant.");
            }
            return tenantId.Value;
        }

        public class SendEmailRequest
        {
            public List<Guid> EmployeeIds { get; set; } = [];
            public List<Guid> ViolationIds { get; set; } = [];
            public string Subject { get; set; } = string.Empty;
            public string Body { get; set; } = string.Empty;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendEmail([FromForm] SendEmailRequest request, [FromForm] List<IFormFile> attachments)
        {
            var tenantId = GetTenantId();
            if (tenantId == Guid.Empty) return Unauthorized("Tenant ID not found in token");

            if (request.EmployeeIds == null || request.EmployeeIds.Count == 0)
                return BadRequest("No employees selected.");

            // 1. Fetch Employees
            var employees = await context.Employees
                .Where(e => request.EmployeeIds.Contains(e.Id) && e.TenantId == tenantId.ToString())
                .ToListAsync();

            if (employees.Count == 0)
                return BadRequest("No valid employees found.");

            var recipientEmails = employees.Select(e => e.Email).ToList();

            // 2. Prepare Attachments
            var emailAttachments = new List<AttachmentDto>();

            // a. Process Uploaded Files
            if (attachments != null)
            {
                foreach (var file in attachments)
                {
                    if (file.Length > 0)
                    {
                        using var ms = new MemoryStream();
                        await file.CopyToAsync(ms);
                        emailAttachments.Add(new AttachmentDto
                        {
                            FileName = file.FileName,
                            ContentType = file.ContentType,
                            Content = ms.ToArray()
                        });
                    }
                }
            }

            // b. Process Violations (Download from S3 and Link)
            if (request.ViolationIds != null && request.ViolationIds.Count > 0)
            {
                var violations = await context.Violations
                    .Where(v => request.ViolationIds.Contains(v.Id) && v.TenantId == tenantId)
                    .ToListAsync();

                var bucketName = config["S3Config:BucketName"];
                var linksHtml = "<h3>Violation Evidences:</h3><ul>";

                foreach (var violation in violations)
                {
                    if (!string.IsNullOrEmpty(violation.FramePath))
                    {
                        try
                        {
                            // 1. Generate Presigned URL (Valid for 7 days)
                            var requestUrl = new GetPreSignedUrlRequest
                            {
                                BucketName = bucketName,
                                Key = violation.FramePath,
                                Expires = DateTime.UtcNow.AddDays(7)
                            };
                            var url = s3Client.GetPreSignedURL(requestUrl);
                            linksHtml += $"<li><a href='{url}'>View Evidence for Violation {violation.Id}</a></li>";

                            // 2. Download and Attach (as requested "aswell")
                            var getObjectRequest = new GetObjectRequest
                            {
                                BucketName = bucketName,
                                Key = violation.FramePath
                            };

                            using var response = await s3Client.GetObjectAsync(getObjectRequest);
                            using var ms = new MemoryStream();
                            await response.ResponseStream.CopyToAsync(ms);

                            var fileName = $"Violation_{violation.Id}.jpg"; 
                            if (violation.FramePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) fileName = $"Violation_{violation.Id}.png";

                            emailAttachments.Add(new AttachmentDto
                            {
                                FileName = fileName,
                                ContentType = response.Headers.ContentType ?? "image/jpeg",
                                Content = ms.ToArray()
                            });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to process violation image: {ex.Message}"); 
                        }
                    }
                }
                linksHtml += "</ul>";
                
                // Append links to body
                request.Body += "<br/><hr/>" + linksHtml;
            }

            // 3. Send Email
            var result = await emailService.SendEmailAsync(recipientEmails, request.Subject, request.Body, emailAttachments);

            if (result)
                return Ok(new { Message = "Email sent successfully", RecipientCount = recipientEmails.Count });
            else
                return StatusCode(500, "Failed to send email");
        }
    }
}
