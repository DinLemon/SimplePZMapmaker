namespace SimplePZMapmaker.Core;
public static class EnvironmentStyles
{
 public static readonly Dictionary<string,string> Names=new(){["legacy"]="Прежнее оформление",["temperate"]="Смешанный лес и город",["rural"]="Посёлок и просёлки",["urban"]="Город и парковые деревья",["dry"]="Сухая местность"};
 public static uint Hash(int x,int y,int seed)=>unchecked((uint)x*73856093u^(uint)y*19349663u^(uint)seed);
 public static string Floor(Terrain terrain,string theme,uint hash)=>terrain switch{Terrain.Forest=>theme=="dry"?"blends_natural_01_80":"blends_natural_01_16",Terrain.Road=>theme=="rural"?"blends_street_01_16":"blends_street_01_0",Terrain.Water=>"blends_natural_02_0",Terrain.Sand=>"blends_natural_01_0",Terrain.Dirt=>"blends_natural_01_64",Terrain.Pavement=>"blends_street_01_48",_=>theme=="dry"?"blends_natural_01_80":"blends_natural_01_32"};
 public static string Tree(string theme,uint hash){if(theme=="legacy")return "vegetation_trees_01_8";var species=theme=="dry"?new[]{"e_virginiapine_1_2","e_canadianhemlock_1_2"}:theme=="urban"?new[]{"e_americanholly_1_2","e_redmaple_1_2","e_cockspurhawthorn_1_2"}:new[]{"e_americanholly_1_2","e_canadianhemlock_1_2","e_redmaple_1_2","e_cockspurhawthorn_1_2","e_virginiapine_1_2"};return species[hash%(uint)species.Length];}
}
