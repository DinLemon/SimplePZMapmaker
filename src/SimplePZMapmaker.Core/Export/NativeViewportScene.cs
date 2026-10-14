namespace SimplePZMapmaker.Core.Export;

/// <summary>A validated, read-only viewport source. Native cell composers are reused, never rebuilt per tile.</summary>
public sealed class NativeViewportScene
{
 public sealed record Sprite(int X,int Y,int Z,string Tile,int Span=1,int Layer=1);
 public sealed record Model(StaticModelPlacement Placement,int X,int Y,int Z);
 public sealed record Chunk(int X,int Y,int Size,int Stride,int MinLevel,int MaxLevel,List<Sprite> Sprites,List<Model> Models);
 private readonly MapProject project;
 private readonly Dictionary<(int,int),TileComposer> cells=[];
 private readonly Queue<(int,int)> order=[];
 private readonly Model[] models;
 public int Width=>project.Width;
 public int Height=>project.Height;
 public long EstimatedBytes=>project.Terrain.Length+project.Zombies.Length*4L+project.Buildings.Sum(b=>b.Design?.Floors.Sum(f=>f.Cells.Length*2L+f.Objects.Count*256L+f.Edges.Count*192L)??1024L)+project.Decorations.Count*256L+models.Length*512L;
 public int CachedCells=>cells.Count;
 private NativeViewportScene(MapProject project){this.project=project;models=StaticModelExporter.Gather(project).Select(m=>new Model(m.Placement,m.X-project.WorldX*256,m.Y-project.WorldY*256,m.Z)).Where(m=>m.X>=0&&m.Y>=0&&m.X<project.Width&&m.Y<project.Height).ToArray();}
 public static NativeViewportScene FromProject(MapProject project){ArgumentNullException.ThrowIfNull(project);ProjectValidator.Validate(project);return new(project);}
 public static NativeViewportScene FromWorld(WorldProject world,string memberId){ArgumentNullException.ThrowIfNull(world);WorldAssembler.Validate(world);var member=world.Maps.SingleOrDefault(m=>m.Id==memberId)??throw new ArgumentException("World section not found.");return new(WorldAssembler.ProjectSection(world,member));}
 public Chunk Read(int x,int y,int size=32,int stride=1,int? level=null,CancellationToken cancellation=default){
  if(x<0||y<0||size is <1 or >256||stride is not(1 or 2 or 4 or 8)||size%stride!=0||x>Width-size||y>Height-size||x%256+size>256||y%256+size>256||level is < ZLevels.Min or > ZLevels.Max)throw new ArgumentException("A viewport chunk must fit one native cell, with a valid signed floor and stride 1, 2, 4, or 8.");
  cancellation.ThrowIfCancellationRequested();var key=(x/256,y/256);
  lock(cells){
   cancellation.ThrowIfCancellationRequested();
   if(!cells.TryGetValue(key,out var composer)){composer=new(project,key.Item1,key.Item2);cells.Add(key,composer);order.Enqueue(key);while(order.Count>4)cells.Remove(order.Dequeue());}
   var min=level??composer.MinLevel;var max=level??composer.MaxLevel;var ox=key.Item1*256;var oy=key.Item2*256;var sprites=new List<Sprite>();
   // Coarse terrain samples keep the world overview bounded. Authored geometry is always exact.
   var authored=new bool[size*size];
   foreach(var b in project.Buildings){cancellation.ThrowIfCancellationRequested();var left=Math.Max(x,b.X);var right=Math.Min(x+size,b.X+b.Width+1);var top=Math.Max(y,b.Y);var bottom=Math.Min(y+size,b.Y+b.Height+1);if(right<=left||bottom<=top)continue;for(var yy=top;yy<bottom;yy++)Array.Fill(authored,true,(yy-y)*size+left-x,right-left);}
   foreach(var d in project.Decorations)foreach(var part in d.Parts){var xx=d.X+part.Dx-x;var yy=d.Y+part.Dy-y;if(xx>=0&&yy>=0&&xx<size&&yy<size)authored[yy*size+xx]=true;}
   void Add(int xx,int yy,int z,int span,int layer){var first=true;foreach(var tile in composer.At(xx-ox,yy-oy,z)){if(sprites.Count>=250000)throw new ArgumentException("Too many visible sprites. Select a single floor.");sprites.Add(new(xx,yy,z,tile,first?span:1,first?layer:1));first=false;}}
   if(min<=0&&max>=0)for(var yy=y;yy<y+size;yy+=stride){cancellation.ThrowIfCancellationRequested();for(var xx=x;xx<x+size;xx+=stride){
    if(stride==1){if(!authored[(yy-y)*size+xx-x])Add(xx,yy,0,1,0);continue;}
    // The coarse background uses terrain, never an enlarged authored room or wall.
    var theme=project.SurfaceTheme;foreach(var r in project.SurfaceRegions)if(xx>=r.X&&yy>=r.Y&&xx<r.X+r.Width&&yy<r.Y+r.Height)theme=r.Theme;
    var tile=project.SurfaceTiles.GetValueOrDefault(yy*Width+xx)??EnvironmentStyles.Floor(project.Terrain[yy*Width+xx],theme,EnvironmentStyles.Hash(xx,yy,project.Seed));sprites.Add(new(xx,yy,0,tile,stride,0));
    if(!authored[(yy-y)*size+xx-x])foreach(var extra in composer.At(xx-ox,yy-oy,0).Skip(1))sprites.Add(new(xx,yy,0,extra,1,1));
   }}
   for(var z=min;z<=max;z++)for(var yy=y;yy<y+size;yy++){cancellation.ThrowIfCancellationRequested();for(var xx=x;xx<x+size;xx++)if(authored[(yy-y)*size+xx-x])Add(xx,yy,z,1,1);}
   var placed=models.Where(m=>m.X>=x&&m.Y>=y&&m.X<x+size&&m.Y<y+size&&m.Z>=min&&m.Z<=max).ToList();
   return new(x,y,size,stride,min,max,sprites,placed);
  }
 }
}
