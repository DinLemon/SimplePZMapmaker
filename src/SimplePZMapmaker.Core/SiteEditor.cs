namespace SimplePZMapmaker.Core;
public sealed record SiteEditRequest(MapProject Project,string Action,SiteParcel? Site=null,string? SiteId=null,int Dx=0,int Dy=0,string? Name=null,bool? Locked=null);
public sealed record SiteEditResult(MapProject Project,string? SiteId,IReadOnlyList<string> Warnings);

/// <summary>Changes isolated map snapshots. Original source records never follow authored copies or translations.</summary>
public static class SiteEditor
{
 public static SiteEditResult Apply(SiteEditRequest request){
  if(request?.Project is null)throw new ArgumentException("Choose a map before editing sites.");
  var p=NeighborhoodPlanner.Copy(request.Project);ProjectValidator.Validate(p);
  if(request.Action is not("create" or "update" or "move" or "copy" or "remove" or "lock")||Math.Abs((long)request.Dx)>2048||Math.Abs((long)request.Dy)>2048)throw new ArgumentException("Invalid site operation or translation.");
  var warnings=new List<string>();
  SiteParcel site;
  if(request.Action=="create"){
   site=NeighborhoodPlanner.Copy(request.Site??throw new ArgumentException("Draw a site boundary first."));site.Id=Guid.NewGuid().ToString("N");site.Origin="authored";site.SourceId=null;site.SourceFeatureId=null;p.Sites.Add(site);
  }else site=p.Sites.FirstOrDefault(s=>s.Id==request.SiteId)??throw new ArgumentException("Site no longer exists.");
  if(site.Locked&&request.Action!="lock")throw new ArgumentException("Unlock this site before changing its members or geometry.");
  if(request.Action=="lock")site.Locked=request.Locked??!site.Locked;
  if(request.Action=="remove"){p.Sites.Remove(site);return new(p,null,["Site grouping removed; its objects remain."]);}
  if(request.Action=="update"){
   var replacement=NeighborhoodPlanner.Copy(request.Site??site);replacement.Id=site.Id;replacement.SourceId=site.SourceId;replacement.SourceFeatureId=site.SourceFeatureId;replacement.Origin=site.Origin=="source"?"source-edited":site.Origin;replacement.Locked=site.Locked;p.Sites[p.Sites.IndexOf(site)]=replacement;site=replacement;
  }
  if(request.Name is not null)site.Name=request.Name;
  SiteModel.Validate(p);CheckMembers(p,site);
  if(request.Action is "move" or "copy"){
   var duplicate=request.Action=="copy";
   if(p.Sites.Any(s=>s.Id!=site.Id&&s.Locked&&(s.BuildingIds.Intersect(site.BuildingIds).Any()||s.DecorationIds.Intersect(site.DecorationIds).Any()||s.StaticModelIds.Intersect(site.StaticModelIds).Any()||s.LinearFeatureIds.Intersect(site.LinearFeatureIds).Any())))throw new ArgumentException("A member belongs to another locked site. Unlock that site first.");
   if(p.Sites.Any(s=>s.Id!=site.Id&&(s.BuildingIds.Intersect(site.BuildingIds).Any()||s.DecorationIds.Intersect(site.DecorationIds).Any()||s.StaticModelIds.Intersect(site.StaticModelIds).Any()||s.LinearFeatureIds.Intersect(site.LinearFeatureIds).Any())))throw new ArgumentException("Shared membership cannot be translated as one site. Remove duplicate group membership first.");
   if(duplicate){var original=site;site=NeighborhoodPlanner.Copy(site);site.Id=Guid.NewGuid().ToString("N");site.Name=(site.Name+" copy")[..Math.Min(100,site.Name.Length+5)];site.SourceId=null;site.SourceFeatureId=null;site.Origin="authored";p.Sites.Add(site);site.BuildingIds=[];site.DecorationIds=[];site.StaticModelIds=[];site.LinearFeatureIds=[];
    foreach(var id in original.BuildingIds){var b=NeighborhoodPlanner.Copy(p.Buildings.Single(b=>b.Id==id));b.Id=Guid.NewGuid().ToString("N");b.SourceId=null;b.SourceKind=null;b.SourceLevels=null;b.SourceRoof=null;b.SourceBasements=null;b.SourceOutline=[];b.SourceOutlineSimplified=false;b.FootprintOrigin="authored";b.LevelsOrigin="manual";p.Buildings.Add(b);site.BuildingIds.Add(b.Id);}
    foreach(var id in original.DecorationIds){var d=NeighborhoodPlanner.Copy(p.Decorations.Single(d=>d.Id==id));d.Id=Guid.NewGuid().ToString("N");p.Decorations.Add(d);site.DecorationIds.Add(d.Id);}
    foreach(var id in original.StaticModelIds){var m=NeighborhoodPlanner.Copy(p.StaticModels.Single(m=>m.Id==id));m.Id=Guid.NewGuid().ToString("N");p.StaticModels.Add(m);site.StaticModelIds.Add(m.Id);}
    foreach(var id in original.LinearFeatureIds){var l=NeighborhoodPlanner.Copy(p.LinearFeatures.Single(l=>l.Id==id));l.Id=Guid.NewGuid().ToString("N");l.SourceId=null;l.SourcePath=[];l.SourceTags=[];l.Underlay=[];l.GeneratedDecorations=[];l.DecorationIds=[];p.LinearFeatures.Add(l);site.LinearFeatureIds.Add(l.Id);}
   }
   var ids=site.BuildingIds.ToHashSet();foreach(var b in p.Buildings.Where(b=>ids.Contains(b.Id))){b.X+=request.Dx;b.Y+=request.Dy;b.Modified=true;if(b.SourceId is not null||b.SourceOutline.Count>0)b.FootprintOrigin="manual-source-edit";}
   foreach(var d in p.Decorations.Where(d=>site.DecorationIds.Contains(d.Id))){d.X+=request.Dx;d.Y+=request.Dy;}
   foreach(var m in p.StaticModels.Where(m=>site.StaticModelIds.Contains(m.Id))){m.X+=request.Dx;m.Y+=request.Dy;}
   foreach(var e in p.SourceFeatures.Where(e=>e.BuildingId is not null&&ids.Contains(e.BuildingId))){e.Modified=true;e.Origin="source-edited";}
   // Rebuild uses the original layer's underlay to restore strokes before replaying changed paths.
   var lines=NeighborhoodPlanner.Copy(p.LinearFeatures);foreach(var l in lines.Where(l=>site.LinearFeatureIds.Contains(l.Id))){l.Points=l.Points.Select(v=>new LinearPoint(v.X+request.Dx,v.Y+request.Dy)).ToList();l.Modified=true;}
   if(site.LinearFeatureIds.Count>0)p=LinearFeatureEditor.Rebuild(p,lines);
   site=p.Sites.Single(s=>s.Id==site.Id);site.Boundary=site.Boundary.Select(r=>r.Select(v=>new SitePoint(v.X+request.Dx,v.Y+request.Dy)).ToList()).ToList();if(site.Origin=="source")site.Origin="source-edited";
   if(site.Boundary.SelectMany(r=>r).Any(v=>v.X<0||v.Y<0||v.X>=p.Width||v.Y>=p.Height))throw new ArgumentException("The complete site must stay within this section. Cross-section source boundaries are read-only; edit individual local groups.");
   CheckMembers(p,site);warnings.Add("Source coordinates remain unchanged. Translation affects grouped objects and boundary, not painted terrain, service evidence or game zones.");
  }
  ProjectValidator.Validate(p);return new(p,site.Id,warnings);
 }
 static void CheckMembers(MapProject p,SiteParcel s){
  if(p.Buildings.Where(b=>s.BuildingIds.Contains(b.Id)).Any(b=>!SiteModel.FullyContainsBuilding(s,b))||p.Decorations.Where(b=>s.DecorationIds.Contains(b.Id)).Any(d=>d.Parts.Any(c=>!SiteModel.Contains(s,d.X+c.Dx+.5,d.Y+c.Dy+.5)))||p.StaticModels.Where(b=>s.StaticModelIds.Contains(b.Id)).Any(d=>StaticModelPlacement.FootprintCells(d).Any(c=>!SiteModel.Contains(s,d.X+c.Dx+.5,d.Y+c.Dy+.5)))||p.LinearFeatures.Where(b=>s.LinearFeatureIds.Contains(b.Id)).Any(l=>!SiteModel.FullyContainsLinear(s,l,p.Width,p.Height)))throw new ArgumentException("Site members must fit completely inside the boundary, including holes and path widths. Crossing objects remain separate.");
  if(p.LinearFeatures.Any(l=>l.DecorationIds.Intersect(s.DecorationIds).Any()))throw new ArgumentException("Railway artwork belongs to its path. Group the path instead of its generated sprites.");
 }
}
