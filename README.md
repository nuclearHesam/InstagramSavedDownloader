# Instagram Saved Downloader — Windows 10/11

A .NET 8 console app that scans your Instagram Saved posts, downloads them sequentially with yt-dlp, tracks progress in SQLite, and only attempts to Unsave after a successful local download.

## Requirements
- Windows 10/11 and .NET 8 SDK
- Google Chrome installed (recommended configuration)
- `tools/yt-dlp.exe`
- A valid Netscape-format `Cookies/cookies.txt` for the first authentication, if the dedicated browser profile is not already signed in

## First run
1. Export your own Instagram cookies to `Cookies/cookies.txt` in Netscape format.
2. Put `yt-dlp.exe` in `tools/yt-dlp.exe`.
3. Run:

```powershell
dotnet restore
dotnet build -c Release
pwsh bin/Release/net8.0/playwright.ps1 install chromium
dotnet run -c Release
```

The app launches Google Chrome with a **separate automation profile** in `BrowserProfile/`. On the first run, it imports the cookies if that profile is not already signed in. Chrome then stores the session in that profile, so later runs reuse it and normally do not need the cookies file again.

You can also sign in manually in the launched Chrome window once; the dedicated profile will retain that session.

## Important Chrome note
Do not point the app at your everyday Chrome `User Data` directory. Current Chrome security behavior blocks remote debugging/automation against the default profile. The app creates and uses its own `BrowserProfile` folder instead.

## Safety behavior
- Sequential downloads only.
- Retry count is limited per run.
- A post is never Unsave'd when yt-dlp exits with an error or produces no media file.
- If the Unsave control is ambiguous or cannot be verified, the app leaves the post alone.
- State is stored in `Database/downloads.db`, so a later run can resume.

## Session and privacy
- Cookies and the persistent browser profile are credentials. Never commit or share them.
- Keep `Cookies/cookies.txt`, `BrowserProfile/`, `Downloads/`, and `Database/` out of Git.
- Instagram may expire/revoke sessions or request a checkpoint/2FA. In that case, sign in manually in the dedicated Chrome window or refresh the cookies file.
- The app does not bypass CAPTCHA, checkpoints, 2FA, access controls, or rate limits.

## Limitations
Instagram changes its UI frequently. Saved-page discovery and Unsave button selectors are conservative and may need maintenance. Image-only carousel downloads are not guaranteed in this starter version; failed posts remain Saved.
