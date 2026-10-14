using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
namespace SimplePZMapmaker.Core.Export;
/// <summary>Original glue driving installed native objects, collision edges and light sources.</summary>
public static class InteractiveModelExporter
{
 public static void Write(ZipArchive zip,string modRoot,IReadOnlyList<StaticModelExporter.PlacedStaticModel> placements)
 {
  string root=modRoot.TrimEnd('/')+"/42/media/",module="SPZM"+Hash(modRoot,12);var scripts=new StringBuilder("module "+module+"\n{\n");StringBuilder data=new StringBuilder("local placements={\n"),bindings=new StringBuilder("Distributions=Distributions or {}\nlocal bindings={all={\n");var bound=new HashSet<string>();var count=0;
  foreach(var p in placements){var m=p.Placement;StaticModelPlacement.Validate(m,int.MaxValue,int.MaxValue);string scene="Scene"+Hash(m.Id+":"+p.X+":"+p.Y+":"+p.Z,24),proxy="Prop"+Hash(m.ModelId,16),lootType="Loot"+Hash(m.LootDistribution??"",16);if(m.Profile=="pickup"){if(bound.Add("item:"+proxy))scripts.Append(" item "+proxy+" { Type=Normal, DisplayName=Static scenery, Weight=0.1, Icon=Question_On, WorldStaticModel="+m.ModelId+", }\n");}else{Sprite(scene,m,0);if(m.Door is not null)Sprite(scene+"Open",m,90);}
   if(m.LootDistribution is {} distribution&&bound.Add("loot:"+lootType))bindings.Append(" ["+Q(lootType)+"]={procedural=true,procList={{name="+Q(distribution)+",min=1,max=1}}},\n");
   foreach(var cell in StaticModelPlacement.FootprintCells(m)){if(++count>500000)throw new ArgumentException("Interactive objects exceed 500,000 footprint tiles.");var anchor=cell.Dx==0&&cell.Dy==0;data.Append("{id="+Q(scene+":"+cell.Dx+":"+cell.Dy)+",group="+Q(scene)+",x="+(p.X+cell.Dx)+",y="+(p.Y+cell.Dy)+",z="+p.Z+",anchor="+Bool(anchor)+",profile="+Q(m.Profile)+",spriteModel="+Q(module+"."+scene)+",openModel="+Q(module+"."+scene+"Open")+",type="+Q(module+"."+proxy)+",blocking="+Bool(StaticModelPlacement.BlocksTiles(m))+",destructible="+Bool(m.Profile is "destructible" or "container" or "door")+",health="+m.Health+",capacity="+m.Capacity+",offsetX="+N(m.OffsetX)+",offsetY="+N(m.OffsetY)+",offsetZ="+N(m.OffsetZ)+",rotation="+N(m.Rotation)+",scale="+N(m.Scale));if(m.Door is {} door)data.Append(",door={north="+Bool(door.Direction=="n")+",open="+Bool(door.Open)+",locked="+Bool(door.Locked)+",keyId="+door.KeyId+"}");if(anchor&&m.Light is {} light)data.Append(",light={r="+N(light.Rgb[0])+",g="+N(light.Rgb[1])+",b="+N(light.Rgb[2])+",radius="+light.Radius+",active="+Bool(light.Active)+"}");if(anchor&&m.LootDistribution is {} name)data.Append(",loot="+Q(name)+",lootType="+Q(lootType));data.Append("},\n");}
  }
  scripts.Append("}\n");data.Append("}\n");Text(root+"scripts/"+module+".txt",scripts.ToString());if(bound.Any(k=>k.StartsWith("loot:"))){bindings.Append("}}\ntable.insert(Distributions,1,bindings)\n");Text(root+"lua/server/Items/"+module+"Loot.lua",bindings.ToString());}
  var lua="-- Original SimplePZMapmaker native gameplay glue. Power state is authored, not simulated.\nlocal module="+Q(module)+"\n"+data+Runtime;Text(root+"lua/shared/"+module+"Gameplay.lua",lua);Text(root+"lua/server/"+module+".lua","require '"+module+"Gameplay'\n");Text(root+"lua/client/"+module+".lua","require '"+module+"Gameplay'\n");
  void Sprite(string name,StaticModelPlacement m,int angle){scripts.Append(" spriteModel "+name+" { modelScript="+m.ModelId+", translate="+N(m.OffsetX-.5)+" "+N(m.OffsetZ)+" "+N(m.OffsetY-.5)+", rotate="+N(m.WorldRotation[0])+" "+N(m.WorldRotation[1]+m.Rotation+angle)+" "+N(m.WorldRotation[2])+", scale="+N(m.Scale*m.WorldScale)+", }\n");}
  void Text(string path,string value){if(value.Length>64*1024*1024)throw new ArgumentException("Interactive model glue exceeds 64 MB.");using var w=new StreamWriter(zip.CreateEntry(path,CompressionLevel.Fastest).Open(),new UTF8Encoding(false));w.Write(value);}
 }
 private static string Hash(string text,int length)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..length];private static string N(double v)=>v.ToString("R",CultureInfo.InvariantCulture);private static string Bool(bool value)=>value?"true":"false";private static string Q(string v)=>"\""+v.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";
 private const string Runtime="""
local bySquare={}
for _,p in ipairs(placements) do
 local k=p.x..':'..p.y..':'..p.z
 bySquare[k]=bySquare[k] or {};table.insert(bySquare[k],p)
end
local tracked={};local lights={};local pending={};local ticks=0
local function find(square,id)
 local objects=square:getSpecialObjects()
 for i=0,objects:size()-1 do local o=objects:get(i);if o:getModData().SPZMPlacementId==id then return o end end
end
local function removed() return ModData.getOrCreate(module..'Removed') end
local function remember(square,p,object)
 tracked[p.id]={p=p,object=object,square=square}
end
local function load(square)
 local list=bySquare[square:getX()..':'..square:getY()..':'..square:getZ()];if not list then return end
 pending[square:getX()..':'..square:getY()..':'..square:getZ()]=square
 if isClient() then return end
 local data=square:getModData();data[module]=data[module] or {};local done=data[module]
 for _,p in ipairs(list) do
  local object=find(square,p.id)
  if not removed()[p.group] and not done[p.id] then
   if p.profile=='pickup' then
    local item=instanceItem(p.type)
    if item then item:setWorldScale(p.scale);item:setWorldZRotation(p.rotation);local placed=square:AddWorldInventoryItem(item,p.offsetX,p.offsetY,p.offsetZ,false)
     if placed and placed:getWorldItem() then placed:getWorldItem():setIgnoreRemoveSandbox(true);placed:getWorldItem():transmitCompleteItemToClients();done[p.id]=true end
    end
   else
    if p.door then object=IsoThumpable.new(getCell(),square,'invisible_01_0','invisible_01_0',p.door.north,{})
    else object=IsoThumpable.new(getCell(),square,'invisible_01_0',false,{}) end
    object:setName('Authored static object');object:setCanPassThrough(not p.blocking and not p.door);object:setBlockAllTheSquare(p.blocking)
    object:setIsThumpable(p.destructible);object:setIsDismantable(false);object:setMaxHealth(p.health);object:setHealth(p.health)
    object:getModData().SPZMPlacementId=p.id;object:getModData().SPZMCompound=p.group
    if p.anchor then object:setSpriteModelName(p.spriteModel) end
    if p.door then object:setIsDoor(true);object:setKeyId(p.door.keyId);object:setLockedByKey(p.door.locked);if p.door.open then object:ToggleDoorSilent() end end
    if p.anchor and p.profile=='container' then
     object:setIsContainer(true);local container=object:getContainer();container:setCapacity(p.capacity)
     if p.loot then
      local distribution=ItemPickerJava.getItemContainer('all',p.lootType,p.loot,false)
      if distribution then ItemPickerJava.doRollItem(distribution,container,0,nil,true,nil) else print('SimplePZMapmaker: missing registered loot '..p.loot) end
     end
     container:setExplored(true)
    end
    square:AddSpecialObject(object);square:RecalcAllWithNeighbours(true);object:transmitCompleteItemToClients();done[p.id]=true
   end
  end
  if object then remember(square,p,object) end
 end
end
local function sourceLight(p,object)
 if isServer() or not p.light or lights[p.id] then return end
 local l=p.light;local source=IsoLightSource.new(p.x,p.y,p.z,l.r,l.g,l.b,l.radius)
 source:setActive(l.active);object:setLightSource(source);object:setLightSourceRadius(l.radius);object:setLightSourceOn(l.active)
 getCell():addLamppost(source);lights[p.id]={source=source,object=object,square=object:getSquare()}
end
local function tick()
 ticks=ticks+1;if ticks%30~=0 then return end
 for key,square in pairs(pending) do
  if getCell():getGridSquare(square:getX(),square:getY(),square:getZ())~=square then pending[key]=nil
  else for _,p in ipairs(bySquare[key]) do local object=find(square,p.id);if object then remember(square,p,object);sourceLight(p,object) end end end
 end
 local health={};local dead=not isClient() and removed() or nil
 for id,t in pairs(tracked) do
  local p=t.p;local square=getCell():getGridSquare(p.x,p.y,p.z)
  if square~=t.square then tracked[id]=nil
  else
   local object=find(square,id)
   if not object then if dead and (square:getModData()[module] or {})[id] then dead[p.group]=true end;tracked[id]=nil
   else
    if p.door and p.anchor then object:setSpriteModelName(object:IsOpen() and p.openModel or p.spriteModel) end
    if p.destructible and dead then health[p.group]=math.min(health[p.group] or p.health,object:getHealth());if object:getHealth()<=0 then dead[p.group]=true end end
   end
  end
 end
 if dead then for id,t in pairs(tracked) do local p=t.p
  if dead[p.group] then t.square:transmitRemoveItemFromSquare(t.object);t.square:RecalcAllWithNeighbours(true);tracked[id]=nil
  elseif health[p.group] and t.object:getHealth()~=health[p.group] then t.object:setHealth(health[p.group]);t.object:transmitCompleteItemToClients() end
 end end
 for id,t in pairs(lights) do local p=tracked[id]
  if not p or p.object~=t.object then getCell():removeLamppost(t.source);lights[id]=nil end
 end
end
Events.LoadGridsquare.Add(load)
Events.OnTick.Add(tick)
""";
}
