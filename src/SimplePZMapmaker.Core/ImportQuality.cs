namespace SimplePZMapmaker.Core;
public sealed class ImportQualitySummary {
 public int SourceFootprints {get;set;} public int AdaptedFootprints {get;set;} public int GeneratedLayouts {get;set;}
 public int SourceFloors {get;set;} public int EstimatedFloors {get;set;} public int AssumedFloors {get;set;} public int ManualFloors {get;set;}
 public int SourceRoofs {get;set;} public int AdaptedRoofs {get;set;} public int GeneratedRoofs {get;set;}
 public int SourceBasements {get;set;} public int AssumedBasements {get;set;} public int OmittedFeatures {get;set;}
}
public sealed record SkippedImportOutline(string? SourceId,string Name,string Reason,int X,int Y,int Width,int Height);
public static class ImportQuality {
 public static ImportQualitySummary Summarize(IEnumerable<Building> buildings,int omitted=0){var q=new ImportQualitySummary{OmittedFeatures=omitted};foreach(var b in buildings){q.SourceFootprints++;if(b.Design?.Shape=="custom")q.AdaptedFootprints++;q.GeneratedLayouts++;switch(b.LevelsOrigin){case "osm-levels":q.SourceFloors++;break;case "osm-height":q.EstimatedFloors++;break;case "manual":q.ManualFloors++;break;default:q.AssumedFloors++;break;}if(b.SourceRoof is null)q.GeneratedRoofs++;else if(b.SourceRoof is "flat" or "gabled" or "shed" or "hipped" or "skillion"&&b.Design?.Roof==(b.SourceRoof=="skillion"?"shed":b.SourceRoof))q.SourceRoofs++;else q.AdaptedRoofs++;if(int.TryParse(b.SourceBasements,out var depth)&&depth>=0)q.SourceBasements++;else q.AssumedBasements++;}return q;}
 public static void Validate(ImportSummary report){if(report.SkippedOutlines is null||report.SkippedOutlines.Count>256||report.SkippedOutlines.Any(o=>o is null||o.Name is null||o.Name.Length>100||o.Reason is null||o.Reason.Length>100||o.SourceId?.Length>100||o.Width is <0 or >100000||o.Height is <0 or >100000||o.X is < -100000 or >100000||o.Y is < -100000 or >100000))throw new ArgumentException("Invalid skipped import outline report.");if(report.Quality is {} q&&new[]{q.SourceFootprints,q.AdaptedFootprints,q.GeneratedLayouts,q.SourceFloors,q.EstimatedFloors,q.AssumedFloors,q.ManualFloors,q.SourceRoofs,q.AdaptedRoofs,q.GeneratedRoofs,q.SourceBasements,q.AssumedBasements,q.OmittedFeatures}.Any(n=>n is <0 or >30000))throw new ArgumentException("Invalid import quality counts.");}
}
