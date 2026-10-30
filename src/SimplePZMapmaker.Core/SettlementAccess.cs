namespace SimplePZMapmaker.Core;

/// <summary>Gives fallback designs a shared stair hall with a clear bypass beside the three physical step tiles.</summary>
internal static class SettlementAccess
{
 internal static void ConnectLandings(Building building,int seed,bool furnish=true,bool validate=true)
 {
  var design=building.Design!;if(design.Floors.Count<2)return;
  var runs=design.Floors.SelectMany(f=>f.Objects).Where(o=>o.Kind=="stairs").DistinctBy(o=>(o.X,o.Y,o.Direction)).ToArray();
  for(var index=0;index<design.Floors.Count;index++)
  {
   var floor=design.Floors[index];var cells=floor.Cells.ToCharArray();
   foreach(var run in runs)
   {
    // A one-tile stair column is severed by both lower steps and the upper opening.
    // Two side aisles keep landings, room doors, and repeated floors connected without a doorway onto a step.
    if(run.Direction=="n")for(var y=0;y<building.Height;y++)for(var x=Math.Max(0,run.X-1);x<=Math.Min(building.Width-1,run.X+1);x++){if(cells[y*building.Width+x]!='.')cells[y*building.Width+x]='0';}
    else for(var y=Math.Max(0,run.Y-1);y<=Math.Min(building.Height-1,run.Y+1);y++)for(var x=0;x<building.Width;x++){if(cells[y*building.Width+x]!='.')cells[y*building.Width+x]='0';}
   }
   floor.Rooms["0"]="hall";floor.Cells=new(cells);floor.Edges=BuildingDesigner.AutoEdges(building,floor,index+design.MinLevel);
   if(furnish)BuildingDesigner.Furnish(building,index,new Random(unchecked(seed+index*1009)));
  }
  if(validate)BuildingDesigner.Validate(building);
 }
}
