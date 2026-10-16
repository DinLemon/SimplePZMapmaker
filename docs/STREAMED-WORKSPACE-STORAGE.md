# Streamed workspace storage and native export

Saved workspaces use a small `SimplePZWorldSections` index and immutable SHA-256 blobs. Section reads and saves do not read the complete world. Full-world JSON remains a compatibility operation with its own 128 MiB budget.

## HTTP boundary

- `GET /api/world/workspaces/{id}` returns the manifest.
- Manifest and section GET requests accept `?version=0..3` for the current or one of three retained successful revisions. Recovery reads do not replace the current revision. Reapplying recovered authoring data requires an ordinary revision-checked save against the current manifest.
- `GET /api/world/workspaces/{id}/sections/{memberId}` returns `{revision, member, shared, sections, sharedInfrastructure}`. `member.project` is the independently editable source. `shared` retains intersecting authoritative world-coordinate buildings and paths plus the geographic reference. It omits elevation and unrelated geometry. `sections` contains layout/hash summaries, without neighbour rasters. `sharedInfrastructure` contains clipped native deck/stair/support tiles and holes.
- `POST /api/world/workspaces/{id}/sections/{memberId}` accepts `{project, expectedRevision}`. Dimensions and world-cell placement stay fixed. Stale revisions, invalid shared occupancy and cancellation preserve the committed index.
- `POST /api/world/workspaces/{id}/sections` accepts `{memberId, project, expectedRevision}` to append one independently supplied section. It checks unique identity, non-overlapping layout, native-cell budget and shared geometry without materializing old section rasters. Initial creation from a single-section world remains a small ordinary workspace request; further sections use this bounded append path.
- `POST /api/world/workspaces/{id}/sections/{memberId}/validate` accepts the same body and returns `{valid:true, revision}` after local/shared validation.
- Existing workspace `/export` and `/sections/{memberId}/export` downloads stream the saved revision into a native ZIP.
- Existing workspace `/export/jobs` and `/sections/{memberId}/export/jobs` queue a pinned revision. `/api/export/jobs/{jobId}` reports section/cell progress, failure or cancellation; `/cancel` cancels; `/download` serves completed artifacts.

## Trusted in-process boundary

`WorldStore.PinAsync(id, token, version:0)` returns a disposable `WorldSectionArchive.RevisionLease`. Its public manifest is a defensive copy. The lease exposes `ReadMetadataAsync`, `ReadSectionAsync`, `ReadProjectedSectionAsync(memberId, projectOverride, token)`, and source-copy methods. Projected sections contain intentionally partial shared building origins; run ordinary project validation on authored source, rather than validating a projected section as a standalone map. Native scene callers validate resources and compare the request's expected revision before installing the trusted projection.

`ExportJobStore.StartWorkspace(lease, assets, models, memberId)` takes ownership of the lease, including queue rejection, cancellation and worker completion. Pruning keeps every leased metadata/section hash even after four recovery saves or a workspace clear. Application restart marks unfinished jobs interrupted; jobs are not silently resumed against newer data.

The native writer composes one authored/projected section at a time. Shared supports and stair landings sample actual terrain from required neighbours; at most one additional neighbour project is retained while sampling, then released before composition. Missing required terrain fails explicitly. Shared authoritative geometry is built in world coordinates before clipping. Local/shared resources are validated against their saved providers on the same revision as the writer.

## Explicit budgets

- One section blob: 48 MiB; shared metadata: 16 MiB; manifest: 1 MiB.
- Layout: 128 sections and 8,192 non-overlapping native cells. There is no aggregate source-byte limit for section reads/saves or workspace export jobs.
- Native output: 256 MiB compressed; zone/atlas descriptions: 64 million characters/UTF-8 bytes respectively; attribution: 16 million UTF-8 bytes; static models: 100,000 placements.
- Jobs: at most two workers and eight queued; 30 minute execution deadline. Existing cache and artifact retention budgets still apply.
- The compatibility full-world JSON request/response path retains its 128 MiB source budget. Creating a workspace from one monolithic request also retains that HTTP limit.

These bounds are independent of the total number of source raster tiles. Parsed metadata, atlas descriptions, native cell composition and registered model placements also consume memory within their explicit budgets.

## Reconstructing a workspace from exported sources

A complete streamed ZIP contains `source/world.pzworld.index.json`, `source/world.metadata.json`, and `source/sections/<memberId>.pzmap.json`. These are the original authored blobs, including source evidence, resource owners, required mods, density/rule provenance and manual edits. Native clipped geometry does not replace authoring data.

To restore the workspace without constructing a complete `WorldProject` in RAM:

1. Read the small index and use its `storageId` as the destination workspace ID.
2. Create `<storageId>.sections` in the workspace storage directory.
3. Copy `world.metadata.json` to `<storageId>.sections/<metadataHash>.section.json`.
4. For each section entry, copy `sections/<id>.pzmap.json` to `<storageId>.sections/<hash>.section.json`. Verify its exact byte count and SHA-256 against the index before making the index authoritative.
5. Copy the index last, as `<storageId>.pzworld.index.json`. Reopen the workspace normally; each section is independently validated when loaded.

Selected-section ZIPs include only that section's source, a readable `project.pzmap.json`, complete shared source metadata and the original layout context. Their index alone cannot restore absent neighbour section files. Export the complete workspace for a complete restore.
