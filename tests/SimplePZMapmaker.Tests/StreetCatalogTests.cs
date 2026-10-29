using System.Text;
using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;

public static class StreetCatalogTests
{
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("street catalog pinned upstream directional mappings", () =>
        {
            var catalog = Load(); var d = catalog.Snapshot();
            Check(d.SchemaVersion == 1 && d.Source.Commit == "70e8b77676183e579cf9f1f7100dff6c383e28cb", "Unpinned provenance");
            var curb = d.Entries.Single(e => e.Id == "street-curb");
            Check(curb.Variants.Count == 24, "Missing corner/sunken curb entries");
            foreach (var (variant, tile) in new[] { ("near.e", 9), ("near.s", 8), ("far.e", 11), ("far.s", 10),
                ("near.se", 0), ("far.join_se", 3), ("near.sunken.e", 43), ("far.sunken.w", 40) })
                Check(curb.Variants[variant].Single().Tile == "street_curbs_01_" + tile, "Wrong curb " + variant);
            var drain = d.Entries.Single(e => e.Id == "drainage-grate");
            foreach (var (direction, tile) in new[] { ("w", 13), ("n", 14), ("e", 30), ("s", 31) })
                Check(drain.Variants[direction].Single().Tile == "street_decoration_01_" + tile, "Wrong drain " + direction);
            d.Rules.Clear();
            Check(catalog.Snapshot().Rules.Count == 8, "Catalog snapshot leaked mutable state");
        });
        yield return ("street catalog retains signed parking offsets and source layers", () =>
        {
            var variants = Load().Snapshot().Entries.Single(e => e.Id == "parking-stall").Variants;
            foreach (var direction in new[] { "n", "s", "w", "e" })
            {
                Check(variants[direction].Count == 8, "Incomplete source stall");
                Check(variants[direction].Count(p => p.Layer == "0_Furniture") == 3, "Missing wheel stops");
                Check(variants[direction].Count(p => p.Layer == "0_FloorOverlay5") == 5, "Missing original lines layer");
            }
            Check(variants["e"][3] == new StreetCatalogPart(-4, 0, "street_trafficlines_01_2", "0_FloorOverlay5"), "East anchor was mirrored/guessed");
            Check(variants["s"][3] == new StreetCatalogPart(0, -4, "street_trafficlines_01_0", "0_FloorOverlay5"), "South anchor was mirrored/guessed");
            Check(variants["w"].Take(3).Select(p => p.Tile).SequenceEqual(new[] { "street_decoration_01_37", "street_decoration_01_36", "street_decoration_01_35" }), "West bumper order");
        });
        yield return ("street catalog rejects unsupported versions invalid references and unbounded rules", () =>
        {
            foreach (var edit in new Action<StreetCatalogDocument>[]
            {
                d => d.SchemaVersion = 2, d => d.Revision = 0, d => d.Source.Commit = "main",
                d => d.Entries.Add(d.Entries[0]), d => d.Rules.Add(d.Rules[0]),
                d => d.Rules[0].Entry = "missing", d => d.Rules[0].Variant = "missing",
                d => d.Rules[0].Neighbors.Add(new(2, 0, "Road")), d => d.Rules[0].Every = 0,
                d => d.Rules[0].Neighbors = null!, d => d.Entries[0].Variants["bad"] = [new(0, 0, "bad", "0_Furniture")],
                d => d.Entries[0].Variants["bad"] = [new(0, 0, "street_curbs_01_0", "1_Furniture")],
                d => d.Source.Files = ["../tool.lua"]
            })
            {
                var d = Load().Snapshot(); edit(d); Reject(() => StreetCatalog.FromDocument(d));
            }
            var original = Load();
            using var unchanged = Bytes(original.Snapshot()); Reject(() => original.Update(unchanged));
            var update = original.Snapshot(); update.Revision++;
            using var input = Bytes(update); Check(original.Update(input).Snapshot().Revision == 2, "Valid update rejected");
        });
        yield return ("street catalog bounded nonseek stream strict duplicates and unknown fields", () =>
        {
            using var tooLarge = new NonSeekable(new byte[StreetCatalog.MaximumBytes + 1]);
            Reject(() => StreetCatalog.Load(tooLarge));
            foreach (var raw in new[] { "{\"schemaVersion\":1,\"SchemaVersion\":2}", "{\"schemaVersion\":1,\"execute\":\"lua()\"}", "null", "[]" })
            {
                using var input = new MemoryStream(Encoding.UTF8.GetBytes(raw)); Reject(() => StreetCatalog.Load(input));
            }
            using var source = Bytes(Load().Snapshot()); using var nonSeekable = new NonSeekable(source.ToArray());
            Check(StreetCatalog.Load(nonSeekable).Snapshot().Rules.Count == 8 && nonSeekable.CanRead, "Nonseekable source rejected or closed");
        });
        yield return ("street automapping preview deterministic additive atomic and exact edges", () =>
        {
            var p = Street(); var before = JsonSerializer.Serialize(p); var catalog = Load();
            var options = new AutomappingOptions { X = 8, Y = 8, Width = 56, Height = 40, Drains = false, Parking = false };
            var a = Automapping.Preview(p, catalog, options, Resource);
            var b = Automapping.Preview(p, catalog, options, Resource);
            Check(JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b) && JsonSerializer.Serialize(p) == before, "Nondeterministic or mutating preview");
            Check(a.Decorations.Any(d => d.X == 20 && d.Y == 19 && d.Asset == "street_curbs_01_8"), "North pavement edge is near.s");
            Check(a.Decorations.Any(d => d.X == 20 && d.Y == 23 && d.Asset == "street_curbs_01_10"), "South road edge is far.s");
            var result = Automapping.Apply(p, catalog, a, Resource); ProjectValidator.Validate(result);
            Check(result.Decorations.Count == a.Decorations.Count && JsonSerializer.Serialize(p) == before, "Apply mutated source");
            Check(result.Terrain.SequenceEqual(p.Terrain) && result.TerrainEdits == p.TerrainEdits, "Automapping repainted base terrain");
            var again = Automapping.Preview(result, catalog, options, Resource);
            Check(again.Decorations.Count == 0, "Repeat preview duplicates or replaces existing tiles");
        });
        yield return ("street automapping all drain orientations and world spacing", () =>
        {
            foreach (var (direction, dx, dy, expected) in new[] { ("w", -1, 0, 13), ("n", 0, -1, 14), ("e", 1, 0, 30), ("s", 0, 1, 31) })
            {
                var p = Blank();
                for (var y = 25; y <= 39; y++) for (var x = 25; x <= 39; x++) p.Terrain[y * p.Width + x] = Terrain.Pavement;
                if (dx != 0) for (var y = 25; y <= 39; y++) p.Terrain[y * p.Width + 32 + dx] = Terrain.Road;
                else for (var x = 25; x <= 39; x++) p.Terrain[(32 + dy) * p.Width + x] = Terrain.Road;
                var plan = Automapping.Preview(p, Load(), new() { X = 25, Y = 25, Width = 15, Height = 15, Curbs = false, Parking = false }, Resource);
                Check(plan.Decorations.Any(d => d.X == 32 && d.Y == 32 && d.Asset == "street_decoration_01_" + expected), "Drain direction " + direction);
            }
        });
        yield return ("street automapping preserves authored cells explicit access and full model footprints", () =>
        {
            var p = Street(); var index = 19 * p.Width + 20;
            p.SurfaceTiles[index] = "blends_street_01_0"; p.Version = 6;
            TerrainEditMask.Mark(p, 19 * p.Width + 21);
            p.StaticModels.Add(new() { X = 25, Y = 19, ModelId = "Base.Test", Profile = "obstacle", Footprint = [new(0, 0), new(1, 0)] });
            var o = new AutomappingOptions { X = 8, Y = 8, Width = 56, Height = 40, Drains = false, Parking = false, ReservedCells = [new(22, 19)] };
            var plan = Automapping.Preview(p, Load(), o, Resource);
            Check(plan.Decorations.All(d => d.Y != 19 || d.X is not (20 or 21 or 22 or 25 or 26)), "Authored/access/model footprint overwritten");
            Check(plan.Skipped.GetValueOrDefault("occupied-authored-or-access") >= 5, "Protection reasons missing");
        });
        yield return ("street automapping directional parking lines and wheel stops", () =>
        {
            foreach (var direction in new[] { "N", "S", "W", "E" }) foreach (var layer in new[] { "lines", "bumpers" })
            {
                var p = Parking(direction); var catalog = Load(); var o = ParkingOptions(layer);
                var plan = Automapping.Preview(p, catalog, o, Resource);
                Check(plan.Placed.GetValueOrDefault("parking") == 1, "Missing full directional stall");
                Check(plan.Decorations.Sum(d => d.Parts.Count) == (layer == "lines" ? 5 : 3), "Wrong selected parking layer");
                var expected = catalog.Snapshot().Entries.Single(e => e.Id == "parking-stall").Variants[direction.ToLowerInvariant()]
                    .Where(part => part.Layer == (layer == "lines" ? "0_FloorOverlay5" : "0_Furniture"))
                    .Select(part => (30 + (direction == "E" ? 4 : 0) + part.Dx, 30 + (direction == "S" ? 4 : 0) + part.Dy, part.Tile)).Order().ToArray();
                var actual = plan.Decorations.SelectMany(d => d.Parts.Select(part => (d.X + part.Dx, d.Y + part.Dy, part.Tile))).Order().ToArray();
                Check(actual.SequenceEqual(expected), "Parking directions/offsets changed");
                ProjectValidator.Validate(Automapping.Apply(p, catalog, plan, Resource));
            }
        });
        yield return ("street automapping parking rejects partial footprint and decoration budget", () =>
        {
            var p = Parking("N"); var o = ParkingOptions("lines");
            o.ReservedCells = [new(32, 34)]; // Blank unpainted cell still belongs to the car footprint.
            var blocked = Automapping.Preview(p, Load(), o, Resource);
            Check(blocked.Decorations.Count == 0 && blocked.Skipped.ContainsKey("parking-footprint-or-access"), "Stall blocks passage");
            o.ReservedCells.Clear(); o.MaximumDecorations = 4;
            var budget = Automapping.Preview(p, Load(), o, Resource);
            Check(budget.Decorations.Count == 0 && budget.Skipped.ContainsKey("decoration-budget"), "Budget created a partial stall");
        });
        yield return ("street automapping endpoints validate independent bumper sprites and native overlays", () =>
        {
            WithAssets(assets =>
            {
                foreach (var direction in new[] { "N", "S", "W", "E" }) foreach (var layer in new[] { "lines", "bumpers" })
                {
                    var p = Parking(direction); var catalog = Load();
                    var plan = AutomappingEndpoints.Preview(new(p, ParkingOptions(layer)), catalog, assets);
                    var expectedCount = layer == "lines" ? 5 : 3;
                    Check(plan.Decorations.Count == expectedCount && plan.Decorations.All(d => d.Parts.Count == 1), "Invented a SpriteGrid object");
                    var result = AutomappingEndpoints.Apply(new(p, plan), catalog, assets);
                    var scene = NativeScene.Cell(result, 28, 28, 10, 10, 0);
                    foreach (var d in plan.Decorations)
                        Check(scene.Sprites.Any(s => s.X == d.X && s.Y == d.Y && s.Tile == d.Asset), "Native composition lost street overlay");
                    AssetRules.Project(result, assets);
                }
            });
        });
        yield return ("street automapping stale catalog map resource and tampered preview rejected", () =>
        {
            var p = Street(); var catalog = Load(); var plan = Automapping.Preview(p, catalog, new() { Parking = false }, Resource);
            Check(plan.Decorations.Count > 0, "Fixture lacks changes");
            var altered = NeighborhoodPlanner.Copy(plan); altered.Decorations[0].Parts[0] = new(0, 0, "street_decoration_01_0");
            Reject(() => Automapping.Apply(p, catalog, altered, Resource));
            Reject(() => Automapping.Apply(p, catalog, plan, _ => new(1)));
            var document = catalog.Snapshot(); document.Revision++;
            Reject(() => Automapping.Apply(p, StreetCatalog.FromDocument(document), plan, Resource));
            p.Terrain[0] = Terrain.Dirt; var before = JsonSerializer.Serialize(p);
            Reject(() => Automapping.Apply(p, catalog, plan, Resource)); Check(JsonSerializer.Serialize(p) == before, "Failed apply changed map");
        });
        yield return ("street automapping custom neighborhood revision and mod ownership", () =>
        {
            var d = Load().Snapshot(); d.Revision++; d.Rules = [new() { Id = "custom-marking", Entry = "user-marking", Variant = "s", Center = "Road", Neighbors = [new(0, 1, "Road")] }];
            d.Entries.Add(new() { Id = "user-marking", Label = "Explicit mod marking", Kind = "marking", Variants = new() { ["s"] = [new(0, 0, "custom_lines_01_7", "0_FloorOverlay5")] } });
            var p = Street(); var catalog = StreetCatalog.FromDocument(d);
            var o = new AutomappingOptions { X = 20, Y = 20, Width = 1, Height = 1, Parking = false };
            var plan = Automapping.Preview(p, catalog, o, _ => new(0, "StreetMod", ["StreetMod"]));
            Check(plan.Decorations.Single().Asset == "custom_lines_01_7", "Custom rule ignored");
            var result = Automapping.Apply(p, catalog, plan, _ => new(0, "StreetMod", ["StreetMod"]));
            Check(result.AssetSources["custom_lines_01_7"] == "StreetMod" && result.RequiredMods.Contains("StreetMod"), "Mod source lost");
            p.Version = 6; p.AssetSources["custom_lines_01_7"] = "OtherMod";
            Reject(() => Automapping.Preview(p, catalog, o, _ => new(0, "StreetMod")));
        });
    }

    private static StreetCatalog Load()
    {
        // Parent may embed this; explicit stream also works in an isolated harness.
        var explicitPath = Environment.GetEnvironmentVariable("PZMAP_STREET_CATALOG");
        if (!string.IsNullOrEmpty(explicitPath)) { using var input = File.OpenRead(explicitPath); return StreetCatalog.Load(input); }
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "src", "SimplePZMapmaker.Web", "wwwroot", "street-catalog.json");
            if (File.Exists(path)) { using var input = File.OpenRead(path); return StreetCatalog.LoadDefault(input); }
        }
        return StreetCatalog.LoadDefault();
    }
    private static MapProject Blank() => MapGenerator.Generate(new() { Kind = "blank" });
    private static MapProject Street()
    {
        var p = Blank();
        for (var y = 17; y <= 26; y++) for (var x = 10; x <= 60; x++) p.Terrain[y * p.Width + x] = y is >= 20 and <= 23 ? Terrain.Road : Terrain.Pavement;
        return p;
    }
    private static MapProject Parking(string direction)
    {
        var p = Blank(); var vertical = direction is "N" or "S";
        var zone = new GameZone { X = 30, Y = 30, Width = vertical ? 3 : 5, Height = vertical ? 5 : 3,
            Type = "ParkingStall", Direction = direction, VehicleDistribution = "parkingstall" };
        p.GameZones.Add(zone); p.Version = 9;
        for (var y = 30; y < 30 + zone.Height; y++) for (var x = 30; x < 30 + zone.Width; x++) p.Terrain[y * p.Width + x] = Terrain.Pavement;
        return p;
    }
    private static AutomappingOptions ParkingOptions(string layer) => new() { X = 28, Y = 28, Width = 10, Height = 10, Curbs = false, Drains = false, ParkingLayer = layer };
    private static AutomappingResource Resource(string _) => new(0);
    private static void WithAssets(Action<GameAssetCatalog> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pz-street-assets-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(game, "media", "texturepacks"));
        try
        {
            var tiles = Load().Snapshot().Entries.SelectMany(e => e.Variants.Values).SelectMany(v => v).Select(p => p.Tile).Distinct();
            var definitions = string.Join("\n", tiles.Select(tile => "// " + tile + "\ntile\n{\n CustomName = Street fixture\n}\n"))
                + string.Join("\n", new[] { "blends_street_01_48", "blends_street_01_0", "blends_natural_01_32" }
                    .Select(tile => "\n// " + tile + "\ntile\n{\n solidfloor = true\n}\n"));
            File.WriteAllText(Path.Combine(game, "media", "newtiledefinitions.tiles.txt"), definitions);
            action(new GameAssetCatalog(Path.Combine(root, "workspace"), game));
        }
        finally
        {
            // This is the unique temporary fixture created above, never a game path.
            var resolved = Path.GetFullPath(root);
            var prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "pz-street-assets-");
            if (resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved)) Directory.Delete(resolved, true);
        }
    }
    private static MemoryStream Bytes(StreetCatalogDocument d) => new(JsonSerializer.SerializeToUtf8Bytes(d));
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected rejection"); }
    private sealed class NonSeekable(byte[] bytes) : Stream
    {
        private readonly MemoryStream source = new(bytes);
        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
    }
}
