using System.Diagnostics;
using System.Text.Json;
using SimplePZMapmaker.Cli;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;

const string Usage="""
simplepzmap fetch --bounds <south,west,north,east> --out osm.json
simplepzmap build <scene.json> [--out world.pzworld.json] [--export mod.zip] [--report report.json] [--strict] [--game <path>] [mod options]
simplepzmap validate <map.pzmap.json|world.pzworld.json> [--game <path>] [--source data.geojson] [--strict]
simplepzmap export <map.pzmap.json|world.pzworld.json> --out mod.zip [--game <path>] [mod options]
simplepzmap probe <mod.zip> --game <path> [--expect-stairs]
simplepzmap server-check <mod.zip> --game <path> [--timeout-minutes 10]
simplepzmap tiles <tileset.json> --out tiles.zip [--game <path>] [--install] [--mods-dir <dir>]

mod options: --mod-id <id> --map-folder <name> --mod-name "<title>" --common-maps --install [--mods-dir <dir>]
--game checks every tile name against the installed Build 42 tile definitions and validates resources.
--strict fails the command when the seam check finds lost or duplicated source features.
--save-workspace (build) also saves the world as an editor section workspace (open it under World > Section workspaces).
""";
try{return Run(args);}
catch(ArgumentException e){Console.Error.WriteLine("error: "+e.Message);return 1;}
catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or HttpRequestException or TimeoutException){Console.Error.WriteLine("error: "+e.Message);return 1;}

static int Run(string[] args){
 if(args.Length>0&&args[0]=="fetch")return Fetch(Options(args[1..]));
 if(args.Length<2||args[0] is "-h" or "--help"){Console.WriteLine(Usage);return args.Length==0||args[0] is "-h" or "--help"?0:2;}
 var command=args[0];var input=Path.GetFullPath(args[1]);var options=Options(args[2..]);
 string? Option(string name)=>options.GetValueOrDefault(name);bool Flag(string name)=>options.ContainsKey(name);
 var workspace=Environment.GetEnvironmentVariable("PZMAP_WORKSPACE")??Directory.GetCurrentDirectory();
 GameAssetCatalog? assets=null;if(Option("--game") is {} game){assets=new GameAssetCatalog(workspace,Path.GetFullPath(game));if(!assets.Ready)throw new ArgumentException("No Build 42 tile definitions found: "+(assets.Error??game));Console.WriteLine($"game: {assets.GamePath} ({assets.Count} tiles)");}
 var catalog=assets??new GameAssetCatalog(workspace,"");var models=new StaticModelCatalog(catalog);
 NativeModOptions? Mod(NativeModOptions? scene){var o=new NativeModOptions(Option("--mod-id")??scene?.ModId,Option("--map-folder")??scene?.MapFolder,Option("--mod-name")??scene?.Name,Flag("--common-maps")||scene?.CommonMaps==true,Flag("--install")||scene?.Install==true);NativeModPackage.Validate(o);return o.Renames||o.Install?o:null;}
 switch(command){
  case "build":{
   var scene=SceneBuilder.Read(input);var build=SceneBuilder.Build(scene,Path.GetDirectoryName(input)!,w=>WorldEndpoints.Validate(w,catalog,models),Console.WriteLine);
   foreach(var w in build.Warnings)Console.WriteLine("note: "+w);
   var world=build.World;Console.WriteLine($"world {world.Name}: {world.Maps.Count} sections, {world.Maps.Sum(m=>m.Project.Buildings.Count)} section buildings, {world.Buildings.Count} shared buildings, {world.Fences.Count} shared fences");
   var failed=Report(build.Report,Option("--report"));if(assets is not null)failed|=CheckTiles(world,assets);
   var output=Path.GetFullPath(Option("--out")??Path.ChangeExtension(input,null)+".pzworld.json");using(var file=File.Create(output))JsonSerializer.Serialize(file,world,NativeMapExporter.JsonOptions);Console.WriteLine("wrote "+output);
   if(Option("--export") is {} target)Export(s=>NativeWorldExporter.ExportToStream(world,s),Path.GetFullPath(target),Mod(scene.Export),Option("--mods-dir"));
   if(Flag("--save-workspace")){using var store=new WorldStore(Path.Combine(workspace,"work","world-recovery"));store.SaveAsync(world.Id,world).GetAwaiter().GetResult();Console.WriteLine($"saved section workspace {world.Id} under {Path.Combine(workspace,"work","world-recovery")}; open it in the editor under World > Section workspaces");}
   return failed&&Flag("--strict")?1:0;}
  case "validate":{
   var (map,world)=Load(input);var failed=false;
   if(world is not null){WorldEndpoints.Validate(world,catalog,models);Console.WriteLine($"world {world.Name}: valid, {world.Maps.Count} sections");if(Option("--source") is {} source)failed|=Report(GeographicImporter.TransferCheck(File.ReadAllText(Path.GetFullPath(source)),world),Option("--report"));if(assets is not null)failed|=CheckTiles(world,assets);}
   else{AssetRules.Project(map!,catalog);models.Validate(map!);Console.WriteLine($"map {map!.Name}: valid, format {map.Version}");if(assets is not null)failed|=CheckTiles(map,assets);}
   return failed?1:0;}
  case "export":{
   var target=Option("--out")??throw new ArgumentException("export needs --out <mod.zip>.");var (map,world)=Load(input);
   if(world is not null){WorldEndpoints.Validate(world,catalog,models);Export(s=>NativeWorldExporter.ExportToStream(world,s),Path.GetFullPath(target),Mod(null),Option("--mods-dir"));}
   else{AssetRules.Project(map!,catalog);models.Validate(map!);Export(s=>NativeMapExporter.ExportToStream(map!,s),Path.GetFullPath(target),Mod(null),Option("--mods-dir"));}
   return 0;}
  case "tiles":{
   var target=Path.GetFullPath(Option("--out")??throw new ArgumentException("tiles needs --out <tiles.zip>."));var mod=TilesetSpec.Read(input,assets);
   using(var file=File.Create(target))TilesetBuilder.Write(mod,file);Console.WriteLine($"wrote {target}: {mod.Sprites.Count} tiles {mod.Tileset}_0..{mod.Sprites.Count-1}, tiledef {mod.FileNumber}");
   if(Flag("--install")){using var archive=File.OpenRead(target);Console.WriteLine("installed "+NativeModPackage.Install(archive,Option("--mods-dir")??NativeModPackage.DefaultModsDirectory()));}
   return 0;}
  case "server-check":{
   var minutes=int.TryParse(Option("--timeout-minutes"),out var m)?m:10;var result=ServerCheck.Run(input,Option("--game")??throw new ArgumentException("server-check needs --game <Build 42 installation>."),TimeSpan.FromMinutes(minutes),Console.WriteLine);
   foreach(var (point,building,room) in result.Probes)Console.WriteLine($"server metagrid {point.X},{point.Y} {point.Name}: building={building.ToString().ToLowerInvariant()} room={room??"none"}{(building==point.ExpectBuilding?"":" UNEXPECTED")}");
   Console.WriteLine($"server started: {result.Started}; stopped cleanly: {result.Stopped}; {(result.Passed?"server check: passed":"server check: FAILED")}");
   if(!result.Passed)File.WriteAllText(Path.ChangeExtension(input,null)+".server.log",result.Log);
   return result.Passed?0:1;}
  case "probe":return Probe(input,Option("--game")??throw new ArgumentException("probe needs --game <Build 42 installation>."),Flag("--expect-stairs"));
  default:Console.Error.WriteLine(Usage);return 2;
 }
}
/// <summary>Downloads OpenStreetMap data with the editor's own client: tiled Overpass requests, retries, provider fallback and a seven-day cache.</summary>
static int Fetch(Dictionary<string,string> options){
 var parts=(options.GetValueOrDefault("--bounds")??throw new ArgumentException("fetch needs --bounds south,west,north,east.")).Split(',').Select(v=>double.Parse(v,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
 if(parts.Length!=4)throw new ArgumentException("--bounds takes four numbers: south,west,north,east.");var target=Path.GetFullPath(options.GetValueOrDefault("--out")??throw new ArgumentException("fetch needs --out osm.json."));
 var workspace=Environment.GetEnvironmentVariable("PZMAP_WORKSPACE")??Directory.GetCurrentDirectory();using var client=new OsmClient(Path.Combine(workspace,"work","geodata-cache"));
 var download=client.FetchTiledDetailed(new GeoBounds{South=parts[0],West=parts[1],North=parts[2],East=parts[3]},CancellationToken.None,null,64_000_000).GetAwaiter().GetResult();
 File.WriteAllText(target,download.Data);foreach(var w in download.Warnings)Console.WriteLine("note: "+w);
 Console.WriteLine($"wrote {target}: {download.Source}, data date {download.DataTimestamp??"not specified"}, {download.Requests} requests{(download.FromCache?" (cache)":"")}. Map data (c) OpenStreetMap contributors, ODbL 1.0.");
 return 0;
}
static Dictionary<string,string> Options(string[] args){
 var flags=new HashSet<string>{"--common-maps","--install","--strict","--expect-stairs","--save-workspace"};var result=new Dictionary<string,string>(StringComparer.Ordinal);
 for(var i=0;i<args.Length;i++){if(!args[i].StartsWith("--",StringComparison.Ordinal))throw new ArgumentException("Unexpected argument: "+args[i]);if(flags.Contains(args[i])){result[args[i]]="true";continue;}if(i+1>=args.Length)throw new ArgumentException(args[i]+" needs a value.");result[args[i]]=args[++i];}
 return result;
}
static (MapProject? Map,WorldProject? World) Load(string path){
 using var document=JsonDocument.Parse(File.ReadAllText(path),new JsonDocumentOptions{MaxDepth=64});
 var format=document.RootElement.TryGetProperty("format",out var f)?f.GetString():null;
 if(format=="SimplePZWorld")return(null,document.RootElement.Deserialize<WorldProject>(NativeMapExporter.JsonOptions));
 var map=document.RootElement.Deserialize<MapProject>(NativeMapExporter.JsonOptions)??throw new ArgumentException("The project file is empty.");ProjectValidator.Validate(map);return(map,null);
}
static bool Report(TransferReport report,string? file){
 foreach(var (kind,o) in report.ByKind.OrderBy(k=>k.Key))Console.WriteLine($"seam check {kind}: placed-section {o.PlacedSection}, placed-world {o.PlacedWorld}, evidence {o.Evidence}, lost {o.Lost}, duplicated {o.Duplicated}");
 foreach(var (reason,count) in report.EvidenceReasons.OrderByDescending(r=>r.Value))Console.WriteLine($"  evidence {count}: {reason}");
 foreach(var problem in report.Problems)Console.WriteLine("  "+problem);
 Console.WriteLine(report.Passed?"seam check: passed":"seam check: FAILED");
 if(file is not null)File.WriteAllText(Path.GetFullPath(file),JsonSerializer.Serialize(new{passed=report.Passed,byKind=report.ByKind,evidenceReasons=report.EvidenceReasons,problems=report.Problems},new JsonSerializerOptions(NativeMapExporter.JsonOptions){WriteIndented=true}));
 return !report.Passed;
}
/// <summary>Every tile the native exporter would write must exist in the installed game's tile definitions (or an active mod).</summary>
static bool CheckTiles(object project,GameAssetCatalog assets){
 var sections=project is WorldProject world?world.Maps.Select(m=>WorldAssembler.ProjectSection(world,m)):[(MapProject)project];var missing=new SortedSet<string>(StringComparer.Ordinal);var total=0;
 foreach(var p in sections)foreach(var id in NativeScene.ResourceIds(p).Concat(p.Fences.Count>0?FenceLines.Compose(p.Fences,p.FenceStyles,p.GateStyles,p.Width,p.Height).Pieces.Select(x=>x.Tile):[])){total++;try{assets.Get(id);}catch(ArgumentException){missing.Add(id);}}
 Console.WriteLine(missing.Count==0?$"tiles: all {total} references exist in the game":$"tiles: {missing.Count} unknown tile names: {string.Join(", ",missing.Take(40))}");
 return missing.Count>0;
}
static void Export(Action<Stream> write,string target,NativeModOptions? package,string? modsDirectory){
 var temporary=target+".tmp";try{
  using(var file=File.Create(temporary))write(file);
  if(package?.Renames==true){using(var source=File.OpenRead(temporary))using(var file=File.Create(target))NativeModPackage.Repack(source,file,package);}else File.Copy(temporary,target,true);
 }finally{if(File.Exists(temporary))File.Delete(temporary);}
 Console.WriteLine("wrote "+target);
 if(package?.Install==true){using var archive=File.OpenRead(target);Console.WriteLine("installed "+NativeModPackage.Install(archive,modsDirectory??NativeModPackage.DefaultModsDirectory()));}
}
/// <summary>Runs scripts/verify-game-runtime.ps1: the installed game's own classes load the lot headers, chunks, rooms and stairs.</summary>
static int Probe(string archive,string game,bool expectStairs){
 var script=new[]{AppContext.BaseDirectory,Directory.GetCurrentDirectory()}.Select(d=>Path.Combine(d,"scripts","verify-game-runtime.ps1")).Concat(Ancestors(AppContext.BaseDirectory).Select(d=>Path.Combine(d,"scripts","verify-game-runtime.ps1"))).FirstOrDefault(File.Exists)??throw new ArgumentException("scripts/verify-game-runtime.ps1 was not found; run probe from the repository.");
 var start=new ProcessStartInfo("powershell",["-NoProfile","-ExecutionPolicy","Bypass","-File",script,"-MapArchive",archive,"-GamePath",Path.GetFullPath(game),..(expectStairs?Array.Empty<string>():["-NoStairs"])]){UseShellExecute=false};
 using var process=Process.Start(start)??throw new ArgumentException("Could not start PowerShell.");process.WaitForExit();return process.ExitCode==0?0:1;
}
static IEnumerable<string> Ancestors(string directory){for(var d=new DirectoryInfo(directory);d is not null;d=d.Parent)yield return d.FullName;}
