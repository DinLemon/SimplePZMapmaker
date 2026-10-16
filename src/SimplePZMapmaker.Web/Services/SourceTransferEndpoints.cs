using SimplePZMapmaker.Core;
namespace SimplePZMapmaker.Web.Services;
public sealed record SourceTransferRequest(MapProject Project,WorldProject? World=null,string? MemberId=null,GenerationQualityArea? Area=null,int? MaxFeatures=null);
public static class SourceTransferEndpoints{
 public static SourceTransferReport Analyze(SourceTransferRequest request,CancellationToken token=default){
  if(request?.Project is null)throw new ArgumentException("Select a source map first.");
  var raw=NeighborhoodPlanner.Copy(request.Project);ProjectValidator.Validate(raw);MapProject p=raw;
  if(request.World is not null)p=GenerationStudioWorldContext.Create(raw,WorldAssembler.Clone(request.World),request.MemberId).ProjectedProject;
  else if(request.MemberId is not null)throw new ArgumentException("World member needs a matching world snapshot.");
  var report=SourceTransferAudit.Analyze(p,request.Area,request.MaxFeatures??5000,token);
  if(request.World is not null)report.Notes.Add("World section scope includes projected shared buildings. Beyond-section data and routes are unknown.");return report;
 }
 public static void MapSourceTransferEndpoints(this WebApplication app)=>app.MapPost("/api/geography/audit",(SourceTransferRequest request,CancellationToken token)=>Analyze(request,token));
}
