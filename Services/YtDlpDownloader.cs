using System.Diagnostics;
namespace InstagramSavedDownloader.Services;
public sealed class YtDlpDownloader {
 private readonly string _exe,_cookies,_root;
 public YtDlpDownloader(string exe,string cookies,string root){_exe=exe;_cookies=cookies;_root=root;Directory.CreateDirectory(root);}
 public async Task<(bool Ok,string? Folder,string? Error)> DownloadAsync(string shortcode,string url){
  if(!File.Exists(_exe)) return(false,null,$"yt-dlp not found: {_exe}");
  var folder=Path.Combine(_root,shortcode); Directory.CreateDirectory(folder);
  var psi=new ProcessStartInfo{FileName=_exe,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
  foreach(var a in new[]{"--cookies",_cookies,"--newline","--write-info-json","--windows-filenames","--no-overwrites","-o",Path.Combine(folder,"%(id)s_%(autonumber)s.%(ext)s"),url}) psi.ArgumentList.Add(a);
  using var p=new Process{StartInfo=psi}; var err=new System.Text.StringBuilder();
  p.OutputDataReceived+=(_,e)=>{if(e.Data!=null)Console.WriteLine("  "+e.Data);};
  p.ErrorDataReceived+=(_,e)=>{if(e.Data!=null)err.AppendLine(e.Data);};
  p.Start();p.BeginOutputReadLine();p.BeginErrorReadLine();await p.WaitForExitAsync();
  var media=Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Where(f=>!f.EndsWith(".json",StringComparison.OrdinalIgnoreCase)&&!f.EndsWith(".part",StringComparison.OrdinalIgnoreCase)&&!f.EndsWith(".ytdl",StringComparison.OrdinalIgnoreCase)).ToArray();
  return p.ExitCode==0&&media.Length>0?(true,folder,null):(false,folder,$"yt-dlp exit {p.ExitCode}: {err}");
 }
}
