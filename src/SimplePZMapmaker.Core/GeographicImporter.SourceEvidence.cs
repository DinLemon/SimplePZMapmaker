using System.Security.Cryptography;
using System.Text;
namespace SimplePZMapmaker.Core;

public static partial class GeographicImporter
{
    sealed record EvidenceBox(GeographicFeature Feature,double MinX,double MinY,double MaxX,double MaxY);
    static EvidenceBox Box(GeographicFeature f){var ring=f.Geometry[0];return new(f,ring.Min(v=>v.X),ring.Min(v=>v.Y),ring.Max(v=>v.X),ring.Max(v=>v.Y));}
    static string SourceGeometryHash(List<List<(double X,double Y)>> rings,Dictionary<string,string> tags)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new{geometry=rings.Select(r=>r.Select(p=>new{longitude=p.X,latitude=p.Y})),tags})))).ToLowerInvariant()[..32];

    static void CaptureEvidence(MapProject p,List<Feature> features,MapProject previous,bool preserve,CancellationToken token)
    {
        var used=new HashSet<string>();
        foreach(var f in features){
            token.ThrowIfCancellationRequested();
            var tags=GeographicEvidence.CaptureTags(f.Tags);
            var kind=tags.ContainsKey("building")?"building":tags.ContainsKey("entrance")?"entrance":tags.ContainsKey("barrier")?"barrier":f.Polygon&&(tags.ContainsKey("landuse")||tags.ContainsKey("site")||tags.ContainsKey("leisure")||tags.ContainsKey("amenity")||tags.ContainsKey("healthcare"))?"site":tags.ContainsKey("amenity")||tags.ContainsKey("shop")||tags.ContainsKey("healthcare")?"poi":tags.Keys.Any(k=>k.StartsWith("addr:"))?"address":null;
            if(kind is null)continue;
            var original=f.OriginalRings!;
            var source=f.Tags.GetValueOrDefault("__pz_source")??"geo-sha-"+SourceGeometryHash(original,tags);
            var id="evidence-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..24].ToLowerInvariant();
            if(!used.Add(id))continue;
            var e=new GeographicFeature{Id=id,SourceId=source,Kind=kind,Name=tags.GetValueOrDefault("name",""),Tags=tags,GeometryKind=f.Polygon?"polygon":f.Rings[0].Count==1?"point":"line",SourceGeometry=original.Select(r=>r.Select(v=>new SourceOutlinePoint(v.X,v.Y)).ToList()).ToList(),Geometry=f.Rings.Select(r=>r.Select(v=>new SitePoint(v.X,v.Y)).ToList()).ToList()};
            e.BuildingId=p.Buildings.FirstOrDefault(b=>b.SourceId==source)?.Id;
            p.SourceFeatures.Add(e);
        }
        long work=0;bool Step(long amount=1){token.ThrowIfCancellationRequested();work+=amount;return work<=40_000_000;}
        var entranceBuildings=p.SourceFeatures.Where(e=>e.Kind=="building"&&e.GeometryKind=="polygon").Select(Box).ToArray();
        foreach(var e in p.SourceFeatures.Where(e=>e.Kind=="entrance")){if(Step())MapEntrance(p,e,entranceBuildings,Step);else e.EntranceOutcome="budget-exceeded";}
        var sourceBuildings=p.SourceFeatures.Where(e=>e.Kind=="building"&&e.GeometryKind=="polygon"&&e.BuildingId is not null).GroupBy(e=>e.BuildingId!).ToDictionary(g=>g.Key,g=>g.First());
        foreach(var e in p.SourceFeatures.Where(e=>e.Kind=="site"&&e.GeometryKind=="polygon").Take(SiteModel.MaximumSites)){
            var site=new SiteParcel{Id="site-"+e.Id,Name=string.IsNullOrEmpty(e.Name)?e.Tags.GetValueOrDefault("landuse",e.Tags.GetValueOrDefault("site","Mapped area")):e.Name,Origin="source",SourceId=e.SourceId,SourceFeatureId=e.Id,Boundary=e.Geometry.Select(r=>r.ToList()).ToList()};
            site.Name=site.Name[..Math.Min(100,site.Name.Length)];
            // Broad land-use areas describe use, never surveyed ownership. Membership is explicit containment.
            if(site.Boundary.Sum(r=>r.Count)>SiteModel.MaximumVertices)continue;
            var crossing=0;var outer=site.Boundary[0];var minX=outer.Min(v=>v.X);var maxX=outer.Max(v=>v.X);var minY=outer.Min(v=>v.Y);var maxY=outer.Max(v=>v.Y);
            foreach(var b in p.Buildings){
                if(!Step()){site.AssociationNotes.Add("Association budget reached; unchecked members remain unknown.");break;}
                if(b.X+b.Width<minX||b.Y+b.Height<minY||b.X>maxX||b.Y>maxY)continue;
                if(!sourceBuildings.TryGetValue(b.Id,out var originalBuilding))continue;
                if(!SourceFits(site,originalBuilding.Geometry,Step)){if(work>40_000_000){site.AssociationNotes.Add("Association budget reached; unchecked members remain unknown.");break;}crossing++;continue;}
                if(!Step((long)(b.Width+1)*(b.Height+1)*site.Boundary.Sum(r=>r.Count)+(b.Design?.Floors.Count??1)*b.Width*b.Height)){site.AssociationNotes.Add("Association budget reached; unchecked members remain unknown.");break;}
                if(SiteModel.FullyContainsBuilding(site,b))site.BuildingIds.Add(b.Id);else crossing++;
            }
            if(crossing>0)site.AssociationNotes.Add($"Crossing or quantized boundary buildings excluded from this grouping: {crossing}.");
            p.Sites.Add(site);
        }
        if(preserve){
            foreach(var old in previous.Sites.Where(s=>s.Origin!="source"||s.Locked)){
                p.Sites.RemoveAll(s=>s.Id==old.Id);var kept=NeighborhoodPlanner.Copy(old);
                kept.BuildingIds=kept.BuildingIds.Select(id=>p.Buildings.Any(b=>b.Id==id)?id:previous.Buildings.FirstOrDefault(b=>b.Id==id)?.SourceId is {} oldSource?p.Buildings.FirstOrDefault(b=>b.SourceId==oldSource)?.Id:null).Where(id=>id is not null).Cast<string>().Distinct().ToList();kept.DecorationIds.RemoveAll(id=>!p.Decorations.Any(b=>b.Id==id));kept.StaticModelIds.RemoveAll(id=>!p.StaticModels.Any(b=>b.Id==id));kept.LinearFeatureIds.RemoveAll(id=>!p.LinearFeatures.Any(b=>b.Id==id));
                p.Sites.Add(kept);
            }
            foreach(var b in p.Buildings.Where(b=>b.Modified||b.FootprintOrigin=="manual-source-edit"))foreach(var e in p.SourceFeatures.Where(e=>e.BuildingId==b.Id)){e.Modified=true;e.Origin="source-edited";}
        }
        GeographicEvidence.Validate(p);SiteModel.Validate(p);
    }

    static void MapEntrance(MapProject p,GeographicFeature e,IReadOnlyList<EvidenceBox> buildings,Func<long,bool> step)
    {
        var role=e.Tags.GetValueOrDefault("entrance");
        if(role=="no"){e.EntranceOutcome="sealed";return;}
        if(e.Tags.TryGetValue("level",out var level)&&level!="0"||e.Tags.GetValueOrDefault("indoor")=="yes"){e.EntranceOutcome="unsupported-level";return;}
        if(role is "exit" or "emergency"||e.GeometryKind!="point"){e.EntranceOutcome="unsupported-role";return;}
        var point=e.Geometry[0][0];
        // Match original geographic boundary, not the much looser quantized bounding rectangle.
        var candidates=new List<GeographicFeature>();
        foreach(var box in buildings){
            if(!step(1)){e.EntranceOutcome="budget-exceeded";return;}var f=box.Feature;
            if(point.X<box.MinX-.25||point.X>box.MaxX+.25||point.Y<box.MinY-.25||point.Y>box.MaxY+.25)continue;
            if(!step(f.Geometry.Sum(r=>r.Count))){e.EntranceOutcome="budget-exceeded";return;}
            if(f.Geometry.Any(r=>r.Zip(r.Skip(1)).Any(v=>SegmentDistance(point,v.First,v.Second)<=.25)))candidates.Add(f);
        }
        if(candidates.Count!=1){e.EntranceOutcome=candidates.Count>1?"ambiguous":"unassociated";return;}
        e.BuildingId=candidates[0].BuildingId;var b=p.Buildings.FirstOrDefault(b=>b.Id==e.BuildingId);
        if(b?.Design is null){e.EntranceOutcome="unassociated";return;}
        if(b.Modified){e.EntranceOutcome="edited-building";return;}
        var floor=b.Design.Floors[-b.Design.MinLevel];if(!step(floor.Edges.Count+floor.Objects.Count*32L)){e.EntranceOutcome="budget-exceeded";return;}
        var edges=floor.Edges.Where(v=>(BuildingDesigner.Cell(b,floor,v.X,v.Y)=='.')!=(BuildingDesigner.Cell(b,floor,v.X-(v.Edge=="w"?1:0),v.Y-(v.Edge=="n"?1:0))=='.')).Select(v=>(Edge:v,Distance:SegmentDistance(point,new(b.X+v.X,b.Y+v.Y),new(b.X+v.X+(v.Edge=="n"?1:0),b.Y+v.Y+(v.Edge=="w"?1:0))))).OrderBy(v=>v.Distance).ToArray();
        if(edges.Length==0||edges[0].Distance>1.5){e.EntranceOutcome="unsupported-edge";return;}
        if(edges.Length>1&&Math.Abs(edges[0].Distance-edges[1].Distance)<1e-6){e.EntranceOutcome="ambiguous";return;}
        var edge=edges[0].Edge;
        // Private/restricted access needs gameplay semantics the importer cannot represent safely yet.
        if(e.Tags.GetValueOrDefault("access") is "no" or "private" or "customers" or "permit"){e.EntranceOutcome="unsupported-role";return;}
        var adjacent=floor.Objects.Any(o=>BuildingObjects.Cells(o).Any(c=>Math.Abs(c.X-edge.X)+Math.Abs(c.Y-edge.Y)<=1));
        if(adjacent){e.EntranceOutcome="blocked";return;}
        edge.Kind="door";edge.Edited=true;e.EntranceOutcome="mapped";
    }
    static bool SourceFits(SiteParcel site,List<List<SitePoint>> building,Func<long,bool> step){
        var source=new SiteParcel{Boundary=building};
        foreach(var point in building[0]){if(!step(site.Boundary.Sum(r=>r.Count))||!SiteModel.Contains(site,point.X,point.Y))return false;}
        foreach(var hole in site.Boundary.Skip(1)){if(!step(building.Sum(r=>r.Count))||SiteModel.Contains(source,hole[0].X,hole[0].Y))return false;}
        static double Cross(SitePoint a,SitePoint b,SitePoint c)=>(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
        foreach(var edge in building[0].Zip(building[0].Skip(1)))foreach(var ring in site.Boundary)foreach(var boundary in ring.Zip(ring.Skip(1))){if(!step(1))return false;var a=edge.First;var b=edge.Second;var c=boundary.First;var d=boundary.Second;if(Cross(a,b,c)*Cross(a,b,d)<-1e-10&&Cross(c,d,a)*Cross(c,d,b)<-1e-10)return false;}
        return true;
    }
    internal static double SegmentDistance(SitePoint p,SitePoint a,SitePoint b){var dx=b.X-a.X;var dy=b.Y-a.Y;var t=dx*dx+dy*dy==0?0:Math.Clamp(((p.X-a.X)*dx+(p.Y-a.Y)*dy)/(dx*dx+dy*dy),0,1);return Math.Sqrt(Math.Pow(p.X-a.X-t*dx,2)+Math.Pow(p.Y-a.Y-t*dy,2));}
}
