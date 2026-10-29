using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Diagnostics;
using SimplePZMapmaker.Core.Exchange;
using SimplePZMapmaker.Core.Export;

public static class TransferB42Tests
{
    const string Tile = "furniture_storage_01_0";
    const string Tbx = """
        <building version="4" width="2" height="2">
          <tile_entry category="floors"><tile enum="Floor" tile="floors_interior_tilesandwood_01_40"/></tile_entry>
          <room Name="bedroom" InternalName="bedroom" Color="100 120 140" Floor="1"/>
          <floor><rooms>1,1,1,1</rooms></floor>
        </building>
        """;

    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("B42 intensity native bytes use 0..10 with x-major chunks and preserve editor raster", () =>
        {
            var p = MapGenerator.Generate(new() { Kind = "blank", Size = 512 });
            p.WorldX = 110; p.WorldY = 120;
            var house = new Building { Id = "transfer-house", X = 40, Y = 40, Width = 8, Height = 6 };
            house.Design = BuildingDesigner.Generate(house, 1, 42); house.Design.Roof = "flat"; p.Buildings.Add(house);
            for (var i = 0; i < p.Zombies.Length; i++) p.Zombies[i] = i % 256;
            var before = (int[])p.Zombies.Clone();
            var archive = NativeMapExporter.Export(p);
            using var zip = new ZipArchive(new MemoryStream(archive));
            for (var cy = 0; cy < 2; cy++) for (var cx = 0; cx < 2; cx++)
            {
                var tail = Tail(zip, $"{110 + cx}_{120 + cy}.lotheader");
                for (var x = 0; x < 32; x++) for (var y = 0; y < 32; y++)
                {
                    var density = before[(cy * 32 + y) * 64 + cx * 32 + x];
                    // Independent integer ceiling: density 0 remains zero; 1..25 => 1; 26..51 => 2; 255 => 10.
                    var expected = density == 0 ? 0 : (int)Math.Ceiling(density / 25.5);
                    Check(tail[x * 32 + y] == expected, $"Wrong native intensity at {cx},{cy}/{x},{y}: {tail[x * 32 + y]} != {expected}.");
                }
            }
            Check(p.Zombies.SequenceEqual(before), "Export changed the editable raster.");
            using var source = JsonDocument.Parse(zip.Entries.Single(e => e.Name == "project.pzmap.json").Open());
            Check(source.RootElement.GetProperty("zombies").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(before), "Archive source was silently rescaled.");
            using var notice = new StreamReader(zip.Entries.Single(e => e.Name == "INSTALL.txt").Open());
            Check(notice.ReadToEnd().Contains("0..10"), "Native conversion notice is missing.");
            if (Environment.GetEnvironmentVariable("TRANSFER_B42_ARCHIVE") is { Length: > 0 } path) File.WriteAllBytes(path, archive);
        });
        yield return ("B42 zero map stays empty and native density is monotone over all 256 inputs", () =>
        {
            var p = MapGenerator.Generate(new() { Kind = "blank" }); Array.Clear(p.Zombies);
            using (var zip = new ZipArchive(new MemoryStream(NativeMapExporter.Export(p))))
                Check(Tail(zip, $"{p.WorldX}_{p.WorldY}.lotheader").All(b => b == 0), "Zero density creates native population.");
            for (var i = 0; i < p.Zombies.Length; i++) p.Zombies[i] = i % 256;
            using var populated = new ZipArchive(new MemoryStream(NativeMapExporter.Export(p)));
            var tail = Tail(populated, $"{p.WorldX}_{p.WorldY}.lotheader");
            var values = Enumerable.Range(0, 256).Select(i => tail[(i % 32) * 32 + i / 32]).ToArray();
            Check(values[0] == 0 && values[^1] == 10 && values.Skip(1).All(b => b is >= 1 and <= 10), "Native scale endpoints/nonzero policy differ.");
            Check(values.Zip(values.Skip(1)).All(v => v.First <= v.Second), "Native density is not monotone.");
            Check(values[25] == 1 && values[26] == 2 && values[127] == 5 && values[128] == 6 && values[254] == 10, "Native quantization boundaries differ.");
        });
        yield return ("B42 intensity helper rejects invalid raster samples instead of byte wrapping", () =>
        {
            foreach (var value in new[] { -1, 256, int.MinValue, int.MaxValue })
            {
                try { NativeZombieIntensity.FromEditor(value); }
                catch (ArgumentOutOfRangeException) { continue; }
                throw new Exception("Out-of-range raster sample was silently converted.");
            }
        });
        yield return ("B42 exchange multipart furniture preserves valid tile coordinates", () =>
        {
            var b = Building(4, 3); var f = b.Design!.Floors[0];
            f.Objects.Add(new() { Kind = "asset", X = 1, Y = 1, Asset = Tile, Parts = [new(0, 0, Tile), new(1, 0, "furniture_storage_01_1")] });
            foreach (var format in new[] { "tbx", "tmx" })
            {
                var xml = Xml(BuildingExchange.Export(b, format, true));
                var copy = Imported(BuildingExchange.Import(xml, format));
                Check(copy.Design!.Floors[0].Objects.Select(o => (o.X, o.Y, o.Asset)).ToHashSet().SetEquals([(1, 1, Tile), (2, 1, "furniture_storage_01_1")]), "Furniture wrapped or moved.");
            }
        });
        yield return ("B42 exchange rejects exported multipart overhangs even with allowLossy", () =>
        {
            foreach (var (x, y, dx, dy) in new[] { (1, 0, 1, 0), (0, 1, 0, 1), (0, 0, 7, 0) })
            {
                var b = Building(2, 2); b.Design!.Floors[0].Objects.Add(new() { Kind = "asset", X = x, Y = y, Asset = Tile, Parts = [new(0, 0, Tile), new(dx, dy, "furniture_storage_01_1")] });
                var before = JsonSerializer.Serialize(b);
                foreach (var format in new[] { "tbx", "tmx" }) Error(BuildingExchange.Export(b, format, true));
                Check(JsonSerializer.Serialize(b) == before, "Failed export changed source.");
            }
        });
        yield return ("B42 TBX furniture negative anchors and parts outside envelope are hard errors", () =>
        {
            foreach (var (x, y, px, py) in new[] { (-1, 0, 0, 0), (0, 0, -1, 0), (1, 0, 1, 0), (0, 1, 0, 1), (2, 0, 0, 0) })
            {
                var xml = Tbx.Replace("<room ", $"<furniture layer=\"Furniture\"><entry orient=\"W\"><tile x=\"{px}\" y=\"{py}\" name=\"{Tile}\"/></entry></furniture><room ")
                    .Replace("<rooms>", $"<object type=\"furniture\" x=\"{x}\" y=\"{y}\" orient=\"W\" FurnitureTiles=\"0\"/><rooms>");
                Error(BuildingExchange.Import(xml, "tbx", true));
            }
        });
        yield return ("B42 TBX reserved wall border cannot contain furniture even with allowLossy", () =>
        {
            var xml = Tbx.Replace("<room ", $"<user_tiles><tile tile=\"{Tile}\"/></user_tiles><room ")
                .Replace("</floor>", "<tiles layer=\"Furniture\">0,0,1,0,0,0,0,0,0</tiles></floor>");
            Error(BuildingExchange.Import(xml, "tbx", true));
        });
        yield return ("B42 TMX furniture in reserved padding is also a hard import error", () =>
        {
            var root = XElement.Parse(Xml(BuildingExchange.Export(Building(2, 2), "tmx")));
            root.Add(new XElement("layer", new XAttribute("name", "Furniture"), new XAttribute("level", 0), new XAttribute("width", 3), new XAttribute("height", 3),
                new XElement("data", new XAttribute("encoding", "csv"), "0,0,1,0,0,0,0,0,0")));
            Error(BuildingExchange.Import(root.ToString(), "tmx", true));
        });
        yield return ("B42 BuildingEd rectangular flat roofs retain native FlatTop Three semantics", () =>
        {
            foreach (var levels in new[] { 1, 31 })
            {
                var b = Building(2, 2, levels); b.Design!.Roof = "flat"; b.Design.FlatRoofTile = "floors_interior_tilesandwood_01_41";
                var before = JsonSerializer.Serialize(b);
                var xml = Xml(BuildingExchange.Export(b, "tbx")); var root = XElement.Parse(xml);
                var roof = root.Elements("floor").Last().Elements("object").Single(e => (string?)e.Attribute("type") == "roof");
                Check((string?)roof.Attribute("RoofType") == "FlatTop" && (string?)roof.Attribute("Depth") == "Three" && (string?)roof.Attribute("width") == "2", "Roof level/extent semantics differ from BuildingEd.");
                var copy = Imported(BuildingExchange.Import(xml, "tbx"));
                Check(copy.Design!.Roof == "flat" && copy.Design.FlatRoofTile == b.Design.FlatRoofTile && RoofGeometry.MaxLevel(copy) == levels, "Roof surface/headroom lost on import.");
                Check(Xml(BuildingExchange.Export(copy, "tbx")) == xml && JsonSerializer.Serialize(b) == before, "Flat roof roundtrip changed source or XML.");
                if (Environment.GetEnvironmentVariable("TRANSFER_B42_ARTIFACTS") is { Length: > 0 } folder) File.WriteAllText(Path.Combine(folder, $"flat-{levels}-floors.tbx"), xml);
            }
        });
        yield return ("B42 native roof headroom rejects z32 without rewriting the requested roof", () =>
        {
            var b = Building(2, 2, 32); b.Design!.Roof = "flat";
            var before = JsonSerializer.Serialize(b);
            Error(BuildingExchange.Export(b, "tbx", true));
            Check(JsonSerializer.Serialize(b) == before, "Roof validation rewrote the tower.");
        });
        yield return ("B42 independent native flat roof fixture imports without a custom metadata extension", () =>
        {
            var xml = Tbx.Replace("<room ", """
                <tile_entry category="roof_tops"><tile enum="West1" tile="floors_interior_tilesandwood_01_41"/><tile enum="West2" tile="floors_interior_tilesandwood_01_41"/><tile enum="West3" tile="floors_interior_tilesandwood_01_41"/><tile enum="North1" tile="floors_interior_tilesandwood_01_41"/><tile enum="North2" tile="floors_interior_tilesandwood_01_41"/><tile enum="North3" tile="floors_interior_tilesandwood_01_41"/></tile_entry><room
                """ + " ").Replace("</floor>", "<object type=\"roof\" x=\"0\" y=\"0\" width=\"2\" height=\"2\" RoofType=\"FlatTop\" Depth=\"Three\" CapTiles=\"0\" SlopeTiles=\"0\" TopTiles=\"2\" cappedW=\"false\" cappedN=\"false\" cappedE=\"false\" cappedS=\"false\"/></floor>");
            var b = Imported(BuildingExchange.Import(xml, "tbx"));
            Check(b.Design!.Roof == "flat" && b.Design.FlatRoofTile == "floors_interior_tilesandwood_01_41" && RoofGeometry.MaxLevel(b) == 1, "Independent roof contract was not retained.");
            var partial = xml.Replace("width=\"2\" height=\"2\" RoofType", "width=\"1\" height=\"2\" RoofType");
            var rejected = BuildingExchange.Import(partial, "tbx");
            Check(!rejected.Success && rejected.Diagnostics.Any(d => d.Code == "roof"), "Partial roof silently expanded to full coverage.");
        });
        yield return ("B42 unsupported roof slopes and west overlay mirroring are explicit and leave source intact", () =>
        {
            var b = Building(2, 2); b.Design!.Roof = "gabled";
            var before = JsonSerializer.Serialize(b);
            Check(!BuildingExchange.Export(b, "tbx").Success, "Slope silently discarded.");
            b.Design.Roof = "none"; b.Design.Floors[0].Edges.Add(new() { X = 0, Y = 0, Edge = "w", OverlayTile = "walls_exterior_house_01_0" });
            before = JsonSerializer.Serialize(b);
            foreach (var format in new[] { "tbx", "tmx" })
            {
                var result = BuildingExchange.Export(b, format);
                Check(!result.Success && result.Diagnostics.Any(d => d.Code == "wall-overlay" && d.Message.Contains("mirror", StringComparison.OrdinalIgnoreCase)), "West mirror limitation hidden.");
            }
            Check(JsonSerializer.Serialize(b) == before, "Unsupported overlay mutated source.");
        });
        yield return ("B42 partial and overhanging TBX roofs are reported instead of silently imported", () =>
        {
            var xml = Tbx.Replace("</floor>", "<object type=\"roof\" x=\"1\" y=\"0\" width=\"2\" height=\"2\" RoofType=\"FlatTop\" Depth=\"Three\" CapTiles=\"0\" SlopeTiles=\"0\" TopTiles=\"0\" cappedW=\"false\" cappedN=\"false\" cappedE=\"false\" cappedS=\"false\"/></floor>");
            Error(BuildingExchange.Import(xml, "tbx", true));
        });
        yield return ("B42 imported west overlay layers never become ordinary furniture", () =>
        {
            var layers = Tbx.Replace("<room ", $"<user_tiles><tile tile=\"{Tile}\"/></user_tiles><room ")
                .Replace("</floor>", "<tiles layer=\"WallOverlay\">1,0,0,0,0,0,0,0,0</tiles></floor>");
            var furniture = Tbx.Replace("<room ", $"<furniture layer=\"WallOverlay\"><entry orient=\"W\"><tile x=\"0\" y=\"0\" name=\"{Tile}\"/></entry></furniture><room ")
                .Replace("<rooms>", "<object type=\"furniture\" x=\"0\" y=\"0\" orient=\"W\" FurnitureTiles=\"0\"/><rooms>");
            foreach (var xml in new[] { layers, furniture })
            {
                var strict = BuildingExchange.Import(xml, "tbx");
                Check(!strict.Success && strict.Diagnostics.Any(d => d.Code == "wall-overlay"), "Overlay/mirror loss was not reported.");
                var lossy = BuildingExchange.Import(xml, "tbx", true);
                Check(Imported(lossy).Design!.Floors[0].Objects.Count == 0 && lossy.Diagnostics.Any(d => d.Code == "wall-overlay"), "Wall overlay became unmirrored furniture.");
            }
        });
        yield return ("B42 house spawns use free indoor ground tiles and retain dry authored fallback", () =>
        {
            var b = Building(4, 3); b.X = 20; b.Y = 30;
            b.Design!.Floors[0].Objects.Add(new() { Kind = "asset", X = 1, Y = 1, Asset = Tile, Parts = [new(0, 0, Tile)] });
            var plan = new SpawnPlanner.Plan(); plan.Add([b], 2560, 5120);
            var home = plan.Homes.Single(); var floor = b.Design.Floors[0];
            Check(home.Z == 0 && BuildingDesigner.Cell(b, floor, home.X - 2560 - b.X, home.Y - 5120 - b.Y) != '.' && home != new SpawnPlanner.Point(2581, 5151, 0), "House spawn not free/interior.");
            var p = MapGenerator.Generate(new() { Kind = "blank" });
            var fallback = new SpawnPlanner.Plan(); fallback.AddAuthored(p);
            Check(fallback.Authored.Count > 0 && fallback.Authored.All(s => p.Terrain[(s.Y - p.WorldY * 256) * p.Width + s.X - p.WorldX * 256] != Terrain.Water), "Fallback is not dry.");
        });
        yield return ("B42 doctor never treats unavailable native resources and tile baseline as validated", () =>
        {
            var report = ProjectDoctor.Analyze(new() { Project = MapGenerator.Generate(new() { Kind = "blank" }) });
            Check(!report.Complete && report.Checks.Any(c => c.Category == "resources" && c.Status == "unavailable") && report.Checks.Any(c => c.Category == "tile-compatibility" && c.Status == "unavailable"), "Missing runtime evidence presented as complete.");
        });
    }

    static Building Building(int width, int height, int levels = 1) => new()
    {
        Width = width, Height = height, Design = new() { Roof = "none", Floors = Enumerable.Range(0, levels).Select(_ => new BuildingFloor { Cells = new('0', width * height), Rooms = new() { ["0"] = "bedroom" } }).ToList() }
    };
    static byte[] Tail(ZipArchive zip, string suffix) { using var s = zip.Entries.Single(e => e.FullName.EndsWith("/" + suffix, StringComparison.Ordinal)).Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray()[^1024..]; }
    static string Xml(BuildingExchangeResult result) => result.Xml ?? throw new Exception(string.Join("; ", result.Diagnostics.Select(d => d.Message)));
    static Building Imported(BuildingExchangeResult result) => result.Building ?? throw new Exception(string.Join("; ", result.Diagnostics.Select(d => d.Message)));
    static void Error(BuildingExchangeResult result) => Check(!result.Success && result.Diagnostics.Any(d => d.Severity == "error"), "Invalid coordinates were accepted or only warned.");
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
