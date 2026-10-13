using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;
public static class RecoveryTests
{
    public static IEnumerable<(string,Action)> Cases(){
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
