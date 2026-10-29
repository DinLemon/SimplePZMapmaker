// Runtime-only verification harness. Requires the user's installed game; no game classes are bundled.
import java.nio.file.*;
import java.lang.reflect.*;
import java.util.*;
import zombie.iso.*;
import zombie.ZomboidFileSystem;
import zombie.network.GameServer;

public class NativeGameProbe {
 public static void main(String[] args) throws Exception {
  if(IsoChunkMap.class.getField("BOTTOM_LEVEL").getInt(null)!=-32||IsoChunkMap.class.getField("TOP_LEVEL").getInt(null)!=31||IsoChunkMap.class.getField("LEVELS").getInt(null)!=64||IsoChunkMap.class.getField("GROUND_LEVEL").getInt(null)!=32)throw new AssertionError("Installed game signed-floor ABI differs from the supported Build 42 range");
  GameServer.server=true;
  ZomboidFileSystem.instance.setCacheDir(args[1]);
  zombie.core.random.RandStandard.INSTANCE.init();
  String[] definitions={"newtiledefinitions.tiles","tiledefinitions_erosion.tiles","jumbo_trees.tiles","jumbo_trees_big.tiles","tiledefinitions_b42chunkcaching.tiles"};
  int[] definitionNumbers={1,2,8,6,5};
  record Definition(Path path,int number) {}
  var allDefinitions=new ArrayList<Definition>();
  for(int i=0;i<definitions.length;i++){Path file=Path.of(args[2],"media",definitions[i]);if(Files.exists(file))allDefinitions.add(new Definition(file,definitionNumbers[i]));}
  int extra=Arrays.asList(args).indexOf("--tiledefs");
  if(extra>=0){if(extra+1>=args.length)throw new IllegalArgumentException("--tiledefs needs a manifest");var numbers=new HashSet<Integer>();for(var d:allDefinitions)numbers.add(d.number());
   var lines=Files.readAllLines(Path.of(args[extra+1]));if(lines.size()>16)throw new IllegalArgumentException("Too many external tile definitions");
   for(String line:lines){var pair=line.split("\\t",2);if(pair.length!=2)throw new IllegalArgumentException("Invalid external tile definition");int number=Integer.parseInt(pair[0]);Path path=Path.of(pair[1]);if(number<100||number>8190||!numbers.add(number)||!Files.isRegularFile(path)||Files.size(path)>32L*1024*1024)throw new IllegalArgumentException("Invalid external tile definition number or file");allDefinitions.add(new Definition(path,number));}
   System.out.println("External tile definitions loaded: "+lines.size());
  }
  var sprites=zombie.iso.sprite.IsoSpriteManager.instance;
  for(var definition:allDefinitions)IsoWorld.instance.LoadTileDefinitionsPropertyStrings(sprites,definition.path().toString(),definition.number());
  Method aliases=IsoWorld.class.getDeclaredMethod("GenerateTilePropertyLookupTables");aliases.setAccessible(true);aliases.invoke(IsoWorld.instance);
  for(var definition:allDefinitions)IsoWorld.instance.LoadTileDefinitions(sprites,definition.path().toString(),definition.number());
  Path folder=Path.of(args[0]);int cellCount=0,chunkCount=0,roomCount=0,upperRooms=0,basementRooms=0,stairTiles=0,basementStairs=0,minLoadedLevel=0,maxLoadedLevel=0,rampTiles=0,fuelPumpTiles=0;
  var rampIds=new HashSet<String>();var rampDirections=new HashSet<IsoDirections>();
  try(var files=Files.list(folder)){
   for(Path file:files.filter(p->p.toString().endsWith(".lotheader")).sorted().toList()){
    String name=file.getFileName().toString();String[] parts=name.replace(".lotheader","").split("_");int cx=Integer.parseInt(parts[0]),cy=Integer.parseInt(parts[1]);
    MapFiles map=new MapFiles("SimplePZProbe","",folder.toString(),0);LotHeader header=new LotHeader(cx,cy);header.fixed2x=true;header.fileName=name;header.absoluteFilePath=file.toString();header.mapFiles=map;
    map.infoHeaders.put(name,header);map.infoFileNames.put(name,file.toString());map.infoFileNames.put("world_"+cx+"_"+cy+".lotpack",folder.resolve("world_"+cx+"_"+cy+".lotpack").toString());
    IsoMetaGrid grid=IsoWorld.instance.metaGrid;grid.minX=cx;grid.maxX=cx;grid.minY=cy;grid.maxY=cy;
    for(String dimension:new String[]{"width","height"}){Field field=IsoMetaGrid.class.getDeclaredField(dimension);field.setAccessible(true);field.setInt(grid,1);}
    Field gridField=IsoMetaGrid.class.getDeclaredField("grid");gridField.setAccessible(true);gridField.set(grid,new IsoMetaCell[1][1]);
    Class<?> loaderClass=Class.forName("zombie.iso.IsoMetaGrid$MetaGridLoaderThread");Constructor<?> constructor=loaderClass.getDeclaredConstructor(IsoMetaGrid.class,int.class);constructor.setAccessible(true);Object loader=constructor.newInstance(grid,cy);
    Method load=loaderClass.getDeclaredMethod("loadCell",MapFiles.class,int.class,int.class);load.setAccessible(true);load.invoke(loader,map,cx,cy);
    Field roomField=loaderClass.getDeclaredField("roomList");roomField.setAccessible(true);List<?> rooms=(List<?>)roomField.get(loader);roomCount+=rooms.size();for(Object room:rooms){int level=((RoomDef)room).level;if(level>0)upperRooms++;if(level<0)basementRooms++;minLoadedLevel=Math.min(minLoadedLevel,level);maxLoadedLevel=Math.max(maxLoadedLevel,level);}
    if(header.width!=8||header.height!=8||header.minLevel< -32||header.maxLevel<1||header.maxLevel>31||header.tilesUsed.isEmpty())throw new AssertionError("Installed game rejected header "+name);
    for(String tile:header.tilesUsed)if(sprites.getSprite(tile)==null)throw new AssertionError("Game sprite absent: "+tile);
    for(int x=0;x<32;x++)for(int y=0;y<32;y++){
     IsoLot lot=IsoLot.get(map,cx,cy,cx*32+x,cy*32+y,null);if(lot.data.isEmpty())throw new AssertionError("Installed game rejected chunk "+x+","+y);
     for(int squareIndex=0;squareIndex<64*(header.maxLevel-header.minLevel+1);squareIndex++){int offset=lot.offsetInData[squareIndex];if(offset<0)continue;int count=lot.data.get(offset);for(int i=1;i<=count;i++){String tile=(String)header.tilesUsed.get(lot.data.get(offset+i));if(tile.equals("carpentry_02_96")||tile.equals("carpentry_02_97")||tile.equals("carpentry_02_98")||tile.equals("carpentry_02_88")||tile.equals("carpentry_02_89")||tile.equals("carpentry_02_90")){
      int level=header.minLevel+squareIndex/64;var sprite=sprites.getSprite(tile);if(sprite==null)throw new AssertionError("Game sprite absent: "+tile);var square=new IsoGridSquare(null,null,0,0,level);square.getObjects().add(new IsoObject(null,square,sprite));square.RecalcProperties();if(!square.HasStairs())throw new AssertionError("Game does not recognise stairs: "+tile);stairTiles++;if(level<0)basementStairs++;
     }
     var pumpSprite=sprites.getSprite(tile);var fuelProperty=zombie.core.properties.IsoPropertyType.FUEL_AMOUNT;
     if(pumpSprite.getProperties().has(fuelProperty)){
      if(fuelPumpTiles==0){ZomboidFileSystem.instance.init();zombie.Lua.LuaManager.init();}
      if(Integer.parseInt(pumpSprite.getProperties().get(fuelProperty))<=0)throw new AssertionError("Pump lacks native positive fuel capacity: "+tile);
      var square=new IsoGridSquare(null,null,0,0,header.minLevel+squareIndex/64);var pump=new IsoObject(null,square,pumpSprite);square.getObjects().add(pump);square.RecalcProperties();
      if(pump.getFacing()!=IsoDirections.E&&pump.getFacing()!=IsoDirections.S)throw new AssertionError("Unsupported native pump facing: "+tile);
      // In-memory controlled readback only. Exported maps do not prefill fuel or force power/sandbox.
      zombie.SandboxOptions.getInstance().fuelStationGasInfinite.setValue(false);
      pump.getModData().rawset(fuelProperty.getName(),Double.valueOf(137));if(pump.getPipedFuelAmount()!=137)throw new AssertionError("Native fuel getter ignores pump quantity: "+tile);
      pump.getModData().rawset(fuelProperty.getName(),Double.valueOf(0));if(pump.getPipedFuelAmount()!=0)throw new AssertionError("Native empty pump getter differs: "+tile);
      fuelPumpTiles++;
     }
     }}
     if(header.tilesUsed.stream().anyMatch(t->((String)t).startsWith("ramps_01_")))for(int squareIndex=0;squareIndex<64*(header.maxLevel-header.minLevel+1);squareIndex++){
      int offset=lot.offsetInData[squareIndex];if(offset<0)continue;int count=lot.data.get(offset);String ramp=null;
      for(int i=1;i<=count;i++){String tile=(String)header.tilesUsed.get(lot.data.get(offset+i));if(tile.startsWith("ramps_01_")){if(ramp!=null)throw new AssertionError("Two physical ramps on one native square");ramp=tile;}}
      if(ramp==null)continue;
      var square=new IsoGridSquare(null,null,0,0,header.minLevel+squareIndex/64);
      for(int i=1;i<=count;i++){String tile=(String)header.tilesUsed.get(lot.data.get(offset+i));square.getObjects().add(new IsoObject(null,square,sprites.getSprite(tile)));}
      square.RecalcProperties();if(square.getFloor()==null||!ramp.equals(square.getFloor().getSprite().getName()))throw new AssertionError("A flat floor masks native ramp "+ramp);
      int index=Integer.parseInt(ramp.substring("ramps_01_".length())),start;IsoDirections dir;
      if(index<20){start=0;dir=IsoDirections.N;}else if(index>=24&&index<44){start=24;dir=IsoDirections.S;}else if(index>=48&&index<68){start=48;dir=IsoDirections.W;}else if(index>=72&&index<92){start=72;dir=IsoDirections.E;}else throw new AssertionError("Padding tile used as a physical ramp: "+ramp);
      int step=index-start+1;float low=(step-1)/20.0f,high=step/20.0f;
      if(square.getSlopedSurfaceDirection()!=dir||Math.abs(square.getSlopedSurfaceHeightMin()-low)>0.00001f||Math.abs(square.getSlopedSurfaceHeightMax()-high)>0.00001f)throw new AssertionError("Native ramp properties differ: "+ramp);
      float lowEdge=switch(dir){case N->square.getSlopedSurfaceHeight(.5f,1);case S->square.getSlopedSurfaceHeight(.5f,0);case W->square.getSlopedSurfaceHeight(1,.5f);case E->square.getSlopedSurfaceHeight(0,.5f);default->-1;};
      float highEdge=switch(dir){case N->square.getSlopedSurfaceHeight(.5f,0);case S->square.getSlopedSurfaceHeight(.5f,1);case W->square.getSlopedSurfaceHeight(0,.5f);case E->square.getSlopedSurfaceHeight(1,.5f);default->-1;};
      if(Math.abs(lowEdge-low)>0.00001f||Math.abs(highEdge-high)>0.00001f)throw new AssertionError("Native ramp edge heights differ: "+ramp);
      rampTiles++;rampIds.add(ramp);rampDirections.add(dir);
     }
     IsoLot.put(lot);chunkCount++;
    }cellCount++;
   }
  }
  var positional=Arrays.asList(args).subList(0,extra<0?args.length:extra);
  boolean requireBasements=positional.size()>3&&args[3].equals("--basements");
  int expectedMin=requireBasements&&positional.size()>4?Integer.parseInt(args[4]):-16;
  if(expectedMin < -32 || expectedMin > -1)throw new AssertionError("Basement minimum outside supported range");
  if(requireBasements&&positional.size()>5&&!args[5].startsWith("--")&&maxLoadedLevel!=Integer.parseInt(args[5]))throw new AssertionError("Expected top room was not loaded by the installed reader");
  // no-stairs checks a map that has no upper floors: headers, chunks, rooms and every tile sprite are still loaded and checked.
  boolean requireStairs=!Arrays.asList(args).contains("no-stairs");
  if(requireStairs&&(stairTiles==0||(!requireBasements&&upperRooms==0))||(requireBasements&&(minLoadedLevel!=expectedMin||basementRooms==0||basementStairs==0)))throw new AssertionError("Expected signed rooms/stair sprites not loaded");
  if(Arrays.asList(args).contains("--ramps")&&(rampIds.size()!=80||rampDirections.size()!=4))throw new AssertionError("Expected all 80 physical ramp segments in four directions");
  if(rampTiles>0)System.out.printf("NATIVE_RAMP_PROBE tiles=%d directions=%d segmentIds=%d firstFloor=true edgeHeights=true clientTraversal=false status=passed%n",rampTiles,rampDirections.size(),rampIds.size());
  if(Arrays.asList(args).contains("--fuel-pumps")&&fuelPumpTiles==0)throw new AssertionError("Expected native fuel pump was not loaded");
  if(fuelPumpTiles>0)System.out.printf("NATIVE_FUEL_PROBE pumps=%d capacity=true facing=true controlledQuantityReadback=true clientRefuelling=false status=passed%n",fuelPumpTiles);
  System.out.printf("NATIVE_GAME_PROBE cells=%d chunks=%d rooms=%d upperRooms=%d basementRooms=%d minLevel=%d maxRoomLevel=%d stairTiles=%d basementStairs=%d status=passed%n",cellCount,chunkCount,roomCount,upperRooms,basementRooms,minLoadedLevel,maxLoadedLevel,stairTiles,basementStairs);
 }
}
