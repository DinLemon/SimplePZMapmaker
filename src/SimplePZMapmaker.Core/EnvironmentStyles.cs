namespace SimplePZMapmaker.Core;
public static class EnvironmentStyles
{
 public static readonly Dictionary<string,string> Names=new(){["legacy"]="Legacy appearance",["temperate"]="Mixed forest and town",["rural"]="Village and country roads",["urban"]="City and park trees",["dry"]="Dry terrain"};
 public static uint Hash(int x,int y,int seed)=>unchecked((uint)x*73856093u^(uint)y*19349663u^(uint)seed);
 public static string Floor(Terrain terrain,string theme,uint hash)=>terrain switch{Terrain.Forest=>theme=="dry"?"blends_natural_01_80":"blends_natural_01_16",Terrain.Road=>theme=="rural"?"blends_street_01_16":"blends_street_01_0",Terrain.Water=>"blends_natural_02_0",Terrain.Sand=>"blends_natural_01_0",Terrain.Dirt=>"blends_natural_01_64",Terrain.Pavement=>"blends_street_01_48",_=>theme=="dry"?"blends_natural_01_80":"blends_natural_01_32"};
 // Build 42 retains the old vegetation_trees definition but no longer ships its image.
 public static string Tree(string theme,uint hash){if(theme=="legacy")return "e_americanholly_1_2";var species=theme=="dry"?new[]{"e_virginiapine_1_2","e_canadianhemlock_1_2"}:theme=="urban"?new[]{"e_americanholly_1_2","e_redmaple_1_2","e_cockspurhawthorn_1_2"}:new[]{"e_americanholly_1_2","e_canadianhemlock_1_2","e_redmaple_1_2","e_cockspurhawthorn_1_2","e_virginiapine_1_2"};return species[hash%(uint)species.Length];}
}
