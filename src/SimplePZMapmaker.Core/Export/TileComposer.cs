namespace SimplePZMapmaker.Core.Export;

internal sealed record RoomRect(int X,int Y,int Width,int Height);
internal sealed record Room(string Name,int X,int Y,int Width,int Height,int Building,int Level=0,List<RoomRect>? Rectangles=null);
internal sealed partial class TileComposer
{
    private readonly MapProject project;
    private readonly int ox,oy;
    private readonly Dictionary<int,List<string>> ground=[];
    private readonly HashSet<int> roofs=[];
    private readonly HashSet<int> decorationCells=[];
    private readonly HashSet<string> authoredTiles=new(StringComparer.Ordinal);
    private readonly string[]? regionThemes;
    internal int[] RoomIds {get;}=Enumerable.Repeat(-1,65536).ToArray();
    internal byte[] Navigation {get;}=new byte[65536];
    internal List<Room> Rooms {get;}=[];
    internal List<List<int>> Buildings {get;}=[];
    internal TileComposer(MapProject p,int cellX,int cellY)
    {
        project=p;ox=cellX*256;oy=cellY*256;
        if(p.SurfaceRegions.Count>0){regionThemes=new string[65536];Array.Fill(regionThemes,p.SurfaceTheme);foreach(var r in p.SurfaceRegions){var left=Math.Max(0,r.X-ox);var right=Math.Min(256,r.X+r.Width-ox);if(right<=left)continue;for(var y=Math.Max(0,r.Y-oy);y<Math.Min(256,r.Y+r.Height-oy);y++)Array.Fill(regionThemes,r.Theme,y*256+left,right-left);}}
        foreach(var d in p.Decorations)foreach(var part in d.Parts){var x=d.X+part.Dx-ox;var y=d.Y+part.Dy-oy;if(x>=0&&y>=0&&x<256&&y<256)decorationCells.Add(y*256+x);}
        for(var y=0;y<256;y++)for(var x=0;x<256;x++) {
            var terrain=p.Terrain[(oy+y)*p.Width+ox+x];
            Navigation[y*256+x]=terrain==Terrain.Water?(byte)8:(byte)0;
        }
        foreach(var b in p.Buildings.Where(b=>b.X<ox+256&&b.Y<oy+256&&b.X+b.Width>=ox&&b.Y+b.Height>=oy)){if(b.Design is null)Compose(b);else ComposeDesign(b);}
        foreach(var d in p.Decorations)foreach(var part in d.Parts){var x=d.X+part.Dx-ox;var y=d.Y+part.Dy-oy;if(x>=0&&y>=0&&x<256&&y<256)Add(x,y,part.Tile,part.Flags);}
    }
    private bool Tree(int x,int y) => !decorationCells.Contains(y*256+x)&&((uint)((ox+x)*73856093)^((uint)(oy+y)*19349663)^(uint)project.Seed)%19==0 && !project.Spawns.Any(s=>Math.Abs(s.X-ox-x)<=2&&Math.Abs(s.Y-oy-y)<=2);
    internal List<string> At(int x,int y,int z) {
        var index=y*256+x;
        if(layers.TryGetValue(z*65536+index,out var designed))return designed;
        if(z==1)return roofs.Contains(index)?["floors_interior_tilesandwood_01_40"]:[];
        if(z!=0)return [];
        if(ground.TryGetValue(index,out var overrideTiles))return overrideTiles;
        var t=project.Terrain[(oy+y)*project.Width+ox+x];
        var hash=EnvironmentStyles.Hash(ox+x,oy+y,project.Seed);var theme=regionThemes?[index]??project.SurfaceTheme;var custom=project.SurfaceTiles.GetValueOrDefault((oy+y)*project.Width+ox+x);var floor=custom??EnvironmentStyles.Floor(t,theme,hash);
        return custom is null&&t==Terrain.Forest&&Tree(x,y)?[floor,EnvironmentStyles.Tree(theme,hash)]:[floor];
    }
    private void Add(int x,int y,string tile,byte flags=0) {
        if(!InCell(x,y))return;
        authoredTiles.Add(tile);
        var i=y*256+x;if(!ground.ContainsKey(i))ground[i]=At(x,y,0);
        ground[i].Add(tile);Navigation[i]|=flags;
    }
    private void AddRoom(string name,int x,int y,int w,int h,List<int> building) {
        var endX=Math.Min(256,x+w);var endY=Math.Min(256,y+h);x=Math.Max(0,x);y=Math.Max(0,y);w=endX-x;h=endY-y;if(w<=0||h<=0)return;
        var id=Rooms.Count;building.Add(id);Rooms.Add(new(name,x,y,w,h,Buildings.Count));
        for(var yy=y;yy<y+h;yy++)for(var xx=x;xx<x+w;xx++){RoomIds[yy*256+xx]=id;Navigation[yy*256+xx]|=16;}
    }
    private void Compose(Building b)
    {
        var x=b.X-ox;var y=b.Y-oy;var w=b.Width;var h=b.Height;var mid=x+w/2;var half=y+h/2;var building=new List<int>();
        for(var yy=Math.Max(0,y);yy<Math.Min(256,y+h);yy++)for(var xx=Math.Max(0,x);xx<Math.Min(256,x+w);xx++) {
            ground[yy*256+xx]=["floors_interior_tilesandwood_01_40"];authoredTiles.Add("floors_interior_tilesandwood_01_40");Navigation[yy*256+xx]=0;roofs.Add(yy*256+xx);
        }
        if(b.Kind=="house") {
            AddRoom("livingroom",x,y,mid-x,h,building);
            AddRoom("kitchen",mid,y,x+w-mid,half-y,building);
            AddRoom("bedroom",mid,half,x+w-mid,y+h-half,building);
            for(var yy=y;yy<y+h;yy++) {
                var door=yy==y+2||yy==half+2;
                Add(mid,yy,door?"walls_exterior_house_01_10":"walls_exterior_house_01_0",4);
                if(door)Add(mid,yy,"fixtures_doors_01_0");
            }
            for(var xx=mid;xx<x+w;xx++)Add(xx,half,"walls_exterior_house_01_1",2);
            Add(mid+1,y+1,"fixtures_counters_01_0",1);
            Add(x+w-2,y+h-2,"furniture_storage_01_0",1);
            Add(x+1,y+h-2,"furniture_seating_indoor_01_0",1);
        } else {
            AddRoom(b.Kind=="shop"?"conveniencestore":"warehouse",x,y,w,h,building);
            Add(x+w-2,y+h-2,"carpentry_01_16",1);Add(x+w-3,y+h-2,"carpentry_01_16",1);
        }
        for(var xx=x;xx<x+w;xx++) {
            var door=xx==x+Math.Min(3,w/2);var window=xx==x+w-3&&!door;
            Add(xx,y,door?"walls_exterior_house_01_11":window?"walls_exterior_house_01_9":"walls_exterior_house_01_1",2);
            if(door)Add(xx,y,"fixtures_doors_01_1");if(window)Add(xx,y,"fixtures_windows_01_1");
            Add(xx,y+h,xx==x+2?"walls_exterior_house_01_9":"walls_exterior_house_01_1",2);
            if(xx==x+2)Add(xx,y+h,"fixtures_windows_01_1");
        }
        for(var yy=y;yy<y+h;yy++) {
            Add(x,yy,yy==y+3?"walls_exterior_house_01_8":"walls_exterior_house_01_0",4);
            if(yy==y+3)Add(x,yy,"fixtures_windows_01_0");
            Add(x+w,yy,"walls_exterior_house_01_0",4);
        }
        if(building.Count>0)Buildings.Add(building);
    }
    private static bool InCell(int x,int y)=>x>=0&&y>=0&&x<256&&y<256;
    internal HashSet<string> ResourceTiles(bool onlyAuthored=false){
        var ids=ground.Values.Concat(layers.Values).SelectMany(v=>v).ToHashSet(StringComparer.Ordinal);
        if(roofs.Count>0)ids.Add("floors_interior_tilesandwood_01_40");
        if(onlyAuthored){ids.IntersectWith(authoredTiles);return ids;}
        for(var y=0;y<256;y++)for(var x=0;x<256;x++){var local=y*256+x;if(ground.ContainsKey(local))continue;var global=(oy+y)*project.Width+ox+x;var terrain=project.Terrain[global];var hash=EnvironmentStyles.Hash(ox+x,oy+y,project.Seed);var theme=regionThemes?[local]??project.SurfaceTheme;var custom=project.SurfaceTiles.GetValueOrDefault(global);ids.Add(custom??EnvironmentStyles.Floor(terrain,theme,hash));if(custom is null&&terrain==Terrain.Forest&&Tree(x,y))ids.Add(EnvironmentStyles.Tree(theme,hash));}
        return ids;
    }
}
