using Microsoft.AspNetCore.Mvc;

namespace alpha_surveilance_bff.Controllers.Tenant;

[ApiController]
[Route("api/tenant/[controller]")]
public class TenantsController : ProxyControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;

    public TenantsController(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("my-modules")]
    public async Task<IActionResult> GetMyModules()
    {
        var client = _httpClientFactory.CreateClient("ViolationApi");
        var response = await client.GetAsync("/api/tenants/my-modules");
        return await ProxyResponse(response);
    }
}
