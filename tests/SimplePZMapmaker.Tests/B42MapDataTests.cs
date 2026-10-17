using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using System.IO.Compression;
public static class B42MapDataTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("biome map gives every native cell foraging zones like the vanilla maps",()=>{
   var p=MapGenerator.Generate(new(){Kind="blank"});
   void Fill(int x0,int y0,int w,int h,Terrain t){for(var y=y0;y<y0+h;y++)for(var x=x0;x<x0+w;x++)p.Terrain[y*p.Width+x]=t;}
   Fill(0,0,96,96,Terrain.Forest);Fill(160,160,64,64,Terrain.Water);Fill(100,200,150,4,Terrain.Road);
   var b=new Building{X=150,Y=40,Width=12,Height=10};b.Design=BuildingDesigner.Generate(b,1,42);p.Buildings.Add(b);p.Spawns=[new(){X=130,Y=130}];ProjectValidator.Validate(p);
   var image=Png.Decode(BiomeMap.Png(p,0,0));(byte R,byte G,byte B) At(int x,int y){var i=(y*256+x)*4;return(image.Rgba[i],image.Rgba[i+1],image.Rgba[i+2]);}
   Check(At(40,40)==(255,255,255),"Forest interior should be DeepForest");Check(At(92,40).G==BiomeMap.Forest,"Forest edge should be BirchMixForest");
   Check(At(190,190)==(0,0,0),"Water should be 0");Check(At(155,45)==(115,115,115),"A building chunk is TownZone");
   Check(At(240,120).G==BiomeMap.FarmLand,"Open ground away from town is FarmLand");Check(At(240,201).G==BiomeMap.Nav,"A road chunk away from town is ForagingNav");
   for(var y=0;y<256;y+=8)for(var x=0;x<256;x+=8){var g=At(x,y).G;for(var dy=0;dy<8;dy++)for(var dx=0;dx<8;dx++)Check(At(x+dx,y+dy).G==g,"A chunk must have one foraging zone");}
   p.GameZones.Add(new(){Id=new string('a',32),Type="Farm",X=200,Y=8,Width=40,Height=24});Check(Png.Decode(BiomeMap.Png(p,0,0)).Rgba[(12*256+210)*4+1]==BiomeMap.Farm,"An authored Farm zone wins");
   using var zip=new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));Check(zip.Entries.Count(e=>e.FullName.Contains("/maps/biomemap_")&&e.Name.EndsWith(".png"))==1,"Each native cell needs its biome map");
  });
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
