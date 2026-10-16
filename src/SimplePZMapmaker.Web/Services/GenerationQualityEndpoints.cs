using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;

public sealed record GenerationQualityRequest(MapProject Project,GenerationQualityArea? Area=null,int? MaximumBuildings=null,WorldProject? World=null,string? MemberId=null);
public static class GenerationQualityEndpoints {
 public static GenerationQualityReport Analyze(GenerationQualityRequest request,CancellationToken token=default,Func<string,IReadOnlyDictionary<string,string>>? properties=null){
  token.ThrowIfCancellationRequested();
  if(request is null||request.Project is null)throw new ArgumentException("Quality audit needs the active raw map.");
  if(request.World is null){if(request.MemberId is not null)throw new ArgumentException("A world member requires its world snapshot.");return GenerationQuality.Analyze(request.Project,request.Area,request.MaximumBuildings??128,token,properties);}
  // Validation and projection stay transient; even validators that normalize authoring cannot alter the submitted snapshot.
  var world=WorldAssembler.Clone(request.World);
  var context=GenerationStudioWorldContext.Create(request.Project,world,request.MemberId);
  token.ThrowIfCancellationRequested();
  var report=GenerationQuality.Analyze(context.ProjectedProject,request.Area,request.MaximumBuildings??128,token,properties);
  report.Notes.Add($"Shared world scope: section {context.MemberId} includes projected shared buildings, roads and native infrastructure. Coordinates are local to section cell {request.Project.WorldX},{request.Project.WorldY}; geometry and routes beyond this section are unknown. The raw map and world snapshot remain unchanged.");
  return report;
 }
 public static void MapGenerationQualityEndpoints(this WebApplication app){
  app.MapPost("/api/generation/quality",(GenerationQualityRequest request,GameAssetCatalog assets,CancellationToken token)=>
   AnalyzeResources(request,assets,token));
 }
 public static GenerationQualityReport AnalyzeResources(GenerationQualityRequest request,GameAssetCatalog assets,CancellationToken token=default){
  var report=Analyze(request,token,assets.Ready?id=>assets.Get(id).Properties:null);
  if(!assets.Ready){report.Complete=false;if(report.Status=="pass")report.Status="warning";report.Notes.Add(GenerationResourceValidation.OfflineNote);}
  return report;
 }
}
