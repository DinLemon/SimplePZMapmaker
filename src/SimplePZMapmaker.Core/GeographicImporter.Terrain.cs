namespace SimplePZMapmaker.Core;

public static partial class GeographicImporter
{
    private readonly record struct TerrainScanEdge(double X,double Y,double OtherX,double OtherY,int End);

    private static void SpendTerrainWork(ref long work,long amount)
    {
        work+=amount;
        if(work>40_000_000)throw new ArgumentException("Terrain rasterization exceeds this section's processing budget. Use smaller sections while keeping the world area and metres per tile, or simplify overlapping terrain polygons. The original project was preserved.");
    }

    private static void FillPolygon(MapProject p,List<List<(double X,double Y)>> rings,Terrain terrain,CancellationToken token,ref long work)
    {
        // Bucket edges by the first pixel-centre scanline they cross. Large source
        // polygons stay exact, but their distant vertices are not visited on every row.
        var starts=new List<TerrainScanEdge>?[p.Height];var firstRow=p.Height;var endRow=0;
        foreach(var ring in rings){
            SpendTerrainWork(ref work,ring.Count);
            for(int i=0,j=ring.Count-1;i<ring.Count;j=i++){
                if((i&1023)==0)token.ThrowIfCancellationRequested();
                var a=ring[i];var b=ring[j];if(a.Y==b.Y)continue;
                var first=(int)Math.Clamp(Math.Ceiling(Math.Min(a.Y,b.Y)-.5),0,p.Height);
                var end=(int)Math.Clamp(Math.Ceiling(Math.Max(a.Y,b.Y)-.5),0,p.Height);
                if(first>=end)continue;
                (starts[first]??=[]).Add(new(a.X,a.Y,b.X,b.Y,end));
                firstRow=Math.Min(firstRow,first);endRow=Math.Max(endRow,end);
            }
        }
        var active=new List<TerrainScanEdge>();var intersections=new List<double>();
        for(var y=firstRow;y<endRow;y++){
            token.ThrowIfCancellationRequested();active.RemoveAll(edge=>edge.End<=y);
            if(starts[y] is {} incoming)active.AddRange(incoming);
            // Charge active-edge visits/sorting and actual painted spans, rather than
            // a whole source perimeter multiplied by a section-sized bounding box.
            SpendTerrainWork(ref work,(long)active.Count*(2+(active.Count>1?(int)Math.Ceiling(Math.Log2(active.Count)):0)));
            intersections.Clear();var scan=y+.5;
            foreach(var edge in active)intersections.Add(edge.X+(scan-edge.Y)*(edge.OtherX-edge.X)/(edge.OtherY-edge.Y));
            intersections.Sort();
            for(var i=0;i+1<intersections.Count;i+=2){
                var first=(int)Math.Clamp(Math.Ceiling(intersections[i]-.5),0,p.Width);
                var end=(int)Math.Clamp(Math.Ceiling(intersections[i+1]-.5),0,p.Width);
                SpendTerrainWork(ref work,end-first);
                Array.Fill(p.Terrain,terrain,y*p.Width+first,end-first);
            }
        }
    }
}
