using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;

public static class FuelResourceTests
{
 public static IEnumerable<(string,Action)> Cases()
 {
  yield return ("fuel resource validation rejects a replacement without native capacity or facing",()=>{
   var root=Path.Combine(Path.GetTempPath(),"spzm-fuel-resource-"+Guid.NewGuid().ToString("N"));var game=Path.Combine(root,"game");Directory.CreateDirectory(Path.Combine(game,"media","texturepacks"));
   try{
    foreach(var (capacity,facing,valid) in new[]{("20000","E",true),("","E",false),("0","E",false),("NaN","E",false),("20000","W",false)}){
     File.WriteAllText(Path.Combine(game,"media","newtiledefinitions.tiles.txt"),"// location_shop_fossoil_01_12\ntile\n{\nsolid =\nFacing = "+facing+"\n"+(capacity.Length>0?"fuelAmount = "+capacity+"\n":"")+"}\n");
     var assets=new GameAssetCatalog(Path.Combine(root,"workspace"),game);var p=Project();bool accepted;
     try{AssetRules.Project(p,assets);accepted=true;}catch(ArgumentException){accepted=false;}
     if(accepted!=valid)throw new Exception($"Fuel resource acceptance differs for capacity='{capacity}', facing='{facing}'");
    }
   }finally{Directory.Delete(root,true);}
  });
  yield return ("fuel resource offline contract allows only the strict known native pump shape",()=>{
   var root=Path.Combine(Path.GetTempPath(),"spzm-fuel-offline-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   try{
    var assets=new GameAssetCatalog(root,"");if(assets.Ready)throw new Exception("Expected isolated empty catalog");var p=Project();AssetRules.Project(p,assets);
    p.Decorations[0].Parts=[new(0,0,FuelStationLayout.PumpEast,0)];
    try{AssetRules.Project(p,assets);}catch(ArgumentException){return;}throw new Exception("Missing solid pump collision accepted offline");
   }finally{Directory.Delete(root,true);}
  });
 }
 static MapProject Project(){var p=MapGenerator.Generate(new(){Kind="blank"});p.Decorations.Add(new(){Id=FuelStationLayout.SourcePrefix("fixture")+"0",X=30,Y=30,Asset=FuelStationLayout.PumpEast,Parts=[new(0,0,FuelStationLayout.PumpEast,1)]});return p;}
}
