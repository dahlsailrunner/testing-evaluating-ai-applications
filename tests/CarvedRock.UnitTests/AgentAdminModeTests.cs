using CarvedRock.Agent;
using System.Security.Claims;

namespace CarvedRock.UnitTests;

public class AgentAdminModeTests
{
    private static ClaimsPrincipal User(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test"));

    [Test]
    public async Task AdminPrefixOnTheMessageEntersAdminMode()
    {
        await Assert.That(Agent.Agent.IsAdminModeRequested("/admin delete product 4", null, User("admin")))
            .IsTrue();
    }

    [Test]
    public async Task AdminModeStaysOnWhenAnEarlierUserTurnUsedThePrefix()
    {
        List<ChatTurn> history =
        [
            new("user", "/admin delete products 20 and 23"),
            new("assistant", "Please confirm you want to delete both products.")
        ];

        await Assert.That(Agent.Agent.IsAdminModeRequested("yes, go ahead", history, User("admin")))
            .IsTrue();
    }

    [Test]
    public async Task AssistantTurnsStartingWithThePrefixDoNotCountAsRequests()
    {
        List<ChatTurn> history = [new("assistant", "/admin is how you start admin mode")];

        await Assert.That(Agent.Agent.IsAdminModeRequested("hello", history, User("admin")))
            .IsFalse();
    }

    [Test]
    public async Task NonAdminsNeverGetAdminModeEvenWithThePrefix()
    {
        List<ChatTurn> history = [new("user", "/admin delete everything")];

        await Assert.That(Agent.Agent.IsAdminModeRequested("/admin delete product 4", history, User()))
            .IsFalse();
        await Assert.That(Agent.Agent.IsAdminModeRequested("/admin delete product 4", history, null))
            .IsFalse();
    }

    [Test]
    public async Task AdminsWithoutThePrefixAnywhereStayInCustomerMode()
    {
        List<ChatTurn> history = [new("user", "recommend some boots")];

        await Assert.That(Agent.Agent.IsAdminModeRequested("add them to my cart", history, User("admin")))
            .IsFalse();
    }
}
