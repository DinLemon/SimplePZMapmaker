using System.Text.Json;

namespace SimplePZMapmaker.Core;

/// <summary>Authored PVR scenario, not evidence of an observed emergency site. Uses existing native/catalog fixtures.</summary>
public static class EmergencyAccommodation
{
    // Exact S-facing SpriteGridPos group from the installed B42 camping_01 definition.
    public static IReadOnlyList<EnsembleResource> DefaultResources() => new List<EnsembleResource>
    {
        new("tent", "camping_01_2", [new(0, 0, "camping_01_2", 1), new(1, 0, "camping_01_3", 1)])
    };

    internal static StreetEnsemblePlan Create(MapProject project, StreetEnsembleOptions o, IReadOnlyList<EnsembleResource> resources)
    {
        if (o.Width < 88 || o.Height < 72) throw new ArgumentException("Emergency shelter needs at least 88×72 tiles for separate facilities and clear routes.");
        if (resources.Any(r => r is null) || resources.Select(r => r.Role).Distinct().Count() != resources.Count)
            throw new ArgumentException("Emergency resources need unique roles.");
        var plan = new StreetEnsemblePlan { Options = NeighborhoodPlanner.Copy(o), BaseFingerprint = StreetEnsembles.Fingerprint(project) };
        plan.Id = ParametricBuildings.Stable(plan.BaseFingerprint + JsonSerializer.Serialize(o));
        var placement = new ParcelPlacement(project);
        placement.ReserveEntrances();
        var occupied = new HashSet<(int X, int Y)>();
        var routes = new HashSet<(int X, int Y)>();
        var surfaces = new Dictionary<(int X, int Y), EnsembleSurface>();
        bool Inside(int x, int y) => x >= o.X && y >= o.Y && x < o.X + o.Width && y < o.Y + o.Height;
        void Require(int x, int y, bool passage = false)
        {
            if (!Inside(x, y) || !placement.Free(x, y, allowRoad: passage, forPassage: passage) || occupied.Contains((x, y)))
                throw new ArgumentException("Emergency shelter overlaps an existing feature, protected terrain, water or an entrance. Move its preview rectangle.");
        }
        void Paint(int x, int y, Terrain terrain, string kind)
        {
            Require(x, y, true);
            surfaces[(x, y)] = new(x, y, 1, 1, terrain, kind);
            if (kind == "access") routes.Add((x, y));
        }
        void Dependencies(EnsembleResource resource)
        {
            VisualStyles.ValidateParts(resource.Asset, resource.Parts);
            ResourceDependencies.Validate(resource.Requirements ?? [], resource.ModId is {} owner ? new() { [resource.Asset] = owner } : []);
            if (resource.ModId is {} mod)
            {
                foreach (var tile in resource.Parts.Select(p => p.Tile).Append(resource.Asset))
                {
                    if (plan.AssetSources.TryGetValue(tile, out var prior) && prior != mod) throw new ArgumentException("Emergency resource owners conflict.");
                    plan.AssetSources[tile] = mod;
                }
                plan.RequiredMods.Add(mod);
            }
            plan.RequiredMods.AddRange(resource.Requirements ?? []);
        }
        Building Facility(string role, string kind, int x, int y, int width, int height)
        {
            var building = new Building { Id = ParametricBuildings.Stable(plan.Id + ":" + role), Kind = kind, Name = "PVR " + role,
                X = o.X + x, Y = o.Y + y, Width = width, Height = height, SourceKind = "authored:emergency-shelter", Modified = true, LevelsOrigin = "manual" };
            building.Design = BuildingDesigner.Generate(building, 1, o.Seed);
            building.Design!.Roof = "flat";
            VisualStyles.Apply(building, role == "container-hq" ? "industrial" : "commercial");
            foreach (var i in BuildingDesigner.Occupied(building))
            {
                var cell = (X: building.X + i % (width + 1), Y: building.Y + i / (width + 1));
                Require(cell.X, cell.Y);
            }
            foreach (var i in BuildingDesigner.Occupied(building)) occupied.Add((building.X + i % (width + 1), building.Y + i / (width + 1)));
            plan.Buildings.Add(building);
            return building;
        }
        var school = Facility("school-accommodation-kitchen", "school", 6, 16, 24, 18);
        var rooms = school.Design!.Floors[0].Rooms;
        var classrooms = rooms.Where(r => r.Value == "classroom").Select(r => r.Key).ToArray();
        for (var i = 0; i < classrooms.Length; i++) rooms[classrooms[i]] = i == 0 ? "kitchen" : "bedroom";
        BuildingDesigner.Furnish(school, 0, new Random(o.Seed));
        var medical = Facility("medpoint", "clinic", 6, 44, 12, 10);
        var hq = Facility("container-hq", "garage", 24, 44, 12, 8);
        hq.Design!.Floors[0].Rooms = new() { ["1"] = "office" };
        hq.Design.Floors[0].Cells = new('1', hq.Width * hq.Height);
        hq.Design.Floors[0].Edges = BuildingDesigner.AutoEdges(hq, hq.Design.Floors[0], 0);
        BuildingDesigner.Furnish(hq, 0, new Random(o.Seed));
        var toilets = Facility("toilets", "house", 42, 44, 6, 6);
        toilets.Design!.Floors[0].Rooms = new() { ["1"] = "bathroom" };
        toilets.Design.Floors[0].Cells = new('1', 36);
        toilets.Design.Floors[0].Edges = BuildingDesigner.AutoEdges(toilets, toilets.Design.Floors[0], 0);
        BuildingDesigner.Furnish(toilets, 0, new Random(o.Seed));
        foreach (var b in plan.Buildings) BuildingDesigner.Validate(b);
        for (var y = o.Y + 2; y <= o.Y + 5; y++)
            for (var x = o.X + 2; x < o.X + o.Width - 2; x++) Paint(x, y, Terrain.Road, "street");
        for (var y = o.Y + 6; y <= o.Y + 7; y++)
            for (var x = o.X + 2; x < o.X + o.Width - 2; x++) Paint(x, y, Terrain.Pavement, "sidewalk");
        var spine = o.X + 52;
        for (var y = o.Y + 7; y < o.Y + o.Height - 4; y++)
            for (var x = spine; x <= spine + 1; x++) Paint(x, y, Terrain.Pavement, "access");
        // Each route starts at the public sidewalk and ends immediately outside an actual entrance.
        void Route(int targetX, int targetY, bool south = false)
        {
            var path = new List<EnsemblePoint>();
            var junctionY = south ? targetY + 1 : targetY - 1;
            for (var y = o.Y + 7; y <= junctionY; y++) path.Add(new(spine, y));
            var step = Math.Sign(targetX - spine);
            for (var x = spine + step; step != 0 && (step > 0 ? x <= targetX : x >= targetX); x += step) path.Add(new(x, junctionY));
            path.Add(new(targetX, targetY));
            foreach (var point in path)
            {
                Paint(point.X, point.Y, Terrain.Pavement, "access");
                Paint(point.X + 1, point.Y, Terrain.Pavement, "access");
            }
            plan.AccessPaths.Add(path);
        }
        foreach (var b in new[] { school, medical, hq, toilets })
        {
            var entrance = b.Design!.Floors[0].Edges.First(e => e.Kind == "door" && e.Edge == "n" && e.Y == 0);
            Route(b.X + entrance.X, b.Y - 1);
        }
        var tent = resources.FirstOrDefault(r => r.Role == "tent") ?? DefaultResources()[0];
        Dependencies(tent);
        var th = tent.Parts.Max(p => p.Dy) + 1;
        foreach (var (dx, dy) in new[] { (60, 19), (74, 19), (60, 35), (74, 35) })
        {
            var x = o.X + dx; var y = o.Y + dy;
            Route(x, y + th, south: true);
            foreach (var p in tent.Parts)
            {
                Require(x + p.Dx, y + p.Dy);
                if (routes.Contains((x + p.Dx, y + p.Dy))) throw new ArgumentException("The complete tent footprint blocks a route.");
            }
            foreach (var p in tent.Parts) occupied.Add((x + p.Dx, y + p.Dy));
            plan.Decorations.Add(new() { Id = ParametricBuildings.Stable(plan.Id + ":tent:" + dx + ":" + dy), X = x, Y = y,
                Asset = tent.Asset, Parts = NeighborhoodPlanner.Copy(tent.Parts) });
        }
        if (o.Fences)
        {
            var fence = NeighborhoodPlanner.Copy(ParcelFences.Picket);
            plan.FenceStyles.Add(fence);
            // Only a genuine single wicket. The separate three-tile vehicle entrance stays open.
            var wicket = new GateStyle { Id = "pvr-wicket", Width = 1, ClosedW = ["fixtures_doors_fences_01_12"],
                ClosedN = ["fixtures_doors_fences_01_13"], OpenW = ["fixtures_doors_fences_01_14"], OpenN = ["fixtures_doors_fences_01_15"] };
            plan.GateStyles.Add(wicket);
            var frontY = o.Y + 10;
            plan.Fences.Add(new() { Id = ParametricBuildings.Stable(plan.Id + ":front"), Style = fence.Id,
                Points = [new(o.X + 2, frontY), new(o.X + 51, frontY)], Gates = [new() { At = 38, Style = wicket.Id, Open = true }] });
            // x=52..54 is a three-tile open vehicle gap; routes cross here.
            plan.Fences.Add(new() { Id = ParametricBuildings.Stable(plan.Id + ":front-right"), Style = fence.Id,
                Points = [new(o.X + 55, frontY), new(o.X + o.Width - 3, frontY)] });
            var edges = new List<(int X, int Y, bool North)>();
            for (var x = o.X + 2; x < o.X + o.Width - 3; x++) edges.Add((x, o.Y + o.Height - 3, true));
            for (var y = frontY + 1; y < o.Y + o.Height - 3; y++) { edges.Add((o.X + 2, y, false)); edges.Add((o.X + o.Width - 3, y, false)); }
            plan.Fences.AddRange(ParcelFences.Runs(plan.Id, fence, edges));
            // Give the wicket its own pedestrian spur, joining the same clear spine.
            for (var y = o.Y + 7; y <= o.Y + 12; y++) Paint(o.X + 40, y, Terrain.Pavement, "access");
            for (var x = o.X + 40; x <= spine; x++) Paint(x, o.Y + 12, Terrain.Pavement, "access");
            foreach (var piece in FenceLines.Compose(plan.Fences, plan.FenceStyles, plan.GateStyles, project.Width, project.Height).Pieces)
            {
                Require(piece.X, piece.Y, piece.Flags == 0);
                if (piece.Flags != 0 && routes.Contains((piece.X, piece.Y))) throw new ArgumentException("A fence blocks shelter access.");
            }
            plan.Warnings.Add("No configured three-wide vehicle gate style: retained a three-tile open vehicle gap plus a separate one-tile wicket; never three wickets presented as one gate.");
        }
        if (o.Lights) plan.Warnings.Add("Emergency shelter has no configured native lighting; no model IDs were invented.");
        plan.Warnings.Add("Authored PVR layout, not an observed site. Container HQ uses an existing industrial facade; a distinct container shell needs original assets.");
        plan.RequiredMods = plan.RequiredMods.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        plan.Surfaces = surfaces.Values.GroupBy(s => (s.Y, s.Terrain, s.Kind)).SelectMany(g =>
        {
            var row = g.OrderBy(s => s.X).ToArray(); var runs = new List<EnsembleSurface>();
            for (var i = 0; i < row.Length; i++) { var first = row[i]; var last = first.X; while (i + 1 < row.Length && row[i + 1].X == last + 1) { i++; last++; } runs.Add(first with { Width = last - first.X + 1 }); }
            return runs;
        }).ToList();
        return plan;
    }
}
