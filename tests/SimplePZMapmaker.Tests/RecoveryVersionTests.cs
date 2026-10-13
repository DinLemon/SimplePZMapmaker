using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;
static class RecoveryVersionTests {
 public static IEnumerable<(string,Action)> Cases(){
  yield return("streamed recovery retains three revisions and falls back from corrupt current save",()=>{
   var root=Path.Combine(Environment.GetEnvironmentVariable("PZMAP_WORKSPACE")!,"work","tests","recovery-versions-"+Guid.NewGuid().ToString("N"));var id=Guid.NewGuid().ToString("N");
   try{using var store=new RecoveryStore(root);var p=MapGenerator.Generate(new(){Kind="blank"});for(var i=1;i<=5;i++){p.Name="Revision "+i;store.SaveAsync(id,p).GetAwaiter().GetResult();}
    Check(store.Versions(id).Length==4,"Previous revisions not bounded");Check(store.LoadAsync(id,version:3).GetAwaiter().GetResult()!.Name=="Revision 2","Previous revision lost");
    File.WriteAllText(Path.Combine(root,id+".pzmap.json"),"{ broken");Check(store.LoadAsync(id).GetAwaiter().GetResult()!.Name=="Revision 4","Valid backup not recovered");
    var file=Path.Combine(root,"bounded.json");RecoveryArchive.Write(file,new string('x',100),256).GetAwaiter().GetResult();var before=File.ReadAllBytes(file);
    try{RecoveryArchive.Write(file,new string('x',1000),256).GetAwaiter().GetResult();throw new Exception("Budget bypassed");}catch(ArgumentException){}
    Check(before.SequenceEqual(File.ReadAllBytes(file))&&!File.Exists(file+".tmp"),"Oversized write damaged previous save");
   }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
  });
 }
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
}
