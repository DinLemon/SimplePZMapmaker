using System.Text.Json;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;

public static class GenerationStudioTests
{
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("selective generation records contextual rules and rejects unsupported explicit legacy mode", () =>
        {
            var p = Fixture();
            var preview = GenerationStudio.Preview(p, Full(), 42);
            if (preview.Project.Generation?.Rules?.Preset != "contextual") throw new Exception("Selective generation misreported its actual algorithm.");
            Reject(() => GenerationStudio.Preview(p, Full(), 42, rules: new() { Preset = "legacy" }));
        });
        yield return ("generation studio preview is pure and seeds are reproducible", () =>
        {
            var project = Fixture(); var before = JsonSerializer.Serialize(project);
            var first = GenerationStudio.Preview(project, Full(), 42);
            var second = GenerationStudio.Preview(project, Full(), 42);
            if (JsonSerializer.Serialize(first) != JsonSerializer.Serialize(second) || JsonSerializer.Serialize(project) != before) throw new Exception("Preview changed the source or lost reproducibility.");
            if (first.Summary.AddedBuildings < 1) throw new Exception("Road-adjacent completion produced no buildings.");
            if (first.Summary.ChangedTerrainTiles < 1) throw new Exception("Accepted buildings lost their generated access paths and courts.");
            var other = GenerationStudio.Preview(project, Full(), 42, 1);
            if (JsonSerializer.Serialize(first.Project.Buildings) == JsonSerializer.Serialize(other.Project.Buildings)) throw new Exception("Variant did not change the generated settlement.");
            var otherSeed = GenerationStudio.Preview(project, Full(), 43);
            if (JsonSerializer.Serialize(first.Project.Buildings) == JsonSerializer.Serialize(otherSeed.Project.Buildings)) throw new Exception("Seed did not change the generated settlement.");
        });
        yield return ("generation studio fill preserves source authored and modified buildings", () =>
        {
            var project = Fixture();
            var result = GenerationStudio.Preview(project, Full(), 14);
            foreach (var building in project.Buildings) Same(building, result.Project.Buildings.Single(b => b.Id == building.Id));
            if (result.Summary.RemovedBuildings != 0 || result.Summary.PreservedBuildings != project.Buildings.Count) throw new Exception("Fill removed existing geometry.");
            if (result.Provenance.Single(p => p.BuildingId == "source-building").Footprint != "osm-source" || result.Provenance.Single(p => p.BuildingId == "source-building").Layout != "inferred") throw new Exception("Source geometry and synthesized layout provenance were conflated.");
            if (result.Provenance.Single(p => p.BuildingId == "manual").Footprint != "authored") throw new Exception("Authored footprint provenance lost.");
            var imported = project.Buildings.Single(b => b.Id == "source-building");
            imported.FootprintOrigin = "manual-source-edit"; imported.Modified = true;
            var edited = GenerationStudio.Provenance(project).Single(p => p.BuildingId == imported.Id);
            if (edited.Footprint != "source-edited" || edited.SourceId != imported.SourceId || imported.SourceOutline.Count == 0) throw new Exception("A retained source outline hid manual source edits in provenance.");
        });
        yield return ("generation studio regeneration honors locks protected edges and authored terrain", () =>
        {
            var project = Fixture();
            project = LinearFeatureEditor.Put(project, new() { Id = "source-river", Kind = "river", Width = 4, SourceId = "way/river", Points = [new(224, 65), new(224, 150)] });
            project = LinearFeatureEditor.Put(project, new() { Id = "authored-bridge", Kind = "bridge", Level = 1, Access = "stairs", Width = 4, Points = [new(40, 70), new(80, 70)] });
            var infrastructure = InfrastructureGeometry.Build(project);
            project.Generation = new() { LockedBuildingIds = ["generated-lock"], ProtectedAreas = [new() { X = 93, Y = 20, Width = 5, Height = 5 }, new() { X = 70, Y = 104, Width = 35, Height = 60 }] };
            var result = GenerationStudio.Preview(project, Full(), 25, mode: "regenerate");
            if (result.Project.Buildings.Any(b => b.Id == "generated-replace")) throw new Exception("Regeneration did not replace an eligible generated building.");
            foreach (var id in new[] { "source-building", "manual", "generated-modified", "generated-lock", "generated-protected" }) Same(project.Buildings.Single(b => b.Id == id), result.Project.Buildings.Single(b => b.Id == id));
            foreach (var b in result.Project.Buildings.Where(b => !project.Buildings.Any(a => a.Id == b.Id)))
                if (project.Generation.ProtectedAreas.Any(a => a.Intersects(b))) throw new Exception("New footprint crossed a protection edge.");
            Same(project.Decorations, result.Project.Decorations); Same(project.StaticModels, result.Project.StaticModels); Same(project.Spawns, result.Project.Spawns); Same(project.GameZones, result.Project.GameZones); Same(project.LinearFeatures, result.Project.LinearFeatures);
            Same(infrastructure, InfrastructureGeometry.Build(result.Project));
            Same(project.SurfaceTiles, result.Project.SurfaceTiles);
            var mask = TerrainEditMask.Decode(project);
            for (var i = 0; i < project.Terrain.Length; i++) if ((TerrainEditMask.IsEdited(mask, i) || project.Terrain[i] is Terrain.Road or Terrain.Water || project.SurfaceTiles.ContainsKey(i)) && project.Terrain[i] != result.Project.Terrain[i]) throw new Exception("Protected terrain changed.");
            foreach (var area in project.Generation.ProtectedAreas) for (var y = area.Y; y < area.Y + area.Height; y++) for (var x = area.X; x < area.X + area.Width; x++) if (project.Terrain[y * project.Width + x] != result.Project.Terrain[y * project.Width + x]) throw new Exception("Terrain inside a protection changed.");
        });
        yield return ("generation studio selected area leaves crossing buildings intact", () =>
        {
            var project = Fixture();
            var area = new GenerationArea { X = 35, Y = 15, Width = 120, Height = 130 };
            var result = GenerationStudio.Preview(project, area, 17, mode: "regenerate");
            Same(project.Buildings.Single(b => b.Id == "generated-replace"), result.Project.Buildings.Single(b => b.Id == "generated-replace"));
            foreach (var b in result.Project.Buildings.Where(b => !project.Buildings.Any(a => a.Id == b.Id))) if (!area.Contains(b)) throw new Exception("A new building crossed the selected edge.");
            for (var y = 0; y < project.Height; y++) for (var x = 0; x < project.Width; x++) if (!area.Contains(x, y) && result.Project.Terrain[y * project.Width + x] != project.Terrain[y * project.Width + x]) throw new Exception("Terrain outside selection changed.");
        });
        yield return ("generation studio regenerates every eligible building despite its generated entrance pavement", () =>
        {
            var project = MapGenerator.Generate(new() { Kind = "town", Seed = 42, BuildingDensity = 100, Rules = new() { Preset = "contextual", Density = 100 } });
            var original = JsonSerializer.Serialize(project);
            var eligible = project.Buildings.Where(GenerationStudio.IsGenerated).ToArray();
            var entrance = eligible.SelectMany(b => Enumerable.Range(b.Y, b.Height + 1).SelectMany(y => Enumerable.Range(b.X, b.Width + 1).Select(x => (Building: b, Index: y * project.Width + x)))).First(p => project.Terrain[p.Index] == Terrain.Pavement);
            if (eligible.Length < 20) throw new Exception("Contextual regression fixture unexpectedly lacks buildings.");
            var result = GenerationStudio.Preview(project, Full(), 43, mode: "regenerate");
            if (result.Summary.RemovedBuildings != eligible.Length || result.Project.Buildings.Any(b => eligible.Any(old => old.Id == b.Id))) throw new Exception($"Generated entrance pavement prevented regeneration: removed {result.Summary.RemovedBuildings}/{eligible.Length}.");
            if (result.Summary.AddedBuildings == 0) throw new Exception("Regeneration removed buildings without finding replacement frontage.");
            for (var i = 0; i < project.Terrain.Length; i++) if (project.Terrain[i] == Terrain.Pavement && result.Project.Terrain[i] != Terrain.Pavement) throw new Exception("Existing pavement was overwritten during replacement placement.");
            if (JsonSerializer.Serialize(project) != original) throw new Exception("Regeneration mutated its source.");
            // Once this exact entrance tile is painted by the author, its footprint becomes protected.
            TerrainEditMask.Mark(project, entrance.Index);
            var protectedResult = GenerationStudio.Preview(project, Full(), 43, mode: "regenerate");
            Same(entrance.Building, protectedResult.Project.Buildings.Single(b => b.Id == entrance.Building.Id));
            if (!TerrainEditMask.IsEdited(TerrainEditMask.Decode(protectedResult.Project), entrance.Index) || protectedResult.Project.Terrain[entrance.Index] != Terrain.Pavement) throw new Exception("Authored pavement protection was weakened.");
        });
        yield return ("generation studio server proposals reject stale and forged apply", () =>
        {
            var project = Fixture(); var store = new GenerationProposalStore();
            var source = JsonSerializer.Serialize(project);
            var proposal = store.Add(GenerationStudio.Preview(project, Full(), 42));
            var expected = JsonSerializer.Serialize(proposal.Project);
            // Mutating the client-visible candidate cannot change the server-owned proposal.
            proposal.Project.Name = "forged candidate";
            var applied = store.Apply(project, proposal.ProposalId);
            if (JsonSerializer.Serialize(applied.Project) != expected) throw new Exception("Client candidate tampered with the server snapshot.");
            Same(applied.Project, store.Apply(project, proposal.ProposalId).Project);
            if (JsonSerializer.Serialize(project) != source) throw new Exception("Apply mutated its input.");
            project.Generation = new() { LockedBuildingIds = ["manual"] };
            Reject(() => store.Apply(project, proposal.ProposalId));
            project.Generation = null;
            project.Terrain[0] = Terrain.Dirt;
            Reject(() => store.Apply(project, proposal.ProposalId));
            Reject(() => store.Apply(project, new string('f', 32)));
        });
        yield return ("generation studio proposal count is bounded", () =>
        {
            var project = Fixture(); var store = new GenerationProposalStore();
            var preview = GenerationStudio.Preview(project, new() { X = 0, Y = 0, Width = 10, Height = 10 }, 42);
            var first = store.Add(preview);
            for (var i = 0; i < 12; i++) store.Add(preview);
            Reject(() => store.Apply(project, first.ProposalId));
        });
        yield return ("generation studio persisted protections survive save and invalid metadata rejects", () =>
        {
            var project = Fixture();
            project.Generation = new() { LockedBuildingIds = ["manual"], ProtectedAreas = [new() { Id = "garden", Name = "Manual garden", X = 20, Y = 60, Width = 20, Height = 20 }], Rules = new() };
            var saved = JsonSerializer.Deserialize<MapProject>(JsonSerializer.Serialize(project))!;
            GenerationControl.Validate(saved); Same(project.Generation, saved.Generation);
            project.Generation.LockedBuildingIds.Add("manual"); Reject(() => GenerationControl.Validate(project));
            project.Generation.LockedBuildingIds.RemoveAt(1); project.Generation.ProtectedAreas[0].Width = int.MaxValue; Reject(() => GenerationControl.Validate(project));
            Reject(() => GenerationStudio.Preview(Fixture(), Full(), 4, variant: -1));
            Reject(() => GenerationStudio.Preview(Fixture(), Full(), 4, mode: "replace-all"));
            Reject(() => GenerationStudio.Preview(Fixture(), Full(), 4, rules: new() { MaximumFloors = 0 }));
        });
        yield return ("generation studio world projection retains isolated generation metadata", () =>
        {
            var world = WorldFixture(); var raw = world.Maps[0].Project;
            var restored = WorldAssembler.Clone(world);
            Same(raw.Generation, restored.Maps[0].Project.Generation);
            var projected = WorldAssembler.ProjectSection(restored, restored.Maps[0]);
            Same(raw.Generation, projected.Generation);
            projected.Generation!.LockedBuildingIds.Add("projection-only");
            projected.Generation.ProtectedAreas[0].Name = "projection-only";
            Same(raw.Generation, restored.Maps[0].Project.Generation);
        });
        yield return ("generation studio world preview reserves shared context and world-bound apply rejects stale geometry", () =>
        {
            var world = WorldFixture(); var member = world.Maps[0]; var raw = member.Project;
            var before = JsonSerializer.Serialize(world);
            var context = GenerationStudioWorldContext.Create(raw, world, member.Id);
            var preview = GenerationStudio.Preview(raw, Full(), 82, rules: raw.Generation!.Rules, worldContext: context);
            if (preview.Summary.AddedBuildings == 0) throw new Exception("Shared road frontage was unavailable to section generation.");
            if (preview.Project.SharedInfrastructure is not null || preview.Project.Buildings.Any(b => world.Buildings.Any(shared => shared.Id == b.Id)) || preview.Project.Decorations.Count != raw.Decorations.Count) throw new Exception("A projection object leaked into raw section data.");
            Same(raw.Generation.ProtectedAreas, preview.Project.Generation!.ProtectedAreas);
            if (preview.Project.Terrain[100 * 256 + 50] != raw.Terrain[100 * 256 + 50] || preview.Project.Terrain[50 * 256 + 190] != raw.Terrain[50 * 256 + 190]) throw new Exception("Shared road or river projection was persisted into raw terrain.");
            var sharedCells = world.Buildings.SelectMany(WorldAssembler.BuildingCells).Where(c => c.X < 256 && c.Y < 256).ToHashSet();
            foreach (var feature in world.LinearFeatures) sharedCells.UnionWith(WorldInfrastructure.Rasterize(feature).Where(c => c.X < 256 && c.Y < 256));
            var infrastructure = WorldInfrastructure.Build(world);
            sharedCells.UnionWith(infrastructure.Tiles.Where(t => t.X < 256 && t.Y < 256).Select(t => (t.X, t.Y)));
            sharedCells.UnionWith(infrastructure.Holes.Where(h => h.X < 256 && h.Y < 256).Select(h => (h.X, h.Y)));
            foreach (var b in preview.Project.Buildings) if (WorldAssembler.BuildingCells(b).Any(sharedCells.Contains)) throw new Exception("A generated building overlaps shared world geometry.");
            if (preview.Provenance.Count != preview.Project.Buildings.Count || preview.Provenance.Any(p => world.Buildings.Any(b => b.Id == p.BuildingId))) throw new Exception("Shared projection leaked into local provenance.");
            var store = new GenerationProposalStore(); var proposal = store.Add(preview);
            Same(preview.Project, store.Apply(raw, proposal.ProposalId, world, member.Id).Project);
            Reject(() => store.Apply(raw, proposal.ProposalId));
            Reject(() => store.Apply(raw, proposal.ProposalId, world, world.Maps[1].Id));
            var changed = WorldAssembler.Clone(world); changed.LinearFeatures[0].Points[0] = new(10, 100);
            Reject(() => store.Apply(raw, proposal.ProposalId, changed, member.Id));
            var mismatched = WorldAssembler.Clone(world); mismatched.Maps[0].Project.Terrain[0] = Terrain.Dirt;
            Reject(() => GenerationStudioWorldContext.Create(raw, mismatched, member.Id));
            var colliding = NeighborhoodPlanner.Copy(raw); colliding.Buildings.Add(new() { Id = "invalid-candidate", X = 244, Y = 60, Width = 10, Height = 18 });
            var badProposal = store.Add(preview with { Project = colliding });
            Reject(() => store.Apply(raw, badProposal.ProposalId, world, member.Id));
            if (JsonSerializer.Serialize(world) != before) throw new Exception("World-aware preview or apply mutated its source snapshot.");
        });
    }

    static GenerationArea Full() => new() { X = 0, Y = 0, Width = 256, Height = 256 };
    static WorldProject WorldFixture()
    {
        var left = MapGenerator.Generate(new() { Kind = "blank" }); left.WorldX = 0; left.WorldY = 0;
        var right = MapGenerator.Generate(new() { Kind = "blank" }); right.WorldX = 1; right.WorldY = 0;
        left.Generation = new() { LockedBuildingIds = ["remembered-lock"], ProtectedAreas = [new() { Name = "Manual reserve", X = 10, Y = 10, Width = 15, Height = 15 }], Rules = new() { Preset = "contextual", Density = 100 } };
        var shared = new Building { Id = "shared-source-building", SourceId = "way/shared", X = 248, Y = 60, Width = 16, Height = 18 };
        shared.Design = BuildingDesigner.Generate(shared, 1, 4);
        var world = new WorldProject { Maps = [new() { Project = left }, new() { Project = right }], Buildings = [shared], LinearFeatures = [new() { Id = "world-road", Width = 6, Points = [new(8, 100), new(500, 100)] }, new() { Id = "world-river", Kind = "river", Width = 5, Points = [new(190, 8), new(190, 200)] }, new() { Id = "world-bridge", Kind = "bridge", Width = 4, Level = 1, Access = "stairs", Points = [new(100, 65), new(150, 65)] }] };
        WorldAssembler.Validate(world); return world;
    }
    static MapProject Fixture()
    {
        var project = MapGenerator.Generate(new() { Kind = "blank" });
        for (var y = 98; y <= 102; y++) for (var x = 4; x < 252; x++) project.Terrain[y * project.Width + x] = Terrain.Road;
        project.Buildings = [new() { Id = "source-building", X = 10, Y = 20, SourceId = "way/1", FootprintOrigin = "source-grid", SourceOutline = [[new(45, 55), new(45.001, 55), new(45.001, 55.001), new(45, 55)]] }, new() { Id = "generated-replace", X = 30, Y = 20 }, new() { Id = "generated-lock", X = 50, Y = 20 }, new() { Id = "manual", X = 70, Y = 20 }, new() { Id = "generated-protected", X = 90, Y = 20 }, new() { Id = "generated-modified", X = 110, Y = 20, Modified = true }];
        project.Decorations.Add(new() { X = 120, Y = 110, Asset = "vegetation_ornamental_01_0", Parts = [new(0, 0, "vegetation_ornamental_01_0")] });
        project.StaticModels.Add(new() { ModelId = "Base.Book", X = 140, Y = 110 });
        project.GameZones.Add(new() { X = 160, Y = 110, Width = 12, Height = 12 });
        project.Version = 9;
        project.SurfaceTiles[112 * project.Width + 180] = "blends_natural_01_0";
        TerrainEditMask.Mark(project, 112 * project.Width + 190);
        ProjectValidator.Validate(project);
        return project;
    }
    static void Same<T>(T a, T b) { if (JsonSerializer.Serialize(a) != JsonSerializer.Serialize(b)) throw new Exception("Preserved authoring data changed."); }
    static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid generation request accepted."); }
}
