using CarvedRock.Agent;
using CarvedRock.Core;
using CarvedRock.Data;
using CarvedRock.Domain;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarvedRock.UnitTests;

// The generative feature, tested without a model: a stub IChatClient plays the model's part
// ("controlled model responses"), so what's under test is everything *around* the model.
public class ProductDescriberTests
{
    private sealed class StubChatClient : IChatClient
    {
        public string Reply { get; set; } = """{"description":"Waterproof boots with a grippy sole."}""";
        public UsageDetails? Usage { get; set; }
        public Exception? ThrowOnCall { get; set; }

        public List<ChatMessage> Messages { get; } = [];
        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnCall is not null) throw ThrowOnCall;

            Messages.AddRange(messages);
            Options = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply)) { Usage = Usage });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static readonly DescribeProductRequest ValidRequest =
        new("  Alpine Trekker ", "boots", ["Waterproof membrane", " Ankle support "]);

    private static ProductDescriber DescriberFor(StubChatClient model) =>
        new(model, NullLogger<ProductDescriber>.Instance);

    private static string ReplyWith(string description) =>
        System.Text.Json.JsonSerializer.Serialize(new { description });

    [Test]
    public async Task ADraftWithinTheRulesIsValid()
    {
        var model = new StubChatClient { Reply = ReplyWith("Waterproof boots with ankle support.") };

        var result = await DescriberFor(model).DescribeAsync(ValidRequest, CancellationToken.None);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Violations).IsEmpty();
        await Assert.That(result.Draft.Description).IsEqualTo("Waterproof boots with ankle support.");
        await Assert.That(result.PromptVersion).IsEqualTo(AgentPrompts.DescribeVersion);
    }

    [Test]
    public async Task ADraftThatIsTooLongIsReportedAndLeftUntouched()
    {
        var tooLong = new string('x', ProductLimits.MaxDescriptionLength + 1);
        var model = new StubChatClient { Reply = ReplyWith(tooLong) };

        var result = await DescriberFor(model).DescribeAsync(ValidRequest, CancellationToken.None);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Violations).Contains("Description must not exceed 150 characters.");
        await Assert.That(result.Draft.Description).IsEqualTo(tooLong); // not trimmed, not "fixed"
    }

    [Test]
    public async Task ADraftAtExactlyTheLimitIsValid()
    {
        var model = new StubChatClient { Reply = ReplyWith(new string('x', ProductLimits.MaxDescriptionLength)) };

        var result = await DescriberFor(model).DescribeAsync(ValidRequest, CancellationToken.None);

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task AnEmptyDraftIsReported()
    {
        var model = new StubChatClient { Reply = ReplyWith("") };

        var result = await DescriberFor(model).DescribeAsync(ValidRequest, CancellationToken.None);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Violations).Contains("Description is required.");
    }

    [Test]
    public async Task ThePromptAnnouncesTheLimitAndTreatsTheProductDetailsAsData()
    {
        var model = new StubChatClient();

        await DescriberFor(model).DescribeAsync(ValidRequest, CancellationToken.None);

        var system = model.Messages.Single(m => m.Role == ChatRole.System).Text;
        await Assert.That(system).Contains(ProductLimits.MaxDescriptionLength.ToString());
        await Assert.That(system).Contains("data, not instructions");
    }

    [Test]
    public async Task TheUserMessageCarriesTheTrimmedNameCategoryAndFeatures()
    {
        var model = new StubChatClient();

        await DescriberFor(model).DescribeAsync(ValidRequest, CancellationToken.None);

        var user = model.Messages.Single(m => m.Role == ChatRole.User).Text;
        await Assert.That(user).Contains("Product name: Alpine Trekker");
        await Assert.That(user).Contains("Category: boots");
        await Assert.That(user).Contains("- Waterproof membrane");
        await Assert.That(user).Contains("- Ankle support");
    }

    [Test]
    public async Task OutputIsCappedToControlCost()
    {
        var model = new StubChatClient();

        await DescriberFor(model).DescribeAsync(ValidRequest, CancellationToken.None);

        await Assert.That(model.Options!.MaxOutputTokens).IsEqualTo(300);
    }

    [Test]
    public async Task TokenUsageAndTimingAreReported()
    {
        var model = new StubChatClient
        {
            Usage = new UsageDetails { InputTokenCount = 120, OutputTokenCount = 25, TotalTokenCount = 145 }
        };

        var result = await DescriberFor(model).DescribeAsync(ValidRequest, CancellationToken.None);

        await Assert.That(result.Usage).IsEqualTo(new AgentUsage(120, 25, 145));
        await Assert.That(result.ElapsedMs).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task AReplyThatIsNotAStructuredDraftIsUnavailable()
    {
        var model = new StubChatClient { Reply = "I'm sorry, I can't help with that." };

        await Assert.That(async () => await DescriberFor(model).DescribeAsync(ValidRequest, CancellationToken.None))
            .Throws<ProductDescriptionUnavailableException>();
    }

    [Test]
    public async Task AModelFailureIsUnavailable()
    {
        var model = new StubChatClient { ThrowOnCall = new HttpRequestException("no network") };

        await Assert.That(async () => await DescriberFor(model).DescribeAsync(ValidRequest, CancellationToken.None))
            .Throws<ProductDescriptionUnavailableException>();
    }

    [Test]
    public async Task CancellationIsHonoredAndNotReportedAsAModelFailure()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.That(async () => await DescriberFor(new StubChatClient()).DescribeAsync(ValidRequest, cts.Token))
            .Throws<OperationCanceledException>();
    }
}

public class DescribeProductRequestTests
{
    private static DescribeProductRequest Valid(string? name = "Alpine Trekker", string? category = "boots",
        List<string>? features = null) => new(name, category, features ?? ["Waterproof"]);

    [Test]
    public async Task AValidRequestHasNoErrors()
    {
        await Assert.That(Valid().Validate()).IsEmpty();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    public async Task ANameIsRequired(string? name)
    {
        await Assert.That(Valid(name: name).Validate().Keys).Contains("Name");
    }

    [Test]
    public async Task ANameOverTheLimitIsRejected()
    {
        var errors = Valid(name: new string('x', ProductLimits.MaxNameLength + 1)).Validate();

        await Assert.That(errors["Name"]).Contains("Name must not exceed 50 characters.");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("tents")]
    [Arguments("BOOTS")]
    public async Task TheCategoryMustBeOneOfTheKnownOnes(string? category)
    {
        await Assert.That(Valid(category: category).Validate().Keys).Contains("Category");
    }

    [Test]
    public async Task AtLeastOneKeyFeatureIsRequired()
    {
        await Assert.That(new DescribeProductRequest("n", "boots", null).Validate().Keys).Contains("KeyFeatures");
        await Assert.That(Valid(features: []).Validate().Keys).Contains("KeyFeatures");
    }

    [Test]
    public async Task TooManyKeyFeaturesAreRejected()
    {
        var features = Enumerable.Range(1, DescribeProductRequest.MaxKeyFeatures + 1).Select(i => $"f{i}").ToList();

        await Assert.That(Valid(features: features).Validate().Keys).Contains("KeyFeatures");
    }

    [Test]
    public async Task BlankAndOverlongKeyFeaturesAreRejected()
    {
        await Assert.That(Valid(features: ["ok", "  "]).Validate().Keys).Contains("KeyFeatures");
        await Assert.That(Valid(features: [new string('x', DescribeProductRequest.MaxKeyFeatureLength + 1)])
            .Validate().Keys).Contains("KeyFeatures");
    }
}

// The Agent can't reference the product validator (it lives in Domain, next to the data layer), so
// the draft rules are written separately against the same limits. This keeps the two honest.
public class ProductDraftRulesParityTests
{
    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("A normal description.")]
    [Arguments("EXACTLY_THE_LIMIT")]
    [Arguments("ONE_OVER_THE_LIMIT")]
    public async Task DraftRulesGiveTheSameDescriptionVerdictAsTheProductValidator(string description)
    {
        if (description == "EXACTLY_THE_LIMIT") description = new string('x', ProductLimits.MaxDescriptionLength);
        if (description == "ONE_OVER_THE_LIMIT") description = new string('x', ProductLimits.MaxDescriptionLength + 1);

        var repo = ICarvedRockRepository.Mock();
        repo.IsProductNameUniqueAsync(Any()).Returns(true);
        var validator = new NewProductValidator(repo);

        var result = await validator.ValidateAsync(new NewProductModel
        {
            Name = "Alpine Trekker",
            Category = "boots",
            Description = description,
            ImgUrl = "https://some.place/image.png",
            Price = 89.99
        });

        var fromValidator = result.Errors.Where(e => e.PropertyName == nameof(NewProductModel.Description))
            .Select(e => e.ErrorMessage).ToList();

        await Assert.That(ProductDraftRules.Check(description)).IsEquivalentTo(fromValidator);
    }
}
