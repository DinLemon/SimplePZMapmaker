using System.Buffers.Binary;
using System.Text;
using SimplePZMapmaker.Core;
using SimplePZMapmaker.Web.Services;

public static class LargePbfTests
{
    // Register Cases() in the main runner when integrating; this file deliberately does not edit it.
    public static IEnumerable<(string, Action)> Cases()
    {
        foreach (var compressed in new[] { false, true })
        foreach (var mode in new[] { "original", "road", "rail", "polygon", "hole", "split", "missing", "poi", "barrier", "healthcare" })
        foreach (var incomplete in new[] { false, true })
            yield return ($"large PBF legacy geometry/omission parity {compressed}/{mode}/{incomplete}", () => InDirectory(directory =>
            {
                var path = Write(directory, UpstreamTests.PbfFixture(compressed, incomplete, mode));
                foreach (var bounds in new[] { Interior(), Evidence(), new GeoBounds { South = 54.776, North = 54.778, West = 32.050, East = 32.052 } })
                    Equal(PbfExtractReader.Read(path, bounds), PbfExtractReader.ReadLarge(path, bounds, directory));
                Clean(directory);
            }));

        yield return ("large PBF unsorted IDs survive multiple merge passes and mapped windows", () => InDirectory(directory =>
        {
            var path = Write(directory, Fixture());
            var phases = new HashSet<string>();
            var result = PbfExtractReader.ReadLargeForTests(path, Interior(), directory, TinyRuns(), progress: new InlineProgress(p => { if (p.Phase != "pbf-index") throw new Exception("Unexpected index progress phase"); phases.Add(p.Source!); }));
            Equal(PbfExtractReader.Read(path, Interior()), result);
            if (!phases.Contains("pbf-merge-nodes") || !phases.Contains("pbf-merge-ways")) throw new Exception("External merge was not exercised");
            Clean(directory);
        }));

        yield return ("large PBF sparse buckets preserve boundary and missing node geometry", () => InDirectory(directory =>
        {
            foreach (var references in new long[][] { [1, 2, 3, 4, 255, 256, 257, 1], [256, 258], [258, 257] })
            {
                var path = Write(directory, Fixture(wayCount: 1, wayReferences: references));
                foreach (var window in new[] { 1, 2, 3, 8 })
                    Equal(PbfExtractReader.Read(path, Interior()), PbfExtractReader.ReadLargeForTests(path, Interior(), directory,
                        TinyRuns() with { WindowRecords = window }));
                Clean(directory);
            }
        }));

        yield return ("large PBF mapped sparse lookup handles empty singleton fences tails and 64-bit IDs", () => InDirectory(directory =>
        {
            foreach (var count in new[] { 0, 1, 2, 3, 4, 5, 17, 257 })
            foreach (var window in new[] { 1, 2, 3, 8 })
            {
                var ids = Enumerable.Range(0, count).Select(i => i == count - 1 ? long.MaxValue : 1L + i * 3_000_000_007L).ToArray();
                var path = WriteIndex(directory, ids);
                using (var index = new PbfExtractReader.MappedIndex(path, count, window, default))
                {
                    for (var i = count - 1; i >= 0; i--) CheckRecord(index, ids[i], i);
                    foreach (var id in ids.Where(id => id < long.MaxValue).Select(id => id + 1).Concat(new[] { long.MinValue, -1L, 0L, long.MaxValue - 1 }))
                        if (index.TryGet(id, out var missing) || missing != default) throw new Exception("Missing index key returned a record");
                    if (count == 0 && index.TryGet(long.MaxValue, out _)) throw new Exception("Empty index returned a record");
                }
                File.Delete(path);
            }
            Clean(directory);
        }));

        yield return ("large PBF sparse lookup maps only its bucket and retains at most eight windows", () => InDirectory(directory =>
        {
            var ids = Enumerable.Range(0, 1025).Select(i => 10L + i * 3).ToArray();
            var path = WriteIndex(directory, ids);
            using (var index = new PbfExtractReader.MappedIndex(path, ids.Length, 17, default))
            {
                CheckRecord(index, ids[^1], ids.Length - 1);
                if (MappedField<System.Collections.ICollection>(index, "views").Count != 1)
                    throw new Exception("One lookup mapped windows outside its sparse bucket");
                for (var i = 0; i < ids.Length; i += 17) CheckRecord(index, ids[i], i);
                if (MappedField<System.Collections.ICollection>(index, "views").Count > 8)
                    throw new Exception("Mapped window cache grew beyond its bound");
                CheckRecord(index, ids[0], 0);
            }
            File.Delete(path);
            Clean(directory);
        }));

        yield return ("large PBF sparse fences stay bounded with tiny windows and span multiple windows correctly", () => InDirectory(directory =>
        {
            var ids = Enumerable.Range(0, 32771).Select(i => 10L + i * 3).ToArray();
            var path = WriteIndex(directory, ids);
            foreach (var window in new[] { 1, 2, 3 })
            {
                using var index = new PbfExtractReader.MappedIndex(path, ids.Length, window, default);
                var fences = MappedField<long[]>(index, "fenceKeys");
                var stride = MappedField<long>(index, "fenceStride");
                if (fences.Length is < 1 or > 8192 || stride % window != 0)
                    throw new Exception("Sparse fences exceeded their memory bound or lost window alignment");
                for (var i = 0; i < ids.Length; i++)
                {
                    CheckRecord(index, ids[i], i);
                    if (index.TryGet(ids[i] + 1, out _)) throw new Exception("Gap key matched a neighboring bucket");
                }
            }
            File.Delete(path);
            Clean(directory);
        }));

        yield return ("large PBF mapped index enforces complete 24-byte records", () => InDirectory(directory =>
        {
            var path = Path.Combine(directory, "index.bin");
            foreach (var size in new[] { 0, 8, 16, 23, 24, 25, 47, 49 })
            {
                File.WriteAllBytes(path, new byte[size]);
                try { using var index = new PbfExtractReader.MappedIndex(path, 2, 1, default); }
                catch (ArgumentException) { continue; }
                throw new Exception("Malformed index length accepted");
            }
            File.Delete(path);
            Clean(directory);
        }));

        yield return ("large PBF mapped index observes cancellation before fences and lookup", () => InDirectory(directory =>
        {
            foreach (var ids in new long[][] { [], [1, 3, long.MaxValue] })
            {
                var path = WriteIndex(directory, ids);
                using var cancel = new CancellationTokenSource();
                using (var index = new PbfExtractReader.MappedIndex(path, ids.Length, 1, cancel.Token))
                {
                    cancel.Cancel();
                    Cancelled(() => index.TryGet(1, out _));
                    Cancelled(() => { using var other = new PbfExtractReader.MappedIndex(path, ids.Length, 1, cancel.Token); });
                }
                File.Delete(path);
            }
            Clean(directory);
        }));

        foreach (var duplicate in new[] { "node", "way" })
            yield return ($"large PBF rejects {duplicate} duplicates across runs", () => InDirectory(directory =>
            {
                var path = Write(directory, Fixture(duplicate));
                foreach (var settings in new[] { TinyRuns(), new PbfExtractReader.LargeReadOptions() })
                {
                    Reject(() => PbfExtractReader.ReadLargeForTests(path, Interior(), directory, settings), "Duplicate PBF " + duplicate + " ID");
                    Clean(directory);
                }
            }));

        yield return ("large PBF empty indexes and completely missing node index retain legacy omissions", () => InDirectory(directory =>
        {
            foreach (var ways in new[] { 0, 1 })
            {
                var path = Write(directory, Fixture(wayCount: ways, nodeCount: 0));
                Equal(PbfExtractReader.Read(path, Interior()), PbfExtractReader.ReadLargeForTests(path, Interior(), directory, TinyRuns()));
                Clean(directory);
            }
        }));

        yield return ("large PBF truncation and invalid protobuf clean private scratch only", () => InDirectory(directory =>
        {
            var fixture = UpstreamTests.PbfFixture(true);
            foreach (var bytes in new[] { Array.Empty<byte>(), new byte[] { 0, 0, 0, 32, 1 }, fixture[..^1], fixture[..^7], Fixture()[..^1] })
            {
                Reject(() => PbfExtractReader.ReadLargeForTests(Write(directory, bytes), Interior(), directory, TinyRuns()));
                Clean(directory);
            }
        }));

        foreach (var phase in new[] { "before", "pbf-index-nodes", "pbf-merge-nodes", "pbf-scan-ways", "pbf-merge-ways", "pbf-geometry", "pbf-output" })
            yield return ($"large PBF cancellation cleans scratch at {phase}", () => InDirectory(directory =>
            {
                var path = Write(directory, Fixture());
                using var cancel = new CancellationTokenSource();
                if (phase == "before") cancel.Cancel();
                var reports = 0;
                var threshold = phase is "pbf-index-nodes" or "pbf-scan-ways" or "pbf-merge-nodes" or "pbf-merge-ways" ? 3 : 1;
                try
                {
                    PbfExtractReader.ReadLargeForTests(path, Interior(), directory, TinyRuns(), cancel.Token,
                        new InlineProgress(p => { if (p.Source == phase && ++reports >= threshold) cancel.Cancel(); }));
                }
                catch (OperationCanceledException) { Clean(directory); return; }
                throw new Exception("Cancellation did not stop the reader at " + phase);
            }));

        yield return ("large PBF independent finite index/geometry/retained budgets", () => InDirectory(directory =>
        {
            var path = Write(directory, Fixture());
            foreach (var limits in new[] { new PbfExtractReader.Budgets(Nodes: 2), new PbfExtractReader.Budgets(Ways: 2), new PbfExtractReader.Budgets(References: 2), new PbfExtractReader.Budgets(GeometryWork: 2) })
            {
                Reject(() => PbfExtractReader.ReadLargeForTests(path, Interior(), directory, TinyRuns() with { Limits = limits }));
                Clean(directory);
            }
            Reject(() => PbfExtractReader.ReadLargeForTests(path, Interior(), directory, TinyRuns() with { RetainedBytes = 128 }));
            Clean(directory);
        }));

        yield return ("large PBF relation streaming discards unrelated relations within retained budget", () => InDirectory(directory =>
        {
            var bytes = RelationFixture();
            var path = Write(directory, bytes);
            var result = PbfExtractReader.ReadLargeForTests(path, Interior(), directory, TinyRuns() with { RetainedBytes = 16 * 1024 });
            Equal(PbfExtractReader.Read(path, Interior()), result);
            if (result.OmittedRelations != 1 || result.OmittedWays != 0) throw new Exception("Incomplete relation omission counts changed");
            using var json = System.Text.Json.JsonDocument.Parse(result.Data);
            var relations = json.RootElement.GetProperty("elements");
            if (relations.GetArrayLength() != 2 || relations[0].GetProperty("id").GetInt64() != 3000 || relations[1].GetProperty("id").GetInt64() != 3001)
                throw new Exception("Unrelated relations leaked into output or relevant relations were dropped");
            var crossing = relations[0].GetProperty("members");
            if (crossing.GetArrayLength() != 2 || crossing[0].GetProperty("role").GetString() != "outer" || crossing[1].GetProperty("role").GetString() != "inner")
                throw new Exception("Intersecting relation lost its complete outer/inner members");
            CheckRelationRing(crossing[0], 1001, [(32.051,54.7825),(32.054,54.7825),(32.054,54.7835),(32.051,54.7835),(32.051,54.7825)]);
            CheckRelationRing(crossing[1], 1002, [(32.0515,54.7827),(32.0525,54.7827),(32.0525,54.783),(32.0515,54.783),(32.0515,54.7827)]);
            var containing = relations[1].GetProperty("members");
            if (containing.GetArrayLength() != 1) throw new Exception("Enclosing polygon lost a member");
            CheckRelationRing(containing[0], 1003, [(32.049,54.781),(32.053,54.781),(32.053,54.785),(32.049,54.785),(32.049,54.781)]);
            if (!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new Exception("Relation extraction changed the complete source PBF");
            Clean(directory);
        }));

        yield return ("large PBF relation streaming counts selected relations instead of the entire region", () => InDirectory(directory =>
        {
            var path=Write(directory,RelationFixture(unrelatedCount:50_001));
            var result=PbfExtractReader.ReadLargeForTests(path,Interior(),directory,TinyRuns() with{RetainedBytes=16*1024});
            using var json=System.Text.Json.JsonDocument.Parse(result.Data);
            if(json.RootElement.GetProperty("elements").GetArrayLength()!=2||result.OmittedRelations!=1)throw new Exception("Regional relation count discarded selected geometry");
            Clean(directory);
            path=Write(directory,RelationFixture(unrelatedCount:50_001,allSelected:true));
            Reject(()=>PbfExtractReader.ReadLargeForTests(path,Interior(),directory,TinyRuns() with{RetainedBytes=128L*1024*1024},maxOutputBytes:64_000_000),"50");
            Clean(directory);
        }));

        yield return ("large PBF relation streaming still bounds retained selected relations", () => InDirectory(directory =>
        {
            var bytes = RelationFixture(allSelected: true);
            var path = Write(directory, bytes);
            Reject(() => PbfExtractReader.ReadLargeForTests(path, Interior(), directory, TinyRuns() with { RetainedBytes = 16 * 1024 }), "limit");
            Clean(directory);
            Equal(PbfExtractReader.Read(path, Interior()), PbfExtractReader.ReadLargeForTests(path, Interior(), directory, TinyRuns() with { RetainedBytes = 128 * 1024 }));
            if (!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new Exception("Retained-memory rejection changed the complete source PBF");
            Clean(directory);
        }));

        yield return ("large PBF relation streaming bounds one transient unrelated relation", () => InDirectory(directory =>
        {
            var path = Write(directory, RelationFixture(unrelatedCount: 1, unrelatedName: new string('a', 9000)));
            Reject(() => PbfExtractReader.ReadLargeForTests(path, Interior(), directory, TinyRuns() with { RetainedBytes = 16 * 1024 }), "limit");
            Clean(directory);
            Equal(PbfExtractReader.Read(path, Interior()), PbfExtractReader.ReadLargeForTests(path, Interior(), directory, TinyRuns() with { RetainedBytes = 128 * 1024 }));
            Clean(directory);
        }));

        foreach (var phase in new[] { "pbf-scan-relations", "pbf-output" })
            yield return ($"large PBF relation streaming cancellation cleans scratch at {phase}", () => InDirectory(directory =>
            {
                var bytes = RelationFixture();
                var path = Write(directory, bytes);
                using var cancel = new CancellationTokenSource();
                var reports = 0;
                Cancelled(() => PbfExtractReader.ReadLargeForTests(path, Interior(), directory, TinyRuns() with { RetainedBytes = 16 * 1024 }, cancel.Token,
                    new InlineProgress(p => {
                        if (p.Source != phase) return;
                        // Header and two relation blocks: cancel after processing unrelated relations, not at phase entry.
                        if (phase == "pbf-scan-relations" && (p.BytesReceived is null || ++reports < 3)) return;
                        cancel.Cancel();
                    })));
                if (!cancel.IsCancellationRequested || !File.ReadAllBytes(path).SequenceEqual(bytes)) throw new Exception("Relation cancellation missed its phase or changed the source PBF");
                Clean(directory);
                Equal(PbfExtractReader.Read(path, Interior()), PbfExtractReader.ReadLargeForTests(path, Interior(), directory, TinyRuns() with { RetainedBytes = 16 * 1024 }));
                Clean(directory);
            }));

        yield return ("large PBF scratch budget includes simultaneous merge inputs and output", () => InDirectory(directory =>
        {
            var path = Write(directory, Fixture());
            var reachedWays = false;
            // Final indexes + refs = 11,328 bytes; way merge peak = 14,424 bytes.
            Reject(() => PbfExtractReader.ReadLargeForTests(path, Interior(), directory, TinyRuns() with { ScratchBytes = 13_000 },
                progress: new InlineProgress(p => { if (p.Source == "pbf-merge-ways") reachedWays = true; })), "scratch disk budget");
            if (!reachedWays) throw new Exception("Disk limit did not exercise the way merge peak");
            Clean(directory);
            Reject(() => PbfExtractReader.ReadLargeForTests(path, Interior(), directory, TinyRuns() with { ScratchBytes = 128 }), "scratch disk budget");
            Clean(directory);
        }));

        yield return ("large PBF JSON byte cap and world allowance are explicit", () => InDirectory(directory =>
        {
            var path = Write(directory, Fixture());
            Reject(() => PbfExtractReader.ReadLarge(path, Interior(), directory, maxOutputBytes: 128));
            Equal(PbfExtractReader.Read(path, Interior()), PbfExtractReader.ReadLarge(path, Interior(), directory, maxOutputBytes: 64_000_000));
            Reject(() => PbfExtractReader.ReadLarge(path, Interior(), directory, maxOutputBytes: 64_000_001));
            Clean(directory);
        }));

        yield return ("large PBF world allowance really accepts JSON beyond 16 MB", () => InDirectory(directory =>
        {
            var path = Write(directory, Fixture(wayCount: 600, tagValue: new string('\u0416', 5000)));
            Reject(() => PbfExtractReader.ReadLarge(path, Interior(), directory));
            var result = PbfExtractReader.ReadLarge(path, Interior(), directory, maxOutputBytes: 64_000_000);
            using var json = System.Text.Json.JsonDocument.Parse(result.Data);
            if (Encoding.UTF8.GetByteCount(result.Data) <= 16_000_000 || json.RootElement.GetProperty("elements").GetArrayLength() != 600)
                throw new Exception("World output allowance was ignored or geometry was truncated");
            Clean(directory);
        }));

        yield return ("large PBF world aggregate budgets scale while single-map limits remain", () => InDirectory(directory =>
        {
            // Each individual way is below MaxPoints; only the output aggregate exceeds it.
            var pointPath=Write(directory,Fixture(wayCount:501,referencesPerWay:500));
            Reject(() => PbfExtractReader.ReadLarge(pointPath,Interior(),directory));
            var pointResult=PbfExtractReader.ReadLarge(pointPath,Interior(),directory,maxOutputBytes:64_000_000);
            using(var json=System.Text.Json.JsonDocument.Parse(pointResult.Data))if(json.RootElement.GetProperty("elements").GetArrayLength()!=501)throw new Exception("World allowance dropped ways");
            Clean(directory);
            // Individually <30k selected ways and <30k selected tagged nodes, together >30k.
            var featurePath=Write(directory,Fixture(wayCount:29_800,taggedNodes:true));
            Reject(() => PbfExtractReader.ReadLarge(featurePath,Interior(),directory));
            var featureResult=PbfExtractReader.ReadLarge(featurePath,Interior(),directory,maxOutputBytes:64_000_000);
            using(var json=System.Text.Json.JsonDocument.Parse(featureResult.Data))if(json.RootElement.GetProperty("elements").GetArrayLength()!=30_057)throw new Exception("World allowance dropped tagged nodes or ways");
            Clean(directory);
            // The world allowance is bounded too: a million output points, not unlimited geometry.
            Reject(()=>PbfExtractReader.ReadLarge(Write(directory,Fixture(wayCount:2001,referencesPerWay:500)),Interior(),directory,maxOutputBytes:64_000_000));
            Clean(directory);
        }));
        yield return ("large PBF world node selection accepts more than single-map node budget",()=>InDirectory(directory=>
        {
            var path=Write(directory,Fixture(wayCount:0,nodeCount:130_001,taggedNodes:true));
            Reject(()=>PbfExtractReader.ReadLarge(path,Interior(),directory));
            var result=PbfExtractReader.ReadLarge(path,Interior(),directory,maxOutputBytes:64_000_000);
            using var json=System.Text.Json.JsonDocument.Parse(result.Data);
            if(json.RootElement.GetProperty("elements").GetArrayLength()!=130_001)throw new Exception("Tagged nodes were omitted rather than imported");
            Clean(directory);
        }));

        yield return ("large PBF accepts valid input above legacy 64 MiB gate", () => InDirectory(directory =>
        {
            var path = Write(directory, UpstreamTests.PbfFixture(false, false, "road"));
            // Legal unknown optional blocks exercise the file gate without a huge object graph.
            var padding = Frame("optional-test-padding", new byte[1024 * 1024]);
            using (var stream = new FileStream(path, FileMode.Append))
                for (var i = 0; i < 65; i++) stream.Write(padding);
            Reject(() => PbfExtractReader.Read(path, Interior()));
            var expectedPath = Path.Combine(directory, "small.pbf");
            File.WriteAllBytes(expectedPath, UpstreamTests.PbfFixture(false, false, "road"));
            Equal(PbfExtractReader.Read(expectedPath, Interior()), PbfExtractReader.ReadLarge(path, Interior(), directory));
            File.Delete(expectedPath);
            Clean(directory);
        }));
    }

    private static PbfExtractReader.LargeReadOptions TinyRuns() => new(ChunkRecords: 3, MergeFanIn: 2, WindowRecords: 2);
    private static GeoBounds Interior() => new() { South = 54.782, North = 54.784, West = 32.050, East = 32.052 };
    private static GeoBounds Evidence() => new() { South = 54.777, North = 54.779, West = 32.046, East = 32.049 };
    private static void Equal(PbfExtractReader.Result expected, PbfExtractReader.Result actual)
    {
        if (expected != actual) throw new Exception("Large PBF changed complete geometry, order, metadata or omission counts");
    }
    private static string WriteIndex(string directory, long[] ids)
    {
        var path = Path.Combine(directory, "index.bin");
        using var writer = new BinaryWriter(File.Create(path));
        for (var i = 0; i < ids.Length; i++) { writer.Write(ids[i]); writer.Write(i * 24L); writer.Write(-1L - i); }
        return path;
    }
    private static void CheckRecord(PbfExtractReader.MappedIndex index, long id, int position)
    {
        if (!index.TryGet(id, out var record) || record != new PbfExtractReader.IndexRecord(id, position * 24L, -1L - position))
            throw new Exception("Sparse lookup lost a key or crossed a 24-byte record boundary");
    }
    // Inspect resource bounds without counters or instrumentation on the production lookup path.
    private static T MappedField<T>(PbfExtractReader.MappedIndex index, string name) =>
        (T)(typeof(PbfExtractReader.MappedIndex).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(index) ?? throw new Exception("Missing mapped index state: " + name));
    private static void Cancelled(Action run)
    {
        try { run(); }
        catch (OperationCanceledException) { return; }
        throw new Exception("Cancelled mapped index operation continued");
    }
    private static void Reject(Action run, string? message = null)
    {
        try { run(); }
        catch (ArgumentException e) { if (message is null || e.ToString().Contains(message, StringComparison.Ordinal)) return; throw; }
        throw new Exception("Invalid or over-budget PBF accepted");
    }
    private static void InDirectory(Action<string> run)
    {
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "work", "large-pbf-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "keep.txt"), "owned by caller");
        try { run(directory); }
        finally
        {
            // Delete only known test-owned direct files; never traverse reader scratch on failure.
            foreach (var name in new[] { "fixture.pbf", "keep.txt", "small.pbf", "index.bin" }) File.Delete(Path.Combine(directory, name));
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
    }
    private static string Write(string directory, byte[] bytes)
    {
        var path = Path.Combine(directory, "fixture.pbf"); File.WriteAllBytes(path, bytes); return path;
    }
    private static void Clean(string directory)
    {
        if (File.ReadAllText(Path.Combine(directory, "keep.txt")) != "owned by caller" ||
            Directory.EnumerateFileSystemEntries(directory).Any(p => Path.GetFileName(p) is not ("fixture.pbf" or "keep.txt")))
            throw new Exception("Scratch leaked or a caller-owned file was touched");
    }
    private sealed class InlineProgress(Action<OsmProgress> report) : IProgress<OsmProgress>
    {
        public void Report(OsmProgress value) => report(value);
    }

    private static byte[] Fixture(string? duplicate = null, int wayCount = 129, string? tagValue = null, int referencesPerWay = 2, bool taggedNodes = false, int nodeCount = 257, long[]? wayReferences = null)
    {
        var nodes = Enumerable.Range(1, nodeCount).Reverse().Select(id => Bytes(1, Join(Int(1, id * 2), Int(8, 547830000L * 2), Int(9, 320510000L * 2), taggedNodes ? Join(Bytes(2, [5]), Bytes(3, [6])) : []))).ToList();
        if (duplicate == "node") nodes.Add(nodes[0]);
        var refs = Enumerable.Range(0, referencesPerWay).Select(i => (byte)(i == 0 || i % 2 != 0 ? 2 : 1)).ToArray();
        if (wayReferences is not null)
        {
            long previous = 0;
            refs = Join(wayReferences.Select(id => { var delta = id - previous; previous = id; return Var((ulong)((delta << 1) ^ (delta >> 63))); }).ToArray());
        }
        var ways = Enumerable.Range(1000, wayCount).Reverse().Select(id => Bytes(3, Join(Int(1, id), Bytes(2, tagValue is null ? [1] : [1, 3]), Bytes(3, tagValue is null ? [2] : [2, 4]), Bytes(8, refs)))).ToList();
        if (duplicate == "way") ways.Add(ways[0]);
        var table = Join(new[] { "", "highway", "residential", "name", tagValue ?? "", "amenity", "school" }.Select(s => Bytes(1, Encoding.UTF8.GetBytes(s))).ToArray());
        return Join(Frame("OSMHeader", Bytes(4, Encoding.UTF8.GetBytes("OsmSchema-V0.6"))),
            Frame("OSMData", Join(Bytes(1, table), Bytes(2, Join(nodes.Concat(ways).ToArray())))));
    }
    private static void CheckRelationRing(System.Text.Json.JsonElement member, long wayId, (double Lon, double Lat)[] expected)
    {
        var points = member.GetProperty("geometry");
        if (member.GetProperty("ref").GetInt64() != wayId || points.GetArrayLength() != expected.Length)
            throw new Exception("Relation member was replaced or its geometry truncated");
        for (var i = 0; i < expected.Length; i++)
            if (Math.Abs(points[i].GetProperty("lon").GetDouble() - expected[i].Lon) > 1e-7 || Math.Abs(points[i].GetProperty("lat").GetDouble() - expected[i].Lat) > 1e-7)
                throw new Exception("Relation member lost original coordinates or closure");
    }
    private static byte[] RelationFixture(int unrelatedCount = 80, bool allSelected = false, string unrelatedName = "outside-region")
    {
        byte[] Deltas(IEnumerable<long> values)
        {
            long previous = 0;
            return Join(values.Select(value => { var delta = value - previous; previous = value; return Var((ulong)((delta << 1) ^ (delta >> 63))); }).ToArray());
        }
        var strings = new[] { "", "type", "multipolygon", "landuse", "forest", "outer", "inner", "name", unrelatedName };
        var table = Join(strings.Select(s => Bytes(1, Encoding.UTF8.GetBytes(s))).ToArray());
        var nodes = new List<byte[]>();
        var ways = new List<byte[]>();
        void Ring(long wayId, params (double Lon, double Lat)[] points)
        {
            var start = nodes.Count + 1L;
            foreach (var (lon, lat) in points)
            {
                var id = nodes.Count + 1L;
                nodes.Add(Bytes(1, Join(Int(1, id * 2), Int(8, (long)Math.Round(lat * 10_000_000) * 2), Int(9, (long)Math.Round(lon * 10_000_000) * 2))));
            }
            // Member ways are deliberately untagged: complete relation geometry must come from the disk index.
            ways.Add(Bytes(3, Join(Int(1, wayId), Bytes(8, Deltas(Enumerable.Range(0, points.Length).Select(i => start + i).Append(start))))));
        }
        Ring(1000, (33,55), (33.01,55), (33.01,55.01), (33,55.01));
        Ring(1001, (32.051,54.7825), (32.054,54.7825), (32.054,54.7835), (32.051,54.7835));
        Ring(1002, (32.0515,54.7827), (32.0525,54.7827), (32.0525,54.783), (32.0515,54.783));
        // No vertex is inside Interior(): selection must detect containment, not just in-box vertices.
        Ring(1003, (32.049,54.781), (32.053,54.781), (32.053,54.785), (32.049,54.785));
        byte[] Relation(long id, long[] members, bool named = false) => Bytes(4, Join(Int(1, id), Bytes(2, named ? [1,3,7] : [1,3]),
            Bytes(3, named ? [2,4,8] : [2,4]), Bytes(8, members.Select((_, i) => (byte)(i == 0 ? 5 : 6)).ToArray()),
            Bytes(9, Deltas(members)), Bytes(10, members.Select(_ => (byte)1).ToArray())));
        var relations = Enumerable.Range(2000, unrelatedCount).Select(id => Relation(id, [allSelected ? 1001 : 1000], true))
            .Concat(new[] { Relation(3000, [1001,1002]), Relation(3001, [1003]), Relation(3002, [9999]) }).ToArray();
        var blocks = new List<byte[]> { Frame("OSMHeader", Join(Bytes(4, Encoding.UTF8.GetBytes("OsmSchema-V0.6")), Int(32, 1700000000))) };
        // Relations precede their member ways on disk, so lookups require a completed way index.
        foreach (var batch in relations.Chunk(8)) blocks.Add(Frame("OSMData", Join(Bytes(1, table), Bytes(2, Join(batch)))));
        blocks.Add(Frame("OSMData", Join(Bytes(1, table), Bytes(2, Join(nodes.Concat(ways).ToArray())))));
        return Join(blocks.ToArray());
    }
    private static byte[] Frame(string type, byte[] raw)
    {
        var blob = Bytes(1, raw); var header = Join(Bytes(1, Encoding.UTF8.GetBytes(type)), Int(3, blob.Length));
        var prefix = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(prefix, header.Length); return Join(prefix, header, blob);
    }
    private static byte[] Int(int field, long value) => Join(Var((ulong)field << 3), Var((ulong)value));
    private static byte[] Bytes(int field, byte[] value) => Join(Var(((ulong)field << 3) | 2), Var((ulong)value.Length), value);
    private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static byte[] Var(ulong value)
    {
        var bytes = new List<byte>(); do { var b = (byte)(value & 127); value >>= 7; bytes.Add((byte)(b | (value > 0 ? 128 : 0))); } while (value > 0); return bytes.ToArray();
    }
}
