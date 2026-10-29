using System.Globalization;

namespace SimplePZMapmaker.Core.Diagnostics;

public sealed record TileDefinitionDifference(string Kind, string Tileset, int? TileId, string? Field, string? Before, string? After);
public sealed record TileCompatibilityReport(bool Complete, string Status, string? BeforeSha256, string? AfterSha256,
    int BeforeTilesets, int AfterTilesets, int BeforeTiles, int AfterTiles, IReadOnlyList<string> Notes,
    IReadOnlyList<TileDefinitionDifference> Differences, IReadOnlyList<DoctorFinding> Findings);

public static class TileDefinitionComparer
{
    public static void Validate(TileDefinitionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Tilesets is null || snapshot.Tilesets.Count > 4096) throw new ArgumentException("Invalid snapshot tilesets.");
        var names = new HashSet<string>(StringComparer.Ordinal); var count = 0;
        foreach (var set in snapshot.Tilesets)
        {
            if (set is null || string.IsNullOrWhiteSpace(set.Name) || set.Name.Length > 160 || !names.Add(set.Name) || set.Tiles is null || set.NumericId < 0)
                throw new ArgumentException("Invalid or duplicate snapshot tileset.");
            var ids = new HashSet<int>();
            var columns = set.Columns.GetValueOrDefault(); var rows = set.Rows.GetValueOrDefault();
            if (set.Tiles.Count > 100000 || (count += set.Tiles.Count) > 400000) throw new ArgumentException("Too many snapshot tiles.");
            if (snapshot.HasStructure && (columns is < 1 or > 1024 || rows is < 1 or > 1024 ||
                set.StoredTileCount is null || set.StoredTileCount < 0 || set.StoredTileCount > (long)columns * rows ||
                set.Image is null || set.Tiles.Count != (long)columns * rows))
                throw new ArgumentException("Missing or invalid snapshot structural metadata.");
            foreach (var tile in set.Tiles)
            {
                if (tile is null || tile.Id < 0 || !ids.Add(tile.Id) || tile.Properties is null || tile.Properties.Count > 256 ||
                    tile.Properties.Any(p => string.IsNullOrEmpty(p.Key) || p.Key.Length > 4096 || p.Value is null || p.Value.Length > 4096) ||
                    snapshot.HasStructure && (tile.Id >= (long)columns * rows || tile.Stored != (tile.Id < set.StoredTileCount)))
                    throw new ArgumentException("Invalid or duplicate tile slot/properties in snapshot.");
            }
        }
    }

    public static TileCompatibilityReport Compare(TileDefinitionSnapshot before, TileDefinitionSnapshot after,
        Func<string, bool>? installedTileExists = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); Validate(before); Validate(after);
        var differences = new List<TileDefinitionDifference>(); var findings = new List<DoctorFinding>(); var notes = new List<string>();
        var a = before.Tilesets.ToDictionary(s => s.Name, StringComparer.Ordinal); var b = after.Tilesets.ToDictionary(s => s.Name, StringComparer.Ordinal);
        var structure = before.HasStructure && after.HasStructure;
        if (!structure) notes.Add("Structural comparison unavailable for at least one property-only catalog/text snapshot: numeric tileset IDs, image, grid and stored record counts were not inferred.");
        if (before.Sha256 is null || after.Sha256 is null) notes.Add("At least one snapshot has no original file hash; catalog snapshots are semantic property views.");
        notes.Add("TileId is the stored local numeric slot. No game-global sprite ID is invented. This comparison never merges or rewrites files.");
        void Difference(string kind, string set, int? tile, string? field, string? oldValue, string? newValue)
        { differences.Add(new(kind, set, tile, field, oldValue, newValue)); }
        static string? Value(object? value) => value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        foreach (var name in a.Keys.Union(b.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if (!a.TryGetValue(name, out var left))
            {
                Difference("tileset-added", name, null, null, null, name);
                foreach (var tile in b[name].Tiles.OrderBy(t => t.Id)) Difference("tile-added", name, tile.Id, null, null, name + "_" + tile.Id);
                continue;
            }
            if (!b.TryGetValue(name, out var right))
            {
                Difference("tileset-removed", name, null, null, name, null);
                foreach (var tile in left.Tiles.OrderBy(t => t.Id)) Difference("tile-removed", name, tile.Id, null, name + "_" + tile.Id, null);
                continue;
            }
            if (structure)
            {
                void Field(string key, object? oldValue, object? newValue) { if (!Equals(oldValue, newValue)) Difference("structure", name, null, key, Value(oldValue), Value(newValue)); }
                Field("numericId", left.NumericId, right.NumericId); Field("image", left.Image, right.Image);
                Field("columns", left.Columns, right.Columns); Field("rows", left.Rows, right.Rows); Field("storedTileCount", left.StoredTileCount, right.StoredTileCount);
            }
            var lt = left.Tiles.ToDictionary(t => t.Id); var rt = right.Tiles.ToDictionary(t => t.Id);
            foreach (var id in lt.Keys.Union(rt.Keys).Order())
            {
                if (!lt.TryGetValue(id, out var l)) { Difference("tile-added", name, id, null, null, name + "_" + id); continue; }
                if (!rt.TryGetValue(id, out var r)) { Difference("tile-removed", name, id, null, name + "_" + id, null); continue; }
                if (structure && l.Stored != r.Stored) Difference("structure", name, id, "stored", l.Stored ? "true" : "false", r.Stored ? "true" : "false");
                foreach (var key in l.Properties.Keys.Union(r.Properties.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
                {
                    l.Properties.TryGetValue(key, out var oldValue); r.Properties.TryGetValue(key, out var newValue);
                    if (oldValue != newValue) Difference("property", name, id, key, oldValue, newValue);
                }
            }
        }
        var currentNames = after.Tilesets.SelectMany(s => s.Tiles.Select(t => s.Name + "_" + t.Id)).ToHashSet(StringComparer.Ordinal);
        foreach (var set in after.Tilesets.OrderBy(s => s.Name, StringComparer.Ordinal))
        foreach (var tile in set.Tiles.OrderBy(t => t.Id))
        {
            token.ThrowIfCancellationRequested();
            foreach (var key in new[] { "SnowTile", "BurntTile" })
                if (tile.Properties.TryGetValue(key, out var target) && !string.IsNullOrWhiteSpace(target) && !currentNames.Contains(target) && installedTileExists?.Invoke(target) != true)
                    findings.Add(new("warning", "tile-compatibility", "missing-replacement", $"{set.Name}_{tile.Id}: {key} references unresolved tile '{target}'.",
                        new(Source: after.Source, Tile: set.Name + "_" + tile.Id)));
        }
        if (installedTileExists is null) notes.Add("Replacement resolution is limited to the candidate snapshot; references supplied by other catalogs remain unresolved until a complete installed catalog is connected.");
        var numericIds = before.Tilesets.Concat(after.Tilesets).All(s => s.NumericId is not null);
        if (structure && !numericIds) notes.Add("Legacy tdef version 0 has no numeric tileset IDs; stable tileset identity cannot be fully verified.");
        var complete = structure && numericIds && installedTileExists is not null;
        return new(complete, differences.Count > 0 || findings.Count > 0 || !complete ? "warning" : "pass", before.Sha256, after.Sha256,
            a.Count, b.Count, before.Tilesets.Sum(s => s.Tiles.Count), after.Tilesets.Sum(s => s.Tiles.Count), notes, differences, findings);
    }
}
