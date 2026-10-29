using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;
using SimplePZMapmaker.Web.Services;

public static class TransferRegionalTests
{
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("regional emergency preview is pure and applies a functional shelter", () =>
        {
            var project = MapGenerator.Generate(new() { Kind = "blank" });
            var before = JsonSerializer.Serialize(project);
            var options = new StreetEnsembleOptions { X = 20, Y = 20, Width = 100, Height = 90, Preset = "emergency-shelter" };
            var plan = StreetEnsembles.Create(project, options, []);
            Check(plan.Buildings.Any(b => b.Kind == "school"), "PVR has no school");
            Check(plan.AccessPaths.Count > 1, "Facilities have no pedestrian routes");
            Check(JsonSerializer.Serialize(project) == before, "Preview mutated the source");
            var applied = StreetEnsembles.Apply(project, plan);
            ProjectValidator.Validate(applied);
            Check(applied.Buildings.Count > project.Buildings.Count, "Apply discarded the shelter");
            Check(JsonSerializer.Serialize(project) == before, "Apply mutated the source");
        });
        yield return ("regional explicit OSM surfaces override every road class and preserve evidence", () =>
        {
            foreach (var profile in new[] { "default", "ru-rural" }) foreach (var highway in new[] { "residential", "service", "track", "path", "primary" })
                foreach (var (surface, expected) in new[] { ("asphalt", Terrain.Road), ("gravel", Terrain.Dirt), ("sand", Terrain.Sand), ("paving_stones", Terrain.Pavement) })
                {
                    var tags = new Dictionary<string, string> { ["highway"] = highway, ["surface"] = surface, ["source"] = "survey", ["name"] = "Original road" };
                    var before = JsonSerializer.Serialize(tags);
                    var decision = TransferRegionalProfiles.ExplainRoad(tags, profile);
                    Check(TransferRegionalProfiles.RoadTerrain(tags, profile) == expected && !decision.IsInferred, "Explicit surface lost to highway/profile");
                    Check(JsonSerializer.Serialize(tags) == before && decision.SourceTags["source"] == "survey", "Source evidence was rewritten");
                    tags["surface"] = "mud";
                    Check(decision.SourceTags["surface"] == surface, "Decision aliases mutable source tags");
                }
            var unsupported = TransferRegionalProfiles.ExplainRoad(new Dictionary<string, string> { ["highway"] = "primary", ["surface"] = "ice" }, "ru-rural");
            Check(unsupported.Terrain != Terrain.Road && unsupported.IsInferred && unsupported.SourceTags["surface"] == "ice", "Unknown explicit surface invented asphalt");
        });
        yield return ("regional rural defaults distinguish through roads from service and paths", () =>
        {
            foreach (var road in new[] { "residential", "unclassified", "tertiary", "secondary", "primary", "trunk", "motorway", "living_street" })
            {
                var decision = TransferRegionalProfiles.ExplainRoad(new Dictionary<string, string> { ["highway"] = road }, "ru-rural");
                Check(decision.Terrain == Terrain.Road && decision.IsInferred && !decision.SourceTags.ContainsKey("surface") && decision.Reason.Length > 0, "Missing surface became observed asphalt");
            }
            foreach (var road in new[] { "service", "track", "path", "footway" })
                Check(TransferRegionalProfiles.RoadTerrain(new Dictionary<string, string> { ["highway"] = road }, "ru-rural") == Terrain.Dirt, "Rural access path became asphalt");
            Check(TransferRegionalProfiles.RoadTerrain(new Dictionary<string, string> { ["highway"] = "service" }) == Terrain.Road, "Opt-in profile changed default service roads");
            Reject(() => TransferRegionalProfiles.RoadTerrain(new Dictionary<string, string>(), "invented"));
        });
        yield return ("regional emergency routes connect all facilities and tents without blocking footprints", () =>
        {
            var project = MapGenerator.Generate(new() { Kind = "blank" });
            var plan = StreetEnsembles.Create(project, new() { X = 20, Y = 20, Width = 100, Height = 90, Preset = "emergency-shelter" }, []);
            var applied = StreetEnsembles.Apply(project, plan);
            var blocked = new HashSet<(int X, int Y)>();
            foreach (var b in plan.Buildings) foreach (var i in BuildingDesigner.Occupied(b)) blocked.Add((b.X + i % (b.Width + 1), b.Y + i / (b.Width + 1)));
            foreach (var d in plan.Decorations) foreach (var p in d.Parts) Check(blocked.Add((d.X + p.Dx, d.Y + p.Dy)), "Complete tent footprint overlaps another facility");
            foreach (var p in FenceLines.Compose(plan.Fences, plan.FenceStyles, plan.GateStyles, applied.Width, applied.Height).Pieces.Where(p => p.Flags != 0)) blocked.Add((p.X, p.Y));
            var queue = new Queue<(int X, int Y)>(); var reached = new HashSet<(int X, int Y)>();
            queue.Enqueue((72, 27)); reached.Add((72, 27));
            while (queue.TryDequeue(out var at)) foreach (var (dx, dy) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
            {
                var next = (X: at.X + dx, Y: at.Y + dy);
                if (next.X < 20 || next.Y < 20 || next.X >= 120 || next.Y >= 110 || blocked.Contains(next)
                    || applied.Terrain[next.Y * applied.Width + next.X] != Terrain.Pavement || !reached.Add(next)) continue;
                queue.Enqueue(next);
            }
            foreach (var b in plan.Buildings)
            {
                var door = b.Design!.Floors[0].Edges.First(e => e.Kind == "door" && e.Edge == "n" && e.Y == 0);
                Check(reached.Contains((b.X + door.X, b.Y - 1)), "Facility entrance disconnected from street");
            }
            foreach (var tent in plan.Decorations) Check(reached.Contains((tent.X, tent.Y + tent.Parts.Max(p => p.Dy) + 1)), "Tent entrance disconnected");
            Check(plan.AccessPaths.All(path => path.All(p => reached.Contains((p.X, p.Y)))), "Declared route blocked or disconnected");
            Check(plan.Buildings.SelectMany(b => b.Design!.Floors).SelectMany(f => f.Rooms.Values).Contains("kitchen"), "No field kitchen");
            Check(plan.Buildings.SelectMany(b => b.Design!.Floors).SelectMany(f => f.Objects).Any(o => o.Kind == "furniture" && o.Asset!.StartsWith("toilet:")), "Toilets are labels without fixtures");
            Check(plan.Fences.SelectMany(f => f.Gates).Any() && plan.Warnings.Any(w => w.Contains("three-wide")), "Missing real wicket or vehicle gate limitation");
        });
        yield return ("regional emergency deterministic snapshots reject stale bounds water and route tampering atomically", () =>
        {
            var p = MapGenerator.Generate(new() { Kind = "blank" });
            var o = new StreetEnsembleOptions { X = 20, Y = 20, Width = 100, Height = 90, Preset = "emergency-shelter", Seed = -7 };
            var plan = StreetEnsembles.Create(p, o, []);
            Check(JsonSerializer.Serialize(plan) == JsonSerializer.Serialize(StreetEnsembles.Create(p, o, [])), "Preview contains random IDs");
            var before = JsonSerializer.Serialize(p);
            var at = plan.AccessPaths[0][5];
            plan.Decorations.Add(new() { Id = "route-blocker", X = at.X, Y = at.Y, Asset = "furniture_storage_01_8", Parts = [new(0, 0, "furniture_storage_01_8", 1)] });
            Reject(() => StreetEnsembles.Apply(p, plan));
            Check(JsonSerializer.Serialize(p) == before, "Rejected apply changed source");
            Reject(() => StreetEnsembles.Create(p, new() { Width = 40, Height = 40, Preset = "emergency-shelter" }, []));
            plan = StreetEnsembles.Create(p, o, []); p.Terrain[0] = Terrain.Dirt;
            Reject(() => StreetEnsembles.Apply(p, plan));
            p = MapGenerator.Generate(new() { Kind = "blank" }); p.Terrain[36 * p.Width + 26] = Terrain.Water;
            before = JsonSerializer.Serialize(p); Reject(() => StreetEnsembles.Create(p, o, []));
            Check(JsonSerializer.Serialize(p) == before, "Rejected preview changed source");
        });
        yield return ("regional rural appearance uses available native directions and preserves authored edges", () =>
        {
            var wall = WallCatalog.Find("walls_exterior_house_01_48")!;
            var ids = new[] { wall.West, wall.North, wall.NorthWest, wall.SouthEast, wall.WestWindow, wall.NorthWindow, wall.WestDoor, wall.NorthDoor }.OfType<string>().ToArray();
            var available = ids.Select(id => new RegionalSprite(id, id == wall.West ? new Dictionary<string, string> { ["WallW"] = "" }
                : id == wall.North ? new Dictionary<string, string> { ["WallN"] = "" } : new Dictionary<string, string>(), "rural-owner", ["rural-base"])).ToList();
            available.Add(new("walls_detailing_01_0", new Dictionary<string, string> { ["WallOverlay"] = "", ["attachedW"] = "" }));
            var choice = TransferRegionalProfiles.SelectRuralAppearance(available);
            Check(choice.ExternalWall is not null && choice.TrimNorth is null && choice.TrimWest is not null && choice.RoofTileset is null, "Unavailable directional sprites fabricated");
            Check(ids.All(id => choice.AssetSources.GetValueOrDefault(id) == "rural-owner") && choice.RequiredMods.Contains("rural-base"), "Native ownership lost");
            var b = new Building { Width = 12, Height = 10 }; BuildingDesigner.Generate(b, 1, 42);
            var preserved = b.Design!.Floors[0].Edges.First(e => e.Y == 0); preserved.Edited = true; preserved.WallTile = "walls_exterior_house_01_0";
            var before = JsonSerializer.Serialize(b);
            var result = TransferRegionalProfiles.ApplyRuralAppearance(b, choice);
            Check(JsonSerializer.Serialize(b) == before && result.Design!.Floors[0].Edges.First(e => e.Y == 0).WallTile == preserved.WallTile, "Profile overwrote manual edge/source");
            Check(result.AssetSources.Count > 0 && result.RequiredMods.Contains("rural-owner"), "Applied appearance dropped ownership");
            Check(TransferRegionalProfiles.SelectRuralAppearance([]).ExternalWall is null, "Empty catalog invented assets");
        });
        yield return ("regional L and T roofs cover only occupied spans in native preview", () =>
        {
            foreach (var shape in new[] { "l", "t" }) foreach (var roof in new[] { "gabled", "hipped" })
            {
                var b = new Building { Width = 18, Height = 15 }; BuildingDesigner.Generate(b, 1, 12, shape); b.Design!.Roof = roof;
                var before = JsonSerializer.Serialize(b);
                var scene = NativeScene.Building(b);
                var pitched = scene.Sprites.Where(s => s.Tile.StartsWith("roofs_30_", StringComparison.Ordinal)).ToArray();
                var floor = b.Design.Floors[0];
                Check(pitched.Length == floor.Cells.Count(c => c != '.'), "Roof lost a wing or padded the bounding box");
                Check(pitched.All(s => BuildingDesigner.Cell(b, floor, s.X - 2, s.Y - 2) != '.'), "Roof filled an L/T courtyard void");
                Check(JsonSerializer.Serialize(b) == before, "Roof preview mutated footprint");
            }
        });
        yield return ("regional original rural palette carries real owners and rejects three disguised wickets", () =>
        {
            var empty = TransferRegionalProfiles.SelectRuralPalette([]);
            Check(empty.Fence is null && empty.Objects.Count == 0 && empty.VehicleGate is null && empty.Warnings.Any(w => w.Contains("three-wide")), "Missing custom assets were fabricated or gate gap hidden");
            var available = new List<RegionalSprite>
            {
                new("original_fixture_0", new Dictionary<string, string> { ["CustomName"] = "rural-corrugated-green", ["Facing"] = "N", ["collideN"] = "" }, "fixture-owner"),
                new("original_fixture_1", new Dictionary<string, string> { ["CustomName"] = "rural-corrugated-green", ["Facing"] = "W", ["collideW"] = "" }, "fixture-owner"),
                new("original_fixture_2", new Dictionary<string, string> { ["CustomName"] = "rural-shutters", ["Facing"] = "N", ["WallOverlay"] = "", ["attachedN"] = "" }, "fixture-owner", ["fixture-base"])
            };
            var palette = TransferRegionalProfiles.SelectRuralPalette(available);
            Check(palette.Fence?.N == "original_fixture_0" && palette.Fence.W == "original_fixture_1" && palette.Objects.Single().Role == "shutters-n", "Available directional original fixtures lost");
            Check(palette.AssetSources.Count == 3 && palette.RequiredMods.Contains("fixture-base"), "Original mod ownership missing");
            Check(TransferRegionalProfiles.SelectRuralPalette(available, "maroon").Fence is null, "Green fixture relabeled as unavailable maroon");
            var wickets = new GateStyle { Id = "not-a-vehicle-gate", Width = 3,
                ClosedW = ["original_fixture_1", "original_fixture_1", "original_fixture_1"], ClosedN = ["original_fixture_0", "original_fixture_0", "original_fixture_0"] };
            Reject(() => TransferRegionalProfiles.SelectRuralPalette(available, vehicleGate: wickets));
        });
        yield return ("regional installed B42 shelter resources resolve to real sprites and native owners", () =>
        {
            var game = Environment.GetEnvironmentVariable("PZMAP_GAME_PATH");
            if (string.IsNullOrEmpty(game)) { Console.WriteLine("SKIP regional installed-game check: set PZMAP_GAME_PATH"); return; }
            var catalog = new GameAssetCatalog(Path.Combine(Path.GetTempPath(), "pz-regional-resource-check"), game);
            Check(catalog.Ready, "Configured game catalog unavailable");
            var p = MapGenerator.Generate(new() { Kind = "blank" });
            var plan = StreetEnsembles.Create(p, new() { X = 20, Y = 20, Width = 100, Height = 90, Preset = "emergency-shelter" }, []);
            foreach (var id in plan.Buildings.SelectMany(NativeScene.BuildingResourceIds).Concat(plan.Decorations.SelectMany(d => d.Parts.Select(part => part.Tile)))
                .Concat(FenceLines.Compose(plan.Fences, plan.FenceStyles, plan.GateStyles, p.Width, p.Height).Pieces.Select(piece => piece.Tile)).Distinct())
            {
                var sprite = catalog.Get(id); Check(sprite.Sprite is not null && sprite.ModId is null, "Missing vanilla sprite or incorrect ownership: " + id);
            }
        });
        yield return ("regional original recipe builds separate native pack and resolves owned palette", () =>
        {
            var recipe = Environment.GetEnvironmentVariable("PZMAP_RURAL_RECIPE_DIR");
            var game = Environment.GetEnvironmentVariable("PZMAP_GAME_PATH");
            if (string.IsNullOrEmpty(recipe) || string.IsNullOrEmpty(game)) { Console.WriteLine("SKIP regional original recipe: set PZMAP_RURAL_RECIPE_DIR and PZMAP_GAME_PATH"); return; }
            var root = Path.Combine(Path.GetTempPath(), "pz-regional-recipe-" + Guid.NewGuid().ToString("N"));
            var maps = new AgentProjectStore(Path.Combine(root, "maps"));
            var worlds = new AgentWorldStore(Path.Combine(root, "worlds"));
            var assets = new GameAssetCatalog(root, game);
            var store = new AgentTilesetStore(Path.Combine(root, "drafts"), maps, worlds, assets);
            var source = maps.Create(MapGenerator.Generate(new() { Kind = "blank" }));
            var before = JsonSerializer.Serialize(source.Project);
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var spec = JsonSerializer.Deserialize<AgentTilesetSpec>(File.ReadAllText(Path.Combine(recipe, "spec.json")), json)!;
            var draft = store.Create(source.Token, source.Revision, spec);
            for (var i = 0; i < spec.Frames.Length; i++) draft = store.Upload(source.Token, draft.Id, draft.Revision, i, Convert.ToBase64String(File.ReadAllBytes(Path.Combine(recipe, spec.Frames[i].Key + ".png"))));
            draft = store.Build(source.Token, draft.Id, draft.Revision);
            for (var i = 0; i < spec.Frames.Length; i++)
            {
                Check(store.PreviewInfo(source.Token, draft.Id, i).Origin == "validated-native-pack", "Preview bypassed actual native pack");
                var pixels = Png.Decode(store.Preview(source.Token, draft.Id, i));
                Check(pixels.Rgba.SequenceEqual(Png.Decode(File.ReadAllBytes(Path.Combine(recipe, spec.Frames[i].Key + ".png"))).Rgba), "Native pack changed original pixels");
            }
            Check(maps.Read(source.Token).Revision == source.Revision && JsonSerializer.Serialize(maps.Read(source.Token).Project) == before, "Draft/build modified source map");
            File.WriteAllBytes(Path.Combine(recipe, spec.ModId + ".zip"), store.Download(source.Token, draft.Id));
            store.Attach(source.Token, draft.Id, source.Revision, draft.Revision);
            var available = draft.Frames.Select(f => assets.Get(f.TileId)).Select(a => new RegionalSprite(a.Id, a.Properties, a.ModId, a.Requirements)).ToArray();
            foreach (var color in new[] { "green", "maroon" })
            {
                var palette = TransferRegionalProfiles.SelectRuralPalette(available, color);
                Check(palette.Fence is not null && palette.Objects.Select(o => o.Role.Split('-')[0]).Distinct().Count() == 5, "Built original palette lost fence or attachments");
                Check(palette.AssetSources.Values.All(owner => owner == spec.ModId) && palette.RequiredMods.Contains(spec.ModId), "Original native assets lost owner");
            }
            using var bindings = JsonDocument.Parse(File.ReadAllText(Path.Combine(recipe, "bindings.json")));
            var gate = bindings.RootElement.GetProperty("editorVehicleGate").Deserialize<GateStyle>()!;
            var configured = TransferRegionalProfiles.SelectRuralPalette(available, vehicleGate: gate);
            Check(configured.VehicleGate?.Width == 3 && configured.Warnings.Any(w => w.Contains("coupled native")), "Editor artwork falsely claimed native linked gate behavior");
            var originalWall = bindings.RootElement.GetProperty("boards_green").Deserialize<WallSet>()!;
            Check(TransferRegionalProfiles.SelectRuralAppearance(available, externalWall: originalWall).ExternalWall == originalWall, "Active original board wall rejected");
        });
    }

    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Reject(Action run) { try { run(); } catch (ArgumentException) { return; } throw new Exception("Invalid regional proposal accepted"); }
}
