using System.Runtime.CompilerServices;
namespace SimplePZMapmaker.Core;

/// <summary>Deterministic row-run decomposition of L/T/courtyard roofs into rectangular wings.
/// It never fills a missing room tile. Cached by the floor's immutable cell string and balcony codes.</summary>
public static class RoofPartitions
{
 public sealed record Rectangle(int X,int Y,int Width,int Height);
 sealed class Entry { public string Cells="",Balconies="";public int Width,Height;public int[] Owners=[];public IReadOnlyList<Rectangle> Parts=[]; }
 static readonly ConditionalWeakTable<BuildingFloor,Entry> Cache=new();
 static Entry Get(Building b,BuildingFloor f){
  var balconies=string.Join("",f.Rooms.Where(p=>p.Value=="balcony").Select(p=>p.Key).Order(StringComparer.Ordinal));var entry=Cache.GetOrCreateValue(f);
  lock(entry){if(entry.Cells==f.Cells&&entry.Width==b.Width&&entry.Height==b.Height&&entry.Balconies==balconies&&entry.Owners.Length==b.Width*b.Height)return entry;
   var parts=new List<Rectangle>();var owners=Enumerable.Repeat(-1,b.Width*b.Height).ToArray();var active=new Dictionary<(int X,int Width),int>();
   bool Inside(int x,int y){var code=BuildingDesigner.Cell(b,f,x,y);return code!='.'&&!balconies.Contains(code);}
   for(var y=0;y<b.Height;y++){var next=new Dictionary<(int X,int Width),int>();for(var x=0;x<b.Width;x++){
    if(!Inside(x,y))continue;var first=x;while(x+1<b.Width&&Inside(x+1,y))x++;var width=x-first+1;var key=(first,width);
    if(active.TryGetValue(key,out var index))parts[index]=parts[index] with{Height=parts[index].Height+1};else{index=parts.Count;parts.Add(new(first,y,width,1));}
    next[key]=index;for(var xx=first;xx<=x;xx++)owners[y*b.Width+xx]=index;
   }active=next;}
   entry.Cells=f.Cells;entry.Width=b.Width;entry.Height=b.Height;entry.Balconies=balconies;entry.Owners=owners;entry.Parts=parts.AsReadOnly();return entry;
  }
 }
 public static IReadOnlyList<Rectangle> Parts(Building b,BuildingFloor floor)=>Get(b,floor).Parts;
 public static Rectangle At(Building b,BuildingFloor floor,int x,int y){var entry=Get(b,floor);if(x<0||y<0||x>=b.Width||y>=b.Height||entry.Owners[y*b.Width+x]<0)throw new ArgumentException("Roof tile is outside its occupied wing.");return entry.Parts[entry.Owners[y*b.Width+x]];}
}
