using System.Text;
using SimplePZMapmaker.Web.Services;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
public static class AssetTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("chosen outdoor floor still opens holes above the stairs",()=>{
   var b=new Building();BuildingDesigner.Generate(b,2,42);b.Design!.Floors[1].FloorTile="blends_natural_01_32";var stairs=b.Design.Floors[0].Objects.Single(o=>o.Kind=="stairs");var scene=NativeScene.Building(b,1);for(var i=1;i<=3;i++){var x=2+stairs.X+(stairs.Direction=="w"?i:0);var y=2+stairs.Y+(stairs.Direction=="n"?i:0);if(scene.Sprites.Any(s=>s.X==x&&s.Y==y&&s.Tile=="blends_natural_01_32"))throw new Exception("Chosen floor seals the stairs");}
  });
  yield return ("manual forest decoration replaces generated tree",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank",SurfaceTheme="temperate"});var point=Enumerable.Range(10,100).SelectMany(y=>Enumerable.Range(10,100).Select(x=>(x,y))).First(a=>((uint)(a.x*73856093)^(uint)(a.y*19349663)^(uint)p.Seed)%19==0);p.Terrain[point.y*256+point.x]=Terrain.Forest;var before=NativeScene.Cell(p,point.x,point.y,1,1,0);if(!before.Sprites.Any(s=>s.Tile.StartsWith("e_")))throw new Exception("Fixture needs a generated tree");p.Decorations.Add(new(){X=point.x,Y=point.y,Asset="furniture_seating_indoor_01_11",Parts=[new(0,0,"furniture_seating_indoor_01_11",1)]});var after=NativeScene.Cell(p,point.x,point.y,1,1,0);if(after.Sprites.Any(s=>s.Tile.StartsWith("e_")))throw new Exception("Generated tree overlaps chosen decoration");
  });
  yield return ("appearance assets require format4 and survive native scene composition",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{X=40,Y=40};BuildingDesigner.Generate(b,1,42);b.Design!.Floors[0].FloorTile="floors_interior_tilesandwood_01_8";p.Buildings.Add(b);p.Version=3;Reject(()=>ProjectValidator.Validate(p));p.Version=4;ProjectValidator.Validate(p);var scene=NativeScene.Building(b,0);if(!scene.Sprites.Any(s=>s.Tile=="floors_interior_tilesandwood_01_8"))throw new Exception("Chosen floor missing from scene");VisualStyles.Apply(b,"cottage");if(!NativeScene.Building(b,0).Sprites.Any(s=>s.Tile.StartsWith("walls_exterior_house_01_5")))throw new Exception("Selected facade missing");
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
 static void Reject(Action a){try{a();}catch(ArgumentException){return;}throw new Exception("Invalid asset accepted");}
}
