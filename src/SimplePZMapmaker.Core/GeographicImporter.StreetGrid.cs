namespace SimplePZMapmaker.Core;

public sealed record StreetGridSuggestion(double? RotationDegrees,double Strength,double RoadMetres,int DistrictCount,int SegmentsUsed,string Reason);

public static partial class GeographicImporter
{
    // Adapted from KnoxMap generator/rendering/gridangle.py, MIT.
    // Copyright (c) 2026 spytheeuclidean-a11y. See docs/licenses/KNOXMAP-MIT.txt.
    private const double GridDistrictMetres=400;
    private readonly record struct GridVote(double Angle,double Weight,double Sin,double Cos,int X,int Y);

    /// <summary>Suggest one common rotation before splitting a source into world sections.
    /// Returns no rotation when streets do not supply enough consistent evidence.</summary>
    public static StreetGridSuggestion SuggestStreetGrid(string json,GeoBounds bounds,CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();WorldLayoutLimits.ValidateSourceBounds(bounds);
        if(string.IsNullOrWhiteSpace(json))throw new ArgumentException("Choose a complete geographic source first.");
        if(json.Length>64_000_000||System.Text.Encoding.UTF8.GetByteCount(json)>64_000_000)throw new ArgumentException("The world geographic data file exceeds 64 MB.");
        List<Feature> features;
        using(var document=Parse(json)) {
            if(document.RootElement.ValueKind==System.Text.Json.JsonValueKind.Object&&document.RootElement.TryGetProperty("remark",out var remark)&&!string.IsNullOrWhiteSpace(remark.ToString()))
                throw new ArgumentException("The geographic source is incomplete. Retry the download before measuring its street grid.");
            features=ReadFeatures(document.RootElement,out var omitted,GeographicGeometryBudget.ForBytes(64_000_000));
            if(omitted>0)throw new ArgumentException($"The geographic source has {omitted} incomplete geometries. Retry or repair the source before measuring its street grid.");
        }
        token.ThrowIfCancellationRequested();
        var metresX=111320*Math.Cos((bounds.South+bounds.North)/2*Math.PI/180);
        var width=(bounds.East-bounds.West)*metresX;var height=(bounds.North-bounds.South)*111320;
        var cells=new Dictionary<(int X,int Y),(double Sin,double Cos,double Length)>();
        var segments=0;double total=0;
        foreach(var feature in features) {
            token.ThrowIfCancellationRequested();
            if(feature.Polygon||feature.Tags.GetValueOrDefault("highway") is not("residential" or "living_street" or "unclassified" or "tertiary" or "secondary" or "service" or "pedestrian" or "road"))continue;
            foreach(var ring in feature.Rings)for(var i=1;i<ring.Count;i++) {
                if((i&255)==0)token.ThrowIfCancellationRequested();
                var ax=(ring[i-1].X-bounds.West)*metresX;var ay=(bounds.North-ring[i-1].Y)*111320;
                var dx=(ring[i].X-ring[i-1].X)*metresX;var dy=(ring[i-1].Y-ring[i].Y)*111320;
                var length=Math.Sqrt(dx*dx+dy*dy);if(length<1e-8)continue;
                double lo=0,hi=1;
                if(!GridClip(-dx,ax,ref lo,ref hi)||!GridClip(dx,width-ax,ref lo,ref hi)||!GridClip(-dy,ay,ref lo,ref hi)||!GridClip(dy,height-ay,ref lo,ref hi)||hi<=lo)continue;
                // Clip before measuring; split at district boundaries so an undensified
                // OSM way and its identical densified geometry cast the same votes.
                var cuts=new List<double>{lo,hi};
                void Cut(double origin,double delta) {
                    if(Math.Abs(delta)<1e-10)return;
                    var start=origin+delta*lo;var end=origin+delta*hi;
                    for(var line=(int)Math.Floor(Math.Min(start,end)/GridDistrictMetres)+1;line*GridDistrictMetres<Math.Max(start,end);line++) {
                        var t=(line*GridDistrictMetres-origin)/delta;if(t>lo+1e-12&&t<hi-1e-12)cuts.Add(t);
                    }
                }
                Cut(ax,dx);Cut(ay,dy);cuts.Sort();
                var angle=4*Math.Atan2(-dy,dx);var sin=Math.Sin(angle);var cos=Math.Cos(angle);
                for(var part=1;part<cuts.Count;part++) {
                    var a=cuts[part-1];var b=cuts[part];var metres=(b-a)*length;if(metres<1e-8)continue;
                    var middle=(a+b)/2;var key=((int)Math.Floor((ax+dx*middle)/GridDistrictMetres),(int)Math.Floor((ay+dy*middle)/GridDistrictMetres));
                    var old=cells.GetValueOrDefault(key);cells[key]=(old.Sin+metres*sin,old.Cos+metres*cos,old.Length+metres);total+=metres;
                }
                segments++;
            }
        }
        var votes=cells.Where(c=>c.Value.Length>=60-1e-7).Select(c=>new GridVote(Math.Atan2(c.Value.Sin,c.Value.Cos)*45/Math.PI,Math.Sqrt(c.Value.Sin*c.Value.Sin+c.Value.Cos*c.Value.Cos),c.Value.Sin,c.Value.Cos,c.Key.X,c.Key.Y))
            .OrderByDescending(v=>v.Weight).ThenBy(v=>v.Angle).ThenBy(v=>v.X).ThenBy(v=>v.Y).ToArray();
        if(votes.Length==0)return new(null,0,total,0,segments,"Not enough local street evidence: each voting district needs at least 60 m of mapped roads.");
        var assigned=new bool[votes.Length];double best=0,bestSin=0,bestCos=0;
        for(var i=0;i<votes.Length;i++) {
            token.ThrowIfCancellationRequested();if(assigned[i])continue;
            double sin=0,cos=0;
            for(var j=i;j<votes.Length;j++)if(!assigned[j]&&GridAngleDistance(votes[i].Angle,votes[j].Angle)<=12) {assigned[j]=true;sin+=votes[j].Sin;cos+=votes[j].Cos;}
            var weight=Math.Sqrt(sin*sin+cos*cos);if(weight>best){best=weight;bestSin=sin;bestCos=cos;}
        }
        var strength=Math.Clamp(best/Math.Max(total,1),0,1);
        if(strength<.25)return new(null,strength,total,votes.Length,segments,"Streets do not agree on a dominant grid. Keep the manual rotation.");
        var rotation=Math.Atan2(bestSin,bestCos)*45/Math.PI;
        if(Math.Abs(rotation)<1e-10)rotation=0;
        return new(rotation,strength,total,votes.Length,segments,"Aligns the strongest street district. Other districts keep their own offsets; this does not bend roads or verify building outlines.");
    }
    private static bool GridClip(double p,double q,ref double lo,ref double hi) {
        if(p==0)return q>=0;var t=q/p;
        if(p<0){if(t>hi)return false;lo=Math.Max(lo,t);}else{if(t<lo)return false;hi=Math.Min(hi,t);}return true;
    }
    private static double GridAngleDistance(double a,double b){var d=Math.Abs(a-b)%90;return Math.Min(d,90-d);}
}
