using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

public static class ImportedBridgeDeckTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("imported ground bridge keeps water and connects flat approaches without inferring OSM layer",()=>{
   var p=RiverMap();var before=JsonSerializer.Serialize(p);var bridge=Bridge();var next=LinearFeatureEditor.Put(p,bridge);
   Check(JsonSerializer.Serialize(p)==before,"Bridge placement mutated its source");
   Check(next.Terrain[100*256+100]==Terrain.Water&&next.Terrain[100*256+80]==Terrain.Road,"Bridge lost water or its dry approach");
   Check(next.LinearFeatures.Single().Underlay.All(c=>c.Terrain!=Terrain.Water),"Bridge owns painted water underlay");
   Check(InfrastructureGeometry.HasFeatures(next)&&InfrastructureGeometry.Build(next).Tiles.All(t=>t.Z==0),"Bridge missing or OSM relative layer became native height");
   var scene=NativeScene.Cell(next,64,88,80,24,0);Check(scene.Sprites.Where(s=>s.X==100&&s.Y==100).Select(s=>s.Tile).SequenceEqual([InfrastructureGeometry.DeckTile]),"Native ground deck still contains a water sprite");
   var path=WalkabilityAnalyzer.Analyze(next,new(){X=64,Y=88,Width=80,Height=24,Start=new(75,100,0),End=new(125,100,0)});
   Check(path.Path.Count==51&&path.Path.All(c=>c.Y==100&&c.Z==0),"Flat approach roads do not connect across native deck");
   var rebuilt=LinearFeatureEditor.Rebuild(next,next.LinearFeatures);Check(rebuilt.Terrain[100*256+100]==Terrain.Water,"Rebuild painted away water");
   var removed=LinearFeatureEditor.Remove(rebuilt,"imported-bridge");Check(removed.Terrain.SequenceEqual(p.Terrain),"Removal did not restore source terrain");
   Check(NativeScene.Cell(removed,96,96,8,8,0).Sprites.Any(s=>s.X==100&&s.Y==100&&s.Tile=="blends_natural_02_0"),"Removal did not restore native water");
  });
  yield return("ground bridge native scene lotpack and navigation survive a cell boundary",()=>{
   var p=RiverMap(512,256);p=LinearFeatureEditor.Put(p,Bridge(256));
   foreach(var x in new[]{255,256}){var scene=NativeScene.Cell(p,x%256,96,1,8,0,cellX:x/256);Check(scene.Sprites.Where(s=>s.X==x%256&&s.Y==100).Select(s=>s.Tile).SequenceEqual([InfrastructureGeometry.DeckTile]),"Preview lost seam deck");}
   using(var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p))))foreach(var x in new[]{255,256}){
    Check(ReadSquare(zip,p.WorldX+x/256,p.WorldY,x%256,100,0).SequenceEqual([InfrastructureGeometry.DeckTile]),"LOT seam is not one walkable ground deck");
    Check((ReadNavigation(zip,p.WorldX+x/256,p.WorldY,x%256,100)&8)==0,"Native ground deck retains water navigation");
    Check((ReadNavigation(zip,p.WorldX+x/256,p.WorldY,x%256,110)&8)!=0,"Deck cleared navigation outside its footprint");
   }
   p=LinearFeatureEditor.Remove(p,"imported-bridge");using var restored=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));
   Check(ReadSquare(restored,p.WorldX+1,p.WorldY,0,100,0).Contains("blends_natural_02_0")&&(ReadNavigation(restored,p.WorldX+1,p.WorldY,0,100)&8)!=0,"Removed seam deck did not restore exported water");
  });
  yield return("level two bridge retains native water floor and water navigation below its deck",()=>{
   var p=RiverMap();var bridge=Bridge();bridge.Level=2;p=LinearFeatureEditor.Put(p,bridge);using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));
   Check(p.Terrain[100*256+100]==Terrain.Water&&ReadSquare(zip,p.WorldX,p.WorldY,100,100,0).Contains("blends_natural_02_0"),"Elevated bridge lost underlying water");
   Check(ReadSquare(zip,p.WorldX,p.WorldY,100,100,2).Contains(InfrastructureGeometry.DeckTile)&&(ReadNavigation(zip,p.WorldX,p.WorldY,100,100)&8)!=0,"Elevated bridge changed ground navigation");
  });
  yield return("ground bridges clip imported paths at map edges without changing railway limitations",()=>{
   var p=RiverMap();var bridge=Bridge();bridge.Points=[new(-20,100),new(280,100)];p=LinearFeatureEditor.Put(p,bridge);
   var tiles=InfrastructureGeometry.Build(p).Tiles;Check(tiles.Any(t=>t.X==0&&t.Y==100)&&tiles.Any(t=>t.X==255&&t.Y==100),"Imported edge crossing lost its deck");
   var rail=new LinearFeature{Kind="railway",SourceTags=new(){{"bridge","yes"},{"layer","2"}},Points=[new(70,100),new(130,100)]};var tracks=LinearFeatureEditor.Put(RiverMap(),rail);
   Check(!InfrastructureGeometry.HasFeatures(tracks)&&tracks.Terrain[100*256+100]==Terrain.Water,"Railway bridge invented a native floor");
   rail.Level=2;Reject(()=>LinearFeatureEditor.Put(RiverMap(),rail));
  });
  yield return("ground bridge retains props and edge fences and rejects incompatible floor replacements atomically",()=>{
   for(var kind=0;kind<6;kind++){
    var p=RiverMap();switch(kind){
     case 0:p.Decorations.Add(new(){X=80,Y=100,Asset="furniture_storage_01_0",Parts=[new(0,0,"furniture_storage_01_0",1)]});break;
     case 1:p.SurfaceTiles[100*256+80]="floors_interior_tilesandwood_01_40";break;
     case 2:p.Spawns=[new(){X=80,Y=100}];break;
     case 3:p.FenceStyles=[new(){Id="sheet",W="fencing_01_40",N="fencing_01_41"}];p.Fences=[new(){Style="sheet",Points=[new(80,100),new(84,100)]}];break;
     case 4:p.Buildings.Add(new(){X=76,Y=96,Width=8,Height=8});break;
     case 5:p.StaticModels.Add(new(){X=80,Y=100,ModelId="Base.Test",Profile="obstacle"});break;
    }
    var before=JsonSerializer.Serialize(p);
    if(kind is 1 or 4)Reject(()=>LinearFeatureEditor.Put(p,Bridge()));
    else{var next=LinearFeatureEditor.Put(p,Bridge());ProjectValidator.Validate(next);var scene=NativeScene.Cell(next,76,96,16,12,0);Check(scene.Sprites.Any(s=>s.X==80&&s.Y==100&&s.Tile==InfrastructureGeometry.DeckTile),"Deck floor lost");if(kind==0)Check(scene.Sprites.Any(s=>s.X==80&&s.Y==100&&s.Tile=="furniture_storage_01_0"),"Bridge erased an authored prop");if(kind==3)Check(scene.Sprites.Any(s=>s.Tile=="fencing_01_41"),"Bridge erased its fence railing");}
    Check(before==JsonSerializer.Serialize(p),"Bridge operation changed authored source");
   }
  });
  yield return("safe spawn skips a ground bridge on its dry approach including the map center",()=>{
   var p=RiverMap();p.Spawns=[];Check(MapGenerator.FindSafeSpawn(p) is {X:128,Y:128},"Spawn fixture does not target map center");
   var bridge=Bridge();bridge.Points=[new(110,128),new(150,128)];p=LinearFeatureEditor.Rebuild(p,[bridge]);
   var spawn=MapGenerator.FindSafeSpawn(p);Check(!LinearFeatureEditor.Rasterize(bridge,p.Width,p.Height).Contains(spawn.Y*p.Width+spawn.X),"Safe spawn selected the native ground deck");
   p.Spawns=[spawn];ProjectValidator.Validate(p);
  });
  yield return("shared ground bridge preserves section water with native floors on both sides of the seam",()=>{
   var a=RiverMap();a.WorldX=a.WorldY=0;var b=RiverMap();b.WorldX=1;b.WorldY=0;
   // Put the river on the world seam rather than in the middle of each section.
   Array.Fill(a.Terrain,Terrain.Grass);Array.Fill(b.Terrain,Terrain.Grass);
   for(var y=0;y<256;y++)for(var x=0;x<8;x++){a.Terrain[y*256+255-x]=Terrain.Water;b.Terrain[y*256+x]=Terrain.Water;}
   var source=new WorldProject{Maps=[new(){Project=a},new(){Project=b}]};var before=JsonSerializer.Serialize(source);var world=WorldInfrastructure.Put(source,Bridge(256));
   Check(JsonSerializer.Serialize(source)==before,"Shared bridge mutated source sections");
   foreach(var member in world.Maps){var p=WorldAssembler.ProjectSection(world,member);var x=p.WorldX==0?255:0;Check(p.LinearFeatures.Count==0&&p.Terrain[100*256+x]==Terrain.Water,"Shared projection painted water or duplicated ownership");Check(NativeScene.Cell(p,x,96,1,8,0).Sprites.Where(s=>s.X==x&&s.Y==100).Select(s=>s.Tile).SequenceEqual([InfrastructureGeometry.DeckTile]),"Shared ground bridge missing from native preview");}
   using(var zip=new ZipArchive(new MemoryStream(NativeWorldExporter.Export(world))))foreach(var x in new[]{255,256})Check(ReadSquare(zip,x/256,0,x%256,100,0).SequenceEqual([InfrastructureGeometry.DeckTile])&&(ReadNavigation(zip,x/256,0,x%256,100)&8)==0,"Shared export lost ground deck or retained water navigation");
   var removed=WorldInfrastructure.Remove(world,"imported-bridge");Check(JsonSerializer.Serialize(removed.Maps)==JsonSerializer.Serialize(source.Maps),"Shared removal changed section terrain");
   foreach(var member in removed.Maps){var p=WorldAssembler.ProjectSection(removed,member);var x=p.WorldX==0?255:0;Check(NativeScene.Cell(p,x,96,1,8,0).Sprites.Any(s=>s.X==x&&s.Y==100&&s.Tile=="blends_natural_02_0"),"Shared removal did not restore native water");}
  });
 }
 static MapProject RiverMap(int size=256,int riverX=100){var p=MapGenerator.Generate(new(){Kind="blank",Size=size});p.Spawns=[new(){X=20,Y=20}];for(var y=0;y<size;y++)for(var x=riverX-8;x<riverX+8;x++)p.Terrain[y*size+x]=Terrain.Water;for(var x=riverX-40;x<=riverX+40;x++)if(p.Terrain[100*size+x]!=Terrain.Water)p.Terrain[100*size+x]=Terrain.Road;return p;}
 static LinearFeature Bridge(int x=100)=>new(){Id="imported-bridge",Kind="bridge",Level=0,Width=4,SourceId="way/77",SourceTags=new(){{"bridge","yes"},{"highway","residential"},{"layer","2"}},Points=[new(x-30,100),new(x+30,100)]};
 static byte ReadNavigation(ZipArchive zip,int cx,int cy,int x,int y){using var r=new BinaryReader(zip.Entries.Single(e=>e.FullName.EndsWith($"/chunkdata_{cx}_{cy}.bin")).Open());Check(r.ReadByte()==0&&r.ReadByte()==1,"Navigation header");for(var yy=0;yy<32;yy++)for(var xx=0;xx<32;xx++){var tag=r.ReadByte();var bits=tag==2?r.ReadBytes(64):null;if(xx==x/8&&yy==y/8)return bits is null?(byte)(tag switch{0=>0,1=>1,3=>8,4=>16,5=>32,_=>throw new Exception("Navigation tag")}):bits[y%8*8+x%8];}throw new Exception("Navigation square missing");}
 static List<string> ReadSquare(ZipArchive zip,int cx,int cy,int x,int y,int z){using var h=new BinaryReader(zip.Entries.Single(e=>e.FullName.EndsWith($"/{cx}_{cy}.lotheader")).Open());Check(new string(h.ReadChars(4))=="LOTH"&&h.ReadInt32()==1,"LOT header");var tiles=Enumerable.Range(0,h.ReadInt32()).Select(_=>Line(h)).ToArray();Check(h.ReadInt32()==8&&h.ReadInt32()==8,"LOT dimensions");var min=h.ReadInt32();var max=h.ReadInt32();Check(z>=min&&z<=max,"Missing floor");using var stream=new MemoryStream();using(var input=zip.Entries.Single(e=>e.FullName.EndsWith($"/world_{cx}_{cy}.lotpack")).Open())input.CopyTo(stream);stream.Position=0;using var r=new BinaryReader(stream);Check(new string(r.ReadChars(4))=="LOTP"&&r.ReadInt32()==1&&r.ReadInt32()==1024,"LOT pack");var offsets=Enumerable.Range(0,1024).Select(_=>r.ReadInt64()).ToArray();stream.Position=offsets[x/8*32+y/8];var target=(z-min)*64+x%8*8+y%8;var at=0;while(at<=target){var count=r.ReadInt32();if(count==-1){at+=r.ReadInt32();if(at>target)return [];continue;}r.ReadInt32();var result=Enumerable.Range(1,count-1).Select(_=>tiles[r.ReadInt32()]).ToList();if(at++==target)return result;}return [];}
 static string Line(BinaryReader r){var bytes=new List<byte>();byte b;while((b=r.ReadByte())!=10)bytes.Add(b);return Encoding.ASCII.GetString(bytes.ToArray());}
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected ground bridge validation failure");}
}
