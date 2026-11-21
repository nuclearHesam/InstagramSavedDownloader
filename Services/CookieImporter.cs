using Microsoft.Playwright;
namespace InstagramSavedDownloader.Services;

public static class CookieImporter
{
    public static async Task ImportAsync(IBrowserContext context, string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Cookies file not found: {path}");
        var cookies = new List<Cookie>();
        foreach (var raw in await File.ReadAllLinesAsync(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") && !line.StartsWith("#HttpOnly_", StringComparison.OrdinalIgnoreCase)) continue;
            bool httpOnly = line.StartsWith("#HttpOnly_", StringComparison.OrdinalIgnoreCase);
            if (httpOnly) line = line["#HttpOnly_".Length..];
            var p = line.Split('\t');
            if (p.Length < 7) continue;
            var exp = float.TryParse(p[4], out var e) ? e : -1;
            cookies.Add(new Cookie
            {
                Domain = p[0],
                Path = string.IsNullOrWhiteSpace(p[2]) ? "/" : p[2],
                Secure = p[3].Equals("TRUE", StringComparison.OrdinalIgnoreCase),
                HttpOnly = httpOnly,
                Expires = exp > 0 ? exp : -1,
                Name = p[5],
                Value = string.Join("\t", p.Skip(6)),
                SameSite = SameSiteAttribute.Lax
            });
        }
        if (cookies.Count == 0) throw new InvalidDataException("No valid Netscape cookies found.");
        await context.AddCookiesAsync(cookies);
    }
}
