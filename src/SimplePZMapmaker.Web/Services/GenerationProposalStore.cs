using System.Text.Json;
using SimplePZMapmaker.Core;

namespace SimplePZMapmaker.Web.Services;

public sealed record GenerationStudioProposal(string ProposalId, string BaseFingerprint, GenerationStudioSummary Summary, MapProject Project, IReadOnlyList<GenerationBuildingProvenance> Provenance, string? WorldFingerprint = null, string? MemberId = null);
public sealed record GenerationStudioApplied(string ProposalId, string BaseFingerprint, GenerationStudioSummary Summary, MapProject Project, string? WorldFingerprint = null, string? MemberId = null);

/// <summary>Bounded, expiring server snapshots. Apply never accepts candidate data from a client.</summary>
public sealed class GenerationProposalStore
{
    const int MaxCount = 12;
    const long MaxBytes = 64 * 1024 * 1024;
    readonly object gate = new();
    readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    long bytes;
    sealed record Entry(string Fingerprint, byte[] Candidate, GenerationStudioSummary Summary, DateTimeOffset Created, string? WorldFingerprint, string? MemberId);

    public GenerationStudioProposal Add(GenerationStudioPreview preview)
    {
        var candidate = JsonSerializer.SerializeToUtf8Bytes(preview.Project);
        if (candidate.LongLength > MaxBytes) throw new ArgumentException("The generation preview exceeds the 64 MiB proposal budget. Use a smaller map.");
        lock (gate)
        {
            Prune();
            while (entries.Count >= MaxCount || bytes + candidate.LongLength > MaxBytes) Remove(entries.MinBy(e => e.Value.Created).Key);
            var id = Guid.NewGuid().ToString("N");
            entries.Add(id, new(preview.BaseFingerprint, candidate, preview.Summary, DateTimeOffset.UtcNow, preview.WorldFingerprint, preview.MemberId)); bytes += candidate.LongLength;
            return new(id, preview.BaseFingerprint, preview.Summary, JsonSerializer.Deserialize<MapProject>(candidate)!, GenerationStudio.Provenance(preview.Project), preview.WorldFingerprint, preview.MemberId);
        }
    }

    public GenerationStudioApplied Apply(MapProject project, string proposalId, WorldProject? world = null, string? memberId = null)
    {
        ProjectValidator.Validate(project); GenerationControl.Validate(project);
        if (proposalId is null || proposalId.Length != 32) throw new ArgumentException("The generation proposal is missing or expired. Preview again.");
        lock (gate)
        {
            Prune();
            if (!entries.TryGetValue(proposalId, out var entry)) throw new ArgumentException("The generation proposal is missing or expired. Preview again.");
            if (!string.Equals(GenerationStudio.Fingerprint(project), entry.Fingerprint, StringComparison.Ordinal)) throw new ArgumentException("The map changed since this preview. Preview again before applying.");
            GenerationStudioWorldContext? context = null;
            if (entry.WorldFingerprint is not null)
            {
                if (world is null || memberId != entry.MemberId) throw new ArgumentException("Apply requires the same world snapshot and section as its preview.");
                context = GenerationStudioWorldContext.Create(project, world, memberId);
                if (!string.Equals(context.WorldFingerprint, entry.WorldFingerprint, StringComparison.Ordinal)) throw new ArgumentException("Shared world geometry changed since this preview. Preview again before applying.");
            }
            else if (world is not null || memberId is not null) throw new ArgumentException("Preview this map with its world context before applying to a world section.");
            var candidate = JsonSerializer.Deserialize<MapProject>(entry.Candidate)!;
            ProjectValidator.Validate(candidate); GenerationControl.Validate(candidate);
            context?.ValidateCandidate(candidate);
            return new(proposalId, entry.Fingerprint, entry.Summary, candidate, entry.WorldFingerprint, entry.MemberId);
        }
    }

    void Prune()
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-15);
        foreach (var id in entries.Where(e => e.Value.Created < cutoff).Select(e => e.Key).ToArray()) Remove(id);
    }
    void Remove(string id) { bytes -= entries[id].Candidate.LongLength; entries.Remove(id); }
}
