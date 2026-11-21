namespace InstagramSavedDownloader.Models;

public sealed class AppOptions
{
    public InstagramOptions Instagram { get; set; } = new();
    public DownloaderOptions Downloader { get; set; } = new();
    public DatabaseOptions Database { get; set; } = new();
}

public sealed class InstagramOptions
{
    public string CookiesPath { get; set; } = "Cookies/cookies.txt";
    public string BrowserProfilePath { get; set; } = "BrowserProfile";
    public bool UseGoogleChrome { get; set; } = true;
    public bool Headless { get; set; } = false;
    public int MaxScrolls { get; set; } = 80;
    public int ScrollDelayMs { get; set; } = 1800;
    public int ActionDelayMs { get; set; } = 2500;
}

public sealed class DownloaderOptions
{
    public string YtDlpPath { get; set; } = "tools/yt-dlp.exe";
    public string OutputDirectory { get; set; } = "Downloads";
    public int RetryDelaySeconds { get; set; } = 60;
    public int MaxRetriesPerRun { get; set; } = 3;
}

public sealed class DatabaseOptions
{
    public string Path { get; set; } = "Database/downloads.db";
}
