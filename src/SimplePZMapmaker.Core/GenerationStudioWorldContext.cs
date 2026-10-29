using System.Security.Cryptography;
using System.Text.Json;

namespace SimplePZMapmaker.Core;

/// <summary>Transient projection and occupancy for a raw section; never persisted into its map.</summary>
public sealed class GenerationStudioWorldContext
{
    readonly WorldProject world;
    readonly string projectFingerprint;
    public string MemberId { get; }
    public string WorldFingerprint { get; }
    public MapProject ProjectedProject { get; }
    public MapProject Projected => ProjectedProject;
    internal bool[] ReservedCells { get; }

    GenerationStudioWorldContext(WorldProject world, string memberId, string fingerprint, MapProject projected, bool[] reserved)
    {
        this.world = world; MemberId = memberId; projectFingerprint = fingerprint;
        WorldFingerprint = Fingerprint(world); ProjectedProject = projected; ReservedCells = reserved;
    }

    public static string Fingerprint(WorldProject world) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(world)));

    public static GenerationStudioWorldContext Create(MapProject project, WorldProject world, string? memberId)
    {
        WorldAssembler.Validate(world);
        var member = world.Maps.SingleOrDefault(m => m.Id == memberId) ?? throw new ArgumentException("Select the active world section for generation.");
        var fingerprint = GenerationStudio.Fingerprint(project);
        if (!string.Equals(fingerprint, GenerationStudio.Fingerprint(member.Project), StringComparison.Ordinal))
            throw new ArgumentException("The active map does not match its raw world section. Refresh the world snapshot before generation.");
        var projected = WorldAssembler.ProjectSection(world, member);
        var reserved = new bool[project.Terrain.Length];
        var ox = project.WorldX * 256; var oy = project.WorldY * 256;
        void Reserve(int worldX, int worldY)
        {
            var x = worldX - ox; var y = worldY - oy;
            if (x >= 0 && y >= 0 && x < project.Width && y < project.Height) reserved[y * project.Width + x] = true;
        }
        foreach (var building in world.Buildings) foreach (var cell in WorldAssembler.BuildingCells(building)) Reserve(cell.X, cell.Y);
        foreach (var feature in world.LinearFeatures) foreach (var cell in WorldInfrastructure.Rasterize(feature)) Reserve(cell.X, cell.Y);
        if (projected.SharedInfrastructure is { } geometry)
        {
            foreach (var tile in geometry.Tiles) reserved[tile.Y * project.Width + tile.X] = true;
            foreach (var hole in geometry.Holes) reserved[hole.Y * project.Width + hole.X] = true;
        }
        // Shared railway artwork may extend one cell beyond its sampled centerline.
        foreach (var decoration in projected.Decorations) foreach (var part in decoration.Parts) reserved[(decoration.Y + part.Dy) * project.Width + decoration.X + part.Dx] = true;
        foreach(var piece in FenceLines.Compose(projected.Fences,projected.FenceStyles,projected.GateStyles,project.Width,project.Height).Pieces)reserved[piece.Y*project.Width+piece.X]=true;
        return new(world, member.Id, fingerprint, projected, reserved);
    }

    internal void ValidateSource(MapProject project)
    {
        if (!string.Equals(projectFingerprint, GenerationStudio.Fingerprint(project), StringComparison.Ordinal)) throw new ArgumentException("The map changed after its world context was prepared.");
    }

    public void ValidateCandidate(MapProject candidate)
    {
        var copy = WorldAssembler.Clone(world);
        copy.Maps.Single(m => m.Id == MemberId).Project = candidate;
        WorldAssembler.Validate(copy);
    }
}
