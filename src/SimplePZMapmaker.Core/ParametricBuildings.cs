using System.Security.Cryptography;
using System.Text;
namespace SimplePZMapmaker.Core;
public sealed class ParametricBuildingOptions {
 public string Name {get;set;}="Apartment compound";
 public int Entrances {get;set;}=2;
 public int Floors {get;set;}=3;
 /// <summary>Apartment units per entrance on each floor.</summary>
 public int ApartmentsPerFloor {get;set;}=2;
 public string Layout {get;set;}="slab";
 public string Facade {get;set;}="brick";
 public int Seed {get;set;}=42;
}
public sealed record GeneratedApartment(int Entrance,int Level,int Number,int X,int Y,int Width,int Height);
public sealed class ParametricBuildingResult {
 public Building Building {get;set;}=new();
 public List<Building> Modules {get;set;}=[];
 public List<GeneratedApartment> Apartments {get;set;}=[];
 public int TotalApartments=>Apartments.Count;
}
public static class ParametricBuildings {
 const string Codes="123456789abcdefghijklmnopqrstuvwxyz";
 public static void ValidateOptions(ParametricBuildingOptions o){if(o is null||string.IsNullOrWhiteSpace(o.Name)||o.Name.Length>100||o.Name.Any(char.IsControl)||o.Entrances is <1 or >4||o.Floors is <1 or >32||o.ApartmentsPerFloor is <1 or >4||o.Entrances*o.ApartmentsPerFloor>8||o.Layout is not("slab" or "staggered" or "courtyard")||o.Facade is not("brick" or "plaster" or "wood"))throw new ArgumentException("Choose 1–4 entrances, 1–32 floors and 1–4 apartments per entrance per floor. At most eight apartments per floor fit the native 35-room limit. Choose slab, staggered or courtyard and a supported facade.");}
 public static ParametricBuildingResult Create(ParametricBuildingOptions o){
  ValidateOptions(o);var result=new ParametricBuildingResult();var depth=Math.Max(8,6*((o.ApartmentsPerFloor+1)/2));
  int Offset(int e)=>o.Layout switch{"staggered"=>(e%2)*3,"courtyard"=>o.Entrances==2?e*6:e>0&&e<o.Entrances-1?6:0,_=>0};
  var b=new Building{Id=Stable($"building:{o.Seed}:{o.Entrances}:{o.Floors}:{o.ApartmentsPerFloor}:{o.Layout}:{o.Facade}"),Name=o.Name.Trim(),Width=o.Entrances*14,Height=depth+Enumerable.Range(0,o.Entrances).Max(Offset),Kind="apartments",SourceKind="parametric:apartments",Modified=true,LevelsOrigin="manual",Design=new(){Material=o.Facade,Roof="flat",Shape="custom",Variant=o.Seed}};result.Building=b;
  for(var z=0;z<o.Floors;z++){
   var f=new BuildingFloor{Rooms=new(){["0"]="hall"}};var cells=Enumerable.Repeat('.',b.Width*b.Height).ToArray();var units=Enumerable.Repeat(-2,cells.Length).ToArray();
   void Fill(int x,int y,int w,int h,char code,int unit){for(var yy=y;yy<y+h;yy++)for(var xx=x;xx<x+w;xx++){cells[yy*b.Width+xx]=code;units[yy*b.Width+xx]=unit;}}
   for(var e=0;e<o.Entrances;e++){
    var ox=e*14;var oy=Offset(e);Fill(ox+6,oy,2,depth,'0',-1);
    if(z<o.Floors-1)f.Objects.Add(new(){Kind="stairs",X=ox+6,Y=oy+1,Direction="n"});
    // Each unit has four private rooms. The seed swaps wet rooms and moves the
    // bedroom partition, while the protected circulation core stays invariant.
    for(var a=0;a<o.ApartmentsPerFloor;a++){
     var unit=e*o.ApartmentsPerFloor+a;var x=ox+(a%2==0?0:8);var y=oy+(a/2)*6;var random=new Random(unchecked(o.Seed+unit*1009+z*7919));var partition=random.Next(2,5);var swap=random.Next(2)==1;var names=swap?new[]{"kitchen","livingroom","bathroom","bedroom"}:new[]{"livingroom","kitchen","bedroom","bathroom"};
     for(var r=0;r<4;r++)f.Rooms[Codes[unit*4+r].ToString()]=names[r];
     Fill(x,y,partition,3,Codes[unit*4],unit);Fill(x+partition,y,6-partition,3,Codes[unit*4+1],unit);Fill(x,y+3,partition,3,Codes[unit*4+2],unit);Fill(x+partition,y+3,6-partition,3,Codes[unit*4+3],unit);
     result.Apartments.Add(new(e+1,z,a+1,x,y,6,6));
    }
   }
   f.Cells=new(cells);b.Design!.Floors.Add(f);f.Edges=BuildingDesigner.AutoEdges(b,f,z);
   int Unit(int x,int y)=>x<0||y<0||x>=b.Width||y>=b.Height?-2:units[y*b.Width+x];
   var entered=new HashSet<int>();
   foreach(var edge in f.Edges){var ua=Unit(edge.X,edge.Y);var ub=edge.Edge=="n"?Unit(edge.X,edge.Y-1):Unit(edge.X-1,edge.Y);if(ua==-2||ub==-2){if(edge.Kind=="door")edge.Kind="wall";}else if(ua!=ub){if(ua>=0&&ub>=0)edge.Kind="wall";else{var unit=Math.Max(ua,ub);edge.Kind=entered.Add(unit)?"door":"wall";}}}
   if(z==0)for(var e=0;e<o.Entrances;e++){var door=f.Edges.Single(v=>v.X==e*14+7&&v.Y==Offset(e)&&v.Edge=="n");door.Kind="door";}
   BuildingDesigner.Furnish(b,z,new Random(unchecked(o.Seed+z*3571)));
  }
  RoofGeometry.SelectSafeAutomaticRoof(b);BuildingDesigner.Validate(b);
  // Captures are ordinary project library modules, ready for the existing
  // Compose path, rather than opaque generator metadata.
  for(var e=0;e<o.Entrances;e++){
   var module=BuildingModules.Capture(b,e*14,Offset(e),14,depth,0,o.Floors-1,$"{o.Name[..Math.Min(o.Name.Length,80)]} · entrance {e+1}","entrance");module.Id=Stable(b.Id+":module:"+e);result.Modules.Add(module);
  }
  var sample=BuildingModules.Capture(b,0,0,6,6,0,0,"Apartment unit · four rooms","section");sample.Id=Stable(b.Id+":unit");result.Modules.Add(sample);
  return result;
 }
 internal static string Stable(string value)=>"gen-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();
}
