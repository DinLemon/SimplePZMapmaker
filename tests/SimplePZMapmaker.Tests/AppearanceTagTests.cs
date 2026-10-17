using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
public static class AppearanceTagTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("appearance tags set style roof colour wall set trim and kind on imported buildings",()=>{
   var p=Import(new(){{"building","house"},{"pz:style","brick"},{"roof:shape","gabled"},{"roof:colour","red"},{"pz:wall","set:walls_exterior_house_02_32"},{"pz:trim","fixtures_windows_detailing_01_1:fixtures_windows_detailing_01_0"},{"pz:unknown-thing","x"}},
    ("osm-way-2",new(){{"building","yes"},{"pz:kind","bathhouse"}},3,3));
   var house=p.Buildings.Single(b=>b.SourceId=="geo-osm-way-1");var d=house.Design!;
   Check(d.Style=="brick"&&d.RoofTileset=="roofs_30_09","pz:style or roof:colour was not applied");
   var outer=d.Floors.SelectMany(f=>f.Edges.Select(e=>(f,e))).Where(x=>x.e.Kind!="open"&&((BuildingDesigner.Cell(house,x.f,x.e.X,x.e.Y)=='.')!=((x.e.Edge=="n"?BuildingDesigner.Cell(house,x.f,x.e.X,x.e.Y-1):BuildingDesigner.Cell(house,x.f,x.e.X-1,x.e.Y))=='.'))).ToList();
   Check(outer.Count>0&&outer.All(x=>x.e.WallTile?.StartsWith("walls_exterior_house_02_")==true),"pz:wall did not reach every exterior edge");
   Check(outer.Where(x=>x.e.Kind=="window").All(x=>x.e.OverlayTile==(x.e.Edge=="n"?"fixtures_windows_detailing_01_1":"fixtures_windows_detailing_01_0"))&&outer.Any(x=>x.e.Kind=="window"),"pz:trim did not reach exterior windows");
   Check(outer.Where(x=>x.e.Kind=="door").All(x=>x.e.WallTile==$"walls_exterior_house_02_{42+(x.e.Edge=="n"?1:0)}"),"Door frames do not follow the wall set");
   Check(d.Floors.SelectMany(f=>f.Edges).Where(e=>e.WallTile is not null).All(e=>outer.Any(x=>x.e==e)),"Interior walls were changed");
   var bath=p.Buildings.Single(b=>b.SourceId=="geo-osm-way-2");Check(bath.Kind=="house"&&bath.Design!.Floors[0].Rooms.ContainsValue("bathroom"),"pz:kind=bathhouse is not a one-room bath");
   Check(p.ImportSummary!.Notes.Any(n=>n.Contains("pz:unknown-thing")),"Unknown pz:* key is not reported");
   ProjectValidator.Validate(p);using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));using var r=new StreamReader(zip.Entries.Single(e=>e.Name.EndsWith(".lotheader")).Open(),Encoding.Latin1);var header=r.ReadToEnd();
   Check(header.Contains("walls_exterior_house_02_32"),"wall missing");Check(header.Contains("roofs_30_09"),"roof missing: "+house.Design!.Roof);Check(header.Contains("fixtures_windows_detailing_01_"),"trim missing");
  });
  yield return("bad appearance values are reported and never fail the import",()=>{
   var p=Import(new(){{"building","house"},{"pz:style","no-such-style"},{"roof:colour","plaid"},{"pz:wall","not a tile"},{"pz:trim","onlyone_1"}});
   Check(p.Buildings.Count==1&&p.ImportSummary!.Notes.Count(n=>n.StartsWith("Appearance tag ignored"))==4,"Bad tag values must be reported, not fatal");
  });
  yield return("hex roof colours pick the nearest measured roof set and palettes name wall sets",()=>{
   Check(GeographicImporter.RoofTilesetFor("#d0d0d0")=="roofs_30_03"&&GeographicImporter.RoofTilesetFor("#304030")=="roofs_30_05"&&GeographicImporter.RoofTilesetFor("Grey")=="roofs_30_04","Roof colour lookup changed");
   var palette=new ResourcePalette{Id=new string('a',32),Name="Pilna",Resources=[new(){Role="wall",Id="walls_exterior_wooden_01_24"}]};
   Check(GeographicImporter.WallSetFor(palette.Id,[palette])==("walls_exterior_wooden_01",24)&&GeographicImporter.WallSetFor("walls_exterior_house_01_48",null)==("walls_exterior_house_01",48),"Wall set lookup changed");
  });
 }
 static MapProject Import(Dictionary<string,string> tags,params (string Id,Dictionary<string,string> Tags,int W,int H)[] more){
  var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.01,South=54,North=54.01},MetresPerTile=1,OriginCellX=100,OriginCellY=100};var projection=new GeographicProjection(reference,100,100);
  double[] At(double x,double y){var g=projection.Geographic(x,y);return[g.Longitude,g.Latitude];}
  object Box(string id,double x,double y,double w,double h,Dictionary<string,string> t)=>new{type="Feature",id,properties=t,geometry=new{type="Polygon",coordinates=new[]{new[]{At(x,y),At(x+w,y),At(x+w,y+h),At(x,y+h),At(x,y)}}}};
  var features=new List<object>{Box("osm-way-1",40.2,40.2,12,10,tags)};var x=70.2;foreach(var m in more){features.Add(Box(m.Id,x,40.2,m.W,m.H,m.Tags));x+=20;}
  var json=JsonSerializer.Serialize(new{type="FeatureCollection",pzTransfer=1,features});
  return GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),reference.Bounds,projection:projection).Project;
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
