import java.lang.reflect.Method;
public final class StaticModelGameProbe {
 public static void main(String[] args) throws Exception {
  ClassLoader loader=StaticModelGameProbe.class.getClassLoader();
  Class<?> item=Class.forName("zombie.inventory.InventoryItem",false,loader);
  Class<?> square=Class.forName("zombie.iso.IsoGridSquare",false,loader);
  Class<?> world=Class.forName("zombie.iso.objects.IsoWorldInventoryObject",false,loader);
  check(item,"setWorldScale",float.class);check(item,"setWorldZRotation",float.class);
  check(item,"setWorldStaticModel",String.class);check(item,"getWorldItem");
  check(square,"AddWorldInventoryItem",item,float.class,float.class,float.class,boolean.class);
  check(square,"getModData");check(world,"setIgnoreRemoveSandbox",boolean.class);
  check(world,"transmitCompleteItemToClients");
  Class<?> thump=Class.forName("zombie.iso.objects.IsoThumpable",false,loader);
  Class<?> cell=Class.forName("zombie.iso.IsoCell",false,loader);
  Class<?> table=Class.forName("se.krka.kahlua.vm.KahluaTable",false,loader);
  thump.getConstructor(cell,square,String.class,boolean.class,table);
  check(thump,"setSpriteModelName",String.class);check(thump,"setBlockAllTheSquare",boolean.class);
  check(thump,"setCanPassThrough",boolean.class);check(thump,"setIsThumpable",boolean.class);
  check(thump,"setMaxHealth",int.class);check(thump,"setHealth",int.class);
  check(thump,"setIsDismantable",boolean.class);check(thump,"setIsContainer",boolean.class);
  check(thump,"getContainer");check(thump,"transmitCompleteItemToClients");
  check(Class.forName("zombie.inventory.ItemContainer",false,loader),"setCapacity",int.class);
  check(square,"AddSpecialObject",Class.forName("zombie.iso.IsoObject",false,loader));
  check(square,"RecalcAllWithNeighbours",boolean.class);
  // Exercise the installed collision/health implementation without loading a game world.
  Object obstacle=thump.getConstructor(cell).newInstance(new Object[]{null});
  Object target=square.getConstructor(cell,Class.forName("zombie.iso.SliceY",false,loader),int.class,int.class,int.class).newInstance(null,null,100,100,0);
  thump.getMethod("setSquare",square).invoke(obstacle,target);
  thump.getMethod("setCanPassThrough",boolean.class).invoke(obstacle,false);
  thump.getMethod("setBlockAllTheSquare",boolean.class).invoke(obstacle,true);
  thump.getMethod("setHealth",int.class).invoke(obstacle,350);
  if(!thump.getMethod("getHealth").invoke(obstacle).equals(350))throw new AssertionError("Native health differs.");
  Method collide=thump.getMethod("TestCollide",Class.forName("zombie.iso.IsoMovingObject",false,loader),square,square);
  for(int[] offset:new int[][]{{-1,0},{1,0},{0,-1},{0,1}}){Object from=square.getConstructor(cell,Class.forName("zombie.iso.SliceY",false,loader),int.class,int.class,int.class).newInstance(null,null,100+offset[0],100+offset[1],0);if(!collide.invoke(obstacle,null,from,target).equals(true))throw new AssertionError("Native solid cell did not block an approach.");}
  thump.getMethod("setBlockAllTheSquare",boolean.class).invoke(obstacle,false);
  thump.getMethod("setCanPassThrough",boolean.class).invoke(obstacle,true);
  Object west=square.getConstructor(cell,Class.forName("zombie.iso.SliceY",false,loader),int.class,int.class,int.class).newInstance(null,null,99,100,0);
  if(!collide.invoke(obstacle,null,west,target).equals(false))throw new AssertionError("Native scenery blocked movement.");
  System.out.println("Installed IsoThumpable.TestCollide blocks all four approaches; scenery allows passage; health persists in object.");
  Class<?> sprite=Class.forName("zombie.iso.sprite.IsoSprite",false,loader);
  thump.getMethod("setSprite",sprite).invoke(obstacle,sprite.getConstructor().newInstance());
  thump.getMethod("setIsContainer",boolean.class).invoke(obstacle,true);
  Object container=thump.getMethod("getContainer").invoke(obstacle);
  container.getClass().getMethod("setCapacity",int.class).invoke(container,75);
  if(!container.getClass().getMethod("getCapacity").invoke(container).equals(75))throw new AssertionError("Native container capacity differs.");
  System.out.println("Installed IsoThumpable.setIsContainer created a real ItemContainer with capacity75.");
  Class<?> parser=Class.forName("zombie.scripting.ScriptParser",false,loader);
  String script=args.length>1?java.nio.file.Files.readString(java.nio.file.Path.of(args[1])):"module SPZMProbe { item Prop { Type = Normal, DisplayName = Static scenery, Weight = 0.1, Icon = Question_On, WorldStaticModel = Base.BeerBottle, } spriteModel Scene { modelScript = Base.BeerBottle, translate = 0 0 0, rotate = 0 45 0, scale = 2, } }";
  Object parsed=parser.getMethod("parse",String.class).invoke(null,script);
  java.util.List<?> modules=(java.util.List<?>)parsed.getClass().getField("children").get(parsed);
  if(modules.size()!=1)throw new AssertionError("Proxy module was not parsed.");
  java.util.List<?> items=(java.util.List<?>)modules.get(0).getClass().getField("children").get(modules.get(0));
  if(items.isEmpty())throw new AssertionError("No proxy/model declaration was parsed.");
  for(Object block:items){Object type=block.getClass().getField("type").get(block);if(!type.equals("item")&&!type.equals("spriteModel"))throw new AssertionError("Unexpected placement declaration kind.");}
  System.out.println("ScriptParser.parse accepted original proxy/model declarations: "+items.size());
  Class<?> spriteModel=Class.forName("zombie.iso.SpriteModel",false,loader);
  java.util.regex.Matcher matcher=java.util.regex.Pattern.compile("spriteModel\\s+([A-Za-z0-9_]+)\\s*(\\{[^}]*\\})").matcher(script);
  int scenery=0;
  while(matcher.find()){Object value=spriteModel.getConstructor().newInstance();spriteModel.getMethod("Load",String.class,String.class).invoke(value,matcher.group(1),matcher.group());if(spriteModel.getMethod("getModelScriptName").invoke(value)==null)throw new AssertionError("SpriteModel lost registered model reference.");scenery++;}
  System.out.println("Installed SpriteModel.Load accepted native scenery declarations: "+scenery);
  if(args.length>0){Class<?> compiler=Class.forName("se.krka.kahlua.luaj.compiler.LuaCompiler",false,loader);String lua=java.nio.file.Files.readString(java.nio.file.Path.of(args[0]));Object closure=compiler.getMethod("loadstring",String.class,String.class,table).invoke(null,lua,"SimplePZMapmaker static placement",null);if(closure==null)throw new AssertionError("Lua compiler returned no closure.");System.out.println("Installed LuaCompiler accepted exported placement glue without executing it.");}
  System.out.println("PASS: installed game exposes world-item/native scenery, collision, health, containers and server replication APIs. This is API verification, not an in-game playthrough.");
 }
 private static void check(Class<?> type,String name,Class<?>...args)throws Exception{Method method=type.getMethod(name,args);System.out.println(type.getSimpleName()+"."+method.getName());}
}
