using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

public static class SharedExportMetadataTests
{
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("shared export metadata preserves standalone terrain Lua and map layout", () =>
        {
            var p = MapGenerator.Generate(new() { Kind = "blank", Name = "Legacy metadata" });
            p.WorldX = 110; p.WorldY = 120;
            p.Terrain[0] = Terrain.Forest;
            p.Terrain[8] = Terrain.Water;
            p.Terrain[16] = Terrain.Road;
            var files = ReadArchive(NativeMapExporter.Export(p));
            var zones = Text(files, "objects.lua");
            Check(zones.StartsWith("objects = {\n { name = \"\", type = \"Forest\", x = 28160, y = 30720, z = 0, width = 8, height = 8 },\n { name = \"\", type = \"TownZone\", x = 28176, y = 30720, z = 0, width = 8, height = 8 },\n", StringComparison.Ordinal), "Standalone fallback whitespace, water exclusion or coordinates changed");
            Check(zones.Split('\n').Length == 1026 && zones.EndsWith("}\n", StringComparison.Ordinal), "Standalone terrain zones changed count or wrapper");
            Check(Text(files, "spawnpoints.lua") == SpawnPlanner.Lua(p), "Standalone authored spawns changed");
            Check(Text(files, "map.info") == "title=Legacy metadata\nfixed2x=true\ndescription=Map 256 x 256, SimplePZMapmaker\nzoomX=28288\nzoomY=30848\nzoomS=8\n", "Standalone map.info layout changed");
            var slug = NativeMapExporter.ModId(p);
            Check(Text(files, "mod.info") == $"name={p.Name}\nid={slug}\ndescription=Map created with SimplePZMapmaker. Build 42; new world.\nversionMin=42.0\n", "Standalone mod.info field order changed");
            Check(files.ContainsKey(slug + "/project.pzmap.json") && !files.Keys.Any(k => k.Contains("/source/", StringComparison.Ordinal)), "Standalone source layout changed");
            Check(Text(files, "spawnregions.lua") == Regions(slug), "Standalone spawn region points outside its map");
            CheckAtlas(files, 1);
        });

        yield return ("shared export metadata map loaded and streamed parity includes zones spawns dependencies attribution and models", () =>
        {
            var world = Fixture(1);
            var p = world.Maps[0].Project;
            var map = ReadArchive(NativeMapExporter.Export(p));
            var loaded = ReadArchive(NativeWorldExporter.Export(world));
            using var output = new MemoryStream();
            var source = new SequentialSource(world, output);
            NativeStreamedWorldExporter.ExportAsync(source, output).GetAwaiter().GetResult();
            Check(output.CanWrite, "Streamed exporter closed the caller's output");
            var streamed = ReadArchive(output.ToArray());
            foreach (var suffix in new[] { "spawnpoints.lua", "worldmap.xml", "worldmap.xml.bin", "thumb.png" })
            {
                Equal(Entry(map, suffix), Entry(loaded, suffix), suffix + " differs between map and loaded world");
                Equal(Entry(loaded, suffix), Entry(streamed, suffix), suffix + " differs between loaded and streamed world");
            }
            var expectedZones = GameZonesExporter.ObjectsLua(p);
            Check(Text(map, "objects.lua") == expectedZones, "Map authored zones changed");
            Check(Text(loaded, "objects.lua") == expectedZones.Insert("objects = {\n".Length, "\n"), "World section zone wrapper changed");
            Equal(Entry(loaded, "objects.lua"), Entry(streamed, "objects.lua"), "Streamed authored zones differ");
            Check(Text(map, "objects.lua").Contains("Direction=\"E\", FaceDirection=false", StringComparison.Ordinal), "Vehicle properties disappeared");
            const string require = "require=HouseProvider,LootProvider,ModelProvider,VehicleProvider,ZProvider\n";
            foreach (var files in new[] { map, loaded, streamed })
            {
                Check(Text(files, "mod.info").EndsWith(require, StringComparison.Ordinal), "Required mods lost sorting or a model/zone/loot provider");
                Check(AttributionBody(files) == "Terrain source α\nHouse license\n", "Attribution lost UTF-8 content, order or deduplication");
                CheckAtlas(files, 1);
            }
            EqualModels(map, loaded);
            EqualModels(loaded, streamed);
            Equal(source.SectionBytes[world.Maps[0].Id], Entry(streamed, world.Maps[0].Id + ".pzmap.json"), "Streamed authoring source was projected or reserialized");
            Equal(source.MetadataBytes, Entry(streamed, "world.metadata.json"), "Streamed metadata source was changed");
            Check(Text(streamed, "world.pzworld.index.json") == JsonSerializer.Serialize(source.AuthoringIndex, NativeMapExporter.JsonOptions), "Streamed authoring index changed");
            Check(loaded.Keys.Any(k => k.EndsWith("/world.pzworld.json", StringComparison.Ordinal)) && !streamed.Keys.Any(k => k.EndsWith("/world.pzworld.json", StringComparison.Ordinal)), "Loaded and streamed reconstruction layouts were conflated");
        });

        yield return ("shared export metadata streamed sections stay sequential and preserve shared seam metadata", () =>
        {
            var world = Fixture(2);
            var shared = House(100 * 256 + 250, 100 * 256 + 40);
            shared.Attribution = ["Shared house license"];
            shared.RequiredMods = ["SharedHouseProvider"];
            shared.Design!.Floors[0].StaticModels.Add(new() { X = 8, Y = 4, ModelId = "Base.BeerBottle", RequiredMods = ["SharedModelProvider"] });
            world.Buildings.Add(shared);
            world.Elevation = new() { Source = "Survey", License = "Survey license", Columns = 1, Rows = 1, CellSize = .01, Values = [123.5] };
            var original = JsonSerializer.Serialize(world, NativeMapExporter.JsonOptions);
            var loaded = ReadArchive(NativeWorldExporter.Export(world));
            using var output = new MemoryStream();
            var source = new SequentialSource(world, output);
            Check(source.Metadata.Maps.Count == 0, "Fixture materialized a loaded world in streamed metadata");
            var progress = new List<NativeExportProgress>();
            NativeStreamedWorldExporter.ExportAsync(source, output, new(progress: progress.Add)).GetAwaiter().GetResult();
            var streamed = ReadArchive(output.ToArray());
            foreach (var suffix in new[] { "objects.lua", "spawnpoints.lua", "spawnregions.lua", "worldmap.xml", "worldmap.xml.bin", "thumb.png", "elevation.asc" })
                Equal(Entry(loaded, suffix), Entry(streamed, suffix), "Shared seam metadata differs: " + suffix);
            Check(AttributionBody(loaded) == AttributionBody(streamed) && AttributionBody(streamed).Contains("Shared house license\nElevation: Survey\nSurvey license\n", StringComparison.Ordinal), "Shared or elevation attribution changed");
            Check(Text(loaded, "mod.info").Split("require=")[1] == Text(streamed, "mod.info").Split("require=")[1], "Shared building/model dependencies differ");
            EqualModels(loaded, streamed);
            Check(source.Events.SequenceEqual(world.Maps.SelectMany(m => new[] { "read:" + m.Id, "project:" + m.Id, "copy:" + m.Id })), "Streamed source was read ahead, reread or projected after source copy");
            Check(progress.Last() is { Stage: "completed", CellsCompleted: 2, SectionsCompleted: 2 }, "Refactor changed cell/section progress lifecycle");
            foreach (var member in world.Maps)
                Equal(source.SectionBytes[member.Id], Entry(streamed, member.Id + ".pzmap.json"), "Shared projection contaminated source section " + member.Id);
            var restored = JsonSerializer.Deserialize<WorldProject>(Entry(streamed, "world.metadata.json"), NativeMapExporter.JsonOptions)!;
            Check(restored.Maps.Count == 0 && restored.Buildings.Single().X == shared.X && restored.Elevation!.Values[0] == 123.5, "Shared metadata is no longer reconstructible");
            Check(JsonSerializer.Serialize(world, NativeMapExporter.JsonOptions) == original, "Export mutated authoring data");
            CheckAtlas(streamed, 2);
        });

        yield return ("shared export metadata selected stream ignores corrupt unselected sections and preserves context", () =>
        {
            var world = Fixture(2);
            var shared = House(100 * 256 + 250, 100 * 256 + 40);
            shared.Design!.Floors[0].StaticModels.Add(new() { X = 8, Y = 4, ModelId = "Base.BeerBottle", RequiredMods = ["SharedModelProvider"] });
            world.Buildings.Add(shared);
            var member = world.Maps[1];
            using var loadedOutput = new MemoryStream();
            NativeWorldExporter.ExportSectionToStream(world, member.Id, loadedOutput);
            var loaded = ReadArchive(loadedOutput.ToArray());
            using var output = new MemoryStream();
            var source = new SequentialSource(world, output);
            source.SectionBytes[world.Maps[0].Id] = Encoding.UTF8.GetBytes("corrupt unselected section");
            NativeStreamedWorldExporter.ExportAsync(source, output, memberId: member.Id).GetAwaiter().GetResult();
            var selected = ReadArchive(output.ToArray());
            Check(source.Events.SequenceEqual(new[] { "read:" + member.Id, "project:" + member.Id, "copy:" + member.Id, "copy:" + member.Id }), "Selected export inspected another section");
            Check(selected.Keys.Count(k => k.Contains("/source/sections/", StringComparison.Ordinal)) == 1, "Selected source copied unselected sections");
            Equal(source.SectionBytes[member.Id], Entry(selected, "project.pzmap.json"), "Selected local project was replaced by projected shared data");
            var authored = JsonSerializer.Deserialize<MapProject>(Entry(selected, "project.pzmap.json"), NativeMapExporter.JsonOptions)!;
            ProjectValidator.Validate(authored);
            Check(authored.Buildings.Count == 0, "Clipped shared building leaked into standalone authored project");
            using var context = JsonDocument.Parse(Entry(selected, "section-world-context.json"));
            Check(context.RootElement.GetProperty("memberId").GetString() == member.Id && context.RootElement.GetProperty("sections").GetArrayLength() == 2 && context.RootElement.GetProperty("buildings").GetArrayLength() == 1, "Selected export lost complete layout or authoritative shared building context");
            foreach (var suffix in new[] { "spawnpoints.lua", "worldmap.xml", "worldmap.xml.bin", "thumb.png" })
                Equal(Entry(loaded, suffix), Entry(selected, suffix), "Selected map metadata differs: " + suffix);
            Check(Text(selected, "objects.lua") == Text(loaded, "objects.lua").Insert("objects = {\n".Length, "\n"), "Selected stream lost authored zones or changed its world wrapper");
            Check(AttributionBody(selected) == AttributionBody(loaded), "Selected shared attribution differs");
            Check(Text(loaded, "mod.info").Split("require=")[1] == Text(selected, "mod.info").Split("require=")[1], "Selected required mods differ");
            EqualModels(loaded, selected);
            CheckAtlas(selected, 1);
        });

        yield return ("shared export metadata cancellation stops before the next streamed section and leaves output open", () =>
        {
            var world = Fixture(2);
            using var output = new MemoryStream();
            using var cancellation = new CancellationTokenSource();
            var source = new SequentialSource(world, output) { AfterCopy = cancellation.Cancel };
            var progress = new List<NativeExportProgress>();
            try
            {
                NativeStreamedWorldExporter.ExportAsync(source, output, new(progress: progress.Add), cancellation.Token).GetAwaiter().GetResult();
                throw new Exception("Cancelled export completed");
            }
            catch (OperationCanceledException) { }
            Check(source.Events.Count(e => e.StartsWith("read:", StringComparison.Ordinal)) == 1, "Cancellation read another section");
            Check(!progress.Any(p => p.Stage == "completed") && output.CanWrite, "Metadata writer took over output or completion lifecycle");
        });

        yield return ("shared export metadata water-only selection stays empty while full streamed world needs spawn", () =>
        {
            var world = WorldTests.Fixture(2);
            var water = world.Maps[1];
            water.Project.Version = 6;
            water.Project.WorldSection = true;
            water.Project.Spawns.Clear();
            Array.Fill(water.Project.Terrain, Terrain.Water);
            using var full = new MemoryStream();
            NativeStreamedWorldExporter.ExportAsync(new SequentialSource(world, full), full).GetAwaiter().GetResult();
            using var selected = new MemoryStream();
            NativeStreamedWorldExporter.ExportAsync(new SequentialSource(world, selected), selected, memberId: water.Id).GetAwaiter().GetResult();
            var files = ReadArchive(selected.ToArray());
            Check(Text(files, "objects.lua") == "objects = {\n\n}\n" && !Text(files, "spawnpoints.lua").Contains("posX", StringComparison.Ordinal), "Water selection invented land zones or spawn points");
            CheckAtlas(files, 1);
            world.Maps.RemoveAt(0);
            using var rejected = new MemoryStream();
            try
            {
                NativeStreamedWorldExporter.ExportAsync(new SequentialSource(world, rejected), rejected).GetAwaiter().GetResult();
                throw new Exception("A full streamed world without spawn points was accepted");
            }
            catch (ArgumentException e) when (e.Message == "The world needs at least one dry-land spawn.") { }
        });
    }

    static WorldProject Fixture(int count)
    {
        var world = WorldTests.Fixture(count);
        var p = world.Maps[0].Project;
        p.RequiredMods = ["ZProvider"];
        p.Attribution = ["Terrain source α", "Terrain source α"];
        p.Buildings.Add(House(40, 40));
        p.StaticModels.Add(new() { ModelId = "Base.BeerBottle", X = 80, Y = 80, Profile = "container", LootDistribution = "StoreShelf", RequiredMods = ["ModelProvider"], LootRequiredMods = ["LootProvider"] });
        foreach (var member in world.Maps)
            member.Project.GameZones.Add(new() { Name = "Parking", Type = "ParkingStall", X = 16, Y = 16, Width = 3, Height = 5, VehicleDistribution = "parkingstall", Direction = "E", FaceDirection = false, RequiredMods = ["VehicleProvider"] });
        return world;
    }

    static Building House(int x, int y)
    {
        var house = new Building { X = x, Y = y, Width = 12, Height = 10, RequiredMods = ["HouseProvider"], Attribution = ["House license"] };
        BuildingDesigner.Generate(house, 1, 42);
        var floor = house.Design!.Floors[0];
        floor.Cells = new string('0', house.Width * house.Height);
        floor.Rooms = new() { ["0"] = "bedroom" };
        floor.Objects.Clear();
        floor.Edges = BuildingDesigner.AutoEdges(house, floor, 0);
        return house;
    }

    // Serialized sections, empty Metadata.Maps and enforced read/project/copy phases exercise
    // the streaming API without a compatibility world or a store/lease owned by the writer.
    sealed class SequentialSource : IStreamedWorldExportSource
    {
        readonly MemoryStream output;
        string? pending, lastCopied;
        MapProject? authored;
        bool projected;
        long lengthBeforeRead;
        public WorldProject Metadata { get; }
        public IReadOnlyList<StreamedWorldSection> Sections { get; }
        public object AuthoringIndex { get; }
        public Dictionary<string, byte[]> SectionBytes { get; }
        public byte[] MetadataBytes { get; }
        public List<string> Events { get; } = [];
        public Action? AfterCopy { get; init; }

        public SequentialSource(WorldProject world, MemoryStream output)
        {
            this.output = output;
            Sections = world.Maps.Select(m => new StreamedWorldSection(m.Id, m.Project.Name, m.Project.WorldX, m.Project.WorldY, m.Project.Width, m.Project.Height)).ToArray();
            SectionBytes = world.Maps.ToDictionary(m => m.Id, m => JsonSerializer.SerializeToUtf8Bytes(m.Project, NativeMapExporter.JsonOptions));
            Metadata = WorldAssembler.Clone(world);
            Metadata.Maps.Clear();
            MetadataBytes = JsonSerializer.SerializeToUtf8Bytes(Metadata, NativeMapExporter.JsonOptions);
            AuthoringIndex = new { format = "test-index", worldId = world.Id, sections = Sections };
        }

        public async Task<MapProject> ReadSectionAsync(string id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Check(pending is null, "Exporter read another section before consuming the current section");
            await Task.Yield();
            Events.Add("read:" + id);
            pending = id;
            projected = false;
            lengthBeforeRead = output.Length;
            return authored = JsonSerializer.Deserialize<MapProject>(SectionBytes[id], NativeMapExporter.JsonOptions)!;
        }

        public Task<MapProject> ProjectSectionAsync(string id, MapProject project, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Check(pending == id && ReferenceEquals(project, authored) && !projected, "Exporter projected the wrong section or reread its source");
            projected = true;
            Events.Add("project:" + id);
            return Task.FromResult(WorldAssembler.ProjectSection(Metadata, new() { Id = id, Project = project }));
        }

        public async Task CopySectionSourceAsync(string id, Stream destination, CancellationToken token)
        {
            Check(pending == id && projected || pending is null && lastCopied == id, "Source copy occurred outside the active section");
            Check(output.Length > lengthBeforeRead, "Exporter buffered sections before writing native cells");
            await destination.WriteAsync(SectionBytes[id], token);
            Events.Add("copy:" + id);
            lastCopied = id;
            pending = null;
            authored = null;
            AfterCopy?.Invoke();
        }

        public Task CopyMetadataSourceAsync(Stream destination, CancellationToken token) => destination.WriteAsync(MetadataBytes, token).AsTask();
    }

    static Dictionary<string, byte[]> ReadArchive(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0))
        {
            using var input = entry.Open();
            using var output = new MemoryStream();
            input.CopyTo(output);
            files.Add(entry.FullName, output.ToArray());
        }
        return files;
    }

    static byte[] Entry(Dictionary<string, byte[]> files, string suffix) => files.Single(f => f.Key.EndsWith("/" + suffix, StringComparison.Ordinal)).Value;
    static string Text(Dictionary<string, byte[]> files, string suffix) => Encoding.UTF8.GetString(Entry(files, suffix));
    static string AttributionBody(Dictionary<string, byte[]> files) => Text(files, "ATTRIBUTION.txt").Split('\n', 2)[1];
    static string Regions(string slug) => $"function SpawnRegions()\n return {{ {{ name = \"{slug}\", file = \"media/maps/{slug}/spawnpoints.lua\" }} }}\nend\n";

    static void CheckAtlas(Dictionary<string, byte[]> files, int cells)
    {
        var xml = XElement.Parse(Text(files, "worldmap.xml"));
        Check(xml.Elements("cell").Count() == cells && (string?)xml.Attribute("cellSize") == "256", "Atlas lost sparse native cells");
        Equal(NativeWorldMapBinary.Write(xml), Entry(files, "worldmap.xml.bin"), "Binary atlas no longer matches XML");
    }

    static void EqualModels(Dictionary<string, byte[]> left, Dictionary<string, byte[]> right)
    {
        string Normalize(string text) => Regex.Replace(text, "SPZM[A-F0-9]{12}", "MODULE");
        Dictionary<string, string> Models(Dictionary<string, byte[]> files) => files
            .Where(f => f.Key.Contains("/42/media/scripts/", StringComparison.Ordinal) || f.Key.Contains("/42/media/lua/", StringComparison.Ordinal))
            .ToDictionary(f => Normalize(f.Key[(f.Key.IndexOf("/42/media/", StringComparison.Ordinal) + 10)..]), f => Normalize(Encoding.UTF8.GetString(f.Value)));
        var a = Models(left); var b = Models(right);
        Check(a.Count > 0 && a.Count == b.Count, "Model scripts disappeared");
        foreach (var (path, value) in a)
            Check(b.TryGetValue(path, out var actual) && value == actual, "Model placement/glue differs: " + path);
    }

    static void Equal(byte[] expected, byte[] actual, string message) => Check(expected.AsSpan().SequenceEqual(actual), message);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
