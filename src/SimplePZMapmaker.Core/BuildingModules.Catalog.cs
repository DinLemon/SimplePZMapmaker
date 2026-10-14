namespace SimplePZMapmaker.Core;
public static class BuildingModuleCatalog
{
 public sealed record Entry(string Id,string Name,string Role,string Description,int Width,int Height,int Floors);
 public static readonly Entry[] Entries=[
  new("entrance-lobby","Entrance and reception","entrance","Vestibule, reception office and bathroom with a common entrance hall.",8,8,1),
  new("residential-section","Residential section","section","Living room, kitchen and bedroom off an internal hall.",12,10,1),
  new("clinic-wing","Clinic wing","section","Two treatment rooms, office and shared corridor.",12,10,1),
  new("garage-annex","Garage and store annex","annex","Garage work area and partitioned storage, ready to attach to a house.",10,8,1),
  new("service-annex","Service annex","annex","Kitchen, storage and washroom around a central service hall.",10,8,1),
  new("stair-core","Two-storey staircase core","stairs","Protected five-tile stair run and connected landings on two floors.",6,8,2),
  new("stair-tower","Four-storey staircase tower","stairs","Stacked circulation core for basements and upper-floor sections.",6,8,4)
 ];
 public static Building Create(string id){
  var entry=Entries.SingleOrDefault(e=>e.Id==id)??throw new ArgumentException("Unknown building module.");var b=new Building{Id="builtin-module-"+id,Name=entry.Name,Width=entry.Width,Height=entry.Height,Kind=id=="clinic-wing"?"clinic":id=="garage-annex"?"garage":"house",SourceKind=BuildingModules.Prefix+entry.Role,LevelsOrigin="manual",Design=new(){Shape="custom",Material=id.Contains("annex",StringComparison.Ordinal)?"brick":"plaster",Roof="flat"}};
  for(var z=0;z<entry.Floors;z++){
   var f=new BuildingFloor{Rooms=new(){["0"]="hall"}};var cells=Enumerable.Repeat('0',b.Width*b.Height).ToArray();
   if(entry.Role!="stairs"){
    var names=id switch{"entrance-lobby"=>new[]{"office","bathroom","livingroom"},"clinic-wing"=>new[]{"medical","medical","office"},"garage-annex"=>new[]{"garage","storage","storage"},"service-annex"=>new[]{"kitchen","storage","bathroom"},_=>new[]{"livingroom","kitchen","bedroom"}};
    for(var y=2;y<b.Height-1;y++)for(var x=1;x<b.Width-1;x++){if(x==b.Width/2||y==b.Height/2)continue;var section=x<b.Width/2?0:y<b.Height/2?1:2;var code=(char)('1'+section);cells[y*b.Width+x]=code;f.Rooms[code.ToString()]=names[section];}
   }
   f.Cells=new(cells);if(z<entry.Floors-1)f.Objects.Add(new(){Kind="stairs",X=2,Y=1});b.Design.Floors.Add(f);f.Edges=BuildingDesigner.AutoEdges(b,f,z);
  }
  if(entry.Role!="stairs")for(var z=0;z<entry.Floors;z++)BuildingDesigner.Furnish(b,z,new Random(id.Length*31+z));BuildingDesigner.Validate(b);return b;
 }
}
