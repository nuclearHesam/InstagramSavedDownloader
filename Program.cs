using InstagramSavedDownloader.Models;
using InstagramSavedDownloader.Services;
using Microsoft.Extensions.Configuration;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("Instagram Saved Downloader | persistent Chrome session");

var cfg = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .Build();
var options = cfg.Get<AppOptions>() ?? new AppOptions();

string Resolve(string path) => Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
var ytDlpPath = Resolve(options.Downloader.YtDlpPath);
var outputDirectory = Resolve(options.Downloader.OutputDirectory);

if (!File.Exists(ytDlpPath))
{
    Console.WriteLine($"Missing yt-dlp.exe: {ytDlpPath}");
    return;
}

var database = new StateStore(Resolve(options.Database.Path));
await database.InitializeAsync();

try
{
    await using var instagram = await InstagramBrowser.CreateAsync(options);
    Console.WriteLine($"Using persistent browser profile: {Resolve(options.Instagram.BrowserProfilePath)}");
    Console.WriteLine("Scanning Saved...");
    var posts = await instagram.CollectSavedPostsAsync();
    await database.UpsertPostsAsync(posts);
    Console.WriteLine($"Detected {posts.Count} post(s).");

    var downloader = new YtDlpDownloader(ytDlpPath, Resolve(options.Instagram.CookiesPath), outputDirectory);
    foreach (var item in await database.GetWorkAsync())
    {
        var completed = false;
        for (int retry = 1; retry <= options.Downloader.MaxRetriesPerRun && !completed; retry++)
        {
            Console.WriteLine($"\\n{item.Code} attempt {retry}/{options.Downloader.MaxRetriesPerRun} (lifetime attempts: {item.Attempts + retry})");
            await database.SetAsync(item.Code, "Downloading", item.Attempts + retry);
            var result = await downloader.DownloadAsync(item.Code, item.Url);

            if (!result.Ok || result.Folder == null)
            {
                await database.SetAsync(item.Code, "Failed", error: result.Error ?? "No media output.");
                Console.WriteLine("FAILED — remains in Saved.");
                if (retry < options.Downloader.MaxRetriesPerRun && options.Downloader.RetryDelaySeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(options.Downloader.RetryDelaySeconds));
                continue;
            }

            Console.WriteLine("Download output found. Checking Unsave control...");
            if (await instagram.TryUnsaveAsync(item.Url))
            {
                await database.SetAsync(item.Code, "Completed", output: result.Folder);
                Console.WriteLine("Completed and Unsave verified.");
            }
            else
            {
                await database.SetAsync(item.Code, "UnsaveFailed", output: result.Folder,
                    error: "Unsave control unavailable/ambiguous; left unchanged.");
                Console.WriteLine("Downloaded, but Unsave not verified — left untouched.");
            }

            completed = true;
            await Task.Delay(options.Instagram.ActionDelayMs);
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine("Stopped safely: " + ex.Message);
}

Console.WriteLine("\\nFinished. Press Enter.");
Console.ReadLine();
