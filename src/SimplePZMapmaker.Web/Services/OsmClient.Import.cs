using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

public sealed partial class OsmClient
{
 private GeofabrikClient? globalSources;
 public GeofabrikClient GlobalSources {get{lock(gate)return globalSources??=new(Path.Combine(cacheDirectory,"regions"),client,importTimeouts);}}
 public OsmSourceFiles Sources {get;}=new(Path.Combine(cacheDirectory,"sources"));
 public async Task<OsmDownload> FetchForImport(GeoBounds bounds,string mode,string? sourceId,CancellationToken token,IProgress<OsmProgress>? progress=null,int maxBytes=16_000_000,bool world=false)
 {
  if(world)WorldLayoutLimits.ValidateSourceBounds(bounds);else ProjectValidator.ValidateBounds(bounds);
  if(mode is not("auto" or "global" or "overpass" or "regional" or "file"))throw new ArgumentException("Choose automatic, Overpass, regional extract or saved file as the source.");
  if(maxBytes is <1 or >OsmSourceFiles.MaximumJsonBytes)throw new ArgumentException("Invalid geographic source size limit.");
  token.ThrowIfCancellationRequested();
  if(mode=="file"){
   if(!OsmSourceFiles.SafeId(sourceId))throw new ArgumentException("Choose and prepare a geographic source file first.");
   progress?.Report(new("source",0,1,"saved file"));var offline=await Sources.ReadAsync(sourceId!,bounds,maxBytes,token);progress?.Report(new("downloaded",1,1,offline.Source,offline.DataTimestamp));return offline;
  }
  OsmDownload download;var warnings=new List<string>();string? regionalFailure=null;
  try{
   if(mode=="regional"){
    progress?.Report(new("regional",0,1,"download.openstreetmap.fr"));download=await FetchExtractDetailed(bounds,token);
   }else if(mode is "auto" or "global"){
    try{download=await GlobalSources.FetchAsync(bounds,token,progress,maxBytes);}
    catch(Exception e) when(mode=="auto"&&!token.IsCancellationRequested&&e is not (RegionalProcessingException or RegionalTransferTimeoutException)&&(e is HttpRequestException or ArgumentException or IOException)){
     regionalFailure="Geofabrik regional source: "+RegionalProcessingException.Detail(e);
     warnings.Add("The regional source could not be loaded; "+regionalFailure+" Trying online OSM providers. Partial regional downloads are retained for resume.");progress?.Report(new("download",0,1,null,null,warnings[^1]));download=await Online();
    }
   }else download=await Online();
  }catch(RegionalTransferTimeoutException){throw;}
  catch(HttpRequestException e) when(mode=="auto"&&!token.IsCancellationRequested){
   var saved=await Sources.LastDownloadedAsync(bounds,maxBytes,token);
   if(saved is null)throw new HttpRequestException((regionalFailure is null?"":regionalFailure+". ")+"Online OSM sources are unavailable. Choose Saved file and load GeoJSON, Overpass JSON or a regional .osm.pbf (up to 64 MiB). Previously downloaded source files work without OSM. The current map was preserved.",e);
   download=saved;warnings.Add($"Online OSM sources are unavailable. Used a previously downloaded source from {saved.RetrievedAtUtc:u}; data date: {saved.DataTimestamp??"not specified"}. This snapshot may be out of date.");
  }
  token.ThrowIfCancellationRequested();if(System.Text.Encoding.UTF8.GetByteCount(download.Data)>maxBytes)throw new ArgumentException($"This import's source exceeds {maxBytes/1_000_000} MB. Reduce the area or use a smaller saved source.");
  download.Warnings.InsertRange(0,warnings);var snapshot=await Sources.SaveAsync(download,bounds,token);progress?.Report(new("downloaded",1,1,download.Source,download.DataTimestamp));return download with{SourceId=snapshot.Id};
  Task<OsmDownload> Online()=>world?FetchTiledDetailed(bounds,token,progress,maxBytes,world:true):bounds.North-bounds.South>.01||bounds.East-bounds.West>.02?FetchTiledDetailed(bounds,token,progress,maxBytes):FetchSingleDetailed(bounds,token,progress,mapBackup:true);
 }
}
