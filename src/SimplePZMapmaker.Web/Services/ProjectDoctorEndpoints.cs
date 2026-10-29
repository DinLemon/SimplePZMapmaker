using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Diagnostics;
using SimplePZMapmaker.Core.Export;

namespace SimplePZMapmaker.Web.Services;

public sealed record ProjectDoctorTileComparison(TileDefinitionSnapshot Before, TileDefinitionSnapshot After);
public sealed record ProjectDoctorTileUpload(string Base64, string? FileName = null);

/// <summary>Read-only adapters. Parent host explicitly invokes MapProjectDoctor; no startup side effects.</summary>
public static class ProjectDoctorEndpoints
{
    public sealed record CatalogEntry(string Id, string Source, string FileName, string Format);
    sealed record CatalogFile(CatalogEntry Entry, string Path);

    public static IEndpointRouteBuilder MapProjectDoctor(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/project-doctor/check", (ProjectDoctorRequest request, GameAssetCatalog assets, StaticModelCatalog models, CancellationToken token) =>
            Analyze(request, assets, models, token));
        app.MapGet("/api/project-doctor/tiles/catalogs", (GameAssetCatalog assets) =>
        {
            var files = CatalogFiles(assets);
            return new { available = assets.Ready, catalogs = files.Select(f => f.Entry),
                note = "Only the configured game's definition files and registered active mod layers are listed. Text files provide properties only. Catalog reloads invalidate selections." };
        });
        app.MapGet("/api/project-doctor/tiles/snapshot", (string catalogId, GameAssetCatalog assets) => InstalledSnapshot(assets, catalogId));
        app.MapPost("/api/project-doctor/tiles/read", (ProjectDoctorTileUpload request, CancellationToken token) => ReadUpload(request, token));
        app.MapPost("/api/project-doctor/tiles/compare", (ProjectDoctorTileComparison request, GameAssetCatalog assets, CancellationToken token) => Compare(request, assets, token));
        return app;
    }

    public static TileDefinitionSnapshot ReadUpload(ProjectDoctorTileUpload request, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        const int maximumEncoded = ((TileDefinitionSnapshot.MaximumBytes + 2) / 3) * 4;
        // Reject before allocating decoded bytes, including whitespace-padded base64 bodies.
        if (request.Base64 is null || request.Base64.Length == 0 || request.Base64.Length > maximumEncoded ||
            request.Base64.Length % 4 != 0 || request.Base64.Any(char.IsWhiteSpace))
            throw new ArgumentException("Supply canonical base64 for a binary .tiles file up to 32 MiB.");
        var padding = request.Base64.EndsWith("==", StringComparison.Ordinal) ? 2 : request.Base64.EndsWith('=') ? 1 : 0;
        if ((long)request.Base64.Length / 4 * 3 - padding > TileDefinitionSnapshot.MaximumBytes)
            throw new ArgumentException("Decoded tile definition exceeds 32 MiB.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(request.Base64); }
        catch (FormatException e) { throw new ArgumentException("Invalid tile definition base64.", e); }
        if (bytes.Length > TileDefinitionSnapshot.MaximumBytes) throw new ArgumentException("Decoded tile definition exceeds 32 MiB.");
        token.ThrowIfCancellationRequested();
        // Caller filenames are labels only and are never opened as local paths.
        var name = request.FileName is { Length: > 0 and <= 160 } label && !label.Any(char.IsControl)
            ? label.Replace('\\', '/').Split('/')[^1] : "uploaded .tiles";
        using var stream = new MemoryStream(bytes, false);
        return ReadBinary(stream, name);
    }

    public static TileCompatibilityReport Compare(ProjectDoctorTileComparison request, GameAssetCatalog assets, CancellationToken token = default)
    {
        if (request is null || request.Before is null || request.After is null) throw new ArgumentException("Supply before and after tile snapshots.");
        return TileDefinitionComparer.Compare(request.Before, request.After, assets.Ready ? id => Exists(assets, id) : null, token);
    }

    public static ProjectDoctorReport Analyze(ProjectDoctorRequest request, GameAssetCatalog assets, StaticModelCatalog models, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        IReadOnlyList<CellConflicts.InstalledMap>? maps = null; string? scanNote = null;
        if (assets.Ready && Directory.Exists(Path.Combine(assets.GamePath, "media", "maps")))
        {
            try
            {
                // Scan only known active source folders; never accept a directory supplied by an HTTP caller.
                maps = assets.ActiveSources().SelectMany(source => CellConflicts.Scan(Path.Combine(ModRegistry.SafePath(source.Root), "media", "maps"), source.Id))
                    .OrderBy(m => m.Source, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal).ToArray();
                scanNote = "Scanned the configured game and active mod layers only. Inactive Workshop/local mods were not scanned. The existing scanner skips links and inaccessible folders and searches at most eight levels.";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { scanNote = "Installed map scan unavailable: " + e.Message; }
        }
        var context = new ProjectDoctorContext
        {
            ValidateMapResources = assets.Ready ? p => { AssetRules.Project(p, assets); models.Validate(p); } : null,
            ValidateWorldResources = assets.Ready ? w => { WorldEndpoints.Validate(w, assets, models); } : null,
            TileProperties = assets.Ready ? id => assets.Get(id).Properties : null,
            InstalledMaps = maps, InstalledMapsCoverageNote = scanNote
        };
        return ProjectDoctor.Analyze(request, context, token);
    }

    public static TileDefinitionSnapshot ReadBinary(Stream stream, string source = "snapshot")
    {
        // Keep acceptance aligned with the installed mod loader; the diagnostic reader retains additional structure.
        TileDefinitionReader.Read(stream); stream.Position = 0;
        return TileDefinitionSnapshot.Read(stream, source);
    }

    public static TileDefinitionSnapshot InstalledSnapshot(GameAssetCatalog assets, string catalogId)
    {
        if (string.IsNullOrWhiteSpace(catalogId) || catalogId.Length != 64) throw new ArgumentException("Choose a catalog returned by /tiles/catalogs.");
        var file = CatalogFiles(assets).SingleOrDefault(f => f.Entry.Id == catalogId) ?? throw new ArgumentException("Catalog is no longer installed/active. Reload the list.");
        var path = ModRegistry.SafePath(file.Path);
        if (file.Entry.Format == "binary") { using var stream = File.OpenRead(path); return ReadBinary(stream, file.Entry.Source + "/" + file.Entry.FileName); }
        // Hold a shared-read handle across parsing and hashing so a source update cannot mix two file revisions.
        using var text = File.OpenRead(path);
        var tiles = TileDefinitionReader.ReadText(path);
        if (tiles.Count == 0) throw new ArgumentException("The existing text reader found no tile records; this file's syntax is unsupported or empty.");
        var hash = Convert.ToHexString(SHA256.HashData(text)).ToLowerInvariant();
        return TileDefinitionSnapshot.FromProperties(file.Entry.Source + "/" + file.Entry.FileName, tiles.Select(t => (t.Id, (IReadOnlyDictionary<string, string>)t.Properties)), hash);
    }

    static bool Exists(GameAssetCatalog assets, string id)
    {
        try { assets.Get(id); return true; } catch (ArgumentException) { return false; }
    }

    static IReadOnlyList<CatalogFile> CatalogFiles(GameAssetCatalog assets)
    {
        if (!assets.Ready) return [];
        var result = new List<CatalogFile>();
        // ModsStatus is the existing public view; no private catalog state or parallel mod discovery is used.
        var status = JsonSerializer.SerializeToElement(assets.ModsStatus());
        var active = status.GetProperty("active").EnumerateArray().Select(v => v.GetString()!).ToHashSet(StringComparer.Ordinal);
        var registrations = status.GetProperty("mods").EnumerateArray()
            .Where(m => active.Contains(m.GetProperty("Id").GetString()!))
            .ToDictionary(m => m.GetProperty("Id").GetString()!, m => m.GetProperty("TileDefinitions").EnumerateArray().Select(v => v.GetString()!).ToArray(), StringComparer.Ordinal);
        foreach (var source in assets.ActiveSources().OrderBy(s => s.Id, StringComparer.Ordinal))
        {
            var media = ModRegistry.SafePath(Path.Combine(source.Root, "media"));
            var names = source.RequiredModId is null
                ? new[] { "newtiledefinitions", "tiledefinitions_erosion", "jumbo_trees", "jumbo_trees_big" }
                : registrations.GetValueOrDefault(source.RequiredModId, []).Select(r => r.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "").ToArray();
            foreach (var name in names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (!Regex.IsMatch(name, @"^[A-Za-z0-9_-]{1,160}$")) continue;
                var binary = Path.Combine(media, name + ".tiles"); var text = binary + ".txt";
                // GameAssetCatalog uses game text files, while registered mods prefer binary.
                var selected = source.RequiredModId is null ? File.Exists(text) ? text : null : File.Exists(binary) ? binary : File.Exists(text) ? text : null;
                if (selected is null) continue;
                selected = ModRegistry.SafePath(selected);
                if (result.Count >= 4096) throw new ArgumentException("Too many installed tile definition files.");
                var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.Id + "\n" + selected))).ToLowerInvariant();
                result.Add(new(new(id, source.Id, Path.GetFileName(selected), selected.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? "text-properties" : "binary"), selected));
            }
        }
        return result.OrderBy(f => f.Entry.Source, StringComparer.Ordinal).ThenBy(f => f.Entry.FileName, StringComparer.Ordinal).ToArray();
    }
}
