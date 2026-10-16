namespace SimplePZMapmaker.Core;

/// <summary>Portable settlement controls. The legacy preset retains the original generator.</summary>
public sealed class SettlementRules
{
 public string Preset {get;set;}="legacy";
 public string Era {get;set;}="mixed";
 public string LandUse {get;set;}="mixed";
 public int Setback {get;set;}=3;
 public int MinimumFloors {get;set;}=1;
 public int MaximumFloors {get;set;}=5;
 public bool Courtyards {get;set;}=true;
 public int Density {get;set;}=55;
 public static void Validate(SettlementRules? rules)
 {
  if(rules is null||rules.Preset is not("legacy" or "contextual")||rules.Era is not("prewar" or "soviet" or "modern" or "mixed")||rules.LandUse is not("residential" or "mixed" or "industrial" or "rural")||rules.Setback is <1 or >16||rules.MinimumFloors is <1 or >ZLevels.AboveGroundCount||rules.MaximumFloors<rules.MinimumFloors||rules.MaximumFloors>ZLevels.AboveGroundCount||rules.Density is <0 or >100)
   throw new ArgumentException("Check the settlement preset, era, land use, setback (1–16), density (0–100), and floor interval (1–32).");
 }
}

public sealed class SettlementPopulationResult
{
 public int AttemptedSites {get;set;}
 public int PlacedBuildings {get;set;}
 public List<string> Warnings {get;set;}=[];
}
