using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;

/// <summary>Validated, immutable per-section cell fragments. No game files or complete exports are cached.</summary>
public sealed class NativeSectionCache:INativeSectionCache
{
 readonly string directory;readonly SemaphoreSlim gate=new(1,1);readonly long maximum;
 public NativeSectionCache(string directory,long maximumBytes=512L*1024*1024){this.directory=directory;maximum=maximumBytes;Directory.CreateDirectory(directory);foreach(var p in Directory.EnumerateFiles(directory,"*.partial"))if(Owned(p))File.Delete(p);Prune();}
 static bool Owned(string path){var stem=Path.GetFileName(path).Split('.')[0];return stem.Length==64&&stem.All(Uri.IsHexDigit);}
 public static string Key(MapProject p){using var hash=SHA256.Create();using var sink=new CryptoStream(Stream.Null,hash,CryptoStreamMode.Write);sink.Write(Encoding.UTF8.GetBytes("B42-256-8:"+typeof(NativeMapExporter).Module.ModuleVersionId));JsonSerializer.Serialize(sink,p,NativeMapExporter.JsonOptions);JsonSerializer.Serialize(sink,p.SharedInfrastructure,NativeMapExporter.JsonOptions);sink.FlushFinalBlock();return Convert.ToHexString(hash.Hash!).ToLowerInvariant();}
 public bool Write(MapProject p,ZipArchive destination,string root,CancellationToken token,Action<int>? cellCompleted,Action<ZipArchive> compose){gate.Wait(token);try{
  token.ThrowIfCancellationRequested();var key=Key(p);var target=Path.Combine(directory,key+".zip");var digest=Path.Combine(directory,key+".sha256");var temporary=Path.Combine(directory,key+".partial");var hit=Valid(target,digest);
  if(!hit){try{using(var output=new FileStream(temporary,FileMode.Create,FileAccess.ReadWrite,FileShare.None)){using var bounded=new BoundedArchiveStream(output,256L*1024*1024);using var fragment=new ZipArchive(bounded,ZipArchiveMode.Create,true);compose(fragment);}token.ThrowIfCancellationRequested();File.Move(temporary,target,true);File.WriteAllText(digest,Hash(target));}finally{if(File.Exists(temporary))File.Delete(temporary);}}
  // Holding the cache gate keeps eviction from removing an open fragment on Windows.
  using(var source=ZipFile.OpenRead(target)){if(source.Entries.Count!=p.Width/256*(p.Height/256)*4)throw new ArgumentException("Cached native section is incomplete. Retry the export.");var cells=0;foreach(var entry in source.Entries){token.ThrowIfCancellationRequested();if(!(entry.FullName==entry.Name||entry.FullName=="maps/"+entry.Name&&entry.Name.StartsWith("biomemap_",StringComparison.Ordinal)&&entry.Name.EndsWith(".png",StringComparison.Ordinal))||!(entry.Name.StartsWith("biomemap_",StringComparison.Ordinal)||entry.Name.EndsWith(".lotheader",StringComparison.Ordinal)||entry.Name.EndsWith(".lotpack",StringComparison.Ordinal)||entry.Name.StartsWith("chunkdata_",StringComparison.Ordinal)&&entry.Name.EndsWith(".bin",StringComparison.Ordinal)))throw new ArgumentException("Cached native section is invalid. Retry the export.");using var input=entry.Open();using var output=destination.CreateEntry(root+entry.FullName,CompressionLevel.Fastest).Open();var buffer=new byte[65536];int count;while((count=input.Read(buffer))>0){token.ThrowIfCancellationRequested();output.Write(buffer,0,count);}if(hit&&++cells%4==0)cellCompleted?.Invoke(cells/4);}}
  File.SetLastWriteTimeUtc(target,DateTime.UtcNow);Prune();return hit;
 }finally{gate.Release();}}
 static string Hash(string path){using var s=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(s));}
 static bool Valid(string file,string digest){try{return File.Exists(file)&&File.Exists(digest)&&new FileInfo(file).Length<=256L*1024*1024&&File.ReadAllText(digest)==Hash(file);}catch(IOException){return false;}}
 void Prune(){var files=Directory.EnumerateFiles(directory,"*.zip").Where(Owned).Select(p=>new FileInfo(p)).OrderByDescending(f=>f.LastWriteTimeUtc).ToArray();long size=0;for(var i=0;i<files.Length;i++){size+=files[i].Length;if(i<128&&size<=maximum)continue;File.Delete(files[i].FullName);var digest=Path.ChangeExtension(files[i].FullName,".sha256");if(File.Exists(digest))File.Delete(digest);}}
}
