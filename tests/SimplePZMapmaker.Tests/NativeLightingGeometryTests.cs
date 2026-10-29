using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

public static class NativeLightingGeometryTests
{
 public static IEnumerable<(string,Action)> Cases()
 {
  yield return("lighting normalizes actual compound walls without closing window and door frames",()=>{
   Check(NativeLightingGeometry.TileFlags(Properties("WallNW"))==(NativeLightingGeometry.WallN|NativeLightingGeometry.WallW),"Compound corner wall lost");
   Check(NativeLightingGeometry.TileFlags(Properties("WindowN","collideN","transparentN"))==0,"Open window frame became an opaque wall");
   Check(NativeLightingGeometry.TileFlags(Properties("DoorWallW","collideW"))==0,"Door frame became an opaque wall");
   Check(NativeLightingGeometry.TileFlags(Properties("windowN","collideN","transparentN"))==NativeLightingGeometry.WindowN,"Native window not registered");
   Check(NativeLightingGeometry.TileFlags(Properties("solidfloor","transparentFloor"))==0,"Transparent floor blocks vertical light");
   Check(NativeLightingGeometry.TileFlags(Properties("solidtrans"))==0,"Translucent collision became opaque");
   Check((NativeLightingGeometry.TileFlags(Properties("stairsMN"))&NativeLightingGeometry.Elevated)!=0,"Middle stair elevation lost");
  });
  yield return("lighting scene is aligned bounded deterministic and read-only with 24 tile halo",()=>{
   var p=Blank();var before=JsonSerializer.Serialize(p);var scene=NativeLightingGeometry.FromProject(p,70,80,64,0,false,Tiles);
   Check(scene.WorldTileX%8==0&&scene.WorldTileY%8==0&&scene.ChunksX<=16&&scene.ChunksY<=16,"Native chunk bounds invalid");
   Check(scene.OutputX>=24&&scene.OutputY>=24&&scene.ChunksX*8-scene.OutputX-scene.Size>=24&&scene.ChunksY*8-scene.OutputY-scene.Size>=24,"Light radius halo is missing");
   Check(scene.Squares.Where(s=>s.Z==0).All(s=>(s.Flags&NativeLightingGeometry.OpenAir)!=0),"Uncovered terrain is not exterior");
   Check(JsonSerializer.Serialize(scene)==JsonSerializer.Serialize(NativeLightingGeometry.FromProject(p,70,80,64,0,false,Tiles)),"Lighting input changed between identical reads");
   Check(JsonSerializer.Serialize(p)==before,"Lighting preview mutated the project");
  });
  yield return("lighting includes authored basement lights doors and room power without model mesh opacity",()=>{
   var p=Blank();var b=new Building{X=50,Y=50,Width=12,Height=12};b.Design=BuildingDesigner.Generate(b,2,42,"rect",1);
   b.Design.Floors[0].StaticModels.Add(new(){ModelId="Base.Lamp",Profile="light",X=2,Y=2,Z=-1,Light=new(){Rgb=[.2,.6,1],Radius=8}});p.Buildings.Add(b);
   p.StaticModels.Add(new(){ModelId="Base.Door",Profile="door",X=80,Y=80,Door=new(){Direction="n",Open=true}});
   p.StaticModels.Add(new(){ModelId="Base.Object",Profile="obstacle",X=82,Y=80,Footprint=[new(0,0),new(1,0)]});
   var scene=NativeLightingGeometry.FromProject(p,48,48,40,-1,true,Tiles);
   Check(scene.MinZ== -1&&scene.PointLights.Single() is {X:2612,Y:2612,Z:-1,Radius:8,R:.2,G:.6,B:1,Active:true},"Authored light floor, colors or coordinates changed");
   Check(scene.Squares.Where(s=>s.Z<0).All(s=>(s.Flags&NativeLightingGeometry.OpenAir)==0),"Basement received direct sky exposure");
   var door=scene.Squares.Single(s=>s.X==2640-scene.WorldTileX&&s.Y==2640-scene.WorldTileY&&s.Z==0);
   Check((door.Flags&(NativeLightingGeometry.DoorN|NativeLightingGeometry.OpenN))==(NativeLightingGeometry.DoorN|NativeLightingGeometry.OpenN)&&(door.Flags&NativeLightingGeometry.WallN)==0,"Open model door has an invented solid wall");
   var obstacle=scene.Squares.Single(s=>s.X==2642-scene.WorldTileX&&s.Y==2640-scene.WorldTileY&&s.Z==0);Check((obstacle.Flags&NativeLightingGeometry.Opaque)==0,"Physical model obstacle became a fake optical wall");
   Check(scene.RoomLights.Count>0&&scene.RoomLights.All(r=>r.Active)&&scene.RoomLights.Any(r=>r.Z<0),"Room power assumption or basement room missing");
   var off=NativeLightingGeometry.FromProject(p,48,48,40,-1,false,Tiles);Check(off.RoomLights.All(r=>!r.Active)&&off.PointLights.Single().Active,"Room power changed independent model light state");
  });
  yield return("world lighting includes adjacent sections and both halves of shared building geometry",()=>{
   var a=Blank();var b=Blank();b.WorldX=11;b.StaticModels.Add(new(){ModelId="Base.Lamp",Profile="light",X=3,Y=60,Light=new(){Rgb=[0,1,0],Radius=10}});
   var first=new WorldMapMember{Project=a};var second=new WorldMapMember{Project=b};var shared=new Building{X=2810,Y=2600,Width=16,Height=16};shared.Design=BuildingDesigner.Generate(shared,1,42);
   var world=new WorldProject{Maps=[first,second],Buildings=[shared]};var before=JsonSerializer.Serialize(world);
   var scene=NativeLightingGeometry.FromWorld(world,first.Id,240,40,16,0,true,Tiles);
   Check(scene.PointLights.Any(l=>l.X==2819&&l.Y==2620),"Neighbor's light omitted at section seam");
   var roomSquares=scene.Squares.Where(s=>s.Z==0&&s.RoomId>=0).Select(s=>s.X+scene.WorldTileX).ToArray();Check(roomSquares.Any(v=>v<2816)&&roomSquares.Any(v=>v>=2816),"Shared building was clipped to selected section");
   Check(scene.RoomLights.Any(r=>r.X<2816)&&scene.RoomLights.Any(r=>r.X>=2816),"Neighbor room light rectangle lost");Check(JsonSerializer.Serialize(world)==before,"World lighting mutated shared geometry");
  });
  yield return("lighting refuses invalid crops and cancels without inventing empty signed-floor terrain",()=>{
   var p=Blank();Reject(()=>NativeLightingGeometry.FromProject(p,200,200,64,0,false,Tiles));Reject(()=>NativeLightingGeometry.FromProject(p,30,30,65,0,false,Tiles));Reject(()=>NativeLightingGeometry.FromProject(p,30,30,32,-33,false,Tiles));
   var scene=NativeLightingGeometry.FromProject(p,30,30,32,-32,false,Tiles);Check(scene.Squares.All(s=>s.Z==0),"Empty basement became a synthetic floor");
   using var source=new CancellationTokenSource();source.Cancel();try{NativeLightingGeometry.FromProject(p,30,30,32,0,false,Tiles,source.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
  });
  yield return("linear river editing and geographic reimport protect the full model footprint",()=>{
   var p=Blank();p.StaticModels.Add(new(){ModelId="Base.Object",Profile="obstacle",X=40,Y=40,Footprint=[new(0,0),new(1,0),new(2,0)]});
   var changed=LinearFeatureEditor.Put(p,new(){Kind="river",Width=2,Points=[new(42,35),new(42,50)]});Check(Enumerable.Range(40,3).All(x=>changed.Terrain[40*256+x]==Terrain.Grass),"River flooded a non-anchor model footprint cell");
   var bridge=Blank();for(var xx=44;xx<46;xx++)bridge.Terrain[40*256+xx]=Terrain.Water;bridge=LinearFeatureEditor.Put(bridge,new(){Id="bridge-test",Kind="bridge",Width=2,Points=[new(38,40),new(46,40)]});bridge.StaticModels.Add(new(){ModelId="Base.Object",Profile="obstacle",X=40,Y=40,Footprint=[new(0,0),new(1,0),new(2,0)]});bridge=LinearFeatureEditor.Remove(bridge,"bridge-test");Check(Enumerable.Range(40,3).All(x=>bridge.Terrain[40*256+x]==Terrain.Road),"Restoring bridge underlay flooded an occupied model footprint");
   var json="{\"type\":\"FeatureCollection\",\"features\":[{\"type\":\"Feature\",\"properties\":{\"natural\":\"water\"},\"geometry\":{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,1],[0,0]]]}}]}";
   var imported=GeographicImporter.Import(json,p,new(){West=0,East=.01,South=0,North=.01},preserveEdits:true).Project;Check(Enumerable.Range(40,3).All(x=>imported.Terrain[40*256+x]==Terrain.Grass),"Geographic reimport flooded model footprint");
  });
 }
 static MapProject Blank(){var p=MapGenerator.Generate(new(){Kind="blank"});p.WorldX=p.WorldY=10;return p;}
 static IReadOnlyDictionary<string,string> Properties(params string[] keys)=>keys.ToDictionary(k=>k,_=>"");
 static IReadOnlyDictionary<string,string> Tiles(string id){
  if(id.StartsWith("floors_")||id.StartsWith("blends_"))return Properties("solidfloor");
  if(id.StartsWith("walls_exterior_house_01_")){var n=int.Parse(id[(id.LastIndexOf('_')+1)..])%16;return n switch{0 or 4=>Properties("WallW"),1 or 5=>Properties("WallN"),2 or 6=>Properties("WallNW"),8=>Properties("WindowW"),9=>Properties("WindowN"),10=>Properties("DoorWallW"),11=>Properties("DoorWallN"),_=>Properties()};}
  if(id.StartsWith("fixtures_doors_"))return Properties(id.EndsWith("_0")?"doorW":"doorN");if(id.StartsWith("fixtures_windows_"))return Properties(id.EndsWith("_0")?"windowW":"windowN");return Properties();
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected lighting validation failure");}
}
