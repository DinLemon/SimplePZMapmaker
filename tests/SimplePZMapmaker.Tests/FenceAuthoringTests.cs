using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;
using System.Text.Json;
public static class FenceAuthoringTests
{
 public static IEnumerable<(string,Action)> Cases()
 {
  yield return("fence authoring clones metadata retains decorations and rejects style overwrite",()=>{var p=MapGenerator.Generate(new(){Kind="blank"});p.Decorations.Add(new(){X=20,Y=20,Asset="vegetation_ornamental_01_0",Parts=[new(0,0,"vegetation_ornamental_01_0")]});p.ZombieDensityState=new(){Baseline=p.Zombies.ToArray()};var before=JsonSerializer.Serialize(p);var line=Line();var next=FenceAuthoringEndpoints.Prepare(new(p,line,Style()));Check(next.Fences.Count==1&&next.Decorations.Count==1&&next.ZombieDensityState?.Baseline.Length==p.Zombies.Length,"Fence authoring discarded independent authoring layers");Check(JsonSerializer.Serialize(p)==before,"Fence authoring mutated the input");var conflict=Style();conflict.W="fencing_01_2";Reject(()=>FenceAuthoringEndpoints.Prepare(new(next,line,conflict)));Check(JsonSerializer.Serialize(p)==before,"Rejected style mutated input");});
  yield return("fence authoring preserves exact cardinal edges and refuses unowned boundaries",()=>{var p=MapGenerator.Generate(new(){Kind="blank"});foreach(var points in new List<FencePoint>[] { [new(1,1),new(3,3)], [new(-1,1),new(3,1)], [new(256,1),new(256,5)], [new(1,256),new(5,256)] }){var line=Line();line.Points=points;Reject(()=>FenceAuthoringEndpoints.Prepare(new(p,line,Style())));}var gate=GeographicImporter.DefaultWicket;var f=Line();f.Gates=[new(){At=2,Style=gate.Id,OpensTo="left"}];var result=FenceAuthoringEndpoints.Prepare(new(p,f,Style()));Check(result.Fences[0].Gates[0].OpensTo=="left"&&result.GateStyles.Single().Id==gate.Id,"Wicket placement was not retained");});
  yield return("fence authoring stamps provider dependencies and rejects switched or missing tiles atomically",()=>{var p=FenceAuthoringEndpoints.Prepare(new(MapGenerator.Generate(new(){Kind="blank"}),Line(),Style()));GameAssetCatalog.Asset Asset(string id)=>new(id,"fences",id,"","",null,true,false,[],null,null,"mod","FenceMod",["BaseMod","FenceMod"]);FenceAuthoringEndpoints.StampFenceResources(p,Asset);Check(p.RequiredMods.SequenceEqual(new[]{"BaseMod","FenceMod"})&&p.AssetSources["fencing_01_0"]=="FenceMod","Fence provider or transitive requirement omitted");var before=JsonSerializer.Serialize(p);Reject(()=>FenceAuthoringEndpoints.StampFenceResources(p,id=>Asset(id) with{ModId="Replacement"}));Check(JsonSerializer.Serialize(p)==before,"Rejected provider changed dependencies");Reject(()=>FenceAuthoringEndpoints.StampFenceResources(p,id=>throw new ArgumentException("Tile missing")));Check(JsonSerializer.Serialize(p)==before,"Missing fence tile changed authoring data");});
 }
 static FenceLine Line()=>new(){Id="authored-fence",Style="fixture",Points=[new(20,20),new(28,20),new(28,28)]};
 static FenceStyle Style()=>new(){Id="fixture",W="fencing_01_0",N="fencing_01_1"};
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Invalid fence authoring accepted");}
}
