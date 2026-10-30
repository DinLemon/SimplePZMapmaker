namespace SimplePZMapmaker.Core;

/// <summary>Purpose-specific furnishing using existing vanilla object groups; circulation has priority over density.</summary>
internal static class RoomFurnishing
{
 internal static void Furnish(Building b,BuildingFloor f,int z,char code,string room,HashSet<int> passages){
  var cells=Enumerable.Range(0,f.Cells.Length).Where(i=>f.Cells[i]==code).ToArray();if(cells.Length==0)return;
  var seats=Math.Clamp(cells.Length/8,2,12);var racks=Math.Clamp(cells.Length/48,1,6);
  string[] roles=room switch{
   "hall" or "balcony" or "stairwell" or "empty" or "greenhouse"=>[],
   "classroom" or "daycare"=>["counter",..Enumerable.Repeat("seat",seats),"storage"],
   "church"=>[..Enumerable.Repeat("seat",seats)],
   "office" or "policeoffice" or "medicaloffice" or "bank"=>["counter","seat","storage"],
   "medical" or "dentist"=>["sink","counter","storage"],
   "diningroom" or "cafe" or "restaurantdining" or "burgerdining" or "bar"=>["counter",..Enumerable.Repeat("seat",seats)],
   "warehouse" or "factory" or "garage" or "garagestorage" or "shed" or "barn" or "farmstorage"=>[..Enumerable.Repeat("storage",racks),"counter"],
   var name when name.EndsWith("storage",StringComparison.Ordinal)=>[..Enumerable.Repeat("storage",racks)],
   _=>["storage","counter"]
  };
  var placement=new FurniturePlacement(b,f,z,code,passages);
  foreach(var kind in roles){
   // Repeated seats and shelving form rows with a two-tile aisle, rather than random scatter.
   var row=kind is "seat" or "storage"&&cells.Length>=36;
   var candidates=row?cells.Where(i=>i%b.Width%3==1&&i/b.Width%3==1).Concat(cells).Distinct():cells;
   var placed=false;foreach(var i in candidates){foreach(var direction in new[]{"n","w"})if(placement.Place(new(){X=i%b.Width,Y=i/b.Width,Kind=kind,Direction=direction,Style=b.Design!.Style},!row)){placed=true;break;}if(placed)break;}
  }
 }
}
