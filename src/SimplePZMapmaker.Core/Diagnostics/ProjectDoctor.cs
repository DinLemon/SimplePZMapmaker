using SimplePZMapmaker.Core.Export;

namespace SimplePZMapmaker.Core.Diagnostics;

/// <summary>Tile coordinates are local to MemberId; cell coordinates are absolute native cells.</summary>
public sealed record DoctorLocation(string? MemberId = null, string? BuildingId = null,
    int? X = null, int? Y = null, int? Z = null, int? CellX = null, int? CellY = null,
    string? Source = null, string? Tile = null);
public sealed record DoctorFinding(string Severity, string Category, string Code, string Message, DoctorLocation? Location = null);
public sealed record DoctorCheck(string Category, string Status, string Message);
public sealed record ProjectDoctorReport(bool Complete, string Status, IReadOnlyList<DoctorCheck> Checks, IReadOnlyList<DoctorFinding> Findings);

public sealed class ProjectDoctorRequest
{
    public MapProject? Project { get; set; }
    public WorldProject? World { get; set; }
    public string? MemberId { get; set; }
    public WalkabilityOptions Access { get; set; } = new();
}

/// <summary>Adapters use installed catalogs in the host; null explicitly means unavailable.</summary>
public sealed class ProjectDoctorContext
{
    public Action<MapProject>? ValidateMapResources { get; init; }
    public Action<WorldProject>? ValidateWorldResources { get; init; }
    public Func<string, IReadOnlyDictionary<string, string>>? TileProperties { get; init; }
    public IReadOnlyList<CellConflicts.InstalledMap>? InstalledMaps { get; init; }
    public string? InstalledMapsCoverageNote { get; init; }
}

/// <summary>Aggregates existing checks. Every potentially normalizing validator receives a fresh clone.</summary>
public static class ProjectDoctor
{
    public static ProjectDoctorReport Analyze(ProjectDoctorRequest request, ProjectDoctorContext? context = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        if ((request.Project is null) == (request.World is null)) throw new ArgumentException("Choose exactly one map or world snapshot.");
        if (request.World is {} input && (input.Maps is null || input.Maps.Count is < 1 or > WorldLayoutLimits.MaxSections || input.Maps.Any(m => m is null || m.Project is null)))
            throw new ArgumentException("A doctor world request needs 1-8,192 non-null map sections.");
        if (request.Project is not null && request.MemberId is not null) throw new ArgumentException("MemberId belongs to a world snapshot.");
        if (request.World is {} supplied && request.MemberId is not null && !supplied.Maps.Any(m => m?.Id == request.MemberId))
            throw new ArgumentException("Select an existing world section for access analysis.");
        context ??= new();
        var checks = new List<DoctorCheck>(); var findings = new List<DoctorFinding>();
        MapProject CopyMap(MapProject p) => NeighborhoodPlanner.Copy(p);
        void CheckZones(MapProject p)
        {
            if (p.Terrain is null || p.Terrain.Length != (long)p.Width * p.Height || p.Buildings is null)
                throw new ArgumentException("Zone validation requires a complete terrain layer and building list.");
            GameZones.ValidateProject(p); ZombieDensityRegions.ValidateProject(p); RoomRegionRules.ValidateProject(p);
        }
        void Skip(string category, string status, string message)
        {
            checks.Add(new(category, status, message));
            findings.Add(new("info", category, status, message));
        }
        void Run(string category, Action action, DoctorLocation? location = null)
        {
            token.ThrowIfCancellationRequested();
            try { action(); checks.Add(new(category, "pass", "Existing check completed for the stated scope.")); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                checks.Add(new(category, "fail", "Existing check rejected this input; it may stop at its first failure."));
                findings.Add(new("error", category, "validation", e.Message, location));
            }
        }

        var map = request.Project; var world = request.World;
        Run("structure", () => { if (map is not null) ProjectValidator.Validate(CopyMap(map)); else WorldAssembler.Validate(WorldAssembler.Clone(world!)); });

        if (map is not null && context.ValidateMapResources is {} mapResources)
            Run("resources", () => mapResources(CopyMap(map)));
        else if (world is not null && context.ValidateWorldResources is {} worldResources)
            Run("resources", () => worldResources(WorldAssembler.Clone(world)));
        else Skip("resources", "unavailable", "Installed game/mod sprite, model and provider catalogs are unavailable. Saved dependency declarations are not proof of compatibility.");

        // Zone checks remain independent of unrelated structural or resource failures.
        if (map is not null) Run("zones", () => CheckZones(CopyMap(map)), new(CellX: map.WorldX, CellY: map.WorldY));
        else foreach (var member in world!.Maps.OrderBy(m => m.Id, StringComparer.Ordinal))
            Run("zones", () => CheckZones(CopyMap(member.Project)), new(MemberId: member.Id, CellX: member.Project.WorldX, CellY: member.Project.WorldY));

        findings.Add(new("info","zones","native-density-scale",NativeZombieIntensity.ExportNotice));

        var accessMember = world?.Maps.FirstOrDefault(m => m.Id == request.MemberId);
        if (world is not null && accessMember is null)
            Skip("access", "unavailable", "Choose MemberId and an access crop to inspect a world section. Whole-world route coverage is unavailable.");
        else
        {
            var index = checks.Count;
            DoctorCheck? coverageCheck = null;
            Run("access", () =>
            {
                var report = map is not null
                    ? WalkabilityAnalyzer.Analyze(CopyMap(map), request.Access, token, context.TileProperties)
                    : WalkabilityAnalyzer.World(WorldAssembler.Clone(world!), accessMember!.Id, request.Access, token, context.TileProperties);
                var p = map ?? accessMember!.Project;
                foreach (var issue in report.Issues)
                    findings.Add(new(issue.Severity, "access", issue.Code, issue.Message,
                        new(accessMember?.Id, issue.BuildingId, issue.X, issue.Y, issue.Z, p.WorldX, p.WorldY)));
                var cropped = report.Coverage.X != 0 || report.Coverage.Y != 0 || report.Coverage.Width != p.Width || report.Coverage.Height != p.Height;
                var partial = !report.Complete || cropped || world is not null || context.TileProperties is null;
                var message = $"Section-local crop {report.Coverage.X},{report.Coverage.Y}, {report.Coverage.Width}×{report.Coverage.Height}; {report.Method}";
                if (cropped) message += " Tiles outside this crop were not checked.";
                if (world is not null) message += " Other sections and routes across section boundaries were not checked.";
                if (context.TileProperties is null) message += " Native tile physics were unavailable; saved flags were used.";
                if (partial) findings.Add(new("info", "access", "partial", message));
                foreach (var note in report.Notes) findings.Add(new("info", "access", "coverage", note));
                coverageCheck = new("access", partial ? "partial" : report.Issues.Any(i => i.Severity == "error") ? "fail" : "pass", message);
            }, accessMember is null ? null : new(MemberId: accessMember.Id));
            if (coverageCheck is not null) checks[index] = coverageCheck;
        }

        if (world is null) Skip("seams", "not-applicable", "A standalone map has no supplied neighboring world sections.");
        else
        {
            Run("seams", () =>
            {
                var warnings = WorldAssembler.SeamWarnings(WorldAssembler.Clone(world));
                foreach (var warning in warnings) findings.Add(new("warning", "seams", "seam", warning));
                // The existing implementation truncates at 64 and supplies text, not exact seam coordinates.
                if (warnings.Length == 64) Skip("seams", "partial", "Existing seam diagnostics reached their 64-warning limit; additional warnings may be omitted.");
            });
        }

        if (context.InstalledMaps is null) Skip("cell-conflicts", "unavailable", "Installed map folders were not scanned. No claim is made about occupied native cells.");
        else Run("cell-conflicts", () =>
        {
            foreach (var p in map is not null ? new[] { map } : world!.Maps.Select(m => m.Project))
                if (p.Width is < 256 or > 2048 || p.Height is < 256 or > 2048 || p.Width % 256 != 0 || p.Height % 256 != 0 || p.WorldX is < 0 or > 1000 || p.WorldY is < 0 or > 1000)
                    throw new ArgumentException("Cell conflict checks require valid native cell dimensions and world coordinates.");
            var cells = map is not null ? CellConflicts.Cells(map) : CellConflicts.Cells(world!);
            // Do not exclude a map based only on its display name: another map may share that name.
            foreach (var conflict in CellConflicts.Check(cells, context.InstalledMaps))
                findings.Add(new("warning", "cell-conflicts", "occupied-cell", $"{conflict.Map}: {conflict.Count} native cell(s) are already occupied.",
                    new(CellX: conflict.First.X, CellY: conflict.First.Y, Source: conflict.Source)));
            if (context.InstalledMapsCoverageNote is {} note) Skip("cell-conflicts", "partial", note);
        });

        Skip("paths", "unsupported", "PZW/TMX/TBX/TSX dependency paths and missing PNG declarations are not inspected: this request contains native authoring JSON, not those source files. No cleanup is performed.");
        Skip("tile-compatibility", "unavailable", "Use the tile snapshot comparison endpoint with an explicit baseline. Project validation alone cannot detect tile-definition drift.");
        var ordered = findings.OrderBy(f => f.Category, StringComparer.Ordinal).ThenBy(f => f.Code, StringComparer.Ordinal)
            .ThenBy(f => f.Location?.MemberId, StringComparer.Ordinal).ThenBy(f => f.Location?.Y).ThenBy(f => f.Location?.X)
            .ThenBy(f => f.Message, StringComparer.Ordinal).ToArray();
        // Several sections/adapters can contribute to one category; expose one aggregate check per category.
        int Rank(string status) => status switch { "fail" => 7, "unsupported" => 6, "unavailable" => 5, "partial" => 4, "warning" => 3, "pass" => 2, _ => 1 };
        var grouped = checks.GroupBy(c => c.Category, StringComparer.Ordinal).Select(group =>
        {
            var status = group.OrderByDescending(c => Rank(c.Status)).First().Status;
            if (status == "pass" && ordered.Any(f => f.Category == group.Key && f.Severity == "warning")) status = "warning";
            return new DoctorCheck(group.Key, status, string.Join(" ", group.Select(c => c.Message).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)));
        }).OrderBy(c => c.Category, StringComparer.Ordinal).ToArray();
        var complete = grouped.All(c => c.Status is "pass" or "fail" or "warning" or "not-applicable");
        return new(complete, ordered.Any(f => f.Severity == "error") ? "error" : !complete || ordered.Any(f => f.Severity == "warning") ? "warning" : "pass",
            grouped, ordered);
    }
}
