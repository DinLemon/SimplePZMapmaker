using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Web.Services;

public static class OsmStreamTests
{
 public static IEnumerable<(string,Action)> Cases(){
  yield return("OSM stream throttles repeated indexing progress but preserves stage changes",()=>{
   var lines=new List<string>();var now=DateTimeOffset.UtcNow;var progress=new OsmMapImport.StreamProgress(lines.Add,()=>now);
   for(var i=0;i<5000;i++)progress.Report(new("pbf-index",0,0,"pbf-scan-ways"){BytesReceived=i});
   if(lines.Count!=1)throw new Exception("PBF blocks flooded the progress stream");
   progress.Report(new("pbf-index",0,0,"pbf-geometry"));now+=TimeSpan.FromSeconds(2);progress.Report(new("pbf-index",0,0,"pbf-geometry"));
   if(lines.Count!=3)throw new Exception("New stages or periodic updates were lost");
  });
  yield return("OSM stream bounds UTF8 progress across long downloads and preserves heartbeats",()=>{
   var lines=new List<string>();var now=DateTimeOffset.UtcNow;var progress=new OsmMapImport.StreamProgress(lines.Add,()=>now);
   for(var i=0;i<10000;i++){now+=TimeSpan.FromSeconds(2);progress.Report(new("pbf-index",i,10000,"Область " + i + " Kantō"));}
   var frames=lines.Where(line=>line!="\n").ToArray();
   if(frames.Sum(Encoding.UTF8.GetByteCount)>OsmMapImport.StreamProgress.MaximumProgressBytes||!lines.Any(line=>line=="\n"))throw new Exception("Unbounded progress or missing live heartbeat");
   foreach(var frame in frames){using var document=JsonDocument.Parse(frame);if(document.RootElement.GetProperty("kind").GetString()!="progress")throw new Exception("Invalid stream frame");}
  });
 }
}
