using System.Text;
using System.Text.Json;
using System.IO.Compression;
using SimplePZMapmaker.Web.Services;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
public static class AssetTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("legacy forests use B42-visible tree frames in preview and native archive",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});p.SurfaceTheme="legacy";
   for(var y=0;y<24;y++)for(var x=0;x<24;x++)p.Terrain[y*p.Width+x]=Terrain.Forest;
   var sprites=NativeScene.Cell(p,0,0,24,24,0).Sprites;
   var trees=sprites.Where(s=>s.Tile.StartsWith("e_")||s.Tile.StartsWith("vegetation_trees_")).Select(s=>s.Tile).Distinct().ToArray();
   if(trees.Length==0||trees.Any(t=>!new[]{"e_americanholly_1_2","e_canadianhemlock_1_2","e_redmaple_1_2","e_cockspurhawthorn_1_2","e_virginiapine_1_2"}.Contains(t)))throw new Exception("Legacy forest still uses a tree without a Build 42 image");
   using var archive=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));using var header=archive.Entries.Single(e=>e.Name.EndsWith(".lotheader")).Open();using var bytes=new MemoryStream();header.CopyTo(bytes);
   var names=Encoding.ASCII.GetString(bytes.ToArray());if(trees.Any(t=>!names.Contains(t,StringComparison.Ordinal))||names.Contains("vegetation_trees_01_8",StringComparison.Ordinal))throw new Exception("Native archive differs from the B42-visible preview");
  });
  yield return ("whole-object palette groups before paging and keeps variants directions and raw search",()=>WithPaletteFixture((assets,root)=>{
   var page=PaletteJson(assets.SearchObjects("Bed","furniture",0,1));
   if(page.GetProperty("total").GetInt32()!=4||page.GetProperty("items").GetArrayLength()!=1)throw new Exception("Expected four objects, one per page");
   var first=page.GetProperty("items")[0];if(first.GetProperty("id").GetString()!="furniture_bed_0"||first.GetProperty("parts").GetArrayLength()!=2||!first.GetProperty("wholeObject").GetBoolean())throw new Exception("Bed parts not assembled");
   var second=PaletteJson(assets.SearchObjects("Bed","furniture",1,1)).GetProperty("items")[0];if(second.GetProperty("id").GetString()!="furniture_bed_2")throw new Exception("Different bed variant collapsed");
   if(PaletteJson(assets.SearchObjects("Bed","furniture",0,12,null,"N")).GetProperty("total").GetInt32()!=2)throw new Exception("Facing filter collapsed orientations");
   var byPart=PaletteJson(assets.SearchObjects("furniture_bed_1",null));if(byPart.GetProperty("total").GetInt32()!=1||byPart.GetProperty("items")[0].GetProperty("parts").GetArrayLength()!=2)throw new Exception("Part-ID search truncated the object");
   if(PaletteJson(assets.Search(null,null)).GetProperty("total").GetInt32()!=12)throw new Exception("Raw search lost tiles");
   if(PaletteJson(assets.SearchObjects(null,null,100,12)).GetProperty("items").GetArrayLength()!=0)throw new Exception("Past-end page is not empty");
   Reject(()=>assets.SearchObjects(null,null,-1));Reject(()=>assets.SearchObjects(null,null,0,121));
  }));
  yield return ("whole-object palette rejects incomplete uncertain and mixed-source grids and refreshes on activation",()=>WithPaletteFixture((assets,root)=>{
   if(PaletteJson(assets.SearchObjects("Broken",null)).GetProperty("total").GetInt32()!=0||PaletteJson(assets.SearchObjects("Uncertain",null)).GetProperty("total").GetInt32()!=0)throw new Exception("Partial grid claimed complete");
   var media=Path.Combine(root,"mods","one","media");Directory.CreateDirectory(media);File.WriteAllText(Path.Combine(root,"mods","one","mod.info"),"id=one\nname=One\ntiledef=fixture 900\n");
   File.WriteAllText(Path.Combine(media,"fixture.tiles.txt"),PaletteTile("furniture_bed_0","Bed","0,1","N")+PaletteTile("furniture_bed_100","Bed","0,1","N")+PaletteTile("furniture_bed_101","Bed","0,0","N"));
   assets.DiscoverMods([Path.Combine(root,"mods")],"42.0.0");assets.ActivateMods(["one"]);
   Reject(()=>assets.PlacementFor("furniture_bed_1"));
   var page=PaletteJson(assets.SearchObjects("Bed",null,0,12,"one"));if(page.GetProperty("total").GetInt32()!=1)throw new Exception("Mod and base parts were merged");
   var card=page.GetProperty("items")[0];if(card.GetProperty("parts").GetArrayLength()!=2||card.GetProperty("modId").GetString()!="one"||card.GetProperty("requirements")[0].GetString()!="one")throw new Exception("Object provenance/dependencies lost");
   if(PaletteJson(assets.SearchObjects("Bed",null,0,12,"game")).GetProperty("total").GetInt32()!=3)throw new Exception("Game source included mixed grid");
   assets.ActivateMods([]);if(PaletteJson(assets.SearchObjects("Bed",null)).GetProperty("total").GetInt32()!=4)throw new Exception("Object index stale after mods disabled");
  }));
  yield return ("whole-object palette placement callers cannot damage cached object footprints",()=>WithPaletteFixture((assets,root)=>{
   var placement=assets.PlacementFor("furniture_bed_0");placement.Parts.Clear();
   if(assets.PlacementFor("furniture_bed_1").Parts.Count!=2||PaletteJson(assets.SearchObjects("furniture_bed_0",null)).GetProperty("items")[0].GetProperty("parts").GetArrayLength()!=2)throw new Exception("Mutable placement leaked into object index");
  }));
  yield return ("whole-object palette never promotes the rectangular remainder of a renamed mod override",()=>WithPaletteFixture((assets,root)=>{
   var game=Path.Combine(root,"game");File.AppendAllText(Path.Combine(game,"media","newtiledefinitions.tiles.txt"),PaletteTile("furniture_double_0","Double","0,0")+PaletteTile("furniture_double_1","Double","1,0")+PaletteTile("furniture_double_2","Double","0,1")+PaletteTile("furniture_double_3","Double","1,1"));assets.Refresh(game,false);
   if(PaletteJson(assets.SearchObjects("Double",null)).GetProperty("total").GetInt32()!=1)throw new Exception("Full four-cell fixture missing");
   var media=Path.Combine(root,"mods","one","media");Directory.CreateDirectory(media);File.WriteAllText(Path.Combine(root,"mods","one","mod.info"),"id=one\nname=One\ntiledef=fixture 900\n");File.WriteAllText(Path.Combine(media,"fixture.tiles.txt"),PaletteTile("furniture_double_1","Changed","1,0")+PaletteTile("furniture_double_3","Changed","1,1"));assets.DiscoverMods([Path.Combine(root,"mods")],"42.0.0");assets.ActivateMods(["one"]);
   if(PaletteJson(assets.SearchObjects("Double",null)).GetProperty("total").GetInt32()!=0)throw new Exception("Half of an overridden object claimed complete");Reject(()=>assets.PlacementFor("furniture_double_0"));
  }));
  yield return ("rejected appearance changes leave the whole project unchanged",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=40,Y=40};BuildingDesigner.Generate(b,1,42);p.Buildings.Add(b);p.Version=5;
   for(var i=0;i<128;i++)p.SurfaceRegions.Add(new(){X=i,Y=0,Width=1,Height=1,Theme="dry"});
   var before=JsonSerializer.Serialize(p);Reject(()=>AppearanceDesigner.Apply(p,"rural",new(){X=40,Y=40,Width=40,Height=40}));if(JsonSerializer.Serialize(p)!=before)throw new Exception("Rejected129th region mutated project");
   Reject(()=>AppearanceDesigner.Apply(p,"dry",new(){X=250,Y=250,Width=int.MaxValue,Height=10}));if(JsonSerializer.Serialize(p)!=before)throw new Exception("Out-of-bounds region mutated project");
   Reject(()=>AppearanceDesigner.Apply(p,"unknown"));if(JsonSerializer.Serialize(p)!=before)throw new Exception("Unknown theme mutated project");
   AppearanceDesigner.Apply(p,"rural",new(){X=0,Y=0,Width=1,Height=1},false);if(p.SurfaceRegions.Count!=128||p.SurfaceRegions[^1].Theme!="rural")throw new Exception("Replacing a region at the cap must succeed");
  });
  yield return ("regional appearance is version gated and composes exact local terrain",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});p.SurfaceRegions.Add(new(){X=40,Y=40,Width=8,Height=8,Theme="dry"});p.Version=4;Reject(()=>ProjectValidator.Validate(p));p.Version=5;ProjectValidator.Validate(p);var scene=NativeScene.Cell(p,39,40,2,1,0);if(!scene.Sprites.Any(s=>s.X==40&&s.Tile=="blends_natural_01_80")||!scene.Sprites.Any(s=>s.X==39&&s.Tile=="blends_natural_01_32"))throw new Exception("Regional style leaked or missing");p.SurfaceTiles[40*256+40]="blends_street_01_16";if(!NativeScene.Cell(p,40,40,1,1,0).Sprites.Any(s=>s.Tile=="blends_street_01_16"))throw new Exception("Manual surface override lost");p.SurfaceRegions[0].Width=300;Reject(()=>ProjectValidator.Validate(p));
  });
  yield return ("regional appearance spans native cells and survives preserve reimport",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank",Size=512});p.Version=5;p.SurfaceRegions.Add(new(){X=250,Y=250,Width=20,Height=20,Theme="dry"});if(!NativeScene.Cell(p,4,4,1,1,0,0,0,1,1).Sprites.Any(s=>s.Tile=="blends_natural_01_80"))throw new Exception("Region lost across native cell");var source="""{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"highway":"path"},"geometry":{"type":"LineString","coordinates":[[30,50],[30.003,50.003]]}}]}""";var next=GeographicImporter.Import(source,p,null,default,true).Project;if(next.Version!=9||next.LinearFeatures.Count!=1||next.SurfaceRegions.Count!=1)throw new Exception("Preserved region dropped by import");ProjectValidator.Validate(next);
  });
  yield return ("regional overlap and manual overrides survive actual four-cell native packs",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank",Size=512,SurfaceTheme="temperate"});p.Version=5;
   p.SurfaceRegions=[new(){X=250,Y=250,Width=20,Height=20,Theme="dry"},new(){X=255,Y=255,Width=5,Height=5,Theme="rural"}];
   p.SurfaceTiles[256*512+256]="blends_street_01_48";p.Terrain[258*512+258]=Terrain.Road;p.Terrain[253*512+253]=Terrain.Water;
   var tree=Enumerable.Range(260,10).SelectMany(y=>Enumerable.Range(260,10).Select(x=>(x,y))).First(point=>EnvironmentStyles.Hash(point.x,point.y,p.Seed)%19==0);p.Terrain[tree.y*p.Width+tree.x]=Terrain.Forest;
   p.Decorations=[new(){X=257,Y=257,Asset="furniture_seating_indoor_01_11",Parts=[new(0,0,"furniture_seating_indoor_01_11",1)]}];
   using var archive=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)),ZipArchiveMode.Read);
   foreach(var (x,y,tile) in new[]{(250,250,"blends_natural_01_80"),(255,255,"blends_natural_01_32"),(256,255,"blends_natural_01_32"),(255,256,"blends_natural_01_32"),(256,256,"blends_street_01_48"),(269,269,"blends_natural_01_80"),(270,269,"blends_natural_01_32"),(258,258,"blends_street_01_16"),(253,253,"blends_natural_02_0")}){
    var tiles=NativeTiles(archive,p,x,y,0);if(!tiles.Contains(tile))throw new Exception($"Actual native pack lost regional floor at {x},{y}: {string.Join(',',tiles)}");
   }
   if(!NativeTiles(archive,p,257,257,0).Contains("furniture_seating_indoor_01_11"))throw new Exception("Regional style discarded manual decoration");
   var treeTiles=NativeTiles(archive,p,tree.x,tree.y,0);if(!treeTiles.Contains(EnvironmentStyles.Tree("dry",EnvironmentStyles.Hash(tree.x,tree.y,p.Seed)))||!treeTiles.SequenceEqual(NativeScene.Cell(p,tree.x%256,tree.y%256,1,1,0,0,0,tree.x/256,tree.y/256).Sprites.Select(s=>s.Tile)))throw new Exception("Regional forest differs between preview and actual pack");
  });
  yield return ("applying area appearance preserves manual buildings and exact resources",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var inside=new Building{X=40,Y=40};var crossing=new Building{X=75,Y=40};var manual=new Building{X=40,Y=70,Modified=true};foreach(var b in new[]{inside,crossing,manual}){BuildingDesigner.Generate(b,1,42);p.Buildings.Add(b);}
   inside.Design!.Floors[0].FloorTile="floors_interior_tilesandwood_01_8";inside.Design.RoofTileset="roofs_30_03";inside.Design.Floors[0].Edges[0].WallTile="walls_exterior_house_01_49";
   var kept=JsonSerializer.Serialize(manual);var outside=JsonSerializer.Serialize(crossing);var spawns=JsonSerializer.Serialize(p.Spawns);var terrain=p.Terrain.ToArray();
   if(AppearanceDesigner.Apply(p,"dry",new(){X=30,Y=30,Width=50,Height=60})!=1||inside.Design.Style=="")throw new Exception("Area must style only fully contained generated buildings");
   if(JsonSerializer.Serialize(manual)!=kept||JsonSerializer.Serialize(crossing)!=outside||JsonSerializer.Serialize(p.Spawns)!=spawns||!p.Terrain.SequenceEqual(terrain))throw new Exception("Area changed manual buildings, geometry or spawn");
   if(inside.Design.RoofTileset!="roofs_30_03"||inside.Design.Floors[0].FloorTile!="floors_interior_tilesandwood_01_8"||inside.Design.Floors[0].Edges[0].WallTile!="walls_exterior_house_01_49")throw new Exception("Area discarded explicit building resources");
   var styled=JsonSerializer.Serialize(p.Buildings);if(AppearanceDesigner.Apply(p,"rural",new(){X=10,Y=10,Width=200,Height=200},false)!=0||JsonSerializer.Serialize(p.Buildings)!=styled)throw new Exception("Terrain-only area changed buildings");
   p.SurfaceTiles[20*256+20]="blends_street_01_16";AppearanceDesigner.Apply(p,"urban",null,false);if(p.SurfaceRegions.Count!=0||p.SurfaceTheme!="urban"||p.Version<5||p.SurfaceTiles[20*256+20]!="blends_street_01_16")throw new Exception("Whole-map application must clear regions and retain manual overrides");
  });
  yield return ("regional persistence survives regeneration and preserves independent import copies",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=40,Y=40};BuildingDesigner.Generate(b,1,42);p.Buildings.Add(b);AppearanceDesigner.Apply(p,"dry",new(){X=30,Y=30,Width=60,Height=60},false);var region=JsonSerializer.Serialize(p.SurfaceRegions);
   BuildingDesigner.Regenerate(p);if(p.Version<5||JsonSerializer.Serialize(p.SurfaceRegions)!=region)throw new Exception("Regeneration downgraded or discarded regions");
   var source="""{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"highway":"path"},"geometry":{"type":"LineString","coordinates":[[30,50],[30.003,50.003]]}}]}""";
   var before=JsonSerializer.Serialize(p);var kept=GeographicImporter.Import(source,p,null,default,true).Project;var cleared=GeographicImporter.Import(source,p,null,default,false).Project;
   if(kept.Version<9||kept.LinearFeatures.Count!=1||JsonSerializer.Serialize(kept.SurfaceRegions)!=region||cleared.Version<9||cleared.LinearFeatures.Count!=1||cleared.SurfaceRegions.Count!=0||JsonSerializer.Serialize(p)!=before)throw new Exception("Import preserve/replace semantics damaged regions or original project");
   kept.SurfaceRegions[0].Theme="rural";if(p.SurfaceRegions[0].Theme!="dry")throw new Exception("Imported region aliases the original project");
  });
  yield return ("chosen outdoor floor still opens holes above the stairs",()=>{
   var b=new Building();BuildingDesigner.Generate(b,2,42);b.Design!.Floors[1].FloorTile="blends_natural_01_32";var stairs=b.Design.Floors[0].Objects.Single(o=>o.Kind=="stairs");var scene=NativeScene.Building(b,1);for(var i=1;i<=3;i++){var x=2+stairs.X+(stairs.Direction=="w"?i:0);var y=2+stairs.Y+(stairs.Direction=="n"?i:0);if(scene.Sprites.Any(s=>s.X==x&&s.Y==y&&s.Tile=="blends_natural_01_32"))throw new Exception("Chosen floor seals the stairs");}
  });
  yield return ("manual forest decoration replaces generated tree",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank",SurfaceTheme="temperate"});var point=Enumerable.Range(10,100).SelectMany(y=>Enumerable.Range(10,100).Select(x=>(x,y))).First(a=>((uint)(a.x*73856093)^(uint)(a.y*19349663)^(uint)p.Seed)%19==0);p.Terrain[point.y*256+point.x]=Terrain.Forest;var before=NativeScene.Cell(p,point.x,point.y,1,1,0);if(!before.Sprites.Any(s=>s.Tile.StartsWith("e_")))throw new Exception("Fixture needs a generated tree");p.Decorations.Add(new(){X=point.x,Y=point.y,Asset="furniture_seating_indoor_01_11",Parts=[new(0,0,"furniture_seating_indoor_01_11",1)]});var after=NativeScene.Cell(p,point.x,point.y,1,1,0);if(after.Sprites.Any(s=>s.Tile.StartsWith("e_")))throw new Exception("Generated tree overlaps chosen decoration");
  });
  yield return ("appearance assets require format4 and survive native scene composition",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=40,Y=40};BuildingDesigner.Generate(b,1,42);foreach(var f in b.Design!.Floors)f.Objects.RemoveAll(o=>o.Kind=="furniture");b.Design!.Floors[0].FloorTile="floors_interior_tilesandwood_01_8";p.Buildings.Add(b);p.Version=3;Reject(()=>ProjectValidator.Validate(p));p.Version=4;ProjectValidator.Validate(p);var scene=NativeScene.Building(b,0);if(!scene.Sprites.Any(s=>s.Tile=="floors_interior_tilesandwood_01_8"))throw new Exception("Chosen floor missing from scene");VisualStyles.Apply(b,"cottage");if(!NativeScene.Building(b,0).Sprites.Any(s=>s.Tile.StartsWith("walls_exterior_house_01_5")))throw new Exception("Selected facade missing");
  });
  yield return ("complete custom furniture reserves all parts against stairs and void",()=>{
   var b=new Building{Width=12,Height=10};BuildingDesigner.Generate(b,2,42);var f=b.Design!.Floors[0];f.Objects.RemoveAll(o=>o.Kind!="stairs");var bed=new BuildingObject{Kind="asset",X=3,Y=8,Asset="furniture_bedding_01_69",Parts=[new(0,0,"furniture_bedding_01_69",1),new(0,1,"furniture_bedding_01_68",1)]};f.Objects.Add(bed);BuildingDesigner.Validate(b);bed.Y=9;Reject(()=>BuildingDesigner.Validate(b));bed.X=1;bed.Y=2;Reject(()=>BuildingDesigner.Validate(b));
  });
  yield return ("world decorations and floor overrides enter exported tiles with navigation flags",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});p.SurfaceTiles[20*256+20]="blends_street_01_16";p.Decorations=[new(){X=30,Y=30,Asset="furniture_seating_indoor_01_11",Parts=[new(0,0,"furniture_seating_indoor_01_11",1)]}];ProjectValidator.Validate(p);var scene=NativeScene.Cell(p,16,16,24,24,0);if(!scene.Sprites.Any(s=>s.X==20&&s.Y==20&&s.Tile=="blends_street_01_16")||!scene.Sprites.Any(s=>s.X==30&&s.Y==30&&s.Tile=="furniture_seating_indoor_01_11"))throw new Exception("World appearance lost");p.Decorations[0].X=128;p.Decorations[0].Y=128;Reject(()=>ProjectValidator.Validate(p));
  });
  yield return ("reimport preserves manual world resources alongside building edits",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});p.Decorations=[new(){X=40,Y=40,Asset="furniture_seating_indoor_01_11",Parts=[new(0,0,"furniture_seating_indoor_01_11",1)]}];p.SurfaceTiles[50*256+50]="blends_street_01_16";var source="""{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"highway":"path"},"geometry":{"type":"LineString","coordinates":[[30,50],[30.003,50.003]]}}]}""";var next=GeographicImporter.Import(source,p,null,default,true).Project;if(next.Decorations.Count!=1||next.SurfaceTiles.GetValueOrDefault(50*256+50)!="blends_street_01_16")throw new Exception("Manual world resource lost");ProjectValidator.Validate(next);
  });
  yield return ("texture pack preserves cropped sprite coordinates and signed offsets",()=>{
   using var stream=new MemoryStream();using(var w=new BinaryWriter(stream,Encoding.UTF8,true)){w.Write("PZPK"u8);w.Write(1);w.Write(1);Text(w,"page");w.Write(1);w.Write(1);Text(w,"floors_test_0");foreach(var n in new[]{2,3,4,5,0,10,128,256})w.Write(n);var png=new byte[24];new byte[]{137,80,78,71,13,10,26,10}.CopyTo(png,0);System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16),64);System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20),64);w.Write(png.Length);w.Write(png);}stream.Position=0;var page=TexturePackIndex.Read(stream,"test").Single();var sprite=page.Sprites.Single();if(sprite.Name!="floors_test_0"||sprite.X!=2||sprite.OffsetY!=10||sprite.FrameHeight!=256||page.Length!=24)throw new Exception("Pack crop metadata lost");
  });
  yield return ("texture pack rejects truncated or unreasonable pages",()=>{foreach(var data in new[]{Array.Empty<byte>(),"PZPK"u8.ToArray(),"PZPK\x01\x00\x00\x00\xff\xff\xff\x7f"u8.ToArray()}){try{TexturePackIndex.Read(new MemoryStream(data),"bad");}catch(ArgumentException){continue;}throw new Exception("Invalid pack accepted");}});
 }
 static void Text(BinaryWriter w,string s){var bytes=Encoding.ASCII.GetBytes(s);w.Write(bytes.Length);w.Write(bytes);}
 static JsonElement PaletteJson(object value)=>JsonSerializer.SerializeToElement(value,new JsonSerializerOptions(JsonSerializerDefaults.Web));
 static string PaletteTile(string id,string name,string? grid,string facing="")=>$"// {id}\ntile\n{{\nCustomName = {name}\nGroupName = Fixture\nFacing = {facing}\nsolidtrans =\n"+(grid is null?"":$"SpriteGridPos = {grid}\n")+"}\n";
 static void WithPaletteFixture(Action<GameAssetCatalog,string> action){
  var root=Path.Combine(Path.GetTempPath(),"pz-palette-tests",Guid.NewGuid().ToString("N"));var game=Path.Combine(root,"game");Directory.CreateDirectory(Path.Combine(game,"media","texturepacks"));
  try{var tiles=PaletteTile("furniture_bed_0","Bed","0,1","N")+PaletteTile("furniture_bed_1","Bed","0,0","N")+PaletteTile("furniture_bed_2","Bed","0,1","N")+PaletteTile("furniture_bed_3","Bed","0,0","N")+PaletteTile("furniture_bed_4","Bed","1,0","S")+PaletteTile("furniture_bed_5","Bed","0,0","S")+PaletteTile("furniture_bed_6","Bed",null)+PaletteTile("furniture_broken_0","Broken","0,1")+PaletteTile("furniture_broken_1","Broken","1,0")+PaletteTile("furniture_broken_2","Broken","1,1")+PaletteTile("furniture_uncertain_0","Uncertain","0,0")+PaletteTile("furniture_attached_0","Attached",null).Replace("solidtrans =","attachedN =");
   File.WriteAllText(Path.Combine(game,"media","newtiledefinitions.tiles.txt"),tiles);action(new GameAssetCatalog(Path.Combine(root,"workspace"),game),root);
  }finally{var checkedRoot=Path.GetFullPath(root);var prefix=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"pz-palette-tests"))+Path.DirectorySeparatorChar;if(!checkedRoot.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new Exception("Unsafe fixture cleanup");Directory.Delete(checkedRoot,true);}
 }
 internal static List<string> NativeTiles(ZipArchive archive,MapProject p,int x,int y,int z){
  var root=$"{NativeMapExporter.ModId(p)}/42/media/maps/{NativeMapExporter.ModId(p)}/";var cx=p.WorldX+x/256;var cy=p.WorldY+y/256;
  using var header=archive.GetEntry(root+$"{cx}_{cy}.lotheader")!.Open();using var h=new BinaryReader(header);h.ReadBytes(8);var names=new List<string>();var count=h.ReadInt32();for(var i=0;i<count;i++){var bytes=new List<byte>();byte value;while((value=h.ReadByte())!=10)bytes.Add(value);names.Add(Encoding.ASCII.GetString(bytes.ToArray()));}h.ReadInt32();h.ReadInt32();var min=h.ReadInt32();
  using var source=archive.GetEntry(root+$"world_{cx}_{cy}.lotpack")!.Open();using var data=new MemoryStream();source.CopyTo(data);using var r=new BinaryReader(data);
  var localX=x%256;var localY=y%256;data.Position=12+8*(localX/8*32+localY/8);var offset=r.ReadInt64();data.Position=offset;var target=(z-min)*64+(localX%8)*8+localY%8;var index=0;
  while(index<=target){var n=r.ReadInt32();if(n==-1){index+=r.ReadInt32();if(index>target)return [];continue;}r.ReadInt32();var tiles=new List<string>();for(var i=1;i<n;i++)tiles.Add(names[r.ReadInt32()]);if(index++==target)return tiles;}
  throw new Exception("Native square not decoded");
 }
 static void Reject(Action a){try{a();}catch(ArgumentException){return;}throw new Exception("Invalid asset accepted");}
}
