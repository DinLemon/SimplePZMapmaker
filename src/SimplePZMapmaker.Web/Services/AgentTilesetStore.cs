using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Core.Export;

namespace SimplePZMapmaker.Web.Services;

public sealed record AgentSpriteProvenance(string Kind, string Source, string Reference, string License,
    string? OriginalAuthor = null, string? Generator = null, string? Prompt = null, bool CanRedistribute = false);
public sealed record AgentSpriteFrame(string Key, string Direction = "none", int Width = 128, int Height = 256,
    string? CopyFrom = null, Dictionary<string, string>? Properties = null);
public sealed record AgentTilesetSpec(string ModId, string Name, string Tileset, int FileNumber,
    AgentSpriteProvenance Provenance, AgentSpriteFrame[] Frames);
public sealed record AgentTilesetFrameInfo(int Index, string TileId, string Key, string Direction, int Width, int Height,
    string? CopyFrom, Dictionary<string, string> Properties, bool Uploaded, string PreviewUrl);
public sealed record AgentTilesetDraft(string Id, long Revision, long BaseRevision, string Scope, string? MemberId,
    string State, AgentTilesetSpec Spec, AgentTilesetFrameInfo[] Frames, string? Sha256, string? DownloadUrl);
public sealed record AgentTilesetAttachment(string DraftId, string ModId, long Revision, string Scope, string? MemberId);
public sealed record AgentTilesetPreview(string TileId, string Origin, int Width, int Height,
    string? CopyFrom, Dictionary<string, string> Properties, AgentSpriteProvenance Provenance);

/// <summary>Workspace-only sprite drafts. Public operations accept opaque IDs, never filesystem paths.
/// Register as a singleton together with the existing singleton session stores and asset catalog.</summary>
public sealed class AgentTilesetStore
{
    public const int MaximumFrames = 64, MaximumPngBytes = 2 * 1024 * 1024;
    public const int MaximumBase64Characters = (MaximumPngBytes + 2) / 3 * 4;
    public const int MaximumDraftBytes = 16 * 1024 * 1024;
    readonly object gate = new();
    readonly string directory;
    readonly AgentProjectStore maps;
    readonly AgentWorldStore worlds;
    readonly GameAssetCatalog assets;
    public List<string> RestoreDiagnostics { get; } = [];
    static readonly JsonSerializerOptions Json = new(NativeMapExporter.JsonOptions) { MaxDepth = 32 };
    static readonly Regex Canonical = new("^[a-z][a-z0-9_]{2,63}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public AgentTilesetStore(string directory, AgentProjectStore maps, AgentWorldStore worlds, GameAssetCatalog assets)
    {
        this.directory = ModRegistry.SafePath(directory);
        this.maps = maps; this.worlds = worlds; this.assets = assets;
        RestoreAttached();
    }

    public sealed class Document
    {
        public string Id { get; set; } = "";
        public long Revision { get; set; } = 1;
        public long BaseRevision { get; set; }
        public string Scope { get; set; } = "map";
        public string? MemberId { get; set; }
        public AgentTilesetSpec Spec { get; set; } = null!;
        public Dictionary<string, string>[] Properties { get; set; } = [];
        public string[]? FrameHashes { get; set; }
        public string? Sha256 { get; set; }
    }

    public static object Capabilities() => new
    {
        format = "SimplePZAgentTileset", version = 1, gameBuild = 42,
        creation = new { supported = true, embeddedImageGeneration = false, provider = "Use the connected agent image_gen tool when available; otherwise an explicitly supplied image provider or licensed PNG. The editor has no model or image-generation credentials." },
        limits = new { maximumFrames = MaximumFrames, maximumPngBytes = MaximumPngBytes, maximumBase64Characters = MaximumBase64Characters,
            maximumDraftBytes = MaximumDraftBytes, maximumFramePixels = 262144, maximumTotalPixels = 2097152, fileNumberMin = 100, fileNumberMax = 8190 },
        canonicalNames = "Explicit lowercase modId/tileset: [a-z][a-z0-9_]{2,63}, no trailing underscore. Tile IDs remain <tileset>_<frame index>. Never guess installed tile IDs or FileNumbers; collisions are rejected.",
        recipe = new[] { "Inspect the installed tile catalog; choose optional existing vanilla copyFrom per frame for physical properties.",
            "Create a revision-bound draft with source/license/author or generator provenance and explicit directional frames.",
            "Use connected image_gen with transparent_background=true when available. Save its output and source provenance. Scale/crop/pad normally into declared native frames, then upload bounded base64. No editor image-generation credentials exist.",
            "Preview real uploaded pixels; build a separate validated B42 mod ZIP. Download it independently of the map.",
            "Explicitly attach with current expectedRevision and expectedDraftRevision; then place returned tile IDs using existing map tools." },
        imageRecipe = new { suggestedWidth = 128, suggestedHeight = 256, pixelFormat = "8-bit RGBA (or RGB), non-interlaced PNG",
            prompt = "Draw one Project Zomboid Build 42 2x sprite in isometric game view (elevated 30-degree camera), true transparent background, aligned to a transparent 128x256 native frame. Use the requested N or W orientation. Preserve scale and upright verticals. No text, labels, contact sheet, checkerboard background or ground shadow.",
            dimensions = "Native 2x default 128x256. A 64x128 legacy frame is allowed only when explicitly declared; do not silently confuse 1x with native 2x. High-resolution imagegen output may be proportionally scaled and alpha-padded into the declared frame by normal image composition tools.",
            directions = new[] { "none", "n", "w", "s", "e" },
            directionalPrompt = "Draw each requested north/west/south/east view as its own frame. Do not rotate a flat bitmap to synthesize an isometric direction." },
        provenance = "kind=generated requires generator and prompt; kind=source-authored requires originalAuthor and canRedistribute=true. Both require source, reference and license. These are declarations, not legal verification or an approval workflow.",
        endpoints = "/api/agent/tilesets", separateMod = true, writesZomboidDirectory = false
    };

    public AgentTilesetDraft Create(string token, long expectedRevision, AgentTilesetSpec spec, string scope = "map", string? memberId = null)
    {
        lock (gate)
        {
            Source(token, expectedRevision, scope, memberId);
            spec = Clone(spec); ValidateSpec(spec);
            assets.CheckAgentTilesetNames(spec.ModId, spec.Tileset, spec.FileNumber);
            var folder = SessionFolder(token); Directory.CreateDirectory(folder);
            if (Directory.EnumerateDirectories(folder).Count() >= 16) throw new ArgumentException("A session may have 16 tileset drafts.");
            // Reserve names/numbers across all workspace drafts, including inactive and not-yet-built drafts.
            foreach (var file in Documents())
            {
                var old = ReadDocumentFile(file);
                if (old.Spec.ModId.Equals(spec.ModId, StringComparison.OrdinalIgnoreCase) || old.Spec.Tileset.Equals(spec.Tileset, StringComparison.OrdinalIgnoreCase) || old.Spec.FileNumber == spec.FileNumber)
                    throw new ArgumentException("ModId, tileset or FileNumber is already reserved by a workspace draft.");
            }
            var properties = spec.Frames.Select(frame =>
            {
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                if (frame.CopyFrom is { } id)
                {
                    var original = assets.Get(id);
                    if (original.ModId is not null || original.Source != "game") throw new ArgumentException("copyFrom must name an existing vanilla tile, not another mod.");
                    foreach (var pair in original.Properties) result[pair.Key] = pair.Value;
                }
                foreach (var pair in frame.Properties ?? []) result[pair.Key] = pair.Value;
                ValidateProperties(result, frame.Direction); return result;
            }).ToArray();
            var doc = new Document { Id = Guid.NewGuid().ToString("N"), BaseRevision = expectedRevision, Scope = scope, MemberId = memberId, Spec = spec, Properties = properties };
            Source(token, expectedRevision, scope, memberId);
            Save(token, doc); return Summary(token, doc);
        }
    }

    public AgentTilesetDraft[] List(string token)
    {
        lock (gate)
        {
            var folder = SessionFolder(token);
            return Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal).Take(16)
                .Where(d => File.Exists(Path.Combine(d, "draft.json"))).Select(d => Summary(token, Load(token, Path.GetFileName(d)))).ToArray() : [];
        }
    }

    public AgentTilesetDraft Read(string token, string draftId) { lock (gate) return Summary(token, Load(token, draftId)); }

    public AgentTilesetDraft Upload(string token, string draftId, long expectedDraftRevision, int frameIndex, string pngBase64)
    {
        lock (gate)
        {
            var d = Load(token, draftId); Revision(d.Revision, expectedDraftRevision);
            if (d.Sha256 is not null) throw new ArgumentException("A built tileset is immutable. Create a new draft with new names to change it.");
            var frame = Frame(d, frameIndex);
            if (pngBase64 is null || pngBase64.Length is < 1 or > MaximumBase64Characters) throw new ArgumentException("Upload at most 2 MiB of PNG as plain base64, without a data URL.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(pngBase64); } catch (FormatException e) { throw new ArgumentException("Invalid PNG base64.", e); }
            var image = ValidatePng(bytes, frame.Width, frame.Height);
            var normalized = Png.Encode(image);
            var file = FrameFile(token, d.Id, frameIndex);
            var total = Directory.EnumerateFiles(DraftFolder(token, d.Id), "*.png").Where(f => f != file).Sum(f => new FileInfo(f).Length) + normalized.Length;
            if (total > MaximumDraftBytes) throw new ArgumentException("Draft images exceed 16 MiB.");
            // Store pixels first; a failed metadata write does not claim a new revision or a successful upload.
            var before = File.Exists(file) ? File.ReadAllBytes(file) : null;
            try { WriteAtomic(file, normalized); d.Revision = checked(d.Revision + 1); Save(token, d); }
            catch { if (before is null) File.Delete(file); else WriteAtomic(file, before); throw; }
            return Summary(token, d);
        }
    }

    public byte[] Preview(string token, string draftId, int frameIndex)
    {
        lock (gate)
        {
            var d = Load(token, draftId); var frame = Frame(d, frameIndex);
            if (d.Sha256 is not null)
            {
                VerifyBuilt(token, d);
                return assets.ReadAgentTilesetFrame(Descriptor(Path.Combine(DraftFolder(token, d.Id), "staged", d.Spec.ModId), d), d.Spec.Tileset, frameIndex).Png;
            }
            var png = ReadFrame(token, d, frameIndex);
            ValidatePng(png, frame.Width, frame.Height);
            return png;
        }
    }

    public AgentTilesetPreview PreviewInfo(string token, string draftId, int frameIndex)
    {
        lock (gate)
        {
            var d = Load(token, draftId); var frame = Frame(d, frameIndex);
            Preview(token, draftId, frameIndex); // No metadata-only preview can hide a missing/invalid image.
            var properties = d.Properties[frameIndex];
            if (d.Sha256 is not null)
                properties = assets.ReadAgentTilesetFrame(Descriptor(Path.Combine(DraftFolder(token, d.Id), "staged", d.Spec.ModId), d), d.Spec.Tileset, frameIndex).Asset.Properties;
            return new(d.Spec.Tileset + "_" + frameIndex, d.Sha256 is null ? "uploaded-png" : "validated-native-pack", frame.Width, frame.Height, frame.CopyFrom, new(properties), d.Spec.Provenance);
        }
    }

    public AgentTilesetDraft Build(string token, string draftId, long expectedDraftRevision)
    {
        lock (gate)
        {
            var d = Load(token, draftId); Revision(d.Revision, expectedDraftRevision);
            if (d.Sha256 is not null) { VerifyBuilt(token, d); return Summary(token, d); }
            var sprites = d.Spec.Frames.Select((f, i) => { var png = ReadFrame(token, d, i); ValidatePng(png, f.Width, f.Height); return new TilesetSprite(png, new(d.Properties[i])); }).ToList();
            assets.CheckAgentTilesetNames(d.Spec.ModId, d.Spec.Tileset, d.Spec.FileNumber);
            var mod = new TilesetMod(d.Spec.ModId, d.Spec.Name, d.Spec.Tileset, d.Spec.FileNumber, sprites);
            using var output = new MemoryStream(); TilesetBuilder.Write(mod, output);
            // Keep declarations with the redistributed mod, including per-frame copied property provenance.
            using (var zip = new ZipArchive(output, ZipArchiveMode.Update, true))
            {
                using var entry = zip.CreateEntry(d.Spec.ModId + "/42/agent-tileset.json").Open();
                JsonSerializer.Serialize(entry, new { format = "SimplePZAgentTileset", version = 1, d.Id, d.Spec, properties = d.Properties }, Json);
            }
            var bytes = output.ToArray();
            if (bytes.Length > 32 * 1024 * 1024) throw new ArgumentException("The tileset ZIP exceeds 32 MiB.");
            var folder = DraftFolder(token, d.Id); var package = Path.Combine(folder, "mod.zip"); var staged = Path.Combine(folder, "staged");
            var hashes = sprites.Select(s => Hash(s.Png)).ToArray();
            try
            {
                WriteAtomic(package, bytes);
                using var stream = new MemoryStream(bytes);
                // NativeModPackage sees only our own generated ZIP and a checked workspace subdirectory.
                var root = NativeModPackage.Install(stream, staged);
                var descriptor = Descriptor(root, d);
                assets.ValidateAgentTileset(descriptor, d.Spec.Tileset, d.Spec.FileNumber, hashes.Length);
                d.Sha256 = Hash(bytes); d.FrameHashes = hashes; d.Revision = checked(d.Revision + 1);
                Save(token, d);
            }
            catch { DeleteDirectory(staged); File.Delete(package); throw; }
            return Summary(token, d);
        }
    }

    public byte[] Download(string token, string draftId)
    {
        lock (gate) { var d = Load(token, draftId); VerifyBuilt(token, d); return File.ReadAllBytes(Path.Combine(DraftFolder(token, d.Id), "mod.zip")); }
    }

    public AgentTilesetAttachment Attach(string token, string draftId, long expectedRevision, long expectedDraftRevision)
    {
        lock (gate)
        {
            var d = Load(token, draftId); Revision(d.Revision, expectedDraftRevision); VerifyBuilt(token, d);
            Action rollback = () => { };
            var marker = Path.Combine(DraftFolder(token, d.Id), "attached"); var hadMarker = File.Exists(marker);
            void Register()
            {
                rollback = assets.RegisterAgentTileset(Descriptor(Path.Combine(DraftFolder(token, d.Id), "staged", d.Spec.ModId), d), d.Spec.Tileset, d.Spec.FileNumber, d.Spec.Frames.Length);
                WriteAtomic(marker, Encoding.ASCII.GetBytes(d.Sha256!));
            }
            try
            {
                long revision;
                if (d.Scope == "map") revision = maps.Mutate(token, expectedRevision, p => { Stamp(p, d); Register(); }, "agent").Revision;
                else revision = worlds.Mutate(token, expectedRevision, w =>
                {
                    var member = w.Maps.SingleOrDefault(m => m.Id == d.MemberId) ?? throw new ArgumentException("World section not found.");
                    Stamp(member.Project, d);
                    // Shared objects carry their own metadata. Do not stamp unrelated members or pretend a world has top-level RequiredMods.
                    foreach (var b in w.Buildings) StampUsed(b.RequiredMods, b.AssetSources, NativeScene.BuildingResourceIds(b), d);
                    foreach (var decoration in w.Decorations) StampUsed(decoration.RequiredMods, decoration.AssetSources, decoration.Parts.Select(p => p.Tile).Append(decoration.Asset), d);
                    Register();
                }).Revision;
                return new(d.Id, d.Spec.ModId, revision, d.Scope, d.MemberId);
            }
            catch { rollback(); if (!hadMarker) File.Delete(marker); throw; }
        }
    }

    static void Stamp(MapProject p, Document d)
    {
        foreach (var id in TileIds(d))
        {
            if (p.AssetSources.TryGetValue(id, out var owner) && owner != d.Spec.ModId) throw new ArgumentException("Tile ownership conflicts with the source project: " + id);
            p.AssetSources[id] = d.Spec.ModId;
        }
        if (!p.RequiredMods.Contains(d.Spec.ModId, StringComparer.Ordinal)) p.RequiredMods.Add(d.Spec.ModId);
        foreach (var b in p.Buildings.Concat(p.BuildingLibrary)) StampUsed(b.RequiredMods, b.AssetSources, NativeScene.BuildingResourceIds(b), d);
        p.Version = Math.Max(6, p.Version);
    }

    static void StampUsed(List<string> mods, Dictionary<string, string> sources, IEnumerable<string> used, Document d)
    {
        var ids = used.Concat(sources.Keys).Intersect(TileIds(d), StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return;
        foreach (var id in ids)
        {
            if (sources.TryGetValue(id, out var owner) && owner != d.Spec.ModId) throw new ArgumentException("Shared resource ownership conflicts: " + id);
            sources[id] = d.Spec.ModId;
        }
        if (!mods.Contains(d.Spec.ModId, StringComparer.Ordinal)) mods.Add(d.Spec.ModId);
    }

    IEnumerable<string> Documents()
    {
        if (!Directory.Exists(directory)) yield break;
        foreach (var session in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
        {
            var token = Path.GetFileName(session);
            if (token.Length != 64 || !token.All(char.IsAsciiHexDigit)) continue;
            ModRegistry.SafePath(session);
            foreach (var draft in Directory.EnumerateDirectories(session).Order(StringComparer.Ordinal))
            {
                if (!WorldAssembler.SafeId(Path.GetFileName(draft))) continue;
                ModRegistry.SafePath(draft);
                var file = Path.Combine(draft, "draft.json"); if (File.Exists(file)) yield return file;
            }
        }
    }

    void RestoreAttached()
    {
        foreach (var file in Documents())
        {
            var folder = Path.GetDirectoryName(file)!;
            if (!File.Exists(Path.Combine(folder, "attached"))) continue;
            try
            {
                var token = Path.GetFileName(Path.GetDirectoryName(folder)!); var d = ReadDocumentFile(file);
                VerifyBuilt(token, d);
                assets.RegisterAgentTileset(Descriptor(Path.Combine(folder, "staged", d.Spec.ModId), d), d.Spec.Tileset, d.Spec.FileNumber, d.Spec.Frames.Length, restoring: true);
            }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
            { RestoreDiagnostics.Add(Path.GetFileName(folder) + ": " + e.Message); }
        }
    }

    void Source(string token, long expected, string scope, string? member)
    {
        if (scope == "map" && member is null) { Revision(maps.Read(token).Revision, expected); return; }
        if (scope == "world" && member is not null)
        {
            var s = worlds.Read(token); Revision(s.Revision, expected);
            if (!s.World.Maps.Any(m => m.Id == member)) throw new ArgumentException("World section not found.");
            return;
        }
        throw new ArgumentException("Choose scope map, or scope world with an existing memberId.");
    }

    void VerifyBuilt(string token, Document d)
    {
        if (d.Sha256 is null || d.FrameHashes?.Length != d.Spec.Frames.Length) throw new ArgumentException("Build and validate the complete tileset first.");
        var file = Path.Combine(DraftFolder(token, d.Id), "mod.zip");
        if (!File.Exists(file) || new FileInfo(file).Length > 32 * 1024 * 1024 || Hash(File.ReadAllBytes(file)) != d.Sha256) throw new ArgumentException("The built ZIP is missing or changed. It cannot be published.");
        for (var i = 0; i < d.FrameHashes.Length; i++) if (Hash(ReadFrame(token, d, i)) != d.FrameHashes[i]) throw new ArgumentException("A built frame changed.");
        // Check the staged files against the trusted archive before exposing them to the catalog.
        using var zip = new ZipArchive(File.OpenRead(file));
        var root = Path.Combine(DraftFolder(token, d.Id), "staged");
        foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
        {
            var stagedFile = ModRegistry.SafePath(Path.Combine(root, entry.FullName));
            if (!stagedFile.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(stagedFile) || new FileInfo(stagedFile).Length != entry.Length) throw new ArgumentException("Staged resource is missing or changed.");
            using var input = entry.Open(); using var stagedInput = File.OpenRead(stagedFile);
            if (!SHA256.HashData(input).SequenceEqual(SHA256.HashData(stagedInput))) throw new ArgumentException("Staged resource changed.");
        }
    }

    static ModDescriptor Descriptor(string root, Document d)
    {
        var found = ModRegistry.Discover([root], "42.0.0");
        if (found.Diagnostics.Count != 0 || found.Mods.Count != 1 || !found.Mods.TryGetValue(d.Spec.ModId, out var mod)) throw new ArgumentException("The staged ZIP is not a valid separate Build 42 mod.");
        return mod;
    }

    static void ValidateSpec(AgentTilesetSpec s)
    {
        if (s is null || s.ModId is null || s.Tileset is null || !Canonical.IsMatch(s.ModId) || !Canonical.IsMatch(s.Tileset) || s.ModId.EndsWith('_') || s.Tileset.EndsWith('_')) throw new ArgumentException("Use explicit canonical lowercase modId and tileset names, 3–64 ASCII letters/digits/underscores, starting with a letter and not ending in underscore.");
        Text(s.Name, 80, "mod name");
        NativeModPackage.Validate(new(s.ModId, Name: s.Name));
        if (s.FileNumber is < 100 or > 8190 || s.Frames is null || s.Frames.Length is < 1 or > MaximumFrames) throw new ArgumentException("Choose a FileNumber in 100–8190 and 1–64 explicit frames.");
        Text(s.Provenance?.Source, 500, "provenance source"); Text(s.Provenance?.Reference, 1000, "provenance reference"); Text(s.Provenance?.License, 500, "license");
        var p = s.Provenance!;
        if (p.Kind == "source-authored") { Text(p.OriginalAuthor, 200, "original author"); if (!p.CanRedistribute) throw new ArgumentException("Declare source artwork redistribution rights before packaging it."); }
        else if (p.Kind == "generated") { Text(p.Generator, 200, "image generator/provider"); Text(p.Prompt, 4000, "image prompt"); if (!string.IsNullOrEmpty(p.OriginalAuthor)) throw new ArgumentException("Generated artwork must not claim an original source author. Credit source references in reference."); }
        else throw new ArgumentException("Provenance kind must be generated or source-authored.");
        long pixels = 0;
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in s.Frames)
        {
            if (f is null || f.Key is null || !Regex.IsMatch(f.Key, "^[a-z][a-z0-9_-]{0,47}$", RegexOptions.CultureInvariant) || !keys.Add(f.Key) || f.Direction is not ("none" or "n" or "w" or "s" or "e") || f.Width is < 1 or > 512 || f.Height is < 1 or > 1024 || (long)f.Width * f.Height > 262144) throw new ArgumentException("Frames need unique safe keys, explicit directions and dimensions up to 512x1024 / 262144 pixels.");
            pixels += (long)f.Width * f.Height;
            if (f.CopyFrom is not null && !VisualStyles.TileId(f.CopyFrom)) throw new ArgumentException("copyFrom must be an exact catalog tile ID.");
            ValidateProperties(f.Properties ?? [], f.Direction);
        }
        if (pixels > 2097152) throw new ArgumentException("A draft may contain at most 2,097,152 frame pixels.");
    }

    static void Text(string? s, int max, string name) { if (string.IsNullOrWhiteSpace(s) || s.Length > max || s.Any(char.IsControl)) throw new ArgumentException("Provide bounded single-line " + name + "."); }
    static void ValidateProperties(Dictionary<string, string> p, string direction)
    {
        // Native tiledefs are Latin-1, line delimited. Refuse characters the writer would silently corrupt.
        if (p.Count > 256 || p.Any(v => string.IsNullOrEmpty(v.Key) || v.Key.Length > 100 || v.Value is null || v.Value.Length > 1000 || v.Key.Concat(v.Value).Any(c => char.IsControl(c) || c > 255))) throw new ArgumentException("Tile properties must be bounded Latin-1 single-line keys/values.");
        if (direction != "none" && (p.TryGetValue("Facing", out var facing) && facing.Length > 0 && !facing.Equals(direction, StringComparison.OrdinalIgnoreCase) || direction == "w" && p.Keys.Any(k => k is "WallN" or "collideN" or "doorN" or "windowN") || direction == "n" && p.Keys.Any(k => k is "WallW" or "collideW" or "doorW" or "windowW"))) throw new ArgumentException("The frame direction disagrees with its physical properties. Supply a matching frame/copyFrom; do not rotate sprites.");
    }

    /// <summary>Validate structure/CRC before calling the existing bounded decoder, then canonicalize on upload.</summary>
    static Png.Image ValidatePng(byte[] png, int width, int height)
    {
        if (png.Length is < 45 or > MaximumPngBytes || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new ArgumentException("A frame must be a PNG of at most 2 MiB.");
        var at = 8; var header = false; var data = false; var end = false;
        while (at + 12 <= png.Length)
        {
            var size = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            if (size < 0 || size > png.Length - at - 12) throw new ArgumentException("Truncated PNG chunk.");
            var type = Encoding.ASCII.GetString(png, at + 4, 4); var body = png.AsSpan(at + 8, size);
            if (Crc(png.AsSpan(at + 4, size + 4)) != BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + size))) throw new ArgumentException("PNG checksum mismatch.");
            if (!header && type != "IHDR") throw new ArgumentException("PNG must start with IHDR.");
            if (type == "IHDR")
            {
                if (header || size != 13 || BinaryPrimitives.ReadInt32BigEndian(body) != width || BinaryPrimitives.ReadInt32BigEndian(body[4..]) != height || body[8] != 8 || body[9] is not (2 or 6) || body[10] != 0 || body[11] != 0 || body[12] != 0) throw new ArgumentException("PNG dimensions/format do not match the declared RGB/RGBA frame.");
                header = true;
            }
            else if (type == "IDAT") data = true;
            else if (type == "IEND") { if (size != 0 || !data || at + 12 != png.Length) throw new ArgumentException("Invalid PNG end."); end = true; }
            else if (type is "acTL" or "fcTL" or "fdAT" or "tRNS" || type[0] is >= 'A' and <= 'Z' && type != "PLTE") throw new ArgumentException("Animated/unsupported PNG chunks are not accepted. Use RGBA for transparency.");
            at += size + 12;
        }
        if (!end || at != png.Length) throw new ArgumentException("Incomplete PNG.");
        Png.Image image;
        try { image = Png.Decode(png); } catch (Exception e) when (e is IOException or OverflowException) { throw new ArgumentException("Invalid compressed PNG pixels.", e); }
        if (!image.Rgba.Where((_, i) => i % 4 == 3).Any(a => a != 0)) throw new ArgumentException("An empty transparent frame is not an actual sprite.");
        return image;
    }

    static uint Crc(ReadOnlySpan<byte> bytes) { var c = 0xffffffffu; foreach (var b in bytes) { c ^= b; for (var i = 0; i < 8; i++) c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1; } return c ^ 0xffffffffu; }
    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value, Json), Json)!;
    static void Revision(long actual, long expected) { if (actual != expected) throw new AgentRevisionConflict(actual); }
    static string[] TileIds(Document d) => Enumerable.Range(0, d.Spec.Frames.Length).Select(i => d.Spec.Tileset + "_" + i).ToArray();
    static AgentSpriteFrame Frame(Document d, int i) => i >= 0 && i < d.Spec.Frames.Length ? d.Spec.Frames[i] : throw new ArgumentException("Frame index is outside the declared frames.");
    string SessionFolder(string token)
    {
        if (token is not { Length: 64 } || !token.All(char.IsAsciiHexDigit)) throw new ArgumentException("Invalid agent session key.");
        return ModRegistry.SafePath(Path.Combine(directory, token.ToLowerInvariant()));
    }
    string DraftFolder(string token, string id) { if (!WorldAssembler.SafeId(id)) throw new ArgumentException("Invalid tileset draft ID."); return ModRegistry.SafePath(Path.Combine(SessionFolder(token), id.ToLowerInvariant())); }
    string FrameFile(string token, string id, int i) => ModRegistry.SafePath(Path.Combine(DraftFolder(token, id), i + ".png"));
    byte[] ReadFrame(string token, Document d, int i) { Frame(d, i); var f = FrameFile(token, d.Id, i); if (!File.Exists(f) || new FileInfo(f).Length > MaximumPngBytes) throw new ArgumentException("Upload a real PNG for frame " + i + " first."); return File.ReadAllBytes(f); }
    Document Load(string token, string id) { var d = ReadDocumentFile(Path.Combine(DraftFolder(token, id), "draft.json")); if (d.Id != id.ToLowerInvariant()) throw new ArgumentException("Draft identity mismatch."); return d; }
    static Document ReadDocumentFile(string file)
    {
        ModRegistry.SafePath(file);
        if (!File.Exists(file) || new FileInfo(file).Length > 2 * 1024 * 1024) throw new ArgumentException("Tileset draft not found or too large.");
        var d = JsonSerializer.Deserialize<Document>(File.ReadAllBytes(file), Json) ?? throw new ArgumentException("Invalid tileset draft.");
        ValidateSpec(d.Spec);
        if (!WorldAssembler.SafeId(d.Id) || d.Revision < 1 || d.BaseRevision < 1 || d.Scope is not ("map" or "world") || d.Scope == "map" && d.MemberId is not null || d.Scope == "world" && !WorldAssembler.SafeId(d.MemberId) || d.Properties.Length != d.Spec.Frames.Length) throw new ArgumentException("Invalid tileset draft metadata.");
        for (var i = 0; i < d.Properties.Length; i++) ValidateProperties(d.Properties[i], d.Spec.Frames[i].Direction);
        return d;
    }
    void Save(string token, Document d) { var bytes = JsonSerializer.SerializeToUtf8Bytes(d, Json); if (bytes.Length > 2 * 1024 * 1024) throw new ArgumentException("Draft metadata exceeds 2 MiB."); WriteAtomic(Path.Combine(DraftFolder(token, d.Id), "draft.json"), bytes); }
    static void WriteAtomic(string file, byte[] bytes) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); var tmp = file + "." + Guid.NewGuid().ToString("N") + ".tmp"; try { File.WriteAllBytes(tmp, bytes); File.Move(tmp, file, true); } finally { if (File.Exists(tmp)) File.Delete(tmp); } }
    void DeleteDirectory(string path) { var full = ModRegistry.SafePath(path); if (!full.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe workspace cleanup."); if (Directory.Exists(full)) Directory.Delete(full, true); }
    AgentTilesetDraft Summary(string token, Document d)
    {
        var url = "/api/agent/tilesets/" + token.ToLowerInvariant() + "/" + d.Id;
        return new(d.Id, d.Revision, d.BaseRevision, d.Scope, d.MemberId, d.Sha256 is null ? "draft" : "built", Clone(d.Spec),
            d.Spec.Frames.Select((f, i) => new AgentTilesetFrameInfo(i, d.Spec.Tileset + "_" + i, f.Key, f.Direction, f.Width, f.Height, f.CopyFrom, new(d.Properties[i]), File.Exists(FrameFile(token, d.Id, i)), url + "/frames/" + i + ".png")).ToArray(), d.Sha256, d.Sha256 is null ? null : url + "/mod.zip");
    }
}

/// <summary>The catalog has no generic AddSource API. Keep the narrow agent publication adapter here,
/// sharing its existing lock, loaders, dependency ordering and view invalidation.</summary>
public sealed partial class GameAssetCatalog
{
    internal void CheckAgentTilesetNames(string modId, string tileset, int number, string? ownRoot = null)
    {
        lock (sync)
        {
            foreach (var mod in discoveredMods.Values.Concat(activeMods).DistinctBy(m => m.Root))
            {
                if (ownRoot is not null && mod.Root.Equals(ownRoot, StringComparison.OrdinalIgnoreCase) && mod.Id == modId) continue;
                if (mod.Id.Equals(modId, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("ModId collides with an existing mod: " + modId);
                foreach (var registration in mod.TileDefinitions)
                {
                    var parts = registration.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && parts[0].Equals(tileset, StringComparison.OrdinalIgnoreCase) || parts.Length > 1 && int.TryParse(parts[1], out var existing) && existing == number) throw new ArgumentException("Tileset filename or FileNumber collides with an existing mod definition.");
                }
                if (mod.Packs.Any(p => p.Split(' ')[0].Equals(tileset, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Texture pack filename collides with an existing mod.");
            }
            var prefix = tileset + "_";
            if (items.Values.Any(a => a.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !(ownRoot is not null && a.ModId == modId)) || gameItems.Keys.Any(id => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Tileset prefix collides with existing catalog tile IDs.");
        }
    }

    internal void ValidateAgentTileset(ModDescriptor mod, string tileset, int number, int frameCount)
    {
        lock (sync)
        {
            CheckAgentTilesetNames(mod.Id, tileset, number, mod.Root);
            var merged = MergeMods([], [], [mod]);
            if (merged.Diagnostics.Count != 0 || merged.Items.Count != (frameCount + 7) / 8 * 8) throw new ArgumentException("Staged tile definitions failed catalog validation.");
            for (var i = 0; i < frameCount; i++)
                if (!merged.Items.TryGetValue(tileset + "_" + i, out var tile) || tile.ModId != mod.Id || tile.Sprite is null || tile.Page is null) throw new ArgumentException("A staged sprite has no actual image or registered tile definition.");
        }
    }

    internal (byte[] Png, Asset Asset) ReadAgentTilesetFrame(ModDescriptor mod, string tileset, int frameIndex)
    {
        lock (sync)
        {
            // Use the real native readers before attach, without activating a temporary/global source.
            var loaded = MergeMods([], [], [mod]);
            if (loaded.Diagnostics.Count != 0 || !loaded.Items.TryGetValue(tileset + "_" + frameIndex, out var asset) || asset.Sprite is not { } sprite || asset.Page is null) throw new ArgumentException("No validated native sprite for this frame.");
            var atlas = loaded.Atlases[asset.Page];
            using var input = File.OpenRead(atlas.Path); input.Position = atlas.Page.Offset;
            var png = new byte[atlas.Page.Length]; input.ReadExactly(png); var decoded = Png.Decode(png);
            var pixels = new byte[sprite.FrameWidth * sprite.FrameHeight * 4];
            for (var y = 0; y < sprite.Height; y++)
                Array.Copy(decoded.Rgba, ((sprite.Y + y) * decoded.Width + sprite.X) * 4, pixels, ((sprite.OffsetY + y) * sprite.FrameWidth + sprite.OffsetX) * 4, sprite.Width * 4);
            return (Core.Export.Png.Encode(new(sprite.FrameWidth, sprite.FrameHeight, pixels)), asset);
        }
    }

    internal Action RegisterAgentTileset(ModDescriptor mod, string tileset, int number, int frameCount, bool restoring = false)
    {
        lock (sync)
        {
            // The catalog skips resource restoration when no game installation is available, but a
            // saved user deactivation must still apply to workspace packages in that situation.
            if (!modSelectionLoaded) LoadModSettings();
            ValidateAgentTileset(mod, tileset, number, frameCount);
            var previous = discoveredMods.GetValueOrDefault(mod.Id);
            var previousWorkspace = agentTilesetMods.GetValueOrDefault(mod.Id);
            var wasKnown = knownAgentTilesetIds.Contains(mod.Id); var wasRequested = requestedModIds.Contains(mod.Id);
            var previousActive = activeMods.SingleOrDefault(m => m.Id == mod.Id);
            var registered = previousActive ?? mod;
            var nextDiscovered = new Dictionary<string, ModDescriptor>(discoveredMods, StringComparer.Ordinal) { [mod.Id] = registered };
            var nextKnown = new HashSet<string>(knownAgentTilesetIds, StringComparer.Ordinal) { mod.Id };
            var nextRequested = requestedModIds.ToList();
            // Old attachment markers default to enabled. Once settings record the ID, absence from the
            // requested selection means explicitly disabled; discovering it must not reactivate it.
            if ((!restoring || !wasKnown) && !nextRequested.Contains(mod.Id)) nextRequested.Add(mod.Id);
            List<ModDiagnostic> pending = [];
            var nextMods = restoring ? RestorableMods(nextDiscovered, nextRequested, out pending)
                : ModRegistry.Order(nextDiscovered, activeMods.Select(m => m.Id).Append(mod.Id));
            var merged = MergeMods(gameItems, gameAtlases, nextMods);
            // Persist explicit attach before publishing the view, and undo its selection if session save fails.
            // Startup restore never rewrites settings while later workspace IDs are still pending validation.
            if (!restoring) SaveModSettings(nextRequested, nextKnown);
            discoveredMods = nextDiscovered; agentTilesetMods[mod.Id] = registered;
            knownAgentTilesetIds = nextKnown; requestedModIds = nextRequested.ToArray();
            activeMods = nextMods; items = merged.Items; atlases = merged.Atlases;
            modDiagnostics = discoveryDiagnostics.Concat(pending).Concat(merged.Diagnostics).ToList();
            return () =>
            {
                lock (sync)
                {
                    if (!agentTilesetMods.TryGetValue(mod.Id, out var owned) || !ReferenceEquals(owned, registered)) return;
                    var remaining = previousActive is null ? activeMods.Where(m => !ReferenceEquals(m, registered)).ToArray() : activeMods.ToArray();
                    var restored = MergeMods(gameItems, gameAtlases, remaining);
                    var restoredKnown = new HashSet<string>(knownAgentTilesetIds, StringComparer.Ordinal);
                    var restoredRequested = requestedModIds.ToList();
                    if (!wasKnown) restoredKnown.Remove(mod.Id);
                    if (wasRequested) { if (!restoredRequested.Contains(mod.Id)) restoredRequested.Add(mod.Id); }
                    else restoredRequested.Remove(mod.Id);
                    if (!restoring) SaveModSettings(restoredRequested, restoredKnown);
                    knownAgentTilesetIds = restoredKnown; requestedModIds = restoredRequested.ToArray();
                    activeMods = remaining; items = restored.Items; atlases = restored.Atlases;
                    if (discoveredMods.TryGetValue(mod.Id, out var known) && ReferenceEquals(known, registered))
                    { if (previous is null) discoveredMods.Remove(mod.Id); else discoveredMods[mod.Id] = previous; }
                    if (previousWorkspace is null) agentTilesetMods.Remove(mod.Id); else agentTilesetMods[mod.Id] = previousWorkspace;
                    modDiagnostics = discoveryDiagnostics.Concat(restored.Diagnostics).ToList();
                }
            };
        }
    }
}
