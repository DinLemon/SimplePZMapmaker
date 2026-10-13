using System.Text.Json;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;

/// <summary>Bounded streaming persistence with three previous successful saves.</summary>
public static class RecoveryArchive {
 public sealed record VersionInfo(int Version,long Bytes,DateTimeOffset Updated);
 static string VersionPath(string file,int version)=>version==0?file:file+".previous"+version;
 public static async Task Write<T>(string file,T value,int maxBytes,CancellationToken token=default) {
  Directory.CreateDirectory(Path.GetDirectoryName(file)!);var temporary=file+".tmp";
  try {
   await using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous))
   await using(var bounded=new LimitedStream(stream,maxBytes))await JsonSerializer.SerializeAsync(bounded,value,NativeMapExporter.JsonOptions,token);
   token.ThrowIfCancellationRequested();
   for(var version=3;version>=1;version--){var previous=VersionPath(file,version-1);if(File.Exists(previous))File.Copy(previous,VersionPath(file,version),true);}
   File.Move(temporary,file,true);
  } finally {if(File.Exists(temporary))File.Delete(temporary);}
 }
 public static async Task<T?> Read<T>(string file,int maxBytes,Action<T> validate,CancellationToken token=default,int? version=null) where T:class {
  if(version is <0 or >3)throw new ArgumentException("Версия восстановления должна быть от0 до3.");
  var found=false;foreach(var number in version is {} explicitVersion?new[]{explicitVersion}:new[]{0,1,2,3}){
   var path=VersionPath(file,number);if(!File.Exists(path))continue;found=true;
   try {
    if(new FileInfo(path).Length>maxBytes)throw new ArgumentException("Автосохранение превышает допустимый размер.");
    await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous);
    var result=await JsonSerializer.DeserializeAsync<T>(stream,NativeMapExporter.JsonOptions,token)??throw new ArgumentException("Автосохранение пусто.");validate(result);return result;
   } catch(Exception e) when(version is null&&e is JsonException or ArgumentException or IOException){}
  }
  if(found)throw new ArgumentException("Все доступные версии автосохранения повреждены. Откройте сохранённый файл проекта.");return null;
 }
 public static VersionInfo[] Versions(string file)=>Enumerable.Range(0,4).Select(v=>(Version:v,File:new FileInfo(VersionPath(file,v)))).Where(v=>v.File.Exists).Select(v=>new VersionInfo(v.Version,v.File.Length,v.File.LastWriteTimeUtc)).ToArray();
 public static void Clear(string file){foreach(var version in Enumerable.Range(0,4)){var path=VersionPath(file,version);if(File.Exists(path))File.Delete(path);}}
 sealed class LimitedStream(Stream inner,long maximum):Stream {
  long written;
  void Reserve(int count){if(count>maximum-written)throw new ArgumentException("Автосохранение превышает допустимый размер. Сохраните части мира отдельно.");written+=count;}
  public override void Write(byte[] buffer,int offset,int count){Reserve(count);inner.Write(buffer,offset,count);}
  public override void Write(ReadOnlySpan<byte> buffer){Reserve(buffer.Length);inner.Write(buffer);}
  public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,CancellationToken token=default){Reserve(buffer.Length);await inner.WriteAsync(buffer,token);}
  public override Task WriteAsync(byte[] buffer,int offset,int count,CancellationToken token){Reserve(count);return inner.WriteAsync(buffer,offset,count,token);}
  public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;public override long Length=>written;public override long Position{get=>written;set=>throw new NotSupportedException();}
  public override void Flush()=>inner.Flush();public override Task FlushAsync(CancellationToken token)=>inner.FlushAsync(token);
  public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
 }
}
