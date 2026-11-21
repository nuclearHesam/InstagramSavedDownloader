using Microsoft.Data.Sqlite;
using InstagramSavedDownloader.Models;
namespace InstagramSavedDownloader.Services;
public sealed class StateStore {
 private readonly string _cs;
 public StateStore(string path) {
  Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
  _cs=new SqliteConnectionStringBuilder{DataSource=path}.ToString();
 }
 public async Task InitializeAsync() {
  await using var c=new SqliteConnection(_cs); await c.OpenAsync();
  var q=c.CreateCommand(); q.CommandText="""
  CREATE TABLE IF NOT EXISTS Posts(
   Shortcode TEXT PRIMARY KEY, Url TEXT NOT NULL, Status TEXT NOT NULL,
   Attempts INTEGER NOT NULL DEFAULT 0, OutputPath TEXT, LastError TEXT, UpdatedUtc TEXT NOT NULL
  );
  """; await q.ExecuteNonQueryAsync();
 }
 public async Task UpsertPostsAsync(IEnumerable<SavedPost> posts) {
  await using var c=new SqliteConnection(_cs); await c.OpenAsync();
  foreach(var p in posts) {
   var q=c.CreateCommand(); q.CommandText="""
   INSERT INTO Posts(Shortcode,Url,Status,UpdatedUtc) VALUES($s,$u,'Pending',$n)
   ON CONFLICT(Shortcode) DO UPDATE SET Url=excluded.Url,
   Status=CASE WHEN Posts.Status='Completed' THEN 'Completed' ELSE 'Pending' END, UpdatedUtc=excluded.UpdatedUtc;
   """;
   q.Parameters.AddWithValue("$s",p.Shortcode); q.Parameters.AddWithValue("$u",p.Url);
   q.Parameters.AddWithValue("$n",DateTime.UtcNow.ToString("O")); await q.ExecuteNonQueryAsync();
  }
 }
 public async Task<List<(string Code,string Url,int Attempts)>> GetWorkAsync() {
  var list=new List<(string,string,int)>(); await using var c=new SqliteConnection(_cs); await c.OpenAsync();
  var q=c.CreateCommand(); q.CommandText="SELECT Shortcode,Url,Attempts FROM Posts WHERE Status <> 'Completed' ORDER BY UpdatedUtc";
  await using var r=await q.ExecuteReaderAsync();
  while(await r.ReadAsync()) list.Add((r.GetString(0),r.GetString(1),r.GetInt32(2))); return list;
 }
 public async Task SetAsync(string code,string status,int? attempts=null,string? output=null,string? error=null) {
  await using var c=new SqliteConnection(_cs); await c.OpenAsync(); var q=c.CreateCommand();
  q.CommandText="UPDATE Posts SET Status=$st,Attempts=COALESCE($a,Attempts),OutputPath=COALESCE($o,OutputPath),LastError=$e,UpdatedUtc=$n WHERE Shortcode=$s";
  q.Parameters.AddWithValue("$st",status); q.Parameters.AddWithValue("$a",(object?)attempts??DBNull.Value);
  q.Parameters.AddWithValue("$o",(object?)output??DBNull.Value); q.Parameters.AddWithValue("$e",(object?)error??DBNull.Value);
  q.Parameters.AddWithValue("$n",DateTime.UtcNow.ToString("O")); q.Parameters.AddWithValue("$s",code); await q.ExecuteNonQueryAsync();
 }
}
