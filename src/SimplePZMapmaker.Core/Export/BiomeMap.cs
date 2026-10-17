namespace SimplePZMapmaker.Core.Export;

/// <summary>Build 42 reads media/maps/&lt;map&gt;/maps/biomemap_&lt;cellX&gt;_&lt;cellY&gt;.png, one 256x256 image per native cell.
/// Each value is looked up in the game's BiomeMapConfig: red selects the WorldGen biome of a square, green the foraging zone,
/// read once per 8x8 chunk. Without these images a map has no foraging zones in Build 42: zones such as Forest or TownZone
/// in objects.lua are not registered by the server. Like the vanilla maps, red repeats the green value on land (vanilla
/// Muldraugh uses 230/230 birch-mix forest and 115/115 town) and blue repeats red.
/// Values from the 42.20 table: 0 Water, 64 ForagingNav, 102 TrailerPark, 115 TownZone, 128 Farm, 141 FarmLand,
/// 230 BirchMixForest, 255 DeepForest.</summary>
public static class BiomeMap
{
 public const byte Water=0,Nav=64,TrailerPark=102,Town=115,Farm=128,FarmLand=141,Forest=230,DeepForest=255;
 /// <summary>Foraging zone (green value) of each 8x8 chunk of a native cell, 32x32 values.</summary>
 public static byte[] Zones(MapProject p,int cellX,int cellY){
  var chunks=p.Width/8;var rows=p.Height/8;var kind=new byte[chunks*rows];var built=new bool[chunks*rows];
  foreach(var b in p.Buildings)for(var y=Math.Max(0,b.Y/8);y<=Math.Min(rows-1,(b.Y+b.Height)/8);y++)for(var x=Math.Max(0,b.X/8);x<=Math.Min(chunks-1,(b.X+b.Width)/8);x++)built[y*chunks+x]=true;
  // Majority terrain of each chunk of the whole map, so forest depth and town proximity see across cell edges.
  const byte water=0,forest=1,paved=2,open=3;
  for(var cy=0;cy<rows;cy++)for(var cx=0;cx<chunks;cx++){
   int w=0,f=0,r=0;for(var y=0;y<8;y++)for(var x=0;x<8;x++){var t=p.Terrain[(cy*8+y)*p.Width+cx*8+x];if(t==Terrain.Water)w++;else if(t==Terrain.Forest)f++;else if(t is Terrain.Road or Terrain.Pavement)r++;}
   kind[cy*chunks+cx]=w>32?water:f>32?forest:r>8?paved:open;
  }
  bool Near(int cx,int cy,int radius,Func<int,bool> test){for(var y=Math.Max(0,cy-radius);y<=Math.Min(rows-1,cy+radius);y++)for(var x=Math.Max(0,cx-radius);x<=Math.Min(chunks-1,cx+radius);x++)if(test(y*chunks+x))return true;return false;}
  var result=new byte[32*32];
  for(var y=0;y<32;y++)for(var x=0;x<32;x++){
   var cx=cellX*32+x;var cy=cellY*32+y;var i=cy*chunks+cx;
   result[y*32+x]=Authored(p,cx*8+4,cy*8+4)??(kind[i] switch{
    water=>Water,
    forest=>Near(cx,cy,3,j=>kind[j]!=forest)?Forest:DeepForest,
    _ when built[i]||Near(cx,cy,2,j=>built[j])=>Town,
    paved=>Nav,
    _=>FarmLand});
  }
  return result;
 }
 /// <summary>The foraging zone of an authored terrain zone covering a tile, if any.</summary>
 static byte? Authored(MapProject p,int x,int y){
  foreach(var z in p.GameZones)if(z.Layer=="terrain"&&ZoneGeometry.Covers(z,x,y))return z.Type switch{"Forest"=>Forest,"DeepForest"=>DeepForest,"Farm"=>Farm,"TownZone"=>Town,"TrailerPark"=>TrailerPark,"Vegitation"=>FarmLand,"Nav"=>Nav,_=>null};
  return null;
 }
 /// <summary>The biome map image of one native cell: the chunk's zone in red, green and blue, and 0 on water squares.</summary>
 public static byte[] Png(MapProject p,int cellX,int cellY){
  var zones=Zones(p,cellX,cellY);var rgba=new byte[256*256*4];
  for(var y=0;y<256;y++)for(var x=0;x<256;x++){var i=(y*256+x)*4;var zone=zones[y/8*32+x/8];var red=p.Terrain[(cellY*256+y)*p.Width+cellX*256+x]==Terrain.Water?Water:zone;rgba[i]=red;rgba[i+1]=zone;rgba[i+2]=red;rgba[i+3]=255;}
  return Export.Png.Encode(new(256,256,rgba));
 }
}
