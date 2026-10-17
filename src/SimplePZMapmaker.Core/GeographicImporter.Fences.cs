using System.Globalization;
using System.Text.Json;
namespace SimplePZMapmaker.Core;

/// <summary>barrier=fence|wall lines become fence lines (docs/TRANSFER-CONTRACT.md, section 4): the projected path is walked along
/// cell edges (a diagonal becomes a staircase), pz:fence picks a style, otherwise fence_type picks a vanilla look; pz:gates and
/// barrier=gate points on the line become gates.</summary>
public static partial class GeographicImporter
{
    public static readonly FenceStyle[] DefaultFenceStyles=[
        new(){Id="vanilla-sheet",W="fencing_01_40",N="fencing_01_41",NW="fencing_01_42"},
        new(){Id="vanilla-planks",W="fencing_01_10",N="fencing_01_9",NW="fencing_01_12"},
        new(){Id="vanilla-picket",W="fencing_01_4",N="fencing_01_5",NW="fencing_01_6"},
        new(){Id="vanilla-wire",W="fencing_01_20",N="fencing_01_21",NW="fencing_01_28"}];
    /// <summary>The solid wooden wicket of the base game: closed W, closed N.</summary>
    public static readonly GateStyle DefaultWicket=new(){Id="vanilla-wicket",Width=1,ClosedW=["fixtures_doors_fences_01_12"],ClosedN=["fixtures_doors_fences_01_13"]};
    static readonly HashSet<string> FenceBarriers=["fence","wall","city_wall","retaining_wall"];
    static readonly HashSet<string> GateBarriers=["gate","wicket_gate","swing_gate","lift_gate","sliding_gate","hampshire_gate"];

    static string DefaultFenceStyle(Dictionary<string,string> tags)=>tags.GetValueOrDefault("fence_type") switch{
        "metal_sheet" or "sheet" or "corrugated" or "metal_bars" or "panel"=>"vanilla-sheet",
        "chain_link" or "wire" or "mesh" or "barbed_wire" or "electric"=>"vanilla-wire",
        "pickets" or "picket" or "split_rail" or "railing"=>"vanilla-picket",
        _=>tags.GetValueOrDefault("barrier") is "wall" or "city_wall" or "retaining_wall"?"vanilla-sheet":"vanilla-planks"};

    /// <summary>Walks a projected polyline over grid vertices with unit steps and merges them into horizontal/vertical runs.</summary>
    public static List<FencePoint> FenceStaircase(IReadOnlyList<(double X,double Y)> path){
        var result=new List<FencePoint>();if(path.Count==0)return result;
        int x=(int)Math.Round(path[0].X),y=(int)Math.Round(path[0].Y);result.Add(new(x,y));long steps=0;
        void Step(int nx,int ny){if(++steps>FenceLines.MaximumEdges)throw new ArgumentException("A fence path exceeds the edge budget.");
            if(result.Count>=2){var a=result[^2];var b=result[^1];if((a.X==b.X&&b.X==nx&&Math.Sign(b.Y-a.Y)==Math.Sign(ny-b.Y))||(a.Y==b.Y&&b.Y==ny&&Math.Sign(b.X-a.X)==Math.Sign(nx-b.X))){result[^1]=new(nx,ny);return;}}
            if(result.Count>=2&&result[^2]==new FencePoint(nx,ny)){result.RemoveAt(result.Count-1);return;}   // a step straight back cancels the previous one
            result.Add(new(nx,ny));}
        for(var i=1;i<path.Count;i++){
            int tx=(int)Math.Round(path[i].X),ty=(int)Math.Round(path[i].Y);int sx=x,sy=y;
            while(x!=tx||y!=ty){
                // step along the axis that keeps the walk closest to the straight segment
                var dx=tx-x;var dy=ty-y;bool horizontal;
                if(dx==0)horizontal=false;else if(dy==0)horizontal=true;else{
                    double Off(int px,int py){double vx=tx-sx,vy=ty-sy;return Math.Abs((px-sx)*vy-(py-sy)*vx)/Math.Sqrt(vx*vx+vy*vy);}
                    horizontal=Off(x+Math.Sign(dx),y)<=Off(x,y+Math.Sign(dy));}
                if(horizontal)x+=Math.Sign(dx);else y+=Math.Sign(dy);Step(x,y);
            }
        }
        return result;
    }

    static void ImportFences(MapProject p,List<Feature> transformed){
        var lines=new List<FenceLine>();var droppedGates=0;var skippedLines=0;
        var gatePoints=transformed.Where(f=>!f.Polygon&&f.Rings.Count==1&&f.Rings[0].Count==1&&GateBarriers.Contains(f.Tags.GetValueOrDefault("barrier",""))).Select(f=>f.Rings[0][0]).ToList();
        var mpt=p.MetresPerTile??1;
        foreach(var f in transformed.Where(f=>FenceBarriers.Contains(f.Tags.GetValueOrDefault("barrier",""))&&f.Rings.Count>0)){
            var sourceId=f.Tags.GetValueOrDefault("__pz_source");
            var requested=f.Tags.GetValueOrDefault("pz:fence");var style=requested is not null&&p.FenceStyles.Any(s=>s.Id==requested)?requested:DefaultFenceStyle(f.Tags);
            foreach(var (ring,index) in f.Rings.Select((r,i)=>(r,i))){
                if(ring.Max(v=>v.X)<0||ring.Max(v=>v.Y)<0||ring.Min(v=>v.X)>p.Width||ring.Min(v=>v.Y)>p.Height){skippedLines++;continue;}
                List<FencePoint> points;try{points=FenceStaircase(ring);}catch(ArgumentException){skippedLines++;continue;}
                if(points.Count<2||points.Count>FenceLines.MaximumPoints){skippedLines++;continue;}
                var line=new FenceLine{Id="geo-fence-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes((sourceId??string.Join(";",ring.Select(v=>v.X.ToString("R",CultureInfo.InvariantCulture)+","+v.Y.ToString("R",CultureInfo.InvariantCulture))))+"/"+index))).ToLowerInvariant()[..24],
                    Style=style,Points=points,SourceId=sourceId,SourceTags=f.Tags.Where(v=>v.Key.Length<=100&&!v.Key.StartsWith("__",StringComparison.Ordinal)).Take(47).ToDictionary(v=>v.Key,v=>v.Value[..Math.Min(300,v.Value.Length)])};
                if(f.Tags.TryGetValue("pz:posts",out var posts)&&int.TryParse(posts,out var spacing)&&spacing is >=0 and <=64)line.PostSpacing=spacing;
                var source=f.OriginalRings![index];line.SourcePath=(source.Count<=512?source:Enumerable.Range(0,512).Select(i=>source[(int)((long)i*(source.Count-1)/511)]).ToList()).Select(v=>new SourceOutlinePoint(v.X,v.Y)).ToList();
                // pz:gates: [{"at": metres along the source line, "width": edges, "style": gate style, "opens": "left"|"right"}]
                if(index==0&&f.Tags.TryGetValue("pz:gates",out var gatesJson)){
                    try{foreach(var g in JsonDocument.Parse(gatesJson).RootElement.EnumerateArray()){
                        var at=g.TryGetProperty("at",out var a)?a.GetDouble():0;var gateStyle=g.TryGetProperty("style",out var s)?s.GetString():null;
                        if(gateStyle is null||p.GateStyles.All(x=>x.Id!=gateStyle))gateStyle=DefaultWicket.Id;
                        if(NearestEdge(points,PointAlong(ring,at/mpt),2) is not {} gateEdge){droppedGates++;continue;}
                        line.Gates.Add(new(){At=gateEdge,Style=gateStyle!,OpensTo=g.TryGetProperty("opens",out var o)&&o.GetString()=="left"?"left":"right"});}}
                    catch(Exception e) when(e is JsonException or InvalidOperationException or FormatException){droppedGates++;}
                }
                // barrier=gate points within a tile of the fence: a wicket on the nearest edge
                foreach(var gp in gatePoints){var edge=NearestEdge(points,gp,1);if(edge is {} found&&line.Gates.All(g=>g.At!=found))line.Gates.Add(new(){At=found,Style=DefaultWicket.Id});}
                lines.Add(line);
            }
        }
        if(lines.Count==0)return;
        var styles=p.FenceStyles.Concat(DefaultFenceStyles.Where(d=>p.FenceStyles.All(s=>s.Id!=d.Id)&&lines.Any(l=>l.Style==d.Id))).ToList();
        var gates=p.GateStyles.Concat(lines.Any(l=>l.Gates.Any(g=>g.Style==DefaultWicket.Id))&&p.GateStyles.All(g=>g.Id!=DefaultWicket.Id)?[DefaultWicket]:[]).ToList();
        foreach(var line in lines){try{FenceLines.Validate([line],styles,gates,258048);}catch(ArgumentException){droppedGates+=line.Gates.Count;line.Gates=[];}}
        // with Preserve edits the previous fence layer was copied over: authored lines stay, source lines are refreshed
        var kept=p.Fences.Where(old=>old.SourceId is null||lines.All(l=>l.SourceId!=old.SourceId)).ToList();
        var total=kept.Concat(lines).ToList();if(total.Count>FenceLines.MaximumLines){skippedLines+=total.Count-FenceLines.MaximumLines;total=total.Take(FenceLines.MaximumLines).ToList();}
        p.Fences=total;p.FenceStyles=styles;p.GateStyles=gates;
        p.ImportSummary?.Notes.Add($"Fence lines from barrier=* paths: {lines.Count}" +(skippedLines>0?$", {skippedLines} outside the map or beyond limits":"")+(droppedGates>0?$", {droppedGates} gates dropped (unknown placement)":"")+". Diagonal fences follow cell edges as staircases.");
    }
    /// <summary>Moves imported fence lines whose edges reach more than one section into the world fence layer (absolute tiles), once.
    /// Every section imports the whole barrier way, so a crossing line arrives as one copy per section; gates found near each
    /// section are merged. Authored section lines (no source) are never moved.</summary>
    public static int PromoteSharedFences(WorldProject world,List<string> warnings){
        var copies=new Dictionary<string,List<(WorldMapMember Member,FenceLine Line)>>(StringComparer.Ordinal);
        foreach(var m in world.Maps)foreach(var f in m.Project.Fences.Where(f=>f.SourceId is not null)){if(!copies.TryGetValue(f.Id,out var list))copies[f.Id]=list=[];list.Add((m,f));}
        var promoted=0;var conflicts=0;var droppedGates=0;
        foreach(var (id,list) in copies){
            var (member,line)=list[0];var ox=member.Project.WorldX*256;var oy=member.Project.WorldY*256;
            var points=line.Points.Select(v=>new FencePoint(v.X+ox,v.Y+oy)).ToList();
            var owners=EdgeCells(points).Select(c=>WorldAssembler.MemberAt(world,c.X,c.Y)?.Id).Where(o=>o is not null).Distinct().ToList();
            // A line owned by one section keeps only that section's copy; neighbours that merely touch its end hold no edge of it.
            if(owners.Count==1){if(list.Any(c=>c.Member.Id==owners[0]))foreach(var (m,_) in list.Where(c=>c.Member.Id!=owners[0]))m.Project.Fences.RemoveAll(f=>f.Id==id);continue;}
            if(owners.Count==0)continue;
            if(world.Fences.Any(f=>f.Id==id)){conflicts++;continue;}
            var style=member.Project.FenceStyles.Single(s=>s.Id==line.Style);
            bool Same<T>(T a,T b)=>JsonSerializer.Serialize(a)==JsonSerializer.Serialize(b);
            if(world.FenceStyles.FirstOrDefault(s=>s.Id==style.Id) is {} known&&!Same(known,style)){conflicts++;continue;}
            var gates=new List<FenceGate>();
            foreach(var (copyMember,copy) in list){var shift=copy.Points.Select(v=>new FencePoint(v.X+copyMember.Project.WorldX*256,v.Y+copyMember.Project.WorldY*256)).SequenceEqual(points);
                foreach(var g in copy.Gates){if(!shift||gates.Any(o=>o.At==g.At)){droppedGates+=shift?0:1;continue;}gates.Add(new(){At=g.At,Style=g.Style,OpensTo=g.OpensTo,Open=g.Open});}}
            var gateStyles=gates.Select(g=>g.Style).Distinct().Select(g=>list.SelectMany(c=>c.Member.Project.GateStyles).First(s=>s.Id==g)).ToList();
            if(gateStyles.Any(g=>world.GateStyles.FirstOrDefault(s=>s.Id==g.Id) is {} known&&!Same(known,g))){conflicts++;continue;}
            var shared=new FenceLine{Id=id,Style=line.Style,PostSpacing=line.PostSpacing,Points=points,Gates=gates.OrderBy(g=>g.At).ToList(),SourceId=line.SourceId,SourceTags=new(line.SourceTags),SourcePath=[..line.SourcePath]};
            var styles=world.FenceStyles.Any(s=>s.Id==style.Id)?world.FenceStyles:[..world.FenceStyles,style];var gateLayer=world.GateStyles.Concat(gateStyles.Where(g=>world.GateStyles.All(s=>s.Id!=g.Id))).ToList();
            try{FenceLines.Validate([..world.Fences,shared],styles,gateLayer,258048);}catch(ArgumentException){droppedGates+=shared.Gates.Count;shared.Gates=[];try{FenceLines.Validate([..world.Fences,shared],styles,gateLayer,258048);}catch(ArgumentException){conflicts++;continue;}}
            world.Fences.Add(shared);world.FenceStyles=styles;world.GateStyles=gateLayer;foreach(var (m,_) in list)m.Project.Fences.RemoveAll(f=>f.Id==id);promoted++;
        }
        if(promoted>0)warnings.Add($"Fence lines crossing section boundaries moved to the shared world fence layer: {promoted}. Each is stored once in absolute tiles and drawn in every section it reaches.");
        if(conflicts>0)warnings.Add($"Crossing fence lines kept in their sections because a shared style, ID or world fence limit conflicts: {conflicts}.");
        if(droppedGates>0)warnings.Add($"Gates dropped while merging crossing fence lines: {droppedGates}.");
        return promoted;
    }
    /// <summary>The cell holding each edge of a grid walk (south of a horizontal edge, east of a vertical one), as in <see cref="FenceLines.Compose"/>.</summary>
    static IEnumerable<(int X,int Y)> EdgeCells(List<FencePoint> points){
        for(var s=1;s<points.Count;s++){var a=points[s-1];var b=points[s];var horizontal=a.Y==b.Y;var step=horizontal?Math.Sign(b.X-a.X):Math.Sign(b.Y-a.Y);var run=horizontal?Math.Abs(b.X-a.X):Math.Abs(b.Y-a.Y);
            for(var i=0;i<run;i++){var vx=horizontal?a.X+i*step:a.X;var vy=horizontal?a.Y:a.Y+i*step;yield return(horizontal?Math.Min(vx,vx+step):vx,horizontal?vy:Math.Min(vy,vy+step));}}
    }
    static (double X,double Y) PointAlong(List<(double X,double Y)> path,double distance){
        for(var i=1;i<path.Count;i++){var a=path[i-1];var b=path[i];var length=Math.Sqrt(Math.Pow(b.X-a.X,2)+Math.Pow(b.Y-a.Y,2));if(distance<=length||i==path.Count-1){var t=length>0?Math.Clamp(distance/length,0,1):0;return(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);}distance-=length;}
        return path[0];}
    /// <summary>The edge of a fence walk whose midpoint is nearest to a point, within a tolerance in tiles.</summary>
    static int? NearestEdge(List<FencePoint> points,(double X,double Y) at,double tolerance){int? best=null;var bestDistance=tolerance;var edge=0;
        for(var i=1;i<points.Count;i++){var a=points[i-1];var b=points[i];var run=Math.Abs(b.X-a.X)+Math.Abs(b.Y-a.Y);var sx=Math.Sign(b.X-a.X);var sy=Math.Sign(b.Y-a.Y);
            for(var k=0;k<run;k++,edge++){var mx=a.X+sx*(k+.5);var my=a.Y+sy*(k+.5);var d=Math.Sqrt(Math.Pow(mx-at.X,2)+Math.Pow(my-at.Y,2));if(d<=bestDistance){bestDistance=d;best=edge;}}}
        return best;}
}
