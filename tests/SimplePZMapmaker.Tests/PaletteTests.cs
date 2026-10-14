using System.Text.Json;
using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Tests;
public static class PaletteTests
{
 public static IEnumerable<(string Name,Action Run)> Cases(){
  yield return ("palette save promotes format and preserves inactive provider metadata",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank",Size=256});p.ResourcePalettes=[Palette()];ProjectValidator.Validate(p);
   var q=JsonSerializer.Deserialize<MapProject>(JsonSerializer.Serialize(p))!;
   if(q.Version!=8||q.ResourcePalettes.Single().Resources.Single().SourceId!="sample-mod"||q.RequiredMods.Count!=0)throw new Exception("Palette save lost format or inactive provider context.");
  });
  yield return ("palette malformed references and bounds rejected",()=>{
   foreach(var mutate in new Action<ResourcePalette>[] {p=>p.Resources[0].SourceId="../mod",p=>p.Resources[0].RequiredMods=["mod","mod"],p=>p.Name="bad\nname",p=>p.Resources.Add(p.Resources[0]),p=>p.Resources[0].Role="script"}){var p=Palette();mutate(p);Reject(()=>ResourcePalette.Validate([p]));}
   Reject(()=>ResourcePalette.Validate([Palette(),Palette()]));Reject(()=>ResourcePalette.Validate(Enumerable.Range(0,17).Select(_=>Palette()).ToList()));
  });
 }
 static ResourcePalette Palette()=>new(){Id=new('a',32),Name="Workshop references",Resources=[new(){Role="floor",Id="floors_interior_tilesandwood_01_0",SourceId="sample-mod",RequiredMods=["sample-mod"]}]};
 static void Reject(Action run){try{run();}catch(ArgumentException){return;}throw new Exception("Invalid palette accepted.");}
}
