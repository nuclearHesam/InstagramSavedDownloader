using InstagramSavedDownloader.Models;
using InstagramSavedDownloader.Services;
using Microsoft.Extensions.Configuration;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("Instagram Saved Downloader | video-only mode");
var cfg = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json").Build();
var o = cfg.Get<AppOptions>() ?? new AppOptions();
string R(string p) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, p));
var cookies = R(o.Instagram.CookiesPath);
var yt = R(o.Downloader.YtDlpPath);
var output = R(o.Downloader.OutputDirectory);
if (!File.Exists(cookies)) { Console.WriteLine($"Missing cookies: {cookies}"); return; }
if (!File.Exists(yt)) { Console.WriteLine($"Missing yt-dlp.exe: {yt}"); return; }

var db = new StateStore(R(o.Database.Path));
await db.InitializeAsync();
try
{
    await using var ig = await InstagramBrowser.CreateAsync(o, cookies);
    Console.WriteLine("Scanning Saved...");
    var posts = await ig.CollectSavedPostsAsync();
    await db.UpsertPostsAsync(posts);
    Console.WriteLine($"Detected {posts.Count} post(s).");

    var downloader = new YtDlpDownloader(yt, cookies, output);
    foreach (var item in await db.GetWorkAsync())
    {
        // A previously downloaded post only needs its Unsave action retried.
        if (item.Status == "UnsaveFailed")
        {
            Console.WriteLine($"\n{item.Code}: retrying Unsave (download already exists).");
            if (await ig.TryUnsaveAsync(item.Url))
            {
                await db.SetAsync(item.Code, "Completed");
                Console.WriteLine("Unsave verified.");
            }
            else
            {
                Console.WriteLine("Unsave still could not be verified; no duplicate download was started.");
            }
            continue;
        }

        // Inspect the post in Instagram before invoking yt-dlp. Photo-only posts are
        // marked once and never enter the download/retry loop.
        Console.WriteLine($"\nChecking media type: {item.Code}");
        if (!await ig.IsVideoPostAsync(item.Url))
        {
            await db.SetAsync(item.Code, "SkippedImage", error: "Photo-only post; image downloads are disabled.");
            Console.WriteLine("Skipped — photo-only post.");
            continue;
        }

        var completed = false;
        for (int retry = 1; retry <= o.Downloader.MaxRetriesPerRun && !completed; retry++)
        {
            Console.WriteLine($"{item.Code} attempt {retry}/{o.Downloader.MaxRetriesPerRun} (lifetime attempts: {item.Attempts + retry})");
            await db.SetAsync(item.Code, "Downloading", item.Attempts + retry);
            var result = await downloader.DownloadAsync(item.Code, item.Url);
            if (!result.Ok || result.Folder == null)
            {
                await db.SetAsync(item.Code, "Failed", error: result.Error ?? "No video output.");
                Console.WriteLine("FAILED — remains in Saved.");
                if (retry < o.Downloader.MaxRetriesPerRun && o.Downloader.RetryDelaySeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(o.Downloader.RetryDelaySeconds));
                continue;
            }

            Console.WriteLine("Video downloaded. Removing post from Saved...");
            if (await ig.TryUnsaveAsync(item.Url))
            {
                await db.SetAsync(item.Code, "Completed", output: result.Folder);
                Console.WriteLine("Completed — video downloaded and Unsave verified.");
            }
            else
            {
                await db.SetAsync(item.Code, "UnsaveFailed", output: result.Folder,
                    error: "Video downloaded, but Instagram did not confirm Unsave. Next run will retry Unsave without downloading again.");
                Console.WriteLine("Video downloaded, but Unsave was not verified. It will retry Unsave only next run.");
            }
            completed = true;
            await Task.Delay(o.Instagram.ActionDelayMs);
        }
    }
}
catch (Exception ex) { Console.WriteLine("Stopped safely: " + ex.Message); }
Console.WriteLine("\nFinished. Press Enter.");
Console.ReadLine();
