using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CarvedRock.Api.Controllers;

[ApiController]
[Route("[controller]")]
public partial class SampleController(ILogger<SampleController> logger)
                                                : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<string> Echo(string text)
    {
        logger.LogInformation("Got into Echo.");
        return $"You sent {text}";
    }

    [HttpGet]
    [Route("auth")]
    public async Task<string> EchoWithAuth(string text)
    {
        logger.LogInformation("Got into EchoWithAuth.");
        foreach (var claim in User.Claims)
        {
            logger.LogInformation($"Found claim: [{claim.Type}]=[{claim.Value}] ");
        }
        return $"You sent {text}";
    }

    [HttpGet]
    [Authorize(Roles = "admin")]
    [Route("auth-admin")]
    public async Task<string> EchoWithAdmin(string text)
    {
        logger.LogInformation("Got into EchoWithAdmin.");
        return $"You sent {text}";
    }
}
