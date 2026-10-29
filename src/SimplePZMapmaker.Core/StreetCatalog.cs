using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SimplePZMapmaker.Core;

public sealed class StreetCatalogDocument
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = "";
    public int Revision { get; set; }
    public StreetCatalogSource Source { get; set; } = new();
    public List<StreetCatalogEntry> Entries { get; set; } = [];
    public List<StreetNeighborhoodRule> Rules { get; set; } = [];
}

public sealed class StreetCatalogSource
{
    public string Repository { get; set; } = "";
    public string Commit { get; set; } = "";
    public string License { get; set; } = "";
    public List<string> Files { get; set; } = [];
}

public sealed class StreetCatalogEntry
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Kind { get; set; } = "";
    public Dictionary<string, List<StreetCatalogPart>> Variants { get; set; } = [];
}

// Signed offsets and original TileZed layers are retained verbatim, including
// the coincident bumper/line at a parking anchor. Never derive IDs arithmetically.
public sealed record StreetCatalogPart(int Dx, int Dy, string Tile, string Layer);
public sealed record StreetNeighbor(int Dx, int Dy, string Terrain);

public sealed class StreetNeighborhoodRule
{
    public string Id { get; set; } = "";
    public string Entry { get; set; } = "";
    public string Variant { get; set; } = "";
    public string Center { get; set; } = "";
    public List<StreetNeighbor> Neighbors { get; set; } = [];
    public int Priority { get; set; }
    public int Every { get; set; } = 1;
    public int Phase { get; set; }
    public string Axis { get; set; } = "x";
}

/// <summary>Immutable, bounded JSON catalog. Loading never executes Lua or fetches remote content.</summary>
public sealed class StreetCatalog
{
    public const int SchemaVersion = 1, MaximumBytes = 1_048_576;
    public const string ResourceName = "SimplePZMapmaker.Core.StreetCatalog.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 24, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly byte[] canonical;
    internal StreetCatalogDocument Data { get; }
    public string Fingerprint { get; }
    public StreetCatalogDocument Snapshot() => JsonSerializer.Deserialize<StreetCatalogDocument>(canonical, Json)!;

    private StreetCatalog(StreetCatalogDocument data)
    {
        Validate(data);
        // Canonical ordering makes equivalent dictionary/object order irrelevant.
        data.Entries = data.Entries.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
        foreach (var entry in data.Entries)
            entry.Variants = entry.Variants.OrderBy(v => v.Key, StringComparer.Ordinal).ToDictionary(v => v.Key, v => v.Value);
        data.Rules = data.Rules.OrderByDescending(r => r.Priority).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        canonical = JsonSerializer.SerializeToUtf8Bytes(data, Json);
        if (canonical.Length > MaximumBytes) throw new ArgumentException("Street catalog exceeds 1 MiB.");
        Data = data;
        Fingerprint = Convert.ToHexString(SHA256.HashData(canonical));
    }

    public static StreetCatalog LoadDefault(Stream? fallback = null)
    {
        using var embedded = typeof(StreetCatalog).Assembly.GetManifestResourceStream(ResourceName);
        if (embedded is not null) return Load(embedded);
        if (fallback is not null) return Load(fallback);
        throw new ArgumentException("Embed StreetCatalog.json or supply the street-catalog.json stream explicitly.");
    }

    /// <summary>Leaves the caller's stream open, including on validation failure.</summary>
    public static StreetCatalog Load(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int count;
        while ((count = input.Read(chunk, 0, (int)Math.Min(chunk.Length, MaximumBytes + 1 - buffer.Length))) > 0)
        {
            buffer.Write(chunk, 0, count);
            if (buffer.Length > MaximumBytes) throw new ArgumentException("Street catalog exceeds 1 MiB.");
        }
        try
        {
            using var parsed = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
            RejectDuplicates(parsed.RootElement);
            return new(JsonSerializer.Deserialize<StreetCatalogDocument>(parsed.RootElement, Json)
                ?? throw new ArgumentException("Street catalog is missing."));
        }
        catch (JsonException e) { throw new ArgumentException("Invalid street catalog JSON: " + e.Message, e); }
    }

    public static StreetCatalog FromDocument(StreetCatalogDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);
        using var input = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(document, Json));
        return Load(input);
    }

    public StreetCatalog Update(Stream input)
    {
        var next = Load(input);
        if (next.Data.Id != Data.Id || next.Data.Revision <= Data.Revision)
            throw new ArgumentException("Catalog updates must retain their ID and increase revision.");
        return next;
    }

    private static void RejectDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException("Duplicate street catalog property: " + property.Name);
                RejectDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicates(item);
    }

    private static bool Key(string? value) => value is { Length: > 0 and <= 80 }
        && Regex.IsMatch(value, "^[a-z0-9][a-z0-9_.-]*$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static bool Text(string? value, int max) => value is { Length: > 0 } && value.Length <= max && !value.Any(char.IsControl);
    internal static bool Ground(string? value) => value is "Road" or "Pavement" or "Grass" or "Dirt" or "Sand" or "Forest";

    public static void Validate(StreetCatalogDocument d)
    {
        if (d is null || d.SchemaVersion != SchemaVersion || !Key(d.Id) || d.Revision is < 1 or > 1_000_000)
            throw new ArgumentException("Street catalog requires schemaVersion 1, an ID and revision 1–1000000.");
        var source = d.Source;
        if (source is null || !Text(source.Repository, 240) || !Uri.TryCreate(source.Repository, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment) || !Text(source.Commit, 40) || source.Commit.Length != 40
            || source.Commit.Any(c => !Uri.IsHexDigit(c)) || !Text(source.License, 120)
            || source.Files is null || source.Files.Count is < 1 or > 16
            || source.Files.Any(f => !Text(f, 180) || f.Contains("..", StringComparison.Ordinal) || f.StartsWith('/')
                || f.Contains('\\') || f.Contains(':')))
            throw new ArgumentException("Street catalog needs pinned HTTPS provenance, license and relative source paths.");
        if (d.Entries is null || d.Entries.Count is < 1 or > 64 || d.Rules is null || d.Rules.Count > 128)
            throw new ArgumentException("Street catalog permits 1–64 entries and at most 128 rules.");
        var entries = new Dictionary<string, StreetCatalogEntry>(StringComparer.Ordinal);
        var totalParts = 0;
        foreach (var e in d.Entries)
        {
            if (e is null || !Key(e.Id) || !entries.TryAdd(e.Id, e) || !Text(e.Label, 120)
                || e.Kind is not ("curb" or "drain" or "parking" or "marking")
                || e.Variants is null || e.Variants.Count is < 1 or > 64)
                throw new ArgumentException("Invalid or duplicate street catalog entry.");
            foreach (var (key, parts) in e.Variants)
            {
                if (!Key(key) || parts is null || parts.Count is < 1 or > 32
                    || parts.Any(p => p is null || p.Dx is < -7 or > 7 || p.Dy is < -7 or > 7
                        || !VisualStyles.TileId(p.Tile) || p.Layer is not ("0_Furniture" or "0_FloorOverlay5"))
                    || parts.Select(p => (p.Dx, p.Dy, p.Layer)).Distinct().Count() != parts.Count
                    || parts.Max(p => p.Dx) - parts.Min(p => p.Dx) > 7 || parts.Max(p => p.Dy) - parts.Min(p => p.Dy) > 7)
                    throw new ArgumentException("Invalid street variant, tile, layer or complete 8×8 footprint.");
                totalParts += parts.Count;
            }
        }
        if (totalParts > 4096) throw new ArgumentException("Street catalog exceeds 4096 parts.");
        var rules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in d.Rules)
        {
            if (rule is null || !Key(rule.Id) || !rules.Add(rule.Id) || !Key(rule.Entry) || !Key(rule.Variant)
                || !entries.TryGetValue(rule.Entry, out var entry) || entry.Kind == "parking" || !entry.Variants.ContainsKey(rule.Variant)
                || !Ground(rule.Center) || rule.Neighbors is null || rule.Neighbors.Count is < 1 or > 8
                || rule.Neighbors.Any(n => n is null || n.Dx is < -1 or > 1 || n.Dy is < -1 or > 1
                    || n.Dx == 0 && n.Dy == 0 || !Ground(n.Terrain))
                || rule.Neighbors.Select(n => (n.Dx, n.Dy)).Distinct().Count() != rule.Neighbors.Count
                || rule.Priority is < 0 or > 1000 || rule.Every is < 1 or > 256
                || rule.Phase < 0 || rule.Phase >= rule.Every || rule.Axis is not ("x" or "y"))
                throw new ArgumentException("Invalid or unbounded street neighborhood rule.");
            var parts = entry.Variants[rule.Variant];
            if (parts.Select(p => (p.Dx, p.Dy)).Distinct().Count() != parts.Count)
                throw new ArgumentException("A neighborhood rule cannot stack two layers in one decoration cell.");
        }
    }
}
