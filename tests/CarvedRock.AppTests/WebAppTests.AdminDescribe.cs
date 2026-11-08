namespace CarvedRock.AppTests;

// "Generate description" on the admin Create and Edit forms: drafts the Description field through
// the agent's POST /agent/describe. Neither test saves anything (the draft only fills the field), so
// they leave no product behind and share no state with the other tests.
public partial class WebAppTests
{
    private async Task SignInAsBobAndOpenAsync(string adminPath)
    {
        await Page.GotoAsync(WebAppUrl);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Sign in" }).ClickAsync();
        await Page.Login("bob", "bob");
        await Page.GotoAsync(new Uri(new Uri(WebAppUrl), adminPath).ToString());
    }

    // Needs an OpenAI key, like the chat tests: the draft comes from the real model, so the
    // assertions are on the rules every draft must meet (present, within the limit), not on its wording.
    [Test]
    [RecordVideo]
    public async Task AdminCanGenerateADescriptionOnTheCreateForm()
    {
        await SignInAsBobAndOpenAsync("Admin/Create");

        await Page.Locator("#Product_Name").FillAsync("Alpine Trail Mug");
        await Page.Locator("#Product_Category").FillAsync("equip");
        await Page.Locator("#keyFeatures").FillAsync("Insulated steel\nHolds 12 ounces\nDishwasher safe");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Generate description" }).ClickAsync();

        var status = Page.Locator("#describeStatus");
        await Expect(status).ToContainTextAsync("Draft added");

        var description = await Page.Locator("#Product_Description").InputValueAsync();
        await Assert.That(description.Trim().Length).IsGreaterThan(0);
        await Assert.That(description.Length).IsLessThanOrEqualTo(150);
    }

    // The request is validated before the model is called, so this needs no OpenAI key and spends
    // no tokens: the field-level message from the agent is shown and the description is left alone.
    [Test]
    [RecordVideo]
    public async Task GeneratingWithoutKeyFeaturesShowsAMessageAndKeepsTheDescription()
    {
        await SignInAsBobAndOpenAsync("Admin/Edit?id=1");

        var description = Page.Locator("#Product_Description");
        var original = await description.InputValueAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Generate description" }).ClickAsync();

        await Expect(Page.Locator("#describeStatus")).ToContainTextAsync("at least one key feature");
        await Expect(description).ToHaveValueAsync(original);
    }

    // The sample data's image strategy: https://picsum.photos/id/{id}/800/600 with ids 10, 15 ... 255.
    [Test]
    public async Task RandomPicsumImageButtonFillsInAnImageUrlLikeTheSampleData()
    {
        await SignInAsBobAndOpenAsync("Admin/Create");
        await Page.Locator("#Product_Description").FillAsync("x".PadRight(40, 'x'));
        await Expect(Page.Locator("#descriptionCount")).ToHaveTextAsync("40");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Random picsum image" }).ClickAsync();

        var url = await Page.Locator("#Product_ImgUrl").InputValueAsync();
        await Assert.That(url).Matches(@"^https://picsum\.photos/id/\d+/800/600$");
        var id = int.Parse(url.Split('/')[4]);
        await Assert.That(id is >= 10 and <= 255 && id % 5 == 0).IsTrue();
    }
}
