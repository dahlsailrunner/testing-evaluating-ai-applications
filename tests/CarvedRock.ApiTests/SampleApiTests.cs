namespace CarvedRock.ApiTests;

public class SampleApiTests : ApiTestsBase
{
    [Test]
    [Arguments("hello")]
    [Arguments("goodbye")]
    public async Task GetSample_ReturnsOk(string message)
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync($"/sample?text={message}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).IsEqualTo($"You sent {message}");
    }

    [Test]
    public async Task GetSampleAnonymous_ReturnsUnauthorized()
    {
        var client = Factory.CreateClient();
        var response = await client.GetAsync("/sample/auth?text=hello");

        await Assert.That(response.StatusCode)
                    .IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetSampleAsAdmin_ReturnsOK()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Authorization", "Bob Smith");
        client.DefaultRequestHeaders.Add("X-Test-idp", "CarvedRock");
        client.DefaultRequestHeaders.Add("X-Test-email", "bobsmith@someplace.com");

        var response = await client.GetAsync("/sample/auth?text=hello");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).IsEqualTo($"You sent hello");
    }

    [Test]
    public async Task GetSampleAsNonAdmin_ReturnsOK()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Authorization", "Erik Dahl");
        client.DefaultRequestHeaders.Add("X-Test-idp", "CarvedRock");
        client.DefaultRequestHeaders.Add("X-Test-email", "erikdahl@someplace.com");

        var response = await client.GetAsync("/sample/auth?text=hello");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).IsEqualTo($"You sent hello");
    }

    [Test]
    public async Task GetSampleAdminAsAdmin_ReturnsOK()
    {
        var client = Factory.CreateClient();
        client.AddAdminAuthHeaders();        

        var response = await client.GetAsync("/sample/auth-admin?text=hello");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).IsEqualTo($"You sent hello");
    }

    [Test]
    public async Task GetSampleAdminAsNonAdmin_ReturnsForbidden()
    {
        var client = Factory.CreateClient();
        client.AddCustomerAuthHeaders();

        var response = await client.GetAsync("/sample/auth-admin?text=hello");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }
}