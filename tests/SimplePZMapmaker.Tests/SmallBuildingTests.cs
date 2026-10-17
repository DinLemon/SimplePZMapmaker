using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
public static class SmallBuildingTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("footprints under 6x6 generate one accessible room and export",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});
   foreach(var (w,h,kind) in new[]{(2,2,"warehouse"),(3,3,"house"),(5,4,"shop"),(3,8,"garage")}){
    var b=new Building{X=30+p.Buildings.Count*12,Y=30,Width=w,Height=h,Kind=kind};BuildingDesigner.Generate(b,1,42);
    var f=b.Design!.Floors.Single();Check(f.Cells.Length==w*h&&f.Cells.All(c=>c=='1')&&f.Rooms.Count==1,$"{w}x{h} is not a single room");
    Check(f.Edges.Any(e=>e.Kind=="door"),$"{w}x{h} has no entrance");p.Buildings.Add(b);
   }
   ProjectValidator.Validate(p);Check(p.Buildings.Single(b=>b.Kind=="shop").Design!.Floors[0].Rooms["1"]=="conveniencestore","A kiosk is not a shop room");
   BuildingDesigner.Regenerate(p);ProjectValidator.Validate(p);
   using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));Check(zip.Entries.Any(e=>e.Name.EndsWith(".lotheader")),"Small buildings did not export");
  });
  yield return("small footprints reject one-tile sides, odd shapes and stairs that cannot fit",()=>{
   Reject(()=>BuildingDesigner.Generate(new Building{Width=1,Height=4},1,42));
   Reject(()=>BuildingDesigner.Generate(new Building{Width=4,Height=4},1,42,"l"));
   Reject(()=>BuildingDesigner.Generate(new Building{Width=5,Height=5},2,42));
   Reject(()=>BuildingDesigner.Generate(new Building{Width=5,Height=5},1,42,"rect",1));
   Check(BuildingDesigner.HoldsStairs(4,10)&&!BuildingDesigner.HoldsStairs(5,5),"Stair fit rule changed");
  });
  yield return("a narrow footprint with a long side holds stairs and a basement",()=>{
   foreach(var (w,h) in new[]{(3,10),(12,2)}){
    var b=new Building{Width=w,Height=h,Kind="house"};BuildingDesigner.Generate(b,2,7,"rect",1);
    Check(b.Design!.MinLevel==-1&&b.Design.Floors.Count==3&&b.Design.Floors.Take(2).All(f=>f.Objects.Any(o=>o.Kind=="stairs")),$"{w}x{h} lost its stairs");
    Check(b.Design.Floors[0].Rooms["1"]=="storage","Basement room should be storage");
   }
  });
  yield return("imported small outlines keep their size and one floor",()=>{
   var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.01,South=54,North=54.01},MetresPerTile=1,OriginCellX=100,OriginCellY=100};var projection=new GeographicProjection(reference,100,100);
   double[] At(double x,double y){var g=projection.Geographic(x,y);return[g.Longitude,g.Latitude];}
   object Box(string id,double x,double y,double w,double h,Dictionary<string,string> tags)=>new{type="Feature",id,properties=tags,geometry=new{type="Polygon",coordinates=new[]{new[]{At(x,y),At(x+w,y),At(x+w,y+h),At(x,y+h),At(x,y)}}}};
   var json=JsonSerializer.Serialize(new{type="FeatureCollection",pzTransfer=1,features=new[]{
    Box("osm-way-1",40,40,3,3,new(){{"building","shed"}}),
    Box("osm-way-2",60,40,3,3,new(){{"building","kiosk"},{"shop","newsagent"},{"building:levels","2"}}),
    Box("osm-way-3",80,40,2,2,new(){{"building","yes"}})}});
   var p=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),reference.Bounds,projection:projection).Project;
   var shed=p.Buildings.Single(b=>b.SourceId=="geo-osm-way-1");var kiosk=p.Buildings.Single(b=>b.SourceId=="geo-osm-way-2");
   Check(shed.Width<=4&&shed.Height<=4&&shed.Design!.Floors.Single().Cells.Count(c=>c!='.')>=4,"Shed was inflated or lost");
   Check(kiosk.Width<=4&&kiosk.Height<=4&&kiosk.Kind=="shop"&&kiosk.Design!.Floors.Count==1&&kiosk.SourceLevels=="2",$"Kiosk floors were not limited or tags were lost: {kiosk.Width}x{kiosk.Height} {kiosk.Kind} {kiosk.Design!.Floors.Count} {kiosk.SourceLevels}");
   Check(p.ImportSummary!.Notes.Any(n=>n.Contains("one floor only")),"The single-floor limit is not explained");
   Check(p.Buildings.Any(b=>b.SourceId=="geo-osm-way-3"),"A 2x2 outline should be placed");
   ProjectValidator.Validate(p);
  });
  yield return("a small shared building crosses a seam without a 6x6 reservation",()=>{
   var w=WorldTests.Fixture(2);var b=new Building{X=100*256+254,Y=100*256+40,Width=4,Height=3,Kind="garage"};b.Design=BuildingDesigner.Generate(b,1,42);w.Buildings.Add(b);WorldAssembler.Validate(w);
   var right=WorldAssembler.ProjectSection(w,w.Maps[1]);Check(right.Buildings.Single().X==-2&&right.Buildings.Single().Width==4,"Projection of the small shared building is wrong");
  });
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected validation failure");}
}
