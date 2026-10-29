namespace SimplePZMapmaker.Core;

public sealed class GeographicWarpAnchor
{
    public double MinX { get; set; }
    public double MinY { get; set; }
    public double MaxX { get; set; }
    public double MaxY { get; set; }
    public double RotationDegrees { get; set; }
    /// <summary>Null uses the field-compressed source centre.</summary>
    public GeographicWarpPoint? DestinationCentre { get; set; }
}

public sealed class GeographicWarpBuildOptions
{
    public double Width { get; set; }
    public double Height { get; set; }
    public double MaxStep { get; set; } = 256;
    public double FieldScaleX { get; set; } = 1;
    public double FieldScaleY { get; set; } = 1;
    public GeographicWarpAnchor[] Anchors { get; set; } = [];
}

/// <summary>Rigid protected rectangles plus graph-harmonic field displacement. This is NOT ARAP.
/// Anchor bounds become mesh lines, so every triangle inside an anchor has exactly its rigid transform.</summary>
public static class GeographicWarpBuilder
{
    public static GeographicWarpDefinition Build(GeographicWarpBuildOptions options)
    {
        if(options is null || !double.IsFinite(options.Width) || !double.IsFinite(options.Height) || options.Width<GeographicWarp.MinAxisSpacing || options.Height<GeographicWarp.MinAxisSpacing ||
           options.Width>GeographicWarp.MaxCoordinate || options.Height>GeographicWarp.MaxCoordinate || !double.IsFinite(options.MaxStep) || options.MaxStep<GeographicWarp.MinAxisSpacing ||
           !double.IsFinite(options.FieldScaleX) || !double.IsFinite(options.FieldScaleY) || options.FieldScaleX is <.01 or >1 || options.FieldScaleY is <.01 or >1 || options.Anchors is null || options.Anchors.Length>32)
            throw new ArgumentException("Invalid geographic warp builder dimensions, spacing, field scales (0.01-1) or anchors (maximum 32).");
        var anchors=options.Anchors;
        for(var i=0;i<anchors.Length;i++) {
            var a=anchors[i];
            if(a is null || !double.IsFinite(a.MinX)||!double.IsFinite(a.MinY)||!double.IsFinite(a.MaxX)||!double.IsFinite(a.MaxY)||!double.IsFinite(a.RotationDegrees)||Math.Abs(a.RotationDegrees)>180 ||
               a.MinX<=0||a.MinY<=0||a.MaxX>=options.Width||a.MaxY>=options.Height||a.MaxX-a.MinX<GeographicWarp.MinAxisSpacing||a.MaxY-a.MinY<GeographicWarp.MinAxisSpacing ||
               a.DestinationCentre is {} d && (!double.IsFinite(d.X)||!double.IsFinite(d.Y)||d.X<0||d.Y<0||d.X>GeographicWarp.MaxCoordinate||d.Y>GeographicWarp.MaxCoordinate))
                throw new ArgumentException("Protected warp anchors must be finite, strictly inside the domain, and at least 0.01 tile wide/high.");
            for(var j=0;j<i;j++)if(a.MinX<=anchors[j].MaxX&&a.MaxX>=anchors[j].MinX&&a.MinY<=anchors[j].MaxY&&a.MaxY>=anchors[j].MinY)
                throw new ArgumentException("Protected warp anchors must be separated; touching or overlapping constraints are ambiguous.");
        }
        var xs=Axis(options.Width,options.MaxStep,anchors.SelectMany(a=>new[]{a.MinX,a.MaxX}));
        var ys=Axis(options.Height,options.MaxStep,anchors.SelectMany(a=>new[]{a.MinY,a.MaxY}));
        var displacement=new GeographicWarpPoint[xs.Length*ys.Length];var fixedNodes=new bool[displacement.Length];
        for(var y=0;y<ys.Length;y++)for(var x=0;x<xs.Length;x++) {
            var i=y*xs.Length+x;var sx=xs[x];var sy=ys[y];
            var target=new GeographicWarpPoint(sx*options.FieldScaleX,sy*options.FieldScaleY);
            fixedNodes[i]=x==0||y==0||x==xs.Length-1||y==ys.Length-1;
            foreach(var a in anchors)if(sx>=a.MinX&&sx<=a.MaxX&&sy>=a.MinY&&sy<=a.MaxY) {
                var cx=(a.MinX+a.MaxX)/2;var cy=(a.MinY+a.MaxY)/2;
                var centre=a.DestinationCentre??new(cx*options.FieldScaleX,cy*options.FieldScaleY);
                var angle=a.RotationDegrees*Math.PI/180;var cos=Math.Cos(angle);var sin=Math.Sin(angle);
                target=new(centre.X+(sx-cx)*cos-(sy-cy)*sin,centre.Y+(sx-cx)*sin+(sy-cy)*cos);fixedNodes[i]=true;
            }
            // Solve correction to the global field compression, with zero boundary correction.
            displacement[i]=new(target.X-sx*options.FieldScaleX,target.Y-sy*options.FieldScaleY);
        }
        var converged=false;
        for(var iteration=0;iteration<10_000;iteration++) {
            double change=0;
            for(var y=1;y<ys.Length-1;y++)for(var x=1;x<xs.Length-1;x++) {
                var i=y*xs.Length+x;if(fixedNodes[i])continue;
                double total=0,dx=0,dy=0;
                Add(i-1,xs[x]-xs[x-1]);Add(i+1,xs[x+1]-xs[x]);Add(i-xs.Length,ys[y]-ys[y-1]);Add(i+xs.Length,ys[y+1]-ys[y]);
                var next=new GeographicWarpPoint(dx/total,dy/total);
                change=Math.Max(change,Math.Max(Math.Abs(next.X-displacement[i].X),Math.Abs(next.Y-displacement[i].Y)));displacement[i]=next;
                void Add(int neighbor,double distance){var weight=1/(distance*distance);total+=weight;dx+=displacement[neighbor].X*weight;dy+=displacement[neighbor].Y*weight;}
            }
            if(change<=1e-9){converged=true;break;}
        }
        if(!converged)throw new ArgumentException("Geographic warp interpolation did not converge within 10000 sweeps; use a better spaced mesh.");
        var result=new GeographicWarpDefinition{SourceX=xs,SourceY=ys,Destination=new GeographicWarpPoint[displacement.Length]};
        for(var y=0;y<ys.Length;y++)for(var x=0;x<xs.Length;x++) {
            var i=y*xs.Length+x;result.Destination[i]=new(xs[x]*options.FieldScaleX+displacement[i].X,ys[y]*options.FieldScaleY+displacement[i].Y);
        }
        try { _=new GeographicWarp(result); }
        catch(ArgumentException) when(anchors.Length==1 && options.FieldScaleX==options.FieldScaleY && anchors[0].DestinationCentre is null) {
            // A large rigid turn can fold a harmonic displacement on thin Cartesian strips.
            // One isolated anchor admits a radial transition with monotone radius instead.
            var anchor=anchors[0];var cx=(anchor.MinX+anchor.MaxX)/2;var cy=(anchor.MinY+anchor.MaxY)/2;
            var inner=Math.Sqrt(Math.Pow((anchor.MaxX-anchor.MinX)/2,2)+Math.Pow((anchor.MaxY-anchor.MinY)/2,2));
            var outer=Math.Min(Math.Min(cx,options.Width-cx),Math.Min(cy,options.Height-cy));
            if(outer<=inner||options.FieldScaleX*outer<=inner)throw;
            for(var y=0;y<ys.Length;y++)for(var x=0;x<xs.Length;x++) {
                var dx=xs[x]-cx;var dy=ys[y]-cy;var radius=Math.Sqrt(dx*dx+dy*dy);
                var t=Math.Clamp((radius-inner)/(outer-inner),0,1);
                var angle=Math.Atan2(dy,dx)+anchor.RotationDegrees*Math.PI/180*(1-t);
                var distance=radius<=inner?radius:radius>=outer?radius*options.FieldScaleX:inner+(options.FieldScaleX*outer-inner)*t;
                var px=cx*options.FieldScaleX+distance*Math.Cos(angle);var py=cy*options.FieldScaleY+distance*Math.Sin(angle);
                result.Destination[y*xs.Length+x]=new(Math.Abs(px)<1e-8?0:px,Math.Abs(py)<1e-8?0:py);
            }
            _=new GeographicWarp(result);
        } // Never publish a folded or globally overlapping proposal.
        return result;
    }

    private static double[] Axis(double extent,double step,IEnumerable<double> anchors)
    {
        var lines=anchors.Append(0).Append(extent).Distinct().Order().ToArray();var output=new List<double>{0};
        for(var i=1;i<lines.Length;i++) {
            var length=lines[i]-lines[i-1];var count=Math.Ceiling(length/step);
            if(length<GeographicWarp.MinAxisSpacing || count>GeographicWarp.MaxAxisPoints || output.Count+count>GeographicWarp.MaxAxisPoints)
                throw new ArgumentException("Warp builder exceeds 33 axis lines or creates spacing below 0.01 tile; increase MaxStep or separate anchors.");
            for(var j=1;j<=(int)count;j++)output.Add(j==(int)count?lines[i]:lines[i-1]+length*j/count);
        }
        return output.ToArray();
    }
}
