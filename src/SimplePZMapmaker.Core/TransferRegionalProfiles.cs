using System.Collections.ObjectModel;

namespace SimplePZMapmaker.Core;

public sealed record RegionalRoadDecision(Terrain Terrain, string Profile, bool IsInferred, string Reason,
    IReadOnlyDictionary<string, string> SourceTags);

/// <summary>One sprite from the active native catalog, including its real owner and native properties. No image data is copied.</summary>
public sealed record RegionalSprite(string Id, IReadOnlyDictionary<string, string> Properties, string? ModId = null,
    IReadOnlyList<string>? Requirements = null);

public sealed record RuralAppearance(WallSet? ExternalWall, string? RoofTileset, string? FlatRoofTile,
    string? TrimNorth, string? TrimWest, IReadOnlyDictionary<string, string> AssetSources,
    IReadOnlyList<string> RequiredMods, IReadOnlyList<string> Warnings);

public sealed record RuralPalette(FenceStyle? Fence, IReadOnlyList<EnsembleResource> Objects, GateStyle? VehicleGate,
    IReadOnlyDictionary<string, string> AssetSources, IReadOnlyList<string> RequiredMods, IReadOnlyList<string> Warnings);

/// <summary>Opt-in authored defaults. A small Pilna listing sample is never treated as national source evidence.</summary>
public static class TransferRegionalProfiles
{
    static readonly int[] AutomaticRoofIndices=[..Enumerable.Range(0,30).Where(i=>i%8<6),..Enumerable.Range(64,54).Where(i=>i%8<6)];
    /// <summary>Original/mod assets opt in through CustomName=rural-role[-color], Facing=N/W. Only active catalog sprites qualify.</summary>
    public static RuralPalette SelectRuralPalette(IReadOnlyList<RegionalSprite> available, string color = "green", GateStyle? vehicleGate = null)
    {
        if (color is not ("green" or "maroon")) throw new ArgumentException("Choose green or maroon rural corrugated panels.");
        // Reuse availability/ownership validation, without depending on its appearance choices.
        _ = SelectRuralAppearance(available);
        var used = new List<RegionalSprite>(); var objects = new List<EnsembleResource>(); var warnings = new List<string>();
        RegionalSprite? Find(string role, string direction) => available.Where(s => s.Properties.GetValueOrDefault("CustomName") == "rural-" + role
            && s.Properties.GetValueOrDefault("Facing") == direction).OrderBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault();
        var north = Find("corrugated-" + color, "N"); var west = Find("corrugated-" + color, "W");
        FenceStyle? fence = null;
        if (north is not null && west is not null && north.Properties.ContainsKey("collideN") && west.Properties.ContainsKey("collideW"))
        {
            used.AddRange([north, west]); fence = new() { Id = "rural-" + ParametricBuildings.Stable(north.Id + ":" + west.Id), N = north.Id, W = west.Id };
        }
        else warnings.Add($"No available complete {color} corrugated N/W fence; use default picket.");
        foreach (var role in new[] { "trim", "shutters", "chimney", "awning", "antenna" })
        {
            var found = false;
            foreach (var direction in new[] { "N", "W" })
            {
                var sprite = Find(role, direction);
                if (sprite is null) continue;
                if (role is "trim" or "shutters" or "awning" && (!sprite.Properties.ContainsKey("WallOverlay") || !sprite.Properties.ContainsKey("attached" + direction))) continue;
                used.Add(sprite); found = true;
                objects.Add(new(role + "-" + direction.ToLowerInvariant(), sprite.Id, [new(0, 0, sprite.Id)], sprite.ModId, sprite.Requirements?.ToList()));
            }
            if (!found) warnings.Add($"No verified rural {role} sprite available; omitted.");
        }
        warnings.AddRange(VehicleGateWarnings(vehicleGate));
        if (vehicleGate is not null)
        {
            var byId = available.ToDictionary(s => s.Id, StringComparer.Ordinal);
            foreach (var id in vehicleGate.ClosedN.Concat(vehicleGate.ClosedW).Concat(vehicleGate.OpenN).Concat(vehicleGate.OpenW))
            {
                if (!byId.TryGetValue(id, out var sprite)) throw new ArgumentException("Configured vehicle gate references an unavailable sprite: " + id);
                used.Add(sprite);
            }
        }
        var sources = used.Where(s => s.ModId is not null).DistinctBy(s => s.Id).ToDictionary(s => s.Id, s => s.ModId!, StringComparer.Ordinal);
        var mods = used.SelectMany(s => (s.Requirements ?? []).Concat(s.ModId is {} mod ? [mod] : [])).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(fence, objects, vehicleGate is null ? null : NeighborhoodPlanner.Copy(vehicleGate), new ReadOnlyDictionary<string, string>(sources), mods, warnings);
    }
    public static Terrain RoadTerrain(IReadOnlyDictionary<string, string> tags, string profile = "default") => ExplainRoad(tags, profile).Terrain;

    public static RegionalRoadDecision ExplainRoad(IReadOnlyDictionary<string, string> tags, string profile = "default")
    {
        ArgumentNullException.ThrowIfNull(tags);
        if (profile is not ("default" or "ru-rural")) throw new ArgumentException("Unknown transfer regional profile.", nameof(profile));
        var source = new ReadOnlyDictionary<string, string>(tags.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
        var surface = tags.GetValueOrDefault("surface")?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(surface))
        {
            Terrain? explicitTerrain = surface switch
            {
                "asphalt" or "concrete" or "concrete:lanes" or "concrete:plates" or "paved" => Terrain.Road,
                "paving_stones" or "sett" or "cobblestone" or "unhewn_cobblestone" or "bricks" or "wood" or "metal" => Terrain.Pavement,
                "unpaved" or "dirt" or "earth" or "ground" or "gravel" or "fine_gravel" or "compacted" or "pebblestone" or "mud" or "clay" => Terrain.Dirt,
                "sand" => Terrain.Sand,
                "grass" or "grass_paver" => Terrain.Grass,
                _ => null
            };
            // Unknown explicit surfaces must not turn into alleged asphalt through a regional default.
            return new(explicitTerrain ?? Terrain.Dirt, profile, explicitTerrain is null,
                explicitTerrain is null ? $"Unsupported explicit OSM surface '{surface}'; conservative Dirt rendering, original tag retained."
                    : $"Explicit OSM surface '{surface}' mapped to the available terrain classes.", source);
        }
        var highway = tags.GetValueOrDefault("highway")?.Trim().ToLowerInvariant();
        var terrain = highway switch
        {
            "motorway" or "motorway_link" or "trunk" or "trunk_link" or "primary" or "primary_link" or "secondary" or "secondary_link"
                or "tertiary" or "tertiary_link" or "residential" or "unclassified" or "living_street" or "road" => Terrain.Road,
            "service" => profile == "ru-rural" ? Terrain.Dirt : Terrain.Road,
            "footway" or "pedestrian" or "cycleway" or "steps" => profile == "ru-rural" ? Terrain.Dirt : Terrain.Pavement,
            _ => Terrain.Dirt
        };
        return new(terrain, profile, true, $"Profile '{profile}' inferred {terrain} for highway '{highway ?? "unknown"}' without a surface tag; authored default, not a source observation.", source);
    }

    /// <summary>Choose only complete, available exterior sets and native directional trim. Ordering is independent of catalog enumeration.</summary>
    public static RuralAppearance SelectRuralAppearance(IReadOnlyList<RegionalSprite> available, string material = "wood", int seed = 42, WallSet? externalWall = null)
    {
        ArgumentNullException.ThrowIfNull(available);
        if (available.Count > 100000 || available.Any(s => s is null || !VisualStyles.TileId(s.Id) || s.Properties is null)
            || available.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != available.Count)
            throw new ArgumentException("Supply unique sprites from the active native catalog.");
        foreach (var sprite in available)
            ResourceDependencies.Validate(sprite.Requirements?.ToList() ?? [], sprite.ModId is {} owner ? new() { [sprite.Id] = owner } : []);
        var sprites = available.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var preferred = material switch { "brick" => "brick", "plaster" => "modern", _ => "cottage" };
        var style = VisualStyles.All[preferred];
        string[] Tiles(WallSet w) => new[] { w.West, w.North, w.NorthWest, w.SouthEast, w.WestWindow, w.NorthWindow, w.WestDoor, w.NorthDoor }.OfType<string>().ToArray();
        var sets = WallCatalog.Sets.Where(w => w.WestWindow is not null && w.NorthWindow is not null && w.WestDoor is not null && w.NorthDoor is not null
            && Tiles(w).All(sprites.ContainsKey) && sprites[w.West].Properties.ContainsKey("WallW") && sprites[w.North].Properties.ContainsKey("WallN"))
            .OrderBy(w => w.West == $"{style.Wall}_{style.WallBase}" ? 0 : 1).ThenBy(w => w.West, StringComparer.Ordinal).ToArray();
        if (externalWall is not null && (!Tiles(externalWall).All(sprites.ContainsKey)
            || !sprites[externalWall.West].Properties.ContainsKey("WallW") || !sprites[externalWall.North].Properties.ContainsKey("WallN")
            || externalWall.WestWindow is null || externalWall.NorthWindow is null || externalWall.WestDoor is null || externalWall.NorthDoor is null))
            throw new ArgumentException("Original external wall bindings must resolve to a complete active native set.");
        var wall = externalWall ?? sets.FirstOrDefault();
        if (wall is null) warnings.Add("No complete available exterior wall set; retained existing walls.");
        else foreach (var id in Tiles(wall)) used.Add(id);
        string? roof = new[] { style.Roof, "roofs_30_03", "roofs_30_01", "roofs_30_02" }.Distinct()
            .FirstOrDefault(prefix => AutomaticRoofIndices.All(i => sprites.ContainsKey(prefix + "_" + i)));
        if (roof is not null) foreach (var i in AutomaticRoofIndices) used.Add(roof + "_" + i);
        else warnings.Add("No complete available automatic roof tileset; retained existing pitched roof.");
        var flat = new[] { "roofs_flat_01_0", style.Floor }.FirstOrDefault(id => sprites.TryGetValue(id, out var s)
            && s.Properties.ContainsKey("solidfloor") && !s.Properties.ContainsKey("water"));
        if (flat is not null) used.Add(flat);
        else warnings.Add("No available dry flat roof surface; retained existing flat roof.");
        string? Trim(string direction)
        {
            var choices = available.Where(s => s.Properties.ContainsKey("WallOverlay") && s.Properties.ContainsKey("attached" + direction)
                && s.Id.StartsWith("walls_detailing_", StringComparison.Ordinal) && !s.Properties.ContainsKey("FasciaEdge"))
                .OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
            if (choices.Length == 0) return null;
            var selected = choices[(int)(unchecked((uint)seed) % choices.Length)].Id;
            used.Add(selected); return selected;
        }
        var trimN = Trim("N"); var trimW = Trim("W");
        if (trimN is null || trimW is null) warnings.Add("Complete N/W native trim unavailable; missing directions omitted.");
        warnings.Add("Rural appearance is an authored profile. Colored boards, matching shutters, chimney, awning and antenna need verified native sprites or an original owned tile mod; no listing statistics imply country-wide facts.");
        var sources = used.Where(id => sprites[id].ModId is not null).Order(StringComparer.Ordinal).ToDictionary(id => id, id => sprites[id].ModId!, StringComparer.Ordinal);
        var mods = used.SelectMany(id => (sprites[id].Requirements ?? []).Concat(sprites[id].ModId is {} mod ? [mod] : []))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(wall, roof, flat, trimN, trimW, new ReadOnlyDictionary<string, string>(sources), mods, warnings);
    }

    /// <summary>Pure application to unmodified exterior edges only; the caller owns project-level dependency merging.</summary>
    public static Building ApplyRuralAppearance(Building source, RuralAppearance appearance)
    {
        BuildingDesigner.Validate(source);
        var b = NeighborhoodPlanner.Copy(source);
        if (b.Modified || b.Design is null) return b;
        var d = b.Design;
        foreach (var floor in d.Floors) foreach (var edge in floor.Edges.Where(e => !e.Edited))
        {
            var a = BuildingDesigner.Cell(b, floor, edge.X, edge.Y);
            var other = BuildingDesigner.Cell(b, floor, edge.X - (edge.Edge == "w" ? 1 : 0), edge.Y - (edge.Edge == "n" ? 1 : 0));
            if ((a == '.') == (other == '.') || edge.Kind == "open") continue;
            if (edge.WallTile is null && appearance.ExternalWall is {} wall) edge.WallTile = wall.Edge(edge.Kind, edge.Edge == "n");
            if (edge.Kind == "window" && edge.OverlayTile is null) edge.OverlayTile = edge.Edge == "n" ? appearance.TrimNorth : appearance.TrimWest;
        }
        if (d.RoofTileset is null && appearance.RoofTileset is {} roof) d.RoofTileset = roof;
        if (d.FlatRoofTile is null && appearance.FlatRoofTile is {} flat) d.FlatRoofTile = flat;
        foreach (var (id, owner) in appearance.AssetSources)
        {
            if (b.AssetSources.TryGetValue(id, out var prior) && prior != owner) throw new ArgumentException("Rural appearance resource owner conflicts with the building.");
            b.AssetSources[id] = owner;
        }
        b.RequiredMods = b.RequiredMods.Concat(appearance.RequiredMods).Distinct(StringComparer.Ordinal).ToList();
        BuildingDesigner.Validate(b);
        return b;
    }

    public static IReadOnlyList<string> VehicleGateWarnings(GateStyle? configured)
    {
        if (configured is null) return ["No configured three-wide vehicle gate style: use picket fence, a one-tile wicket and a separate three-tile open vehicle gap."];
        FenceLines.Validate([], [], [configured], 256);
        if (configured.Width != 3) throw new ArgumentException("A vehicle gate must be one three-wide GateStyle, not three one-tile wickets.");
        if (new[] { configured.ClosedW, configured.ClosedN, configured.OpenW, configured.OpenN }.Any(parts => parts.Count > 0 && parts.Distinct(StringComparer.Ordinal).Count() != 3))
            throw new ArgumentException("A three-panel gate needs distinct native panel bindings; repeating a wicket is not a vehicle gate.");
        return ["Three-wide editor gate artwork configured; verify a coupled native three-leaf/sliding mechanism in the client. GateStyle alone does not implement linked game door behavior."];
    }
}
