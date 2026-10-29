namespace SimplePZMapmaker.Core;

/// <summary>A straightened footprint in absolute world tiles. Index is its original source feature ordinal,
/// not its position in Candidates; ignored features can leave gaps. Width and height are at most 512.</summary>
public sealed record WorldBuildingCandidate(int Index,int X,int Y,int Width,int Height);

/// <summary>Parsed source geometry and full source-order priority, reusable across bounded world windows.
/// Contains no terrain rasters and performs no disk I/O.</summary>
public sealed class WorldBuildingBatch
{
 readonly IReadOnlyDictionary<int,GeographicImporter.PreparedWorldBuilding> prepared;
 readonly IReadOnlyDictionary<string,int> sourceOrder;
 public IReadOnlyList<WorldBuildingCandidate> Candidates {get;}

 internal WorldBuildingBatch(List<GeographicImporter.PreparedWorldBuilding> buildings,IReadOnlyDictionary<string,int> sourceOrder){
  prepared=buildings.ToDictionary(b=>b.Candidate.Index);this.sourceOrder=sourceOrder;
  Candidates=Array.AsReadOnly(buildings.Select(b=>b.Candidate).ToArray());
 }

 /// <summary>Import only the candidate with this source index. The caller supplies valid terrain sections
 /// intersecting its bbox plus a one-tile margin, and owners of prior overlapping shared buildings.
 /// Global metadata, complete world coverage and overlap bookkeeping remain the caller's responsibility.</summary>
 public string? SourceId(int index)=>prepared.TryGetValue(index,out var value)?value.Tags.GetValueOrDefault("__pz_source"):throw new ArgumentOutOfRangeException(nameof(index));

 public (int Added,int Skipped) ImportOne(int index,WorldProject boundedWindow,int seed,List<string> warnings,CancellationToken token=default){
  if(!prepared.TryGetValue(index,out var building))throw new ArgumentOutOfRangeException(nameof(index),"Unknown prepared building source index.");
  return GeographicImporter.ImportPreparedWorldBuilding(building,boundedWindow,seed,warnings,sourceOrder,token);
 }
}

public static partial class GeographicImporter
{
 /// <summary>Parse the source once and retain straightened global footprints and the complete original
 /// feature priority. ImportOne reuses this data without reparsing or projecting the source.</summary>
 public static WorldBuildingBatch PrepareWorldBuildings(string json,WorldGeoReference reference){
  var features=ReadWorldFeatures(json);var projection=new GeographicProjection(reference,reference.OriginCellX,reference.OriginCellY);
  var sourceOrder=features.Select((f,i)=>(Id:f.Tags.GetValueOrDefault("__pz_source"),Index:i)).Where(v=>v.Id is not null).GroupBy(v=>v.Id!).ToDictionary(g=>g.Key,g=>g.First().Index);
  var buildings=new List<PreparedWorldBuilding>();
  for(var i=0;i<features.Count;i++){
   var feature=features[i];if(!feature.Polygon||!feature.Tags.ContainsKey("building"))continue;
   if(PrepareWorldBuilding(feature,projection,reference,i,out _) is {} prepared)buildings.Add(prepared);
  }
  return new(buildings,sourceOrder);
 }
}
