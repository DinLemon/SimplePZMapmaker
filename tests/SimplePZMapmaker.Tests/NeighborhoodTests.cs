using SimplePZMapmaker.Core;
static class NeighborhoodTests {
 public static IEnumerable<(string,Action)> Cases(){
  yield return("neighborhood proposal leaves source untouched and respects bounded water and occupied land",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var before=NeighborhoodPlanner.Copy(p);p.Buildings.Add(HousingTemplates.Create("village-izba"));p.Buildings[0].X=32;p.Buildings[0].Y=32;
   p.Spawns[0].X=20;p.Spawns[0].Y=20;for(var y=100;y<180;y++)for(var x=100;x<180;x++)p.Terrain[y*p.Width+x]=Terrain.Water;
   var constraints=new NeighborhoodConstraints{MaximumBuildings=12,TemplateIds=["village-izba"],Roads=false};var plan=NeighborhoodPlanner.Create(p,constraints);Check(p.Buildings.Count==1,"Proposal modified original");Check(plan.Buildings.Count>0&&plan.Buildings.Count<=12,"Building budget lost");
   var copy=NeighborhoodPlanner.Copy(p);NeighborhoodPlanner.Apply(copy,plan);ProjectValidator.Validate(copy);Check(copy.Buildings.Count==plan.Buildings.Count+1&&p.Buildings.Count==1,"Commit lost existing building");Check(copy.Terrain[120*p.Width+120]==Terrain.Water,"Water was replaced");
  });
  yield return("neighborhood rejects impossible constraints and preserves manual terrain on roads",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});var mask=TerrainEditMask.Decode(p);mask[45/8]|=(byte)(1<<(45%8));p.Version=6;p.TerrainEdits=TerrainEditMask.Encode(mask);p.Terrain[45]=Terrain.Sand;
   var plan=NeighborhoodPlanner.Create(p,new(){X=0,Y=0,Width=240,Height=240,MaximumBuildings=8,TemplateIds=["village-izba"]});var copy=NeighborhoodPlanner.Copy(p);NeighborhoodPlanner.Apply(copy,plan);Check(copy.Terrain[45]==Terrain.Sand,"Manual ground lost");
   try{NeighborhoodPlanner.Create(p,new(){MaximumBuildings=129});throw new Exception("Budget accepted");}catch(ArgumentException){}
   Array.Fill(p.Terrain,Terrain.Water);p.Terrain[p.Spawns[0].Y*p.Width+p.Spawns[0].X]=Terrain.Grass;
   try{NeighborhoodPlanner.Create(p,new());throw new Exception("Impossible plan accepted");}catch(ArgumentException){}
  });
 }
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
