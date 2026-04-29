using Microsoft.AspNetCore.Mvc;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using System.Text;

namespace alpha_surveilance_bff.Controllers
{
    [ApiController]
    [Route("api/face-scan")]
    public class FaceScanBffController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public FaceScanBffController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        [AllowAnonymous]
        [HttpGet("verify-token")]
        public async Task<IActionResult> VerifyToken([FromQuery] string token)
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var response = await client.GetAsync($"/api/face-scan/verify-token?token={token}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }
            return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        [AllowAnonymous]
        [HttpPost("submit")]
        public async Task<IActionResult> Submit([FromBody] object requestBody)
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/face-scan/submit", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return Content(responseContent, "application/json");
            }
            return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        [Authorize(Policy = "TenantAdmin")]
        [HttpPost("send-invites")]
        public async Task<IActionResult> SendInvites([FromBody] object requestBody)
        {
            var client = _httpClientFactory.CreateClient("ViolationApi");
            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/api/face-scan/send-invites", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return Content(responseContent, "application/json");
            }
            return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
    }
}
