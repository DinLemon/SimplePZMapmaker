using SimplePZMapmaker.Core;
public static class GeoTests
{
    public static IEnumerable<(string,Action)> Cases() {
        yield return("geographic source keeps invalid parcel outlines as evidence instead of failing import",()=>{
            var bounds=new GeoBounds{West=30,East=30.004,South=50,North=50.004};var reference=new WorldGeoReference{Bounds=bounds,MetresPerTile=1,OriginCellX=100,OriginCellY=100};var projection=new GeographicProjection(reference,100,100);
            var points=new[]{(10,10),(100,100),(10,80),(120,10),(10,10)}.Select(p=>{var g=projection.Geographic(p.Item1,p.Item2);return new[]{g.Longitude,g.Latitude};}).ToArray();
            var json=System.Text.Json.JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{new{type="Feature",id="crossing-area",properties=new{landuse="forest"},geometry=new{type="Polygon",coordinates=new[]{points}}}}});
            var result=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),bounds,projection:projection);ProjectValidator.Validate(result.Project);
            if(result.Project.SourceFeatures.Single().SourceId!="geo-crossing-area"||result.Project.SourceFeatures[0].Geometry[0].Count!=5||result.Project.Sites.Count!=0||!result.Warnings.Any(w=>w.Contains("Site grouping")&&w.Contains("evidence")))throw new Exception("Invalid source parcel was lost, accepted or omitted without a report");
        });
        yield return("geographic source preserves sites with more than thirty one holes",()=>{
            var bounds=new GeoBounds{West=30,East=30.004,South=50,North=50.004};var reference=new WorldGeoReference{Bounds=bounds,MetresPerTile=1,OriginCellX=100,OriginCellY=100};var projection=new GeographicProjection(reference,100,100);
            double[][] Ring(int x,int y,int size)=>new[]{(x,y),(x+size,y),(x+size,y+size),(x,y+size),(x,y)}.Select(p=>{var g=projection.Geographic(p.Item1,p.Item2);return new[]{g.Longitude,g.Latitude};}).ToArray();
            var rings=new[]{Ring(10,10,220)}.Concat(Enumerable.Range(0,33).Select(i=>Ring(20+i%6*30,20+i/6*30,8))).ToArray();var json=System.Text.Json.JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{new{type="Feature",id="many-holes",properties=new{landuse="forest"},geometry=new{type="Polygon",coordinates=rings}}}});
            var result=GeographicImporter.Import(json,MapGenerator.Generate(new(){Kind="blank"}),bounds,projection:projection);var p=result.Project;ProjectValidator.Validate(p);
            if(p.SourceFeatures.Single().Geometry.Count!=34||p.SourceFeatures[0].SourceGeometry.Count!=34||p.Sites.Single().Boundary.Count!=34)throw new Exception("Source or site holes were discarded");
            if(SiteModel.Contains(p.Sites[0],24,24)||!SiteModel.Contains(p.Sites[0],15,15)||p.Terrain[24*256+24]!=Terrain.Grass||p.Terrain[15*256+15]!=Terrain.Forest)throw new Exception("Import filled a hole or lost the outer polygon");
        });
        foreach(var retained in new[]{0,9999})yield return ($"dense geographic dressing respects decoration capacity (retained={retained})",()=>{
            var p=MapGenerator.Generate(new(){Kind="blank",Size=1024});p.Spawns=[new(){X=900,Y=900}];
            for(var i=0;i<retained;i++)p.Decorations.Add(new(){Id="authored-"+i,X=800+i%100,Y=10+i/100,Asset="street_decoration_01_28",Parts=[new(0,0,"street_decoration_01_28")]});
            var reference=new WorldGeoReference{Bounds=new(){West=32,East=32.01,South=54,North=54.01},MetresPerTile=1,OriginCellX=100,OriginCellY=100};
            var projection=new GeographicProjection(reference,100,100);
            double[] At(double x,double y){var point=projection.Geographic(x,y);return[point.Longitude,point.Latitude];}
            // More than 10,000 whole stalls WITH rear aisles: capacity and omission reporting must still be exercised.
            var json=System.Text.Json.JsonSerializer.Serialize(new{type="FeatureCollection",features=new[]{new{type="Feature",properties=new{amenity="parking"},geometry=new{type="Polygon",coordinates=new[]{new[]{At(10,10),At(710,10),At(710,370),At(10,370),At(10,10)}}}}}});
            var result=GeographicImporter.Import(json,p,reference.Bounds,preserveEdits:retained>0,projection:projection);
            ProjectValidator.Validate(result.Project);
            if(result.Project.Decorations.Count!=10000||p.Decorations.Count!=retained||result.Project.Decorations.Count(d=>d.Id.StartsWith("authored-"))!=retained)throw new Exception($"Dressing capacity or preservation failed: {result.Project.Decorations.Count} decorations; "+string.Join("; ",result.Project.ImportSummary!.Notes));
            if(!result.Project.ImportSummary!.Notes.Any(n=>n.Contains("decoration limit")))throw new Exception("Dressing capacity omissions were not reported");
        });
        foreach(var preserve in new[]{false,true})yield return ($"dense geographic import respects building capacity (preserve={preserve})",()=>{
            var p=MapGenerator.Generate(new(){Kind="blank",Size=1024});
            p.Spawns=[new(){X=1000,Y=1000}];
            if(preserve)p.Buildings.Add(new(){Id="manual",Name="Authored shed",Kind="garage",X=900,Y=900,Width=2,Height=2,Modified=true});
            const double south=50,north=50.02,west=30;
            var cos=Math.Cos((south+north)/2*Math.PI/180);
            var bounds=new GeoBounds{West=west,East=west+(north-south)/cos,South=south,North=north};
            var features=Enumerable.Range(0,4002).Select(i=>{
                var x=16+i%64*12;var y=16+i/64*12;
                double[] Point(int dx,int dy)=>[west+(x+dx-8)*(bounds.East-west)/1008,north-(y+dy-8)*(north-south)/1008];
                return new{type="Feature",id=i,properties=new{building="garage"},geometry=new{type="Polygon",coordinates=new[]{new[]{Point(0,0),Point(2,0),Point(2,2),Point(0,2),Point(0,0)}}}};
            });
            var json=System.Text.Json.JsonSerializer.Serialize(new{type="FeatureCollection",features});
            var result=GeographicImporter.Import(json,p,bounds,preserveEdits:preserve);
            ProjectValidator.Validate(result.Project);
            var expectedSkipped=preserve?3:2;
            if(result.Project.Buildings.Count!=4000||result.SkippedBuildings!=expectedSkipped||result.SkipReasons.GetValueOrDefault("Section building limit (4000)")!=expectedSkipped)throw new Exception("Dense import did not report its capacity boundary");
            if(result.SourceBuildings!=4002||result.Project.SourceFeatures.Count!=4002||result.Project.ImportSummary!.SkippedOutlines.Count!=expectedSkipped)throw new Exception("Capacity reporting lost source outlines");
            if(!result.Warnings.Any(w=>w.Contains("sections",StringComparison.OrdinalIgnoreCase)))throw new Exception("No actionable capacity warning");
            if(p.Buildings.Count!=(preserve?1:0)||preserve&&!result.Project.Buildings.Any(b=>b.Id=="manual"&&b.Modified))throw new Exception("Authored input was modified or lost");
        });
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
