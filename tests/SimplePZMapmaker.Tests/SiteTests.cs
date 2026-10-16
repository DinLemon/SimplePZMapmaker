using SimplePZMapmaker.Core;
using System.Text.Json;
public static class SiteTests{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("site group moves and copies full prop model and reversible path footprints",()=>{
   var p=Fixture();p.Version=9;p.Decorations=[new(){Id="prop",X=45,Y=30,Asset="furniture_bedding_01_0",Parts=[new(0,0,"furniture_bedding_01_0")]}];p.StaticModels=[new(){Id=new string('a',32),ModelId="Base.Test",SourceId="game",Profile="scenery",X=45,Y=35,Footprint=[new(0,0),new(1,0)]}];
   p=LinearFeatureEditor.Rebuild(p,[new(){Id="path",Kind="sidewalk",Width=2,Points=[new(40,50),new(50,50)],SourceId="osm-way-path",SourcePath=[new(32,54),new(32.001,54)]}]);var s=p.Sites[0];s.DecorationIds=["prop"];s.StaticModelIds=[new string('a',32)];s.LinearFeatureIds=["path"];var before=JsonSerializer.Serialize(p);
   var moved=SiteEditor.Apply(new(p,"move",SiteId:s.Id,Dx:70)).Project;Check(moved.Decorations.Single().X==115&&moved.StaticModels.Single().X==115&&moved.LinearFeatures.Single().Points[0].X==110,"Group members did not move together");Check(moved.StaticModels[0].SourceId=="game"&&moved.LinearFeatures[0].SourcePath.Count==2,"Provider or original path lost");Check(moved.Terrain[50*256+44]==Terrain.Grass&&moved.Terrain[50*256+114]==Terrain.Pavement,"Path underlay did not restore and replay");
   var copy=SiteEditor.Apply(new(p,"copy",SiteId:s.Id,Dx:70)).Project;Check(copy.Decorations.Count==2&&copy.StaticModels.Count==2&&copy.LinearFeatures.Count==2&&copy.LinearFeatures[1].SourceId is null&&copy.LinearFeatures[1].SourcePath.Count==0,"Copy identity or members invalid");Check(JsonSerializer.Serialize(p)==before,"Group edit mutated source snapshot");
  });
  yield return("site move and copy are atomic and retain original evidence",()=>{
   var p=Fixture();var before=JsonSerializer.Serialize(p);var moved=SiteEditor.Apply(new(p,"move",SiteId:"s",Dx:20,Dy:15)).Project;
   Check(p.Buildings[0].X==30&&JsonSerializer.Serialize(p)==before,"Input mutated");Check(moved.Buildings[0].X==50&&moved.Buildings[0].FootprintOrigin=="manual-source-edit","Source move unmarked");Check(moved.SourceFeatures[0].Geometry[0][0].X==30,"Evidence moved");
   var copied=SiteEditor.Apply(new(p,"copy",SiteId:"s",Dx:40)).Project;Check(copied.Buildings.Count==2&&copied.Buildings[1].SourceId is null&&copied.Buildings[1].SourceOutline.Count==0,"Copy claimed real source identity");
   try{SiteEditor.Apply(new(p,"move",SiteId:"s",Dx:300));throw new Exception("Out of bounds accepted");}catch(ArgumentException){}Check(JsonSerializer.Serialize(p)==before,"Failed operation mutated input");
  });
  yield return("site protection and holes keep geometry ownership precise",()=>{var p=Fixture();p.Sites[0].Locked=true;try{SiteEditor.Apply(new(p,"move",SiteId:"s",Dx:20));throw new Exception("Locked group moved");}catch(ArgumentException){}var s=p.Sites[0];s.Boundary.Add([new(40,40),new(45,40),new(45,45),new(40,45)]);Check(!SiteModel.Contains(s,42,42)&&SiteModel.Contains(s,35,35),"Hole filled");Check(!SiteModel.LockedCells(p).Contains(42*p.Width+42),"Locked hole reserved");});
  yield return("site membership rejects partial buildings",()=>{var p=Fixture();p.Sites[0].Boundary=[[new(30,30),new(34,30),new(34,34),new(30,34)]];try{SiteEditor.Apply(new(p,"move",SiteId:"s",Dx:20));throw new Exception("Partial building moved");}catch(ArgumentException){} });
 }
 public static MapProject Fixture(){var p=MapGenerator.Generate(new(){Kind="blank"});var b=new Building{Id="b",X=30,Y=30,Width=8,Height=8,SourceId="osm-way-1"};b.Design=BuildingDesigner.Generate(b,1,1);p.Buildings=[b];p.SourceFeatures=[new(){Id="e",SourceId="osm-way-1",Kind="building",GeometryKind="polygon",BuildingId="b",Geometry=[[new(30,30),new(38,30),new(38,38),new(30,38),new(30,30)]],SourceGeometry=[[new(32,54),new(32.001,54),new(32.001,54.001),new(32,54.001),new(32,54)]]}];p.Sites=[new(){Id="s",Name="Yard",Boundary=[[new(25,25),new(55,25),new(55,55),new(25,55)]],BuildingIds=["b"]}];return p;}
 static void Check(bool b,string m){if(!b)throw new Exception(m);}
}
