using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SimplePZMapmaker.Web.Services;
public sealed class RecoveryStore(string directory):IDisposable
{
    private readonly SemaphoreSlim gate=new(1,1);
    private string FileName(string id){if(id is null||id.Length!=32||!Regex.IsMatch(id,"^[a-f0-9]{32}$",RegexOptions.CultureInvariant))throw new ArgumentException("Некорректный идентификатор автосохранения.");return Path.Combine(directory,id+".pzmap.json");}
    public async Task SaveAsync(string id,MapProject project,CancellationToken token=default){
        var file=FileName(id);ProjectValidator.Validate(project);var json=JsonSerializer.Serialize(project,NativeMapExporter.JsonOptions);
        if(System.Text.Encoding.UTF8.GetByteCount(json)>16_000_000)throw new ArgumentException("Проект превышает лимит автосохранения 16 МБ.");
        await gate.WaitAsync(token);var temporary=file+".tmp";
        try{Directory.CreateDirectory(directory);await File.WriteAllTextAsync(temporary,json,token);File.Move(temporary,file,true);}
        finally{try{if(File.Exists(temporary))File.Delete(temporary);}finally{gate.Release();}}
    }
    public async Task<MapProject?> LoadAsync(string id,CancellationToken token=default){
        var file=FileName(id);await gate.WaitAsync(token);
        try{if(!File.Exists(file))return null;if(new FileInfo(file).Length>16_000_000)throw new ArgumentException("Автосохранение слишком большое.");
            var project=JsonSerializer.Deserialize<MapProject>(await File.ReadAllTextAsync(file,token),NativeMapExporter.JsonOptions)??throw new ArgumentException("Автосохранение повреждено.");ProjectValidator.Validate(project);return project;
        }finally{gate.Release();}
    }
    public void Dispose()=>gate.Dispose();
}
