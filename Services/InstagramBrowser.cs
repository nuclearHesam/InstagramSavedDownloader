using System.Text.RegularExpressions;
using InstagramSavedDownloader.Models;
using Microsoft.Playwright;

namespace InstagramSavedDownloader.Services;

public sealed class InstagramBrowser : IAsyncDisposable
{
    private readonly IPlaywright _pw;
    private readonly IBrowserContext _ctx;
    private readonly IPage _page;
    private readonly AppOptions _opts;

    private InstagramBrowser(IPlaywright pw, IBrowserContext context, IPage page, AppOptions options)
    {
        _pw = pw;
        _ctx = context;
        _page = page;
        _opts = options;
    }

    public static async Task<InstagramBrowser> CreateAsync(AppOptions options)
    {
        var playwright = await Playwright.CreateAsync();
        var profilePath = Path.GetFullPath(options.Instagram.BrowserProfilePath);
        Directory.CreateDirectory(profilePath);

        try
        {
            // Use a dedicated persistent profile. Never point this at Chrome's normal "User Data" folder.
            var context = await playwright.Chromium.LaunchPersistentContextAsync(profilePath, new()
            {
                Channel = options.Instagram.UseGoogleChrome ? "chrome" : null,
                Headless = options.Instagram.Headless,
                ViewportSize = new() { Width = 1400, Height = 950 },
                Locale = "en-US"
            });

            var page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
            await page.GotoAsync("https://www.instagram.com/", new()
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60000
            });

            // Existing Chrome automation profile is reused automatically. Import cookies only
            // when that profile is not already authenticated.
            if (IsLoginPage(page))
            {
                var cookiesPath = Path.GetFullPath(options.Instagram.CookiesPath);
                if (!File.Exists(cookiesPath))
                    throw new InvalidOperationException(
                        $"Instagram session is not active. Provide cookies once at: {cookiesPath}");

                await CookieImporter.ImportAsync(context, cookiesPath);
                await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
                await page.WaitForTimeoutAsync(2500);

                if (IsLoginPage(page))
                    throw new InvalidOperationException(
                        "Instagram still shows the login page. The cookies may be expired or invalid; refresh them and try again.");
            }

            return new InstagramBrowser(playwright, context, page, options);
        }
        catch
        {
            playwright.Dispose();
            throw;
        }
    }

    private static bool IsLoginPage(IPage page) =>
        page.Url.Contains("/accounts/login", StringComparison.OrdinalIgnoreCase);

    public async Task<List<SavedPost>> CollectSavedPostsAsync()
    {
        await _page.GotoAsync("https://www.instagram.com/", new()
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await _page.WaitForTimeoutAsync(3500);

        if (IsLoginPage(_page))
            throw new InvalidOperationException("Instagram session expired. Refresh cookies or sign in to the dedicated browser profile.");

        var links = await _page.Locator("a[href]").EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('href') || '').filter(h => /^\\/[A-Za-z0-9._]+\\/$/.test(h))");
        var excluded = new[] { "explore", "accounts", "direct", "reels", "stories", "about", "legal", "privacy" };
        var user = links.Select(x => x.Trim('/'))
            .FirstOrDefault(x => !excluded.Contains(x, StringComparer.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(user))
            throw new InvalidOperationException("Could not identify logged-in profile from page links.");

        await _page.GotoAsync($"https://www.instagram.com/{user}/saved/all/", new()
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await _page.WaitForTimeoutAsync(3000);

        if (IsLoginPage(_page))
            throw new InvalidOperationException("Instagram login required.");

        var found = new Dictionary<string, SavedPost>(StringComparer.OrdinalIgnoreCase);
        var quiet = 0;
        for (int i = 0; i < _opts.Instagram.MaxScrolls && quiet < 5; i++)
        {
            var hrefs = await _page.Locator("a[href]").EvaluateAllAsync<string[]>("els => els.map(e => e.href)");
            var oldCount = found.Count;

            foreach (var href in hrefs)
            {
                var match = Regex.Match(href, @"instagram\\.com/(p|reel|tv)/([A-Za-z0-9_-]+)");
                if (!match.Success) continue;
                var kind = match.Groups[1].Value;
                var code = match.Groups[2].Value;
                found[code] = new SavedPost(code, $"https://www.instagram.com/{kind}/{code}/");
            }

            quiet = found.Count == oldCount ? quiet + 1 : 0;
            await _page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
            await _page.WaitForTimeoutAsync(_opts.Instagram.ScrollDelayMs);
        }

        if (found.Count == 0)
            throw new InvalidOperationException("No Saved post links found. Instagram may have changed its Saved page structure.");

        return found.Values.ToList();
    }

    public async Task<bool> TryUnsaveAsync(string url)
    {
        await _page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await _page.WaitForTimeoutAsync(2200);

        var controls = _page.Locator("button[aria-label],div[role='button'][aria-label]");
        var count = await controls.CountAsync();
        var matches = new List<ILocator>();

        for (int i = 0; i < count; i++)
        {
            var element = controls.Nth(i);
            var label = await element.GetAttributeAsync("aria-label") ?? "";
            if (Regex.IsMatch(label, @"(remove.*saved|remove from saved|unsave)", RegexOptions.IgnoreCase))
                matches.Add(element);
        }

        if (matches.Count != 1) return false;

        await matches[0].ClickAsync(new() { Timeout = 10000 });
        await _page.WaitForTimeoutAsync(_opts.Instagram.ActionDelayMs);

        var labels = await controls.EvaluateAllAsync<string[]>(
            "els => els.map(e => e.getAttribute('aria-label') || '')");

        return labels.Any(x => Regex.IsMatch(x, @"(^save$|save this post)", RegexOptions.IgnoreCase))
            && !labels.Any(x => Regex.IsMatch(x, @"remove.*saved|remove from saved|unsave", RegexOptions.IgnoreCase));
    }

    public async ValueTask DisposeAsync()
    {
        await _ctx.CloseAsync();
        _pw.Dispose();
    }
}
