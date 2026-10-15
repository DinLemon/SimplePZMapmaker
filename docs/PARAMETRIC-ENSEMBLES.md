# Parametric apartments and street ensembles

These tools create ordinary authored buildings, project library modules, terrain, game sprites and native model lights. Generated results remain editable with the existing building workshop and map tools.

## Create an apartment building

1. Open **Building editor** and choose **Create your own building template**, or open the workshop for an existing map building.
2. Expand **Parametric apartment buildings** in the building controls.
3. Set the entrance count, floor count and **Apartments / entrance / floor**. For example, 2 entrances × 3 floors × 3 apartments produces exactly **18 apartments**.
4. Choose a continuous slab, staggered entrances or a recessed courtyard. Choose brick, plaster or wood for the facade. Set a seed to repeat the layout.
5. Click **Preview apartment building**. The preview reports the exact apartment count, footprint and reusable module count. Use **Preview floor** to inspect another floor.
6. Click **Use generated building as draft**. This replaces the current workshop draft in one undo step. It retains the draft's identity and map position. The ordinary workshop Apply action publishes it to the map; map collision rules still apply.
7. Optionally click **Save generated entrance and apartment modules**. The project library receives one full-height section per entrance and a furnished four-room apartment module. Select these under **Reusable building modules** to attach them to another draft. They use the same compose, save and export paths as manually captured modules.

Each apartment has four private rooms: living room, kitchen, bedroom and bathroom. Private apartment boundaries remain walls, each apartment has one shared-hall entrance, and every entrance section has its own clear staircase and ground-level entrance. The seed changes real room partitions and furnishings. It does not merely rename a stock template.

![Parametric controls and a generated upper-floor plan](screenshots/19-parametric-building.png)

### Native limits

- 1–4 entrances, 1–32 above-ground floors, and 1–4 apartments per entrance per floor.
- At most **8 apartments across all entrances on a floor**. Four private room identities per apartment plus the shared hall fit the native floor limit of 35 room identities. Settings over this limit are rejected with an explanation.
- The building fits the existing 64 × 64 tile envelope. Automatic roofs are omitted at z=31 so the roof cannot extend beyond the native signed vertical range.
- A courtyard with two entrances uses recessed wings; three or four entrances can form a central recess. An odd apartment count leaves a genuinely open portion of that entrance section's footprint.
- Generated modules use the existing library limit of 64 entries. Saving them is separate from replacing the draft.

## Generate a street ensemble

1. Create or open a map, or open a section for editing inside a world.
2. Expand **Street ensembles** in the map properties panel.
3. Choose **Residential frontage**, **Courtyard compound** or **Garden housing**.
4. Set the rectangle's X, Y, width and height in local map tiles. Use 40–256 tiles per side. A 100 × 80 rectangle fits the default two-entrance, three-floor building comfortably.
5. Set the building parameters and seed. Toggle fences, parking, greenery and native lights as needed.
6. Click **Preview ensemble**. The bounded drawing shows the complete building, continuous frontage street, sidewalk, parking apron, yard, two-tile entrance paths, fence openings and props. The status reports buildings, props, native lights and any unavailable resources.
7. Click **Apply entire ensemble**. The complete result is applied as one map or section history operation. Undo restores all terrain, buildings, props, model lights and dependencies together. Redo restores the ensemble.
8. Save the map or world normally. Project save/reload and native export retain the generated content.

![Garden ensemble preview with canonical game fences, greenery and native lights](screenshots/20-street-ensemble.png)

Preview leaves the project unchanged. Apply compares the current project with the preview snapshot and rejects stale proposals. Changing the map requires another preview. Changing the rectangle, preset or generation settings also invalidates the proposal.

Existing buildings, all tiles of furniture and model footprints, spawns, water, authored surface overrides, manual terrain edits, ground-level linear features and native infrastructure footprints are protected. Required building and access collisions reject the proposal. Optional yard decoration skips protected tiles. The generator cannot silently trim the requested apartment count to fit an occupied rectangle.

### Resources and lights

Fence facings come from the active game catalog's actual north/west physical properties. Every decoration uses `PlacementFor`, including its complete sprite grid and canonical physical flags. Active mod provider ownership and dependencies are retained and checked again on apply.

Street lights combine a canonical outdoor lamp sprite with a registered native model using the existing `light` behavior. If no lamp model exists, the registered `Base.LightBulb` model supplies the light component. Both use the existing native model export path. The generated point light is active, warm white, with a radius of 10 tiles. Its power state is authored; the editor does not simulate electrical supply. If usable light resources are unavailable, preview reports the omission.

The frontage includes a parking apron. It does not invent vehicle spawn distributions or force traffic. The preset creates one compound per proposal; repeat it in another clear rectangle to build a larger street.

## Verification

`ParametricBuildingTests.Cases()` verifies exact apartment counts, independent stair stacks, all layouts, determinism, real partition variation, reusable module capture, clear capacity rejection and the maximum native floor. `StreetEnsembleTests.Cases()` verifies immutable previews, bounded placement, complete prop footprints, clear entrance paths, stale/water rejection, all presets and native lights.

Run `node --test tests/generation-ui.test.mjs` for control validation. With the editor running, set `PZMAP_TEST_URL` and run `node scripts/verify-generation-browser.mjs` for the real browser workflow. It checks 18 generated apartments, floor preview, draft undo/redo, saving and reusing a generated apartment module, canonical game fences and native lights, ensemble history, and project save/reload. Screenshots and the saved fixture are written under `work/generation-qa`.

Validation uses read-only game resources. No game session is started, and no files are installed into the game directory.
