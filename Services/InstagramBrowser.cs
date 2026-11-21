using System.Text.RegularExpressions;
using InstagramSavedDownloader.Models;
using Microsoft.Playwright;
namespace InstagramSavedDownloader.Services;
public sealed class InstagramBrowser : IAsyncDisposable {
 private readonly IPlaywright _pw; private readonly IBrowser _browser; private readonly IBrowserContext _ctx;
 private readonly IPage _page; private readonly AppOptions _opts;
 private InstagramBrowser(IPlaywright p,IBrowser b,IBrowserContext c,IPage page,AppOptions o){_pw=p;_browser=b;_ctx=c;_page=page;_opts=o;}
 public static async Task<InstagramBrowser> CreateAsync(AppOptions o, string cookiesPath) {
  var p=await Playwright.CreateAsync();
  try {
   var channel=string.IsNullOrWhiteSpace(o.Instagram.BrowserChannel) ? null : o.Instagram.BrowserChannel.Trim();
   IBrowser b;
   try {
    b=await p.Chromium.LaunchAsync(new(){Headless=o.Instagram.Headless,Channel=channel});
   } catch (PlaywrightException ex) when (!string.IsNullOrWhiteSpace(channel)) {
    throw new InvalidOperationException($"Could not start browser channel '{channel}'. Install Google Chrome, or set Instagram:BrowserChannel to 'chromium' in appsettings.json. Details: {ex.Message}",ex);
   }
   var c=await b.NewContextAsync(new(){ViewportSize=new(){Width=1400,Height=950},Locale="en-US"});
   try {
    await CookieImporter.ImportAsync(c,cookiesPath);
    return new InstagramBrowser(p,b,c,await c.NewPageAsync(),o);
   } catch {
    await c.CloseAsync(); await b.CloseAsync(); throw;
   }
  } catch { p.Dispose(); throw; }
 }
 public async Task<List<SavedPost>> CollectSavedPostsAsync() {
  await _page.GotoAsync("https://www.instagram.com/",new(){WaitUntil=WaitUntilState.DOMContentLoaded,Timeout=60000});
  await _page.WaitForTimeoutAsync(3500);
  if(_page.Url.Contains("/accounts/login",StringComparison.OrdinalIgnoreCase))
   throw new InvalidOperationException("Cookies expired/invalid: Instagram redirected to login.");
  var links=await _page.Locator("a[href]").EvaluateAllAsync<string[]>("els => els.map(e=>e.getAttribute('href')||'').filter(h=>/^\\/[A-Za-z0-9._]+\\/$/.test(h))");
  var excluded=new[]{"explore","accounts","direct","reels","stories","about","legal","privacy"};
  var user=links.Select(x=>x.Trim('/')).FirstOrDefault(x=>!excluded.Contains(x,StringComparer.OrdinalIgnoreCase));
  if(string.IsNullOrWhiteSpace(user)) throw new InvalidOperationException("Could not identify logged-in profile from page links.");
  await _page.GotoAsync($"https://www.instagram.com/{user}/saved/all-posts/",new(){WaitUntil=WaitUntilState.DOMContentLoaded,Timeout=60000});
  await _page.WaitForTimeoutAsync(3000);
  if(_page.Url.Contains("/accounts/login",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Instagram login required.");
  var found=new Dictionary<string,SavedPost>(StringComparer.OrdinalIgnoreCase); var quiet=0;
  for(int i=0;i<_opts.Instagram.MaxScrolls && quiet<5;i++){
   var hrefs=await _page.Locator("a[href]").EvaluateAllAsync<string[]>("els=>els.map(e=>e.href)");
   int oldCount=found.Count;
   foreach(var h in hrefs){var m=Regex.Match(h,@"instagram\.com/(p|reel|tv)/([A-Za-z0-9_-]+)"); if(m.Success){var kind=m.Groups[1].Value; var id=m.Groups[2].Value; found[id]=new(id,$"https://www.instagram.com/{kind}/{id}/");}}
   quiet=found.Count==oldCount?quiet+1:0;
   await _page.EvaluateAsync("window.scrollTo(0,document.body.scrollHeight)");
   await _page.WaitForTimeoutAsync(_opts.Instagram.ScrollDelayMs);
  }
  if(found.Count==0) throw new InvalidOperationException("No Saved post links found. Instagram may have changed its Saved page structure.");
  return found.Values.ToList();
 }
 public async Task<bool> IsVideoPostAsync(string url) {
  await _page.GotoAsync(url,new(){WaitUntil=WaitUntilState.DOMContentLoaded,Timeout=60000});
  await _page.WaitForTimeoutAsync(1800);
  if(_page.Url.Contains("/accounts/login",StringComparison.OrdinalIgnoreCase))
   throw new InvalidOperationException("Instagram login required while checking post media type.");
  return await _page.EvaluateAsync<bool>("() => !!(document.querySelector('video') || document.querySelector('meta[property=\\"og:video\\"]') || document.querySelector('meta[property=\\"og:video:secure_url\\"]') || document.querySelector('meta[name=\\"twitter:player:stream\\"]'))");
 }
 public async Task<bool> TryUnsaveAsync(string url) {
  await _page.GotoAsync(url,new(){WaitUntil=WaitUntilState.DOMContentLoaded,Timeout=60000}); await _page.WaitForTimeoutAsync(2200);
  var controls=_page.Locator("button,[role='button']");
  int n=await controls.CountAsync();
  var matches=new List<ILocator>();
  for(int i=0;i<n;i++){
   var el=controls.Nth(i);
   var label=await el.EvaluateAsync<string>("e => [e.getAttribute('aria-label'),e.getAttribute('title'),e.innerText,...Array.from(e.querySelectorAll('svg[aria-label],svg title')).map(x=>x.getAttribute('aria-label')||x.textContent)].filter(Boolean).join(' ')");
   if(Regex.IsMatch(label,@"(remove.*saved|remove from saved|unsave|^\\s*remove\\s*$)",RegexOptions.IgnoreCase)) matches.Add(el);
  }
  if(matches.Count!=1) return false;
  await matches[0].ClickAsync(new(){Timeout=10000});
  await _page.WaitForTimeoutAsync(Math.Max(1200,_opts.Instagram.ActionDelayMs));
  var labels=await controls.EvaluateAllAsync<string[]>("els=>els.map(e=>[e.getAttribute('aria-label'),e.getAttribute('title'),e.innerText,...Array.from(e.querySelectorAll('svg[aria-label],svg title')).map(x=>x.getAttribute('aria-label')||x.textContent)].filter(Boolean).join(' '))");
  bool stillSaved=labels.Any(x=>Regex.IsMatch(x,@"(remove.*saved|remove from saved|unsave|^\\s*remove\\s*$)",RegexOptions.IgnoreCase));
  bool nowSavable=labels.Any(x=>Regex.IsMatch(x,@"(^\\s*save\\s*$|save this post)",RegexOptions.IgnoreCase));
  return !stillSaved && nowSavable;
 }
 public async ValueTask DisposeAsync(){await _ctx.CloseAsync();await _browser.CloseAsync();_pw.Dispose();}
}
