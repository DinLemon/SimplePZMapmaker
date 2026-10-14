namespace SimplePZMapmaker.Core;

/// <summary>Reusable modules are ordinary, valid building templates; assembly bakes into ordinary floors.</summary>
public static class BuildingModules
{
 public const string Prefix="module:";
 public static readonly string[] Roles=["entrance","section","annex","stairs"];
 public static bool IsModule(Building b)=>b.SourceKind is {} kind&&kind.StartsWith(Prefix,StringComparison.Ordinal)&&Roles.Contains(kind[Prefix.Length..]);
 public static Building Capture(Building source,int x,int y,int width,int height,int minLevel,int maxLevel,string name,string role){
  BuildingDesigner.Validate(source,false);
  if(source.Design is null||!Roles.Contains(role)||string.IsNullOrWhiteSpace(name)||name.Length>100||name.Any(char.IsControl))throw new ArgumentException("Choose a module role and a name of 1–100 characters.");
  if(width is <6 or >64||height is <6 or >64||x<0||y<0||(long)x+width>source.Width||(long)y+height>source.Height||minLevel<source.Design.MinLevel||maxLevel>=source.Design.MinLevel+source.Design.Floors.Count||minLevel>maxLevel||maxLevel-minLevel>=64)throw new ArgumentException("Capture a rectangle of 6–64 tiles inside the building and consecutive floors within z=-32..31.");
  var relativeMin=minLevel<=0&&maxLevel>=0?minLevel:0;
  var result=WorldAssembler.CloneBuilding(source);result.Id=Guid.NewGuid().ToString("N");result.Name=name.Trim();result.X=result.Y=0;result.Width=width;result.Height=height;result.SourceId=null;result.SourceKind=Prefix+role;result.SourceOutline=[];result.FootprintOrigin="authored";result.Modified=false;result.LevelsOrigin="manual";
  result.Design!.MinLevel=relativeMin;result.Design.Shape="custom";result.Design.Floors=[];
  bool Inside(int xx,int yy)=>xx>=x&&yy>=y&&xx<x+width&&yy<y+height;
  for(var z=minLevel;z<=maxLevel;z++){
   var original=source.Design.Floors[z-source.Design.MinLevel];var f=new BuildingFloor{Rooms=new(original.Rooms),FloorTile=original.FloorTile};
   f.Cells=string.Concat(Enumerable.Range(y,height).Select(yy=>original.Cells.Substring(yy*source.Width+x,width)));
   f.Rooms=original.Rooms.Where(r=>f.Cells.Contains(r.Key,StringComparison.Ordinal)).ToDictionary(r=>r.Key,r=>r.Value);
   foreach(var entry in original.FloorTiles)if(Inside(entry.Key%source.Width,entry.Key/source.Width))f.FloorTiles[(entry.Key/source.Width-y)*width+entry.Key%source.Width-x]=entry.Value;
   foreach(var o in original.Objects){var points=BuildingObjects.Cells(o).ToArray();if(!points.Any(p=>Inside(p.X,p.Y)))continue;if(points.Any(p=>!Inside(p.X,p.Y))||o.Kind=="stairs"&&z==maxLevel)throw new ArgumentException("The selection cuts furniture or a staircase transition. Include the complete object and its upper landing floor.");var copy=CloneObject(o);copy.X-=x;copy.Y-=y;f.Objects.Add(copy);}
   foreach(var m in original.StaticModels){var points=StaticModelPlacement.FootprintCells(m).Select(c=>(X:m.X+c.Dx,Y:m.Y+c.Dy)).ToArray();if(!points.Any(p=>Inside(p.X,p.Y)))continue;if(points.Any(p=>!Inside(p.X,p.Y)))throw new ArgumentException("The selection cuts a model footprint.");var copy=CloneModel(m);copy.Id=Guid.NewGuid().ToString("N");copy.X-=x;copy.Y-=y;copy.Z=z-minLevel+relativeMin;f.StaticModels.Add(copy);}
   foreach(var edge in original.Edges){var e=CloneEdge(edge);e.X-=x;e.Y-=y;if(e.X<0||e.Y<0||e.X>width||e.Y>height)continue;if(BuildingDesigner.Cell(result,f,e.X,e.Y)=='.'&&(e.Edge=="n"?BuildingDesigner.Cell(result,f,e.X,e.Y-1):BuildingDesigner.Cell(result,f,e.X-1,e.Y))=='.')continue;f.Edges.Add(e);}
   result.Design.Floors.Add(f);
   var existing=f.Edges.Select(Key).ToHashSet();foreach(var e in BuildingDesigner.AutoEdges(result,f,z-minLevel+relativeMin))if(existing.Add(Key(e)))f.Edges.Add(e);
  }
  var ground=result.Design.Floors[-relativeMin];if(!ground.Edges.Any(e=>e.Kind is "door" or "open"&&Exterior(result,ground,e))){var door=ground.Edges.FirstOrDefault(e=>Exterior(result,ground,e)&&Clear(result,-relativeMin,e))??throw new ArgumentException("No clear exterior entrance can be added to this module.");door.Kind="door";door.WallTile=door.OpeningTile=null;}
  RoofGeometry.SelectSafeAutomaticRoof(result);BuildingDesigner.Validate(result);return result;
 }

 public static Building Transform(Building module,int quarterTurns=0,bool mirror=false,Func<string,string,string,string>? edgeTile=null){
  BuildingDesigner.Validate(module);if(module.Design is null||quarterTurns is <0 or >3)throw new ArgumentException("A module needs a design and a rotation of 0, 90, 180 or 270 degrees.");
  var result=WorldAssembler.CloneBuilding(module);if(quarterTurns==0&&!mirror)return result;
  if(module.Design.Floors.Any(f=>f.Objects.Any(o=>o.Kind=="asset")||f.StaticModels.Any(m=>m.Door is not null)))throw new ArgumentException("This module contains grouped game sprites or a native model door. Their facing cannot be transformed safely; author the required orientation before saving it.");
  var oldW=module.Width;var oldH=module.Height;result.Width=quarterTurns%2==0?oldW:oldH;result.Height=quarterTurns%2==0?oldH:oldW;
  (int X,int Y) Grid(int x,int y,bool vertex=false){var w=vertex?oldW:oldW-1;var h=vertex?oldH:oldH-1;if(mirror)x=w-x;return quarterTurns switch{1=>(h-y,x),2=>(w-x,h-y),3=>(y,w-x),_=>(x,y)};}
  foreach(var f in result.Design!.Floors){
   var cells=new char[f.Cells.Length];var tiles=new Dictionary<int,string>();for(var y=0;y<oldH;y++)for(var x=0;x<oldW;x++){var p=Grid(x,y);cells[p.Y*result.Width+p.X]=f.Cells[y*oldW+x];if(f.FloorTiles.TryGetValue(y*oldW+x,out var tile))tiles[p.Y*result.Width+p.X]=tile;}f.Cells=new(cells);f.FloorTiles=tiles;
   foreach(var e in f.Edges){var a=Grid(e.X,e.Y,true);var b=Grid(e.X+(e.Edge=="n"?1:0),e.Y+(e.Edge=="w"?1:0),true);var direction=a.Y==b.Y?"n":"w";if(direction!=e.Edge){string? Pair(string? tile,string role)=>tile is null?null:edgeTile is null?throw new ArgumentException("Directional wall/opening resources need a verified catalog pair before rotating this module."):edgeTile(tile,direction,role);e.WallTile=Pair(e.WallTile,e.Kind=="door"?"doorwall":e.Kind=="window"?"windowwall":"wall");e.OpeningTile=Pair(e.OpeningTile,e.Kind);}e.X=Math.Min(a.X,b.X);e.Y=Math.Min(a.Y,b.Y);e.Edge=direction;}
   foreach(var o in f.Objects){var anchor=Grid(o.X,o.Y);var facing=Grid(o.X+(o.Direction=="w"?1:0),o.Y+(o.Direction=="n"?1:0));if(facing.X<anchor.X||facing.Y<anchor.Y)throw new ArgumentException(o.Kind=="stairs"?"This transform reverses stair ascent. Native stairs only support north/west runs; choose another transform.":"This transform needs a south/east furniture variant. Author that orientation with registered game sprites before saving the module.");var points=BuildingObjects.Cells(o).Select(p=>Grid(p.X,p.Y)).ToArray();o.X=points.Min(p=>p.X);o.Y=points.Min(p=>p.Y);o.Direction=facing.X>anchor.X?"w":"n";}
   foreach(var m in f.StaticModels){var oldX=m.X;var oldY=m.Y;var anchor=Grid(oldX,oldY);var points=StaticModelPlacement.FootprintCells(m).Select(c=>Grid(oldX+c.Dx,oldY+c.Dy)).ToArray();if(points.Any(p=>p.X<anchor.X||p.Y<anchor.Y))throw new ArgumentException("This transform moves the model mesh anchor outside its positive footprint. Author an explicit model variant and anchor for that orientation.");m.X=anchor.X;m.Y=anchor.Y;if(m.Footprint.Count>0)m.Footprint=points.Select(p=>new ModelFootprintCell(p.X-m.X,p.Y-m.Y)).ToList();var ox=mirror?1-m.OffsetX:m.OffsetX;var oy=m.OffsetY;(m.OffsetX,m.OffsetY)=quarterTurns switch{1=>(1-oy,ox),2=>(1-ox,1-oy),3=>(oy,1-ox),_=>(ox,oy)};m.Rotation=((mirror?360-m.Rotation:m.Rotation)+quarterTurns*90)%360;if(mirror)throw new ArgumentException("Static model geometry has no mirrored asset. Use a rotation or author a separate mirrored module.");}
  }
  BuildingDesigner.Validate(result);return result;
 }

 public static Building Compose(Building building,Building module,int x,int y,int level,bool autoConnect=true,bool expand=true){
  BuildingDesigner.Validate(building);BuildingDesigner.Validate(module);if(building.Design is null||module.Design is null)throw new ArgumentException("Both building and module need authored floors.");
  var moduleMin=(long)level+module.Design.MinLevel;var moduleMax=moduleMin+module.Design.Floors.Count-1;if(x<0||y<0||(long)x+module.Width>64||(long)y+module.Height>64||moduleMin<ZLevels.Min||moduleMax>ZLevels.Max)throw new ArgumentException("The module exceeds the 64×64 building envelope or signed z=-32..31 range.");
  if(!expand&&((long)x+module.Width>building.Width||(long)y+module.Height>building.Height||moduleMin<building.Design.MinLevel||moduleMax>=building.Design.MinLevel+building.Design.Floors.Count))throw new ArgumentException("The module extends outside the building. Enable expansion or choose coordinates inside the current envelope.");
  var result=WorldAssembler.CloneBuilding(building);var d=result.Design!;var oldW=result.Width;var oldH=result.Height;result.Width=Math.Max(result.Width,x+module.Width);result.Height=Math.Max(result.Height,y+module.Height);
  foreach(var f in d.Floors){var cells=Enumerable.Repeat('.',result.Width*result.Height).ToArray();for(var yy=0;yy<oldH;yy++)for(var xx=0;xx<oldW;xx++)cells[yy*result.Width+xx]=f.Cells[yy*oldW+xx];f.Cells=new(cells);f.FloorTiles=f.FloorTiles.ToDictionary(e=>(e.Key/oldW)*result.Width+e.Key%oldW,e=>e.Value);}
  var min=Math.Min(d.MinLevel,(int)moduleMin);var max=Math.Max(d.MinLevel+d.Floors.Count-1,(int)moduleMax);var floors=new List<BuildingFloor>();for(var z=min;z<=max;z++)floors.Add(z>=d.MinLevel&&z<d.MinLevel+d.Floors.Count?d.Floors[z-d.MinLevel]:new(){Cells=new string('.',result.Width*result.Height),Rooms=new(){["0"]="hall"}});d.MinLevel=min;d.Floors=floors;
  for(var i=0;i<module.Design.Floors.Count;i++){
   var source=module.Design.Floors[i];var index=(int)moduleMin+i-min;var target=floors[index];var chars=target.Cells.ToCharArray();var ownership=new HashSet<(int X,int Y)>();var mapping=new Dictionary<char,char>();
   foreach(var code in source.Cells.Where(c=>c!='.').Distinct()){var next="0123456789abcdefghijklmnopqrstuvwxyz".FirstOrDefault(c=>!target.Rooms.ContainsKey(c.ToString()));if(next=='\0'||target.Rooms.Count>=35)throw new ArgumentException("The composed floor exceeds 35 room identities. Remove unused rooms before adding another module.");mapping[code]=next;target.Rooms[next.ToString()]=source.Rooms[code.ToString()];}
   for(var yy=0;yy<module.Height;yy++)for(var xx=0;xx<module.Width;xx++){var sourceIndex=yy*module.Width+xx;if(source.Cells[sourceIndex]=='.')continue;var pos=(yy+y)*result.Width+xx+x;if(chars[pos]!='.')throw new ArgumentException($"Module rooms overlap at ({xx+x},{yy+y},z={moduleMin+i}).");chars[pos]=mapping[source.Cells[sourceIndex]];ownership.Add((xx+x,yy+y));if(source.FloorTiles.TryGetValue(sourceIndex,out var tile)||((tile=source.FloorTile) is not null))target.FloorTiles[pos]=tile!;}
   target.Cells=new(chars);
   foreach(var o in source.Objects){var copy=CloneObject(o);copy.X+=x;copy.Y+=y;target.Objects.Add(copy);}foreach(var m in source.StaticModels){var copy=CloneModel(m);copy.Id=Guid.NewGuid().ToString("N");copy.X+=x;copy.Y+=y;copy.Z=(int)moduleMin+i;target.StaticModels.Add(copy);}
   foreach(var e in source.Edges){var copy=CloneEdge(e);copy.X+=x;copy.Y+=y;var prior=target.Edges.FindIndex(v=>Key(v)==Key(copy));if(prior>=0)target.Edges[prior]=copy;else target.Edges.Add(copy);}
   if(autoConnect){var seams=target.Edges.Where(e=>{var a=(e.X,e.Y);var b=e.Edge=="n"?(e.X,e.Y-1):(e.X-1,e.Y);return BuildingDesigner.Cell(result,target,a.Item1,a.Item2)!='.'&&BuildingDesigner.Cell(result,target,b.Item1,b.Item2)!='.'&&ownership.Contains(a)!=ownership.Contains(b);}).ToArray();var joined=new HashSet<string>();foreach(var e in seams){var a=BuildingDesigner.Cell(result,target,e.X,e.Y);var b=e.Edge=="n"?BuildingDesigner.Cell(result,target,e.X,e.Y-1):BuildingDesigner.Cell(result,target,e.X-1,e.Y);var pair=a<b?$"{a}{b}":$"{b}{a}";if(joined.Contains(pair)||!Clear(result,index,e))continue;e.Kind="door";e.Edited=true;e.WallTile=e.OpeningTile=null;joined.Add(pair);}}
  }
  result.RequiredMods=result.RequiredMods.Concat(module.RequiredMods).Distinct(StringComparer.Ordinal).ToList();foreach(var (key,value) in module.AssetSources){if(result.AssetSources.TryGetValue(key,out var prior)&&prior!=value)throw new ArgumentException("The modules use conflicting asset sources: "+key);result.AssetSources[key]=value;}result.Attribution=result.Attribution.Concat(module.Attribution).Distinct().ToList();d.Shape="custom";result.Modified=true;result.LevelsOrigin="manual";BuildingDesigner.Validate(result);return result;
 }
 static string Key(BuildingEdge e)=>$"{e.X},{e.Y},{e.Edge}";
 static bool Exterior(Building b,BuildingFloor f,BuildingEdge e)=>(BuildingDesigner.Cell(b,f,e.X,e.Y)=='.')!=((e.Edge=="n"?BuildingDesigner.Cell(b,f,e.X,e.Y-1):BuildingDesigner.Cell(b,f,e.X-1,e.Y))=='.');
 static bool Clear(Building b,int index,BuildingEdge e){(int X,int Y)[] points=[(e.X,e.Y),e.Edge=="n"?(e.X,e.Y-1):(e.X-1,e.Y)];return points.All(p=>!BuildingDesigner.StairSpace(b,index,p.X,p.Y)&&!b.Design!.Floors[index].Objects.Where(o=>o.Kind!="stairs").SelectMany(BuildingObjects.Cells).Contains(p)&&!b.Design.Floors[index].StaticModels.Where(m=>StaticModelPlacement.BlocksTiles(m)||m.Door is not null).Any(m=>StaticModelPlacement.FootprintCells(m).Any(c=>m.X+c.Dx==p.X&&m.Y+c.Dy==p.Y)));}
 static T Clone<T>(T value)=>System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(value))!;
 static BuildingObject CloneObject(BuildingObject o)=>Clone(o);
 static StaticModelPlacement CloneModel(StaticModelPlacement m)=>Clone(m);
 static BuildingEdge CloneEdge(BuildingEdge e)=>Clone(e);
}
