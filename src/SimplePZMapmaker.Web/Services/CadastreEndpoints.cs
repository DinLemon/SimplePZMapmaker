using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

public sealed record CadastreCombineRequest(string BaselineJson,string SupplementJson);
public sealed record CadastreNormalizeRequest(string GeoJson,GeographicSurveyContext Source);
public static class CadastreEndpoints
{
 static readonly JsonSerializerOptions PublicJson=new(JsonSerializerDefaults.Web){Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
 /// <summary>Register a singleton CadastreClient before mapping. No project mutation or arbitrary server file paths.</summary>
 public static IEndpointRouteBuilder MapCadastreEndpoints(this IEndpointRouteBuilder app)
 {
  app.MapGet("/api/geographic/sources",()=>Results.Json(GeographicSurveySources.All,PublicJson));
  app.MapPost("/api/cadastre/plan",async(HttpContext context,CadastreClient client)=>{
   try{return Results.Ok(client.Plan(await ReadAsync<CadastreRequest>(context,32*1024)));}
   catch(ArgumentException e){return Results.BadRequest(new{code="invalid-plan",message=e.Message});}
  });
  app.MapPost("/api/cadastre/fetch",async(HttpContext context,CadastreClient client)=>{
   try{var request=await ReadAsync<CadastreRequest>(context,32*1024);return Results.Ok(await client.FetchAsync(request,context.RequestAborted));}
   catch(ArgumentException e){return Results.BadRequest(new{code="invalid-request",message=e.Message});}
   catch(CadastreException e){return Results.Json(new{code=e.Code,message=e.Message,progress=e.Progress},statusCode:e.Code=="timeout"?504:502);}
  });
  app.MapPost("/api/cadastre/combine",async(HttpContext context)=>{
   try{var request=await ReadAsync<CadastreCombineRequest>(context,40*1024*1024);return Results.Ok(GeographicImporter.CombineSources([request.BaselineJson,request.SupplementJson]));}
   catch(ArgumentException e){return Results.BadRequest(new{code="invalid-supplement",message=e.Message});}
  });
  app.MapPost("/api/cadastre/normalize",async(HttpContext context)=>{
   try{var request=await ReadAsync<CadastreNormalizeRequest>(context,20*1024*1024);return Results.Ok(GeographicSurvey.Normalize(request.GeoJson,request.Source,context.RequestAborted));}
   catch(ArgumentException){return Results.BadRequest(new{code="invalid-survey",message="Invalid attributed GeoJSON. Supply source attribution, matching input CRS and valid closed polygon rings within documented budgets."});}
  });
  app.MapPost("/api/cadastre/fetch/stream",StreamAsync);
  return app;
 }
 sealed class InlineProgress(Action<CadastreProgress> report):IProgress<CadastreProgress>{public void Report(CadastreProgress value)=>report(value);}
 static async Task StreamAsync(HttpContext context,CadastreClient client)
 {
  CadastreRequest request;
  try{request=await ReadAsync<CadastreRequest>(context,32*1024);client.Plan(request);}
  catch(ArgumentException e){context.Response.StatusCode=400;await context.Response.WriteAsJsonAsync(new{code="invalid-plan",message=e.Message},context.RequestAborted);return;}
  context.Response.ContentType="application/x-ndjson";context.Response.Headers.CacheControl="no-store";
  var channel=System.Threading.Channels.Channel.CreateBounded<object>(new System.Threading.Channels.BoundedChannelOptions(32){FullMode=System.Threading.Channels.BoundedChannelFullMode.DropOldest,SingleReader=true,SingleWriter=true});
  async Task Produce(){try{
   var result=await client.FetchAsync(request,context.RequestAborted,new InlineProgress(p=>channel.Writer.TryWrite(new{kind="progress",progress=p})));
   channel.Writer.TryWrite(new{kind="result",result});
  }catch(CadastreException e){channel.Writer.TryWrite(new{kind="error",code=e.Code,message=e.Message,progress=e.Progress});}
   catch(OperationCanceledException) when(context.RequestAborted.IsCancellationRequested){}
   finally{channel.Writer.TryComplete();}}
  var producer=Produce();
  try{await foreach(var item in channel.Reader.ReadAllAsync(context.RequestAborted)){
   await context.Response.WriteAsync(JsonSerializer.Serialize(item,PublicJson)+"\n",context.RequestAborted);await context.Response.Body.FlushAsync(context.RequestAborted);
  }}finally{await producer;}
 }
 static async Task<T> ReadAsync<T>(HttpContext context,int maxBytes)
 {
  if(context.Request.ContentLength>maxBytes)throw new ArgumentException("Request body exceeds limit.");
  using var output=new MemoryStream();var buffer=new byte[16384];
  while(true){var n=await context.Request.Body.ReadAsync(buffer.AsMemory(0,Math.Min(buffer.Length,maxBytes-(int)output.Length+1)),context.RequestAborted);if(n==0)break;
   if(output.Length+n>maxBytes)throw new ArgumentException("Request body exceeds limit.");await output.WriteAsync(buffer.AsMemory(0,n),context.RequestAborted);}
  try{return JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(output.ToArray()),new JsonSerializerOptions(JsonSerializerDefaults.Web){MaxDepth=64,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}})??throw new ArgumentException("Request body is required.");}
  catch(JsonException){throw new ArgumentException("Invalid JSON request.");}
 }
}
