using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Security.Cryptography;
namespace SimplePZMapmaker.Core.Export;
public static class NativeMapExporter {
    public static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web){WriteIndented=false};
    public static string ModId(MapProject p)=>"SimplePZMap_"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{p.Name}|{p.Seed}")))[..12].ToLowerInvariant();
    public static byte[] Export(MapProject p)
    {
        ProjectValidator.Validate(p);
        using var output=new MemoryStream();using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)) {
            var slug=ModId(p);var root=$"{slug}/42/media/maps/{slug}/";
            void Text(string path,string text){var e=zip.CreateEntry(path,CompressionLevel.Fastest);using var w=new StreamWriter(e.Open(),new UTF8Encoding(false));w.Write(text);}
            void Bytes(string path,byte[] bytes){var e=zip.CreateEntry(path,CompressionLevel.Fastest);using var s=e.Open();s.Write(bytes);}
            zip.CreateEntry($"{slug}/common/");
            Text($"{slug}/42/mod.info",$"name={p.Name}\nid={slug}\ndescription=Карта создана в SimplePZMapmaker. Build 42; новый мир.\nversionMin=42.0\n");
            Text(root+"map.info",$"title={p.Name}\nfixed2x=true\ndescription=Карта {p.Width}×{p.Height}, SimplePZMapmaker\nzoomX={p.WorldX*256+p.Width/2}\nzoomY={p.WorldY*256+p.Height/2}\nzoomS=8\n");
            var spawns=string.Join(",\n",p.Spawns.Select(s=>$"        {{ posX = {p.WorldX*256+s.X}, posY = {p.WorldY*256+s.Y}, posZ = 0 }}"));
            Text(root+"spawnpoints.lua",$"function SpawnPoints()\n    local points = {{\n{spawns}\n    }}\n    return {{ unemployed = points }}\nend\n");
            Text(root+"spawnregions.lua",$"function SpawnRegions()\n return {{ {{ name = \"{slug}\", file = \"media/maps/{slug}/spawnpoints.lua\" }} }}\nend\n");
            var zones=new StringBuilder("objects = {\n");
            for(var cy=0;cy<p.Height/8;cy++)for(var cx=0;cx<p.Width/8;cx++) {
                var t=p.Terrain[cy*8*p.Width+cx*8];var type=t switch{Terrain.Forest=>"Forest",Terrain.Water=>"Water",Terrain.Grass=>"Vegitation",_=>"TownZone"};
                if(t==Terrain.Water)continue;
                zones.Append(Culture($" {{ name = \"\", type = \"{type}\", x = {p.WorldX*256+cx*8}, y = {p.WorldY*256+cy*8}, z = 0, width = 8, height = 8 }},\n"));
            }
            zones.Append("}\n");Text(root+"objects.lua",zones.ToString());
            var attribution=p.Attribution.Concat(p.Buildings.Concat(p.BuildingLibrary).SelectMany(b=>b.Attribution)).Distinct();
            Text($"{slug}/ATTRIBUTION.txt",string.Join('\n',attribution.Prepend("Created with SimplePZMapmaker. Native export format: Build 42 (256-cell / 8-chunk)."))+"\n");
            Text($"{slug}/project.pzmap.json",JsonSerializer.Serialize(p,JsonOptions));
            Text($"{slug}/INSTALL.txt",$"Распакуйте {slug} в папку Zomboid/mods. Включите мод, создайте НОВУЮ игру и выберите карту {p.Name}. Не добавляйте в важный существующий мир. Генератор не устанавливает мод автоматически. Другие карты, занимающие те же координаты, могут конфликтовать. Для своего сервера укажите Map={slug} и Mods={slug}. Экспорт B42; совместимость в игре проверяйте на отдельном новом сохранении. Идентификатор зависит от названия и seed; их изменение создаёт другой мод.\n");
            Text(root+"worldmap.xml",WorldMap(p));
            for(var cellY=0;cellY<p.Height/256;cellY++)for(var cellX=0;cellX<p.Width/256;cellX++) {
                var composer=new TileComposer(p,cellX,cellY);
                var names=new HashSet<string>(StringComparer.Ordinal);
                for(var z=0;z<=composer.MaxLevel;z++)for(var y=0;y<256;y++)for(var x=0;x<256;x++) foreach(var name in composer.At(x,y,z))names.Add(name);
                var tiles=names.Order(StringComparer.Ordinal).ToArray();var ids=tiles.Select((name,id)=>(name,id)).ToDictionary(t=>t.name,t=>t.id);
                Bytes(root+$"{p.WorldX+cellX}_{p.WorldY+cellY}.lotheader",Header(p,composer,tiles,cellX,cellY));
                Bytes(root+$"world_{p.WorldX+cellX}_{p.WorldY+cellY}.lotpack",Pack(composer,ids));
                Bytes(root+$"chunkdata_{p.WorldX+cellX}_{p.WorldY+cellY}.bin",Navigation(composer));
            }
        }
        return output.ToArray();
    }
    private static string Culture(FormattableString s)=>s.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static void Line(BinaryWriter w,string text){w.Write(Encoding.ASCII.GetBytes(text));w.Write((byte)10);}
    private static byte[] Header(MapProject p,TileComposer c,string[] tiles,int cx,int cy) {
        using var s=new MemoryStream();using var w=new BinaryWriter(s);w.Write("LOTH"u8);w.Write(1);w.Write(tiles.Length);foreach(var tile in tiles)Line(w,tile);
        w.Write(8);w.Write(8);w.Write(0);w.Write(c.MaxLevel);w.Write(c.Rooms.Count);
        foreach(var r in c.Rooms){Line(w,r.Name);w.Write(r.Level);var rects=r.Rectangles??[new(r.X,r.Y,r.Width,r.Height)];w.Write(rects.Count);foreach(var rectangle in rects){w.Write(rectangle.X);w.Write(rectangle.Y);w.Write(rectangle.Width);w.Write(rectangle.Height);}w.Write(0);}
        w.Write(c.Buildings.Count);foreach(var b in c.Buildings){w.Write(b.Count);foreach(var id in b)w.Write(id);}
        for(var x=0;x<32;x++)for(var y=0;y<32;y++)w.Write((byte)p.Zombies[(cy*32+y)*(p.Width/8)+cx*32+x]);
        return s.ToArray();
    }
    private static byte[] Pack(TileComposer c,Dictionary<string,int> ids) {
        using var s=new MemoryStream();using var w=new BinaryWriter(s);w.Write("LOTP"u8);w.Write(1);w.Write(1024);var offsets=new long[1024];for(var i=0;i<1024;i++)w.Write(0L);
        for(var cx=0;cx<32;cx++)for(var cy=0;cy<32;cy++) {
            offsets[cx*32+cy]=s.Position;var skip=0;
            for(var z=0;z<=c.MaxLevel;z++)for(var x=cx*8;x<cx*8+8;x++)for(var y=cy*8;y<cy*8+8;y++) {
                var tiles=c.At(x,y,z);if(tiles.Count==0){skip++;continue;}if(skip>0){w.Write(-1);w.Write(skip);skip=0;}
                w.Write(tiles.Count+1);w.Write(c.RoomAt(x,y,z));foreach(var tile in tiles)w.Write(ids[tile]);
            }
            if(skip>0){w.Write(-1);w.Write(skip);}
        }
        s.Position=12;foreach(var offset in offsets)w.Write(offset);return s.ToArray();
    }
    private static byte[] Navigation(TileComposer c) {
        using var s=new MemoryStream();s.WriteByte(0);s.WriteByte(1);
        for(var cy=0;cy<32;cy++)for(var cx=0;cx<32;cx++) {
            var bits=new byte[64];for(var y=0;y<8;y++)for(var x=0;x<8;x++)bits[y*8+x]=c.Navigation[(cy*8+y)*256+cx*8+x];
            if(bits.All(b=>b==bits[0]) && bits[0] is 0 or 1 or 8 or 16 or 32) s.WriteByte(bits[0] switch{0=>0,1=>1,8=>3,16=>4,_=>5});
            else{s.WriteByte(2);s.Write(bits);}
        }
        return s.ToArray();
    }
    private static string WorldMap(MapProject p) {
        var world=new XElement("world",new XAttribute("version","1.0"),new XAttribute("cellSize",256));
        for(var cy=0;cy<p.Height/256;cy++)for(var cx=0;cx<p.Width/256;cx++) {
            var cell=new XElement("cell",new XAttribute("x",p.WorldX+cx),new XAttribute("y",p.WorldY+cy));
            void Feature(string key,string value,int x,int y,int w,int h) {
                cell.Add(new XElement("feature",new XElement("geometry",new XAttribute("type","Polygon"),new XElement("coordinates",new XElement("point",new XAttribute("x",x),new XAttribute("y",y)),new XElement("point",new XAttribute("x",x+w),new XAttribute("y",y)),new XElement("point",new XAttribute("x",x+w),new XAttribute("y",y+h)),new XElement("point",new XAttribute("x",x),new XAttribute("y",y+h)))),new XElement("properties",new XElement("property",new XAttribute("name",key),new XAttribute("value",value)))));
            }
            foreach(var b in p.Buildings.Where(b=>b.X/256==cx&&b.Y/256==cy)){
                if(b.Design is null){Feature("building","yes",b.X%256,b.Y%256,b.Width,b.Height);continue;}
                var rectangles=new List<(int X,int Y,int Width,int Height)>();
                for(var y=0;y<b.Height;y++)for(var x=0;x<b.Width;){if(!BuildingDesigner.Occupies(b,x,y)){x++;continue;}var start=x;while(x<b.Width&&BuildingDesigner.Occupies(b,x,y))x++;var i=rectangles.FindLastIndex(r=>r.X==start&&r.Width==x-start&&r.Y+r.Height==y);if(i<0)rectangles.Add((start,y,x-start,1));else{var r=rectangles[i];rectangles[i]=(r.X,r.Y,r.Width,r.Height+1);}}
                foreach(var r in rectangles)Feature("building","yes",b.X%256+r.X,b.Y%256+r.Y,r.Width,r.Height);
            }
            // Merge equal terrain runs to keep the in-game map bounded.
            for(var y=0;y<256;y+=8)for(var x=0;x<256;) {
                var terrain=p.Terrain[(cy*256+y)*p.Width+cx*256+x];var end=x+8;while(end<256&&p.Terrain[(cy*256+y)*p.Width+cx*256+end]==terrain)end+=8;
                if(terrain==Terrain.Road)Feature("highway","residential",x,y,end-x,8);
                else if(terrain==Terrain.Water)Feature("water","river",x,y,end-x,8);
                else if(terrain==Terrain.Forest)Feature("natural","forest",x,y,end-x,8);
                x=end;
            }
            world.Add(cell);
        }
        return new XDocument(new XDeclaration("1.0","UTF-8",null),world).ToString();
    }
}
