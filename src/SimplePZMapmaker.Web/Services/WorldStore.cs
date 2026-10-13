using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;

public sealed class WorldStore(string directory):IDisposable
{
 public const int MaxBytes=128*1024*1024;
 private readonly SemaphoreSlim gate=new(1,1);
 private string FileName(string id){if(!WorldAssembler.SafeId(id))throw new ArgumentException("Некорректный идентификатор автосохранения мира.");return Path.Combine(directory,id+".pzworld.json");}
 public async Task SaveAsync(string id,WorldProject world,CancellationToken token=default){
  var file=FileName(id);WorldAssembler.Validate(world);var json=JsonSerializer.Serialize(world,NativeMapExporter.JsonOptions);
  if(Encoding.UTF8.GetByteCount(json)>MaxBytes)throw new ArgumentException("Проект мира превышает128МБ. Разделите его на несколько миров.");
  await gate.WaitAsync(token);var temporary=file+".tmp";
  try{Directory.CreateDirectory(directory);await File.WriteAllTextAsync(temporary,json,token);File.Move(temporary,file,true);}
  finally{try{if(File.Exists(temporary))File.Delete(temporary);}finally{gate.Release();}}
 }
 public async Task<WorldProject?> LoadAsync(string id,CancellationToken token=default){
  var file=FileName(id);await gate.WaitAsync(token);
  try{if(!File.Exists(file))return null;if(new FileInfo(file).Length>MaxBytes)throw new ArgumentException("Автосохранение мира превышает128МБ.");
   var world=JsonSerializer.Deserialize<WorldProject>(await File.ReadAllTextAsync(file,token),NativeMapExporter.JsonOptions)??throw new ArgumentException("Автосохранение мира повреждено.");WorldAssembler.Validate(world);return world;
  }finally{gate.Release();}
 }
 public async Task ClearAsync(string id,CancellationToken token=default){var file=FileName(id);await gate.WaitAsync(token);try{if(File.Exists(file))File.Delete(file);}finally{gate.Release();}}
 public void Dispose()=>gate.Dispose();
}
