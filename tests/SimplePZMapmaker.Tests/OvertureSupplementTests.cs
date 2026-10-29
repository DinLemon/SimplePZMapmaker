using System.Text.Json.Nodes;
using SimplePZMapmaker.Core;

public static class OvertureSupplementTests
{
    public static IEnumerable<(string Name, Action Run)> Cases()
    {
        yield return ("overture adds missing building and preserves GeoJSON roads holes and tags", () => {
            var road = new JsonObject { ["type"] = "Feature", ["id"] = "road", ["properties"] = new JsonObject { ["highway"] = "service", ["custom"] = new JsonObject { ["keep"] = 7 } },
                ["geometry"] = new JsonObject { ["type"] = "LineString", ["coordinates"] = Ring((0, 0), (100, 100)) } };
            var old = Building("old", Rect(0, 0, 20, 20), Rect(4, 4, 8, 8)); old["properties"]!["building"] = "yes";
            var baseline = Collection(road, old);
            var r = Merge(baseline, Collection(Building("new", Rect(40, 40, 50, 50))));
            Check(r.Candidates == 1 && r.Added == 1 && Features(r.Json).Count == 3, "Missing footprint was not added");
            var retained = Features(r.Json).Single(f => f!["properties"]?["highway"] is not null)!;
            Check(JsonNode.DeepEquals(retained["geometry"], road["geometry"]) && retained["properties"]!["custom"]!["keep"]!.GetValue<int>() == 7, "Road or nested metadata changed");
            Check(JsonNode.DeepEquals(Features(r.Json)[1]!["geometry"], old["geometry"]), "Baseline courtyard changed");
        });
        yield return ("overture offsets and repeated candidates do not duplicate OSM buildings", () => {
            const string osm = """{"elements":[{"type":"way","id":17,"tags":{"building":"yes","name":"Original"},"geometry":[{"lon":0,"lat":0},{"lon":0.0002,"lat":0},{"lon":0.0002,"lat":0.0002},{"lon":0,"lat":0.0002},{"lon":0,"lat":0}]}]}""";
            var shifted = Building("roof", Rect(1, 1, 23, 23));
            var r = Merge(osm, Collection(shifted, (JsonObject)shifted.DeepClone()));
            Check(r.Added == 0 && r.Existing == 2 && r.Candidates == 2, "Tiny roof offset escaped duplicate detection");
            var old = Features(r.Json).Single()!;
            Check(old["properties"]!["pz:source_id"]!.ToString() == "osm-way-17" && old["properties"]!["name"]!.ToString() == "Original", "OSM identity/tags changed");
        });
        yield return ("overture overlap catches offset wings whose centroids lie outside each other", () => {
            var old = Building("osm", Rect(0, 0, 20, 2)); old["properties"]!["building"] = "yes";
            var r = Merge(Collection(old), Collection(Building("roof", Rect(8, 1.5, 28, 3.5))));
            Check(r.Existing == 1 && r.Added == 0, "Intersecting roof strips escaped overlap detection");
        });
        yield return ("overture net area excludes holes and rejects sheds below ten square metres", () => {
            var r = Merge(Empty, Collection(Building("shed", Rect(0, 0, 3, 3)), Building("almost-all-hole", Rect(10, 0, 20, 10), Rect(10.1, .1, 19.9, 9.9)), Building("house", Rect(40, 0, 44, 4))));
            Check(r.TooSmall == 2 && r.Added == 1 && r.Candidates == 3, "Net area threshold did not subtract holes");
        });
        yield return ("overture courtyard survives merge and allows an independent building inside hole", () => {
            var outer = Building("courtyard", Rect(0, 0, 40, 40), Rect(10, 10, 30, 30)); outer["properties"]!["building"] = "yes";
            var r = Merge(Collection(outer), Collection(Building("in-courtyard", Rect(15, 15, 20, 20)), Building("other-courtyard", Rect(60, 0, 90, 30), Rect(65, 5, 85, 25))));
            Check(r.Added == 2 && r.Existing == 0, "A courtyard was treated as solid building area");
            Check(Features(r.Json).Last()!["geometry"]!["coordinates"]!.AsArray().Count == 2, "Overture hole disappeared");
        });
        yield return ("overture boundary selection includes crossing footprint and excludes disjoint footprint", () => {
            var r = Merge(Empty, Collection(Building("edge", Rect(-100, 10, 2, 20)), Building("outside", Rect(-50, 30, -40, 40)), Building("surrounds", Rect(-20, -20, 120, 120), Rect(-10, -10, 110, 110))), Bounds(0, 0, 100, 100));
            Check(r.Added == 1 && r.Outside == 2, "Boundary crossing or bounds inside a hole misclassified");
            Check(Features(r.Json)[0]!["geometry"]!["coordinates"]![0]![0]![0]!.GetValue<double>() < 0, "Source footprint was destructively truncated");
        });
        yield return ("overture copies only supplied levels height name and retains provider metadata", () => {
            var feature = Building("attributed", Rect(0, 0, 20, 20));
            feature["properties"]!["class"] = "commercial"; feature["properties"]!["num_floors"] = 3; feature["properties"]!["height"] = 9.5;
            feature["properties"]!["names"] = new JsonObject { ["primary"] = "Shop" };
            feature["properties"]!["sources"] = new JsonArray(new JsonObject { ["dataset"] = "Provider", ["record_id"] = "abc", ["confidence"] = .7 });
            var r = Merge(Empty, Collection(feature)); var p = Features(r.Json)[0]!["properties"]!;
            Check(p["source:attribution"]!.ToString() == "Overture Maps contributors" && p["source"]!.ToString() == "Overture Maps", "Attribution missing");
            Check(p["source:license"]!.ToString() == "ODbL-1.0" && r.Warnings.Any(w => w.Contains("roofprint", StringComparison.OrdinalIgnoreCase)), "Buildings-theme license or roofprint limitation missing");
            Check(p["building:levels"]!.ToString() == "3" && p["height"]!.ToString() == "9.5" && p["name"]!.ToString() == "Shop", "Supplied attributes lost");
            Check(JsonNode.DeepEquals(p["sources"], feature["properties"]!["sources"]) && p["id"]!.ToString() == "attributed", "Provider metadata or raw ID lost");
            Check(p["confidence"] is null && p["interior"] is null && p["pz:evidence"] is null, "Invented building evidence");
            var plain = Features(Merge(Empty, Collection(Building("plain", Rect(0, 0, 10, 10)))).Json)[0]!["properties"]!;
            Check(plain["building:levels"] is null && plain["height"] is null && plain["name"] is null, "Unsupplied attributes invented");
        });
        yield return ("overture multipart identifiers are stable distinct and remerge without duplicates", () => {
            var f = Building("multi", Rect(0, 0, 10, 10));
            f["geometry"] = new JsonObject { ["type"] = "MultiPolygon", ["coordinates"] = new JsonArray(new JsonArray(Rect(0, 0, 10, 10), Rect(2, 2, 5, 5)), new JsonArray(Rect(30, 0, 40, 10))) };
            var input = Collection(f); var a = Merge(Empty, input); var b = Merge(a.Json, input);
            Check(a.Candidates == 2 && a.Added == 2 && b.Existing == 2 && b.Added == 0, "Multipart remerge duplicated parts");
            var ids = Features(a.Json).Select(n => n!["properties"]!["pz:source_id"]!.ToString()).ToArray();
            Check(ids[0].StartsWith("overture-", StringComparison.Ordinal) && ids[0].EndsWith("-part-0", StringComparison.Ordinal) && ids[1].EndsWith("-part-1", StringComparison.Ordinal), "Stable part IDs missing");
            Check(Features(a.Json)[0]!["geometry"]!["coordinates"]!.AsArray().Count == 2, "Multipart hole lost");
            Check(a.Json == Merge(Empty, input).Json, "Output is nondeterministic");
        });
        yield return ("overture accepts exported feature id fallback while preferring official properties id", () => {
            var official = Building("exported", Rect(0, 0, 20, 20)); official["id"] = "different-container-id";
            var exported = Building("exported", Rect(0, 0, 20, 20)); exported["properties"]!.AsObject().Remove("id"); exported["id"] = "exported";
            var a = Features(Merge(Empty, Collection(official)).Json).Single()!["properties"]!;
            var b = Features(Merge(Empty, Collection(exported)).Json).Single()!["properties"]!;
            Check(a["pz:source_id"]!.ToString() == b["pz:source_id"]!.ToString() && b["overture:id"]!.ToString() == "exported", "Export fallback lost source ID or overrode official properties.id");
        });
        yield return ("overture candidate dedup keeps adjacent buildings and removes repeated geometry", () => {
            var r = Merge(Empty, Collection(Building("a", Rect(0, 0, 10, 10)), Building("alias", Rect(0, 0, 10, 10)), Building("b", Rect(10, 0, 20, 10))));
            Check(r.Added == 2 && r.Existing == 1, "Repeated footprint added or shared wall treated as overlap");
        });
        yield return ("overture diagonal shared wall with overlapping envelopes is not a duplicate", () => {
            var r = Merge(Empty, Collection(Building("lower", Ring((0, 0), (20, 0), (0, 20), (0, 0))), Building("upper", Ring((20, 0), (20, 20), (0, 20), (20, 0)))));
            Check(r.Added == 2 && r.Existing == 0, "Shared diagonal boundary was counted as filled-area overlap");
        });
        yield return ("overture deduplicates coincident courtyard outlines even with reversed ring winding", () => {
            var a = Building("a", Rect(0, 0, 20, 20), Rect(2, 2, 18, 18));
            var b = Building("b", new JsonArray(Rect(0, 0, 20, 20).Reverse().Select(n => n!.DeepClone()).ToArray()), Rect(2, 2, 18, 18));
            var r = Merge(Empty, Collection(a, b));
            Check(r.Added == 1 && r.Existing == 1, "A courtyard centroid in a hole hid coincident filled building area");
        });
        yield return ("overture never replaces a baseline source identity even if its new geometry moved", () => {
            var input = Collection(Building("same-source", Rect(0, 0, 10, 10)));
            var original = Merge(Empty, input);
            var updated = Merge(original.Json, Collection(Building("same-source", Rect(50, 50, 60, 60))));
            Check(updated.Added == 0 && updated.Existing == 1 && JsonNode.DeepEquals(JsonNode.Parse(original.Json), JsonNode.Parse(updated.Json)), "Existing baseline source was overwritten");
        });
        yield return ("overture filtered multipart keeps original part numbers and source elevation", () => {
            var f = Building("filtered", Rect(0, 0, 10, 10));
            var elevated = Rect(40, 40, 50, 50); foreach (var p in elevated) p!.AsArray().Add(12);
            f["geometry"] = new JsonObject { ["type"] = "MultiPolygon", ["coordinates"] = new JsonArray(new JsonArray(Rect(0, 0, 1, 1)), new JsonArray(elevated)) };
            var r = Merge(Empty, Collection(f)); var retained = Features(r.Json).Single()!;
            Check(r.Candidates == 2 && r.TooSmall == 1 && r.Added == 1 && retained["properties"]!["pz:source_id"]!.ToString().EndsWith("-part-1", StringComparison.Ordinal), "Filtering renumbered parts");
            Check(retained["geometry"]!["coordinates"]![0]![0]!.AsArray().Count == 3, "Source elevation was discarded");
        });
        yield return ("overture merged source imports through regular importer with source identity and courtyard", () => {
            var r = Merge(Empty, Collection(Building("regular-import", Rect(-100, -100, -50, -50), Rect(-90, -90, -70, -70))));
            var imported = GeographicImporter.Import(r.Json, MapGenerator.Generate(new() { Kind = "blank" }), Bounds());
            ProjectValidator.Validate(imported.Project);
            var source = imported.Project.SourceFeatures.Single();
            Check(source.SourceId == Features(r.Json)[0]!["properties"]!["pz:source_id"]!.ToString() && source.SourceGeometry.Count == 2, "Regular importer changed source identity or courtyard");
            Check(imported.Project.Attribution.Contains("Overture Maps contributors"), "Regular importer lost Overture credit");
        });
        yield return ("overture rejects malformed incomplete geometry atomically", () => {
            var variants = new List<JsonObject>();
            var open = Building("open", Ring((0, 0), (10, 0), (10, 10), (0, 10))); variants.Add(open);
            variants.Add(Building("bowtie", Ring((0, 0), (10, 10), (0, 10), (10, 0), (0, 0))));
            variants.Add(Building("bad-hole", Rect(0, 0, 10, 10), Rect(20, 20, 25, 25)));
            variants.Add(Building("overlapping-holes", Rect(0, 0, 20, 20), Rect(2, 2, 10, 10), Rect(5, 5, 15, 15)));
            var incomplete = Building("partial-multi", Rect(0, 0, 10, 10));
            incomplete["geometry"] = new JsonObject { ["type"] = "MultiPolygon", ["coordinates"] = new JsonArray(new JsonArray(Rect(0, 0, 10, 10)), new JsonArray()) }; variants.Add(incomplete);
            var overlap = Building("invalid-multi", Rect(0, 0, 10, 10));
            overlap["geometry"] = new JsonObject { ["type"] = "MultiPolygon", ["coordinates"] = new JsonArray(new JsonArray(Rect(0, 0, 10, 10)), new JsonArray(Rect(5, 5, 15, 15))) }; variants.Add(overlap);
            var badCoordinate = Building("coordinate", Rect(0, 0, 10, 10)); badCoordinate["geometry"]!["coordinates"]![0]![0]![0] = "oops"; variants.Add(badCoordinate);
            var noId = Building("no-id", Rect(0, 0, 10, 10)); noId["properties"]!.AsObject().Remove("id"); variants.Add(noId);
            var noGeometry = Building("missing", Rect(0, 0, 10, 10)); noGeometry["geometry"] = null; variants.Add(noGeometry);
            foreach (var bad in variants) Reject<ArgumentException>(() => Merge(Empty, Collection(Building("valid", Rect(40, 40, 50, 50)), bad)));
            Reject<ArgumentException>(() => Merge(Empty, "{}"));
            Reject<ArgumentException>(() => Merge(Empty, "not json"));
            Reject<ArgumentException>(() => Merge(Empty, "{\"type\":7,\"features\":[]}"));
        });
        yield return ("overture honors cancellation before parsing and bounds validation", () => {
            using var cts = new CancellationTokenSource(); cts.Cancel();
            Reject<OperationCanceledException>(() => GeographicImporter.MergeOvertureBuildings("invalid", "invalid", Bounds(), cts.Token));
            Reject<ArgumentException>(() => Merge(Empty, Empty, new GeoBounds { West = 1, East = 0, South = 0, North = .001 }));
        });
        yield return ("overture rejects building parts underground and raised floors before ground placement", () => {
            foreach (var (key, value) in new (string, JsonNode)[] { ("type", JsonValue.Create("building_part")!), ("is_underground", JsonValue.Create(true)!), ("min_floor", JsonValue.Create(1)!) })
            {
                var f = Building("not-ground", Rect(0, 0, 20, 20)); f["properties"]![key] = value;
                Reject<ArgumentException>(() => Merge(Empty, Collection(f)));
            }
            var ground = Building("ground", Rect(0, 0, 20, 20));
            ground["properties"]!["type"] = "building"; ground["properties"]!["is_underground"] = false; ground["properties"]!["min_floor"] = 0; ground["properties"]!["has_parts"] = true;
            var r = Merge(Empty, Collection(ground));
            Check(r.Added == 1 && Features(r.Json)[0]!["properties"]!["has_parts"]!.GetValue<bool>(), "Ground-level building outline or has_parts metadata lost");
        });
        yield return ("overture caps UTF8 input and geometry work without emitting partial results", () => {
            Reject<ArgumentException>(() => Merge(Empty, Empty + new string(' ', 64_000_001)));
            Reject<ArgumentException>(() => Merge(Empty, "{\"type\":\"FeatureCollection\",\"features\":[],\"note\":\"" + new string('я', 32_000_001) + "\"}"));
            var f = Building("budget", Rect(0, 0, 10, 10));
            var points = new JsonArray(); for (var i = 0; i < 3_000; i++) { var a = 2 * Math.PI * i / 3_000; points.Add(new JsonArray((50 + 20 * Math.Cos(a)) / Metres, (50 + 20 * Math.Sin(a)) / Metres)); }
            points.Add(points[0]!.DeepClone()); f["geometry"]!["coordinates"] = new JsonArray(points);
            Reject<ArgumentException>(() => Merge(Empty, Collection(f)));
        });
    }

    const double Metres = 111320;
    const string Empty = "{\"type\":\"FeatureCollection\",\"features\":[]}";
    static OvertureSupplementResult Merge(string baseline, string supplement, GeoBounds? bounds = null) => GeographicImporter.MergeOvertureBuildings(baseline, supplement, bounds ?? Bounds());
    static GeoBounds Bounds(double x = -200, double y = -200, double right = 200, double top = 200) => new() { West = x / Metres, South = y / Metres, East = right / Metres, North = top / Metres };
    static JsonArray Rect(double x, double y, double right, double top) => Ring((x, y), (right, y), (right, top), (x, top), (x, y));
    static JsonArray Ring(params (double X, double Y)[] points) => new(points.Select(p => (JsonNode)new JsonArray(p.X / Metres, p.Y / Metres)).ToArray());
    static JsonObject Building(string id, params JsonArray[] rings) => new() { ["type"] = "Feature", ["properties"] = new JsonObject { ["id"] = id }, ["geometry"] = new JsonObject { ["type"] = "Polygon", ["coordinates"] = new JsonArray(rings.Cast<JsonNode>().ToArray()) } };
    static string Collection(params JsonObject[] features) => new JsonObject { ["type"] = "FeatureCollection", ["features"] = new JsonArray(features.Cast<JsonNode>().ToArray()) }.ToJsonString();
    static JsonArray Features(string json) => JsonNode.Parse(json)!["features"]!.AsArray();
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
