using System.Globalization;
using System.Text.RegularExpressions;
using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public sealed class StaticModelCatalog(GameAssetCatalog assets,GameplayCatalog? gameplay=null)
{
 public sealed record Model(string Id,string Name,string SourceId,string? ModId,string[] RequiredMods,string? ItemType,string Format,double ScriptScale,string? Texture,string? Error);
 private sealed record Entry(Model Model,string? MeshPath,string? TexturePath,string Fingerprint,string? TextureFingerprint,double[] WorldRotation,double WorldScale);
 private Dictionary<string,Entry> entries=[];private string sourcesKey="";private readonly object gate=new();
 public object Search(string? query=null,int offset=0,int limit=60,string? source=null)
 {Refresh();if(offset<0||offset>200000||limit is <1 or >120||(query?.Length??0)>120)throw new ArgumentException("Check the model catalog parameters.");var list=entries.Values.Select(e=>e.Model).Where(m=>string.IsNullOrEmpty(source)||(source=="game"?m.ModId is null:source=="mod"?m.ModId is not null:m.ModId==source)).Where(m=>string.IsNullOrEmpty(query)||m.Id.Contains(query,StringComparison.OrdinalIgnoreCase)||(m.ModId?.Contains(query,StringComparison.OrdinalIgnoreCase)??false)).OrderBy(m=>m.Id).ToArray();return new{total=list.Length,offset,limit,items=list.Skip(offset).Take(limit)};}
 public Model Get(string id){Refresh();return EntryFor(id).Model;}
 public object Geometry(string id)
 {Refresh();var entry=EntryFor(id);EnsureFile(entry);if(entry.Model.Error is not null)throw new ArgumentException(entry.Model.Error);if(entry.Model.Format=="fbx")return new{format="fbx",url="/api/models/mesh/"+Uri.EscapeDataString(id),scale=entry.Model.ScriptScale,worldRotation=entry.WorldRotation,worldScale=entry.WorldScale,texture=entry.TexturePath is not null?"/api/models/texture/"+Uri.EscapeDataString(id):null};var text=File.ReadAllText(entry.MeshPath!);return new{format=entry.Model.Format,meshes=entry.Model.Format=="x"?StaticModelGeometry.ReadX(text):StaticModelGeometry.ReadObj(text),scale=entry.Model.ScriptScale,worldRotation=entry.WorldRotation,worldScale=entry.WorldScale,texture=entry.TexturePath is not null?"/api/models/texture/"+Uri.EscapeDataString(id):null};}
 public byte[] Mesh(string id){Refresh();var e=EntryFor(id);EnsureFile(e);if(e.Model.Format!="fbx")throw new ArgumentException("Only FBX is available here.");return File.ReadAllBytes(e.MeshPath!);}
 public byte[] Texture(string id){Refresh();var e=EntryFor(id);EnsureFile(e);if(e.TexturePath is null)throw new ArgumentException("Model texture not found.");if(new FileInfo(e.TexturePath).Length>16*1024*1024)throw new ArgumentException("The texture is too large.");return File.ReadAllBytes(e.TexturePath);}
 public StaticModelPlacement Placement(string id){var m=Get(id);if(m.Error is not null)throw new ArgumentException(m.Error);var entry=EntryFor(id);return new(){ModelId=m.Id,ItemType=m.ItemType,SourceId=m.SourceId,RequiredMods=m.RequiredMods.ToList(),WorldRotation=entry.WorldRotation.ToArray(),WorldScale=entry.WorldScale};}
 public void Validate(StaticModelPlacement p){StaticModelPlacement.Validate(p,int.MaxValue,int.MaxValue);var m=Get(p.ModelId);if(m.Error is not null)throw new ArgumentException(m.Error);if(m.SourceId!=p.SourceId||!m.RequiredMods.Order(StringComparer.Ordinal).SequenceEqual(p.RequiredMods.Order(StringComparer.Ordinal)))throw new ArgumentException("The model source has changed. Select the model again.");var entry=EntryFor(p.ModelId);p.WorldRotation=entry.WorldRotation.ToArray();p.WorldScale=entry.WorldScale;(gameplay??new GameplayCatalog(assets)).Validate(p);}
 public void Validate(MapProject p){foreach(var m in p.StaticModels)Validate(m);foreach(var b in p.Buildings.Concat(p.BuildingLibrary))if(b.Design is {} d)foreach(var f in d.Floors)foreach(var m in f.StaticModels)Validate(m);(gameplay??new GameplayCatalog(assets)).Validate(p);}
 public void Refresh(bool force=false)
 {
  lock(gate){var sources=assets.ActiveSources().Where(s=>!string.IsNullOrWhiteSpace(s.Root)).ToArray();var key=string.Join('|',sources.Select(s=>s.Id+":"+s.Root));if(!force&&sourcesKey==key)return;var models=new Dictionary<string,(string Name,string SourceId,string? ModId,string[] Requirements,Dictionary<string,string> Props,Dictionary<string,string> World)>();var items=new Dictionary<string,string>();var roots=sources.Select(s=>s.Root).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();var scriptCount=0;
   long scriptBytes=0;foreach(var source in sources){var folder=Path.Combine(source.Root,"media","scripts");if(!Directory.Exists(folder))continue;foreach(var file in SafeFiles(folder,"*.txt")){
    var size=new FileInfo(file).Length;scriptBytes+=size;if(++scriptCount>10000||size>16*1024*1024||scriptBytes>128*1024*1024)throw new ArgumentException("Too many model declarations.");var text=File.ReadAllText(file);List<ScriptBlocks.Block> nodes;try{nodes=ScriptBlocks.Read(text);}catch(ArgumentException){continue;}
    foreach(var n in nodes.Where(n=>n.Kind is "model" or "item")){var module=n.Parent;while(module is not null&&module.Kind!="module")module=module.Parent;if(module is null||!SafeName(module.Name)||!SafeName(n.Name))continue;var id=module.Name+"."+n.Name;var props=ScriptBlocks.Properties(text,n,nodes);
     if(n.Kind=="model"&&props.TryGetValue("mesh",out _)){var attachment=nodes.FirstOrDefault(a=>a.Parent==n&&a.Kind=="attachment"&&a.Name=="world");var world=attachment is null?new Dictionary<string,string>():ScriptBlocks.Properties(text,attachment,nodes);models[id]=(n.Name,source.Id,source.RequiredModId,source.Requirements.Concat(source.RequiredModId is {} mod?[mod]:[]).Distinct().ToArray(),props,world);}
     else if(n.Kind=="item"&&props.TryGetValue("WorldStaticModel",out var model)){items[id]=model.Contains('.')?model:module.Name+"."+model;}
    }
   }}
   if(models.Count>30000)throw new ArgumentException("The model catalog exceeds 30,000 declarations.");
   var next=new Dictionary<string,Entry>();foreach(var (id,data) in models){var mesh=FindResource(roots,"models_X",data.Props["mesh"],[".fbx",".x",".obj"]);var textureName=data.Props.GetValueOrDefault("texture");if(textureName is null&&mesh is not null&&Path.GetExtension(mesh).Equals(".x",StringComparison.OrdinalIgnoreCase)&&new FileInfo(mesh).Length<=32*1024*1024){var declaration=Regex.Match(File.ReadAllText(mesh),@"TextureFilename\s*\{\s*""([^""]+)""\s*;?\s*\}",RegexOptions.None,TimeSpan.FromSeconds(2));if(declaration.Success)textureName=declaration.Groups[1].Value;}
    var texture=textureName is null?null:FindResource(roots,"textures",textureName,[".png"]);string? error=mesh is null?"Model geometry file not found.":textureName is not null&&texture is null?"The texture referenced by the model declaration was not found.":null;var format=mesh is null?"missing":Path.GetExtension(mesh).TrimStart('.').ToLowerInvariant();if(data.Props.GetValueOrDefault("static")=="false")error="Animated models are not supported by the static object editor.";if(mesh is not null&&new FileInfo(mesh).Length>32*1024*1024)error="Model geometry exceeds 32 MB.";var scale=1d;if(data.Props.TryGetValue("scale",out var s)&&(!double.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out scale)||!double.IsFinite(scale)||scale is <=0 or >100))error="Invalid model scale in the script.";
    var owners=sources.Where(source=>new[]{mesh,texture}.Any(path=>path is not null&&path.StartsWith(Path.GetFullPath(source.Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)));
    var requirements=data.Requirements.Concat(owners.SelectMany(s=>s.Requirements.Concat(s.RequiredModId is {} mod?[mod]:[]))).Distinct(StringComparer.Ordinal).ToArray();
    var worldRotation=new[]{0d,0d,0d};var worldScale=1d;if(data.World.TryGetValue("rotate",out var rotation)){var components=rotation.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);if(components.Length!=3||components.Select((x,i)=>double.TryParse(x,NumberStyles.Float,CultureInfo.InvariantCulture,out worldRotation[i])&&double.IsFinite(worldRotation[i])&&Math.Abs(worldRotation[i])<=36000).Any(valid=>!valid))error="Invalid model world attachment rotation.";}if(data.World.TryGetValue("scale",out var worldScaleText)&&(!double.TryParse(worldScaleText,NumberStyles.Float,CultureInfo.InvariantCulture,out worldScale)||!double.IsFinite(worldScale)||worldScale is <=0 or >100))error="Invalid model world attachment scale.";
    var model=new Model(id,data.Name,data.SourceId,data.ModId,requirements,items.FirstOrDefault(i=>i.Value==id).Key,format,scale,textureName,error);next[id]=new(model,mesh,texture,mesh is null?"":Fingerprint(mesh),texture is null?null:Fingerprint(texture),worldRotation,worldScale);
   }entries=next;sourcesKey=key;
  }
 }
 private Entry EntryFor(string id)=>entries.GetValueOrDefault(id)??throw new ArgumentException("The registered model is missing from the active resources.");
 private static void EnsureFile(Entry e){if(e.MeshPath is null||!File.Exists(e.MeshPath)||Fingerprint(e.MeshPath)!=e.Fingerprint||e.TexturePath is not null&&(!File.Exists(e.TexturePath)||Fingerprint(e.TexturePath)!=e.TextureFingerprint))throw new ArgumentException("Model resources have changed. Refresh the catalog.");if(new FileInfo(e.MeshPath).Length>32*1024*1024)throw new ArgumentException("Model geometry exceeds 32 MB.");RejectReparse(e.MeshPath);if(e.TexturePath is not null)RejectReparse(e.TexturePath);}
 private static string Fingerprint(string path){var f=new FileInfo(path);return f.Length+":"+f.LastWriteTimeUtc.Ticks;}
 private static bool SafeName(string value)=>Regex.IsMatch(value,@"^[A-Za-z][A-Za-z0-9_]*$",RegexOptions.None,TimeSpan.FromSeconds(1));
 private static string? FindResource(string[] roots,string folder,string relative,string[] extensions)
 {
  if(relative.Length>240||relative.Contains('\\')||relative.Split('/').Any(p=>p is "" or "." or "..")||relative.Any(c=>char.IsControl(c)||c is ':' or '"'))return null;
  foreach(var root in roots.Reverse()){var dir=Path.GetFullPath(Path.Combine(root,"media",folder));foreach(var ext in extensions){var path=Path.GetFullPath(Path.Combine(dir,Path.HasExtension(relative)?relative:relative+ext));if(!path.StartsWith(dir+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;if(File.Exists(path)){RejectReparse(path);return path;}}}return null;
 }
 private static IEnumerable<string> SafeFiles(string dir,string pattern){RejectReparse(dir);var stack=new Stack<string>();stack.Push(dir);while(stack.Count>0){var current=stack.Pop();foreach(var file in Directory.EnumerateFiles(current,pattern)){RejectReparse(file);yield return file;}foreach(var child in Directory.EnumerateDirectories(current)){RejectReparse(child);stack.Push(child);}}}
 private static void RejectReparse(string path){for(var p=Path.GetFullPath(path);!string.IsNullOrEmpty(p);p=Path.GetDirectoryName(p)??"")if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Model resource links and redirects are not supported.");}
}
public static class StaticModelEndpoints
{
 public static void MapStaticModelEndpoints(this WebApplication app)
 {
  app.MapGet("/api/models/search",(StaticModelCatalog models,string? q,int? offset,int? limit,string? source)=>models.Search(q,offset??0,limit??60,source));
  app.MapGet("/api/models/item/{id}",(StaticModelCatalog models,string id)=>models.Get(id));
  app.MapGet("/api/models/geometry/{id}",(StaticModelCatalog models,string id)=>models.Geometry(id));
  app.MapGet("/api/models/mesh/{id}",(StaticModelCatalog models,string id)=>Results.Bytes(models.Mesh(id),"application/octet-stream"));
  app.MapGet("/api/models/texture/{id}",(StaticModelCatalog models,string id)=>Results.Bytes(models.Texture(id),"image/png"));
  app.MapPost("/api/models/placement/{id}",(StaticModelCatalog models,string id)=>models.Placement(id));
  app.MapPost("/api/models/refresh",(StaticModelCatalog models)=>{models.Refresh(true);return models.Search();});
 }
}
