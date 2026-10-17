using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Cli;

/// <summary>Starts the installed Build 42 dedicated server in a throw-away cache folder with the exported mod and a test-only Lua probe.
/// The probe asks the server's own metagrid for buildings at room tiles taken from the archive's authoring source, plus one empty
/// control tile, then the server is stopped with "quit". Nothing is written to the user's Zomboid folder.</summary>
public static class ServerCheck
{
 public sealed record Point(int X,int Y,string Name,bool ExpectBuilding);
 public sealed record Result(bool Started,bool Stopped,List<(Point Point,bool Building,string? Room)> Probes,string Log)
 {
  public bool Passed=>Started&&Stopped&&Probes.Count>0&&Probes.All(p=>p.Building==p.Point.ExpectBuilding);
 }
 /// <summary>Up to <paramref name="count"/> ground-floor room tiles spread over the sections (and shared buildings), and one tile without a building.</summary>
 public static List<Point> Points(ZipArchive zip,int count=12){
  var source=zip.Entries.FirstOrDefault(e=>e.Name is "world.pzworld.json" or "project.pzmap.json")??throw new ArgumentException("The archive has no world.pzworld.json or project.pzmap.json source to pick test buildings from.");
  using var stream=source.Open();var buildings=new List<(int OX,int OY,Building B)>();var occupied=new HashSet<(int,int)>();
  if(source.Name=="world.pzworld.json"){var world=JsonSerializer.Deserialize<WorldProject>(stream,NativeMapExporter.JsonOptions)!;foreach(var m in world.Maps)foreach(var b in m.Project.Buildings)buildings.Add((m.Project.WorldX*256,m.Project.WorldY*256,b));foreach(var b in world.Buildings)buildings.Add((0,0,b));}
  else{var p=JsonSerializer.Deserialize<MapProject>(stream,NativeMapExporter.JsonOptions)!;foreach(var b in p.Buildings)buildings.Add((p.WorldX*256,p.WorldY*256,b));}
  var points=new List<Point>();
  foreach(var (ox,oy,b) in buildings.Where(x=>x.B.Design is not null)){var f=b.Design!.Floors[-b.Design.MinLevel];for(var i=0;i<f.Cells.Length;i++)if(f.Cells[i]!='.')occupied.Add((ox+b.X+i%b.Width,oy+b.Y+i/b.Width));}
  var step=Math.Max(1,buildings.Count/Math.Max(1,count));
  for(var n=0;n<buildings.Count&&points.Count<count;n+=step){var (ox,oy,b)=buildings[n];if(b.Design is null)continue;var f=b.Design.Floors[-b.Design.MinLevel];var i=f.Cells.IndexOf(f.Cells.First(c=>c!='.'));points.Add(new(ox+b.X+i%b.Width,oy+b.Y+i/b.Width,b.Name,true));}
  if(points.Count==0)throw new ArgumentException("The export has no buildings to look for on the server.");
  // A control tile two rows north of the first building, which no building covers.
  for(var d=2;d<64;d++){(int X,int Y) c=(points[0].X,points[0].Y-d);if(!occupied.Contains(c)&&!occupied.Contains((c.X,c.Y+1))){points.Add(new(c.X,c.Y,"control without a building",false));break;}}
  return points;
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
   return new(ok&&started.Task.IsCompleted,stopped,probes,text);
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
    end
    print("SIMPLEPZ_PROBE done")
   end)
   """);
 }
}
