using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;

namespace alpha_surveilance_bff.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EmployeesController(IHttpClientFactory httpClientFactory) : ProxyControllerBase
    {
        private readonly HttpClient _client = httpClientFactory.CreateClient("ViolationApi");

        [HttpGet("template")]
        public IActionResult GetTemplate()
        {
            var csvHeader = "firstName,lastName,email,employeeId,number,companyName,designation,department,tenure,grade,gender,managerId,Skills,Certifications,Languages";
            var bytes = Encoding.UTF8.GetBytes(csvHeader);
            return File(bytes, "text/csv", "employees_template.csv");
        }

        [HttpPost("bulk-import")]
        public async Task<IActionResult> BulkImport(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest("No file uploaded.");

            var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(file.OpenReadStream());
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
            content.Add(fileContent, "file", file.FileName);

            // Forward X-Tenant-Id header if present (HeaderPropagation should handle Authorization, but let's be safe for TenantId)
            if (Request.Headers.TryGetValue("X-Tenant-Id", out var tenantId))
            {
                _client.DefaultRequestHeaders.Remove("X-Tenant-Id");
                _client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString());
            }

            var response = await _client.PostAsync("/api/employees/bulk-import", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, error);
            }

            var result = await response.Content.ReadAsStringAsync();
            return Content(result, "application/json");
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            // Forward Query String
            var queryString = Request.QueryString.ToString();
            var response = await _client.GetAsync($"/api/employees{queryString}");
            
            var content = await response.Content.ReadAsStringAsync();
            
            // Forward Pagination Headers
            if (response.Headers.TryGetValues("X-Total-Count", out var totalCount))
            {
                Response.Headers.Append("X-Total-Count", totalCount.FirstOrDefault());
            }

            return StatusCode((int)response.StatusCode, content);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] JsonElement body)
        {
            var response = await _client.PostAsJsonAsync("/api/employees", body);
            var content = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, content);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var response = await _client.GetAsync($"/api/employees/{id}");
            var content = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, content);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] JsonElement body)
        {
            var response = await _client.PutAsJsonAsync($"/api/employees/{id}", body);
            return StatusCode((int)response.StatusCode);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var response = await _client.DeleteAsync($"/api/employees/{id}");
            return StatusCode((int)response.StatusCode);
        }
    }
}
