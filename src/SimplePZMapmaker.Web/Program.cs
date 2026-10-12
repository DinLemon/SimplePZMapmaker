using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;
using System.Text.Json;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:48765");
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=64_000_000);
builder.Services.ConfigureHttpJsonOptions(o=>{o.SerializerOptions.MaxDepth=48;});
var workspace=Environment.GetEnvironmentVariable("PZMAP_WORKSPACE") ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath,"../.."));
builder.Services.AddSingleton(new OsmClient(Path.Combine(workspace,"work","geodata-cache")));
builder.Services.AddSingleton(new RecoveryStore(Path.Combine(workspace,"work","recovery")));
var app=builder.Build();
app.Use(async(context,next)=> {
    var host=context.Request.Host.Host;
    if(host is not ("127.0.0.1" or "localhost")){context.Response.StatusCode=403;return;}
    if(context.Request.Method=="POST"&&(!context.Request.HasJsonContentType() || context.Request.Headers.TryGetValue("Origin",out var origin)&&origin.ToString()!=$"http://{context.Request.Host}")) {context.Response.StatusCode=403;await context.Response.WriteAsJsonAsync(new{error="Запрос должен поступать из локального редактора."});return;}
    context.Response.Headers["X-Content-Type-Options"]="nosniff";
    context.Response.Headers["Referrer-Policy"]="no-referrer";
    context.Response.Headers["Content-Security-Policy"]="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; connect-src 'self'; frame-src https://www.openstreetmap.org; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
    try{await next();}
    catch(Exception e) when(e is ArgumentException or JsonException or InvalidOperationException or BadHttpRequestException or KeyNotFoundException or FormatException) {
        if(context.Response.HasStarted)throw;
        context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{error=e is ArgumentException?e.Message:"Не удалось прочитать данные. Проверьте проект или файл геоданных."});
    }
    catch(Exception e) when(e is HttpRequestException or OperationCanceledException) {
        if(context.RequestAborted.IsCancellationRequested)return;
        context.Response.StatusCode=502;await context.Response.WriteAsJsonAsync(new{error=e is HttpRequestException?e.Message:"Сервис не ответил вовремя. Уменьшите участок или повторите позже."});
    }
});
app.UseDefaultFiles();app.UseStaticFiles();
app.MapGet("/api/environment",()=>new {version="0.3.1",target="Project Zomboid Build 42",gameFound=Directory.Exists(@"./game-installation\media"),gamePath=@"./game-installation",cellSize=256,chunkSize=8,workspace});
app.MapGet("/api/examples/pilna",async(CancellationToken token)=>{
    var file=Path.Combine(workspace,"examples","Pilna","pilna.pzmap.json");
    if(!File.Exists(file))file=Path.Combine(AppContext.BaseDirectory,"examples","Pilna","pilna.pzmap.json");
    if(!File.Exists(file))return Results.NotFound(new{error="Пример Пильны отсутствует. Пересоберите приложение с папкой examples/Pilna."});
    if(new FileInfo(file).Length>48_000_000)throw new ArgumentException("Пример карты слишком большой.");
    var p=JsonSerializer.Deserialize<MapProject>(await File.ReadAllTextAsync(file,token),NativeMapExporter.JsonOptions)??throw new ArgumentException("Пример карты повреждён.");
    ProjectValidator.Validate(p);return Results.Ok(p);
});
app.MapPost("/api/generate",(GenerationOptions options)=>MapGenerator.Generate(options));
app.MapPost("/api/import/geojson",(GeoImportRequest request,CancellationToken token)=>GeographicImporter.Import(request.Json,request.Project,request.Bounds,token,request.PreserveEdits));
app.MapPost("/api/import/osm",async(OsmImportRequest request,OsmClient client,CancellationToken token)=>{ProjectValidator.Validate(request.Project);return GeographicImporter.Import(await client.Fetch(request.Bounds,token),request.Project,request.Bounds,token,request.PreserveEdits);});
app.MapPost("/api/building/design",(BuildingDesignRequest request)=>{var b=request.Building;if(b is null)throw new ArgumentException("Не задано здание.");b.Design=BuildingDesigner.Generate(b,request.Levels,request.Seed,request.Shape,request.Basements);return b;});
app.MapPost("/api/building/validate",(Building b)=>{BuildingDesigner.Validate(b);return Results.Ok(new{valid=true});});
app.MapPost("/api/buildings/regenerate",(BuildingRegenerateRequest request)=>{var changed=BuildingDesigner.Regenerate(request.Project,request.Id);return Results.Ok(new{project=request.Project,changed,skipped=request.Project.Buildings.Count-changed});});
app.MapPost("/api/search",async(SearchRequest request,OsmClient client,CancellationToken token)=>await client.Search(request.Query,token));
app.MapPost("/api/export",(MapProject project)=>Results.File(NativeMapExporter.Export(project),"application/zip","SimplePZMap.zip"));
app.MapPost("/api/validate",(MapProject project)=>{ProjectValidator.Validate(project);return Results.Ok(new{valid=true});});
app.MapGet("/api/recovery/{id}",async(string id,RecoveryStore store,CancellationToken token)=>{var project=await store.LoadAsync(id,token);return project is null?Results.NotFound():Results.Ok(project);});
app.MapPost("/api/recovery/{id}",async(string id,MapProject project,RecoveryStore store,CancellationToken token)=>{await store.SaveAsync(id,project,token);return Results.Ok(new{saved=true});});
app.Run();
public sealed record GeoImportRequest(MapProject Project,string Json,GeoBounds? Bounds,bool PreserveEdits=false);
public sealed record OsmImportRequest(MapProject Project,GeoBounds Bounds,bool PreserveEdits=false);
public sealed record SearchRequest(string Query);
public sealed record BuildingDesignRequest(Building Building,int Levels=1,int Seed=42,string Shape="rect",int Basements=0);
public sealed record BuildingRegenerateRequest(MapProject Project,string? Id=null);
