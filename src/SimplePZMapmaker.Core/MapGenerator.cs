namespace SimplePZMapmaker.Core;
public static class MapGenerator
{
    public static MapProject Generate(GenerationOptions o)
    {
        if(o.Size is <256 or >2048 || o.Size%256!=0 || o.Kind is not ("town" or "wilderness" or "blank") || o.ForestDensity is <0 or >100 || o.BuildingDensity is <0 or >100 || o.ZombieDensity is <0 or >255) throw new ArgumentException("Check the map size, type, and object density.");
        var n=o.Size; var p=new MapProject {Name=o.Name,Width=n,Height=n,Seed=o.Seed,SurfaceTheme=o.SurfaceTheme,Terrain=new Terrain[n*n],Zombies=new int[n*n/64]};
        var random=new Random(o.Seed);
        if(o.Kind!="blank")
        {
            for(var y=0;y<n;y++) for(var x=0;x<n;x++) {
                var wave=Math.Sin(x/21.0+o.Seed%83)+Math.Cos(y/27.0-o.Seed%71)+Math.Sin((x+y)/43.0);
                p.Terrain[y*n+x]=wave>(1.9-o.ForestDensity/35.0) ? Terrain.Forest:Terrain.Grass;
                var river=n*0.79+Math.Sin(y/45.0+o.Seed%11)*n*0.025;
                if(Math.Abs(x-river)<5) p.Terrain[y*n+x]=Terrain.Water;
                else if(Math.Abs(x-river)<8) p.Terrain[y*n+x]=Terrain.Sand;
            }
            if(o.Kind=="town") {
                for(var y=24;y<n-16;y+=48) for(var x=8;x<n-8;x++) PaintRoad(p,x,y,5);
                for(var x=24;x<n*0.70;x+=48) for(var y=8;y<n-8;y++) PaintRoad(p,x,y,5);
                for(var y=34;y<n-24;y+=24) for(var x=34;x<n*0.7-20;x+=24) {
                    if(random.Next(100)>=o.BuildingDensity) continue;
                    var kind=BuildingDesigner.Kinds[random.Next(BuildingDesigner.Kinds.Length)];
                    var b=new Building{Id=$"generated-{o.Seed}-{x}-{y}",X=x,Y=y,Width=12+random.Next(4)*2,Height=10+random.Next(4)*2,Kind=kind,Name=BuildingDesigner.Names[kind]};
                    b.Design=BuildingDesigner.Generate(b,kind is "apartments" or "hotel"?random.Next(2,5):kind is "school" or "clinic"?2:1,random.Next(),new[]{"rect","l","t","annex"}[random.Next(4)]);
                    if(o.SurfaceTheme!="legacy")VisualStyles.Apply(b);
                    if(CanPlace(p,b)) {p.Buildings.Add(b);}
                }
            }
            Array.Fill(p.Zombies,o.ZombieDensity/12);
            foreach(var b in p.Buildings)for(var y=Math.Max(0,b.Y/8-2);y<=Math.Min(n/8-1,b.Y/8+2);y++)for(var x=Math.Max(0,b.X/8-2);x<=Math.Min(n/8-1,b.X/8+2);x++)p.Zombies[y*(n/8)+x]=o.ZombieDensity;
        }
        p.Spawns.Add(FindSafeSpawn(p)); ProjectValidator.Validate(p); return p;
    }
    private static void PaintRoad(MapProject p,int x,int y,int radius) {
        for(var yy=Math.Max(0,y-radius);yy<=Math.Min(p.Height-1,y+radius);yy++) for(var xx=Math.Max(0,x-radius);xx<=Math.Min(p.Width-1,x+radius);xx++) p.Terrain[yy*p.Width+xx]=Terrain.Road;
    }
    public static bool CanPlace(MapProject p,Building b,bool avoidRoads=true) {
        if(b.X<1||b.Y<1||b.X+b.Width>=p.Width||b.Y+b.Height>=p.Height) return false;
        var occupied=BuildingDesigner.Occupied(b).Select(i=>(b.Y+i/(b.Width+1))*p.Width+b.X+i%(b.Width+1)).ToHashSet();
        if(p.Buildings.Where(a=>a!=b&&b.X<a.X+a.Width+1&&b.X+b.Width+1>a.X&&b.Y<a.Y+a.Height+1&&b.Y+b.Height+1>a.Y).Any(a=>BuildingDesigner.Occupied(a).Any(i=>occupied.Contains((a.Y+i/(a.Width+1))*p.Width+a.X+i%(a.Width+1)))))return false;
        if(p.Decorations.Any(d=>d.Parts.Any(part=>occupied.Contains((d.Y+part.Dy)*p.Width+d.X+part.Dx))))return false;
        for(var y=0;y<b.Height;y++) for(var x=0;x<b.Width;x++) if(BuildingDesigner.Occupies(b,x,y)&&(p.Terrain[(b.Y+y)*p.Width+b.X+x]==Terrain.Water||avoidRoads&&p.Terrain[(b.Y+y)*p.Width+b.X+x]==Terrain.Road)) return false;
        return true;
    }
    public static SpawnPoint FindSafeSpawn(MapProject p) {
        var decor=p.Decorations.SelectMany(d=>d.Parts.Select(part=>(d.Y+part.Dy)*p.Width+d.X+part.Dx)).ToHashSet();
        for(var y=p.Height/2;y<p.Height;y++) for(var x=p.Width/2;x<p.Width;x++)
            if(p.Terrain[y*p.Width+x] is Terrain.Grass or Terrain.Road or Terrain.Pavement && !decor.Contains(y*p.Width+x)&&!p.Buildings.Any(b=>x>=b.X-1&&x<=b.X+b.Width+1&&y>=b.Y-1&&y<=b.Y+b.Height+1)) return new(){X=x,Y=y};
        for(var y=0;y<p.Height;y++) for(var x=0;x<p.Width;x++) if(p.Terrain[y*p.Width+x]!=Terrain.Water &&!decor.Contains(y*p.Width+x)&&!p.Buildings.Any(b=>x>=b.X-1&&x<=b.X+b.Width+1&&y>=b.Y-1&&y<=b.Y+b.Height+1)) return new(){X=x,Y=y};
        throw new ArgumentException("The map has no safe land for spawning.");
    }
}
