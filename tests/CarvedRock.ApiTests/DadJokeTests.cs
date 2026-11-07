using Microsoft.Extensions.Logging;

namespace CarvedRock.ApiTests;

public class DadJokeTests : ApiTestsBase
{
    [Test]
    public async Task GetDadJokeWorks()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/dadjoke");

        var joke = await response.Content.ReadAsStringAsync();

        TestLogger.LogInformation($"JOKE RESPONSE: {joke}");

        await Assert.That(joke).IsNotNullOrWhiteSpace();
    }
}