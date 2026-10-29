using System.IO.Compression;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

public static class LargeBuildingTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("large footprint generation accepts schools and factories up to 512 tiles",()=>{
   foreach(var (width,height,kind) in new[]{(87,92,"school"),(66,65,"school"),(110,27,"shop"),(254,109,"industrial"),(256,256,"warehouse"),(263,24,"industrial"),(512,512,"warehouse")}){
    var b=new Building{Width=width,Height=height,Kind=kind};BuildingDesigner.Generate(b,1,42);
    Check(b.Design!.Floors[0].Cells.Length==width*height,"Large footprint was resized or truncated");BuildingDesigner.Validate(b);
   }
   Reject(()=>BuildingDesigner.Generate(new(){Width=513,Height=10},1,42),"dimensions");
  });
  yield return("large footprint floor budget rejects before generation and retains legacy capacity",()=>{
   var b=new Building{Width=256,Height=256};Reject(()=>BuildingDesigner.Generate(b,5,42),"floor");Check(b.Design is null,"Over-budget generation mutated the building");
   b.Design=new(){Roof="none",Floors=Enumerable.Range(0,5).Select(_=>new BuildingFloor{Cells=new string('1',65536),Rooms=new(){["1"]="warehouse"}}).ToList()};
   Reject(()=>BuildingDesigner.Validate(b,false),"floor");
   var legacy=new Building{Width=64,Height=64,Design=new(){MinLevel=-32,Roof="none",Floors=Enumerable.Range(0,64).Select(_=>new BuildingFloor{Cells=new string('1',4096),Rooms=new(){["1"]="warehouse"}}).ToList()}};
   BuildingDesigner.Validate(legacy,false);
  });
  yield return("large footprint source import keeps identity and original evidence",()=>{
   foreach(var (id,width,height,kind) in new[]{(1504085667L,87,92,"school"),(161025204L,66,65,"school"),(160914660L,110,27,"retail"),(828793794L,254,109,"industrial")}){
    var reference=Reference();var json=Source(reference,id,40.25,40.25,width,height,kind,1);
    var p=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank",Size=512}),reference.Bounds,projection:new(reference,100,100)).Project;
    Check(p.Buildings.Count==1,$"Large source {id} skipped: {JsonSerializer.Serialize(p.ImportSummary!.SkipReasons)}");var b=p.Buildings.Single();
    Check(b.SourceId==$"osm-way-{id}"&&b.SourceLevels=="1"&&b.SourceOutline.Single().Count==5&&b.FootprintOrigin=="source-grid","Source identity/outline lost");
    Check(p.SourceFeatures.Single().Tags["source:attribution"]=="Large footprint regression survey","Provenance lost");ProjectValidator.Validate(p);
    var loaded=JsonSerializer.Deserialize<MapProject>(JsonSerializer.Serialize(p,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;
    Check(loaded.Version==16&&loaded.Buildings[0].SourceId==b.SourceId,"Large footprint format/identity did not roundtrip");ProjectValidator.Validate(loaded);
   }
  });
  yield return("large footprint importer retains over-budget source without aborting",()=>{
   var r=Reference();var p=GeographicImporter.Import(Source(r,1,30.25,30.25,255,255,"industrial",5),MapGenerator.Generate(new(){Kind="blank",Size=512}),r.Bounds,projection:new(r,100,100)).Project;
   Check(p.Buildings.Count==0&&p.SourceFeatures.Count==1&&p.ImportSummary!.SkipReasons.Keys.Any(s=>s.Contains("floor",StringComparison.OrdinalIgnoreCase)),"Over-budget source was not retained with a floor-budget reason");
  });
  yield return("large footprint shared import is stored once and roundtrips world format",()=>{
   var w=WorldTests.Fixture(2);w.GeoReference=Reference();var json=Source(w.GeoReference,160914660,220.25,40.25,110,27,"retail",1);
   var result=GeographicImporter.ImportWorldBuildings(json,w,42,[]);Check(result.Added==1&&w.Buildings.Count==1,"Large seam building was dropped");
   var again=GeographicImporter.ImportWorldBuildings(json,w,42,[]);Check(again.Added==0&&w.Buildings.Single().SourceId=="osm-way-160914660","Shared source duplicated or changed identity");
   var loaded=JsonSerializer.Deserialize<WorldProject>(JsonSerializer.Serialize(w,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;Check(loaded.Version==11,"Shared large footprint did not use world format 11");WorldAssembler.Validate(loaded);
  });
  yield return("large footprint native export clips rooms across four cells without lost area",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank",Size=512});p.Spawns=[new(){X=20,Y=20}];var b=new Building{X=220,Y=220,Width=90,Height=90,Kind="industrial"};BuildingDesigner.Generate(b,1,42);p.Buildings.Add(b);
   using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));var area=0;var occupiedCells=0;
   foreach(var e in zip.Entries.Where(e=>e.Name.EndsWith(".lotheader"))){using var reader=new BinaryReader(e.Open());reader.ReadBytes(8);var tiles=reader.ReadInt32();for(var i=0;i<tiles;i++)Line(reader);reader.ReadBytes(16);var rooms=reader.ReadInt32();if(rooms>0)occupiedCells++;
    for(var i=0;i<rooms;i++){Line(reader);reader.ReadInt32();var rects=reader.ReadInt32();for(var j=0;j<rects;j++){var x=reader.ReadInt32();var y=reader.ReadInt32();var width=reader.ReadInt32();var height=reader.ReadInt32();Check(x>=0&&y>=0&&width>0&&height>0&&x+width<=256&&y+height<=256,"Room escaped native cell");area+=width*height;}Check(reader.ReadInt32()==0,"Unexpected room objects");}}
   Check(occupiedCells==4&&area==8100,$"Large native rooms lost/duplicated area: {occupiedCells} cells, {area} tiles");
  });
 }
 static WorldGeoReference Reference()=>new(){Bounds=new(){West=32,East=32.01,South=54,North=54.01},MetresPerTile=1,OriginCellX=100,OriginCellY=100};
 static string Source(WorldGeoReference r,long id,double x,double y,int width,int height,string kind,int floors){var projection=new GeographicProjection(r,100,100);object At(double a,double b){var g=projection.Geographic(a,b);return new{lon=g.Longitude,lat=g.Latitude};}return JsonSerializer.Serialize(new{elements=new[]{new{type="way",id,tags=new Dictionary<string,string>{{"building",kind},{"building:levels",floors.ToString()},{"source:attribution","Large footprint regression survey"}},geometry=new[]{At(x,y),At(x+width,y),At(x+width,y+height),At(x,y+height),At(x,y)}}}});}
 static void Line(BinaryReader r){while(r.ReadByte()!=10){}}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Reject(Action action,string reason){try{action();}catch(ArgumentException e){Check(e.Message.Contains(reason,StringComparison.OrdinalIgnoreCase),"Wrong rejection: "+e.Message);return;}throw new Exception("Expected validation failure");}
}
