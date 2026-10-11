using SimplePZMapmaker.Core;
public static class GeoTests
{
    public static IEnumerable<(string,Action)> Cases() {
        yield return ("GeoJSON imports road and attribution",()=>{
            var result=GeographicImporter.Import("""{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"highway":"residential"},"geometry":{"type":"LineString","coordinates":[[30,50],[30.002,50.002]]}}]}""",MapGenerator.Generate(new(){Kind="blank"}));
            if(!result.Project.Terrain.Contains(Terrain.Road)||result.ImportedFeatures!=1||result.Project.Attribution.Count==0)throw new Exception("Import lost geography or attribution");
        });
        yield return ("water polygon hole preserves island",()=>{
            var result=GeographicImporter.Import("""{"type":"FeatureCollection","features":[{"type":"Feature","properties":{"natural":"water"},"geometry":{"type":"Polygon","coordinates":[[[30,50],[30.004,50],[30.004,50.004],[30,50.004],[30,50]],[[30.001,50.001],[30.003,50.001],[30.003,50.003],[30.001,50.003],[30.001,50.001]]]}}]}""",MapGenerator.Generate(new(){Kind="blank"}));
            if(result.Project.Terrain[128*256+128]==Terrain.Water||!result.Project.Terrain.Contains(Terrain.Water))throw new Exception("Hole was filled");
        });
        yield return ("malformed coordinates rejected",()=>{
            try {GeographicImporter.Import("""{"type":"FeatureCollection","features":[{"properties":{},"geometry":{"type":"LineString","coordinates":[[1e100,50],[30,50]]}}]}""",MapGenerator.Generate(new(){Kind="blank"}));}catch(ArgumentException){return;}throw new Exception("Invalid coordinate accepted");
        });
        yield return ("OSM buildings and clipping do not violate project bounds",()=>{
            var json="""{"elements":[{"type":"way","id":1,"tags":{"building":"house"},"geometry":[{"lon":30.001,"lat":50.001},{"lon":30.0012,"lat":50.001},{"lon":30.0012,"lat":50.0012},{"lon":30.001,"lat":50.0012},{"lon":30.001,"lat":50.001}]},{"type":"way","id":2,"tags":{"highway":"residential"},"geometry":[{"lon":29.99,"lat":50.002},{"lon":30.02,"lat":50.002}]}]}""";
            var result=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),new(){West=30,East=30.004,South=50,North=50.004});
            ProjectValidator.Validate(result.Project); if(result.Project.Buildings.Count!=1||!result.Project.Terrain.Contains(Terrain.Road))throw new Exception("Building or clipped road missing");
        });
    }
}
