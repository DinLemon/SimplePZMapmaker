using SimplePZMapmaker.Core;
public static class PilnaTests
{
    public static IEnumerable<(string,Action)> Cases(){
        yield return ("2048 map supports entire settlement",()=>{var p=MapGenerator.Generate(new(){Kind="blank",Size=2048});ProjectValidator.Validate(p);if(p.Terrain.Length!=4194304||p.Zombies.Length!=65536)throw new Exception("Large map layers incomplete");});
        yield return ("geographic road width follows metres rather than fixed tiles",()=>{
            var json="""{"type":"FeatureCollection","features":[{"properties":{"highway":"residential","width":"6"},"geometry":{"type":"LineString","coordinates":[[30,50.002],[30.004,50.002]]}}]}""";
            var small=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank",Size=256}),new(){West=30,East=30.004,South=50,North=50.004});
            var large=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank",Size=1024}),new(){West=30,East=30.004,South=50,North=50.004});
            int Count(MapProject p)=>Enumerable.Range(0,p.Height).Count(y=>p.Terrain[y*p.Width+p.Width/2]==Terrain.Road);
            if(Count(large.Project)<=Count(small.Project))throw new Exception("Road has same tile width at different geographic scales");
        });
        yield return ("real Pilna source imports reproducibly with majority of buildings",()=>{
            var source=File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"examples","Pilna","osm-source.json"));
            var result=GeographicImporter.Import(source.TrimStart('\uFEFF'),MapGenerator.Generate(new(){Kind="blank",Size=2048,Name="Pilna"}),new(){South=55.5363,North=55.5781,West=45.8833,East=45.9422});
            if(result.SourceBuildings!=1789||result.Project.Buildings.Count<1300||result.Project.Buildings.Count+result.SkippedBuildings!=result.SourceBuildings||result.Project.MetresPerTile is not >2 or not <3||result.Project.ImportSummary?.PlacedBuildings!=result.Project.Buildings.Count)throw new Exception("Pilna geometry, scale or report changed unexpectedly");
            ProjectValidator.Validate(result.Project);
        });
    }
}
