using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Web.Services;

public static class RampAssetTests
{
 public static IEnumerable<(string Name,Action Run)> Cases()
 {
  yield return("ramp assets load binary properties and both image pages without replacing regular tiles",()=>WithFixture(root=>{
   var game=Fixture(root);var assets=new GameAssetCatalog(root,game);var regular=assets.Get("fixture_floor_0");
   Check(regular.Name=="Regular floor"&&regular.Page is not null,"Regular tile changed");
   foreach(var (direction,start) in new[]{("N",0),("S",24),("W",48),("E",72)})for(var segment=1;segment<=20;segment++){
    var a=assets.Get("ramps_01_"+(start+segment-1));
    Check(a.Floor&&a.Source=="game"&&a.ModId is null&&a.Category=="floors","Ramp lost floor or provider metadata");
    Check(a.Properties["SlopedSurfaceDirection"]==direction&&a.Properties["PhysicsMesh"]==$"ramp20segment{segment}{direction.ToLowerInvariant()}"&&a.Properties["SlopedSurfaceHeightMax"]==(segment*5).ToString(),"Ramp physics metadata changed");
    Check(a.Properties.GetValueOrDefault("SlopedSurfaceHeightMin","0")==((segment-1)*5).ToString(),"Ramp minimum height changed");
    Check(a.Page is not null&&a.Page!=regular.Page&&a.Sprite is {Width:1,Height:1},"Ramp image missing or regular image replaced");
    Check(assets.Page(a.Page!).SequenceEqual(Png),"Catalog page is not the fixture PNG");
   }
   Reject(()=>assets.Get("ramps_01_20"));
   var search=JsonSerializer.SerializeToElement(assets.Search("ramps_01_",null,0,120),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
   Check(search.GetProperty("total").GetInt32()==80&&search.GetProperty("items").EnumerateArray().All(a=>a.GetProperty("pageWidth").GetInt32()==1),"Search omitted ramp image dimensions");
   Check(!File.Exists(Path.Combine(root,"work","assets","source.json")),"Construction saved a game profile");
  }));
  yield return("ramp assets are optional for older game catalogs",()=>WithFixture(root=>{
   var game=Fixture(root,false);var assets=new GameAssetCatalog(root,game);Check(assets.Ready&&assets.Count==4&&assets.Get("fixture_floor_0").Page is not null,"Absent optional ramps broke ordinary assets");
  }));
  yield return("ramp assets reject malformed and oversized definitions without damaging the active catalog",()=>WithFixture(root=>{
   var game=Fixture(root);var assets=new GameAssetCatalog(root,game);var original=assets.Count;var floor=assets.Get("fixture_floor_0");var path=Path.Combine(game,"media","tiledefinitions_b42chunkcaching.tiles");
   File.WriteAllBytes(path,"tdef"u8.ToArray());Reject(()=>assets.Refresh(game,false));
   Check(assets.Count==original&&ReferenceEquals(assets.Get("fixture_floor_0"),floor),"Bad ramp definitions replaced the active catalog");
   using(var file=File.Create(path))file.SetLength(32L*1024*1024+1);Reject(()=>assets.Refresh(game,false));
   Check(assets.Count==original,"Oversized ramp definitions replaced the active catalog");
  }));
  yield return("ramp assets reject corrupt and oversized packs atomically",()=>WithFixture(root=>{
   var game=Fixture(root);var assets=new GameAssetCatalog(root,game);var floor=assets.Get("fixture_floor_0");var path=Path.Combine(game,"media","texturepacks","B42ChunkCaching2x.pack");
   File.WriteAllBytes(path,"PZPK"u8.ToArray());Reject(()=>assets.Refresh(game,false));
   Check(ReferenceEquals(assets.Get("fixture_floor_0"),floor),"Bad ramp pack replaced the active catalog");
   using(var file=File.Create(path))file.SetLength(512L*1024*1024+1);Reject(()=>assets.Refresh(game,false));
   Check(ReferenceEquals(assets.Get("fixture_floor_0"),floor),"Oversized ramp pack replaced the active catalog");
  }));
  yield return("ramp assets retain mod override provenance and restore original images on disable",()=>WithFixture(root=>{
   var game=Fixture(root);var assets=new GameAssetCatalog(root,game);var original=assets.Get("ramps_01_72");
   var mod=Path.Combine(root,"mods","override");Directory.CreateDirectory(Path.Combine(mod,"media","texturepacks"));
   File.WriteAllText(Path.Combine(mod,"mod.info"),"id=ramp_override\nname=Ramp override\ntiledef=override 1911\npack=override\n");
   File.WriteAllText(Path.Combine(mod,"media","override.tiles.txt"),"// ramps_01_72\ntile\n{\nsolidfloor =\nPhysicsMesh = altered\nCustomName = Mod ramp\n}\n// fixture_modflat_0\ntile\n{\nsolidfloor =\n}\n");
   Pack(Path.Combine(mod,"media","texturepacks","override.pack"),[new[]{"ramps_01_72"}]);
   assets.DiscoverMods([Path.Combine(root,"mods")]);assets.ActivateMods(["ramp_override"]);var overridden=assets.Get("ramps_01_72");
   Check(overridden.Source=="mod"&&overridden.ModId=="ramp_override"&&overridden.Name=="Mod ramp"&&overridden.Page!=original.Page&&assets.ResolveRequirements([overridden.Id]).Single().ModId=="ramp_override","Ramp mod ownership/image lost");
   AssetRules.CheckDeckArtwork(assets,"fixture_modflat_0");Reject(()=>AssetRules.CheckDeckArtwork(assets,overridden.Id));
   assets.ActivateMods([]);var restored=assets.Get(original.Id);
   Check(restored.Source=="game"&&restored.ModId is null&&restored.Page==original.Page&&restored.Properties["PhysicsMesh"]=="ramp20segment1e"&&assets.ResolveRequirements([restored.Id]).Count==0,"Disable did not restore native ramp");
  }));
  foreach(var id in new[]{"fixture_floor_1","fixture_floor_2","fixture_floor_3"}){
   var tile=id;
   yield return("ramp assets prevent physical slope cosmetics "+tile,()=>WithFixture(root=>{
    var assets=new GameAssetCatalog(root,Fixture(root,false));AssetRules.CheckDeckArtwork(assets,"fixture_floor_0");Reject(()=>AssetRules.CheckDeckArtwork(assets,tile));
   }));
  }
  yield return("ramp assets resolve installed game directions and original PNG pages",()=>WithFixture(root=>{
   var game=Environment.GetEnvironmentVariable("PZMAP_GAME_PATH")??"";
   if(!File.Exists(Path.Combine(game,"media","tiledefinitions_b42chunkcaching.tiles"))){Console.WriteLine("SKIP installed-game ramp assets: optional game absent");return;}
   var assets=new GameAssetCatalog(root,game);
   foreach(var (direction,start) in new[]{("N",0),("S",24),("W",48),("E",72)})for(var segment=1;segment<=20;segment++){
    var a=assets.Get("ramps_01_"+(start+segment-1));Check(a.Source=="game"&&a.Floor&&a.Page is not null&&a.Sprite is not null,"Installed ramp has no native image");
    Check(a.Properties["SlopedSurfaceDirection"]==direction&&a.Properties["PhysicsMesh"]==$"ramp20segment{segment}{direction.ToLowerInvariant()}"&&int.Parse(a.Properties["SlopedSurfaceHeightMax"])==5*segment&&int.Parse(a.Properties.GetValueOrDefault("SlopedSurfaceHeightMin","0"))==5*(segment-1),"Installed ramp physical properties changed");
    var png=assets.Page(a.Page!);Check(png.AsSpan(0,8).SequenceEqual(Png.AsSpan(0,8))&&BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16))>0&&BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20))>0,"Installed ramp page is not a PNG");
   }
  }));
 }
 static readonly byte[] Png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZf8AAAAASUVORK5CYII=");
 static string Fixture(string root,bool ramps=true){
  var game=Path.Combine(root,"game");var media=Path.Combine(game,"media");Directory.CreateDirectory(Path.Combine(media,"texturepacks"));
  File.WriteAllText(Path.Combine(media,"newtiledefinitions.tiles.txt"),"// fixture_floor_0\ntile\n{\nsolidfloor =\nCustomName = Regular floor\n}\n// fixture_floor_1\ntile\n{\nsolidfloor =\nPhysicsMesh = ramp20segment1n\n}\n// fixture_floor_2\ntile\n{\nsolidfloor =\nSlopedSurfaceDirection = N\n}\n// fixture_floor_3\ntile\n{\nsolidfloor =\nSlopedSurfaceHeightMax = 5\n}\n");
  Pack(Path.Combine(media,"texturepacks","Tiles2x.pack"),[new[]{"fixture_floor_0"}]);
  if(ramps){
   using(var w=new BinaryWriter(File.Create(Path.Combine(media,"tiledefinitions_b42chunkcaching.tiles")),Encoding.Latin1)){
    w.Write("tdef"u8);w.Write(1);w.Write(2);Line(w,"ramps_01");Line(w,"ramps_01.png");w.Write(8);w.Write(32);w.Write(1);w.Write(256);
    for(var i=0;i<256;i++){
     var start=i/24*24;var segment=i%24+1;var props=new Dictionary<string,string>();
     if(i<92&&segment<=20){var direction=new[]{"N","S","W","E"}[start/24];props["PhysicsMesh"]=$"ramp20segment{segment}{direction.ToLowerInvariant()}";props["SlopedSurfaceDirection"]=direction;props["SlopedSurfaceHeightMax"]=(segment*5).ToString();if(segment>1)props["SlopedSurfaceHeightMin"]=((segment-1)*5).ToString();props["solidfloor"]="";}
     w.Write(props.Count);foreach(var (key,value) in props){Line(w,key);Line(w,value);}
    }
    Line(w,"fixture_floor");Line(w,"fixture.png");w.Write(1);w.Write(1);w.Write(2);w.Write(1);w.Write(1);Line(w,"CustomName");Line(w,"Wrong binary override");
   }
   var names=new[]{0,24,48,72}.SelectMany(start=>Enumerable.Range(start,20)).Select(i=>"ramps_01_"+i).ToArray();
   Pack(Path.Combine(media,"texturepacks","B42ChunkCaching2x.pack"),[names[..40].Append("fixture_floor_0").ToArray(),names[40..]]);
  }
  return game;
 }
 static void Pack(string path,string[][] pages){using var w=new BinaryWriter(File.Create(path),Encoding.ASCII);w.Write("PZPK"u8);w.Write(1);w.Write(pages.Length);for(var p=0;p<pages.Length;p++){Text(w,"fixture_page_"+p);w.Write(pages[p].Length);w.Write(1);foreach(var name in pages[p]){Text(w,name);foreach(var n in new[]{0,0,1,1,0,0,1,1})w.Write(n);}w.Write(Png.Length);w.Write(Png);}}
 static void Text(BinaryWriter w,string value){var bytes=Encoding.ASCII.GetBytes(value);w.Write(bytes.Length);w.Write(bytes);}
 static void Line(BinaryWriter w,string value){w.Write(Encoding.Latin1.GetBytes(value));w.Write((byte)10);}
 static void WithFixture(Action<string> action){var basis=Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("PZMAP_WORKSPACE")??Directory.GetCurrentDirectory(),"work","ramp-asset-tests"));var root=Path.Combine(basis,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{action(root);}finally{var target=Path.GetFullPath(root);if(!target.StartsWith(basis+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Unsafe ramp fixture cleanup");Directory.Delete(target,true);}}
 static void Check(bool value,string error){if(!value)throw new Exception(error);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected invalid ramp data or physical artwork to be rejected");}
}
