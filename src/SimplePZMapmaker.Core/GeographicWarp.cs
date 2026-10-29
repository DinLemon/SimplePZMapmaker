namespace SimplePZMapmaker.Core;

/// <summary>Local tile coordinates in the common affine frame, before any section offset.</summary>
public readonly record struct GeographicWarpPoint(double X, double Y);

/// <summary>Complete rectangular source lattice; destination vertices are row-major (X changes fastest).
/// Each cell has the fixed NW-SE diagonal. No independent triangle indices or missing cells are allowed.</summary>
public sealed class GeographicWarpDefinition
{
    public double[] SourceX { get; set; } = [];
    public double[] SourceY { get; set; } = [];
    public GeographicWarpPoint[] Destination { get; set; } = [];
}

/// <summary>Immutable, validated piecewise affine homeomorphism of a bounded triangulated disk.</summary>
public sealed class GeographicWarp
{
    public const int MaxAxisPoints = 33;
    public const int MaxPathPoints = 100_000;
    public const double MaxCoordinate = 1_000_000;
    public const double MinAxisSpacing = .01;
    public const double MinDeterminant = 1e-6;
    private const double CoordinateTolerance = 1e-8;
    private const double WeightTolerance = 1e-10;
    private readonly double[] xs, ys;
    private readonly GeographicWarpPoint[] source, destination;
    private readonly Triangle[] triangles;
    public double SourceWidth => xs[^1];
    public double SourceHeight => ys[^1];
    public double MinimumDeterminant { get; }
    private readonly record struct Triangle(int A, int B, int C);

    public GeographicWarp(GeographicWarpDefinition definition)
    {
        if (definition is null) throw new ArgumentException("Missing geographic warp.");
        ValidateAxis(definition.SourceX); ValidateAxis(definition.SourceY);
        if (definition.Destination is null || definition.Destination.Length != definition.SourceX.Length * definition.SourceY.Length)
            throw new ArgumentException("Warp must provide one destination vertex for every source lattice vertex; holes are forbidden.");
        xs = (double[])definition.SourceX.Clone(); ys = (double[])definition.SourceY.Clone();
        destination = (GeographicWarpPoint[])definition.Destination.Clone();
        if (destination.Any(p => !Finite(p) || p.X < 0 || p.Y < 0 || p.X > MaxCoordinate || p.Y > MaxCoordinate))
            throw new ArgumentException("Warp destination coordinates must be finite and between 0 and 1000000 tiles.");
        source = (from y in ys from x in xs select new GeographicWarpPoint(x, y)).ToArray();
        var cells = new List<Triangle>();
        for (var y = 0; y < ys.Length - 1; y++) for (var x = 0; x < xs.Length - 1; x++) {
            var a = y * xs.Length + x; var b = a + 1; var c = a + xs.Length; var d = c + 1;
            cells.Add(new(a, b, d)); cells.Add(new(a, d, c));
        }
        triangles = cells.ToArray();
        MinimumDeterminant = double.PositiveInfinity;
        foreach (var t in triangles) {
            var area = Cross(destination[t.A], destination[t.B], destination[t.C]);
            var determinant = area / Cross(source[t.A], source[t.B], source[t.C]);
            if (!double.IsFinite(determinant) || determinant < MinDeterminant || area <= 1e-8)
                throw new ArgumentException("Geographic warp folds, collapses or is numerically degenerate; relax compression/rotation or enlarge transition fields.");
            MinimumDeterminant = Math.Min(MinimumDeterminant, determinant);
        }
        ValidateBoundary();
        // The source is a complete triangulated disk. Strictly positive faces and a simple
        // positively oriented boundary imply degree one: destination interiors cannot overlap.
    }

    private static void ValidateAxis(double[] axis)
    {
        if (axis is null || axis.Length is < 2 or > MaxAxisPoints || axis[0] != 0)
            throw new ArgumentException("Warp axes must start at zero and have 2-33 coordinates.");
        for (var i = 0; i < axis.Length; i++)
            if (!double.IsFinite(axis[i]) || axis[i] < 0 || axis[i] > MaxCoordinate || (i > 0 && axis[i] - axis[i - 1] < MinAxisSpacing))
                throw new ArgumentException("Warp source axes must be finite, increasing by at least 0.01 tile, and bounded by 1000000 tiles.");
    }

    private void ValidateBoundary()
    {
        var boundary = new List<GeographicWarpPoint>();
        for (var x = 0; x < xs.Length; x++) boundary.Add(destination[x]);
        for (var y = 1; y < ys.Length; y++) boundary.Add(destination[y * xs.Length + xs.Length - 1]);
        for (var x = xs.Length - 2; x >= 0; x--) boundary.Add(destination[(ys.Length - 1) * xs.Length + x]);
        for (var y = ys.Length - 2; y > 0; y--) boundary.Add(destination[y * xs.Length]);
        for (var i = 0; i < boundary.Count; i++) {
            var a = boundary[i]; var b = boundary[(i + 1) % boundary.Count];
            var c = boundary[(i + 2) % boundary.Count];
            if (Math.Abs(Cross(a,b,c)) <= CoordinateTolerance && (a.X-b.X)*(c.X-b.X)+(a.Y-b.Y)*(c.Y-b.Y) >= 0)
                throw new ArgumentException("Warp boundary doubles back or collapses.");
            for (var j = i + 2; j < boundary.Count; j++) {
                if (i == 0 && j == boundary.Count - 1) continue;
                if (SegmentsTouch(a, b, boundary[j], boundary[(j + 1) % boundary.Count]))
                    throw new ArgumentException("Warp destination boundary self-intersects/touches; overlapping destination triangles are forbidden.");
            }
        }
    }

    private static bool SegmentsTouch(GeographicWarpPoint a, GeographicWarpPoint b, GeographicWarpPoint c, GeographicWarpPoint d)
    {
        if (Math.Max(a.X,b.X) < Math.Min(c.X,d.X)-CoordinateTolerance || Math.Max(c.X,d.X) < Math.Min(a.X,b.X)-CoordinateTolerance ||
            Math.Max(a.Y,b.Y) < Math.Min(c.Y,d.Y)-CoordinateTolerance || Math.Max(c.Y,d.Y) < Math.Min(a.Y,b.Y)-CoordinateTolerance) return false;
        var abC=Cross(a,b,c); var abD=Cross(a,b,d); var cdA=Cross(c,d,a); var cdB=Cross(c,d,b);
        return !((abC > CoordinateTolerance && abD > CoordinateTolerance) || (abC < -CoordinateTolerance && abD < -CoordinateTolerance) ||
                 (cdA > CoordinateTolerance && cdB > CoordinateTolerance) || (cdA < -CoordinateTolerance && cdB < -CoordinateTolerance));
    }

    public GeographicWarpPoint Forward(GeographicWarpPoint point)
    {
        if (!Finite(point) || point.X < -CoordinateTolerance || point.Y < -CoordinateTolerance || point.X > SourceWidth+CoordinateTolerance || point.Y > SourceHeight+CoordinateTolerance)
            throw new ArgumentException("Source coordinate is outside the bounded geographic warp; clip source geometry before projection.");
        point = new(Math.Clamp(point.X,0,SourceWidth), Math.Clamp(point.Y,0,SourceHeight));
        var x = Interval(xs,point.X); var y = Interval(ys,point.Y);
        var u=(point.X-xs[x])/(xs[x+1]-xs[x]); var v=(point.Y-ys[y])/(ys[y+1]-ys[y]);
        // On the diagonal choose the first triangle, just as inverse's stable row-major scan does.
        var t=triangles[2*(y*(xs.Length-1)+x)+(v<=u?0:1)];
        return Map(point,t,source,destination);
    }

    public GeographicWarpPoint Inverse(GeographicWarpPoint point)
    {
        if (!Finite(point)) throw new ArgumentException("Non-finite destination coordinate.");
        if(TryInverse(point,out var result))return result;
        throw new ArgumentException("Destination coordinate is outside the bounded geographic warp.");
    }

    public bool TryInverse(GeographicWarpPoint point,out GeographicWarpPoint result)
    {
        result=default;if(!Finite(point))return false;
        foreach (var t in triangles) {
            var a=destination[t.A];var b=destination[t.B];var c=destination[t.C];
            if(point.X<Math.Min(a.X,Math.Min(b.X,c.X))-CoordinateTolerance || point.X>Math.Max(a.X,Math.Max(b.X,c.X))+CoordinateTolerance ||
               point.Y<Math.Min(a.Y,Math.Min(b.Y,c.Y))-CoordinateTolerance || point.Y>Math.Max(a.Y,Math.Max(b.Y,c.Y))+CoordinateTolerance) continue;
            var (u,v)=Weights(point,a,b,c);
            if(u>=-WeightTolerance && v>=-WeightTolerance && u+v<=1+WeightTolerance){result=Map(point,t,destination,source);return true;}
        }
        return false;
    }

    /// <summary>Splits every source segment at lattice edges and diagonals before mapping.
    /// Rings keep closure and winding. No clipping, rounding, hole removal or section-dependent simplification occurs.</summary>
    public IReadOnlyList<GeographicWarpPoint> ForwardPath(IEnumerable<GeographicWarpPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var input=points.Take(MaxPathPoints+1).ToArray();
        if(input.Length>MaxPathPoints) throw new ArgumentException("Warp path exceeds 100000 input points.");
        var output=new List<GeographicWarpPoint>();
        if(input.Length==0)return output;
        output.Add(Forward(input[0])); long work=0;
        for(var i=1;i<input.Length;i++) {
            var a=input[i-1];var b=input[i]; _=Forward(b);
            var cuts=new List<double>{0,1}; var dx=b.X-a.X;var dy=b.Y-a.Y;
            if(dx!=0)foreach(var x in xs){var t=(x-a.X)/dx;if(t>0&&t<1)cuts.Add(t);}
            if(dy!=0)foreach(var y in ys){var t=(y-a.Y)/dy;if(t>0&&t<1)cuts.Add(t);}
            cuts.Sort();
            var gridCuts=cuts.Distinct().ToArray();
            // A segment crosses at most one diagonal in each visited grid cell.
            for(var k=1;k<gridCuts.Length;k++) {
                if(++work>2_000_000)throw new ArgumentException("Warp path subdivision work budget exceeded.");
                var middle=(gridCuts[k-1]+gridCuts[k])/2;
                var x=Interval(xs,Math.Clamp(a.X+dx*middle,0,SourceWidth));var y=Interval(ys,Math.Clamp(a.Y+dy*middle,0,SourceHeight));
                var w=xs[x+1]-xs[x];var h=ys[y+1]-ys[y];
                var denominator=dx/w-dy/h;
                if(denominator==0)continue;
                var t=((a.Y-ys[y])/h-(a.X-xs[x])/w)/denominator;
                if(t>gridCuts[k-1]&&t<gridCuts[k])cuts.Add(t);
            }
            cuts.Sort(); double previous=0;
            foreach(var t in cuts) {
                if(t<=previous || t-previous<1e-13 && t!=1)continue;
                previous=t;
                if(output.Count>=MaxPathPoints)throw new ArgumentException("Warp path exceeds 100000 output points after subdivision.");
                output.Add(Forward(t==1?b:new(a.X+dx*t,a.Y+dy*t)));
            }
        }
        return output;
    }

    internal IEnumerable<List<GeographicWarpPoint>> MapRegion(IReadOnlyList<GeographicWarpPoint> polygon, bool inverse)
    {
        var from=inverse?destination:source;var to=inverse?source:destination;
        foreach(var t in triangles) {
            var clipped=Clip(polygon,[from[t.A],from[t.B],from[t.C]]);
            if(clipped.Count>0)yield return clipped.Select(p=>Map(p,t,from,to)).ToList();
        }
    }

    // Sutherland-Hodgman against a CCW convex polygon; keeps full mesh/rectangle intersections.
    internal static List<GeographicWarpPoint> Clip(IReadOnlyList<GeographicWarpPoint> subject,IReadOnlyList<GeographicWarpPoint> clip)
    {
        var output=subject.ToList();
        for(var i=0;i<clip.Count && output.Count>0;i++) {
            var input=output;output=[];var a=clip[i];var b=clip[(i+1)%clip.Count];
            var previous=input[^1];var before=Cross(a,b,previous);
            foreach(var current in input) {
                var after=Cross(a,b,current);
                if((before>=0)!=(after>=0)) {
                    var t=before/(before-after);
                    output.Add(new(previous.X+(current.X-previous.X)*t,previous.Y+(current.Y-previous.Y)*t));
                }
                if(after>=0)output.Add(current);
                previous=current;before=after;
            }
        }
        return output;
    }

    private static int Interval(double[] axis,double value){var i=Array.BinarySearch(axis,value);return Math.Clamp(i>=0?i:~i-1,0,axis.Length-2);}
    private static bool Finite(GeographicWarpPoint p)=>double.IsFinite(p.X)&&double.IsFinite(p.Y);
    private static double Cross(GeographicWarpPoint a,GeographicWarpPoint b,GeographicWarpPoint c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
    private static (double U,double V) Weights(GeographicWarpPoint p,GeographicWarpPoint a,GeographicWarpPoint b,GeographicWarpPoint c){var area=Cross(a,b,c);return(Cross(a,p,c)/area,Cross(a,b,p)/area);}
    private static GeographicWarpPoint Map(GeographicWarpPoint p,Triangle t,GeographicWarpPoint[] from,GeographicWarpPoint[] to){var (u,v)=Weights(p,from[t.A],from[t.B],from[t.C]);var a=to[t.A];var b=to[t.B];var c=to[t.C];return new(a.X+u*(b.X-a.X)+v*(c.X-a.X),a.Y+u*(b.Y-a.Y)+v*(c.Y-a.Y));}
}
