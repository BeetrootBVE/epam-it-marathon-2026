using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace RestaurantAqa;

[TestFixture]
[Category("UI")]
[Category("L1")]
public class MenuLevel1UiTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = new AqaConfig().UiUrl,
        ViewportSize = new() { Width = 1280, Height = 800 }
    };

    [SetUp]
    public async Task StartTrace() => await Context.Tracing.StartAsync(new()
    {
        Screenshots = true, Snapshots = true, Sources = true
    });

    [Test]
    public async Task Guest_can_open_menu_and_use_category_and_sort_controls()
    {
        await Page.GotoAsync("/menu");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Menu", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator(".category-buttons button").First).ToBeVisibleAsync();
        await Expect(Page.Locator("#sortSelect")).ToBeVisibleAsync();
        await Page.Locator(".category-buttons button").First.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"[?&]category="));
        await Page.Locator("#sortSelect").SelectOptionAsync(new SelectOptionValue
        {
            Index = 1
        });
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"[?&]sort="));
    }

    [TearDown]
    public async Task SaveFailureArtifacts()
    {
        var failed = TestContext.CurrentContext.Result.Outcome.Status == NUnit.Framework.Interfaces.TestStatus.Failed;
        string? tracePath = null;
        if (failed)
        {
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "TestResults", "artifacts");
            Directory.CreateDirectory(directory);
            var id = Guid.NewGuid().ToString("N")[..10];
            var screenshotPath = Path.Combine(directory, "menu-failure-" + id + ".png");
            tracePath = Path.Combine(directory, "menu-trace-" + id + ".zip");
            try
            {
                await Page.ScreenshotAsync(new() { Path = screenshotPath, FullPage = true });
                TestContext.AddTestAttachment(screenshotPath);
            }
            catch { /* Browser may be unavailable in CI; TRX still records the error. */ }
        }
        if (failed && tracePath is not null)
            await Context.Tracing.StopAsync(new TracingStopOptions { Path = tracePath });
        else
            await Context.Tracing.StopAsync();
        if (failed && tracePath is not null && File.Exists(tracePath))
            TestContext.AddTestAttachment(tracePath);
    }
}
