using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using alpha_surveilance_bff.Controllers;

namespace AlphaSurveilance.Bff.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class EmailController(IHttpClientFactory httpClientFactory) : ProxyControllerBase
    {
        private readonly HttpClient _violationApi = httpClientFactory.CreateClient("ViolationApi");

        public class SendEmailRequest
        {
            public List<System.Guid> EmployeeIds { get; set; } = [];
            public List<System.Guid> ViolationIds { get; set; } = [];
            public string Subject { get; set; } = string.Empty;
            public string Body { get; set; } = string.Empty;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendEmail([FromForm] SendEmailRequest request, [FromForm] List<IFormFile> attachments)
        {
            using var content = new MultipartFormDataContent();

            // Add fields
            if (request.EmployeeIds != null)
            {
                foreach (var id in request.EmployeeIds)
                {
                    content.Add(new StringContent(id.ToString()), "EmployeeIds");
                }
            }

            if (request.ViolationIds != null)
            {
                foreach (var id in request.ViolationIds)
                {
                    content.Add(new StringContent(id.ToString()), "ViolationIds");
                }
            }

            content.Add(new StringContent(request.Subject ?? ""), "Subject");
            content.Add(new StringContent(request.Body ?? ""), "Body");

            // Add attachments
            if (attachments != null)
            {
                foreach (var file in attachments)
                {
                    var fileContent = new StreamContent(file.OpenReadStream());
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
                    content.Add(fileContent, "attachments", file.FileName);
                }
            }

            var response = await _violationApi.PostAsync("/api/Email/send", content);
            
            if (response.IsSuccessStatusCode)
            {
                return Ok(await response.Content.ReadAsStringAsync());
            }

            return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
    }
}
