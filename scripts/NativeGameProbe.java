// Runtime-only verification harness. Requires the user's installed game; no game classes are bundled.
import java.nio.file.*;
import java.lang.reflect.*;
import java.util.*;
import zombie.iso.*;
import zombie.ZomboidFileSystem;
import zombie.network.GameServer;

public class NativeGameProbe {
 public static void main(String[] args) throws Exception {
  GameServer.server=true;
  ZomboidFileSystem.instance.setCacheDir(args[1]);
  zombie.core.random.RandStandard.INSTANCE.init();
  String[] definitions={"newtiledefinitions.tiles","tiledefinitions_erosion.tiles","jumbo_trees.tiles","jumbo_trees_big.tiles"};
  var sprites=zombie.iso.sprite.IsoSpriteManager.instance;
  for(int i=0;i<definitions.length;i++){Path file=Path.of(args[2],"media",definitions[i]);if(Files.exists(file))IsoWorld.instance.LoadTileDefinitionsPropertyStrings(sprites,file.toString(),i);}
  Method aliases=IsoWorld.class.getDeclaredMethod("GenerateTilePropertyLookupTables");aliases.setAccessible(true);aliases.invoke(IsoWorld.instance);
  for(int i=0;i<definitions.length;i++){Path file=Path.of(args[2],"media",definitions[i]);if(Files.exists(file))IsoWorld.instance.LoadTileDefinitions(sprites,file.toString(),i);}
  Path folder=Path.of(args[0]);int cellCount=0,chunkCount=0,roomCount=0,upperRooms=0,basementRooms=0,stairTiles=0,basementStairs=0,minLoadedLevel=0;
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
    Field roomField=loaderClass.getDeclaredField("roomList");roomField.setAccessible(true);List<?> rooms=(List<?>)roomField.get(loader);roomCount+=rooms.size();for(Object room:rooms){int level=((RoomDef)room).level;if(level>0)upperRooms++;if(level<0)basementRooms++;minLoadedLevel=Math.min(minLoadedLevel,level);}
    if(header.width!=8||header.height!=8||header.maxLevel<1||header.tilesUsed.isEmpty())throw new AssertionError("Installed game rejected header "+name);
    for(String tile:header.tilesUsed)if(sprites.getSprite(tile)==null)throw new AssertionError("Game sprite absent: "+tile);
    for(int x=0;x<32;x++)for(int y=0;y<32;y++){
     IsoLot lot=IsoLot.get(map,cx,cy,cx*32+x,cy*32+y,null);if(lot.data.isEmpty())throw new AssertionError("Installed game rejected chunk "+x+","+y);
     for(int squareIndex=0;squareIndex<64*(header.maxLevel-header.minLevel+1);squareIndex++){int offset=lot.offsetInData[squareIndex];if(offset<0)continue;int count=lot.data.get(offset);for(int i=1;i<=count;i++){String tile=(String)header.tilesUsed.get(lot.data.get(offset+i));if(tile.equals("carpentry_02_96")||tile.equals("carpentry_02_97")||tile.equals("carpentry_02_98")||tile.equals("carpentry_02_88")||tile.equals("carpentry_02_89")||tile.equals("carpentry_02_90")){
      int level=header.minLevel+squareIndex/64;var sprite=sprites.getSprite(tile);if(sprite==null)throw new AssertionError("Game sprite absent: "+tile);var square=new IsoGridSquare(null,null,0,0,level);square.getObjects().add(new IsoObject(null,square,sprite));square.RecalcProperties();if(!square.HasStairs())throw new AssertionError("Game does not recognise stairs: "+tile);stairTiles++;if(level<0)basementStairs++;
     }}}
     IsoLot.put(lot);chunkCount++;
    }cellCount++;
   }
  }
  boolean requireBasements=args.length>3&&args[3].equals("--basements");
  if(stairTiles==0||(!requireBasements&&upperRooms==0)||(requireBasements&&(minLoadedLevel!=-16||basementRooms==0||basementStairs==0)))throw new AssertionError("Expected signed rooms/stair sprites not loaded");
  System.out.printf("NATIVE_GAME_PROBE cells=%d chunks=%d rooms=%d upperRooms=%d basementRooms=%d minLevel=%d stairTiles=%d basementStairs=%d status=passed%n",cellCount,chunkCount,roomCount,upperRooms,basementRooms,minLoadedLevel,stairTiles,basementStairs);
 }
}
