using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.Text.Json;
public static class AttachedBuildingTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("pz:attached places a shed against the house with one shared wall and a door between them",()=>{
   var p=Import(new(){{"building","shed"},{"pz:kind","shed"},{"pz:attached","osm-way-1"},{"pz:door-to","osm-way-1"}});
   var house=p.Buildings.Single(b=>b.SourceId=="geo-osm-way-1");var shed=p.Buildings.Single(b=>b.SourceId=="geo-osm-way-2");
   Check(shed.AttachedTo==house.Id&&p.Version==13,"The shed is not attached or the map format was not raised");
   var houseCells=Cells(house);var shedCells=Cells(shed);Check(!houseCells.Overlaps(shedCells)&&shedCells.Any(c=>houseCells.Contains((c.X-1,c.Y))),"The rooms do not touch without a free line");
   var suppressed=AttachedBuildings.SuppressedEdges(p.Buildings);Check(suppressed.Count>0&&suppressed.All(e=>e.Building==shed.Id&&e.Edge=="w"&&e.X==shedCells.Min(c=>c.X)),"Only the shed's side of the shared wall should be suppressed");
   var ground=house.Design!.Floors[-house.Design.MinLevel];var sharedX=shedCells.Min(c=>c.X)-house.X;
   Check(ground.Edges.Any(e=>e.X==sharedX&&e.Edge=="w"&&e.Kind=="door"&&shedCells.Contains((house.X+e.X,house.Y+e.Y))),"pz:door-to did not open the shared wall");
   var shedGround=shed.Design!.Floors[-shed.Design.MinLevel];Check(shedGround.Edges.Any(e=>e.Kind=="door"&&!suppressed.Contains((shed.Id,shed.X+e.X,shed.Y+e.Y,0,e.Edge))),"The shed lost its own entrance");
   Check(ground.Edges.Where(e=>e.X==sharedX&&e.Edge=="w"&&shedCells.Contains((house.X+e.X,house.Y+e.Y))).All(e=>e.Kind is "wall" or "door"),"A window still opens into the attached shed");
   ProjectValidator.Validate(p);
   var scene=NativeScene.Cell(p,house.X-2,house.Y-2,house.Width+shed.Width+6,house.Height+4,0);var sharedWall=shedCells.Min(c=>c.X);
   foreach(var y in shedCells.Select(c=>c.Y).Distinct()){var walls=scene.Sprites.Where(s=>s.Z==0&&s.X==sharedWall&&s.Y==y&&s.Tile.StartsWith("walls_",StringComparison.Ordinal)).ToList();var ownNorth=shedGround.Edges.Count(e=>e.X==sharedWall-shed.X&&e.Y==y-shed.Y&&e.Edge=="n"&&e.Kind!="open");Check(walls.Count==1+ownNorth,$"Shared wall drawn {walls.Count-ownNorth} times at row {y}");}
  });
  yield return("without pz:attached the same shed keeps the free line and is skipped",()=>{
   var p=Import(new(){{"building","shed"}});Check(p.Buildings.Count==1&&p.ImportSummary!.SkipReasons.ContainsKey("Building overlap"),"A touching outline without attachment must keep the free line");
  });
  yield return("attachment must name a building on the same map and rooms never overlap",()=>{
   var p=Import(new(){{"building","shed"},{"pz:attached","osm-way-1"}});var shed=p.Buildings.Single(b=>b.SourceId=="geo-osm-way-2");
   shed.AttachedTo="missing";Reject(()=>ProjectValidator.Validate(p));shed.AttachedTo=shed.Id;Reject(()=>ProjectValidator.Validate(p));
   var house=p.Buildings.Single(b=>b.SourceId=="geo-osm-way-1");shed.AttachedTo=house.Id;shed.X-=1;Reject(()=>ProjectValidator.Validate(p));
   var json=JsonSerializer.Serialize(p,NativeMapExporter.JsonOptions);shed.X+=1;var round=JsonSerializer.Deserialize<MapProject>(JsonSerializer.Serialize(p,NativeMapExporter.JsonOptions),NativeMapExporter.JsonOptions)!;ProjectValidator.Validate(round);Check(round.Buildings.Single(b=>b.SourceId=="geo-osm-way-2").AttachedTo==house.Id,"AttachedTo did not survive JSON");
  });
  yield return("shared world buildings refuse attachment",()=>{
   var w=WorldTests.Fixture(2);var b=new Building{X=100*256+250,Y=100*256+80,Width=12,Height=10,AttachedTo="x"};b.Design=BuildingDesigner.Generate(b,1,42);w.Buildings.Add(b);Reject(()=>WorldAssembler.Validate(w));
  });
 }
 static HashSet<(int X,int Y)> Cells(Building b){var f=b.Design!.Floors[-b.Design.MinLevel];var result=new HashSet<(int,int)>();for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;x++)if(BuildingDesigner.Cell(b,f,x,y)!='.')result.Add((b.X+x,b.Y+y));return result;}
 /// <summary>A 12x10 house and a 4x4 shed sharing the house's east side.</summary>
 static MapProject Import(Dictionary<string,string> shedTags){
  var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.01,South=54,North=54.01},MetresPerTile=1,OriginCellX=100,OriginCellY=100};var projection=new GeographicProjection(reference,100,100);
  double[] At(double x,double y){var g=projection.Geographic(x,y);return[g.Longitude,g.Latitude];}
  object Box(string id,double x,double y,double w,double h,Dictionary<string,string> t)=>new{type="Feature",id,properties=t,geometry=new{type="Polygon",coordinates=new[]{new[]{At(x,y),At(x+w,y),At(x+w,y+h),At(x,y+h),At(x,y)}}}};
  var json=JsonSerializer.Serialize(new{type="FeatureCollection",pzTransfer=1,features=new[]{Box("osm-way-2",52,43,4,4,shedTags),Box("osm-way-1",40,40,12,10,new(){{"building","house"}})}});
  return GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),reference.Bounds,projection:projection).Project;
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected validation failure");}
}
