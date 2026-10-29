using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimplePZMapmaker.Core;

public sealed record OvertureSupplementResult(string Json, int Candidates, int Added, int Existing, int TooSmall, int Outside, List<string> Warnings);

public static partial class GeographicImporter
{
    // Gap-fill design adapted from KnoxMap generator/overture.py (MIT), inspected at
    // 5d8195f94f751264d3b4fd6b0098e6e3dcf3df64. Copyright (c) 2026 spytheeuclidean-a11y.
    // Our adapter retains original rings and source identifiers. See docs/licenses/KNOXMAP-MIT.txt.
    /// <summary>
    /// Supplement a saved OSM/GeoJSON baseline with saved Overture building footprints.
    /// Counters describe Polygon parts (including repeated candidates). Any incomplete or invalid
    /// supplement fails the whole operation. Boundary selection uses filled polygon intersection;
    /// crossing footprints retain their original rings for the importer's later section clipping.
    /// Building parts, underground buildings and raised minimum floors are rejected before ground
    /// placement. No confidence, floor plan or unobserved building attributes are generated.
    /// </summary>
    public static OvertureSupplementResult MergeOvertureBuildings(string baselineJson, string overtureGeoJson, GeoBounds bounds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WorldLayoutLimits.ValidateSourceBounds(bounds);
        CheckOvertureBytes(baselineJson); CheckOvertureBytes(overtureGeoJson);
        var budget = new OvertureWork(cancellationToken);
        using var document = Parse(overtureGeoJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var collectionType) || collectionType.ValueKind != JsonValueKind.String || collectionType.GetString() != "FeatureCollection"
            || !root.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array || root.TryGetProperty("crs", out _))
            throw new ArgumentException("Supply a WGS84 Overture GeoJSON FeatureCollection without a CRS override.");

        // Local metre coordinates keep topology tolerances independent of latitude and avoid
        // cancellation in area/centroid sums for small footprints at large longitudes.
        var scaleX = 111320 * Math.Cos((bounds.South + bounds.North) * Math.PI / 360);
        (double X, double Y) Project((double X, double Y) p) => ((p.X - bounds.West) * scaleX, (p.Y - bounds.South) * 111320);
        var index = new OvertureIndex(budget);
        var baselineIds = new HashSet<string>(StringComparer.Ordinal);
        var baselinePartRoots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in ReadWorldFeatures(baselineJson))
        {
            budget.Step();
            var sourceId = feature.Tags["__pz_source"]; baselineIds.Add(sourceId);
            var suffix = sourceId.LastIndexOf("-part-", StringComparison.Ordinal);
            if (suffix >= 0 && int.TryParse(sourceId.AsSpan(suffix + 6), NumberStyles.None, CultureInfo.InvariantCulture, out _)) baselinePartRoots.Add(sourceId[..suffix]);
            if (feature.Polygon && feature.Tags.TryGetValue("building", out var building) && building is not ("no" or "0" or "false"))
                index.Add(new OvertureFootprint(feature.Rings.Select(r => r.Select(Project).ToList()).ToList()));
        }

        var rectangle = new OvertureFootprint([[
            Project((bounds.West, bounds.South)), Project((bounds.East, bounds.South)),
            Project((bounds.East, bounds.North)), Project((bounds.West, bounds.North)), Project((bounds.West, bounds.South))]]);
        var accepted = new JsonArray(); var ids = new HashSet<string>(StringComparer.Ordinal);
        var candidates = 0; var added = 0; var existing = 0; var small = 0; var outside = 0; var featureCount = 0;
        foreach (var feature in features.EnumerateArray())
        {
            budget.Step();
            if (++featureCount > budget.Limits.Features) throw new ArgumentException("Overture feature budget exceeded.");
            if (feature.ValueKind != JsonValueKind.Object || !feature.TryGetProperty("type", out var featureType) || featureType.ValueKind != JsonValueKind.String || featureType.GetString() != "Feature"
                || !feature.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object
                || !feature.TryGetProperty("geometry", out var geometry) || geometry.ValueKind != JsonValueKind.Object
                || !geometry.TryGetProperty("type", out var geometryType) || geometryType.ValueKind != JsonValueKind.String
                || !geometry.TryGetProperty("coordinates", out var coordinates) || coordinates.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("Each Overture building needs a source id and complete Polygon or MultiPolygon geometry.");
            var type = geometryType.GetString();
            if (type is not ("Polygon" or "MultiPolygon") || coordinates.GetArrayLength() == 0)
                throw new ArgumentException("Overture buildings require nonempty Polygon or MultiPolygon coordinates.");
            ValidateOvertureGroundPlacement(properties);
            var rawId = ReadOvertureId(feature, properties);
            var id = "overture-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawId)));
            var parts = type == "Polygon" ? new[] { coordinates } : coordinates.EnumerateArray().ToArray();
            var partIndex = 0; var featureParts = new OvertureIndex(budget);
            foreach (var part in parts)
            {
                budget.Step();
                if (++candidates > budget.Limits.Features) throw new ArgumentException("Overture polygon part budget exceeded.");
                var partId = type == "Polygon" ? id : id + "-part-" + partIndex.ToString(CultureInfo.InvariantCulture);
                partIndex++;
                var rings = ReadOvertureRings(part, Project, budget);
                var footprint = new OvertureFootprint(rings);
                ValidateOvertureFootprint(footprint, budget);
                if (featureParts.Query(footprint).Any(other => OvertureOverlaps(footprint, other, budget))) throw new ArgumentException("Overture MultiPolygon parts overlap.");
                featureParts.Add(footprint);
                // Validate every part, even a duplicate/outside part: a partial source must never
                // be reported as completely processed just because its first valid part was used.
                if (!ids.Add(partId) || baselineIds.Contains(partId) || baselineIds.Contains(id) || type == "Polygon" && baselinePartRoots.Contains(id)) { existing++; continue; }
                if (OvertureNetArea(rings) < 10) { small++; continue; }
                if (!OvertureOverlaps(footprint, rectangle, budget)) { outside++; continue; }
                if (index.Query(footprint).Any(old => OvertureOverlaps(footprint, old, budget))) { existing++; continue; }

                var tags = (JsonObject)JsonNode.Parse(properties.GetRawText())!;
                tags["pz:source_id"] = partId;
                tags["overture:id"] = rawId;
                tags["source"] = "Overture Maps";
                tags["source:attribution"] = "Overture Maps contributors";
                tags["source:license"] = "ODbL-1.0";
                var suppliedClass = properties.TryGetProperty("class", out var classNode) && classNode.ValueKind == JsonValueKind.String ? classNode.GetString() : null;
                tags["building"] = suppliedClass is "residential" or "commercial" or "industrial" or "retail" or "warehouse" ? suppliedClass : "yes";
                if (properties.TryGetProperty("num_floors", out var floors) && floors.ValueKind == JsonValueKind.Number && floors.TryGetInt32(out var levels) && levels > 0)
                    tags["building:levels"] = levels.ToString(CultureInfo.InvariantCulture);
                if (properties.TryGetProperty("height", out var height) && height.ValueKind == JsonValueKind.Number && height.TryGetDouble(out var metres) && double.IsFinite(metres) && metres > 0)
                    tags["height"] = metres.ToString("R", CultureInfo.InvariantCulture);
                if (properties.TryGetProperty("names", out var names) && names.ValueKind == JsonValueKind.Object && names.TryGetProperty("primary", out var name) && name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
                    tags["name"] = name.GetString();
                accepted.Add(new JsonObject { ["type"] = "Feature", ["id"] = partId, ["properties"] = tags,
                    ["geometry"] = new JsonObject { ["type"] = "Polygon", ["coordinates"] = JsonNode.Parse(part.GetRawText()) } });
                index.Add(footprint); added++;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var supplement = new JsonObject { ["type"] = "FeatureCollection", ["features"] = accepted }.ToJsonString();
        var combination = CombineSources([baselineJson, supplement], OvertureMaxBytes);
        cancellationToken.ThrowIfCancellationRequested();
        List<string> warnings = [.. combination.Diagnostics];
        if (candidates > 0) warnings.Add("Overture buildings theme is licensed under ODbL 1.0. Geometry is a roofprint, not a surveyed ground footprint. has_parts is retained as metadata; parts, interiors and confidence are not inferred.");
        return new(combination.GeoJson, candidates, added, existing, small, outside, warnings);
    }

    private static string ReadOvertureId(JsonElement feature, JsonElement properties)
    {
        // Official exports keep the GERS id in properties. Some GeoJSON exporters move it
        // to Feature.id. Prefer the official field and never hide a malformed supplied id.
        if (properties.TryGetProperty("id", out var official))
        {
            if (official.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(official.GetString())) throw new ArgumentException("Overture properties.id must be a nonempty source identity string.");
            return official.GetString()!;
        }
        if (feature.TryGetProperty("id", out var exported) && exported.ValueKind is JsonValueKind.String or JsonValueKind.Number && !string.IsNullOrWhiteSpace(exported.ToString())) return exported.ToString();
        throw new ArgumentException("Each Overture building needs a source id in properties.id or exported Feature.id.");
    }

    private static void ValidateOvertureGroundPlacement(JsonElement properties)
    {
        if (properties.TryGetProperty("type", out var type) && (type.ValueKind != JsonValueKind.String || type.GetString() != "building"))
            throw new ArgumentException("Only Overture type=building can supplement ground buildings; building_part is unsupported. Supply saved building outlines without parts.");
        if (properties.TryGetProperty("is_underground", out var underground) && underground.ValueKind != JsonValueKind.Null)
        {
            if (underground.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException("Overture is_underground must be boolean.");
            if (underground.GetBoolean()) throw new ArgumentException("Underground Overture buildings cannot be placed as ground buildings.");
        }
        if (properties.TryGetProperty("min_floor", out var minimum) && minimum.ValueKind != JsonValueKind.Null)
        {
            if (minimum.ValueKind != JsonValueKind.Number || !minimum.TryGetInt32(out var floor)) throw new ArgumentException("Overture min_floor must be an integer.");
            if (floor > 0) throw new ArgumentException("Raised Overture buildings with min_floor > 0 cannot be placed as ground buildings.");
        }
    }

    private const int OvertureMaxBytes = 64_000_000;
    private const double OvertureEpsilon = 1e-8; // metres / square metres in local coordinates

    private static void CheckOvertureBytes(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > OvertureMaxBytes || Encoding.UTF8.GetByteCount(json) > OvertureMaxBytes)
            throw new ArgumentException("Each saved geographic source must be nonempty and at most 64 MB of UTF8.");
    }

    private sealed class OvertureWork(CancellationToken token)
    {
        public GeographicGeometryBudget Limits { get; } = GeographicGeometryBudget.ForBytes(OvertureMaxBytes);
        private int work, points;
        public void Step()
        {
            token.ThrowIfCancellationRequested();
            if (++work > Limits.Work) throw new ArgumentException("Overture geometry work budget exceeded. Select a smaller area or simpler footprints.");
        }
        public void Point() { Step(); if (++points > Limits.Points) throw new ArgumentException("Overture point budget exceeded."); }
    }

    private sealed class OvertureFootprint(List<List<(double X, double Y)>> rings)
    {
        public List<List<(double X, double Y)>> Rings { get; } = rings;
        public double Left { get; } = rings[0].Min(p => p.X);
        public double Right { get; } = rings[0].Max(p => p.X);
        public double Bottom { get; } = rings[0].Min(p => p.Y);
        public double Top { get; } = rings[0].Max(p => p.Y);
    }

    // A bounded uniform spatial index avoids an all-pairs scan on village/city inputs.
    // Large envelopes use a separately bounded list instead of allocating millions of bins.
    private sealed class OvertureIndex(OvertureWork budget)
    {
        private readonly Dictionary<(long X, long Y), List<OvertureFootprint>> cells = [];
        private readonly List<OvertureFootprint> large = [], all = [];
        private static (long Left, long Right, long Bottom, long Top) Cells(OvertureFootprint p) => ((long)Math.Floor(p.Left / 64), (long)Math.Floor(p.Right / 64), (long)Math.Floor(p.Bottom / 64), (long)Math.Floor(p.Top / 64));
        public void Add(OvertureFootprint p)
        {
            budget.Step(); all.Add(p); var b = Cells(p);
            if ((b.Right - b.Left + 1) * (b.Top - b.Bottom + 1) > 1024) { large.Add(p); return; }
            for (var x = b.Left; x <= b.Right; x++) for (var y = b.Bottom; y <= b.Top; y++)
            { budget.Step(); if (!cells.TryGetValue((x, y), out var list)) cells[(x, y)] = list = []; list.Add(p); }
        }
        public IEnumerable<OvertureFootprint> Query(OvertureFootprint p)
        {
            var b = Cells(p); var seen = new HashSet<OvertureFootprint>();
            if ((b.Right - b.Left + 1) * (b.Top - b.Bottom + 1) > 1024)
            { foreach (var item in all) { budget.Step(); yield return item; } yield break; }
            foreach (var item in large) { budget.Step(); if (seen.Add(item)) yield return item; }
            for (var x = b.Left; x <= b.Right; x++) for (var y = b.Bottom; y <= b.Top; y++)
            {
                budget.Step();
                if (cells.TryGetValue((x, y), out var list)) foreach (var item in list) { budget.Step(); if (seen.Add(item)) yield return item; }
            }
        }
    }

    private static List<List<(double X, double Y)>> ReadOvertureRings(JsonElement part, Func<(double X, double Y), (double X, double Y)> project, OvertureWork budget)
    {
        if (part.ValueKind != JsonValueKind.Array || part.GetArrayLength() is < 1 or > 256) throw new ArgumentException("An Overture polygon needs a complete outer ring and bounded holes.");
        var rings = new List<List<(double X, double Y)>>();
        foreach (var ring in part.EnumerateArray())
        {
            budget.Step();
            if (ring.ValueKind != JsonValueKind.Array || ring.GetArrayLength() is < 4 or > 4096) throw new ArgumentException("Overture rings must be closed and contain 4–4096 positions.");
            var points = new List<(double X, double Y)>();
            foreach (var position in ring.EnumerateArray())
            {
                budget.Point();
                if (position.ValueKind != JsonValueKind.Array || position.GetArrayLength() is < 2 or > 3
                    || position[0].ValueKind != JsonValueKind.Number || !position[0].TryGetDouble(out var x) || !double.IsFinite(x) || x is < -180 or > 180
                    || position[1].ValueKind != JsonValueKind.Number || !position[1].TryGetDouble(out var y) || !double.IsFinite(y) || y is < -85 or > 85
                    || position.GetArrayLength() == 3 && (position[2].ValueKind != JsonValueKind.Number || !position[2].TryGetDouble(out var z) || !double.IsFinite(z)))
                    throw new ArgumentException("Invalid Overture WGS84 position.");
                points.Add(project((x, y)));
            }
            if (points[0] != points[^1]) throw new ArgumentException("Incomplete Overture ring: first and last positions must match.");
            rings.Add(points);
        }
        return rings;
    }

    private static double OvertureCross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
    private static double OvertureSignedArea(List<(double X, double Y)> ring)
    {
        double area = 0; var origin = ring[0];
        for (var i = 1; i < ring.Count - 1; i++) area += OvertureCross(origin, ring[i], ring[i + 1]);
        return area / 2;
    }
    private static double OvertureRingArea(List<(double X, double Y)> ring) => Math.Abs(OvertureSignedArea(ring));
    private static double OvertureNetArea(List<List<(double X, double Y)>> rings) => OvertureRingArea(rings[0]) - rings.Skip(1).Sum(OvertureRingArea);

    private static bool OvertureOnSegment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b) => Math.Abs(OvertureCross(a, b, p)) <= OvertureEpsilon
        && p.X >= Math.Min(a.X, b.X) - OvertureEpsilon && p.X <= Math.Max(a.X, b.X) + OvertureEpsilon && p.Y >= Math.Min(a.Y, b.Y) - OvertureEpsilon && p.Y <= Math.Max(a.Y, b.Y) + OvertureEpsilon;

    private static bool OvertureSegmentsMeet((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d, bool proper)
    {
        if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X) || Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y)) return false;
        var abC = OvertureCross(a, b, c); var abD = OvertureCross(a, b, d); var cdA = OvertureCross(c, d, a); var cdB = OvertureCross(c, d, b);
        if (((abC > OvertureEpsilon && abD < -OvertureEpsilon) || (abD > OvertureEpsilon && abC < -OvertureEpsilon))
            && ((cdA > OvertureEpsilon && cdB < -OvertureEpsilon) || (cdB > OvertureEpsilon && cdA < -OvertureEpsilon))) return true;
        return !proper && (OvertureOnSegment(a, c, d) || OvertureOnSegment(b, c, d) || OvertureOnSegment(c, a, b) || OvertureOnSegment(d, a, b));
    }

    private static bool OvertureInRing((double X, double Y) p, List<(double X, double Y)> ring, OvertureWork budget)
    {
        var inside = false;
        for (var i = 1; i < ring.Count; i++)
        {
            budget.Step(); var a = ring[i - 1]; var b = ring[i];
            if (OvertureOnSegment(p, a, b)) return false;
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }
    private static bool OvertureContains(OvertureFootprint f, (double X, double Y) p, OvertureWork budget)
    {
        if (!OvertureInRing(p, f.Rings[0], budget)) return false;
        foreach (var hole in f.Rings.Skip(1))
        {
            if (OvertureInRing(p, hole, budget)) return false;
            for (var i = 1; i < hole.Count; i++) { budget.Step(); if (OvertureOnSegment(p, hole[i - 1], hole[i])) return false; }
        }
        return true;
    }

    private static void ValidateOvertureFootprint(OvertureFootprint f, OvertureWork budget)
    {
        foreach (var ring in f.Rings)
        {
            if (OvertureRingArea(ring) <= OvertureEpsilon) throw new ArgumentException("Degenerate Overture ring.");
            for (var i = 0; i < ring.Count - 1; i++)
            {
                budget.Step();
                if (ring[i] == ring[i + 1]) throw new ArgumentException("Overture ring has a zero length edge.");
                var next = ring[(i + 2) % (ring.Count - 1)];
                if (OvertureOnSegment(next, ring[i], ring[i + 1]) || OvertureOnSegment(ring[i], ring[i + 1], next)) throw new ArgumentException("Overture ring folds back over an edge.");
                for (var j = i + 2; j < ring.Count - 1; j++)
                {
                    budget.Step(); if (i == 0 && j == ring.Count - 2) continue;
                    if (OvertureSegmentsMeet(ring[i], ring[i + 1], ring[j], ring[j + 1], false)) throw new ArgumentException("Overture ring intersects itself.");
                }
            }
        }
        for (var i = 1; i < f.Rings.Count; i++)
        {
            var hole = f.Rings[i];
            if (!OvertureInRing(hole[0], f.Rings[0], budget)) throw new ArgumentException("Overture hole is outside its outer ring.");
            for (var j = 0; j < i; j++)
            {
                var other = f.Rings[j];
                if (j > 0 && (OvertureInRing(hole[0], other, budget) || OvertureInRing(other[0], hole, budget))) throw new ArgumentException("Overture holes overlap or nest.");
                for (var a = 1; a < hole.Count; a++) for (var b = 1; b < other.Count; b++)
                { budget.Step(); if (OvertureSegmentsMeet(hole[a - 1], hole[a], other[b - 1], other[b], false)) throw new ArgumentException("Overture polygon rings cross or touch."); }
            }
        }
    }

    private static bool OvertureOverlaps(OvertureFootprint a, OvertureFootprint b, OvertureWork budget)
    {
        budget.Step();
        if (a.Right <= b.Left || b.Right <= a.Left || a.Top <= b.Bottom || b.Top <= a.Bottom) return false;
        // Strict interior tests and proper crossings distinguish overlap from a shared wall.
        bool SamplesInside(OvertureFootprint from, OvertureFootprint into)
        {
            var ring = from.Rings[0]; double cx = 0, cy = 0, sum = 0; var origin = ring[0];
            for (var i = 1; i < ring.Count - 1; i++)
            {
                budget.Step(); var cross = OvertureCross(origin, ring[i], ring[i + 1]); sum += cross;
                cx += ((ring[i].X - origin.X) + (ring[i + 1].X - origin.X)) * cross;
                cy += ((ring[i].Y - origin.Y) + (ring[i + 1].Y - origin.Y)) * cross;
            }
            if (Math.Abs(sum) > OvertureEpsilon)
            {
                var centroid = (origin.X + cx / (3 * sum), origin.Y + cy / (3 * sum));
                if (OvertureContains(from, centroid, budget) && OvertureContains(into, centroid, budget)) return true;
            }
            for (var i = 1; i < ring.Count; i++)
            {
                budget.Step();
                if (OvertureContains(into, ring[i], budget) || OvertureContains(into, ((ring[i - 1].X + ring[i].X) / 2, (ring[i - 1].Y + ring[i].Y) / 2), budget)) return true;
            }
            return false;
        }
        if (SamplesInside(a, b) || SamplesInside(b, a)) return true;
        for (var ai = 0; ai < a.Rings.Count; ai++) for (var bi = 0; bi < b.Rings.Count; bi++)
        {
            var ar = a.Rings[ai]; var br = b.Rings[bi];
            var sideA = Math.Sign(OvertureSignedArea(ar)) * (ai == 0 ? 1 : -1);
            var sideB = Math.Sign(OvertureSignedArea(br)) * (bi == 0 ? 1 : -1);
            for (var i = 1; i < ar.Count; i++) for (var j = 1; j < br.Count; j++)
            {
                budget.Step(); var p = ar[i - 1]; var q = ar[i]; var r = br[j - 1]; var s = br[j];
                if (OvertureSegmentsMeet(p, q, r, s, true)) return true;
                // Coincident edges overlap only when their filled sides agree. This catches
                // identical courtyards (centroid inside a hole) without swallowing shared walls.
                if (Math.Abs(OvertureCross(p, q, r)) > OvertureEpsilon || Math.Abs(OvertureCross(p, q, s)) > OvertureEpsilon) continue;
                var horizontal = Math.Abs(q.X - p.X) >= Math.Abs(q.Y - p.Y);
                var lo = Math.Max(horizontal ? Math.Min(p.X, q.X) : Math.Min(p.Y, q.Y), horizontal ? Math.Min(r.X, s.X) : Math.Min(r.Y, s.Y));
                var hi = Math.Min(horizontal ? Math.Max(p.X, q.X) : Math.Max(p.Y, q.Y), horizontal ? Math.Max(r.X, s.X) : Math.Max(r.Y, s.Y));
                var direction = (q.X - p.X) * (s.X - r.X) + (q.Y - p.Y) * (s.Y - r.Y);
                if (hi - lo > OvertureEpsilon && (direction > 0 ? sideA == sideB : sideA == -sideB)) return true;
            }
        }
        return false;
    }
}
