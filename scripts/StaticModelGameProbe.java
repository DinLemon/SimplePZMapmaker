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
  Class<?> parser=Class.forName("zombie.scripting.ScriptParser",false,loader);
  String script=args.length>1?java.nio.file.Files.readString(java.nio.file.Path.of(args[1])):"module SPZMProbe { item Prop { Type = Normal, DisplayName = Static scenery, Weight = 0.1, Icon = Question_On, WorldStaticModel = Base.BeerBottle, } }";
  Object parsed=parser.getMethod("parse",String.class).invoke(null,script);
  java.util.List<?> modules=(java.util.List<?>)parsed.getClass().getField("children").get(parsed);
  if(modules.size()!=1)throw new AssertionError("Proxy module was not parsed.");
  java.util.List<?> items=(java.util.List<?>)modules.get(0).getClass().getField("children").get(modules.get(0));
  if(items.size()!=1)throw new AssertionError("Proxy item was not parsed.");
  if(!items.get(0).getClass().getField("type").get(items.get(0)).equals("item"))throw new AssertionError("Proxy declaration kind differs.");
  System.out.println("ScriptParser.parse accepted original proxy item declaration.");
  if(args.length>0){Class<?> compiler=Class.forName("se.krka.kahlua.luaj.compiler.LuaCompiler",false,loader);Class<?> table=Class.forName("se.krka.kahlua.vm.KahluaTable",false,loader);String lua=java.nio.file.Files.readString(java.nio.file.Path.of(args[0]));Object closure=compiler.getMethod("loadstring",String.class,String.class,table).invoke(null,lua,"SimplePZMapmaker static placement",null);if(closure==null)throw new AssertionError("Lua compiler returned no closure.");System.out.println("Installed LuaCompiler accepted exported placement glue without executing it.");}
  System.out.println("PASS: installed game exposes static world-item model, transform, persistent square data and server replication APIs. This is API verification, not an in-game playthrough.");
 }
 private static void check(Class<?> type,String name,Class<?>...args)throws Exception{Method method=type.getMethod(name,args);System.out.println(type.getSimpleName()+"."+method.getName());}
}
