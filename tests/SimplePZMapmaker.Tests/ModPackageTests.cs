using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.IO.Compression;
using System.Text;
public static class ModPackageTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("mod package renames the mod id map folder title and can move maps under common",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank",Name="Pilna"});var original=NativeMapExporter.Export(p);var slug=NativeMapExporter.ModId(p);
   using var target=new MemoryStream();NativeModPackage.Repack(new MemoryStream(original),target,new("PilnaVillage",null,"Pilna village",true));
   using var zip=new ZipArchive(new MemoryStream(target.ToArray()));string Read(string path){using var r=new StreamReader(zip.GetEntry(path)!.Open(),Encoding.UTF8);return r.ReadToEnd();}
   Check(zip.Entries.All(e=>e.FullName.StartsWith("PilnaVillage/",StringComparison.Ordinal))&&!zip.Entries.Any(e=>e.FullName.Contains(slug)),"Old mod folder name remains");
   var info=Read("PilnaVillage/42/mod.info");Check(info.Contains("id=PilnaVillage\n")&&info.Contains("name=Pilna village\n"),"mod.info was not renamed: "+info);
   const string maps="PilnaVillage/common/media/maps/PilnaVillage/";Check(zip.Entries.Any(e=>e.FullName.StartsWith(maps,StringComparison.Ordinal)&&e.Name.EndsWith(".lotheader"))&&!zip.Entries.Any(e=>e.FullName.Contains("/42/media/maps/")),"Maps did not move under common");
   Check(Read(maps+"spawnregions.lua").Contains("media/maps/PilnaVillage/spawnpoints.lua")&&Read(maps+"map.info").Contains("title=Pilna village"),"Map references were not rewritten");
   Check(Read("PilnaVillage/INSTALL.txt").Contains("Map=PilnaVillage"),"Server instructions still name the old map");
   using var versioned=new MemoryStream();NativeModPackage.Repack(new MemoryStream(original),versioned,new(ModId:"Pilna",MapFolder:"PilnaMap"));using var v=new ZipArchive(new MemoryStream(versioned.ToArray()));Check(v.Entries.Any(e=>e.FullName.StartsWith("Pilna/42/media/maps/PilnaMap/",StringComparison.Ordinal))&&v.GetEntry("Pilna/common/media/maps/") is not null,"Separate map folder or common skeleton missing");
   Reject(()=>NativeModPackage.Validate(new("../escape")));Reject(()=>NativeModPackage.Validate(new(Name:"two\nlines")));
  });
  yield return("HTTP exports install only on POST and a GET can only rename the archive",()=>{
   Microsoft.AspNetCore.Http.HttpRequest Request(string method,string query){var context=new Microsoft.AspNetCore.Http.DefaultHttpContext();context.Request.Method=method;context.Request.QueryString=new(query);return context.Request;}
   Reject(()=>SimplePZMapmaker.Web.Services.NativeExportService.Options(Request("GET","?install=true")));
   Check(SimplePZMapmaker.Web.Services.NativeExportService.Options(Request("GET","?modId=Pilna")) is {ModId:"Pilna",Install:false},"A GET rename was refused");
   Check(SimplePZMapmaker.Web.Services.NativeExportService.Options(Request("POST","?install=true")) is {Install:true},"A POST install was refused");
   Check(SimplePZMapmaker.Web.Services.NativeExportService.Options(Request("GET","")) is null,"An export without options must not be repackaged");
  });
  yield return("install writes a marked mod folder, replaces only its own folder and refuses escaping paths",()=>{
   var mods=Path.Combine(Environment.GetEnvironmentVariable("PZMAP_WORKSPACE")??Path.GetFullPath("."),"work","mod-install-tests",Guid.NewGuid().ToString("N"));
   try{
    var p=MapGenerator.Generate(new(){Kind="blank",Name="Pilna"});using var named=new MemoryStream();NativeModPackage.Repack(new MemoryStream(NativeMapExporter.Export(p)),named,new("PilnaVillage"));
    var folder=NativeModPackage.Install(new MemoryStream(named.ToArray()),mods);Check(File.Exists(Path.Combine(folder,"42","mod.info"))&&File.Exists(Path.Combine(folder,NativeModPackage.InstallMarker)),"Installed mod is incomplete");
    File.WriteAllText(Path.Combine(folder,"stale.txt"),"old");NativeModPackage.Install(new MemoryStream(named.ToArray()),mods);Check(!File.Exists(Path.Combine(folder,"stale.txt")),"Reinstall did not replace the editor's own folder");
    File.Delete(Path.Combine(folder,NativeModPackage.InstallMarker));Reject(()=>NativeModPackage.Install(new MemoryStream(named.ToArray()),mods));Check(File.Exists(Path.Combine(folder,"42","mod.info")),"A foreign folder was touched");
    using var evil=new MemoryStream();using(var z=new ZipArchive(evil,ZipArchiveMode.Create,true)){z.CreateEntry("Evil/42/mod.info");z.CreateEntry("Evil/../../outside.txt");}
    Reject(()=>NativeModPackage.Install(new MemoryStream(evil.ToArray()),mods));Check(!File.Exists(Path.Combine(mods,"..","outside.txt")),"Archive escaped the mods folder");
   }finally{if(Directory.Exists(mods))Directory.Delete(mods,true);}
  });
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected validation failure");}
}
