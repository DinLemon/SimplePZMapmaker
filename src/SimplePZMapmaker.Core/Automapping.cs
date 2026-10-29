using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SimplePZMapmaker.Core;

public sealed record AutomappingCell(int X, int Y);
public sealed record AutomappingResource(byte Flags, string Provider = "game", string[]? RequiredMods = null);

public sealed class AutomappingOptions
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 256;
    public int Height { get; set; } = 256;
    public bool Curbs { get; set; } = true;
    public bool Drains { get; set; } = true;
    public bool Markings { get; set; } = true;
    public bool Parking { get; set; } = true;
    public string ParkingEntry { get; set; } = "parking-stall";
    public string ParkingLayer { get; set; } = "lines";
    public int MaximumDecorations { get; set; } = 2000;
    /// <summary>Additional entrance/driveway/ensemble paths known by the caller.</summary>
    public List<AutomappingCell> ReservedCells { get; set; } = [];
}

public sealed class AutomappingPlan
{
    public string Id { get; set; } = "";
    public string BaseFingerprint { get; set; } = "";
    public string CatalogFingerprint { get; set; } = "";
    public AutomappingOptions Options { get; set; } = new();
    public List<WorldDecoration> Decorations { get; set; } = [];
    public Dictionary<string, int> Placed { get; set; } = [];
    public Dictionary<string, int> Skipped { get; set; } = [];
    public Dictionary<string, string> AssetSources { get; set; } = [];
    public List<string> RequiredMods { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>Deterministic additive street automapping. Apply recomputes the proposal;
/// posted preview tiles/flags/provider metadata are never trusted.</summary>
public static class Automapping
{
    public const int MaximumArea = 65_536, MaximumDecorations = 4000, MaximumWork = 8_000_000;
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Stable(string text) => Hash(Encoding.UTF8.GetBytes(text))[..32].ToLowerInvariant();
    public static string Fingerprint(MapProject project) => Hash(JsonSerializer.SerializeToUtf8Bytes(project));

    public static AutomappingPlan Preview(MapProject project, StreetCatalog catalog, AutomappingOptions options,
        Func<string, AutomappingResource> resource)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(resource);
        ProjectValidator.Validate(project);
        ValidateOptions(project, options);
        var o = NeighborhoodPlanner.Copy(options);
        var plan = new AutomappingPlan
        {
            BaseFingerprint = Fingerprint(project), CatalogFingerprint = catalog.Fingerprint, Options = o
        };
        var placement = new ParcelPlacement(project);
        placement.ReserveEntrances();
        placement.ReservePassage(o.ReservedCells.Select(c => (c.X, c.Y)));
        var entries = catalog.Data.Entries.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var resources = new Dictionary<string, AutomappingResource>(StringComparer.Ordinal);
        var existingIds = project.Decorations.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var work = 0;
        var remaining = Math.Min(o.MaximumDecorations, ProjectValidator.MaximumDecorations - project.Decorations.Count);
        void Skip(string reason) => plan.Skipped[reason] = plan.Skipped.GetValueOrDefault(reason) + 1;
        void Work() { if (++work > MaximumWork) throw new ArgumentException("Automapping exceeds its work budget. Reduce the rectangle or rule count."); }
        bool Inside(int x, int y) => x >= o.X && y >= o.Y && x < o.X + o.Width && y < o.Y + o.Height;
        AutomappingResource Resource(string tile)
        {
            if (resources.TryGetValue(tile, out var cached)) return cached;
            var actual = resource(tile) ?? throw new ArgumentException("Street tile is unavailable: " + tile);
            if (actual.Flags > 7 || !ResourceDependencies.SafeModId(actual.Provider))
                throw new ArgumentException("Invalid street tile physical properties or provider: " + tile);
            ResourceDependencies.Validate(actual.RequiredMods?.ToList() ?? [], new() { [tile] = actual.Provider });
            if (project.AssetSources.TryGetValue(tile, out var original) && original != actual.Provider)
                throw new ArgumentException("Street tile provider changed: " + tile);
            resources[tile] = actual;
            return actual;
        }

        // A complete selected layer is atomic; a blocked line never becomes a partial stall.
        bool Put(string ruleId, StreetCatalogEntry entry, string variant, int x, int y, List<StreetCatalogPart> parts)
        {
            Work();
            if (parts.Count == 0) { Skip("missing-layer"); return false; }
            if (parts.Select(p => (p.Dx, p.Dy)).Distinct().Count() != parts.Count)
                throw new ArgumentException("Select one parking layer; this map format cannot stack decorations.");
            foreach (var part in parts)
            {
                var px = x + part.Dx; var py = y + part.Dy;
                if (!Inside(px, py)) { Skip("outside-selection"); return false; }
                if (project.Terrain[py * project.Width + px] is not (Terrain.Road or Terrain.Pavement))
                { Skip("unsuitable-ground"); return false; }
                if (!placement.Free(px, py, allowRoad: true)) { Skip("occupied-authored-or-access"); return false; }
            }
            // These source layouts place independent sprites, not a SpriteGrid
            // object. In Build 42 the three bumper pieces also have no SpriteGridPos.
            // Keep each resource's true single-tile placement, atomically per stamp.
            var groups = parts.Select(part => new List<StreetCatalogPart> { part }).ToArray();
            if (groups.Length > remaining) { Skip("decoration-budget"); return false; }
            var additions = new List<WorldDecoration>();
            foreach (var group in groups)
            {
                var left = group.Min(p => p.Dx); var top = group.Min(p => p.Dy);
                var sorted = group.OrderBy(p => p.Dy).ThenBy(p => p.Dx).ToArray();
                var id = "automap-" + Stable(catalog.Data.Id + ":" + ruleId + ":" + variant + ":" + x + ":" + y
                    + ":" + sorted[0].Layer + ":" + left + ":" + top);
                if (existingIds.Contains(id)) { Skip("existing-id"); return false; }
                var d = new WorldDecoration
                {
                    Id = id, X = x + left, Y = y + top, Asset = sorted[0].Tile,
                    Parts = sorted.Select(p => new BuildingObjects.Part(p.Dx - left, p.Dy - top, p.Tile, Resource(p.Tile).Flags)).ToList()
                };
                VisualStyles.ValidateParts(d.Asset, d.Parts);
                additions.Add(d);
            }
            foreach (var d in additions)
            {
                placement.Add(d); plan.Decorations.Add(d); existingIds.Add(d.Id);
                foreach (var part in d.Parts)
                {
                    var actual = Resource(part.Tile);
                    plan.AssetSources[part.Tile] = actual.Provider;
                    plan.RequiredMods.AddRange(actual.RequiredMods ?? []);
                    if (actual.Provider != "game") plan.RequiredMods.Add(actual.Provider);
                }
            }
            remaining -= additions.Count;
            plan.Placed[entry.Kind] = plan.Placed.GetValueOrDefault(entry.Kind) + 1;
            return true;
        }

        bool Enabled(string kind) => kind switch { "curb" => o.Curbs, "drain" => o.Drains, "marking" => o.Markings, _ => false };
        bool Ground(int x, int y, string terrain) => x >= 0 && y >= 0 && x < project.Width && y < project.Height
            && project.Terrain[y * project.Width + x].ToString() == terrain;

        // Priority applies globally, so sparse drains reserve their slots before curbs.
        // Spacing uses absolute world-tile coordinates and is stable across selections/cells.
        foreach (var rule in catalog.Data.Rules.Where(r => Enabled(entries[r.Entry].Kind)))
        {
            var entry = entries[rule.Entry];
            for (var y = o.Y; y < o.Y + o.Height; y++)
                for (var x = o.X; x < o.X + o.Width; x++)
                {
                    Work();
                    var coordinate = rule.Axis == "x" ? project.WorldX * 256 + x : project.WorldY * 256 + y;
                    if (coordinate % rule.Every != rule.Phase || !Ground(x, y, rule.Center)) continue;
                    var matches = true;
                    foreach (var neighbor in rule.Neighbors)
                    {
                        Work();
                        if (!Ground(x + neighbor.Dx, y + neighbor.Dy, neighbor.Terrain)) { matches = false; break; }
                    }
                    if (matches) Put(rule.Id, entry, rule.Variant, x, y, entry.Variants[rule.Variant]);
                }
        }

        if (o.Parking)
        {
            if (!entries.TryGetValue(o.ParkingEntry, out var parking) || parking.Kind != "parking")
                throw new ArgumentException("Choose a parking catalog entry.");
            var selectedLayer = o.ParkingLayer == "lines" ? "0_FloorOverlay5" : "0_Furniture";
            foreach (var zone in project.GameZones.Where(z => z.Vehicle).OrderBy(z => z.Id, StringComparer.Ordinal))
            {
                if (zone.X >= o.X + o.Width || zone.Y >= o.Y + o.Height || zone.X + zone.Width <= o.X || zone.Y + zone.Height <= o.Y) continue;
                if (zone.Polygon is not null) { Skip("polygon-parking-needs-explicit-stalls"); continue; }
                var variant = zone.Direction.ToLowerInvariant();
                if (!parking.Variants.TryGetValue(variant, out var allParts)) { Skip("missing-direction"); continue; }
                var vertical = variant is "n" or "s";
                var width = vertical ? 3 : 5; var height = vertical ? 5 : 3;
                if (zone.Width % width != 0 || zone.Height % height != 0) { Skip("parking-not-3x5"); continue; }
                var parts = allParts.Where(p => p.Layer == selectedLayer).ToList();
                for (var y = Math.Max(zone.Y, zone.Y + Math.Max(0, (o.Y - zone.Y) / height) * height); y < Math.Min(zone.Y + zone.Height, o.Y + o.Height); y += height)
                    for (var x = Math.Max(zone.X, zone.X + Math.Max(0, (o.X - zone.X) / width) * width); x < Math.Min(zone.X + zone.Width, o.X + o.Width); x += width)
                    {
                        Work();
                        if (x < o.X || y < o.Y || x + width > o.X + o.Width || y + height > o.Y + o.Height)
                        { Skip("outside-selection"); continue; }
                        // Reserve the entire stall, including the spaces with no paint.
                        var cells = Enumerable.Range(0, height).SelectMany(dy => Enumerable.Range(0, width).Select(dx => (X: x + dx, Y: y + dy))).ToArray();
                        if (cells.Any(c => !placement.Free(c.X, c.Y, allowRoad: true)
                            || project.Terrain[c.Y * project.Width + c.X] is not (Terrain.Road or Terrain.Pavement)))
                        { Skip("parking-footprint-or-access"); continue; }
                        var anchorX = x + (variant == "e" ? 4 : 0);
                        var anchorY = y + (variant == "s" ? 4 : 0);
                        if (Put("parking-" + zone.Id, parking, variant, anchorX, anchorY, parts))
                            foreach (var cell in cells) placement.Block(cell.X, cell.Y);
                    }
            }
            plan.Warnings.Add("Parking applies only the selected lines or bumpers layer; the catalog retains both original layers. Stacked decorations require a future map-format extension.");
        }
        if (o.Curbs) plan.Warnings.Add("Built-in neighborhood rules cover straight curb edges. Corner/join/sunken mappings are catalogued; ambiguous corners and map-edge neighbors are left untouched.");
        plan.RequiredMods = plan.RequiredMods.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        plan.Id = Stable(JsonSerializer.Serialize(plan));
        return plan;
    }

    public static MapProject Apply(MapProject project, StreetCatalog catalog, AutomappingPlan plan,
        Func<string, AutomappingResource> resource)
    {
        if (plan is null || plan.Options is null || plan.BaseFingerprint != Fingerprint(project)
            || plan.CatalogFingerprint != catalog.Fingerprint)
            throw new ArgumentException("Map or street catalog changed after preview. Preview again.");
        // Recompute from current map/catalog/resources: no caller-controlled tile IDs,
        // footprint, flags, metadata, or removed access reservation can bypass preview.
        var expected = Preview(project, catalog, plan.Options, resource);
        if (JsonSerializer.Serialize(plan) != JsonSerializer.Serialize(expected))
            throw new ArgumentException("Street preview was changed, or its game resources changed. Preview again.");
        var candidate = NeighborhoodPlanner.Copy(project);
        candidate.Decorations.AddRange(NeighborhoodPlanner.Copy(expected.Decorations));
        foreach (var (tile, source) in expected.AssetSources) candidate.AssetSources[tile] = source;
        candidate.RequiredMods = candidate.RequiredMods.Concat(expected.RequiredMods).Distinct(StringComparer.Ordinal).ToList();
        if (expected.Decorations.Count > 0) candidate.Version = Math.Max(candidate.Version, 6);
        ProjectValidator.Validate(candidate);
        return candidate;
    }

    public static void ValidateOptions(MapProject project, AutomappingOptions o)
    {
        if (o is null || o.X < 0 || o.Y < 0 || o.Width is < 1 or > 256 || o.Height is < 1 or > 256
            || (long)o.Width * o.Height > MaximumArea || (long)o.X + o.Width > project.Width || (long)o.Y + o.Height > project.Height
            || o.MaximumDecorations is < 1 or > MaximumDecorations || o.ParkingLayer is not ("lines" or "bumpers")
            || string.IsNullOrWhiteSpace(o.ParkingEntry) || o.ParkingEntry.Length > 80
            || o.ReservedCells is null || o.ReservedCells.Count > 8192
            || o.ReservedCells.Any(c => c is null || c.X < 0 || c.Y < 0 || c.X >= project.Width || c.Y >= project.Height))
            throw new ArgumentException("Automapping needs an in-map 1–256 tile rectangle, at most 4000 decorations and 8192 reserved passage cells.");
    }
}
