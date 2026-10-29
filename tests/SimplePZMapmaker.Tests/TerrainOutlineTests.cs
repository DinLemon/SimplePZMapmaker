using System.Text.Json;
using SimplePZMapmaker.Core;

static class TerrainOutlineTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return ("terrain outlines retain a detailed forest crossing a section",()=>{
   var (map,projection)=Frame(1024);var ring=new List<double[]>();
   var corners=new (double X,double Y)[]{(-500,-500),(5000,-500),(5000,5000),(-500,5000)};
   for(var side=0;side<4;side++)for(var i=0;i<12000;i++){var a=corners[side];var b=corners[(side+1)%4];ring.Add(Point(projection,a.X+(b.X-a.X)*i/12000,a.Y+(b.Y-a.Y)*i/12000));}ring.Add(ring[0]);
   var source=Source(Polygon(new[]{ring.ToArray()},"wood"));
   var imported=GeographicImporter.Import(source,map,projection.Reference.Bounds,projection:projection).Project;
   Check(imported.Terrain.All(t=>t==Terrain.Forest),"Detailed covering forest was clipped, simplified or lost");
   Check(map.Terrain.All(t=>t==Terrain.Grass),"Import mutated the input map");
  });
  yield return ("terrain outlines ignore off-section fill work",()=>{
   var (map,projection)=Frame(1024);
   var features=Enumerable.Range(0,50).Select(i=>Polygon(new[]{Ring(projection,(2000+i*10,-100),(5000+i*10,-100),(5000+i*10,1500),(2000+i*10,1500))},"wood")).ToArray();
   var imported=GeographicImporter.Import(Source(features),map,projection.Reference.Bounds,projection:projection).Project;
   Check(imported.Terrain.All(t=>t==Terrain.Grass),"Off-section forests painted this section");
  });
  yield return ("terrain outlines preserve scanline holes and half-tile boundary rules",()=>{
   var (map,projection)=Frame(256);
   var outer=new (double X,double Y)[]{(-25.5,16.5),(85.5,16.5),(85.5,80.5),(180.5,40.5),(300.5,200.5),(100.5,240.5),(-25.5,160.5),(-25.5,16.5)};
   var hole=new (double X,double Y)[]{(18.5,90.5),(63.5,90.5),(63.5,130.5),(18.5,130.5),(18.5,90.5)};
   var rings=new[]{outer,hole}.Select(r=>r.Select(v=>Point(projection,v.X,v.Y)).ToArray()).ToArray();
   var result=GeographicImporter.Import(Source(Polygon(rings,"water")),map,projection.Reference.Bounds,projection:projection).Project;
   // Reference the previous raster's exact endpoint expression, including round-trip projection.
   var projected=rings.Select(r=>r.Select(v=>projection.Project((v[0],v[1]))).ToArray()).ToArray();
   for(var y=0;y<256;y++)for(var x=0;x<256;x++){
    var inside=false;foreach(var ring in projected)for(int i=0,j=ring.Length-1;i<ring.Length;j=i++)if((ring[i].Y>y+.5)!=(ring[j].Y>y+.5)&&x+.5<ring[i].X+(y+.5-ring[i].Y)*(ring[j].X-ring[i].X)/(ring[j].Y-ring[i].Y))inside=!inside;
    Check(result.Terrain[y*256+x]==(inside?Terrain.Water:Terrain.Grass),$"Terrain parity changed at {x},{y}");
   }
  });
  yield return ("terrain outlines retain an actual fill budget and preserve input on failure",()=>{
   var (map,projection)=Frame(1024);var ring=Ring(projection,(-10,-10),(1100,-10),(1100,1100),(-10,1100));
   var source=Source(Enumerable.Range(0,41).Select(_=>Polygon(new[]{ring},"wood")).ToArray());
   try{GeographicImporter.Import(source,map,projection.Reference.Bounds,projection:projection);throw new Exception("Unbounded repeated terrain painting was accepted");}
   catch(ArgumentException e){Check(e.Message.Contains("terrain",StringComparison.OrdinalIgnoreCase)||e.Message.Contains("outlines",StringComparison.OrdinalIgnoreCase),"Unexpected admission failure");}
   Check(map.Terrain.All(t=>t==Terrain.Grass),"Failed fill mutated the input map");
  });
  yield return ("terrain outlines are continuous across independently imported sections",()=>{
   var (whole,projection)=Frame(512);
   var source=Source(Polygon(new[]{Ring(projection,(-200,-200),(800,-200),(800,800),(-200,800)),Ring(projection,(220,60),(290,60),(290,190),(220,190))},"water"));
   var expected=GeographicImporter.Import(source,whole,projection.Reference.Bounds,projection:projection).Project;
   for(var section=0;section<2;section++){
    var map=MapGenerator.Generate(new(){Kind="blank",Size=256});map.WorldX=100+section;map.WorldY=100;map.WorldSection=true;map.Version=6;
    var local=new GeographicProjection(projection.Reference,100+section,100);
    var actual=GeographicImporter.Import(source,map,local.SectionBounds(256,256),projection:local).Project;
    for(var y=0;y<256;y++)for(var x=0;x<256;x++)Check(actual.Terrain[y*256+x]==expected.Terrain[y*512+x+section*256],"Water or island split at a section seam");
   }
  });
 }
 static (MapProject,GeographicProjection) Frame(int size){var bounds=new GeoBounds{West=30,East=30.5,South=49.8,North=50};var projection=new GeographicProjection(new(){Bounds=bounds},100,100);var map=MapGenerator.Generate(new(){Kind="blank",Size=size});map.WorldX=100;map.WorldY=100;map.WorldSection=true;map.Version=6;return(map,projection);}
 static double[] Point(GeographicProjection projection,double x,double y){var p=projection.Geographic(x,y);return[p.Longitude,p.Latitude];}
 static double[][] Ring(GeographicProjection projection,params (double X,double Y)[] points)=>points.Append(points[0]).Select(p=>Point(projection,p.X,p.Y)).ToArray();
 static object Polygon(double[][][] rings,string natural)=>new{type="Feature",properties=new{natural},geometry=new{type="Polygon",coordinates=rings}};
 static string Source(params object[] features)=>JsonSerializer.Serialize(new{type="FeatureCollection",features});
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
