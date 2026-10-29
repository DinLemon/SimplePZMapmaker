using System.Text.Json;
using SimplePZMapmaker.Core;

public static class PedestrianImportTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static MapProject Import(string profile = "default", string? width = null, bool bridge = false, bool surface = false)
    {
        var reference = new WorldGeoReference { Bounds = new() { West = 45.92, East = 45.93, South = 55.54, North = 55.55 }, MetresPerTile = 1, OriginCellX = 100, OriginCellY = 100 };
        var projection = new GeographicProjection(reference, 100, 100);
        var tags = new Dictionary<string, string> { ["highway"] = "steps", ["incline"] = "down" };
        if (width is not null) tags["width"] = width;
        if (bridge) { tags["bridge"] = "yes"; tags["layer"] = "1"; }
        if (surface) tags["surface"] = "concrete";
        var points = new[] { projection.Geographic(60, 60), projection.Geographic(60, 90) }.Select(p => new[] { p.Longitude, p.Latitude }).ToArray();
        var json = JsonSerializer.Serialize(new { type = "FeatureCollection", features = new[] { new { type = "Feature", id = "stairs", properties = tags, geometry = new { type = "LineString", coordinates = points } } } });
        return GeographicImporter.Import(json, MapGenerator.Generate(new() { Kind = "blank" }), reference.Bounds, projection: projection, regionalProfile: profile).Project;
    }
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("pedestrian steps import as a narrow path with retained source geometry", () => {
            foreach (var profile in new[] { "default", "ru-rural" }) {
                var p = Import(profile); var line = p.LinearFeatures.Single();
                Check(line.Kind == "sidewalk" && Math.Abs(line.Width - 2) < 1e-9, "Steps became a six metre vehicle road");
                Check(line.SourceId == "geo-stairs" && line.SourcePath.Count == 2 && line.SourceTags["incline"] == "down", "Steps evidence was rewritten");
                Check(!line.SourceTags.ContainsKey("surface") && line.Level == 0 && line.Access == "none", "Importer invented surface or surveyed elevation");
                var expected = profile == "default" ? Terrain.Pavement : Terrain.Dirt;
                Check(p.Terrain[75 * p.Width + 60] == expected, "Pedestrian profile was not rendered");
            }
        });
        yield return ("pedestrian steps honour measured width surface and independent bridge role", () => {
            var p = Import(width: "3.5 m", bridge: true, surface: true); var line = p.LinearFeatures.Single();
            Check(line.Kind == "bridge" && Math.Abs(line.Width - 3.5) < 1e-9 && line.SourceTags["surface"] == "concrete", "Explicit source values or bridge role lost");
            Check(line.Level == 0 && line.SourceTags["layer"] == "1", "OSM layer incorrectly treated as a measured floor");
            var unmeasured = Import(bridge: true).LinearFeatures.Single();
            Check(unmeasured.Kind == "bridge" && unmeasured.Width == 2, "Pedestrian bridge width default became a road width");
        });
        yield return ("pedestrian steps surface defaults remain inferred and explicit materials win", () => {
            foreach (var profile in new[] { "default", "ru-rural" }) {
                var inferred = TransferRegionalProfiles.ExplainRoad(new Dictionary<string, string> { ["highway"] = "steps" }, profile);
                Check(inferred.Terrain == (profile == "default" ? Terrain.Pavement : Terrain.Dirt) && inferred.IsInferred && !inferred.SourceTags.ContainsKey("surface"), "Steps use vehicle surface defaults");
                var explicitMaterial = TransferRegionalProfiles.ExplainRoad(new Dictionary<string, string> { ["highway"] = "steps", ["surface"] = "concrete" }, profile);
                Check(explicitMaterial.Terrain == Terrain.Road && !explicitMaterial.IsInferred, "Explicit concrete surface was ignored");
            }
        });
    }
}
