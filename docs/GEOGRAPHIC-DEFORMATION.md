# Shared continuous geographic deformation

Implementation slice for item 6 of `superpowers/plans/2026-10-23-transfer-lessons.md`.
The transform is integrated into the Core importer, Web World import, strict CLI scenes, browser layout/inverse, elevation sampling and visual references.

## Coordinates and JSON

`WorldGeoReference.Warp` is optional. Missing/null keeps the existing affine
formulas, operation order and results, including global rotation. Null is omitted
when serializing, so ordinary affine references keep their previous JSON shape.

There is exactly one mesh for the world. Coordinates are floating point **tiles**
in the common affine frame: apply WGS84 longitude/latitude -> existing scale and
global rotation -> subtract the rotated reference's minimum corner, obtaining
the origin-cell-relative source plane. They are neither longitude/latitude nor
absolute game coordinates nor coordinates relative to a section. Multiply a
mesh distance by `metresPerTile` to get metres. Source evidence, source IDs,
`OriginalRings`, `SourceGeometry` and house `SourceOutline` retain real WGS84.

The stored form is deliberately a complete rectangular lattice. Its fixed
triangulation has no optional cells, triangle indices or duplicate per-section
vertices. Each cell is split on NW-SE, in stable row-major order: `(NW,NE,SE)`,
then `(NW,SE,SW)`. Destination vertices have X changing fastest. This example
compresses the whole 512-by-512 source domain to 384-by-384 destination tiles;
the reference's full affine footprint must fit inside that source domain:

```json
{
  "warp": {
    "sourceX": [0, 256, 512],
    "sourceY": [0, 256, 512],
    "destination": [
      {"x": 0, "y": 0}, {"x": 192, "y": 0}, {"x": 384, "y": 0},
      {"x": 0, "y": 192}, {"x": 192, "y": 192}, {"x": 384, "y": 192},
      {"x": 0, "y": 384}, {"x": 192, "y": 384}, {"x": 384, "y": 384}
    ]
  }
}
```

This uses the existing web/export camelCase JSON options; default
`JsonSerializer` uses the C# property names and also round-trips correctly.
The DTO is editable/serializable. `GeographicWarp` snapshots its arrays; editing
the DTO does not mutate an existing compiled mesh. Rebuild projections after
changing any part of the reference. Treat a reference as immutable during a job.

Forward mapping uses source-cell lookup and barycentric interpolation in that
cell's proper triangle. Inverse mapping searches **destination** triangles,
checks barycentric containment, and uses the corresponding source triangle.
On a shared edge both affine maps agree; a stable scan chooses the first
containing destination triangle. Only after world mapping is the section shift
`(Cell - OriginCell) * 256` subtracted. Triangulation and path splitting never
depend on which section is requesting them. Values within `1e-8` tile of the
source boundary are clamped for floating point roundoff; inverse containment has
a `1e-10` barycentric tolerance. Results are deterministic for the same inputs
and runtime; there is no promise of identical floating point bits across CPUs.

The transform is bounded. Points outside its source rectangle or destination
polygon throw `ArgumentException`; there is no affine fallback across a warped
boundary. Choose source/reference coverage before building the mesh. Intake
must preserve/report features extending outside coverage, or clip them with a
topology-preserving geometry operation while retaining their original WGS84.
Never clamp each vertex separately or treat an outside error as an empty house.
An OSM/source bounding-box query can return complete ways extending beyond the selected world. `ProjectGeometry` clips polygons (including removal of outside holes) and splits clipped polylines before mesh projection. Original source WGS84 is retained separately. The importer bounds aggregate projected geometry to 250,000 points before constructing all retained layers; exceeding it rejects the new result while preserving the current project.

## Protected village/block builder

```csharp
reference.Warp = GeographicWarpBuilder.Build(new() {
    // Use dimensions at least as large as RequiredTiles(reference with Warp=null).
    Width = 1000, Height = 1000, MaxStep = 100,
    FieldScaleX = .75, FieldScaleY = .80,
    Anchors = [
        new() { MinX=200, MinY=200, MaxX=300, MaxY=300, RotationDegrees=18 },
        new() { MinX=700, MinY=700, MaxX=850, MaxY=850 }
    ]
});
WorldGeoReference.Validate(reference);
```

All anchor bounds become lattice lines, and gaps are subdivided to at most
`MaxStep`. Anchors must be separated and strictly interior. Every vertex in a
protected rectangle is hard-constrained to

`destinationCentre + R(rotationDegrees) * (source - sourceCentre)`.

The scale is exactly 1 in tile units, for every point inside that rectangle,
because every contained triangle interpolates the same rigid affine transform.
Positive angles turn clockwise on screen (the source Y axis points south).
Null `DestinationCentre` uses the field-scaled source centre; an explicit centre
allows controlled movement. An anchor angle can be -180 through 180 degrees,
although a tight field transition will reject large rotations. Include the
whole block and its shared road/fence margins inside the protected rectangle.

This is **graph-harmonic mesh interpolation, not ARAP**. Let `B(x,y)` be the
global diagonal field compression and `d` the vertex correction to `B`. Boundary
corrections are zero; protected corrections are the rigid target minus `B`.
For unconstrained vertices minimize

`E(d) = (1/2) * sum_edges w_ij * ||d_i - d_j||^2`,

where edges are horizontal/vertical lattice neighbours and
`w_ij = 1 / ||source_i-source_j||^2`. The positive graph Laplacian with fixed
boundary has a unique minimizer for free vertices. Deterministic, in-place
row-major Gauss-Seidel sweeps replace each free `d_i` by its weighted neighbour
average. Stop at maximum update <= `1e-9` tile or reject after 10,000 sweeps.
The update threshold is a numerical stopping rule, not a certified global
error bound. The final finite piecewise-linear map is independently validated.

The outer boundary has exactly the requested compressed dimensions. Transition
fields can locally stretch or shear to accommodate rigid blocks; this is not
a guarantee that every field triangle has the requested local scale. There is
no hidden relaxation of anchors, angle or compression to make a bad proposal
pass. An impossible proposal throws; enlarge the transition area, adjust block
centres or reduce compression/rotation. Mesh resolution determines the shape
between anchors; no per-section rotation or stochastic fitting occurs.

## No folds, overlaps or mesh holes

Validation requires finite increasing source axes, a complete destination
array, and every destination triangle's signed area strictly positive. The
Jacobian determinant (destination twice-area / source twice-area) must be at
least `1e-6`, and destination twice-area must exceed `1e-8` tile squared.
Boundary segments may meet only at their adjacent common endpoints; collapse,
double-back, nonadjacent touch, crossing and coincident boundary segments reject.
Near boundary contacts within the predicate tolerances may conservatively reject.

Why checking only local determinants is not enough, and why this check is:
the fixed complete source lattice is an oriented triangulated topological disk.
The simple destination boundary has winding number one in its interior and
zero outside (its orientation follows from the sum of positive face areas).
Away from triangle edges, the degree is the sum of face signs covering a point.
Every sign is positive. Degree one therefore permits exactly one covering face,
and zero permits none. Overlapping interiors or an interior hole would violate
that count. Continuity and the incident positive face fans extend injectivity
to edges/vertices. Thus positive faces **plus** the simple boundary establish
the global homeomorphism; positive faces alone are deliberately rejected by
the annular-strip regression, whose local determinants all remain positive.
These are floating point checks with the stated numerical rejection margins,
not exact arithmetic predicates for arbitrarily degenerate coordinates.

## Geometry and bounds API

Use `Project` for actual points (entrances, markers). Use `ProjectPath` for every
polyline/closed ring and `ProjectRings` for a polygon's exterior and holes.
Rings must already repeat their first point. These helpers retain ring order,
closure and winding. They do not simplify, quantize or orthogonalize anything.
All endpoints are mapped through the same mesh; each source segment is first
split at lattice lines and the crossed cells' diagonals. Its image is the exact
piecewise-linear path, rather than a straight chord between warped endpoints.
No output segment crosses an unaccounted triangle edge. Original coordinates
must remain separate because output vertex counts increase.

Section clipping and rasterization happen **after** common-world splitting.
Keep holes and use identical rounding/ownership rules for every geometry type.
The warp itself is C0 continuous and orientation-preserving; it does not make
later independent house orthogonalization, fence snapping, road-width offsets
or parcel simplification consistent. Author houses needing rigid shape inside
anchors, and check those downstream operations in integrated acceptance.

`SectionBounds` intersects the destination section rectangle with **every**
destination triangle, maps each intersection polygon back through that
triangle, clips it to the affine reference footprint, and only then takes WGS84
extrema. It works even when a section corner is outside the mesh and catches
interior/edge extrema missed by inverse-projecting four corners. An empty
warped section returns `West==East` and `South==North`; skip source intake.
The result is a conservative bounding box, not the exact source polygon.

`RequiredTiles` intersects the actual affine geographic footprint with **every**
source triangle and evaluates all mapped intersection vertices. Boundary bulges
and intersections along the rotated footprint contribute to its extrema.
Destination coordinates cannot be negative, so this fits the existing
origin-anchored `(Width,Height)` API. Extra mesh area outside the reference
footprint does not enlarge required tiles. Affine-only bounds are unchanged.

Limits: 33 coordinates per axis (1,089 vertices / 2,048 triangles), minimum
source spacing .01 tile, maximum source/destination coordinate 1,000,000 tiles;
100,000 input/output points per path, 256 rings and 100,000 total output points
per `ProjectRings`; 2,000,000 visited-cell path-subdivision steps; 32 builder
anchors; builder field scales .01 through 1. Bounds and scale limits inherited
from `WorldGeoReference` still apply. Forward lookup is logarithmic in axis
length; inverse and region bounds scan at most 2,048 triangles. These limits do
not raise the existing world section/cell/native-export or parcel vertex limits.

## Integrated entry points and acceptance

`POST /api/geographic/warp/preview` accepts `{reference, options}`; dimensions are derived from the complete rotated affine footprint. `simplepzmap geographic-warp spec.json --out reference.json` writes the same validated frame for scene `reference`. World import offers the common-frame controls under its area settings, locks a saved frame while preserving edits and previews the deformed footprint before section padding. Changing metadata alone does not move existing authored objects.

The builder uses harmonic displacement, with a validated monotone radial transition fallback for one isolated rigid anchor when a large turn would fold harmonic strips. It is not ARAP. Invalid/overlapping/folded meshes are rejected. `ProjectGeometry` clips external fetched geometry to the mesh domain, subdivides it and keeps original WGS84 separately; format 15 allows removed holes or split lines to have independent projected topology. Bulk inverse consumers skip padding through `TryGeographic`; no affine extrapolation is invented.

Actual offline Web import/refresh and strict CLI acceptance use `scripts/verify-transfer-warp-import.mjs`: four linked sections, one shared house, one shared fence, retained hole and a preserved common frame. Evidence is under `work/transfer-acceptance`. Current end-to-end evidence and external client limits are summarized in TRANSFER-LESSONS-IMPLEMENTATION.md.
