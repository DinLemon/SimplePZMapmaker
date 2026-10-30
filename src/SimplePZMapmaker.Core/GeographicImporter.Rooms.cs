namespace SimplePZMapmaker.Core;

/// <summary>Room names from source tags. A Build 42 room name selects the loot distribution and zombie room filters, so a bakery
/// should be "bakery" rather than a generic sales floor. pz:room names the main room explicitly; otherwise shop, amenity and
/// building tags pick a vanilla room. Whole-building uses (barn, shed, church…) rename every above-ground room; shops and
/// services rename the sales room, and cafés and restaurants also turn their back rooms into kitchens.</summary>
public static partial class GeographicImporter
{
 static readonly Dictionary<string,string> WholeBuildingRooms=new(StringComparer.Ordinal){
  ["building=shed"]="shed",["building=barn"]="barn",["building=stable"]="barn",["building=cowshed"]="barn",["building=greenhouse"]="greenhouse",["building=farm_auxiliary"]="farmstorage",
  ["building=garage"]="garagestorage",["building=garages"]="garagestorage",["building=church"]="church",["building=chapel"]="church",["amenity=place_of_worship"]="church",
  ["building=warehouse"]="warehouse",["building=industrial"]="factory",["amenity=fire_station"]="firestorage",["pz:kind=shed"]="shed",["pz:kind=greenhouse"]="greenhouse"};
 static readonly Dictionary<string,(string Room,string? BackRoom)> SalesRooms=new(StringComparer.Ordinal){
  ["shop=bakery"]=("bakery","bakerykitchen"),["shop=butcher"]=("butcher",null),["shop=convenience"]=("conveniencestore",null),["shop=kiosk"]=("conveniencestore",null),
  ["shop=supermarket"]=("grocery","grocerystorage"),["shop=general"]=("generalstore","generalstorestorage"),["shop=greengrocer"]=("grocery",null),["shop=alcohol"]=("liquorstore",null),
  ["shop=hardware"]=("toolstore","toolstorestorage"),["shop=doityourself"]=("toolstore","toolstorestorage"),["shop=clothes"]=("clothingstore","clothingstorage"),["shop=shoes"]=("shoestore",null),
  ["shop=books"]=("bookstore",null),["shop=furniture"]=("furniturestore",null),["shop=electronics"]=("electronicstore","electronicsstorage"),["shop=mobile_phone"]=("electronicstore",null),
  ["shop=florist"]=("florist",null),["shop=jewelry"]=("jewelrystore","jewelrystorage"),["shop=gift"]=("giftstore","giftstorage"),["shop=pet"]=("petstore",null),
  ["shop=sports"]=("sportstore","sportstorage"),["shop=toys"]=("toystore","toystorestorage"),["shop=car_repair"]=("mechanic",null),["shop=car_parts"]=("carsupply",null),
  ["shop=pawnbroker"]=("pawnshop","pawnshopstorage"),["shop=tobacco"]=("tobaccostore","tobaccostorage"),["shop=garden_centre"]=("gardenstore",null),["shop=farm"]=("generalstore",null),
  ["amenity=pharmacy"]=("pharmacy","pharmacystorage"),["shop=chemist"]=("pharmacy",null),["amenity=post_office"]=("post","poststorage"),["amenity=bank"]=("bank",null),
  ["amenity=library"]=("library",null),["amenity=cafe"]=("cafe","cafekitchen"),["amenity=restaurant"]=("restaurantdining","restaurantkitchen"),["amenity=fast_food"]=("burgerdining","burgerkitchen"),
  ["amenity=bar"]=("bar","barstorage"),["amenity=pub"]=("bar","barkitchen"),["amenity=fuel"]=("gasstore","gasstorage"),["amenity=police"]=("policeoffice","policestorage"),
  ["amenity=kindergarten"]=("daycare",null),["amenity=doctors"]=("medicaloffice",null),["amenity=clinic"]=("medicaloffice","medicalstorage"),["amenity=dentist"]=("dentist","dentiststorage"),
  ["amenity=community_centre"]=("hall",null),["amenity=townhall"]=("office",null)};

 /// <summary>Every room name the tag tables can assign.</summary>
 public static IEnumerable<string> MappedRoomNames=>WholeBuildingRooms.Values.Concat(SalesRooms.Values.SelectMany(v=>new[]{v.Room,v.BackRoom}.OfType<string>())).Distinct();
 /// <summary>Renames rooms of an imported building from its tags. Unknown room names are reported, never fatal.</summary>
 public static void ApplyRoomTags(Building b,IReadOnlyDictionary<string,string> tags,Action<string> problem){
  if(b.Design is not {} d)return;var before=d.Floors.Select(f=>string.Join("|",f.Rooms.Select(r=>$"{r.Key}:{r.Value}"))).ToArray();
  void FurnishChanged(){if(b.Modified)return;for(var z=0;z<d.Floors.Count;z++)if(before[z]!=string.Join("|",d.Floors[z].Rooms.Select(r=>$"{r.Key}:{r.Value}")))BuildingDesigner.Furnish(b,z,new Random(unchecked(d.Variant+z*1009)));}
  string? Valid(string? name){if(name is null)return null;if(BuildingDesigner.AllRoomNames.Contains(name))return name;problem($"room '{name}' is not a Build 42 room name");return null;}
  IEnumerable<string> Keys(){foreach(var k in new[]{"pz:kind","shop","amenity","building"})if(tags.TryGetValue(k,out var v))yield return k+"="+v;}
  var aboveGround=d.Floors.Where((_,i)=>i+d.MinLevel>=0).ToList();
  if(tags.TryGetValue("pz:room",out var explicitRoom)){if(Valid(explicitRoom) is {} room)RenameMain(aboveGround,room);FurnishChanged();return;}
  foreach(var key in Keys()){
   if(SalesRooms.TryGetValue(key,out var sales)){if(Valid(sales.Room) is {} room){var ground=d.Floors[-d.MinLevel];if(!RenameAll(ground,"conveniencestore",room))RenameMain([ground],room);if(Valid(sales.BackRoom) is {} back)RenameAll(ground,"storage",back);}FurnishChanged();return;}
   if(WholeBuildingRooms.TryGetValue(key,out var whole)){if(Valid(whole) is {} room)foreach(var f in aboveGround)foreach(var k in f.Rooms.Keys.Where(k=>k!="0").ToList())f.Rooms[k]=room;FurnishChanged();return;}
  }
 }
 static bool RenameAll(BuildingFloor f,string from,string to){var keys=f.Rooms.Where(r=>r.Value==from).Select(r=>r.Key).ToList();foreach(var k in keys)f.Rooms[k]=to;return keys.Count>0;}
 /// <summary>The largest non-hall room of each floor takes the name.</summary>
 static void RenameMain(IEnumerable<BuildingFloor> floors,string room){foreach(var f in floors){var main=f.Rooms.Keys.Where(k=>k!="0").OrderByDescending(k=>f.Cells.Count(c=>c==k[0])).FirstOrDefault();if(main is not null)f.Rooms[main]=room;}}
}
