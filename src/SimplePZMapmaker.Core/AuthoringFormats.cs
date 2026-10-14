namespace SimplePZMapmaker.Core;
public static class AuthoringFormats
{
 // Earlier editor formats supported occupied levels -16..15 and had no explicit open roof option.
 public static bool BuildingHasExtendedLevels(Building? b)=>b?.Design is {} d&&(d.MinLevel< -16||(long)d.MinLevel+(d.Floors?.Count??0)-1>15||d.Roof=="none");
 public static bool MapHasExtendedLevels(MapProject p)=>(p.Buildings??[]).Concat(p.BuildingLibrary??[]).Any(BuildingHasExtendedLevels);
 public static bool WorldHasExtendedLevels(WorldProject w)=>w.Maps?.Any(m=>m?.Project?.Version>=10)==true||w.Buildings?.Any(BuildingHasExtendedLevels)==true;
 public static bool BuildingHasNewAuthoring(Building? b)=>b is not null&&(b.SourceOutline?.Count>0||b.FootprintOrigin is not null&&b.FootprintOrigin!="authored"||b.Design?.Floors?.Any(f=>f?.StaticModels?.Any(m=>m?.Interactive==true)==true)==true);
 public static bool MapHasNewAuthoring(MapProject p)=>p.LinearFeatures?.Count>0||p.GameZones?.Count>0||p.StaticModels?.Any(m=>m?.Interactive==true)==true||(p.Buildings??[]).Concat(p.BuildingLibrary??[]).Any(BuildingHasNewAuthoring);
 public static bool WorldHasNewAuthoring(WorldProject w)=>w.Maps?.Any(m=>m?.Project?.Version>=9)==true||w.Buildings?.Any(BuildingHasNewAuthoring)==true;
}
