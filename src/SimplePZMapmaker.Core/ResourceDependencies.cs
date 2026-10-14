namespace SimplePZMapmaker.Core;
public static class ResourceDependencies {
 public static bool SafeModId(string? value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=120&&!value.Any(c=>char.IsControl(c)||c is ',' or ';' or '=' or '\\' or '/');
 public static void Validate(List<string> mods,Dictionary<string,string> sources){if(mods is null||mods.Count>64||mods.Any(m=>!SafeModId(m))||mods.Distinct(StringComparer.Ordinal).Count()!=mods.Count||sources is null||sources.Count>100000||sources.Any(pair=>!VisualStyles.TileId(pair.Key)||!SafeModId(pair.Value)))throw new ArgumentException("Invalid dependencies or resource sources.");}
 public static string[] ForProject(MapProject p)=>p.RequiredMods.Concat(p.Buildings.SelectMany(b=>b.RequiredMods)).Concat(p.StaticModels.Concat(p.Buildings.Where(b=>b.Design is not null).SelectMany(b=>b.Design!.Floors.SelectMany(f=>f.StaticModels))).SelectMany(m=>m.RequiredMods.Concat(m.LootRequiredMods))).Concat(p.GameZones.SelectMany(z=>z.RequiredMods)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}
