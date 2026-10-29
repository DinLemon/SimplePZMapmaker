using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;
using System.IO.Compression;
using System.Text;
public static class TilesetBuilderTests
{
 /// <summary>A 128x256 frame with an opaque block at (40..79, 150..229) in one colour.</summary>
 static byte[] Frame(byte r,byte g,byte b){var rgba=new byte[128*256*4];for(var y=150;y<230;y++)for(var x=40;x<80;x++){var i=(y*128+x)*4;rgba[i]=r;rgba[i+1]=g;rgba[i+2]=b;rgba[i+3]=255;}return Png.Encode(new(128,256,rgba));}
 public static IEnumerable<(string,Action)> Cases(){
  yield return("tileset builder enforces the game limit of 512 sprites per set",()=>{
   var sprite=new TilesetSprite(Frame(1,2,3),new(){["solidfloor"]=""});
   TilesetBuilder.Validate(new("Ok","Ok","capacity_01",1708,Enumerable.Repeat(sprite,512).ToList()));
   Reject(()=>TilesetBuilder.Validate(new("Ok","Ok","capacity_01",1708,Enumerable.Repeat(sprite,513).ToList())));
  });
  yield return("tileset definitions enforce the game limit of 512 records independently of ZIP validation",()=>{
   var data=TilesetBuilder.Tiles("capacity_01",Enumerable.Range(0,512).Select(_=>new Dictionary<string,string>{{"solidfloor",""}}).ToList());
   Check(TileDefinitionReader.Read(new MemoryStream(data)).Count==512,"Valid boundary rejected");
   Reject(()=>TilesetBuilder.Tiles("capacity_01",Enumerable.Range(0,513).Select(_=>new Dictionary<string,string>()).ToList()));
  });
  yield return("tileset builder uses a stable positive Build 42 tileset number",()=>{
   var data=TilesetBuilder.Tiles("pilna_photo_01",[new(){["WallW"]=""}]);
   using var reader=new BinaryReader(new MemoryStream(data),Encoding.Latin1);
   Check(Encoding.ASCII.GetString(reader.ReadBytes(4))=="tdef"&&reader.ReadInt32()==1&&reader.ReadInt32()==1,"Expected one versioned tileset");
   string Line(){var value=new StringBuilder();byte c;while((c=reader.ReadByte())!=10)value.Append((char)c);return value.ToString();}
   Check(Line()=="pilna_photo_01"&&Line()=="pilna_photo_01.png","Unexpected tileset header");
   Check(reader.ReadInt32()==8&&reader.ReadInt32()==1,"Unexpected tileset dimensions");
   Check(reader.ReadInt32()==1,"Build 42 rejects number zero before loading custom tile properties");
  });
  yield return("tileset builder writes a pack and tile definitions the editor readers accept",()=>{
   var mod=new TilesetMod("PilnaTiles","Pilna tiles","pilna_fence_01",300,[new(Frame(200,40,40),new(){["WallW"]="",["CustomName"]="Pilna fence"}),new(Frame(40,200,40),new(){["WallN"]=""})]);
   using var archive=new MemoryStream();TilesetBuilder.Write(mod,archive);using var zip=new ZipArchive(new MemoryStream(archive.ToArray()));
   string Text(string p){using var r=new StreamReader(zip.GetEntry(p)!.Open(),Encoding.UTF8);return r.ReadToEnd();}
   var info=Text("PilnaTiles/42/mod.info");Check(info.Contains("pack=pilna_fence_01\n")&&info.Contains("tiledef=pilna_fence_01 300\n")&&info.Contains("id=PilnaTiles\n"),"mod.info does not register the pack and tiledef");
   using var pack=new MemoryStream();zip.GetEntry("PilnaTiles/42/media/texturepacks/pilna_fence_01.pack")!.Open().CopyTo(pack);pack.Position=0;var page=TexturePackIndex.Read(pack,"pilna_fence_01.pack").Single();
   var first=page.Sprites.Single(s=>s.Name=="pilna_fence_01_0");Check(page.Sprites.Count==2&&first.FrameWidth==128&&first.FrameHeight==256&&first.OffsetX==40&&first.OffsetY==150&&first.Width==40&&first.Height==80,"Sprite trim or frame size is wrong");
   pack.Position=page.Offset;var png=new byte[page.Length];pack.ReadExactly(png);var atlas=Png.Decode(png);var pixel=((first.Y+10)*atlas.Width+first.X+10)*4;Check(atlas.Rgba[pixel]==200&&atlas.Rgba[pixel+1]==40&&atlas.Rgba[pixel+3]==255,"Atlas pixels do not match the frame");
   using var tiles=new MemoryStream();zip.GetEntry("PilnaTiles/42/media/pilna_fence_01.tiles")!.Open().CopyTo(tiles);tiles.Position=0;var defs=TileDefinitionReader.Read(tiles);
   Check(defs.Count==8&&defs[0].Id=="pilna_fence_01_0"&&defs[0].Properties["CustomName"]=="Pilna fence"&&defs[1].Properties.ContainsKey("WallN")&&defs[2].Properties.Count==0,"Tile definitions do not round-trip");
  });
  yield return("tileset builder rejects bad names numbers and non-RGBA frames",()=>{
   Reject(()=>TilesetBuilder.Validate(new("Ok","Ok","bad name",300,[new(Frame(1,1,1),[])])));
   Reject(()=>TilesetBuilder.Validate(new("Ok","Ok","good_01",42,[new(Frame(1,1,1),[])])));
   Reject(()=>TilesetBuilder.Write(new("Ok","Ok","good_01",300,[new([1,2,3],[])]),new MemoryStream()));
   var round=Png.Decode(Png.Encode(new(3,2,[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24])));Check(round.Rgba.SequenceEqual(new byte[]{1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24}),"PNG codec does not round-trip");
  });
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected validation failure");}
}
