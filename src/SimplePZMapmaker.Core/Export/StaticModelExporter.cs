using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
namespace SimplePZMapmaker.Core.Export;
public static class StaticModelExporter
{
 public sealed record PlacedStaticModel(StaticModelPlacement Placement,int X,int Y,int Z);
 public static IEnumerable<PlacedStaticModel> Gather(MapProject project)
 {
  foreach(var m in project.StaticModels)yield return new(m,project.WorldX*256+m.X,project.WorldY*256+m.Y,m.Z);
  foreach(var b in project.Buildings)if(b.Design is {} d)for(var i=0;i<d.Floors.Count;i++)foreach(var m in d.Floors[i].StaticModels)yield return new(m,project.WorldX*256+b.X+m.X,project.WorldY*256+b.Y+m.Y,d.MinLevel+i);
 }
 public static void Write(ZipArchive zip,string modRoot,IEnumerable<PlacedStaticModel> source)
 {
  var placements=source.ToArray();if(placements.Length==0)return;if(placements.Length>100000)throw new ArgumentException("Too many static models.");
  var root=modRoot.TrimEnd('/')+"/42/media/";
  var module="SPZM"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(modRoot)))[..12];
  string Proxy(string model)=>"Prop"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(model)))[..16];
  string Scene(PlacedStaticModel model)=>"Scene"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(model.Placement.Id+":"+model.X+":"+model.Y+":"+model.Z)))[..24];
  var scripts=new StringBuilder("module "+module+"\n{\n");
  foreach(var model in placements.Where(p=>p.Placement.Profile=="pickup").Select(p=>p.Placement.ModelId).Distinct(StringComparer.Ordinal)){
   StaticModelPlacement.Validate(new(){ModelId=model},1,1);
   scripts.Append(" item ").Append(Proxy(model)).Append("\n {\n Type = Normal,\n DisplayName = Static scenery,\n Weight = 0.1,\n Icon = Question_On,\n WorldStaticModel = ").Append(model).Append(",\n }\n");
  }
  foreach(var p in placements.Where(p=>p.Placement.Profile!="pickup").DistinctBy(Scene)){
   var m=p.Placement;StaticModelPlacement.Validate(m,int.MaxValue,int.MaxValue);
   scripts.Append(" spriteModel ").Append(Scene(p)).Append("\n {\n modelScript = ").Append(m.ModelId).Append(",\n translate = ").Append(N(m.OffsetX-.5)).Append(' ').Append(N(m.OffsetZ)).Append(' ').Append(N(m.OffsetY-.5)).Append(",\n rotate = ").Append(N(m.WorldRotation[0])).Append(' ').Append(N(m.WorldRotation[1]+m.Rotation)).Append(' ').Append(N(m.WorldRotation[2])).Append(",\n scale = ").Append(N(m.Scale*m.WorldScale)).Append(",\n }\n");
  }
  scripts.Append("}\n");Text(root+"scripts/"+module+".txt",scripts.ToString());
  var lua=new StringBuilder("-- Original SimplePZMapmaker glue. Registered models remain in their owning mods.\nlocal placements = {\n");
  foreach(var g in placements.GroupBy(p=>$"{p.X}:{p.Y}:{p.Z}")){
   lua.Append(" [\"").Append(g.Key).Append("\"] = {\n");
   foreach(var p in g){var m=p.Placement;StaticModelPlacement.Validate(m,int.MaxValue,int.MaxValue);lua.Append(" { id=\"").Append(m.Id).Append("\", profile=\"").Append(m.Profile).Append("\", type=\"").Append(module).Append('.').Append(Proxy(m.ModelId)).Append("\", spriteModel=\"").Append(module).Append('.').Append(Scene(p)).Append("\", blocking=").Append(m.Profile is "obstacle" or "destructible" or "container"?"true":"false").Append(", destructible=").Append(m.Profile is "destructible" or "container"?"true":"false").Append(", health=").Append(m.Health).Append(", capacity=").Append(m.Capacity).Append(", x=").Append(N(m.OffsetX)).Append(", y=").Append(N(m.OffsetY)).Append(", z=").Append(N(m.OffsetZ)).Append(", rotation=").Append(N(m.Rotation)).Append(", scale=").Append(N(m.Scale)).Append(" },\n");}
   lua.Append(" },\n");
  }
  lua.Append("}\nlocal function load(square)\n if isClient() then return end\n local list=placements[square:getX()..':'..square:getY()..':'..square:getZ()]\n if not list then return end\n local data=square:getModData()\n data.").Append(module).Append("=data.").Append(module).Append(" or {}\n local done=data.").Append(module).Append("\n for _,p in ipairs(list) do\n  if not done[p.id] then\n   if p.profile=='pickup' then\n    local item=instanceItem(p.type)\n    if item then\n     item:setWorldScale(p.scale)\n     item:setWorldZRotation(p.rotation)\n     local placed=square:AddWorldInventoryItem(item,p.x,p.y,p.z,false)\n     if placed and placed:getWorldItem() then\n      placed:getWorldItem():setIgnoreRemoveSandbox(true)\n      placed:getWorldItem():transmitCompleteItemToClients()\n      done[p.id]=true\n     end\n    end\n   else\n    local object=IsoThumpable.new(getCell(),square,'invisible_01_0',false,{})\n    object:setSpriteModelName(p.spriteModel)\n    object:setName('Static scenery')\n    object:setCanPassThrough(not p.blocking)\n    object:setBlockAllTheSquare(p.blocking)\n    object:setIsThumpable(p.destructible)\n    object:setIsDismantable(false)\n    object:setMaxHealth(p.health)\n    object:setHealth(p.health)\n    object:getModData().SPZMPlacementId=p.id\n    if p.profile=='container' then\n     object:setIsContainer(true)\n     object:getContainer():setCapacity(p.capacity)\n     object:getContainer():setExplored(true)\n    end\n    square:AddSpecialObject(object)\n    square:RecalcAllWithNeighbours(true)\n    object:transmitCompleteItemToClients()\n    done[p.id]=true\n   end\n  end\n end\nend\nEvents.LoadGridsquare.Add(load)\n");
  Text(root+"lua/server/"+module+".lua",lua.ToString());
  void Text(string path,string value){using var writer=new StreamWriter(zip.CreateEntry(path,CompressionLevel.Fastest).Open(),new UTF8Encoding(false));writer.Write(value);}
 }
 private static string N(double value)=>value.ToString("R",CultureInfo.InvariantCulture);
}
