using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

static class NativeRoadRampTests
{
 public static IEnumerable<(string,Action)> Cases()
 {
  yield return ("native road ramps have full width and continuous integer segments in all directions and levels",()=>{
   var seen=new HashSet<string>();
   foreach(var (dx,dy,start,reverse) in new[]{(1,0,72,48),(-1,0,48,72),(0,1,24,0),(0,-1,0,24)})
   foreach(var level in new[]{1,2})foreach(var width in new[]{1,4,16}){
    var p=Blank();var f=Feature(dx,dy,level,width);p=LinearFeatureEditor.Put(p,f);var g=InfrastructureGeometry.Build(p);
    var ramps=g.Tiles.Where(t=>t.Id.StartsWith("ramps_01_")).ToList();
    Check(ramps.Count==40*level*width,"Missing full-width approach segments");
    Check(g.Holes.Count==0&&g.Tiles.All(t=>!t.Artwork),"Ramp was emitted as artwork or a stair shaft");
    for(var at=0;at<40*level+2;at++)for(var lane=0;lane<width;lane++){
     var cross=lane-width/2;var x=100+dx*at+(dx==0?cross:0);var y=100+dy*at+(dy==0?cross:0);
     var floor=g.Tiles.Where(t=>t.X==x&&t.Y==y).ToArray();Check(floor.Length==1,"A slope has an extra flat floor");
     var t=floor[0];
     if(at>=20*level&&at<20*level+2)Check(t.Z==level&&t.Id==InfrastructureGeometry.DeckTile,"Upper deck landing has wrong elevation");
     else{var run=at<20*level?at:40*level+1-at;var expected=(at<20*level?start:reverse)+run%20;Check(t.Z==run/20&&t.Id==$"ramps_01_{expected}",$"Discontinuous slope {dx},{dy} level {level} at {at}: {t}");seen.Add(t.Id);}
    }
    Check(LinearFeatureEditor.Rasterize(f,256,256).Count==(40*level+2)*width,"Reserved footprint differs from full-width geometry");
   }
   Check(seen.Count==80,"Not all 80 native direction/segment IDs were emitted");
  });
  yield return ("native road ramps rails stay on flat deck sides and leave both joins open",()=>{
   foreach(var (dx,dy) in new[]{(1,0),(-1,0),(0,1),(0,-1)}){
    var f=Feature(dx,dy,2,4);f.GuardRails=true;var p=LinearFeatureEditor.Put(Blank(),f);var g=InfrastructureGeometry.Build(p);var rails=g.Tiles.Where(t=>t.Id.StartsWith("fencing_")).ToList();
    Check(rails.Count==4&&rails.All(t=>t.Z==2),"Flat rails floated over inclines or capped joins");
    foreach(var t in rails){var at=dx!=0?(t.X-100)*dx:(t.Y-100)*dy;Check(at is 40 or 41,"Rail crossed an approach/deck join");Check(t.Flags==(dx!=0?2:4),"Rail crosses driving direction");}
   }
  });
  yield return ("native road ramps reject unsupported profiles atomically",()=>{
   foreach(var change in new Action<LinearFeature>[] {f=>f.Level=-1,f=>f.Level=0,f=>f.Level=3,f=>f.Width=1.5,f=>f.Width=17,f=>f.Kind="sidewalk",f=>f.Interpolation="catmull-rom",f=>f.Points.Add(new(150,100)),f=>f.Points[1]=new(142,101),f=>f.Points[1]=new(140,100),f=>f.DeckArtwork.Add(new(120,100,"blends_street_01_1"))}){
    var p=Blank();var before=JsonSerializer.Serialize(p);var f=Feature(1,0,1,4);change(f);Reject(()=>LinearFeatureEditor.Put(p,f));Check(JsonSerializer.Serialize(p)==before,"Rejected profile changed source");
   }
  });
  yield return ("native road ramps reject occupied approaches at every storey without changing source",()=>{
   foreach(var at in new[]{0,19,20,39,42,61,62,81})foreach(var obstruct in new Action<MapProject,int>[] {
    (p,i)=>p.Terrain[i]=Terrain.Water,(p,i)=>p.SurfaceTiles[i]="blends_street_01_1",
    (p,i)=>p.Decorations.Add(new(){X=i%256,Y=i/256,Asset="street_decoration_01_1",Parts=[new(0,0,"street_decoration_01_1")]}),
    (p,i)=>p.Spawns.Add(new(){X=i%256,Y=i/256}),
    (p,i)=>p.StaticModels.Add(new(){X=i%256,Y=i/256,ModelId="Base.Test"}),
    (p,i)=>TerrainEditMask.Mark(p,i),
    (p,i)=>p.Buildings.Add(new(){X=i%256,Y=i/256,Width=4,Height=4})}){
    var p=Blank();obstruct(p,100*256+100+at);var before=JsonSerializer.Serialize(p);Reject(()=>LinearFeatureEditor.Put(p,Feature(1,0,2,4)));Check(JsonSerializer.Serialize(p)==before,"Rejected obstruction changed source");
   }
  });
  yield return ("native road ramps preserve ground roads and water below the flat deck without support piers",()=>{
   var p=Blank();for(var y=90;y<110;y++)p.Terrain[y*256+140]=Terrain.Road;p.Terrain[100*256+141]=Terrain.Water;var terrain=p.Terrain.ToArray();var f=Feature(1,0,2,4);f.SupportSpacing=4;
   var n=LinearFeatureEditor.Put(p,f);var g=InfrastructureGeometry.Build(n);Check(n.Terrain.SequenceEqual(terrain)&&n.LinearFeatures.Single().Underlay.Count==0,"Ramp repainted underlying terrain");Check(!g.Tiles.Any(t=>t.X==140&&t.Z<2&&t.Id.StartsWith("walls_")),"Pier blocks the underlying road");Check(LinearFeatureEditor.Remove(n,f.Id).Terrain.SequenceEqual(terrain),"Removal changed terrain");
  });
  yield return ("native road ramps share local and world geometry across 255 256 without reset",()=>{
   foreach(var level in new[]{1,2}){
    var original=World();var f=new LinearFeature{Id="seam-ramp",Kind="bridge",Access="ramps",Level=level,Width=4,GuardRails=true,Points=[new(240,100),new(240+40*level+1,100)]};var source=JsonSerializer.Serialize(original);var w=WorldInfrastructure.Put(original,f);Check(JsonSerializer.Serialize(original)==source,"World placement mutated source");
    var local=Blank(512);local=LinearFeatureEditor.Put(local,f);var want=InfrastructureGeometry.Build(local).Tiles.ToHashSet();var actual=new HashSet<InfrastructureGeometry.Tile>();
    foreach(var m in w.Maps){var p=WorldAssembler.ProjectSection(w,m);foreach(var t in InfrastructureGeometry.Build(p).Tiles)actual.Add(t with{X=t.X+256*p.WorldX,Y=t.Y+256*p.WorldY});}
    Check(actual.SetEquals(want),"World clipping changed the common profile");Check(actual.Contains(new(255,100,0,"ramps_01_87"))&&actual.Contains(new(256,100,0,"ramps_01_88")),"Height reset at 255/256");Check(WorldInfrastructure.Rasterize(f).Count==(40*level+2)*4,"World reservation omits approaches");
   }
  });
  yield return ("native road ramps reject sparse world gaps budgets and far-edge obstructions atomically",()=>{
   var w=World();var before=JsonSerializer.Serialize(w);Reject(()=>WorldInfrastructure.Put(w,new(){Id="gap",Kind="road",Access="ramps",Level=1,Width=4,Points=[new(490,100),new(550,100)]}));Check(JsonSerializer.Serialize(w)==before,"Gap rejection mutated world");
   w.Maps[1].Project.SurfaceTiles[100*256+5]="blends_street_01_1";before=JsonSerializer.Serialize(w);Reject(()=>WorldInfrastructure.Put(w,new(){Id="blocked",Kind="road",Access="ramps",Level=2,Width=4,Points=[new(240,100),new(321,100)]}));Check(JsonSerializer.Serialize(w)==before,"Remote approach collision changed world");
   Reject(()=>WorldInfrastructure.Rasterize(new(){Id="budget",Access="ramps",Level=1,Width=16,Points=[new(100,100),new(10000,100)]}));
  });
  yield return ("native road ramps independent LOT readback preserves all directions segment20 upper joins and first floor",()=>{
   var p=Blank();var f=Feature(1,0,2,2);p=LinearFeatureEditor.Put(p,f);p=LinearFeatureEditor.Put(p,new(){Id="vertical",Kind="bridge",Level=1,Access="ramps",Width=2,Points=[new(50,100),new(50,141)]});
   using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));
   foreach(var (x,y,z,id) in new[]{(100,100,0,"ramps_01_72"),(119,100,0,"ramps_01_91"),(120,100,1,"ramps_01_72"),(139,100,1,"ramps_01_91"),(140,100,2,InfrastructureGeometry.DeckTile),(142,100,1,"ramps_01_67"),(181,100,0,"ramps_01_48"),(50,100,0,"ramps_01_24"),(50,119,0,"ramps_01_43"),(50,120,1,InfrastructureGeometry.DeckTile),(50,122,0,"ramps_01_19"),(50,141,0,"ramps_01_0")}){
    var square=ReadSquare(zip,x,y,z);Check(square.Count>0&&square[0]==id,$"Native first floor {x},{y},{z} expected {id}, got {string.Join(',',square)}");if(id.StartsWith("ramps_"))Check(!square.Contains(InfrastructureGeometry.DeckTile),"Native slope has an overriding flat deck");
   }
   for(var step=0;step<20;step++)foreach(var (x,y,z,id) in new[]{(100+step,100,0,$"ramps_01_{72+step}"),(181-step,100,0,$"ramps_01_{48+step}"),(50,100+step,0,$"ramps_01_{24+step}"),(50,141-step,0,$"ramps_01_{step}")}){
    var square=ReadSquare(zip,x,y,z);Check(square.Count>0&&square[0]==id,$"Native index {id} missing or not first floor");
   }
  });
  yield return ("native road ramps exact footprint accepts boundary width one",()=>{
   var f=new LinearFeature{Id="edge",Kind="road",Access="ramps",Level=1,Width=1,Points=[new(0,0),new(41,0)]};var n=LinearFeatureEditor.Put(Blank(),f);var g=InfrastructureGeometry.Build(n);Check(g.Tiles.Count==42&&g.Tiles.All(t=>t.Y==0)&&g.Tiles.Any(t=>t.X==0&&t.Id=="ramps_01_72")&&g.Tiles.Any(t=>t.X==41&&t.Id=="ramps_01_48"),"Cell anchors or width-one footprint drifted");
   f.Width=4;Reject(()=>LinearFeatureEditor.Put(Blank(),f));
  });
  yield return ("native road ramps reject fractional point coordinates without upstream rounding",()=>{
   foreach(var points in new List<LinearPoint>[] {[new(100.5,100),new(141.5,100)],[new(100,100.5),new(141,100.5)],[new(100,100),new(141.5,100)],[new(100,100),new(100,141.5)]}){
    var p=Blank();var before=JsonSerializer.Serialize(p);var f=Feature(1,0,1,4);f.Points=points;Reject(()=>LinearFeatureEditor.Put(p,f));Reject(()=>RoadRampGeometry.ValidateProfile(f));Check(before==JsonSerializer.Serialize(p),"Fractional profile changed source");
   }
  });
  yield return ("native road ramps reject authored fence cells adjacent faces and gate faces atomically",()=>{
   foreach(var points in new List<FencePoint>[] {[new(105,100),new(106,100)],[new(105,100),new(105,101)],[new(105,102),new(106,102)],[new(182,100),new(182,101)],[new(125,100),new(126,100)],[new(125,102),new(126,102)]})foreach(var gate in new[]{false,true}){
    var p=Blank();p.FenceStyles=[new(){Id="sheet",W="fencing_01_40",N="fencing_01_41"}];p.GateStyles=[new(){Id="gate",Width=1,ClosedW=["fixtures_doors_fences_01_12"],ClosedN=["fixtures_doors_fences_01_13"]}];p.Fences=[new(){Id="blocked",Style="sheet",Points=points,Gates=gate?[new(){At=0,Style="gate"}]:[]}];var before=JsonSerializer.Serialize(p);
    Reject(()=>LinearFeatureEditor.Put(p,Feature(1,0,2,4)));Check(before==JsonSerializer.Serialize(p),"Authored fence rejection changed source");
   }
   var allowed=Blank();allowed.FenceStyles=[new(){Id="sheet",W="fencing_01_40",N="fencing_01_41"}];allowed.Fences=[new(){Id="clear",Style="sheet",Points=[new(105,103),new(106,103)]}];var n=LinearFeatureEditor.Put(allowed,Feature(1,0,2,4));Check(n.Fences.Count==1,"Unrelated fence was removed or blocked placement");
  });
  yield return ("native road ramps reject shared fence face outside approach section boundary",()=>{
   var w=World();w.FenceStyles=[new(){Id="sheet",W="fencing_01_40",N="fencing_01_41"}];w.Fences=[new(){Id="boundary",Style="sheet",Points=[new(256,100),new(256,101)]}];var before=JsonSerializer.Serialize(w);
   Reject(()=>WorldInfrastructure.Put(w,new(){Id="boundary-ramp",Kind="road",Level=2,Access="ramps",Width=4,Points=[new(174,100),new(255,100)]}));Check(before==JsonSerializer.Serialize(w),"Shared fence rejection changed world");
  });
  yield return ("native road ramps recognize only real native slope records and keep planner tiles structural",()=>{
   for(var index=0;index<256;index++){var expected=index is >=0 and <=19 or >=24 and <=43 or >=48 and <=67 or >=72 and <=91;Check(RoadRampGeometry.IsRampTile($"ramps_01_{index}")==expected,"Padding classified as a slope");}
   foreach(var id in new[]{"ramps_01_00","ramps_01_+1","ramps_01_1x","ramps_01_-1","other_01_0"})Check(!RoadRampGeometry.IsRampTile(id),"Noncanonical native slope ID accepted");
   var plan=RoadRampGeometry.Plan(Feature(1,0,2,4));Check(plan.Footprint.Count==328&&plan.Approaches.Count==320&&plan.Deck.Count==8&&plan.Geometry.Tiles.All(t=>!t.Artwork)&&plan.Geometry.Holes.Count==0,"Planner contract differs from structural geometry");
  });
  yield return ("native road ramps reject floor and stair intersections and mod-overridden structural IDs",()=>{
   var p=LinearFeatureEditor.Put(Blank(),new(){Id="other",Kind="bridge",Level=1,Width=4,Points=[new(120,95),new(120,105)]});Reject(()=>LinearFeatureEditor.Put(p,Feature(1,0,2,4)));
   p=Blank();p.AssetSources["ramps_01_72"]="custom-mod";var before=JsonSerializer.Serialize(p);Reject(()=>LinearFeatureEditor.Put(p,Feature(1,0,1,4)));Check(before==JsonSerializer.Serialize(p),"Provider conflict mutated source");
  });
  yield return ("native road ramps independent world LOT readback retains seam slope and upper transition",()=>{
   var w=WorldInfrastructure.Put(World(),new(){Id="seam-native",Kind="road",Level=2,Access="ramps",Width=2,Points=[new(240,100),new(321,100)]});using var zip=new ZipArchive(new MemoryStream(NativeWorldExporter.Export(w)));
   foreach(var (x,z,id) in new[]{(255,0,"ramps_01_87"),(256,0,"ramps_01_88"),(259,0,"ramps_01_91"),(260,1,"ramps_01_72"),(279,1,"ramps_01_91"),(280,2,InfrastructureGeometry.DeckTile),(282,1,"ramps_01_67")}){var square=ReadSquare(zip,x,100,z);Check(square.Count>0&&square[0]==id,$"World seam native floor {x},{z} expected {id}");}
  });
  yield return ("native road ramps reject stairs supports and guard faces in both placement orders locally and shared",()=>{
   var cases=new (LinearFeature Other,LinearFeature Ramp)[]{
    (new(){Id="stairs",Kind="bridge",Width=1,Level=1,Access="stairs",Points=[new(110,97),new(110,80)]},Feature(1,0,1,1)),
    (new(){Id="pierdeck",Kind="bridge",Width=2,Level=2,SupportSpacing=4,Points=[new(104,100),new(110,100)]},Feature(1,0,1,4)),
    (new(){Id="guarddeck",Kind="bridge",Width=1,Level=1,GuardRails=true,Points=[new(115,100),new(120,100)]},Feature(1,0,2,4)),
    (new(){Id="adjacent-support",Kind="bridge",Width=2,Level=2,SupportSpacing=4,Points=[new(104,102),new(110,102)]},Feature(1,0,1,1))
   };
   foreach(var (other,ramp) in cases)foreach(var reverse in new[]{false,true}){
    var first=reverse?ramp:other;var second=reverse?other:ramp;
    var p=LinearFeatureEditor.Put(Blank(),first);var before=JsonSerializer.Serialize(p);Reject(()=>LinearFeatureEditor.Put(p,second));Check(JsonSerializer.Serialize(p)==before,"Rejected structural collision changed the local source");
    var w=WorldInfrastructure.Put(World(),first);before=JsonSerializer.Serialize(w);Reject(()=>WorldInfrastructure.Put(w,second));Check(JsonSerializer.Serialize(w)==before,"Rejected structural collision changed the shared source");
   }
  });
  yield return ("native road ramps legacy none and stairs keep existing access geometry",()=>{
   foreach(var access in new[]{"none","stairs"})foreach(var level in new[]{-2,2}){var p=LinearFeatureEditor.Put(Blank(),new(){Id="legacy",Kind="bridge",Level=level,Access=access,Width=4,Points=[new(50,100),new(100,100)]});var g=InfrastructureGeometry.Build(p);Check(g.Tiles.All(t=>!t.Id.StartsWith("ramps_")),"Legacy access became a ramp");Check(g.Holes.Count==(access=="stairs"?6:0),"Legacy stair opening count changed");}
  });
 }
 static MapProject Blank(int size=256){var p=MapGenerator.Generate(new(){Kind="blank",Size=size});p.WorldX=p.WorldY=0;return p;}
 static WorldProject World(){var a=Blank();var b=Blank();b.WorldX=1;return new(){Maps=[new(){Project=a},new(){Project=b}]};}
 static LinearFeature Feature(int dx,int dy,int level,int width)=>new(){Id="ramp",Kind="bridge",Access="ramps",Level=level,Width=width,Points=[new(100,100),new(100+dx*(40*level+1),100+dy*(40*level+1))]};
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected ramp rejection");}
 // Independent format reader: no exporter/composer helpers, reads native square order verbatim.
 static List<string> ReadSquare(ZipArchive zip,int x,int y,int z){
  using var h=new BinaryReader(zip.Entries.Single(e=>e.FullName.EndsWith($"/{x/256}_{y/256}.lotheader")).Open());Check(new string(h.ReadChars(4))=="LOTH"&&h.ReadInt32()==1,"Header");var tiles=Enumerable.Range(0,h.ReadInt32()).Select(_=>Line(h)).ToArray();Check(h.ReadInt32()==8&&h.ReadInt32()==8,"Dimensions");var min=h.ReadInt32();var max=h.ReadInt32();Check(z>=min&&z<=max,"Z range");
  using var stream=new MemoryStream();using(var input=zip.Entries.Single(e=>e.FullName.EndsWith($"/world_{x/256}_{y/256}.lotpack")).Open())input.CopyTo(stream);stream.Position=0;using var r=new BinaryReader(stream);Check(new string(r.ReadChars(4))=="LOTP"&&r.ReadInt32()==1&&r.ReadInt32()==1024,"Pack");var offsets=Enumerable.Range(0,1024).Select(_=>r.ReadInt64()).ToArray();stream.Position=offsets[(x%256/8)*32+y%256/8];var target=(z-min)*64+x%8*8+y%8;var at=0;
  while(at<=target){var count=r.ReadInt32();if(count==-1){at+=r.ReadInt32();if(at>target)return [];continue;}r.ReadInt32();var result=Enumerable.Range(1,count-1).Select(_=>tiles[r.ReadInt32()]).ToList();if(at++==target)return result;}return [];
 }
 static string Line(BinaryReader r){var bytes=new List<byte>();byte b;while((b=r.ReadByte())!=10)bytes.Add(b);return Encoding.ASCII.GetString(bytes.ToArray());}
}
