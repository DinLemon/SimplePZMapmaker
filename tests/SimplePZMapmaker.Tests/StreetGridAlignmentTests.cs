using System.Text.Json;
using SimplePZMapmaker.Core;

public static class StreetGridAlignmentTests
{
    private static readonly GeoBounds Bounds = new() { West=30, East=30+2000/MetresX, South=50, North=50+2000/111320d };
    private static double MetresX => 111320*Math.Cos((50+1000/111320d)*Math.PI/180);

    public static IEnumerable<(string,Action)> Cases()
    {
        yield return ("street grid alignment sign straightens the actual world projection", () => {
            var json=Source(Road(-11.22,220,200,200),Road(78.78,180,200,200));
            var result=GeographicImporter.SuggestStreetGrid(json,Bounds);
            Near(result.RotationDegrees!.Value,11.22);Near(result.Strength,1);
            var reference=new WorldGeoReference { Bounds=Bounds,RotationDegrees=result.RotationDegrees.Value,MetresPerTile=1 };
            var projection=new GeographicProjection(reference,reference.OriginCellX,reference.OriginCellY);
            var a=Geo(100,250);var b=Geo(100+180*Math.Cos(-11.22*Math.PI/180),250+180*Math.Sin(-11.22*Math.PI/180));
            Near(projection.Project((a[0],a[1])).Y,projection.Project((b[0],b[1])).Y);
        });
        yield return ("street grid alignment chooses a district rather than averaging phases", () => {
            var result=GeographicImporter.SuggestStreetGrid(Source(Road(10,180,200,200),Road(10,180,600,200),Road(10,180,1000,200),Road(-30,240,200,1000),Road(-30,240,600,1000)),Bounds);
            Near(result.RotationDegrees!.Value,-10);Check(result.DistrictCount==5,"District count differs");
            Near(result.Strength,540d/1020);
        });
        yield return ("street grid alignment clips long crossings and ignores remote roads and motorways", () => {
            var result=GeographicImporter.SuggestStreetGrid(Source(Line([Geo(-1000,800),Geo(3000,800)]),Road(30,500,700,2800),Road(-25,800,900,1300,"motorway"),Road(35,800,900,1700,"path")),Bounds);
            Near(result.RotationDegrees!.Value,0);Near(result.RoadMetres,2000);
            Check(result.DistrictCount==5,"Crossing segment was not split into local votes");
        });
        yield return ("street grid alignment is independent of segment direction and densification", () => {
            var a=Geo(80,500);var b=Geo(1800,1000);var mid=Geo(940,750);
            var sparse=GeographicImporter.SuggestStreetGrid(Source(Line([a,b])),Bounds);
            var dense=GeographicImporter.SuggestStreetGrid(Source(Line([b,mid,a])),Bounds);
            Near(sparse.RotationDegrees!.Value,dense.RotationDegrees!.Value);Near(sparse.Strength,dense.Strength);Near(sparse.RoadMetres,dense.RoadMetres);
        });
        yield return ("street grid alignment wraps orthogonal directions at the 45 degree seam", () => {
            var result=GeographicImporter.SuggestStreetGrid(Source(Road(44,180,200,200),Road(-44,180,600,200)),Bounds);
            Check(result.RotationDegrees.HasValue&&Math.Abs(Math.Abs(result.RotationDegrees.Value)-45)<1e-7,"Equivalent grids failed to cluster at the seam");
        });
        yield return ("street grid alignment refuses weak evidence and short stubs", () => {
            Check(GeographicImporter.SuggestStreetGrid(Source(Road(10,20,200,200)),Bounds).RotationDegrees is null,"A short stub rotated the world");
            var weak=GeographicImporter.SuggestStreetGrid(Source(Enumerable.Range(0,8).Select(i=>Road(i*11.25,100,200,200)).ToArray()),Bounds);
            Check(weak.RotationDegrees is null&&weak.Strength<.25,"An organic network produced an authoritative angle");
            Check(GeographicImporter.SuggestStreetGrid(Source(),Bounds).RotationDegrees is null,"Empty source supplied an angle");
        });
        yield return ("street grid alignment accepts Overpass and rejects incomplete sources", () => {
            var points=new[]{Geo(50,100),Geo(250,100)}.Select(p=>new{lon=p[0],lat=p[1]});
            var json=JsonSerializer.Serialize(new{elements=new[]{new{type="way",id=1,tags=new{highway="residential"},geometry=points}}});
            Near(GeographicImporter.SuggestStreetGrid(json,Bounds).RotationDegrees!.Value,0);
            Reject(()=>GeographicImporter.SuggestStreetGrid("{\"remark\":\"timeout\",\"elements\":[]}",Bounds));
            var damaged=System.Text.Json.Nodes.JsonNode.Parse(json)!;
            ((System.Text.Json.Nodes.JsonArray)damaged["elements"]!).Add(System.Text.Json.Nodes.JsonNode.Parse("{\"type\":\"way\",\"id\":2,\"tags\":{\"highway\":\"residential\"},\"geometry\":[{\"lon\":30.001,\"lat\":50.001},{}]}"));
            Reject(()=>GeographicImporter.SuggestStreetGrid(damaged.ToJsonString(),Bounds));
            Reject(()=>GeographicImporter.SuggestStreetGrid("{}",Bounds));
            using var cancel=new CancellationTokenSource();cancel.Cancel();
            try{GeographicImporter.SuggestStreetGrid(json,Bounds,cancel.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}
        });
    }
    private static double[] Geo(double x,double y)=>[30+x/MetresX,Bounds.North-y/111320];
    private static object Road(double angle,double length,double x,double y,string highway="residential") {
        var a=angle*Math.PI/180;return Line([Geo(x-length/2*Math.Cos(a),y-length/2*Math.Sin(a)),Geo(x+length/2*Math.Cos(a),y+length/2*Math.Sin(a))],highway);
    }
    private static object Line(double[][] points,string highway="residential")=>new{type="Feature",properties=new{highway},geometry=new{type="LineString",coordinates=points}};
    private static string Source(params object[] features)=>JsonSerializer.Serialize(new{type="FeatureCollection",features});
    private static void Near(double a,double b){if(Math.Abs(a-b)>1e-6)throw new Exception($"Expected {b:R}, received {a:R}");}
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Invalid source accepted");}
}
