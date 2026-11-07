using System.Collections.Concurrent;
using TUnit.Core.Interfaces;
using TUnit.Playwright;

namespace CarvedRock.AppTests.Utils;

public class CustomPageTest : PageTest
{
    [ClassDataSource<AppFixture>(Shared = SharedType.PerTestSession)]
    public required AppFixture Fixture { get; init; }

    public string WebAppUrl => Fixture.App.GetEndpoint("webapp").ToString();

    // When a navigation dies at the network layer, Chromium reports only
    // "chrome-error://chromewebdata/" with an empty document - the actual reason
    // (net::ERR_CONNECTION_RESET vs ERR_ABORTED vs a renderer crash) is available
    // solely from these events, and only while the test is still running. Record
    // them as they happen so DescribeLandingPageAsync can report the cause.
    private readonly List<string> _pageEvents = [];

    // Playwright names its recordings page@<hash>.webm, which tells you nothing about
    // which test produced which video once CI has uploaded a dozen of them. The name
    // can't be set through RecordVideoDir, and IVideo.SaveAsAsync waits for the page to
    // close - which hasn't happened yet inside an [After(Test)] hook. So note where each
    // video is headed while the test runs, then rename them all at the end of the
    // session, by which point every browser context has been torn down and flushed.
    private static readonly ConcurrentBag<(string TestName, string SourcePath)>
        RecordedVideos = [];

    [Before(Test)]
    public Task AddPageFailureHandling()
    {
        // No need to unsubscribe: Page lives exactly as long as this test instance.
        Page.RequestFailed += (_, request) => _pageEvents.Add(
            $"request failed: {request.Method} {request.Url} " +
            $"[{request.ResourceType}] -> {request.Failure ?? "(no reason given)"}");

        Page.Response += (_, response) =>
        {
            if (response.Status >= 400)
                _pageEvents.Add($"http {response.Status}: " +
                                $"{response.Request.Method} {response.Url}");
        };

        Page.Crash += (_, _) => _pageEvents.Add("*** the renderer process crashed ***");
        Page.PageError += (_, error) => _pageEvents.Add($"page error: {error}");
        Page.Console += (_, message) =>
        {
            if (message.Type == "error") _pageEvents.Add($"console error: {message.Text}");
        };

        return Task.CompletedTask;
    }

    [Before(Test)]
    public async Task NoteVideoPathForRenaming(TestContext testContext)
    {
        if (Page.Video is null) return;

        // A retried test records once per attempt; number them so the flaky-test videos
        // line up with the attempts shown in the run report instead of overwriting.
        var attempt = testContext.Execution.CurrentRetryAttempt;
        var name = testContext.Metadata.TestName +
                   (attempt > 0 ? $"-attempt{attempt + 1}" : string.Empty);

        RecordedVideos.Add((name, await Page.Video.PathAsync()));
    }

    [After(TestSession)]
    public static void RenameRecordedVideos()
    {
        foreach (var (testName, sourcePath) in RecordedVideos)
        {
            try
            {
                if (!File.Exists(sourcePath)) continue;

                var directory = Path.GetDirectoryName(sourcePath)!;
                var safeName = SanitizeForFileName(testName);
                var target = Path.Combine(directory, $"{safeName}.webm");

                // Last-resort de-duplication, e.g. a test that opens more than one page.
                for (var n = 2; File.Exists(target); n++)
                    target = Path.Combine(directory, $"{safeName}-{n}.webm");

                File.Move(sourcePath, target);
            }
            catch (Exception renameFailure)
            {
                // A recording we couldn't rename is still a usable recording - never fail
                // a run (or hide the real result) over cosmetic artifact naming.
                Console.WriteLine($"Could not rename video for {testName}: {renameFailure.Message}");
            }
        }
    }

    // playwright browsers on linux don't play well with the self-signed certs
    // this override is really only to support CI pipelines
    public override BrowserNewContextOptions ContextOptions(TestContext testContext)
    {
        var options = base.ContextOptions(testContext);
        options.IgnoreHTTPSErrors = true;

        if (testContext.StateBag.ContainsKey(RecordVideoAttribute.StateBagKey))
        {
            options.RecordVideoDir = "playwright-artifacts/";

            options.ViewportSize = new ViewportSize
            { Width = 1280, Height = 1400 };
        }

        return options;
    }

    private static string SanitizeForFileName(string value) =>
        string.Concat(value.Split(Path.GetInvalidFileNameChars())).Replace(' ', '-');
}

// Browser tests are the heaviest thing in this suite: every one is its own Chromium
// instance, running on top of the Aspire AppHost (two containers plus four services)
// and whatever the sibling test projects are doing in the same `dotnet test` run.
public record BrowserParallelLimit : IParallelLimit
{
    public int Limit => 3;
}

public static class PageExtensions
{
    public static async Task Login(this IPage page, string username, string password)
    {
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Username" })
                                .FillAsync(username);
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Password" })
                                .ClickAsync();
        await page.GetByRole(AriaRole.Textbox, new() { Name = "Password" })
                                .FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Login" }).ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Sign Out" }))
                                .ToBeVisibleAsync();
    }
}

// The StateBag is used to avoid reflection and use TUnit source generation approach
[AttributeUsage(AttributeTargets.Method)]
public sealed class RecordVideoAttribute : Attribute, ITestDiscoveryEventReceiver
{
    internal const string StateBagKey = "CarvedRock.RecordVideo";

    public ValueTask OnTestDiscovered(DiscoveredTestContext discoveredTestContext)
    {
        discoveredTestContext.TestContext.StateBag[StateBagKey] = true;
        return default;
    }
}