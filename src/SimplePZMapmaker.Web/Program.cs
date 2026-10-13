using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;
using System.Text.Json;
using ModelContextProtocol.AspNetCore;

var builder=WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore",LogLevel.Warning);
var listenUrl=Environment.GetEnvironmentVariable("PZMAP_LISTEN_URL")??"http://127.0.0.1:48765";
if(!Uri.TryCreate(listenUrl,UriKind.Absolute,out var listen)||listen.Scheme!="http"||listen.Host!="127.0.0.1"||listen.AbsolutePath!="/")throw new ArgumentException("The editor only listens on the local address 127.0.0.1.");
builder.WebHost.UseUrls(listenUrl);
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=128*1024*1024);
builder.Services.ConfigureHttpJsonOptions(o=>{o.SerializerOptions.MaxDepth=48;});
var workspace=Environment.GetEnvironmentVariable("PZMAP_WORKSPACE") ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath,"../.."));
builder.Services.AddSingleton(new OsmClient(Path.Combine(workspace,"work","geodata-cache")));
builder.Services.AddSingleton(new RecoveryStore(Path.Combine(workspace,"work","recovery")));
builder.Services.AddSingleton(new GameAssetCatalog(workspace,Environment.GetEnvironmentVariable("PZMAP_GAME_PATH")??""));
builder.Services.AddSingleton<StaticModelCatalog>();
builder.Services.AddSingleton(new AgentProjectStore(Path.Combine(workspace,"work","agent-sessions")));
builder.Services.AddSingleton(new AgentWorldStore(Path.Combine(workspace,"work","agent-world-sessions")));
builder.Services.AddSingleton(new AgentPlanningStore(Path.Combine(workspace,"work","agent-plans")));
builder.Services.AddSingleton<McpPlanningTools>();
builder.Services.AddSingleton(new WorldStore(Path.Combine(workspace,"work","world-recovery")));
builder.Services.AddSingleton(new TerrainElevationClient(Path.Combine(workspace,"work","elevation-cache")));
builder.Services.AddSingleton(service=>new OsmWorldImportService(service.GetRequiredService<OsmClient>(),service.GetRequiredService<GameAssetCatalog>(),Path.Combine(workspace,"work","osm-jobs")));
builder.Services.AddMcpServer().WithHttpTransport(o=>o.SessionMode=HttpServerSessionMode.Stateless).WithTools<McpBuildingTools>().WithTools<McpPlanningTools>();
var app=builder.Build();
app.Use(async(context,next)=> {
    var host=context.Request.Host.Host;
    if(host is not ("127.0.0.1" or "localhost")){context.Response.StatusCode=403;return;}
    if(context.Request.Method=="POST"&&(!context.Request.HasJsonContentType() || context.Request.Headers.TryGetValue("Origin",out var origin)&&origin.ToString()!=$"http://{context.Request.Host}")) {context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new{error="The request must come from the local editor."});return;}
    context.Response.Headers["X-Content-Type-Options"]="nosniff";
    context.Response.Headers["Referrer-Policy"]="no-referrer";
    context.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; connect-src 'self'; frame-src https://www.openstreetmap.org; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
    try{await next();}
    catch(AgentRevisionConflict e){if(context.Response.HasStarted)throw;context.Response.StatusCode=409;await context.Response.WriteAsJsonAsync(new{error=e.Message,revision=e.Current});}
    catch(Exception e) when(e is ArgumentException or JsonException or InvalidOperationException or BadHttpRequestException or KeyNotFoundException or FormatException) {
        if(context.Response.HasStarted)throw;
        context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{error=e is ArgumentException?e.Message:"Could not read the data. Check the project or geographic data file."});
    }
    catch(Exception e) when(e is HttpRequestException or OperationCanceledException) {
        if(context.RequestAborted.IsCancellationRequested)return;
        context.Response.StatusCode=502;await context.Response.WriteAsJsonAsync(new{error=e is HttpRequestException?e.Message:"The service did not respond in time. Reduce the area or retry later."});
    }
});
app.UseDefaultFiles();app.UseStaticFiles();
app.MapAgentEndpoints();app.MapAgentPlanningEndpoints();app.MapRecoveryVersionEndpoints();app.MapAssetsModEndpoints();app.MapStaticModelEndpoints();app.MapWorldEndpoints();app.MapOsmWorldImportEndpoints();
app.MapGet("/api/assets/status",(GameAssetCatalog assets)=>assets.Status());
app.MapGet("/api/assets/search",(GameAssetCatalog assets,string? q,string? category,string? source,int? offset,int? limit)=>assets.Search(q,category,offset??0,limit??72,source));
app.MapGet("/api/assets/item/{id}",(GameAssetCatalog assets,string id)=>{var a=assets.Get(id);return new{a.Id,a.Category,a.Name,a.Group,a.Facing,a.Grid,a.Blocking,a.Floor,a.Page,a.Sprite,a.Properties,a.Source,a.ModId,a.Requirements};});
app.MapGet("/api/assets/placement/{id}",(GameAssetCatalog assets,string id)=>assets.PlacementFor(id));
app.MapGet("/api/assets/pair/{id}",(GameAssetCatalog assets,string id,string role)=>assets.Pair(id,role));
app.MapGet("/api/assets/page/{id}",(GameAssetCatalog assets,string id)=>Results.Bytes(assets.Page(id),"image/png"));
app.MapPost("/api/assets/describe",(GameAssetCatalog assets,string[] ids)=>assets.Describe(ids));
app.MapPost("/api/assets/refresh",(GameAssetCatalog assets,AssetSourceRequest source)=>{assets.Refresh(source.GamePath);return assets.Status();});
app.MapGet("/api/styles",()=>new{buildings=VisualStyles.All,surfaces=EnvironmentStyles.Names});
app.MapPost("/api/preview/building",(GameAssetCatalog assets,StaticModelCatalog models,BuildingPreviewRequest request)=>{AssetRules.Building(request.Building,assets);ModelRules.Building(request.Building,models);return NativeScene.Building(request.Building,request.Level);});
app.MapPost("/api/preview/building/draft",(GameAssetCatalog assets,StaticModelCatalog models,BuildingPreviewRequest request)=>{AssetRules.Building(request.Building,assets,false);ModelRules.Building(request.Building,models);return NativeScene.DraftBuilding(request.Building,request.Level);});
app.MapGet("/api/building/presets",()=>HousingTemplates.Catalog);
app.MapPost("/api/building/presets/{id}",(string id,GameAssetCatalog assets)=>{var b=HousingTemplates.Create(id);AssetRules.Building(b,assets);return b;});
app.MapPost("/api/preview/cell",(GameAssetCatalog assets,StaticModelCatalog models,CellPreviewRequest request)=>{if(request.Project.Spawns.Count==0&&!request.Project.WorldSection)request.Project.Spawns.Add(MapGenerator.FindSafeSpawn(request.Project));AssetRules.Project(request.Project,assets);models.Validate(request.Project);return NativeScene.Cell(request.Project,request.X,request.Y,request.Width,request.Height,request.Level,request.X,request.Y,request.CellX,request.CellY);});
app.MapPost("/api/preview/appearance",(GameAssetCatalog assets,AppearancePreviewRequest request)=>{AppearanceDesigner.Apply(request.Project,request.SurfaceTheme,request.Region,request.StyleBuildings);AssetRules.Project(request.Project,assets);return NativeScene.Cell(request.Project,request.X,request.Y,48,48,request.Level,request.X,request.Y,request.CellX,request.CellY);});
app.MapPost("/api/styles/apply",(GameAssetCatalog assets,StyleApplyRequest request)=>{if(!assets.Ready)throw new ArgumentException("To apply appearance styles, locate the game installation under Game resources.");var changed=AppearanceDesigner.Apply(request.Project,request.SurfaceTheme,request.Region,request.StyleBuildings);AssetRules.Project(request.Project,assets);return new{project=request.Project,changed};});
app.MapGet("/api/environment",(GameAssetCatalog assets)=>new {version="0.7.2",runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,selfContained=File.Exists(Path.Combine(AppContext.BaseDirectory,"coreclr.dll")),target="Project Zomboid Build 42",gameFound=assets.Ready,gamePath=assets.GamePath,cellSize=256,chunkSize=8,workspace});
app.MapGet("/api/examples/pilna",async(CancellationToken token)=>{
    var file=Path.Combine(workspace,"examples","Pilna","pilna.pzmap.json");
    if(!File.Exists(file))file=Path.Combine(AppContext.BaseDirectory,"examples","Pilna","pilna.pzmap.json");
    if(!File.Exists(file))return Results.NotFound(new{error="The Pilna example is missing. Rebuild the application with the examples/Pilna folder."});
    if(new FileInfo(file).Length>48_000_000)throw new ArgumentException("The example map is too large.");
    var p=JsonSerializer.Deserialize<MapProject>(await File.ReadAllTextAsync(file,token),NativeMapExporter.JsonOptions)??throw new ArgumentException("Invalid example map.");
    ProjectValidator.Validate(p);return Results.Ok(p);
});
app.MapPost("/api/generate",(GameAssetCatalog assets,GenerationOptions options)=>{if(!assets.Ready)options.SurfaceTheme="legacy";return MapGenerator.Generate(options);});
app.MapPost("/api/import/geojson",(GeoImportRequest request,CancellationToken token)=>GeographicImporter.Import(request.Json,request.Project,request.Bounds,token,request.PreserveEdits));
app.MapPost("/api/import/osm",async(OsmImportRequest request,OsmClient client,CancellationToken token)=>{ProjectValidator.Validate(request.Project);var download=request.UseRegionalExtract?await client.FetchExtractDetailed(request.Bounds,token):await client.FetchDetailed(request.Bounds,token);var result=GeographicImporter.Import(download.Data,request.Project,request.Bounds,token,request.PreserveEdits);result.Warnings.AddRange(download.Warnings);if(result.Project.ImportSummary is {} summary){summary.Notes.InsertRange(0,download.Warnings);if(summary.Notes.Count>20)summary.Notes.RemoveRange(20,summary.Notes.Count-20);}return result;});
app.MapPost("/api/building/design",(GameAssetCatalog assets,BuildingDesignRequest request)=>{var b=request.Building;if(b is null)throw new ArgumentException("No building specified.");b.Design=BuildingDesigner.Generate(b,request.Levels,request.Seed,request.Shape,request.Basements);VisualStyles.Apply(b,request.Style=="auto"&&!assets.Ready?"":request.Style);AssetRules.Building(b,assets);return b;});
app.MapPost("/api/building/validate",(GameAssetCatalog assets,StaticModelCatalog models,Building b)=>{AssetRules.Building(b,assets);foreach(var m in b.Design?.Floors.SelectMany(f=>f.StaticModels)??[])models.Validate(m);return Results.Ok(new{valid=true,building=b});});
app.MapPost("/api/buildings/regenerate",(BuildingRegenerateRequest request)=>{var changed=BuildingDesigner.Regenerate(request.Project,request.Id);return Results.Ok(new{project=request.Project,changed,skipped=request.Project.Buildings.Count-changed});});
app.MapPost("/api/search",async(SearchRequest request,OsmClient client,CancellationToken token)=>await client.Search(request.Query,token));
app.MapPost("/api/export",(GameAssetCatalog assets,StaticModelCatalog models,MapProject project,CancellationToken token)=>{AssetRules.Project(project,assets);models.Validate(project);return Results.File(NativeMapExporter.Export(project,false,token),"application/zip","SimplePZMap.zip");});
app.MapPost("/api/validate",(GameAssetCatalog assets,StaticModelCatalog models,MapProject project)=>{AssetRules.Project(project,assets);models.Validate(project);return Results.Ok(new{valid=true,project});});
app.MapGet("/api/recovery/{id}",async(string id,RecoveryStore store,CancellationToken token)=>{var project=await store.LoadAsync(id,token);return project is null?Results.NotFound():Results.Ok(project);});
app.MapPost("/api/recovery/{id}",async(string id,MapProject project,RecoveryStore store,GameAssetCatalog assets,StaticModelCatalog models,CancellationToken token)=>{AssetRules.Project(project,assets);models.Validate(project);await store.SaveAsync(id,project,token);return Results.Ok(new{saved=true});});
app.Run();
public sealed record GeoImportRequest(MapProject Project,string Json,GeoBounds? Bounds,bool PreserveEdits=false);
public sealed record OsmImportRequest(MapProject Project,GeoBounds Bounds,bool PreserveEdits=false,bool UseRegionalExtract=false);
public sealed record SearchRequest(string Query);
public sealed record BuildingDesignRequest(Building Building,int Levels=1,int Seed=42,string Shape="rect",int Basements=0,string Style="auto");
public sealed record BuildingRegenerateRequest(MapProject Project,string? Id=null);
public sealed record AssetSourceRequest(string GamePath);
public sealed record BuildingPreviewRequest(Building Building,int? Level=null);
public sealed record CellPreviewRequest(MapProject Project,int X,int Y,int Width=48,int Height=48,int? Level=0,int CellX=0,int CellY=0);
public sealed record StyleApplyRequest(MapProject Project,string SurfaceTheme="temperate",SurfaceRegion? Region=null,bool StyleBuildings=true);

public sealed record AppearancePreviewRequest(MapProject Project,string SurfaceTheme,int CellX,int CellY,int X,int Y,SurfaceRegion? Region=null,bool StyleBuildings=true,int? Level=0);
