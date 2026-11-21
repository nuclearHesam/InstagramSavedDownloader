# Instagram Saved Downloader — Windows 10/11

.NET 8 console project. It launches Google Chrome by default and imports a Netscape cookies.txt, reads Saved post URLs, downloads sequentially with yt-dlp, persists progress in SQLite, and only attempts Unsave after local media output is detected.

## Requirements
- Windows 10/11 and .NET 8 SDK
- Google Chrome installed (default browser channel), or set `Instagram:BrowserChannel` to `chromium` to use Playwright Chromium
- `tools/yt-dlp.exe`
- `Cookies/cookies.txt` in Netscape HTTP Cookie format (must include valid Instagram session cookies)

## Run
```powershell
dotnet restore
dotnet build -c Release
.\bin\Release\net8.0\playwright.ps1 install chromium
dotnet run -c Release
```

## Safety behavior
- Sequential downloads only.
- Retry count is limited per run.
- A post is never Unsave'd when yt-dlp exits with an error or produces no media file.
- If the Unsave control is ambiguous or cannot be verified, the app leaves the post alone.
- State is stored in `Database/downloads.db`, so a later run can resume.

## Important limitations
Instagram changes its UI frequently. Saved-page discovery and Unsave button selectors are conservative and may need maintenance. If the page structure differs, the program stops rather than guessing.

yt-dlp currently has known failures for some image-only carousel posts; a recent issue documents cases where carousel entries are reported as having no video formats. Such posts remain Saved and are recorded as failures. This starter version does not yet include a separate image downloader.

Use only for your own account/content. It does not bypass CAPTCHA, checkpoints, 2FA, access controls, or rate limits. If Instagram requests verification, stop and refresh your cookies via your normal browser. Keep cookies private.
