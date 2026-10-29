using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SimplePZMapmaker.Core.Diagnostics;

public sealed record TileRecordSnapshot(int Id, bool Stored, IReadOnlyDictionary<string, string> Properties);
public sealed record TilesetSnapshot(string Name, int? NumericId, string? Image, int? Columns, int? Rows,
    int? StoredTileCount, IReadOnlyList<TileRecordSnapshot> Tiles);

/// <summary>Numeric tile IDs are original local slots, never array indexes assigned after sorting.</summary>
public sealed record TileDefinitionSnapshot(string Source, string? Sha256, bool HasStructure, IReadOnlyList<TilesetSnapshot> Tilesets)
{
    public const int MaximumBytes = 32 * 1024 * 1024;

    /// <summary>Matches the existing tdef 0/1 binary reader, retaining the metadata it discards.</summary>
    public static TileDefinitionSnapshot Read(Stream stream, string source = "snapshot")
    {
        if (!stream.CanSeek || stream.Position != 0 || stream.Length > MaximumBytes) throw new ArgumentException("A bounded, seekable tile definition stream at offset zero is required.");
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); stream.Position = 0;
            using var reader = new BinaryReader(stream, Encoding.Latin1, true);
            var signature = reader.ReadBytes(4); if (signature.Length != 4) throw new EndOfStreamException();
            var version = 0;
            if (signature.SequenceEqual("tdef"u8.ToArray())) version = Bound(reader.ReadInt32(), 0, 1); else stream.Position = 0;
            var count = Bound(reader.ReadInt32(), 0, 4096); var sets = new List<TilesetSnapshot>();
            var names = new HashSet<string>(StringComparer.Ordinal); var total = 0;
            string Line()
            {
                var bytes = new List<byte>();
                for (var i = 0; i < 4096; i++) { var b = reader.ReadByte(); if (b == 10) return Encoding.Latin1.GetString(bytes.ToArray()).TrimEnd('\r'); bytes.Add(b); }
                throw new ArgumentException("The tile definition line is too long.");
            }
            for (var i = 0; i < count; i++)
            {
                var name = Line(); var image = Line();
                if (!Regex.IsMatch(name, @"^[A-Za-z0-9_]{1,160}$") || !names.Add(name)) throw new ArgumentException("Invalid or duplicate tileset name.");
                var columns = Bound(reader.ReadInt32(), 1, 1024); var rows = Bound(reader.ReadInt32(), 1, 1024);
                int? numericId = version > 0 ? Bound(reader.ReadInt32(), 0, int.MaxValue) : null;
                var slots = checked(columns * rows); Bound(slots, 0, 100000); total = checked(total + slots); Bound(total, 0, 200000);
                var entries = Bound(reader.ReadInt32(), 0, slots); var tiles = new List<TileRecordSnapshot>();
                for (var j = 0; j < slots; j++)
                {
                    var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
                    if (j < entries)
                    {
                        var n = Bound(reader.ReadInt32(), 0, 256);
                        for (var k = 0; k < n; k++) { var key = Line(); var value = Line(); if (key.Length == 0 || !properties.TryAdd(key, value)) throw new ArgumentException("Duplicate or empty tile property."); }
                    }
                    tiles.Add(new(j, j < entries, properties));
                }
                sets.Add(new(name, numericId, image, columns, rows, entries, tiles));
            }
            if (stream.Position != stream.Length) throw new ArgumentException("Trailing tile definition data.");
            return new(source, hash, true, sets.OrderBy(s => s.Name, StringComparer.Ordinal).ToArray());
        }
        catch (Exception e) when (e is IOException or OverflowException) { throw new ArgumentException("Invalid or truncated tile definition file.", e); }
    }

    /// <summary>Existing text/installed catalogs expose named slots and properties, not numeric tileset IDs or geometry.</summary>
    public static TileDefinitionSnapshot FromProperties(string source, IEnumerable<(string Name, IReadOnlyDictionary<string, string> Properties)> tiles, string? sha256 = null)
    {
        var groups = new Dictionary<string, List<TileRecordSnapshot>>(StringComparer.Ordinal); var count = 0;
        foreach (var tile in tiles)
        {
            if (++count > 400000) throw new ArgumentException("Too many catalog tiles.");
            var split = tile.Name.LastIndexOf('_');
            if (split < 1 || !int.TryParse(tile.Name.AsSpan(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id < 0)
                throw new ArgumentException("Tile name must end with its original numeric slot.");
            var prefix = tile.Name[..split];
            if (!groups.TryGetValue(prefix, out var records)) groups[prefix] = records = [];
            records.Add(new(id, true, new SortedDictionary<string, string>(tile.Properties.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal)));
        }
        var sets = groups.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
            new TilesetSnapshot(g.Key, null, null, null, null, null, g.Value.OrderBy(t => t.Id).ToArray())).ToArray();
        var result = new TileDefinitionSnapshot(source, sha256, false, sets); TileDefinitionComparer.Validate(result); return result;
    }
    static int Bound(int value, int min, int max) => value >= min && value <= max ? value : throw new ArgumentException("Invalid tile data size/version.");
}
