using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
namespace SimplePZMapmaker.Web.Services;

/// <summary>Short-lived viewport snapshots. Full map data is submitted once, then only visible chunks travel.</summary>
public sealed class MapSceneStore:IDisposable
{
 private sealed record Entry(NativeViewportScene Scene,DateTimeOffset LastRead){public DateTimeOffset LastRead {get;set;}=LastRead;}
 private readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
 private readonly long budget;
 private readonly TimeProvider clock;
 private readonly ITimer expiry;
 public MapSceneStore(long byteBudget=192L*1024*1024,TimeProvider? time=null){if(byteBudget<1)throw new ArgumentException("The native viewport budget must be positive.");budget=byteBudget;clock=time??TimeProvider.System;expiry=clock.CreateTimer(_=>{lock(entries)Expire();},null,TimeSpan.FromMinutes(1),TimeSpan.FromMinutes(1));}
 public object Open(NativeViewportScene scene){if(scene.EstimatedBytes>budget)throw new ArgumentException("The map exceeds the native viewport snapshot budget. Open a smaller section.");lock(entries){Expire();while(entries.Count>=4||entries.Count>0&&entries.Values.Sum(e=>e.Scene.EstimatedBytes)+scene.EstimatedBytes>budget)entries.Remove(entries.MinBy(e=>e.Value.LastRead).Key);var id=Guid.NewGuid().ToString("N");entries[id]=new(scene,clock.GetUtcNow());return new{id,scene.Width,scene.Height,expiresInSeconds=300};}}
 public NativeViewportScene.Chunk Read(string id,int x,int y,int size,int stride,int? level,CancellationToken cancellation){NativeViewportScene scene;lock(entries){Expire();if(!entries.TryGetValue(id,out var entry))throw new ArgumentException("The native viewport snapshot expired. Refresh the view.");entry.LastRead=clock.GetUtcNow();scene=entry.Scene;}return scene.Read(x,y,size,stride,level,cancellation);}
 public void Close(string id){lock(entries)entries.Remove(id);}
 public void Dispose(){expiry.Dispose();lock(entries)entries.Clear();}
 private void Expire(){foreach(var id in entries.Where(e=>clock.GetUtcNow()-e.Value.LastRead>TimeSpan.FromMinutes(5)).Select(e=>e.Key).ToArray())entries.Remove(id);}
}
public static class MapSceneEndpoints
{
 public sealed record OpenRequest(MapProject? Project,WorldProject? World,string? MemberId);
 public static void MapMapSceneEndpoints(this WebApplication app){
  app.MapPost("/api/map-scene",(OpenRequest request,MapSceneStore store,GameAssetCatalog assets,StaticModelCatalog models,McpPlanningTools tools,CancellationToken cancellation)=>{
   cancellation.ThrowIfCancellationRequested();if((request.Project is null)==(request.World is null))throw new ArgumentException("Provide either a map or a world section for the native view.");
   NativeViewportScene scene;if(request.World is {} world){if(string.IsNullOrEmpty(request.MemberId))throw new ArgumentException("Select a world section.");tools.ValidateWorld(world);scene=NativeViewportScene.FromWorld(world,request.MemberId);}else{var project=request.Project!;AssetRules.Project(project,assets);models.Validate(project);scene=NativeViewportScene.FromProject(project);}cancellation.ThrowIfCancellationRequested();return store.Open(scene);
  });
  app.MapGet("/api/map-scene/{id}",(string id,int x,int y,int size,int stride,int? level,MapSceneStore store,CancellationToken cancellation)=>store.Read(id,x,y,size,stride,level,cancellation));
  app.MapPost("/api/map-scene/{id}/close",(string id,MapSceneStore store)=>{store.Close(id);return new{closed=true};});
 }
}
