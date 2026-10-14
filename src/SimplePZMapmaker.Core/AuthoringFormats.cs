namespace SimplePZMapmaker.Core;
public static class AuthoringFormats
{
 public static bool BuildingHasNewAuthoring(Building? b)=>b is not null&&(b.SourceOutline?.Count>0||b.FootprintOrigin is not null&&b.FootprintOrigin!="authored"||b.Design?.Floors?.Any(f=>f?.StaticModels?.Any(m=>m?.Interactive==true)==true)==true);
 public static bool MapHasNewAuthoring(MapProject p)=>p.LinearFeatures?.Count>0||p.GameZones?.Count>0||p.StaticModels?.Any(m=>m?.Interactive==true)==true||(p.Buildings??[]).Concat(p.BuildingLibrary??[]).Any(BuildingHasNewAuthoring);
 public static bool WorldHasNewAuthoring(WorldProject w)=>w.Maps?.Any(m=>m?.Project?.Version>=9)==true||w.Buildings?.Any(BuildingHasNewAuthoring)==true;
}
