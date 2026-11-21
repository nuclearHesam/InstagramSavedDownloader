using InstagramSavedDownloader.Models;
using InstagramSavedDownloader.Services;
using Microsoft.Extensions.Configuration;
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("Instagram Saved Downloader | sequential safe mode");
var cfg = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json").Build();
var o = cfg.Get<AppOptions>() ?? new AppOptions();
// Resolve configured paths from the application folder, not the shell's current directory.
string R(string p) => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, p));
var cookies = R(o.Instagram.CookiesPath);
var yt = R(o.Downloader.YtDlpPath);
var output = R(o.Downloader.OutputDirectory);
if (!File.Exists(cookies)) { Console.WriteLine($"Missing cookies: {cookies}"); return; }
if (!File.Exists(yt)) { Console.WriteLine($"Missing yt-dlp.exe: {yt}"); return; }
var db = new StateStore(R(o.Database.Path)); await db.InitializeAsync();
try
{
    await using var ig = await InstagramBrowser.CreateAsync(o, cookies);
    Console.WriteLine("Scanning Saved...");
    var posts = await ig.CollectSavedPostsAsync(); await db.UpsertPostsAsync(posts);
    Console.WriteLine($"Detected {posts.Count} post(s).");
    var downloader = new YtDlpDownloader(yt, cookies, output);
    foreach (var item in await db.GetWorkAsync())
    {
        var completed = false;
        for (int retry = 1; retry <= o.Downloader.MaxRetriesPerRun && !completed; retry++)
        {
            Console.WriteLine($"\\n{item.Code} attempt {retry}/{o.Downloader.MaxRetriesPerRun} (lifetime attempts: {item.Attempts + retry})");
            await db.SetAsync(item.Code, "Downloading", item.Attempts + retry);
            var result = await downloader.DownloadAsync(item.Code, item.Url);
            if (!result.Ok || result.Folder == null)
            {
                await db.SetAsync(item.Code, "Failed", error: result.Error ?? "No media output.");
                Console.WriteLine("FAILED — remains in Saved.");
                if (retry < o.Downloader.MaxRetriesPerRun && o.Downloader.RetryDelaySeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(o.Downloader.RetryDelaySeconds));
                continue;
            }
            Console.WriteLine("Download output found. Checking Unsave control...");
            if (await ig.TryUnsaveAsync(item.Url))
            {
                await db.SetAsync(item.Code, "Completed", output: result.Folder);
                Console.WriteLine("Completed and Unsave verified."); completed = true;
            }
            else
            {
                await db.SetAsync(item.Code, "UnsaveFailed", output: result.Folder, error: "Unsave control unavailable/ambiguous; left unchanged.");
                Console.WriteLine("Downloaded, but Unsave not verified — left untouched."); completed = true;
            }
            await Task.Delay(o.Instagram.ActionDelayMs);
        }
    }
}
catch (Exception ex) { Console.WriteLine("Stopped safely: " + ex.Message); }
Console.WriteLine("\\nFinished. Press Enter."); Console.ReadLine();
