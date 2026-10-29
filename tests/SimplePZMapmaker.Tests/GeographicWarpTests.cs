using SimplePZMapmaker.Core;
using System.Text.Json;

public static class GeographicWarpTests
{
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("warp absent keeps affine bits and legacy JSON", () => {
            foreach (var angle in new[] { 0.0, -17.0 }) {
                var reference = Reference(angle); var p = new GeographicProjection(reference, 102, 101);
                var before = p.Project((45.002, 55.002));
                var json = JsonSerializer.Serialize(reference);
                Check(!json.Contains("Warp"), "Null warp changed legacy reference JSON");
                var copy = JsonSerializer.Deserialize<WorldGeoReference>(json)!;
                Check(new GeographicProjection(copy, 102, 101).Project((45.002, 55.002)) == before, "Affine bits changed");
                if (angle == 0) {
                    var cos = Math.Cos((reference.Bounds.North+reference.Bounds.South)/2 * Math.PI / 180);
                    Check(before == ((45.002 - 45.0) * cos * 111320 - 512, (55.004 - 55.002) * 111320 - 256), "Legacy formula changed");
                }
            }
        });
        yield return ("warp builder preserves two rigid blocks and compresses fields deterministically", () => {
            var options = Options();
            var definition = GeographicWarpBuilder.Build(options);
            Check(JsonSerializer.Serialize(definition) == JsonSerializer.Serialize(GeographicWarpBuilder.Build(options)), "Builder is nondeterministic");
            var warp = new GeographicWarp(definition);
            var a = warp.Forward(new(210, 220)); var b = warp.Forward(new(230, 220));
            Near(Math.Sqrt(Math.Pow(a.X-b.X, 2)+Math.Pow(a.Y-b.Y, 2)), 20);
            Near(b.X-a.X, 20*Math.Cos(Math.PI/10)); Near(b.Y-a.Y, 20*Math.Sin(Math.PI/10));
            Near(warp.Forward(new(800, 800)).X, 606.25); Near(warp.Forward(new(800, 800)).Y, 645);
            Near(warp.Forward(new(1000, 1000)).X, 750); Near(warp.Forward(new(1000, 1000)).Y, 800);
            Check(warp.MinimumDeterminant > 0, "Fold accepted");
            for (var y=0; y<=1000; y+=25) for (var x=0; x<=1000; x+=25) {
                var q = warp.Forward(new(x,y)); var back = warp.Inverse(q); Near(back.X,x); Near(back.Y,y);
            }
        });
        yield return ("warp source path splits triangles and keeps shared roads parcels houses fences and holes", () => {
            var definition = Grid(); definition.Destination[4] = new(256, 64);
            var warp = new GeographicWarp(definition);
            var line = warp.ForwardPath([new(0,256),new(512,256)]);
            Check(line.Count >= 3 && line.Any(p=>Math.Abs(p.Y-64)<1e-8), "Road was reduced to its endpoint chord");
            var diagonalCuts=warp.ForwardPath([new(0,128),new(512,128)]);
            Check(diagonalCuts.Count>=5 && diagonalCuts.Any(p=>Math.Abs(p.X-128)<1e-8&&Math.Abs(p.Y-32)<1e-8),"Crossed cell diagonals were omitted");
            var reference = Reference(); reference.Warp = definition;
            var affine = new GeographicProjection(Reference(),100,100);
            var left = new GeographicProjection(reference,100,100); var right = new GeographicProjection(reference,101,100);
            var outer = new[] { (30.0,30.0),(400.0,30.0),(400.0,400.0),(30.0,400.0),(30.0,30.0) };
            var hole = new[] { (180.0,180.0),(180.0,280.0),(280.0,280.0),(280.0,180.0),(180.0,180.0) };
            var rings = new[] {outer,hole}.Select(r=>r.Select(v=>{var g=affine.Geographic(v.Item1,v.Item2);return(g.Longitude,g.Latitude);}).ToArray()).ToArray();
            var projected = left.ProjectRings(rings);
            Check(projected.Count==2 && Area(projected[0])>0 && Area(projected[1])<0, "Hole or winding lost");
            foreach(var ring in rings) {
                var first = left.ProjectPath(ring); var second = right.ProjectPath(ring);
                Check(first.Count==second.Count, "Section-dependent triangle splitting");
                for(var i=0;i<first.Count;i++){Near(first[i].X,second[i].X+256);Near(first[i].Y,second[i].Y);}
                foreach(var geometry in new[]{"road","parcel","house","fence"})
                    Check(left.ProjectPath(ring).SequenceEqual(first), $"{geometry} uses a different mesh");
                var reversed = left.ProjectPath(ring.Reverse().ToArray()).Reverse().ToArray();
                Check(first.Count==reversed.Length, "Reverse seam split differs");
                for(var i=0;i<first.Count;i++){Near(first[i].X,reversed[i].X);Near(first[i].Y,reversed[i].Y);}
            }
        });
        yield return ("warp section bounds includes interior triangle intersections and empty sections", () => {
            var reference = Reference(); reference.Warp = Grid(); reference.Warp.Destination[4] = new(256,64);
            var p = new GeographicProjection(reference,100,100); var box=p.SectionBounds(512,128);
            var affine = new GeographicProjection(Reference(),100,100); var source=affine.Geographic(256,280);
            Check(source.Latitude>=box.South && source.Latitude<=box.North, "Interior preimage missed by corner bounds");
            var empty = new GeographicProjection(reference,110,110).SectionBounds(256,256);
            Check(empty.West==empty.East && empty.South==empty.North, "Empty section fabricated source coverage");
            for(var y=0;y<=440;y+=10) for(var x=0;x<=250;x+=10) {
                var g=affine.Geographic(x,y);var q=p.Project((g.Longitude,g.Latitude));
                if(q.X>=0&&q.X<=512&&q.Y>=0&&q.Y<=128)
                    Check(g.Longitude>=box.West-1e-12&&g.Longitude<=box.East+1e-12&&g.Latitude>=box.South-1e-12&&g.Latitude<=box.North+1e-12,"Section omitted covered evidence");
            }
        });
        yield return ("warp required tiles includes a boundary bulge between rectangle corners", () => {
            var reference=Reference(); reference.Warp=Grid(); reference.Warp.Destination[5]=new(640,256);
            // Reference east edge is inside the last column; its intersections still exceed all four warped geographic corners.
            reference.Bounds.South=55.004-512.0/111320;
            reference.Bounds.East=45+512/(Math.Cos((reference.Bounds.North+reference.Bounds.South)/2*Math.PI/180)*111320);
            var size=GeographicProjection.RequiredTiles(reference);
            Check(size.Width>=639, $"Mesh boundary bulge omitted: {size}");
        });
        yield return ("warp inverse uses destination triangles and rejects outside rather than extrapolating", () => {
            var d=Grid();d.Destination[4]=new(320,180);var w=new GeographicWarp(d);
            for(var y=0;y<=512;y+=8)for(var x=0;x<=512;x+=8){var back=w.Forward(w.Inverse(new(x,y)));Near(back.X,x);Near(back.Y,y);}
            Reject(()=>w.Inverse(new(-1,1)));Reject(()=>w.Forward(new(513,1)));Reject(()=>w.Forward(new(double.NaN,1)));
            var saved=w.Forward(new(256,256));d.Destination[4]=new(0,0);Check(w.Forward(new(256,256))==saved,"Mutable DTO changed compiled mesh");
        });
        yield return ("warp rejects folds holes malformed meshes and resource excess", () => {
            var folded=Grid();folded.Destination[4]=new(900,20);Reject(()=>new GeographicWarp(folded));
            var flat=Grid();flat.Destination[4]=new(256,0);Reject(()=>new GeographicWarp(flat));
            var missing=Grid();missing.Destination=missing.Destination[..^1];Reject(()=>new GeographicWarp(missing));
            var bad=Grid();bad.SourceX=[0,256,256];Reject(()=>new GeographicWarp(bad));
            var huge=Grid();huge.SourceX=Enumerable.Range(0,10000).Select(x=>(double)x).ToArray();Reject(()=>new GeographicWarp(huge));
            var tiny=Grid();tiny.SourceY=[0,1e-12,512];Reject(()=>new GeographicWarp(tiny));
            var nan=Grid();nan.Destination[0]=new(double.PositiveInfinity,0);Reject(()=>new GeographicWarp(nan));
            var reference=Reference();reference.Warp=new(){SourceX=[0,1],SourceY=[0,1],Destination=[new(0,0),new(1,0),new(0,1),new(1,1)]};Reject(()=>new GeographicProjection(reference,100,100));
            Reject(()=>new GeographicWarp(Grid()).ForwardPath(Enumerable.Repeat(new GeographicWarpPoint(0,0),100001)));
        });
        yield return ("warp rejects positive local triangles that wind and overlap globally", () => {
            // Two turns of an annular strip: every cell has positive area, but the boundary/self overlap is illegal.
            var d=new GeographicWarpDefinition{SourceX=[0,1],SourceY=Enumerable.Range(0,17).Select(i=>(double)i).ToArray()};
            d.Destination=d.SourceY.SelectMany(y=>new[]{1.0,2.0}.Select(r=>new GeographicWarpPoint(10+r*Math.Cos(y*Math.PI/4),10+r*Math.Sin(y*Math.PI/4)))).ToArray();
            for(var y=0;y<16;y++) {
                var a=d.Destination[y*2];var b=d.Destination[y*2+1];var c=d.Destination[y*2+2];var e=d.Destination[y*2+3];
                Check(Cross(a,b,e)>0 && Cross(a,e,c)>0,"Overlap fixture must have positive local determinants");
            }
            Reject(()=>new GeographicWarp(d));
        });
        yield return ("warp builder rejects conflicting anchors impossible compression and budgets", () => {
            var options=Options();options.Anchors=[new(){MinX=100,MinY=100,MaxX=900,MaxY=900,RotationDegrees=90}];options.FieldScaleX=.1;options.FieldScaleY=.1;Reject(()=>GeographicWarpBuilder.Build(options));
            options=Options();options.Anchors=[new(){MinX=100,MinY=100,MaxX=300,MaxY=300},new(){MinX=200,MinY=200,MaxX=400,MaxY=400,RotationDegrees=10}];Reject(()=>GeographicWarpBuilder.Build(options));
            options=Options();options.MaxStep=.01;Reject(()=>GeographicWarpBuilder.Build(options));
        });
        yield return ("warp builder supports a rigid quarter turn with sufficient transition fields", () => {
            var mesh=new GeographicWarp(GeographicWarpBuilder.Build(new(){Width=1000,Height=1000,MaxStep=100,FieldScaleX=.8,FieldScaleY=.8,Anchors=[new(){MinX=480,MinY=480,MaxX=520,MaxY=520,RotationDegrees=90}]}));
            var p=mesh.Forward(new(510,500));Near(p.X,400);Near(p.Y,410);
            var q=mesh.Inverse(p);Near(q.X,510);Near(q.Y,500);
        });
        yield return ("warp rotated affine frame roundtrips across section seams and JSON", () => {
            var reference=Reference(-17);reference.Warp=GeographicWarpBuilder.Build(new(){Width=512,Height=512,MaxStep=128,FieldScaleX=.8,FieldScaleY=.7});
            reference=JsonSerializer.Deserialize<WorldGeoReference>(JsonSerializer.Serialize(reference))!;
            foreach(var (cx,cy) in new[]{(100,100),(101,100),(100,101)}){
                var p=new GeographicProjection(reference,cx,cy);
                foreach(var g in new[]{(45.0001,55.0039),(45.0039,55.0001),(45.002,55.002)}){
                    var q=p.Project(g);var back=p.Geographic(q.X,q.Y);Near(back.Longitude,g.Item1,1e-11);Near(back.Latitude,g.Item2,1e-11);
                }
            }
        });
        yield return ("warp rotated section bounds and required tiles cover full triangle intersections", () => {
            var reference=Reference(-17);reference.Warp=Grid();reference.Warp.Destination[4]=new(320,180);
            var origin=new GeographicProjection(reference,100,100);var required=GeographicProjection.RequiredTiles(reference);
            foreach(var (cx,cy) in new[]{(100,100),(101,100),(100,101),(101,101)}) {
                var section=new GeographicProjection(reference,cx,cy);var box=section.SectionBounds(256,256);
                for(var y=0;y<=40;y++)for(var x=0;x<=40;x++) {
                    var lon=45+.004*x/40;var lat=55+.004*y/40;var global=origin.Project((lon,lat));var local=section.Project((lon,lat));
                    Check(global.X>=-1e-8&&global.Y>=-1e-8&&global.X<=required.Width+1e-8&&global.Y<=required.Height+1e-8,"Required tiles omitted a source point");
                    if(local.X>=0&&local.X<=256&&local.Y>=0&&local.Y<=256)
                        Check(lon>=box.West-1e-12&&lon<=box.East+1e-12&&lat>=box.South-1e-12&&lat<=box.North+1e-12,"Rotated section source intersection omitted");
                }
            }
        });
        yield return ("warp maximum mesh runs and expanded paths and ring budgets reject", () => {
            var d=GeographicWarpBuilder.Build(new(){Width=1024,Height=1024,MaxStep=32,FieldScaleX=.75,FieldScaleY=.8});
            var warp=new GeographicWarp(d);
            var q=warp.Inverse(new(384,409.6));Near(q.X,512);Near(q.Y,512);
            Reject(()=>warp.ForwardPath(Enumerable.Range(0,2001).Select(i=>new GeographicWarpPoint(i%2==0?0:1024,513))));
            var reference=Reference();reference.Warp=d;var p=new GeographicProjection(reference,100,100);
            Reject(()=>p.ProjectRings(Enumerable.Range(0,257).Select(_=>Array.Empty<(double X,double Y)>())));
            var singular=Grid();singular.Destination=singular.Destination.Select(v=>new GeographicWarpPoint(v.X*.0001,v.Y*.0001)).ToArray();Reject(()=>new GeographicWarp(singular));
        });
    }
    static WorldGeoReference Reference(double rotation=0)=>new(){Bounds=new(){West=45,East=45.004,North=55.004,South=55},RotationDegrees=rotation};
    static GeographicWarpDefinition Grid()=>new(){SourceX=[0,256,512],SourceY=[0,256,512],Destination=(from y in new[]{0,256,512} from x in new[]{0,256,512} select new GeographicWarpPoint(x,y)).ToArray()};
    static GeographicWarpBuildOptions Options()=>new(){Width=1000,Height=1000,MaxStep=100,FieldScaleX=.75,FieldScaleY=.8,Anchors=[new(){MinX=200,MinY=200,MaxX=300,MaxY=300,RotationDegrees=18},new(){MinX=700,MinY=700,MaxX=850,MaxY=850}]};
    static double Area(IReadOnlyList<(double X,double Y)> p){double a=0;for(var i=1;i<p.Count;i++)a+=p[i-1].X*p[i].Y-p[i].X*p[i-1].Y;return a/2;}
    static double Cross(GeographicWarpPoint a,GeographicWarpPoint b,GeographicWarpPoint c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
    static void Near(double a,double b,double eps=1e-6)=>Check(Math.Abs(a-b)<=eps,$"Expected {b:R}, got {a:R}");
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected validation failure");}
}
