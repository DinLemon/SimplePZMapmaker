using System.Reflection;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;

public static class FuelSourceTests
{
 public static IEnumerable<(string,Action)> Cases()
 {
  yield return ("fuel source PBF retains standalone mapped pump nodes in normal and indexed readers",()=>{
   var root=Path.Combine(Path.GetTempPath(),"spzm-fuel-source-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   try{
    foreach(var mode in new[]{"fuelpump","inactivepump","closedfuel"}){
    var path=Path.Combine(root,"pump.osm.pbf");File.WriteAllBytes(path,UpstreamTests.PbfFixture(false,mode:mode));
    var bounds=new GeoBounds{West=32.04,East=32.065,South=54.77,North=54.795};
    foreach(var source in new[]{PbfExtractReader.Read(path,bounds),PbfExtractReader.ReadLarge(path,bounds,root)}){
     using var doc=JsonDocument.Parse(source.Data);
     var key=mode=="fuelpump"?"man_made":mode=="inactivepump"?"disused:man_made":"disused:amenity";
     Check(doc.RootElement.GetProperty("elements").EnumerateArray().Any(e=>e.GetProperty("tags").TryGetProperty(key,out var tag)&&tag.GetString()==(mode=="closedfuel"?"fuel":"fuel_pump")),"Standalone PBF pump or closed station context was dropped: "+mode);
    }
   }}finally{Directory.Delete(root,true);}
  });
  yield return ("fuel source OSM map API retains pump nodes without broadening to every man made object",()=>{
   var data=JsonSerializer.Serialize(new{elements=new[]{
    new{type="node",id=1,lon=32.1,lat=54.8,tags=new{man_made="fuel_pump"}},
    new{type="node",id=2,lon=32.1,lat=54.8,tags=new{man_made="water_tower"}}}});
   var method=typeof(OsmClient).GetMethod("MapApiGeometry",BindingFlags.NonPublic|BindingFlags.Static)!;
   using var doc=JsonDocument.Parse((string)method.Invoke(null,[data,CancellationToken.None])!);
   var nodes=doc.RootElement.GetProperty("elements");Check(nodes.GetArrayLength()==1&&nodes[0].GetProperty("id").GetInt32()==1,"Backup provider lost a pump or selected unrelated man-made objects");
  });
  yield return ("fuel source evidence retains native role fuel types and disused state",()=>{
   var tags=new Dictionary<string,string>{{"man_made","fuel_pump"},{"fuel:diesel","yes"},{"disused","yes"},{"abandoned:amenity","fuel"},{"layer","1"},{"access","private"}};
   var kept=GeographicEvidence.CaptureTags(tags);Check(tags.All(t=>kept.GetValueOrDefault(t.Key)==t.Value),"Fuel state/evidence was silently discarded");
   var essential=new Dictionary<string,string>(tags);foreach(var i in Enumerable.Range(0,100))tags["addr:fixture"+i]="extra";
   kept=GeographicEvidence.CaptureTags(tags);Check(kept.Count<=64&&essential.All(t=>kept.GetValueOrDefault(t.Key)==t.Value),"Tag cap discarded role/closure/access/level evidence before optional address fields");
  });
  yield return ("fuel source Overpass query requests standalone mapped pump points",()=>{
   var root=Path.Combine(Path.GetTempPath(),"spzm-fuel-query-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   try{var handler=new QueryProbe();using var client=new OsmClient(root,handler,TimeSpan.Zero);
    client.FetchDetailed(new(){West=30,East=30.001,South=50,North=50.001},default).GetAwaiter().GetResult();
    Check(handler.Query.Contains("node[\"man_made\"=\"fuel_pump\"]",StringComparison.Ordinal)&&handler.Query.Contains("nwr[\"disused:amenity\"=\"fuel\"]",StringComparison.Ordinal),"Online source selection excludes standalone pumps or closed station context");
   }finally{Directory.Delete(root,true);}
  });
 }
 sealed class QueryProbe:HttpMessageHandler
 {
  public string Query="";
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
   Query=Uri.UnescapeDataString(await request.Content!.ReadAsStringAsync(token));
   return new(System.Net.HttpStatusCode.OK){Content=new StringContent("{\"elements\":[{\"type\":\"node\",\"id\":1,\"lon\":30.0001,\"lat\":50.0001,\"tags\":{\"man_made\":\"fuel_pump\"}}]}")};
  }
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
}
