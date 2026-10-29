using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SimplePZMapmaker.Cli;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

public static class KnoxSceneTests
{
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("Knox scene aligns actual section roads in one common projected frame", () => InDirectory(dir => {
            var scene = Request(600, 300, align: true);
            var a = Geo(scene, 50, 210); var b = Geo(scene, 550, 210 - 500 * Math.Tan(Math.PI / 12));
            File.WriteAllText(Path.Combine(dir, "source.json"), Collection(Road(a, b)));
            var result = SceneBuilder.Build(scene, dir);
            Near(result.World.GeoReference!.RotationDegrees, 15);
            var origin = result.World.GeoReference;
            var projection = new GeographicProjection(origin, origin.OriginCellX, origin.OriginCellY);
            var expectedA = projection.Project((a[0], a[1])); var expectedB = projection.Project((b[0], b[1]));
            Near(expectedA.Y, expectedB.Y);
            var sections = result.World.Maps.Where(m => m.Project.LinearFeatures.Any(f => f.SourceTags.GetValueOrDefault("name") == "Grid road")).ToArray();
            Check(sections.Length >= 3, "Road did not span three sections");
            var points = sections.SelectMany(m => m.Project.LinearFeatures.Where(f => f.SourceTags.GetValueOrDefault("name") == "Grid road")
                .SelectMany(f => f.Points.Select(p => new LinearPoint(p.X + (m.Project.WorldX - origin.OriginCellX) * 256, p.Y + (m.Project.WorldY - origin.OriginCellY) * 256)))).ToArray();
            foreach (var p in points) Near(p.Y, expectedA.Y);
            Near(points.Min(p => p.X), expectedA.X); Near(points.Max(p => p.X), expectedB.X);
            Check(result.Report.Passed, "Shared frame failed the seam check");
        }));
        yield return ("Knox scene build preserves serialized input and owns its reference", () => InDirectory(dir => {
            var scene = Request(220, 220, align: true); scene.Reference.RotationDegrees = -7; scene.Reference.RegionalProfile = "ru-rural";
            var json = Collection(Road(Geo(scene, 20, 100), Geo(scene, 200, 100)));
            File.WriteAllText(Path.Combine(dir, "source.json"), json);
            var before = JsonSerializer.Serialize(scene, NativeMapExporter.JsonOptions);
            var result = SceneBuilder.Build(scene, dir);
            Check(JsonSerializer.Serialize(scene, NativeMapExporter.JsonOptions) == before, "Build mutated the supplied scene");
            Check(!ReferenceEquals(scene.Reference, result.World.GeoReference), "World aliases the input reference");
            Near(result.World.GeoReference!.RotationDegrees, 0);
            Check(result.World.GeoReference.RegionalProfile == "ru-rural", "Cloning lost the regional profile");
            Check(result.SourceJson == json, "Alignment rewrote the source");
        }));
        yield return ("Knox scene Overture fills gaps once and reports counts credit and original road tags", () => InDirectory(dir => {
            var scene = Request(220, 220, overture: "saved/official.geojson");
            var baseline = Building(scene, "osm-home", 20, 20); baseline["properties"]!["building"] = "house";
            var road = Road(Geo(scene, 10, 180), Geo(scene, 210, 180));
            road["properties"]!["surface"] = "gravel"; road["properties"]!["source:attribution"] = "Original survey";
            File.WriteAllText(Path.Combine(dir, "source.json"), Collection(baseline, road));
            var gap = Building(scene, "missing-home", 100, 80);
            Directory.CreateDirectory(Path.Combine(dir, "saved"));
            File.WriteAllText(Path.Combine(dir, "saved/official.geojson"), Collection(Building(scene, "duplicate-home", 20, 20), gap, gap.DeepClone(), Building(scene, "tiny", 150, 80, 2), Building(scene, "outside", 300, 80)));
            var before = JsonSerializer.Serialize(scene, NativeMapExporter.JsonOptions);
            var result = SceneBuilder.Build(scene, dir);
            var features = JsonNode.Parse(result.SourceJson)!["features"]!.AsArray();
            Check(features.Count == 3, "Gap fill removed roads or duplicated buildings");
            var keptRoad = features.Single(f => f!["properties"]?["highway"] is not null)!;
            Check(keptRoad["properties"]!["surface"]!.ToString() == "gravel" && keptRoad["properties"]!["source:attribution"]!.ToString() == "Original survey", "Original tags lost");
            Check(JsonNode.DeepEquals(keptRoad["geometry"], road["geometry"]), "Original road geometry changed");
            var added = features.Single(f => f!["properties"]?["overture:id"]?.ToString() == "missing-home")!["properties"]!;
            Check(added["source:attribution"]!.ToString() == "Overture Maps contributors" && added["source:license"]!.ToString() == "ODbL-1.0", "Supplement provenance lost");
            Check(result.World.Maps.SelectMany(m => m.Project.Attribution).Contains("Overture Maps contributors"), "Output credits lost");
            var sourceId = added["pz:source_id"]!.ToString();
            Check(result.World.Maps.SelectMany(m => m.Project.SourceFeatures).Any(f => f.SourceId == sourceId && f.Tags.GetValueOrDefault("source:attribution") == "Overture Maps contributors"), "Supplement identity or credit was not imported");
            Check(result.Warnings.Any(w => w.Contains("candidates 5") && w.Contains("added 1") && w.Contains("existing 2") && w.Contains("too small 1") && w.Contains("outside 1")), "Supplement counts missing");
            Check(result.Warnings.Any(w => w.Contains("ODbL") && w.Contains("roofprint")), "Supplement warning lost");
            Check(result.Report.Passed && result.Report.ByKind["building"].PlacedSection + result.Report.ByKind["building"].PlacedWorld == 2, "Merged buildings missing or duplicated in output");
            Check(JsonSerializer.Serialize(scene, NativeMapExporter.JsonOptions) == before, "Supplement mutated scene input");
        }));
        yield return ("Knox scene weak streets retain manual rotation with a warning", () => InDirectory(dir => {
            var scene = Request(220, 220, align: true); scene.Reference.RotationDegrees = -9;
            File.WriteAllText(Path.Combine(dir, "source.json"), Collection(Road(Geo(scene, 80, 100), Geo(scene, 100, 100))));
            var result = SceneBuilder.Build(scene, dir);
            Near(result.World.GeoReference!.RotationDegrees, -9);
            Check(result.Warnings.Any(w => w.Contains("manual rotation", StringComparison.OrdinalIgnoreCase) && w.Contains("street", StringComparison.OrdinalIgnoreCase)), "Weak street evidence was silent");
        }));
        yield return ("Knox scene rejects grid alignment over a precomputed warp", () => InDirectory(dir => {
            var scene = Request(220, 220, align: true);
            scene.Reference.Warp = new() { SourceX = [0, 512], SourceY = [0, 512], Destination = [new(0, 0), new(512, 0), new(0, 512), new(512, 512)] };
            File.WriteAllText(Path.Combine(dir, "source.json"), Collection(Road(Geo(scene, 20, 100), Geo(scene, 200, 100))));
            var before = JsonSerializer.Serialize(scene, NativeMapExporter.JsonOptions);
            Reject(() => SceneBuilder.Build(scene, dir), "warp");
            Check(JsonSerializer.Serialize(scene, NativeMapExporter.JsonOptions) == before, "Rejected alignment changed warp input");
        }));
        foreach (var sourceKind in new[] { "source", "sources", "overture" })
            yield return ($"Knox scene rejects oversized UTF8 {sourceKind} before parsing", () => InDirectory(dir => {
                var scene = Request(220, 220, overture: sourceKind == "overture" ? "large.json" : null);
                var ordinary = Collection(Road(Geo(scene, 20, 100), Geo(scene, 200, 100)));
                File.WriteAllText(Path.Combine(dir, "source.json"), ordinary);
                var large = Path.Combine(dir, "large.json");
                WriteLargeUtf8(large);
                if (sourceKind == "source") scene.Source = "large.json";
                if (sourceKind == "sources") scene.Sources.Add("large.json");
                Reject(() => SceneBuilder.Build(scene, dir), "64 MB");
            }));
        yield return ("Knox scene legacy source and sources preserve manual angle and listed precedence", () => InDirectory(dir => {
            var scene = Request(220, 220); scene.Reference.RotationDegrees = -12;
            var original = Road(Geo(scene, 20, 100), Geo(scene, 200, 100));
            var json = Collection(original); File.WriteAllText(Path.Combine(dir, "source.json"), json);
            var single = SceneBuilder.Build(scene, dir);
            Near(single.World.GeoReference!.RotationDegrees, -12); Check(single.SourceJson == json, "Legacy single-source bytes changed");
            original["properties"]!["name"] = "Later road"; scene.Sources.Add("later.json");
            File.WriteAllText(Path.Combine(dir, "later.json"), Collection(original));
            var combined = SceneBuilder.Build(scene, dir);
            Near(combined.World.GeoReference!.RotationDegrees, -12);
            var features = JsonNode.Parse(combined.SourceJson)!["features"]!.AsArray();
            Check(features.Count == 1 && features[0]!["properties"]!["name"]!.ToString() == "Later road", "Listed merge precedence changed");
            Check(!combined.Warnings.Any(w => w.Contains("street grid", StringComparison.OrdinalIgnoreCase) || w.Contains("Overture", StringComparison.OrdinalIgnoreCase)), "Legacy build enabled Knox options implicitly");
        }));
        yield return ("Knox scene reads optional fields and rejects unbounded supplement paths", () => InDirectory(dir => {
            var scene = Request(220, 220, align: true, overture: "saved/official.geojson");
            var path = Path.Combine(dir, "scene.json");
            File.WriteAllText(path, JsonSerializer.Serialize(scene, NativeMapExporter.JsonOptions));
            var reread = SceneBuilder.Read(path);
            var value = JsonNode.Parse(JsonSerializer.Serialize(reread, NativeMapExporter.JsonOptions))!;
            Check(value["alignStreetGrid"]?.GetValue<bool>() == true && value["overtureBuildings"]?.ToString() == "saved/official.geojson", "Scene options were dropped");
            foreach (var invalid in new[] { "", new string('x', 4097), "bad\npath", Path.GetFullPath(Path.Combine(dir, "absolute.json")) }) {
                value["overtureBuildings"] = invalid; File.WriteAllText(path, value.ToJsonString());
                Reject(() => SceneBuilder.Read(path), "overtureBuildings");
                var direct = JsonSerializer.Deserialize<Scene>(value.ToJsonString(), NativeMapExporter.JsonOptions)!;
                Reject(() => SceneBuilder.Build(direct, dir), "overtureBuildings");
            }
        }));
    }

    static Scene Request(double width, double height, bool align = false, string? overture = null)
    {
        var bounds = new GeoBounds { West = 30, North = 50.01, South = 50.01 - height / 111320 };
        bounds.East = bounds.West + width / (111320 * Math.Cos((bounds.South + bounds.North) * Math.PI / 360));
        var scene = new Scene { Format = "SimplePZScene", Version = 1, Name = "Knox CLI", Source = "source.json", Reference = new() { Bounds = bounds }, SectionSize = 256, SkipEmptySections = false };
        var json = JsonNode.Parse(JsonSerializer.Serialize(scene, NativeMapExporter.JsonOptions))!;
        json["alignStreetGrid"] = align; json["overtureBuildings"] = overture;
        return JsonSerializer.Deserialize<Scene>(json.ToJsonString(), NativeMapExporter.JsonOptions)!;
    }
    static double[] Geo(Scene scene, double x, double y) => [scene.Reference.Bounds.West + x / (111320 * Math.Cos((scene.Reference.Bounds.South + scene.Reference.Bounds.North) * Math.PI / 360)), scene.Reference.Bounds.North - y / 111320];
    static JsonObject Road(double[] a, double[] b) => JsonSerializer.SerializeToNode(new { type = "Feature", id = "grid-road", properties = new { highway = "residential", name = "Grid road" }, geometry = new { type = "LineString", coordinates = new[] { a, b } } })!.AsObject();
    static JsonObject Building(Scene scene, string id, double x, double y, double size = 12) => JsonSerializer.SerializeToNode(new { type = "Feature", properties = new { id, type = "building" }, geometry = new { type = "Polygon", coordinates = new[] { new[] { Geo(scene, x, y), Geo(scene, x + size, y), Geo(scene, x + size, y + size), Geo(scene, x, y + size), Geo(scene, x, y) } } } })!.AsObject();
    static string Collection(params JsonNode[] features) => new JsonObject { ["type"] = "FeatureCollection", ["features"] = new JsonArray(features.Select(f => f.DeepClone()).ToArray()) }.ToJsonString();
    static void WriteLargeUtf8(string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.Write("{\"type\":\"FeatureCollection\",\"features\":[],\"padding\":\"");
        var chunk = new string('я', 32768); // Characters fit the limit; UTF8 bytes do not.
        for (var i = 0; i < 977; i++) writer.Write(chunk);
        writer.Write("\"}");
    }
    static void InDirectory(Action<string> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "knox-scene-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { test(dir); } finally { Directory.Delete(dir, true); }
    }
    static void Reject(Action action, string message)
    {
        try { action(); } catch (ArgumentException error) { Check(error.Message.Contains(message, StringComparison.OrdinalIgnoreCase), "Wrong rejection: " + error.Message); return; }
        throw new Exception("Expected rejection mentioning " + message);
    }
    static void Near(double actual, double expected) => Check(Math.Abs(actual - expected) < 1e-6, $"Expected {expected:R}, received {actual:R}");
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
