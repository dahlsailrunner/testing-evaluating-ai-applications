using CarvedRock.Agent;

namespace CarvedRock.UnitTests;

public class PromptCatalogTests
{
    [Test]
    public async Task NoVersionMeansTheLatestOne()
    {
        var prompt = PromptCatalog.GetCustomerPrompt();

        await Assert.That(prompt.Id).IsEqualTo(PromptCatalog.LatestCustomer);
        await Assert.That(prompt.Text).IsEqualTo(AgentPrompts.Customer);
    }

    [Test]
    public async Task EveryVersionIsStoredUnderItsOwnId()
    {
        foreach (var id in PromptCatalog.CustomerVersionIds)
        {
            await Assert.That(PromptCatalog.GetCustomerPrompt(id).Id).IsEqualTo(id);
        }
    }

    [Test]
    public async Task TheWeakVersionIsNeverTheDefault()
    {
        // it exists for the evaluation demos, so nothing may reach it without asking for it by id
        await Assert.That(PromptCatalog.LatestCustomer).IsNotEqualTo("customer-3-weak");
        await Assert.That(PromptCatalog.GetCustomerPrompt("customer-3-weak").Text)
            .IsNotEqualTo(PromptCatalog.GetCustomerPrompt().Text);
    }

    [Test]
    public async Task AnUnknownVersionIsRefusedAndTheKnownOnesAreListed()
    {
        var error = Assert.Throws<ArgumentException>(() => PromptCatalog.GetCustomerPrompt("customer-99"));

        await Assert.That(error.Message).Contains("customer-99");
        await Assert.That(error.Message).Contains("customer-3");
    }
}
