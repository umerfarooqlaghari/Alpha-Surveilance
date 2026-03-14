using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Linq;

namespace AlphaSurveilance.Bff.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class EmailTemplatesController : ControllerBase
    {
        private readonly HttpClient _violationApi;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public EmailTemplatesController(IHttpClientFactory httpClientFactory, IHttpContextAccessor httpContextAccessor)
        {
            _violationApi = httpClientFactory.CreateClient("ViolationApi");
            _httpContextAccessor = httpContextAccessor;
        }

        private void EnsureAuthHeader()
        {
            var authHeader = _httpContextAccessor.HttpContext?.Request.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(authHeader, out var headerValue))
            {
                _violationApi.DefaultRequestHeaders.Authorization = headerValue;
            }
        }

        public class EmailTemplateDto
        {
            public System.Guid Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public string Subject { get; set; } = string.Empty;
            public string Body { get; set; } = string.Empty;
            public System.Guid TenantId { get; set; }
            public System.DateTime CreatedAt { get; set; }
            public System.DateTime? UpdatedAt { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> GetTemplates()
        {
            EnsureAuthHeader();
            var response = await _violationApi.GetAsync("/api/EmailTemplates");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }
            return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetTemplate(System.Guid id)
        {
            EnsureAuthHeader();
            var response = await _violationApi.GetAsync($"/api/EmailTemplates/{id}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }
            return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        [HttpPost]
        public async Task<IActionResult> CreateTemplate([FromBody] EmailTemplateDto template)
        {
            EnsureAuthHeader();
            var json = JsonSerializer.Serialize(template);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _violationApi.PostAsync("/api/EmailTemplates", content);
            if (response.IsSuccessStatusCode)
            {
                var resContent = await response.Content.ReadAsStringAsync();
                return Content(resContent, "application/json");
            }
            return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateTemplate(System.Guid id, [FromBody] EmailTemplateDto template)
        {
            EnsureAuthHeader();
            var json = JsonSerializer.Serialize(template);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _violationApi.PutAsync($"/api/EmailTemplates/{id}", content);
            return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTemplate(System.Guid id)
        {
            EnsureAuthHeader();
            var response = await _violationApi.DeleteAsync($"/api/EmailTemplates/{id}");
            return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
    }
}
