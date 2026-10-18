using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Cli;

/// <summary>A Build 42 dedicated server for playtesting an exported map: the mod is installed into its own server folder (never the
/// user's Zomboid folder), the map loads next to Muldraugh with the map's spawn regions, and the server runs in the foreground so the
/// tester can type commands and stop it with "quit". The world is kept between runs unless --fresh is given.</summary>
public static class PlaytestServer
{
 public static int Run(string archive,string game,string root,bool fresh,int port,Action<string> log){
  game=Path.GetFullPath(game);var java=Path.Combine(game,"jre64","bin","java.exe");var jar=Path.Combine(game,"projectzomboid.jar");
  if(!File.Exists(java)||!File.Exists(jar))throw new ArgumentException("The Build 42 installation needs jre64/bin/java.exe and projectzomboid.jar: "+game);
  if(port is <1024 or >65000)throw new ArgumentException("Choose a port between 1024 and 65000.");
  root=Path.GetFullPath(root);if(fresh&&Directory.Exists(root)){if(!File.Exists(Path.Combine(root,"Server","playtest.ini")))throw new ArgumentException("--fresh only clears a folder an earlier serve created (Server/playtest.ini is missing): "+root);Directory.Delete(root,true);}Directory.CreateDirectory(root);
  string mod;using(var zip=ZipFile.OpenRead(archive))mod=zip.Entries.First(e=>e.FullName.Contains('/')).FullName.Split('/')[0];
  if(!System.Text.RegularExpressions.Regex.IsMatch(mod,"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}$")||mod.Contains(".."))throw new ArgumentException("The archive's mod folder name is not a safe folder name: "+mod);
  var mods=Path.GetFullPath(Path.Combine(root,"mods"));var installed=Path.GetFullPath(Path.Combine(mods,mod));
  if(!installed.StartsWith(mods+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("The mod folder leaves the server folder.");
  if(Directory.Exists(installed))Directory.Delete(installed,true);   // only the previous copy of this mod inside the playtest server folder
  using(var stream=File.OpenRead(archive))NativeModPackage.Install(stream,mods);
  var map=Directory.EnumerateDirectories(Path.Combine(mods,mod),"maps",SearchOption.AllDirectories).SelectMany(Directory.EnumerateDirectories).Select(Path.GetFileName).FirstOrDefault()??throw new ArgumentException("The mod has no media/maps folder.");
  var server=Path.Combine(root,"Server");Directory.CreateDirectory(server);
  File.WriteAllText(Path.Combine(server,"playtest.ini"),$"Mods={mod}\nMap={map};Muldraugh, KY\nPublic=false\nOpen=true\nPVP=false\nDefaultPort={port}\nUDPPort={port+1}\nPauseEmpty=true\n");
  File.WriteAllText(Path.Combine(server,"playtest_spawnregions.lua"),$"function SpawnRegions()\n\treturn {{\n\t\t{{ name = \"{map}\", file = \"media/maps/{map}/spawnpoints.lua\" }},\n\t}}\nend\n");
  var password=Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
  log($"playtest server: map {map} (mod {mod}) in {root}");
  log($"join from Project Zomboid started with -nosteam: 127.0.0.1:{port}; admin login: admin / {password}");
  log($"the client needs the same mod: copy {Path.Combine(mods,mod)} into your Zomboid\\mods folder or export with --install");
  log("type quit in this window to save and stop the server");
  var start=new ProcessStartInfo(java){WorkingDirectory=game,UseShellExecute=false};
  foreach(var a in new[]{"--enable-native-access=ALL-UNNAMED","--add-exports=java.base/jdk.internal.misc=ALL-UNNAMED","-XX:+UseZGC","-Xmx6144m","-Djava.library.path=./natives/;./natives/win64/;./","-cp","./;projectzomboid.jar","zombie.network.GameServer",$"-cachedir={root}","-servername","playtest","-adminusername","admin","-adminpassword",password,"-nosteam"})start.ArgumentList.Add(a);
  using var process=Process.Start(start)??throw new ArgumentException("Could not start the game's Java.");process.WaitForExit();return process.ExitCode;
 }
}
