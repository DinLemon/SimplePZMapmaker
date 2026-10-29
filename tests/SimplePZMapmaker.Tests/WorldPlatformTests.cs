using SimplePZMapmaker.Core;
public static class WorldPlatformTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("shared pedestrian platform uses parcel occupancy and preserves fence ground",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});p.WorldX=0;p.WorldY=0;p.FenceStyles=[new(){Id="sheet",W="fencing_01_40",N="fencing_01_41"}];p.Fences=[new(){Style="sheet",Points=[new(45,30),new(45,50)]}];p.Generation=new(){ProtectedAreas=[new(){X=65,Y=35,Width=4,Height=10}]};var w=new WorldProject{Maps=[new(){Project=p}]};var before=p.Terrain.ToArray();var next=WorldInfrastructure.Put(w,new(){Id="platform",Kind="sidewalk",Width=2,Points=[new(20,40),new(80,40)]});var projected=WorldAssembler.ProjectSection(next,next.Maps[0]);if(projected.Terrain[40*256+45]!=before[40*256+45])throw new Exception("Shared platform repainted ground occupied by a fence");if(projected.Terrain[40*256+66]!=before[40*256+66])throw new Exception("Shared platform repainted a protected parcel");if(projected.Terrain[40*256+30]!=Terrain.Pavement)throw new Exception("Unoccupied platform did not render");if(!p.Terrain.SequenceEqual(before)||!WorldInfrastructure.Remove(next,"platform").Maps[0].Project.Terrain.SequenceEqual(before))throw new Exception("Platform changed authored ground or failed removal");
  });
 }
}
