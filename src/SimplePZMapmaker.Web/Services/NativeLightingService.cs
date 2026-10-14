using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

namespace SimplePZMapmaker.Web.Services;

public sealed class NativeLightingRequest
{
 public MapProject? Project {get;set;}
 public WorldProject? World {get;set;}
 public string? MemberId {get;set;}
 public int X {get;set;} public int Y {get;set;} public int Size {get;set;}=32; public int Level {get;set;}
 public double[] SkyRgb {get;set;}=[.4,.4,.4]; public double Ambient {get;set;}=.4;
 public bool RoomLightsPowered {get;set;}=true;
 public string Revision {get;set;}="";
}

/// <summary>Runs only our isolated adapter against the user's installed lighting library. Never starts the game.</summary>
public sealed class NativeLightingService(string workspace,GameAssetCatalog assets,StaticModelCatalog models)
{
 readonly SemaphoreSlim gate=new(1,1);
 readonly string workRoot=Path.GetFullPath(Path.Combine(workspace,"work","native-lighting"));
 readonly Dictionary<string,(long Length,long Modified,string Hash)> hashes=new(StringComparer.OrdinalIgnoreCase);
 string Installed(string name)=>Path.Combine(assets.GamePath,name);
 string Helper=>Path.Combine(AppContext.BaseDirectory,"native-lighting","LightingProbe.class");
 public object Status()=>new{available=assets.Ready&&File.Exists(Installed("Lighting64.dll"))&&File.Exists(Installed("projectzomboid.jar"))&&File.Exists(Installed("jre64/bin/java.exe"))&&File.Exists(Helper),engine="Installed Project Zomboid LightingJNI",protocol=2,maximumSize=64,halo=24,gameSession=false};
 string Fingerprint(string path){lock(hashes){var f=new FileInfo(path);if(hashes.TryGetValue(path,out var old)&&old.Length==f.Length&&old.Modified==f.LastWriteTimeUtc.Ticks)return old.Hash;using var stream=f.OpenRead();var hash=Convert.ToHexStringLower(SHA256.HashData(stream));hashes[path]=(f.Length,f.LastWriteTimeUtc.Ticks,hash);return hash;}}
 void ValidateInstallation(){
  if(!assets.Ready||!File.Exists(Installed("Lighting64.dll"))||!File.Exists(Installed("projectzomboid.jar"))||!File.Exists(Installed("jre64/bin/java.exe")))throw new ArgumentException("Native lighting needs a Windows Build 42 installation with Lighting64.dll and its bundled Java runtime. Select it under Game resources.");
  if(!File.Exists(Helper))throw new ArgumentException("The original lighting adapter is missing from this application package. Rebuild or reinstall the editor.");
  var manifestPath=Path.Combine(Path.GetDirectoryName(Helper)!,"manifest.json");
  if(!File.Exists(manifestPath))throw new ArgumentException("The lighting adapter integrity manifest is missing. Rebuild or reinstall the editor.");
  using var manifest=JsonDocument.Parse(File.ReadAllText(manifestPath));
  if(manifest.RootElement.GetProperty("protocol").GetInt32()!=2||manifest.RootElement.GetProperty("classSha256").GetString()!=Fingerprint(Helper))throw new ArgumentException("The lighting adapter failed its integrity check. Rebuild or reinstall the editor.");
 }
 public async Task<object> Check(NativeLightingRequest request,CancellationToken cancellation){
  if((request.Project is null)==(request.World is null)||request.SkyRgb is null||request.SkyRgb.Length!=3||request.SkyRgb.Any(v=>!double.IsFinite(v)||v is <0 or >1)||!double.IsFinite(request.Ambient)||request.Ambient is <0 or >1||request.Revision is null||request.Revision.Length>100||request.Size is <8 or >64||request.Level<ZLevels.Min||request.Level>ZLevels.Max)throw new ArgumentException("Select one map or world section, a signed floor, 8–64 tiles, and sky/ambient values between 0 and 1.");
  ValidateInstallation();
  if(!await gate.WaitAsync(0,cancellation))throw new ArgumentException("A native lighting check is already running. Cancel it or wait for its result.");
  var job=Path.Combine(workRoot,Guid.NewGuid().ToString("N"));
  try{
   Directory.CreateDirectory(job);Directory.CreateDirectory(Path.Combine(job,"home"));Directory.CreateDirectory(Path.Combine(job,"tmp"));
   NativeLightingGeometry.Scene geometry;
   if(request.Project is {} project){AssetRules.Project(project,assets);models.Validate(project);geometry=NativeLightingGeometry.FromProject(project,request.X,request.Y,request.Size,request.Level,request.RoomLightsPowered,id=>assets.Get(id).Properties,cancellation);}
   else{var world=request.World!;WorldEndpoints.Validate(world,assets,models);geometry=NativeLightingGeometry.FromWorld(world,request.MemberId??"",request.X,request.Y,request.Size,request.Level,request.RoomLightsPowered,id=>assets.Get(id).Properties,cancellation);}
   var input=Path.Combine(job,"scene.bin");var output=Path.Combine(job,"lighting.json");WriteInput(input,geometry,request);
   var inputHash=Fingerprint(input);var nativeHash=Fingerprint(Installed("Lighting64.dll"));var jarHash=Fingerprint(Installed("projectzomboid.jar"));
   var info=new ProcessStartInfo(Installed("jre64/bin/java.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=job,RedirectStandardError=true,RedirectStandardOutput=true};
   foreach(var argument in new[]{"-Djava.awt.headless=true","-Duser.home="+Path.Combine(job,"home"),"-Djava.io.tmpdir="+Path.Combine(job,"tmp"),"-XX:ErrorFile="+Path.Combine(job,"hs_err_%p.log"),"-XX:-CreateCoredumpOnCrash","--enable-native-access=ALL-UNNAMED","-Xmx256m","-cp",Path.GetDirectoryName(Helper)+Path.PathSeparator+Installed("projectzomboid.jar"),"LightingProbe",Installed("Lighting64.dll"),input,output})info.ArgumentList.Add(argument);
   // Prevent inherited JVM options from redirecting files or loading unrelated agents.
   foreach(var name in new[]{"JAVA_TOOL_OPTIONS","JDK_JAVA_OPTIONS","_JAVA_OPTIONS","CLASSPATH"})info.Environment.Remove(name);
   info.Environment["TEMP"]=Path.Combine(job,"tmp");info.Environment["TMP"]=Path.Combine(job,"tmp");
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(30));
   using var process=Process.Start(info)??throw new ArgumentException("The installed Java runtime could not start the lighting adapter.");
   var stdout=Drain(process.StandardOutput,timeout.Token);var stderr=Drain(process.StandardError,timeout.Token);var timer=Stopwatch.StartNew();
   try{await process.WaitForExitAsync(timeout.Token);await Task.WhenAll(stdout,stderr);}catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);if(cancellation.IsCancellationRequested)throw;throw new ArgumentException("The native lighting check exceeded 30 seconds. Choose a smaller area or fewer floors.");}
   if(process.ExitCode!=0||!File.Exists(output))throw new ArgumentException("The installed lighting library could not calculate this scene. Its ABI may differ from supported Build 42; no approximate result was substituted.");
   if(new FileInfo(output).Length>2_000_000)throw new ArgumentException("Native lighting returned an oversized result.");
   using var parsed=JsonDocument.Parse(await File.ReadAllTextAsync(output,cancellation));var root=parsed.RootElement;
   if(root.GetProperty("protocol").GetInt32()!=2||root.GetProperty("counter").GetInt32()<1)throw new ArgumentException("The native lighting library did not complete a frame.");
   var cells=new List<double[]>();var seen=new HashSet<(int,int)>();var expected=geometry.Squares.Where(s=>s.Z==request.Level&&s.X>=geometry.OutputX&&s.X<geometry.OutputX+request.Size&&s.Y>=geometry.OutputY&&s.Y<geometry.OutputY+request.Size).Select(s=>(s.X-geometry.OutputX,s.Y-geometry.OutputY)).ToHashSet();
   foreach(var item in root.GetProperty("cells").EnumerateArray()){
    if(item.GetArrayLength()!=15||cells.Count>=request.Size*request.Size)throw new ArgumentException("The native lighting cell schema is invalid.");
    var cell=item.EnumerateArray().Select(v=>v.GetDouble()).ToArray();var x=(int)cell[0];var y=(int)cell[1];
    if(cell.Any(v=>!double.IsFinite(v))||cell[0]!=x||cell[1]!=y||cell[2]!=request.Level||!expected.Contains((x,y))||!seen.Add((x,y))||cell.Skip(3).Take(3).Any(v=>v is <0 or >1)||cell[6] is <0 or >4||cell.Skip(7).Any(v=>v<int.MinValue||v>int.MaxValue||v!=Math.Truncate(v)))throw new ArgumentException("The native lighting cell values are invalid.");
    cells.Add(cell);
   }
   if(!seen.SetEquals(expected))throw new ArgumentException("Native lighting did not return all exported squares in the selected area.");
   return new{revision=request.Revision,x=request.X,y=request.Y,size=request.Size,level=request.Level,cells,scenario=new{request.SkyRgb,request.Ambient,request.RoomLightsPowered},provenance=new{engine="Project Zomboid LightingJNI",library="Lighting64.dll",nativeSha256=nativeHash,gameJarSha256=jarHash,adapterSha256=Fingerprint(Helper),inputSha256=inputHash,protocol=2,counter=root.GetProperty("counter").GetInt32(),milliseconds=timer.ElapsedMilliseconds,gameSession=false},warnings=geometry.Warnings};
  }
  finally{lock(hashes){foreach(var key in hashes.Keys.Where(k=>k.StartsWith(job+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).ToArray())hashes.Remove(key);}try{if(Directory.Exists(job))Directory.Delete(job,true);}catch(IOException){}catch(UnauthorizedAccessException){}gate.Release();}
 }
 static async Task Drain(StreamReader reader,CancellationToken token){var buffer=new char[2048];while(await reader.ReadAsync(buffer.AsMemory(),token)>0){} }
 public static void WriteInput(string path,NativeLightingGeometry.Scene scene,NativeLightingRequest request){
  using var stream=File.Create(path);using var w=new BinaryWriter(stream);w.Write(0x324c5a50);
  foreach(var n in new[]{scene.ChunksX,scene.ChunksY,scene.WorldTileX/8,scene.WorldTileY/8,scene.MinZ,scene.MaxZ,scene.Level,scene.OutputX,scene.OutputY,scene.Size})w.Write(n);
  foreach(var value in request.SkyRgb)w.Write((float)value);w.Write((float)request.Ambient);w.Write(scene.Squares.Count);
  foreach(var s in scene.Squares){w.Write(s.X);w.Write(s.Y);w.Write(s.Z);w.Write(s.Flags);w.Write(s.BuildingId);w.Write(s.RoomId);}
  w.Write(scene.PointLights.Count);foreach(var l in scene.PointLights){w.Write(l.X);w.Write(l.Y);w.Write(l.Z);w.Write(l.Radius);w.Write((float)l.R);w.Write((float)l.G);w.Write((float)l.B);w.Write(l.Active);}
  w.Write(scene.RoomLights.Count);foreach(var l in scene.RoomLights){w.Write(l.BuildingId);w.Write(l.RoomId);w.Write(l.X);w.Write(l.Y);w.Write(l.Z);w.Write(l.Width);w.Write(l.Height);w.Write(l.Active);}
 }
}

public static class NativeLightingEndpoints
{
 public static void MapNativeLightingEndpoints(this WebApplication app){app.MapGet("/api/lighting/status",(NativeLightingService service)=>service.Status());app.MapPost("/api/lighting/check",(NativeLightingRequest request,NativeLightingService service,CancellationToken cancellation)=>service.Check(request,cancellation));}
}
