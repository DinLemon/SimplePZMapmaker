namespace SimplePZMapmaker.Core;

public sealed record SitePoint(double X, double Y);

/// <summary>An editable group. Boundary is game geometry; original geographic evidence is retained separately.</summary>
public sealed class SiteParcel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Site";
    public string Origin { get; set; } = "authored";
    public string? SourceId { get; set; }
    public string? SourceFeatureId { get; set; }
    public List<List<SitePoint>> Boundary { get; set; } = [];
    public List<string> BuildingIds { get; set; } = [];
    public List<string> DecorationIds { get; set; } = [];
    public List<string> StaticModelIds { get; set; } = [];
    public List<string> LinearFeatureIds { get; set; } = [];
    public List<string> AssociationNotes {get;set;}=[];
    public bool Locked { get; set; }
}

public static class SiteModel
{
    public const int MaximumSites = 512, MaximumVertices = 4096;

    /// <summary>Explicit editor deletions detach membership, in the same history transaction as the removed object.</summary>
    public static void DetachMissingMembers(MapProject p)
    {
        var available=new[]{p.Buildings.Select(b=>b.Id).ToHashSet(),p.Decorations.Select(b=>b.Id).ToHashSet(),p.StaticModels.Select(b=>b.Id).ToHashSet(),p.LinearFeatures.Select(b=>b.Id).ToHashSet()};
        foreach(var s in p.Sites){var refs=new[]{s.BuildingIds,s.DecorationIds,s.StaticModelIds,s.LinearFeatureIds};var removed=0;for(var i=0;i<refs.Length;i++)removed+=refs[i].RemoveAll(id=>!available[i].Contains(id));if(removed>0&&s.Origin=="source")s.Origin="source-edited";}
    }

    public static void Validate(MapProject project)
    {
        if (project.Sites is null || project.Sites.Count > MaximumSites || project.Sites.Any(s => s is null) || project.Sites.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != project.Sites.Count)
            throw new ArgumentException("Use at most 512 uniquely identified sites.");
        var buildings = project.Buildings.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        var decorations = project.Decorations.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        var models = project.StaticModels.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        var lines = project.LinearFeatures.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        long vertices = 0, members = 0;
        foreach (var site in project.Sites)
        {
            if(site.AssociationNotes is null||site.AssociationNotes.Count>8||site.AssociationNotes.Any(n=>n is null||n.Length>300))throw new ArgumentException("Invalid site association notes.");
            if (!Text(site.Id, 100) || !Text(site.Name, 100) || site.Origin is not ("authored" or "source" or "source-edited" or "inferred") || site.SourceId is not null && !Text(site.SourceId, 100) || site.SourceFeatureId is not null && !Text(site.SourceFeatureId, 100))
                throw new ArgumentException("Invalid site identity, name or provenance.");
            if (site.Boundary is null || site.Boundary.Count is < 1 or > 32 || site.Boundary.Any(r => r is null || r.Count is < 3 or > MaximumVertices) || site.Boundary.Sum(r => r.Count) > MaximumVertices)
                throw new ArgumentException("A site needs an outer polygon and at most 31 holes, within 4,096 vertices.");
            vertices += site.Boundary.Sum(r => r.Count);
            if (vertices > 65536) throw new ArgumentException("Site boundaries exceed the 65,536 vertex project budget.");
            foreach (var ring in site.Boundary)
            {
                if (ring.Any(p => p is null || !double.IsFinite(p.X) || !double.IsFinite(p.Y) || Math.Abs(p.X) > 258048 || Math.Abs(p.Y) > 258048) || ring.Distinct().Count() < 3 || Math.Abs(Area(ring)) < .001)
                    throw new ArgumentException("Site polygons need finite, non-degenerate coordinates.");
                // Avoid quadratic adversarial topology work while rejecting self-intersecting small authoring rings.
                if (ring.Count <= 256) for (var i = 0; i < ring.Count; i++) for (var j = i + 2; j < ring.Count; j++)
                    if (!(i == 0 && j == ring.Count - 1) && SegmentsCross(ring[i], ring[(i + 1) % ring.Count], ring[j], ring[(j + 1) % ring.Count]))
                        throw new ArgumentException("Site boundary rings must not cross themselves.");
            }
            foreach (var hole in site.Boundary.Skip(1)) if (hole.Any(p => !InRing(site.Boundary[0], p.X, p.Y))) throw new ArgumentException("Site holes must lie inside their outer ring.");
            Refs(site.BuildingIds, buildings); Refs(site.DecorationIds, decorations); Refs(site.StaticModelIds, models); Refs(site.LinearFeatureIds, lines);
            members += site.BuildingIds.Count + site.DecorationIds.Count + site.StaticModelIds.Count + site.LinearFeatureIds.Count;
            if (members > 50000) throw new ArgumentException("Sites exceed the 50,000 membership project budget.");
        }
    }

    static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);
    static void Refs(List<string>? ids, HashSet<string> available)
    {
        if (ids is null || ids.Count > 10000 || ids.Any(id => !Text(id, 100) || !available.Contains(id)) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new ArgumentException("Site members must reference distinct objects present in this map. Shared or missing world objects are read-only evidence.");
    }
    static double Area(IReadOnlyList<SitePoint> ring) { double a = 0; for (var i = 0; i < ring.Count; i++) { var p = ring[i]; var q = ring[(i + 1) % ring.Count]; a += p.X * q.Y - q.X * p.Y; } return a / 2; }
    static double Cross(SitePoint a, SitePoint b, SitePoint c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    static bool SegmentsCross(SitePoint a, SitePoint b, SitePoint c, SitePoint d) => Cross(a,b,c)*Cross(a,b,d)<-1e-10 && Cross(c,d,a)*Cross(c,d,b)<-1e-10;
    static bool OnSegment(SitePoint a, SitePoint b, double x, double y) => Math.Abs(Cross(a,b,new(x,y))) < 1e-8 && x >= Math.Min(a.X,b.X)-1e-8 && x <= Math.Max(a.X,b.X)+1e-8 && y >= Math.Min(a.Y,b.Y)-1e-8 && y <= Math.Max(a.Y,b.Y)+1e-8;
    static bool InRing(IReadOnlyList<SitePoint> ring, double x, double y)
    {
        var inside = false;
        for (var i = 0; i < ring.Count; i++) { var a = ring[i]; var b = ring[(i+1)%ring.Count]; if (OnSegment(a,b,x,y)) return true; if ((a.Y>y)!=(b.Y>y) && x<(b.X-a.X)*(y-a.Y)/(b.Y-a.Y)+a.X) inside=!inside; }
        return inside;
    }
    public static bool Contains(SiteParcel site, double x, double y) => site.Boundary.Count > 0 && InRing(site.Boundary[0],x,y) && !site.Boundary.Skip(1).Any(r=>InRing(r,x,y));
    public static bool FullyContainsBuilding(SiteParcel site, Building building)
    {
        // Quantized cells, including the wall footprint, preserve courtyards and concave boundaries.
        foreach (var i in BuildingDesigner.Occupied(building)) if (!Contains(site, building.X+i%(building.Width+1), building.Y+i/(building.Width+1))) return false;
        return true;
    }
    public static bool FullyContainsLinear(SiteParcel site, LinearFeature feature, int width, int height)
    {
        if (LinearFeatureEditor.Sample(feature).Any(p => p.X-feature.Width/2 < 0 || p.Y-feature.Width/2 < 0 || p.X+feature.Width/2 >= width || p.Y+feature.Width/2 >= height)) return false;
        var cells=LinearFeatureEditor.Rasterize(feature,width,height);return cells.Count>0&&cells.All(i=>Contains(site,i%width+.5,i/width+.5));
    }
    public static IEnumerable<int> LockedCells(MapProject project)
    {
        long work=0;
        foreach (var site in project.Sites.Where(s=>s.Locked))
        {
            var outer=site.Boundary[0]; var left=Math.Max(0,(int)Math.Floor(outer.Min(p=>p.X))); var right=Math.Min(project.Width-1,(int)Math.Ceiling(outer.Max(p=>p.X)));
            var top=Math.Max(0,(int)Math.Floor(outer.Min(p=>p.Y))); var bottom=Math.Min(project.Height-1,(int)Math.Ceiling(outer.Max(p=>p.Y)));
            if(left>right||top>bottom)continue;
            work+=(long)(bottom-top+1)*(right-left+1)*site.Boundary.Sum(r=>r.Count);if(work>40_000_000)throw new ArgumentException("Pinned site geometry exceeds the generation work budget. Simplify the boundary or unpin large overlapping areas.");
            for(var y=top;y<=bottom;y++)for(var x=left;x<=right;x++)if(Contains(site,x+.5,y+.5))yield return y*project.Width+x;
        }
    }
}
