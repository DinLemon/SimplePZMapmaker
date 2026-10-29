using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;

/// <summary>Layout-only neighbour records reserve geometry; terrain is sampled from at most one extra section.</summary>
internal static class WorldSectionProjection
{
 internal static WorldSectionArchive.Metadata Context(WorldSectionArchive.Metadata metadata,MapProject project){
  var ox=project.WorldX*256;var oy=project.WorldY*256;
  bool BuildingTouches(Building b)=>b.X<ox+project.Width&&b.Y<oy+project.Height&&b.X+b.Width>=ox&&b.Y+b.Height>=oy;
  bool PathTouches(LinearFeature f){var pad=Math.Ceiling(f.Width/2)+1+(f.Access=="stairs"?4*Math.Abs(f.Level):0);return f.Points.Min(p=>p.X)-pad<ox+project.Width&&f.Points.Max(p=>p.X)+pad>=ox&&f.Points.Min(p=>p.Y)-pad<oy+project.Height&&f.Points.Max(p=>p.Y)+pad>=oy;}
  var fences=metadata.Fences.Where(f=>f.Points.Max(v=>v.X)>=ox&&f.Points.Min(v=>v.X)<=ox+project.Width&&f.Points.Max(v=>v.Y)>=oy&&f.Points.Min(v=>v.Y)<=oy+project.Height).ToList();var styles=fences.Select(f=>f.Style).ToHashSet(StringComparer.Ordinal);var gates=fences.SelectMany(f=>f.Gates.Select(g=>g.Style)).ToHashSet(StringComparer.Ordinal);
  return new(){Version=metadata.Version,Id=metadata.Id,Name=metadata.Name,GeoReference=metadata.GeoReference,Buildings=metadata.Buildings.Where(BuildingTouches).ToList(),LinearFeatures=metadata.LinearFeatures.Where(PathTouches).ToList(),Fences=fences,FenceStyles=metadata.FenceStyles.Where(s=>styles.Contains(s.Id)).ToList(),GateStyles=metadata.GateStyles.Where(s=>gates.Contains(s.Id)).ToList(),Decorations=metadata.Decorations.Where(d=>WorldAssembler.DecorationsFor([d],project).Count>0).ToList()};
 }
 internal static async Task<MapProject> Project(WorldSectionArchive.RevisionLease lease,WorldSectionArchive.SectionReference section,MapProject project,WorldSectionArchive.Metadata metadata,CancellationToken token){
  ProjectValidator.Validate(project);WorldSectionArchive.ValidateSharedReplacement(metadata,project);var context=Context(metadata,project);
  var layout=new WorldProject{Id=metadata.Id,Name=metadata.Name,Version=metadata.Version,Buildings=context.Buildings,LinearFeatures=context.LinearFeatures,Fences=context.Fences,FenceStyles=context.FenceStyles,GateStyles=context.GateStyles,Decorations=context.Decorations,GeoReference=metadata.GeoReference};
  foreach(var s in lease.PinnedManifest.Sections)layout.Maps.Add(new(){Id=s.Id,Project=s.Id==section.Id?project:new(){WorldX=s.WorldX,WorldY=s.WorldY,Width=s.Width,Height=s.Height,Name=s.Name}});
  // Build uses layout rectangles for coverage and asks for actual terrain only at support/landing cells.
  // It never receives invented grass for an unloaded section.
  MapProject? sampled=null;var samples=new Dictionary<(int,int),Terrain>();
  Terrain Ground(int x,int y){token.ThrowIfCancellationRequested();if(samples.TryGetValue((x,y),out var known))return known;var s=lease.PinnedManifest.Sections.SingleOrDefault(s=>WorldSectionArchive.Inside(s,x,y))??throw new ArgumentException("A shared path reaches an unmapped world tile.");var p=s.Id==section.Id?project:sampled is {} cached&&cached.WorldX==s.WorldX&&cached.WorldY==s.WorldY?cached:sampled=lease.ReadSectionAsync(s.Id,token).GetAwaiter().GetResult();var ground=p.Terrain[(y-s.WorldY*256)*p.Width+x-s.WorldX*256];samples[(x,y)]=ground;return ground;}
  InfrastructureGeometry.Geometry? geometry=null;if(layout.LinearFeatures.Count>0)geometry=WorldInfrastructure.Build(layout,Ground);sampled=null;
  var projected=WorldAssembler.ProjectSection(layout,layout.Maps.Single(m=>m.Id==section.Id),geometry);InfrastructureGeometry.Validate(projected);
  var landings=layout.LinearFeatures.Where(f=>f.Access=="stairs").Select(f=>((int)Math.Floor(f.Points[0].X),(int)Math.Floor(f.Points[0].Y)+4*f.Level)).ToHashSet();
  if(projected.SharedInfrastructure?.Tiles.Any(t=>t.Z==0&&t.Id==InfrastructureGeometry.DeckTile&&landings.Contains((t.X+projected.WorldX*256,t.Y+projected.WorldY*256))&&projected.Terrain[t.Y*projected.Width+t.X]==Terrain.Water)==true)throw new ArgumentException("Shared stairs need a dry landing after world ground paths are applied.");
  await Task.CompletedTask;return projected;
 }
}
