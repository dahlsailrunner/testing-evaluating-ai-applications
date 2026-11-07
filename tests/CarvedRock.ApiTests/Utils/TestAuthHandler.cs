using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace CarvedRock.ApiTests.Utils;

public class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
                                ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestScheme";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Context.Request.Headers.TryGetValue("X-Authorization", out var value))
        {
            return Task.FromResult(AuthenticateResult.Fail("No X-Authorization Header"));
        }

        var userName = value.First();

        var claims = new List<Claim> { new("name", userName!) };
        claims.AddRange(GetClaimsFromHttpHeaders());

        var identity = new ClaimsIdentity(claims, "TestAuthType");

        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        var result = AuthenticateResult.Success(ticket);

        return Task.FromResult(result);
    }

    private IEnumerable<Claim> GetClaimsFromHttpHeaders()
    {
        var headers = Context.Request.Headers;

        return from header in headers
               where header.Key.StartsWith("X-Test-")
               let claimType = header.Key.Replace("X-Test-", "")
               select new Claim(claimType, header.Value!);
    }
}

public static class AuthHelpers
{
    public static void AddAdminAuthHeaders(this HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Authorization", "Bob Smith");
        client.DefaultRequestHeaders.Add("X-Test-sub", "456");
        client.DefaultRequestHeaders.Add("X-Test-idp", "CarvedRock");
        client.DefaultRequestHeaders.Add("X-Test-email", "bobsmith@someplace.com");
    }

    public static void AddCustomerAuthHeaders(this HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Authorization", "Erik Dahl");
        client.DefaultRequestHeaders.Add("X-Test-sub", "123");
        client.DefaultRequestHeaders.Add("X-Test-idp", "CarvedRock");
        client.DefaultRequestHeaders.Add("X-Test-email", "erikdahl@someplace.com");
    }
}