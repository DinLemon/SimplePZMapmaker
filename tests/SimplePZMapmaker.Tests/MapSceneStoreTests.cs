using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;
public static class MapSceneStoreTests
{
 public static IEnumerable<(string Name,Action Run)> Cases()=>[
  ("native scene rejects oversized snapshots without discarding current view",Oversized),
  ("native scene idle expiry renews after reads and releases old snapshots",Expiry),
  ("native scene session and memory limits evict least recently used sources",Budget)
 ];
 sealed class Clock:TimeProvider{public DateTimeOffset Now=new(2026,10,13,12,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Now;public void Advance(TimeSpan duration)=>Now+=duration;}
 static NativeViewportScene Source(int size=256)=>NativeViewportScene.FromProject(MapGenerator.Generate(new(){Kind="blank",Size=size}));
 static string Open(MapSceneStore store,NativeViewportScene source)=>JsonSerializer.SerializeToElement(store.Open(source)).GetProperty("id").GetString()!;
 static void Read(MapSceneStore store,string id)=>store.Read(id,0,0,32,1,0,CancellationToken.None);
 static void Reject(Action action){try{action();throw new Exception("Expected viewport source rejection");}catch(ArgumentException){}}
 static void Oversized(){var store=new MapSceneStore(1024*1024);var current=Open(store,Source());Reject(()=>Open(store,Source(1024)));Read(store,current);}
 static void Expiry(){var clock=new Clock();var store=new MapSceneStore(time:clock);var id=Open(store,Source());clock.Advance(TimeSpan.FromMinutes(4));Read(store,id);clock.Advance(TimeSpan.FromMinutes(4));Read(store,id);clock.Advance(TimeSpan.FromMinutes(6));Reject(()=>Read(store,id));var replacement=Open(store,Source());Read(store,replacement);store.Close(replacement);Reject(()=>Read(store,replacement));}
 static void Budget(){var clock=new Clock();var store=new MapSceneStore(450000,clock);var ids=new List<string>();for(var i=0;i<4;i++){ids.Add(Open(store,Source()));clock.Advance(TimeSpan.FromSeconds(1));}Read(store,ids[0]);clock.Advance(TimeSpan.FromSeconds(1));var fifth=Open(store,Source());Reject(()=>Read(store,ids[1]));Read(store,ids[0]);Read(store,fifth);clock.Advance(TimeSpan.FromSeconds(1));var large=Open(store,Source(512));Read(store,large);Reject(()=>Read(store,ids[2]));}
}
