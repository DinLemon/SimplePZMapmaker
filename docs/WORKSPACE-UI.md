# Task workspaces and expanded geographic layouts

## Editor interface

The main editor uses **Create**, **Edit map**, and **Check & export**. These are presentations of one project. Save and Build mod stay in the header; Project and Settings collect less frequent commands. The brand returns to Create without reloading or discarding the map.

- **Create** leads with Choose a real place, blank-map creation and opening a project. Continue editing returns to the current map. The guide invitation is inline rather than covering the canvas.
- **Edit map** gives the canvas the available space. Floor operations share the left tools panel. Properties and Layers reveal the same inspector. Narrow screens use drawers. View options collect less frequent display actions. Navigation keeps the camera and pending drafts. Map undo/redo shortcuts stay in Edit; visible text fields keep native editing.
- **Check & export** gathers diagnostics, source/site inspection, resource setup, build entry points and history. Returning to a source finding or drawing a site opens Edit. Completed jobs remain accessible after closing an import wizard.

Existing workshops keep their controllers and project ownership. Navigation never starts generation, import or export. Successful explicit creation/open commands enter Edit; cancelling preserves the current map. The guide reveals the relevant workspace and collapsed control before highlighting it.

## Exceeding the ordinary world layout

Select an area in Real places and choose **Set up linked world**. The ordinary allowance is 128 sections / 768 native cells. Larger layouts show rows, columns, tile dimensions, game-cell count, aggregate raster work and a separate bounded resident-raster estimate. **Use N connected sections** approves that exact layout. It starts no download and preserves area and metres per tile.

Changing bounds, scale, section size, origin, rotation or deformation revokes approval. Changing back does not restore it automatically. Explicit expanded layouts support up to **65,536 sections and 65,536 native cells**, within supported coordinates. Smaller sections do not reduce total native cells. Beyond the hard budget, use less area or a deliberately coarser scale.

## Incremental assembly

New section imports compose and save one section at a time into private staging. A second pass resolves source buildings in original order using only sections intersecting the candidate and earlier overlapping shared buildings. Crossing fences merge once with their gates in global coordinates. Roads, railways and bridges keep their section projections under the same geographic reference. Shared metadata and each projected section are validated before one atomic manifest publication. Cancellation removes owned unpublished staging and retains original worlds and sources.

Aggregate base raster work is `cells * (256*256 + 32*32*4)` bytes, not peak RAM or the serialized file size. Building windows retain at most **256 native cells of base rasters (17 MiB)**. Sources, game-resource caches, shared plans, serializer state and temporary copies need additional memory. Selected source geometry stays within 64 MB; per-section, shared object and coordinate budgets remain separate. This is bounded import, not unlimited size.

Small portable imports retain the compatibility path. Refreshing a supplied complete authored world retains the prior in-memory algorithm and its **8,192-cell** cap. Existing large workspaces remain independently editable by section. Complete-world portable JSON retains its **128 MB** limit and is not loaded to assemble or export a new large section world.

## Verification

Use an isolated workspace and preview, preserving existing user maps:

```powershell
$env:PZMAP_TEST_URL='http://127.0.0.1:48822'
node scripts/verify-workspace-redesign.mjs
node scripts/verify-expanded-world.mjs
node scripts/verify-real-places-usability.mjs
node scripts/verify-interactive-guide.mjs
$env:PZMAP_PLACE_MAP_BROWSER='1'
$env:PZMAP_WORLD_WIZARD_BROWSER='1'
npm test
$env:PZMAP_WORKSPACE=(Get-Location).Path
dotnet run --project tests/SimplePZMapmaker.Tests -c Release
```

Run the larger production offline proof separately; it writes roughly 2 GiB of disposable section data:

```powershell
python scripts/verify-incremental-world.py
```

That fixture covers a 54 by 18 grid of 1,024-tile sections (15,552 cells) at one metre per tile, independent of online OSM availability. Request, job, result checks and seam/transport samples are retained under `work/incremental-world-import/`. Smaller UI artifacts remain in `work/workspace-redesign/` and `work/real-places-usability/`. Source preparation has separate data, download and processing budgets. Scripted usability checks do not replace participant studies or fresh game-client playtests.
