using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
public static class ArchitectureTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("regional architecture catalog contains 24 distinct authored plans and functional varied layouts",()=>{
   if(HousingTemplates.Catalog.Count<24)throw new Exception("Regional catalog needs at least24 original plans");
   foreach(var id in new[]{"settlement-row-four","urban-townhouse-row","urban-perimeter-four","urban-shop-house","village-farmstead"}){var b=HousingTemplates.Create(id);BuildingDesigner.Validate(b);if(b.Design is null||b.Design.Floors.Count==0)throw new Exception("Missing authored layout");}
   var ring=HousingTemplates.Create("urban-perimeter-four");if(ring.Design!.Floors.Any(f=>BuildingDesigner.Cell(ring,f,ring.Width/2,ring.Height/2)!='.'))throw new Exception("Courtyard filled in");
   var podium=HousingTemplates.Create("urban-shop-house");if(podium.Design!.Floors[0].Cells.Count(c=>c!='.')<=podium.Design.Floors[1].Cells.Count(c=>c!='.'))throw new Exception("Commercial podium has no setback");
  });
  yield return("pitched roofs follow occupied geometry and select narrow span across long buildings",()=>{
   var b=new Building{Width=20,Height=10,Kind="house"};BuildingDesigner.Generate(b,1,17);b.Design!.Roof="gabled";var f=b.Design.Floors[0];var edge=RoofGeometry.Piece(b,f,10,0);var ridge=RoofGeometry.Piece(b,f,10,4);if(edge.TileIndex!=29||ridge.Depth!=4)throw new Exception("Gable axis failed for long frontage");
   var chars=f.Cells.ToCharArray();for(var y=0;y<10;y++)for(var x=0;x<4;x++)chars[y*20+x]='.';f.Cells=new(chars);b.Design.Roof="shed";var start=RoofGeometry.Piece(b,f,4,0);if(start.Depth!=0)throw new Exception("Empty footprint margin counted as pitched roof span");
   b.Design.Roof="hipped";var hip=RoofGeometry.Piece(b,f,4,4);if(hip.Depth!=0)throw new Exception("Hip roof ignored contour boundary");
  });
  yield return("blocking model profiles reject furniture entrances and staircase landings",()=>{
   foreach(var profile in new[]{"obstacle","destructible","container"}){
    var b=new Building{Width=10,Height=10,Kind="house",Name="Model access fixture",Design=new(){Floors=[new(){Cells=new string('1',100),Rooms=new(){["1"]="livingroom"},Edges=[new(){X=5,Y=0,Edge="n",Kind="door"}]}]}};
    var f=b.Design.Floors[0];var m=new StaticModelPlacement{ModelId="Base.Test",X=5,Y=0,Profile=profile};f.StaticModels.Add(m);
    void Rejected(){try{BuildingDesigner.Validate(b,false);}catch(ArgumentException e) when(e.Message.Contains("model")){return;}throw new Exception("Blocking model accepted at forbidden location");}
    Rejected();m.X=2;m.Y=2;f.Objects.Add(new(){Kind="storage",X=2,Y=2});Rejected();f.Objects.Clear();f.Objects.Add(new(){Kind="stairs",X=2,Y=2});b.Design.Floors.Add(new(){Cells=f.Cells,Rooms=new(f.Rooms)});Rejected();
   }
  });
 }
}
