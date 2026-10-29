using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;
public static class RecoveryTests
{
    public static IEnumerable<(string,Action)> Cases(){
        yield return ("large geographic recovery round trips above the legacy 48 MB limit",()=>{
            var directory=Path.Combine(Directory.GetCurrentDirectory(),"work","recovery-tests",Guid.NewGuid().ToString("N"));using var store=new RecoveryStore(directory);
            var p=MapGenerator.Generate(new(){Kind="blank",Size=256});p.Version=14;
            var tags=Enumerable.Range(0,64).ToDictionary(i=>i.ToString("D2")+new string('k',98),_=>new string('v',300));
            p.SourceFeatures=Enumerable.Range(0,2000).Select(i=>new GeographicFeature{Id="evidence-"+i,SourceId="source-"+i,Tags=tags,SourceGeometry=[[new(30,50)]],Geometry=[[new(20,20)]]}).ToList();
            const string id="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            store.SaveAsync(id,p).GetAwaiter().GetResult();
            if(new FileInfo(Path.Combine(directory,id+".pzmap.json")).Length<=48_000_000)throw new Exception("Large recovery fixture did not exceed the old limit");
            var loaded=store.LoadAsync(id).GetAwaiter().GetResult();
            if(loaded?.SourceFeatures.Count!=2000||loaded.SourceFeatures[1999].Tags.Count!=64)throw new Exception("Large recovery lost geographic evidence");
        });
        yield return ("2048 recovery persists geographic project to workspace",()=>{
            var directory=Path.Combine(Directory.GetCurrentDirectory(),"work","recovery-tests",Guid.NewGuid().ToString("N"));using var store=new RecoveryStore(directory);
            var p=MapGenerator.Generate(new(){Kind="blank",Size=2048,Name="Pilna"});p.MetresPerTile=2.29;p.Terrain[10000]=Terrain.Dirt;
            store.SaveAsync("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",p).GetAwaiter().GetResult();var loaded=store.LoadAsync("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa").GetAwaiter().GetResult();
            if(loaded?.Width!=2048||loaded.Name!="Pilna"||loaded.Terrain[10000]!=Terrain.Dirt||loaded.MetresPerTile!=2.29)throw new Exception("Recovery lost project fields");
        });
        yield return ("recovery identifiers cannot escape workspace",()=>{
            using var store=new RecoveryStore(Path.Combine(Directory.GetCurrentDirectory(),"work","recovery-tests"));try{store.LoadAsync("../../outside").GetAwaiter().GetResult();}catch(ArgumentException){return;}throw new Exception("Unsafe recovery id accepted");
        });
    }
}
