using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;
namespace SimplePZMapmaker.Cli;

/// <summary>Starts the installed Build 42 dedicated server in a throw-away cache folder with the exported mod and a test-only Lua probe.
/// The probe asks the server's own metagrid for buildings at room tiles taken from the archive's authoring source, plus one empty
/// control tile, then the server is stopped with "quit". Nothing is written to the user's Zomboid folder.</summary>
public static class ServerCheck
{
 public sealed record Point(int X,int Y,string Name,bool ExpectBuilding);
 public sealed record Result(bool Started,bool Stopped,List<(Point Point,bool Building,string? Room)> Probes,string Log)
 {
  /// <summary>Metagrid zone types the server reports at each probe point (foraging zones from the biome map included).</summary>
  public Dictionary<int,string> Zones {get;init;}=[];
  /// <summary>Spawn regions as the server loads them: profession keys, unemployed points and how many of those lie in a building.</summary>
  public List<(string Region,int Professions,int Points,int Inside)> Spawns {get;init;}=[];
  public bool Passed=>Started&&Stopped&&Probes.Count>0&&Probes.All(p=>p.Building==p.Point.ExpectBuilding)&&Spawns.Count>0&&Spawns.All(s=>s.Points>0);
 }
 /// <summary>Up to <paramref name="count"/> ground-floor room tiles spread over the sections (and shared buildings), and one tile without a building.</summary>
 public static List<Point> Points(ZipArchive zip,int count=12){
  if(count is <1 or >128)throw new ArgumentException("Building probe count must be between 1 and 128.");
  var source=Sources(zip);var buildings=new List<GroundBuilding>();var areas=new List<ProbeArea>();var groups=new List<List<Point>>();
  // Retain only ground geometry, never a section's terrain or upper floors. The second pass reads one terrain at a time.
  foreach(var p in source.Projects()){
   ValidateProject(p);var area=new ProbeArea(p.WorldX*256,p.WorldY*256,p.Width,p.Height);areas.Add(area);var group=new List<Point>();groups.Add(group);
   foreach(var b in p.Buildings){var ground=Ground(b,area.X,area.Y);if(b.X<0||b.Y<0||b.X+b.Width>p.Width||b.Y+b.Height>p.Height)throw new ArgumentException("A source building is outside its section.");buildings.Add(ground);if(FirstGround(ground,[area]) is {} point)group.Add(point);}
  }
  ValidateAreas(areas);
  var shared=new List<Point>();foreach(var b in source.Shared){var ground=Ground(b,0,0);buildings.Add(ground);if(FirstGround(ground,areas) is {} point)shared.Add(point);}if(shared.Count>0)groups.Insert(0,shared);
  var points=new List<Point>();
  // Spread the first choices over the whole layout, retaining the shared group first, then round-robin within those groups.
  var available=groups.Where(g=>g.Count>0).ToArray();var sampled=available.Length<=count?available:Enumerable.Range(0,count).Select(i=>available[count==1?0:i*(available.Length-1)/(count-1)]).ToArray();
  for(var i=0;points.Count<count&&sampled.Any(g=>g.Count>i);i++)foreach(var group in sampled)if(i<group.Count&&points.Count<count)points.Add(group[i]);
  if(points.Count==0)throw new ArgumentException("The export has no buildings to look for on the server.");
  // Chunk buckets keep large empty-terrain scans independent of the world's total building count.
  var occupied=new Dictionary<(int,int),List<GroundBuilding>>();foreach(var b in buildings)foreach(var area in areas){var left=Math.Max(b.X,area.X);var top=Math.Max(b.Y,area.Y);var right=Math.Min(b.X+b.Width,area.X+area.Width);var bottom=Math.Min(b.Y+b.Height,area.Y+area.Height);if(left>=right||top>=bottom)continue;for(var y=top/8;y<=(bottom-1)/8;y++)for(var x=left/8;x<=(right-1)/8;x++){if(!occupied.TryGetValue((x,y),out var bucket))occupied[(x,y)]=bucket=[];bucket.Add(b);}}
  bool Covered(int x,int y)=>occupied.TryGetValue((x/8,y/8),out var bucket)&&bucket.Any(b=>b.Contains(x,y));
  bool Inside(int x,int y)=>areas.Any(a=>a.Contains(x,y));
  bool Empty(int x,int y)=>Inside(x,y)&&!Covered(x,y)&&!Covered(x,y+1);
  Point? control=null;for(var d=2;d<64;d++){var x=points[0].X;var y=points[0].Y-d;if(Empty(x,y)){control=new(x,y,"control without a building",false);break;}}
  if(control is null)foreach(var area in areas){for(var y=area.Y;y<area.Y+area.Height&&control is null;y++)for(var x=area.X;x<area.X+area.Width;x++)if(Empty(x,y)){control=new(x,y,"control without a building",false);break;}if(control is not null)break;}
  if(control is not null)points.Add(control);
  // Zone checks: the centre of a fully wooded chunk and of an open chunk far from buildings, to read the foraging zones back.
  var zones=new Dictionary<Terrain,Point>();foreach(var p in source.Projects()){
   foreach(var (terrain,name) in new[]{(Terrain.Forest,"forest (zone check)"),(Terrain.Grass,"open ground (zone check)")}){
    if(zones.ContainsKey(terrain))continue;var found=false;for(var cy=2;cy<p.Height/8-2&&!found;cy++)for(var cx=2;cx<p.Width/8-2&&!found;cx++){
     var all=true;for(var y=cy*8-8;y<cy*8+16&&all;y++)for(var x=cx*8-8;x<cx*8+16&&all;x++)if(p.Terrain[y*p.Width+x]!=terrain||Covered(p.WorldX*256+x,p.WorldY*256+y))all=false;
     if(all){zones.Add(terrain,new(p.WorldX*256+cx*8+4,p.WorldY*256+cy*8+4,name,false));found=true;}}
   }if(zones.Count==2)break;
  }
  foreach(var terrain in new[]{Terrain.Forest,Terrain.Grass})if(zones.TryGetValue(terrain,out var point))points.Add(point);
  return points;
 }
 sealed record ProbeArea(int X,int Y,int Width,int Height){public bool Contains(int x,int y)=>x>=X&&y>=Y&&x<X+Width&&y<Y+Height;}
 sealed record GroundBuilding(int X,int Y,int Width,int Height,string Name,string? Cells){
  public bool Contains(int x,int y)=>x>=X&&y>=Y&&x<X+Width&&y<Y+Height&&(Cells is null||Cells[(y-Y)*Width+x-X]!='.');
 }
 sealed record ProbeSource(Func<IEnumerable<MapProject>> Projects,List<Building> Shared);
 static GroundBuilding Ground(Building b,int ox,int oy){
  if(b is null||b.X is <0 or >258048||b.Y is <0 or >258048||b.Width is <2 or >512||b.Height is <2 or >512||b.Name is null||b.Name.Length>100||string.IsNullOrEmpty(b.Id))throw new ArgumentException("Invalid source building geometry.");
  string? cells=null;if(b.Design is {} d){if(d.MinLevel is <-32 or >0||d.Floors is null||d.Floors.Count is <1 or >64||-d.MinLevel>=d.Floors.Count||d.Floors[-d.MinLevel]?.Cells is not {} ground||ground.Length!=b.Width*b.Height)throw new ArgumentException("Invalid source ground floor.");cells=ground;}
  return new(ox+b.X,oy+b.Y,b.Width,b.Height,b.Name,cells);
 }
 static Point? FirstGround(GroundBuilding b,IReadOnlyList<ProbeArea> areas){
  for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;x++)if(b.Contains(b.X+x,b.Y+y)&&areas.Any(a=>a.Contains(b.X+x,b.Y+y)))return new(b.X+x,b.Y+y,b.Name,true);return null;
 }
 static void ValidateProject(MapProject p){
  // Only the geometry read by this probe is needed; do not compose or rewrite authoring data to validate it.
  if(p.Width is <256 or >2048||p.Height is <256 or >2048||p.Width%256!=0||p.Height%256!=0||p.WorldX is <0 or >1000||p.WorldY is <0 or >1000||p.Terrain is null||p.Terrain.Length!=p.Width*p.Height||p.Terrain.Any(t=>!Enum.IsDefined(t))||p.Buildings is null||p.Buildings.Count>ProjectValidator.MaximumBuildings)throw new ArgumentException("Invalid source section dimensions, terrain or buildings.");
 }
 static void ValidateAreas(IReadOnlyList<ProbeArea> areas){
  if(areas.Count is <1 or >WorldLayoutLimits.MaxSections)throw new ArgumentException("Source requires 1-65,536 sections.");var cells=new HashSet<(int,int)>();foreach(var a in areas)for(var y=a.Y/256;y<(a.Y+a.Height)/256;y++)for(var x=a.X/256;x<(a.X+a.Width)/256;x++)if(!cells.Add((x,y))||cells.Count>WorldLayoutLimits.MaxWorldCells)throw new ArgumentException("Source sections overlap or exceed the cell budget.");
 }
 static ProbeSource Sources(ZipArchive zip){
  var indices=zip.Entries.Where(e=>e.Name=="world.pzworld.index.json").ToArray();var legacy=zip.Entries.Where(e=>e.Name is "world.pzworld.json" or "project.pzmap.json").ToArray();
  if(indices.Length>1)throw new ArgumentException("The archive has ambiguous streamed world sources.");
  if(indices.Length==1){
   var indexEntry=indices[0];var path=indexEntry.FullName.Split('/');if(path.Length!=3||path[1]!="source"||!SafeRoot(path[0]))throw new ArgumentException("Invalid streamed source path.");var root=path[0]+"/";var folder=root+"source/";
   var index=ReadJson<WorldSectionArchive.Manifest>(indexEntry,WorldSectionArchive.ManifestBytes);ValidateIndex(index);
   var metadataEntry=SingleEntry(zip,folder+"world.metadata.json");var metadata=ReadJson<WorldSectionArchive.Metadata>(metadataEntry,WorldSectionArchive.MetadataBytes,index.MetadataHash,index.MetadataBytes);
   if(metadata.Id!=index.Id||metadata.Name!=index.Name||metadata.Version is <1 or >13||metadata.Buildings is null||metadata.Buildings.Count>4000)throw new ArgumentException("World metadata differs from the source index.");
   var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.Ordinal);foreach(var e in zip.Entries.Where(e=>e.FullName.StartsWith(folder+"sections/",StringComparison.Ordinal)&&e.Name.Length>0)){
    var id=e.Name.EndsWith(".pzmap.json",StringComparison.Ordinal)?e.Name[..^11]:"";
    if(!WorldAssembler.SafeId(id)||e.FullName!=folder+"sections/"+id+".pzmap.json"||!index.Sections.Any(s=>s.Id==id)||!entries.TryAdd(id,e))throw new ArgumentException("Invalid, duplicate or unindexed section source.");
   }
   if(entries.Count==0)throw new ArgumentException("The streamed export has no section sources.");
   // Selected exports deliberately retain the complete index and a standalone legacy copy of the selected section.
   if(legacy.Length>0&&(legacy.Length!=1||legacy[0].FullName!=root+"project.pzmap.json"||entries.Count!=1))throw new ArgumentException("The archive has ambiguous authoring sources.");
   IEnumerable<MapProject> Projects(){foreach(var s in index.Sections){if(!entries.TryGetValue(s.Id,out var entry))continue;var p=ReadJson<MapProject>(entry,WorldSectionArchive.SectionBytes,s.Hash,s.Bytes);ValidateProject(p);if(p.Name!=s.Name||p.WorldX!=s.WorldX||p.WorldY!=s.WorldY||p.Width!=s.Width||p.Height!=s.Height)throw new ArgumentException("A section differs from its source index.");yield return p;}}
   return new(Projects,metadata.Buildings);
  }
  if(legacy.Length==0)throw new ArgumentException("The archive has no world.pzworld.json, project.pzmap.json or world.pzworld.index.json source to pick test buildings from.");
  if(legacy.Length!=1)throw new ArgumentException("The archive has ambiguous authoring sources.");var source=legacy[0];
  if(source.Name=="project.pzmap.json")return new(()=>[ReadJson<MapProject>(source,ProjectValidator.MaximumProjectBytes)],[]);
  var world=ReadJson<WorldProject>(source,ProjectValidator.MaximumProjectBytes);if(world.Maps is null||world.Maps.Count is <1 or >WorldLayoutLimits.MaxSections||world.Maps.Any(m=>m?.Project is null)||world.Buildings is null||world.Buildings.Count>4000)throw new ArgumentException("Invalid world source sections or buildings.");return new(()=>world.Maps.Select(m=>m.Project),world.Buildings);
 }
 static bool SafeRoot(string root)=>root.Length is >0 and <=64&&root[0] is not '.' and not '-'&&root.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-' or '.');
 static ZipArchiveEntry SingleEntry(ZipArchive zip,string name){var entries=zip.Entries.Where(e=>e.FullName==name).ToArray();return entries.Length==1?entries[0]:throw new ArgumentException("A required source entry is missing or ambiguous: "+name);}
 static void ValidateIndex(WorldSectionArchive.Manifest index){
  static bool Hash(string? h)=>h is {Length:64}&&h.All(char.IsAsciiHexDigit);
  if(index.Format!="SimplePZWorldSections"||index.Version!=1||!WorldAssembler.SafeId(index.Id)||index.Sections is null||index.Sections.Count is <1 or >WorldLayoutLimits.MaxSections||index.MetadataBytes is <1 or >WorldSectionArchive.MetadataBytes||!Hash(index.MetadataHash))throw new ArgumentException("Invalid streamed source index.");
  var ids=new HashSet<string>(StringComparer.Ordinal);var areas=new List<ProbeArea>();foreach(var s in index.Sections){if(s is null||!WorldAssembler.SafeId(s.Id)||!ids.Add(s.Id)||!Hash(s.Hash)||s.Bytes is <1 or >WorldSectionArchive.SectionBytes||s.WorldX is <0 or >1000||s.WorldY is <0 or >1000||s.Width is <256 or >2048||s.Height is <256 or >2048||s.Width%256!=0||s.Height%256!=0)throw new ArgumentException("Invalid streamed section identity or layout.");areas.Add(new(s.WorldX*256,s.WorldY*256,s.Width,s.Height));}ValidateAreas(areas);
 }
 static T ReadJson<T>(ZipArchiveEntry entry,int maximum,string? hash=null,long? bytes=null) where T:class {
  if(entry.Length<1||entry.Length>maximum||bytes is {} expected&&entry.Length!=expected)throw new ArgumentException("Source entry is empty, oversized or differs from its indexed size: "+entry.FullName);
  try{using var raw=entry.Open();using var bounded=new SourceReadStream(raw,maximum);var value=JsonSerializer.Deserialize<T>(bounded,NativeMapExporter.JsonOptions)??throw new ArgumentException("Source JSON is null: "+entry.FullName);if(bounded.Length!=entry.Length||hash is not null&&!string.Equals(bounded.Fingerprint(),hash,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Source entry differs from its immutable fingerprint: "+entry.FullName);return value;}
  catch(Exception e) when(e is JsonException or InvalidDataException){throw new ArgumentException("Invalid source JSON: "+entry.FullName,e);}
 }
 sealed class SourceReadStream(Stream inner,long maximum):Stream {
  readonly IncrementalHash hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);long read;
  public string Fingerprint()=>Convert.ToHexStringLower(hash.GetHashAndReset());
  public override int Read(byte[] buffer,int offset,int count)=>Read(buffer.AsSpan(offset,count));
  public override int Read(Span<byte> buffer){var n=inner.Read(buffer[..(int)Math.Min(buffer.Length,maximum-read+1)]);if(n>maximum-read)throw new ArgumentException("Source JSON exceeds its byte limit.");hash.AppendData(buffer[..n]);read+=n;return n;}
  public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>read;public override long Position{get=>read;set=>throw new NotSupportedException();}
  public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override void Flush(){}
  protected override void Dispose(bool disposing){if(disposing)hash.Dispose();base.Dispose(disposing);}
 }
 public static Result Run(string archive,string game,TimeSpan timeout,Action<string>? log=null){
  game=Path.GetFullPath(game);var java=Path.Combine(game,"jre64","bin","java.exe");var jar=Path.Combine(game,"projectzomboid.jar");
  if(!File.Exists(java)||!File.Exists(jar))throw new ArgumentException("The Build 42 installation needs jre64/bin/java.exe and projectzomboid.jar: "+game);
  var root=Path.Combine(Path.GetTempPath(),"simplepzmap-server-"+Guid.NewGuid().ToString("N")[..12]);Directory.CreateDirectory(root);
  try{
   string mod;List<Point> points;using(var zip=ZipFile.OpenRead(archive)){mod=zip.Entries.First(e=>e.FullName.Contains('/')).FullName.Split('/')[0];points=Points(zip);}
   using(var stream=File.OpenRead(archive))NativeModPackage.Install(stream,Path.Combine(root,"mods"));
   var map=ReadMapFolder(Path.Combine(root,"mods",mod))??throw new ArgumentException("The mod has no media/maps folder.");
   WriteProbe(Path.Combine(root,"mods","SimplePZServerProbe"),points);
   var server=Path.Combine(root,"Server");Directory.CreateDirectory(server);
   File.WriteAllText(Path.Combine(server,"check.ini"),$"Mods={mod};SimplePZServerProbe\nMap={map};Muldraugh, KY\nPublic=false\nOpen=true\nPVP=false\n");
   File.WriteAllText(Path.Combine(server,"check_spawnregions.lua"),$"function SpawnRegions()\n\treturn {{\n\t\t{{ name = \"{map}\", file = \"media/maps/{map}/spawnpoints.lua\" }},\n\t}}\nend\n");
   var start=new ProcessStartInfo(java){WorkingDirectory=game,UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
   foreach(var a in new[]{"--enable-native-access=ALL-UNNAMED","--add-exports=java.base/jdk.internal.misc=ALL-UNNAMED","-XX:+UseZGC","-Xmx6144m",$"-Djava.library.path=./natives/;./natives/win64/;./","-cp","./;projectzomboid.jar","zombie.network.GameServer",$"-cachedir={root}","-servername","check","-adminpassword",Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12)),"-nosteam"})start.ArgumentList.Add(a);
   var output=new StringBuilder();var started=new TaskCompletionSource();var probed=new TaskCompletionSource();var gate=new object();
   using var process=Process.Start(start)??throw new ArgumentException("Could not start the game's Java.");
   void Line(string? line){if(line is null)return;lock(gate)output.AppendLine(line);if(line.Contains("*** SERVER STARTED ****")){log?.Invoke("server started");started.TrySetResult();}if(line.Contains("SIMPLEPZ_PROBE done"))probed.TrySetResult();}
   process.OutputDataReceived+=(_,e)=>Line(e.Data);process.ErrorDataReceived+=(_,e)=>Line(e.Data);process.BeginOutputReadLine();process.BeginErrorReadLine();
   var ok=Task.WaitAny([started.Task,process.WaitForExitAsync()],timeout)==0&&probed.Task.Wait(TimeSpan.FromSeconds(60));
   if(!process.HasExited){process.StandardInput.WriteLine("quit");process.StandardInput.Flush();}
   var stopped=process.WaitForExit(TimeSpan.FromSeconds(120))&&process.ExitCode==0;if(!process.HasExited){process.Kill(true);process.WaitForExit();}
   var text=output.ToString();var probes=new List<(Point,bool,string?)>();
   foreach(Match m in Regex.Matches(text,@"SIMPLEPZ_PROBE (\d+) (true|false) (\S+)")){var i=int.Parse(m.Groups[1].Value);if(i<points.Count)probes.Add((points[i],m.Groups[2].Value=="true",m.Groups[3].Value=="nil"?null:m.Groups[3].Value));}
   var zones=Regex.Matches(text,@"SIMPLEPZ_ZONES (\d+) ?(\S*)").ToDictionary(m=>int.Parse(m.Groups[1].Value),m=>m.Groups[2].Value);
   var spawns=Regex.Matches(text,@"SIMPLEPZ_SPAWNS (\S+) (\d+) (\d+) (\d+)").Select(m=>(m.Groups[1].Value,int.Parse(m.Groups[2].Value),int.Parse(m.Groups[3].Value),int.Parse(m.Groups[4].Value))).ToList();
   return new(ok&&started.Task.IsCompleted,stopped,probes,text){Zones=zones,Spawns=spawns};
  }finally{try{Directory.Delete(root,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
 }
 static string? ReadMapFolder(string mod)=>Directory.EnumerateDirectories(mod,"maps",SearchOption.AllDirectories).SelectMany(Directory.EnumerateDirectories).Select(Path.GetFileName).FirstOrDefault();
 static void WriteProbe(string folder,List<Point> points){
  var lua=Path.Combine(folder,"42","media","lua","server");Directory.CreateDirectory(lua);Directory.CreateDirectory(Path.Combine(folder,"common"));
  File.WriteAllText(Path.Combine(folder,"42","mod.info"),"name=SimplePZ server probe\nid=SimplePZServerProbe\nversionMin=42.0\ndescription=Test-only: reports metagrid buildings at given tiles.\n");
  var list=string.Join(",",points.Select(p=>$"{{{p.X},{p.Y}}}"));
  File.WriteAllText(Path.Combine(lua,"SimplePZServerProbe.lua"),$$"""
   -- Test-only probe written by simplepzmap server-check; it never ships with a map.
   local points = { {{list}} }
   Events.OnServerStarted.Add(function()
    local grid = getWorld():getMetaGrid()
    for i, p in ipairs(points) do
     local room = grid:getRoomAt(p[1], p[2], 0)
     print(string.format("SIMPLEPZ_PROBE %d %s %s", i - 1, tostring(grid:getBuildingAt(p[1], p[2]) ~= nil), room and room:getName() or "nil"))
     local ok, zones = pcall(function() return grid:getZonesAt(p[1], p[2], 0) end)
     local names = {}
     if ok and zones then for j = 0, zones:size() - 1 do table.insert(names, tostring(zones:get(j):getType())) end elseif not ok then table.insert(names, "error:" .. tostring(zones):gsub("%s", "_")) end
     print(string.format("SIMPLEPZ_ZONES %d %s", i - 1, table.concat(names, ",")))
    end
    local loaded, regions = pcall(function() return SpawnRegionMgr.getSpawnRegionsAux() end)
    if loaded and regions then for _, region in ipairs(regions) do
     local professions, points, inside = 0, 0, 0
     for _ in pairs(region.points) do professions = professions + 1 end
     for _, p in ipairs(region.points.unemployed or {}) do points = points + 1; if grid:getBuildingAt(p.posX, p.posY) ~= nil then inside = inside + 1 end end
     print(string.format("SIMPLEPZ_SPAWNS %s %d %d %d", tostring(region.name):gsub("%s", "_"), professions, points, inside))
    end else print("SIMPLEPZ_SPAWNS_ERROR " .. tostring(regions)) end
    print("SIMPLEPZ_PROBE done")
   end)
   """);
 }
}
