using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

public sealed record VisualTargetRequest(string Scope = "map", string? MemberId = null, string? BuildingId = null,
    string? SourceId = null, double? Longitude = null, double? Latitude = null);
public sealed record VisualReferenceBinding(string Scope, string? MemberId, string? BuildingId, string? SourceId,
    long BaseRevision, double? Longitude, double? Latitude, double? LocalX, double? LocalY,
    double? WorldX, double? WorldY, bool SharedBuilding = false);
public sealed record OwnPhotoProvenance(string Author, string License, DateTimeOffset? CapturedAt = null,
    double? Longitude = null, double? Latitude = null, double? Heading = null);
public sealed record VisualImageInfo(string MimeType, int Width, int Height);
public sealed record VisualReference(string Id, string Provider, string SourceId, string Author, string License,
    string? LicenseUrl, string? SourceUrl, DateTimeOffset? CapturedAt, double? Heading, double? Longitude,
    double? Latitude, DateTimeOffset ImportedAt, string Sha256, string MimeType, int Width, int Height,
    int ByteLength, VisualReferenceBinding Binding, string? ParentReferenceId = null);
public sealed record VisualReferenceImage(VisualReference Reference, byte[] Bytes);
public sealed record VisualReferenceView(VisualReference Reference, byte[] Bytes, string ReceiptId);
public sealed record VisualObservation(string? Value = null, double? Confidence = null,
    string Visibility = "unknown", string? Uncertainty = null);
public sealed class VisualObservations
{
    public VisualObservation Floors { get; set; } = new();
    public VisualObservation WallMaterial { get; set; } = new();
    public VisualObservation Roof { get; set; } = new();
    public VisualObservation Doors { get; set; } = new();
    public VisualObservation Fences { get; set; } = new();
    public VisualObservation Landscaping { get; set; } = new();
    public string[] HiddenFacades { get; set; } = [];
    public string? Uncertainty { get; set; }
    // Exterior references never establish an interior plan. This is deliberately not writable.
    public string Interiors => "unobserved";
}
public sealed record VisualObservationDocument(string Id, string ReferenceId, string ReceiptId, DateTimeOffset CreatedAt,
    VisualReference Source, VisualObservations Observations);
public sealed record VisualReferenceCapability(bool Available, string Message);
public sealed record VisualReferenceCapabilities(VisualReferenceCapability Own, VisualReferenceCapability Mapillary);
public sealed record MapillaryReferenceResult(string SourceId, string Author, DateTimeOffset? CapturedAt,
    double? Heading, double Longitude, double Latitude, string SourceUrl, string License, string LicenseUrl,
    double DistanceMetres);
public sealed record MapillarySearchResult(MapillaryReferenceResult[] Items, bool Complete, string Coverage);
public sealed class VisualReferenceCapabilityException(string message) : Exception(message);

/// <summary>Workspace-local immutable images/evidence. No project or geographical evidence is written here.</summary>
public sealed class VisualReferenceStore
{
    public const int MaximumImageBytes = 8 * 1024 * 1024;
    public const int MaximumDimension = 8192;
    public const int MaximumPixels = 24_000_000;
    const long SessionBudget = 128L * 1024 * 1024;
    const int MaximumReferences = 64;
    const string MapillaryLicense = "CC BY-SA 4.0";
    const string MapillaryLicenseUrl = "https://creativecommons.org/licenses/by-sa/4.0/";
    const string Fields = "id,geometry,computed_geometry,captured_at,compass_angle,computed_compass_angle,creator,thumb_1024_url";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    readonly string root;
    readonly object gate = new();
    readonly HttpClient http;
    readonly Func<string?> token;
    static readonly HttpClient ProviderHttp = CreateProviderHttp();

    // Register with an explicit factory: the sole production dependency is the trusted workspace directory.
    public VisualReferenceStore(string workspace) : this(workspace, ProviderHttp, () => Environment.GetEnvironmentVariable("PZMAP_MAPILLARY_TOKEN")) { }
    internal VisualReferenceStore(string workspace, HttpClient providerHttp, Func<string?> providerToken)
    {
        root = Path.GetFullPath(Path.Combine(workspace, "work", "visual-references"));
        http = providerHttp; token = providerToken;
    }

    public VisualReferenceCapabilities Capabilities() => new(new(true, "Own PNG/JPEG photos are available without an API token."),
        string.IsNullOrWhiteSpace(token()) ? new(false, "Mapillary requires the server environment variable PZMAP_MAPILLARY_TOKEN. Own photos remain available.")
        : new(true, "Mapillary token is configured; provider access and coverage are verified only by a successful request."));

    public static void RequireProvider(string provider)
    {
        if (provider is not ("own" or "mapillary")) throw new ArgumentException("Unsupported imagery provider. Use own photos or Mapillary. Google/Yandex are external navigation links only.");
    }

    static void Text(string? value, int maximum, bool required = false)
    {
        if (required && string.IsNullOrWhiteSpace(value) || value is not null && (value.Length > maximum || value.Any(char.IsControl)))
            throw new ArgumentException("Invalid visual reference text or metadata length.");
    }
    public static void Coordinates(double? longitude, double? latitude)
    {
        if (longitude.HasValue != latitude.HasValue || longitude is { } x && (!double.IsFinite(x) || x is < -180 or > 180) ||
            latitude is { } y && (!double.IsFinite(y) || y is < -90 or > 90)) throw new ArgumentException("Provide finite longitude/latitude together, in degrees.");
    }
    static void Heading(double? value)
    {
        if (value is { } v && (!double.IsFinite(v) || v is < 0 or >= 360)) throw new ArgumentException("Heading must be unknown or in [0,360) degrees.");
    }
    static void Date(DateTimeOffset? value)
    {
        if (value > DateTimeOffset.UtcNow.AddDays(1)) throw new ArgumentException("Capture date cannot be in the future.");
    }
    static void Key(string value, int length)
    {
        if (value is null || value.Length != length || !value.All(char.IsAsciiHexDigit)) throw new ArgumentException("Invalid visual reference key.");
    }
    string Folder(string session)
    {
        Key(session, 64);
        var folder = Path.Combine(root, session.ToLowerInvariant());
        // Refuse junctions/symlinks below the trusted workspace (including work and the store itself).
        var workspace = Directory.GetParent(root)!.Parent!.FullName;
        for (var path = folder; !string.Equals(path, workspace, StringComparison.OrdinalIgnoreCase); path = Path.GetDirectoryName(path)!)
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Reference storage cannot traverse symbolic links.");
        return folder;
    }
    string FileName(string session, string id, string extension)
    {
        Key(id, 32); var file = Path.Combine(Folder(session), id.ToLowerInvariant() + extension);
        if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("Reference files cannot be symbolic links.");
        return file;
    }
    T ReadJson<T>(string session, string id, string extension)
    {
        var file = FileName(session, id, extension);
        if (!File.Exists(file) || new FileInfo(file).Length > 128 * 1024) throw new ArgumentException("Visual reference record not found or too large.");
        try { return JsonSerializer.Deserialize<T>(File.ReadAllBytes(file), Json) ?? throw new ArgumentException("Invalid visual reference record."); }
        catch (JsonException) { throw new ArgumentException("Invalid visual reference record."); }
    }
    void WriteJson<T>(string session, string id, string extension, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > 128 * 1024) throw new ArgumentException("Visual reference metadata exceeds 128 KiB.");
        var folder = Folder(session); Directory.CreateDirectory(folder);
        if (Directory.EnumerateFiles(folder).Sum(f => new FileInfo(f).Length) + bytes.Length > SessionBudget)
            throw new ArgumentException("Visual reference session exceeds 128 MiB.");
        var file = FileName(session, id, extension); var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes); File.Move(temporary, file, false); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public VisualReference Read(string session, string id)
    {
        lock (gate)
        {
            var r = ReadJson<VisualReference>(session, id, ".reference.json");
            if (!string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Reference identity mismatch.");
            RequireProvider(r.Provider); Key(r.Sha256, 64); ValidateBinding(r.Binding);
            return r;
        }
    }
    public VisualReference[] List(string session)
    {
        lock (gate)
        {
            var folder = Folder(session); if (!Directory.Exists(folder)) return [];
            return Directory.EnumerateFiles(folder, "*.reference.json").Take(MaximumReferences + 1)
                .Select(f => Read(session, Path.GetFileName(f)[..32])).OrderByDescending(r => r.ImportedAt).ToArray();
        }
    }
    public VisualReferenceImage ReadImage(string session, string id)
    {
        lock (gate)
        {
            var r = Read(session, id); var file = FileName(session, id, r.MimeType == "image/png" ? ".png" : ".jpg");
            if (!File.Exists(file) || new FileInfo(file).Length != r.ByteLength || r.ByteLength is < 1 or > MaximumImageBytes)
                throw new ArgumentException("Reference image is missing or changed.");
            var bytes = File.ReadAllBytes(file);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), r.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Reference image integrity check failed.");
            return new(r, bytes);
        }
    }
    public VisualReference UploadOwn(string session, VisualReferenceBinding binding, byte[] bytes, string mimeType, OwnPhotoProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        Text(provenance.Author, 200, true); Text(provenance.License, 200, true);
        Coordinates(provenance.Longitude, provenance.Latitude); Heading(provenance.Heading); Date(provenance.CapturedAt);
        var info = ValidateImage(bytes, mimeType);
        var id = Guid.NewGuid().ToString("N");
        return Save(session, new(id, "own", id, provenance.Author.Trim(), provenance.License.Trim(), null, null,
            provenance.CapturedAt, provenance.Heading, provenance.Longitude, provenance.Latitude, DateTimeOffset.UtcNow,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), info.MimeType, info.Width, info.Height, bytes.Length, binding), bytes);
    }
    VisualReference Save(string session, VisualReference r, byte[] bytes)
    {
        lock (gate)
        {
            ValidateBinding(r.Binding); var folder = Folder(session); Directory.CreateDirectory(folder);
            var existing = List(session);
            if (existing.Length >= MaximumReferences || existing.Count(e => SameTarget(e.Binding, r.Binding)) >= 16)
                throw new ArgumentException("Keep at most 64 references per session and 16 per target.");
            if (Directory.EnumerateFiles(folder).Sum(f => new FileInfo(f).Length) + bytes.Length + 128 * 1024 > SessionBudget)
                throw new ArgumentException("Visual reference session exceeds 128 MiB.");
            var image = FileName(session, r.Id, r.MimeType == "image/png" ? ".png" : ".jpg");
            using (var output = new FileStream(image, FileMode.CreateNew, FileAccess.Write, FileShare.None)) output.Write(bytes);
            try { WriteJson(session, r.Id, ".reference.json", r); }
            catch { File.Delete(image); throw; }
            return r;
        }
    }
    public VisualReference Rebind(string session, string id, VisualReferenceBinding binding)
    {
        var image = ReadImage(session, id);
        return Save(session, image.Reference with { Id = Guid.NewGuid().ToString("N"), Binding = binding, ParentReferenceId = id }, image.Bytes);
    }
    public static bool SameTarget(VisualReferenceBinding a, VisualReferenceBinding b) => a.Scope == b.Scope && a.MemberId == b.MemberId &&
        a.BuildingId == b.BuildingId && a.SourceId == b.SourceId && a.SharedBuilding == b.SharedBuilding &&
        (a.BuildingId is not null || a.Longitude == b.Longitude && a.Latitude == b.Latitude);
    static void ValidateBinding(VisualReferenceBinding b)
    {
        if (b is null || b.BaseRevision < 1 || b.Scope is not ("map" or "world") || b.Scope == "map" && b.MemberId is not null ||
            b.Scope == "world" && b.MemberId is null && !b.SharedBuilding)
            throw new ArgumentException("Invalid reference target scope/revision.");
        Text(b.BuildingId, 100); Text(b.SourceId, 100); if (b.MemberId is not null) Key(b.MemberId, 32);
        Coordinates(b.Longitude, b.Latitude);
        if (b.BuildingId is null && b.Longitude is null) throw new ArgumentException("Select a building or a geographic location.");
        if (new[] { b.LocalX, b.LocalY, b.WorldX, b.WorldY }.Any(v => v.HasValue && !double.IsFinite(v.Value))) throw new ArgumentException("Invalid target tile coordinate.");
    }

    sealed record ImageReceipt(string Id, string ReferenceId, string Sha256);
    public VisualReferenceView View(string session, string id)
    {
        lock (gate)
        {
            var image = ReadImage(session, id); var receipt = new ImageReceipt(Guid.NewGuid().ToString("N"), id, image.Reference.Sha256);
            if (Directory.EnumerateFiles(Folder(session), "*.view.json").Count() >= 256) throw new ArgumentException("This session already has 256 image view receipts.");
            WriteJson(session, receipt.Id, ".view.json", receipt);
            return new(image.Reference, image.Bytes, receipt.Id);
        }
    }
    public VisualObservationDocument Observe(string session, string referenceId, string receiptId, VisualObservations observations)
    {
        lock (gate)
        {
            var source = ReadImage(session, referenceId).Reference;
            var receipt = ReadJson<ImageReceipt>(session, receiptId, ".view.json");
            if (receipt.ReferenceId != referenceId || receipt.Sha256 != source.Sha256) throw new ArgumentException("View this exact image through visual_reference_view before recording observations.");
            ValidateObservations(observations);
            if (Directory.EnumerateFiles(Folder(session), "*.observations.json").Count() >= 256) throw new ArgumentException("This session already has 256 observation records.");
            var doc = new VisualObservationDocument(Guid.NewGuid().ToString("N"), referenceId, receiptId, DateTimeOffset.UtcNow, source, observations);
            WriteJson(session, doc.Id, ".observations.json", doc); return doc;
        }
    }
    public VisualObservationDocument ReadObservations(string session, string id)
    {
        lock (gate) return ReadJson<VisualObservationDocument>(session, id, ".observations.json");
    }
    public VisualObservationDocument[] ListObservations(string session, string referenceId)
    {
        lock (gate)
        {
            Read(session, referenceId);
            return Directory.EnumerateFiles(Folder(session), "*.observations.json").Take(256)
                .Select(f => ReadObservations(session, Path.GetFileName(f)[..32])).Where(o => o.ReferenceId == referenceId).ToArray();
        }
    }
    static void ValidateObservations(VisualObservations o)
    {
        if (o is null) throw new ArgumentException("Provide exterior observations.");
        foreach (var v in new[] { o.Floors, o.WallMaterial, o.Roof, o.Doors, o.Fences, o.Landscaping })
        {
            if (v is null || v.Visibility is not ("visible" or "partial" or "hidden" or "unknown") ||
                v.Confidence is { } confidence && (!double.IsFinite(confidence) || confidence is < 0 or > 1) ||
                v.Visibility is "hidden" or "unknown" && (v.Value is not null || v.Confidence is not null) ||
                v.Value is null && v.Confidence is not null)
                throw new ArgumentException("Observations need confidence in [0,1] or unknown. Hidden/unknown features cannot have observed values or confidence.");
            Text(v.Value, 1000); Text(v.Uncertainty, 1000);
        }
        if (o.HiddenFacades is null || o.HiddenFacades.Length > 8) throw new ArgumentException("At most eight hidden facades can be recorded.");
        foreach (var facade in o.HiddenFacades) Text(facade, 100, true);
        Text(o.Uncertainty, 2000);
    }

    static void Selection(VisualTargetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request); Coordinates(request.Longitude, request.Latitude);
        Text(request.BuildingId, 100); Text(request.SourceId, 100);
        if (request.BuildingId is null && request.SourceId is null && request.Longitude is null)
            throw new ArgumentException("Select a building ID, geographic source ID, or longitude/latitude.");
    }
    // Single-map imports are centred with an eight-tile margin. Do not use the world NW-origin projection here.
    static (double X, double Y) MapPoint(MapProject p, double longitude, double latitude)
    {
        if (p.WorldSection) throw new ArgumentException("Use the world session and its full geographic reference for section coordinates.");
        if (p.Bounds is not { } bounds) throw new ArgumentException("This map has no geographic bounds. Select a building by ID or source ID.");
        ProjectValidator.ValidateBounds(bounds);
        var cos = Math.Cos((bounds.North + bounds.South) / 2 * Math.PI / 180);
        var scale = Math.Min((p.Width - 16) / ((bounds.East - bounds.West) * cos), (p.Height - 16) / (bounds.North - bounds.South));
        return ((p.Width - (bounds.East - bounds.West) * cos * scale) / 2 + (longitude - bounds.West) * cos * scale,
            (p.Height - (bounds.North - bounds.South) * scale) / 2 + (bounds.North - latitude) * scale);
    }
    static (double Longitude, double Latitude)? MapGeo(MapProject p, double x, double y)
    {
        if (p.WorldSection) return null;
        if (p.Bounds is not { } b) return null;
        var a = MapPoint(p, b.West, b.North); var z = MapPoint(p, b.East, b.South);
        return (b.West + (x - a.X) / (z.X - a.X) * (b.East - b.West), b.North - (y - a.Y) / (z.Y - a.Y) * (b.North - b.South));
    }
    static bool Contains(Building b, double x, double y) => x >= b.X && y >= b.Y && x < b.X + b.Width && y < b.Y + b.Height &&
        (b.Design is null || BuildingDesigner.Occupies(b, (int)Math.Floor(x - b.X), (int)Math.Floor(y - b.Y)));
    static Building? Choose(IEnumerable<Building> buildings, VisualTargetRequest r, double? x, double? y)
    {
        var candidates = buildings.Where(b => (r.BuildingId is null || b.Id == r.BuildingId) && (r.SourceId is null || b.SourceId == r.SourceId) &&
            (x is null || Contains(b, x.Value, y!.Value))).Take(2).ToArray();
        if (candidates.Length > 1) throw new ArgumentException("More than one building matches; select its exact building ID and world member.");
        if (candidates.Length == 0 && (r.BuildingId is not null || r.SourceId is not null)) throw new ArgumentException("Selected building/source ID was not found at this location.");
        return candidates.SingleOrDefault();
    }
    public static VisualReferenceBinding ResolveMap(MapProject p, long revision, VisualTargetRequest request)
    {
        Selection(request);
        if (request.Scope != "map" || request.MemberId is not null) throw new ArgumentException("Use map scope without a world member.");
        var point = request.Longitude is { } longitude ? MapPoint(p, longitude, request.Latitude!.Value) : ((double X, double Y)?)null;
        if (point is { } q && (q.X < 0 || q.Y < 0 || q.X >= p.Width || q.Y >= p.Height)) throw new ArgumentException("Location lies outside this map.");
        var b = Choose(p.Buildings, request, point?.X, point?.Y);
        var x = b is null ? point!.Value.X : b.X + b.Width / 2d; var y = b is null ? point!.Value.Y : b.Y + b.Height / 2d;
        var geo = b is null ? (request.Longitude!.Value, request.Latitude!.Value) : MapGeo(p, x, y);
        var binding = new VisualReferenceBinding("map", null, b?.Id, b?.SourceId, revision, geo?.Item1, geo?.Item2, x, y, p.WorldX * 256 + x, p.WorldY * 256 + y);
        ValidateBinding(binding); return binding;
    }
    public static VisualReferenceBinding ResolveWorld(WorldProject world, long revision, VisualTargetRequest request)
    {
        Selection(request); if (request.Scope != "world") throw new ArgumentException("Use world scope for a world reference.");
        if (request.MemberId is not null && world.Maps.All(m => m.Id != request.MemberId)) throw new ArgumentException("World member not found.");
        GeographicProjection? projection = world.GeoReference is { } g ? new(g, g.OriginCellX, g.OriginCellY) : null;
        (double X, double Y)? absolute = null;
        if (request.Longitude is { } lon)
        {
            if (projection is null) throw new ArgumentException("This world has no geographic reference.");
            var p = projection.Project((lon, request.Latitude!.Value));
            absolute = (p.X + world.GeoReference!.OriginCellX * 256, p.Y + world.GeoReference.OriginCellY * 256);
        }
        var candidates = world.Maps.Where(m => request.MemberId is null || m.Id == request.MemberId)
            .SelectMany(m => m.Project.Buildings.Select(b => (Building: b, Member: (WorldMapMember?)m, X: m.Project.WorldX * 256, Y: m.Project.WorldY * 256)))
            .Concat(world.Buildings.Select(b => (Building: b, Member: (WorldMapMember?)null, X: 0, Y: 0)))
            .Where(v => (request.BuildingId is null || v.Building.Id == request.BuildingId) && (request.SourceId is null || v.Building.SourceId == request.SourceId) &&
                (absolute is null || Contains(v.Building, absolute.Value.X - v.X, absolute.Value.Y - v.Y))).Take(2).ToArray();
        if (candidates.Length > 1) throw new ArgumentException("Ambiguous world building; select an exact building ID/member.");
        if (candidates.Length == 0 && (request.BuildingId is not null || request.SourceId is not null)) throw new ArgumentException("World building/source ID not found at this location.");
        var selected = candidates.FirstOrDefault(); var building = selected.Building;
        var x = building is not null ? building.X + selected.X + building.Width / 2d : absolute!.Value.X;
        var y = building is not null ? building.Y + selected.Y + building.Height / 2d : absolute!.Value.Y;
        var member = selected.Member ?? WorldAssembler.MemberAt(world, (int)Math.Floor(x), (int)Math.Floor(y));
        if (member is null || request.MemberId is not null && selected.Member is null && request.MemberId != member.Id &&
            (building is null || !Intersects(building, world.Maps.Single(m => m.Id == request.MemberId).Project))) throw new ArgumentException("Location is outside the selected world sections.");
        (double Longitude,double Latitude)? geo = projection is not null&&projection.TryGeographic(x-world.GeoReference!.OriginCellX*256,y-world.GeoReference.OriginCellY*256,out var coordinate)?coordinate:null;
        var binding = new VisualReferenceBinding("world", member.Id, building?.Id, building?.SourceId, revision, geo?.Longitude, geo?.Latitude,
            x - member.Project.WorldX * 256, y - member.Project.WorldY * 256, x, y, building is not null && selected.Member is null);
        ValidateBinding(binding); return binding;
    }
    static bool Intersects(Building b, MapProject p) => b.X < p.WorldX * 256 + p.Width && b.Y < p.WorldY * 256 + p.Height && b.X + b.Width > p.WorldX * 256 && b.Y + b.Height > p.WorldY * 256;

    public static double DistanceMetres(double longitude, double latitude, double otherLongitude, double otherLatitude)
    {
        var a = Math.PI / 180; var dlat = (otherLatitude - latitude) * a; var dlon = (otherLongitude - longitude) * a;
        var h = Math.Pow(Math.Sin(dlat / 2), 2) + Math.Cos(latitude * a) * Math.Cos(otherLatitude * a) * Math.Pow(Math.Sin(dlon / 2), 2);
        return 6371008.8 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(h, 0, 1)));
    }
    static string SourceLink(string id) => "https://www.mapillary.com/app/?pKey=" + id;
    static void SourceId(string id)
    {
        if (id is not { Length: > 0 and <= 32 } || !id.All(char.IsAsciiDigit)) throw new ArgumentException("Select a numeric Mapillary source ID; arbitrary URLs are not accepted.");
    }
    string ProviderToken()
    {
        var value = token();
        if (string.IsNullOrWhiteSpace(value)) throw new VisualReferenceCapabilityException(Capabilities().Mapillary.Message);
        if (value.Length > 4096 || value.Any(c => c <= 32 || c > 126)) throw new VisualReferenceCapabilityException("The server Mapillary token configuration is invalid.");
        return value;
    }
    public async Task<MapillarySearchResult> SearchMapillary(double longitude, double latitude, int radiusMetres = 150, CancellationToken cancellation = default)
    {
        Coordinates(longitude, latitude);
        if (radiusMetres is < 10 or > 1000) throw new ArgumentException("Search radius must be 10–1000 metres.");
        var secret = ProviderToken(); var latDelta = radiusMetres / 111000d;
        var lonDelta = Math.Min(180, latDelta / Math.Max(.001, Math.Cos(latitude * Math.PI / 180)));
        var west = longitude - lonDelta; var east = longitude + lonDelta;
        var ranges = west < -180 ? new[] { (west + 360, 180d), (-180d, east) } : east > 180 ? new[] { (west, 180d), (-180d, east - 360) } : new[] { (west, east) };
        var results = new List<MapillaryReferenceResult>(); var complete = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        foreach (var range in ranges)
        {
            var bbox = string.Join(',', new[] { range.Item1, Math.Max(-90, latitude - latDelta), range.Item2, Math.Min(90, latitude + latDelta) }.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
            using var doc = await Graph("images?fields=" + Fields + "&limit=50&bbox=" + Uri.EscapeDataString(bbox), secret, timeout.Token);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) throw ProviderFailure();
            if (data.GetArrayLength() > 50) throw ProviderFailure();
            if (doc.RootElement.TryGetProperty("paging", out var paging) && paging.TryGetProperty("next", out _)) complete = false;
            foreach (var item in data.EnumerateArray())
            {
                var metadata = Metadata(item, secret, longitude, latitude);
                if (metadata.DistanceMetres <= radiusMetres) results.Add(metadata);
            }
        }
        return new(results.DistinctBy(r => r.SourceId).OrderBy(r => r.DistanceMetres).ToArray(), complete,
            complete ? "Nearest images within the requested radius in the returned provider dataset; direction/occlusion may hide the building."
                : "Provider returned a limited page (50 per bounding box). Nearest among returned images only; narrow the radius for denser coverage.");
    }
    public async Task<VisualReference> ImportMapillary(string session, VisualReferenceBinding binding, string sourceId, CancellationToken cancellation = default)
    {
        SourceId(sourceId); ValidateBinding(binding); Folder(session); var secret = ProviderToken();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var doc = await Graph(sourceId + "?fields=" + Fields, secret, timeout.Token);
        var item = doc.RootElement;
        var metadata = Metadata(item, secret, binding.Longitude ?? 0, binding.Latitude ?? 0);
        if (metadata.SourceId != sourceId) throw ProviderFailure();
        if (!item.TryGetProperty("thumb_1024_url", out var thumb) || thumb.ValueKind != JsonValueKind.String) throw ProviderFailure();
        var raw = thumb.GetString()!;
        // Only an unmodified URL from this authenticated Graph response can be downloaded. Never expose it.
        if (raw.Contains(secret, StringComparison.Ordinal)) throw ProviderFailure();
        var url = ValidateDownloadUri(raw);
        using var request = new HttpRequestMessage(HttpMethod.Get, url); // Authorization is intentionally Graph-only.
        using var response = await Send(request, timeout.Token);
        var mime = response.Content.Headers.ContentType?.MediaType ?? "";
        var bytes = await Limited(response.Content, MaximumImageBytes, timeout.Token);
        var info = ValidateImage(bytes, mime); var id = Guid.NewGuid().ToString("N");
        return Save(session, new(id, "mapillary", sourceId, metadata.Author, MapillaryLicense, MapillaryLicenseUrl, SourceLink(sourceId),
            metadata.CapturedAt, metadata.Heading, metadata.Longitude, metadata.Latitude, DateTimeOffset.UtcNow,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), info.MimeType, info.Width, info.Height, bytes.Length, binding), bytes);
    }
    static VisualReferenceCapabilityException ProviderFailure() => new("Mapillary request failed, was redirected, or returned unsupported data. Verify server token, provider access and image availability. Upstream URLs and response bodies are withheld.");
    async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellation)
    {
        try
        {
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if (!response.IsSuccessStatusCode) { response.Dispose(); throw ProviderFailure(); }
            return response;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException)
        {
            if (cancellation.IsCancellationRequested) throw new VisualReferenceCapabilityException("Mapillary request was cancelled or exceeded its time budget.");
            throw ProviderFailure(); // Never propagate request URLs, headers, bodies or exception inner messages.
        }
    }
    async Task<JsonDocument> Graph(string path, string secret, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.mapillary.com/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", secret);
        using var response = await Send(request, cancellation);
        var bytes = await Limited(response.Content, 1024 * 1024, cancellation);
        try
        {
            var doc = JsonDocument.Parse(bytes, new() { MaxDepth = 24 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object || doc.RootElement.TryGetProperty("error", out _)) { doc.Dispose(); throw ProviderFailure(); }
            return doc;
        }
        catch (JsonException) { throw ProviderFailure(); }
    }
    public static async Task<byte[]> Limited(HttpContent content, int maximum, CancellationToken cancellation)
    {
        if (content.Headers.ContentLength > maximum) throw new ArgumentException("Reference response exceeds its byte budget.");
        try
        {
            using var output = new MemoryStream(); await using var input = await content.ReadAsStreamAsync(cancellation);
            var buffer = new byte[16384]; int count;
            while ((count = await input.ReadAsync(buffer, cancellation)) > 0)
            {
                if (output.Length + count > maximum) throw new ArgumentException("Reference response exceeds its byte budget.");
                output.Write(buffer, 0, count);
            }
            return output.ToArray();
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException) { throw ProviderFailure(); }
    }
    static MapillaryReferenceResult Metadata(JsonElement item, string secret, double longitude, double latitude)
    {
        try
        {
            var id = item.GetProperty("id").GetString()!; SourceId(id);
            var point = item.TryGetProperty("computed_geometry", out var computed) && computed.ValueKind == JsonValueKind.Object ? computed : item.GetProperty("geometry");
            if (point.GetProperty("type").GetString() != "Point") throw ProviderFailure();
            var coordinates = point.GetProperty("coordinates"); if (coordinates.GetArrayLength() != 2) throw ProviderFailure();
            var lon = coordinates[0].GetDouble(); var lat = coordinates[1].GetDouble(); Coordinates(lon, lat);
            var author = item.TryGetProperty("creator", out var creator) && creator.ValueKind == JsonValueKind.Object && creator.TryGetProperty("username", out var user) ? user.GetString() : null;
            // Credit must come from the provider. Do not silently label missing authors as known.
            if (string.IsNullOrWhiteSpace(author) || author.Contains(secret, StringComparison.Ordinal) || author.Contains("://", StringComparison.Ordinal)) throw ProviderFailure();
            Text(author, 200, true);
            DateTimeOffset? captured = item.TryGetProperty("captured_at", out var date) && date.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeMilliseconds(date.GetInt64()) : null;
            Date(captured);
            var angle = item.TryGetProperty("computed_compass_angle", out var heading) && heading.ValueKind == JsonValueKind.Number ? heading.GetDouble()
                : item.TryGetProperty("compass_angle", out heading) && heading.ValueKind == JsonValueKind.Number ? heading.GetDouble() : (double?)null;
            Heading(angle);
            return new(id, author, captured, angle, lon, lat, SourceLink(id), MapillaryLicense, MapillaryLicenseUrl, DistanceMetres(longitude, latitude, lon, lat));
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException) { throw ProviderFailure(); }
    }

    /// <summary>Policy: HTTPS:443, scontent.xx.fbcdn.net or a single scontent-REGION.xx.fbcdn.net label.
    /// Regional names come from Graph, never callers. No other fbcdn subdomain, credentials or redirects.</summary>
    public static Uri ValidateDownloadUri(string raw)
    {
        if (raw is null || raw.Length > 8192 || !Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || !AllowedCdnHost(uri.IdnHost) ||
            uri.Query.Split('&', '?').Any(q => q.Split('=')[0].Equals("access_token", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Mapillary image URL is outside the allowed HTTPS Meta image CDN policy.");
        return uri;
    }
    static bool AllowedCdnHost(string host)
    {
        if (host == "scontent.xx.fbcdn.net") return true;
        const string suffix = ".xx.fbcdn.net";
        if (!host.EndsWith(suffix, StringComparison.Ordinal)) return false;
        var label = host[..^suffix.Length];
        return label.StartsWith("scontent-", StringComparison.Ordinal) && label.Length is > 9 and <= 63 && label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    }
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return b[0] is not (0 or 10 or 127) && b[0] < 224 && !(b[0] == 100 && b[1] is >= 64 and <= 127) &&
                !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31) && !(b[0] == 192 && b[1] == 168) &&
                !(b[0] == 192 && b[1] == 0) && !(b[0] == 198 && b[1] is 18 or 19) &&
                !(b[0] == 198 && b[1] == 51 && b[2] == 100) && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        // Only native global unicast; reject ULA, multicast, link-local, NAT64, mapped and transition routes.
        return b.Length == 16 && (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && b[1] == 0x01 && (b[2] < 2 || b[2] == 0x0d && b[3] == 0xb8)) &&
            !(b[0] == 0x20 && b[1] == 0x02);
    }
    static HttpClient CreateProviderHttp()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false, UseProxy = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None,
            MaxResponseHeadersLength = 32, ConnectTimeout = TimeSpan.FromSeconds(10), PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = async (context, cancellation) =>
            {
                var host = context.DnsEndPoint.Host;
                if (context.DnsEndPoint.Port != 443 || host != "graph.mapillary.com" && !AllowedCdnHost(host)) throw new IOException("Provider host rejected.");
                var addresses = await Dns.GetHostAddressesAsync(host, cancellation);
                if (addresses.Length == 0 || addresses.Any(a => !IsPublicAddress(a))) throw new IOException("Provider DNS address rejected.");
                foreach (var address in addresses)
                {
                    var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try { await socket.ConnectAsync(new IPEndPoint(address, 443), cancellation); return new NetworkStream(socket, ownsSocket: true); }
                    catch (SocketException) { socket.Dispose(); }
                    catch { socket.Dispose(); throw; }
                }
                throw new IOException("Provider connection failed.");
            }
        };
        return new(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public static VisualImageInfo ValidateImage(byte[] bytes, string mimeType)
    {
        if (bytes is null || bytes.Length is < 24 or > MaximumImageBytes) throw new ArgumentException("Upload a PNG/JPEG image of at most 8 MiB.");
        if (mimeType == "image/png" && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return Png(bytes);
        if (mimeType == "image/jpeg" && bytes[0] == 255 && bytes[1] == 216) return Jpeg(bytes);
        throw new ArgumentException("Only actual PNG or JPEG bytes matching their declared MIME type are supported.");
    }
    static void Dimensions(int width, int height)
    {
        if (width is < 1 or > MaximumDimension || height is < 1 or > MaximumDimension || (long)width * height > MaximumPixels)
            throw new ArgumentException("Image dimensions must be 1–8192 per side and at most 24 million pixels.");
    }
    static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (var b in bytes) { crc ^= b; for (var n = 0; n < 8; n++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1; }
        return crc ^ uint.MaxValue;
    }
    static VisualImageInfo Png(byte[] bytes)
    {
        int offset = 8, width = 0, height = 0, depth = 0, channels = 0, interlace = 0; bool header = false, dataSeen = false, dataEnded = false, palette = false, finished = false;
        using var compressed = new MemoryStream();
        while (offset + 12 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length < 0 || offset + 12L + length > bytes.Length) throw new ArgumentException("Truncated PNG chunk.");
            var type = bytes.AsSpan(offset + 4, 4); var data = bytes.AsSpan(offset + 8, length);
            if (Crc(bytes.AsSpan(offset + 4, length + 4)) != BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + length, 4))) throw new ArgumentException("PNG checksum mismatch.");
            if (!header && !type.SequenceEqual("IHDR"u8)) throw new ArgumentException("PNG must begin with IHDR.");
            if (type.SequenceEqual("IHDR"u8))
            {
                if (header || length != 13) throw new ArgumentException("Invalid PNG header.");
                width = BinaryPrimitives.ReadInt32BigEndian(data); height = BinaryPrimitives.ReadInt32BigEndian(data[4..]); Dimensions(width, height);
                depth = data[8]; var colour = data[9]; channels = colour switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 }; interlace = data[12];
                if (channels == 0 || !(colour is 0 or 3 ? depth is 1 or 2 or 4 or 8 || colour == 0 && depth == 16 : depth is 8 or 16) ||
                    data[10] != 0 || data[11] != 0 || interlace > 1) throw new ArgumentException("Unsupported PNG encoding.");
                header = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (palette || dataSeen || length is < 3 or > 768 || length % 3 != 0) throw new ArgumentException("Invalid PNG palette.");
                palette = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (dataEnded || bytes[25] == 3 && !palette) throw new ArgumentException("Invalid PNG image data order.");
                dataSeen = true; compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (length != 0 || !dataSeen) throw new ArgumentException("Invalid PNG end.");
                offset += 12; finished = true; break;
            }
            else
            {
                if (type.SequenceEqual("acTL"u8) || (type[0] & 32) == 0) throw new ArgumentException("Animated PNG and unknown critical chunks are unsupported.");
                if (dataSeen) dataEnded = true;
            }
            offset += length + 12;
        }
        if (!finished || offset != bytes.Length) throw new ArgumentException("PNG is truncated or has trailing bytes.");
        compressed.Position = 0;
        try
        {
            using var z = new ZLibStream(compressed, CompressionMode.Decompress);
            var passes = interlace == 0 ? new[] { (0, 0, 1, 1) } : new[] { (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2) };
            foreach (var (x, y, dx, dy) in passes)
            {
                var w = Math.Max(0, (width - x + dx - 1) / dx); var h = Math.Max(0, (height - y + dy - 1) / dy); if (w == 0 || h == 0) continue;
                var row = new byte[(w * depth * channels + 7) / 8];
                for (var n = 0; n < h; n++) { var filter = z.ReadByte(); if (filter is < 0 or > 4) throw new ArgumentException("Invalid PNG scanline filter."); z.ReadExactly(row); }
            }
            if (z.ReadByte() != -1) throw new ArgumentException("PNG decompressed data exceeds dimensions.");
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException) { throw new ArgumentException("PNG pixel stream is invalid or truncated."); }
        return new("image/png", width, height);
    }
    // Bounded JPEG marker/frame/scan validation, not a lossy decode/re-encode. Original metadata and bytes are retained.
    static VisualImageInfo Jpeg(byte[] bytes)
    {
        int offset = 2, width = 0, height = 0, scans = 0; bool frame = false, quantization = false, huffman = false;
        while (offset < bytes.Length)
        {
            if (bytes[offset++] != 255) throw new ArgumentException("Invalid JPEG marker.");
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            if (offset >= bytes.Length) break;
            var marker = bytes[offset++];
            if (marker == 217)
            {
                if (!frame || scans == 0 || offset != bytes.Length) throw new ArgumentException("Invalid JPEG end or trailing bytes.");
                return new("image/jpeg", width, height);
            }
            if (marker is 0 or 216 or >= 208 and <= 215 || offset + 2 > bytes.Length) throw new ArgumentException("Invalid JPEG structure.");
            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
            if (length < 2 || offset + length > bytes.Length) throw new ArgumentException("Truncated JPEG segment.");
            var segment = bytes.AsSpan(offset + 2, length - 2); offset += length;
            if (marker is >= 192 and <= 207 && marker is not (196 or 200 or 204))
            {
                if (frame || marker is not (192 or 193 or 194) || segment.Length < 6 || segment[0] != 8 || segment[5] is not (1 or 3 or 4) || segment.Length != 6 + segment[5] * 3)
                    throw new ArgumentException("Use an 8-bit baseline or progressive JPEG.");
                height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]); width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]); Dimensions(width, height); frame = true;
            }
            else if (marker == 219) quantization = segment.Length > 0;
            else if (marker == 196) huffman = segment.Length > 0;
            else if (marker == 218)
            {
                if (!frame || !quantization || !huffman || segment.Length < 6 || segment[0] is < 1 or > 4 || segment.Length != 4 + 2 * segment[0]) throw new ArgumentException("Invalid JPEG scan header.");
                var start = offset;
                while (offset < bytes.Length)
                {
                    if (bytes[offset] != 255) { offset++; continue; }
                    var next = offset + 1; while (next < bytes.Length && bytes[next] == 255) next++;
                    if (next >= bytes.Length) throw new ArgumentException("Truncated JPEG scan.");
                    if (bytes[next] == 0 || bytes[next] is >= 208 and <= 215) { offset = next + 1; continue; }
                    break;
                }
                if (offset == start) throw new ArgumentException("Empty JPEG scan.");
                scans++;
            }
        }
        throw new ArgumentException("JPEG is truncated.");
    }
}
