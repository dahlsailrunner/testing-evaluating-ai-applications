using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;

namespace CarvedRock.Api.Controllers;

[ApiController]
[Route("[controller]")]
[AllowAnonymous]
public class DadJokeController(IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<string> Get()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(config.GetValue<string>("DadJokeUrl")!)
        };
        client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.GetFromJsonAsync<JokeResponse>("/");
        return response!.Joke;
    }
}

public record JokeResponse(string Id, string Joke);