using System.Globalization;
using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

public static partial class AssetRules
{
 static void CheckFuelPumps(MapProject project,GameAssetCatalog assets)
 {
  if(!assets.Ready)return;
  foreach(var pump in project.Decorations.Where(FuelStationLayout.IsNativePump)){
   var properties=assets.Get(pump.Asset).Properties;
   var expectedFacing=pump.Asset==FuelStationLayout.PumpEast?"E":"S";
   if(!properties.TryGetValue("fuelAmount",out var value)||!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var amount)||amount<=0||properties.GetValueOrDefault("Facing")!=expectedFacing)
    throw new ArgumentException("The generated fuel pump resource lost its native fuel capacity or facing: "+pump.Asset+". Review the active tile provider before export.");
  }
 }
}
