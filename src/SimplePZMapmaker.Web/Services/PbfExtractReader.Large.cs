using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using SimplePZMapmaker.Core;

[assembly: InternalsVisibleTo("SimplePZMapmaker.Tests")]

namespace SimplePZMapmaker.Web.Services;

public static partial class PbfExtractReader
{
    // Regional, not planet-wide: 8 GiB input, 16 GiB LIVE scratch including merge inputs/outputs.
    // The legacy 64 MiB Read API and its Budgets retain their original ceilings.
    // Nodes/ways/references are disk-backed. Relations are scanned after the way index
    // is complete; only intersecting relations are retained. Retained relations still
    // obey the 50,000/16m-tag-character limits. Selected nodes/ways/relations plus the current
    // transient relation share a conservative 128 MiB retention allowance.
    // A single relation may load at most 1m references for stitching.
    // Larger regional relations fail explicitly rather than yielding partial geometry.
    // Buffers: two 3 MiB sort chunks, <=32 merge readers, <=8 x 3 MiB mapped windows/index,
    // plus <=8192 sparse fence IDs (64 KiB) per index;
    // protobuf block decoding is still bounded by the existing 32 MiB block ceiling.
    internal sealed record LargeReadOptions(
        int ChunkRecords = 131072, int MergeFanIn = 32, int WindowRecords = 131072,
        Budgets? Limits = null, long RetainedBytes = 128L * 1024 * 1024,
        long ScratchBytes = 16L * 1024 * 1024 * 1024);

    public static Result ReadLarge(string path, GeoBounds bounds, string workingDirectory,
        CancellationToken token = default, IProgress<OsmProgress>? progress = null, int maxOutputBytes = 16_000_000)
        => ReadLargeForTests(path, bounds, workingDirectory, new(), token, progress, maxOutputBytes);

    // Per-call settings avoid process-wide test switches or concurrent-reader interference.
    internal static Result ReadLargeForTests(string path, GeoBounds bounds, string workingDirectory,
        LargeReadOptions options, CancellationToken token = default,
        IProgress<OsmProgress>? progress = null, int maxOutputBytes = 16_000_000)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            WorldLayoutLimits.ValidateSourceBounds(bounds);
            LimitLong(new FileInfo(path).Length, 8L * 1024 * 1024 * 1024);
            Limit(maxOutputBytes, 64_000_000, 1);
            Limit(options.ChunkRecords, 131072, 1);
            Limit(options.MergeFanIn, 32, 2);
            Limit(options.WindowRecords, 131072, 1);
            LimitLong(options.RetainedBytes, 128L * 1024 * 1024);
            LimitLong(options.ScratchBytes, 16L * 1024 * 1024 * 1024);
            var limits = options.Limits ?? new Budgets(400_000_000, 60_000_000, 1_000_000_000, 2_000_000_000);
            Limit(limits.Nodes, 400_000_000, 1);
            Limit(limits.Ways, 60_000_000, 1);
            Limit(limits.References, 1_000_000_000, 1);
            Limit(limits.GeometryWork, 2_000_000_000, 1);
            using var scratch = new ScratchDirectory(workingDirectory, options.ScratchBytes);
            using var nodes = new DiskNodeBuilder(scratch, options, limits.Nodes, token, progress);
            using var ways = new DiskWayStore(scratch, options, token, progress);
            long retained = 0;
            void Reserve(long bytes) => LimitLong(retained = checked(retained + bytes), options.RetainedBytes);
            return ReadCore(path, bounds, token, limits, nodes, ways, maxOutputBytes, Reserve, progress);
        }
        catch (IOException e) when (e is not EndOfStreamException)
        {
            token.ThrowIfCancellationRequested();
            throw new ProcessingLimitException("Could not build the regional index. Check scratch disk space and file access.", e);
        }
        catch (Exception e) when (e is EndOfStreamException or InvalidDataException or OverflowException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            // Array.Sort wraps comparer cancellation in InvalidOperationException.
            token.ThrowIfCancellationRequested();
            throw new ArgumentException("Invalid or truncated regional PBF extract, or insufficient scratch disk space.", e);
        }
    }

    private interface INodeIndex : IDisposable { GeoNode Get(long id); }
    private interface INodeBuilder : IDisposable
    {
        void Add(long id, double lat, double lon);
        INodeIndex Build(CancellationToken token);
    }
    private interface IWayStore : IDisposable
    {
        long Count { get; }
        void Add(long id, long[] references);
        void Complete(CancellationToken token);
        bool ContainsKey(long id);
        long[] this[long id] { get; }
    }
    private sealed class MemoryWayStore : IWayStore
    {
        private readonly Dictionary<long, long[]> values = [];
        public long Count => values.Count;
        public void Add(long id, long[] references)
        {
            if (!values.TryAdd(id, references)) throw Fail("Duplicate PBF way ID.");
        }
        public void Complete(CancellationToken token) { token.ThrowIfCancellationRequested(); }
        public bool ContainsKey(long id) => values.ContainsKey(id);
        public long[] this[long id] => values[id];
        public void Dispose() { }
    }

    private static void IndexProgress(IProgress<OsmProgress>? progress, string stage, CancellationToken token)
    {
        progress?.Report(new("pbf-index", 0, 0, stage));
        token.ThrowIfCancellationRequested();
    }

    private sealed class ScratchDirectory : IDisposable
    {
        private const long FreeReserve = 128L * 1024 * 1024;
        private readonly string parent, directory;
        private readonly long budget;
        private long allocated, uncheckedBytes;
        public ScratchDirectory(string workingDirectory, long budget)
        {
            parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory));
            CheckAncestors(parent);
            Directory.CreateDirectory(parent);
            CheckAncestors(parent);
            this.budget = budget;
            directory = Path.Combine(parent, ".pbf-index-" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(directory) || File.Exists(directory)) throw Fail("Scratch path already exists.");
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
            else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Verify();
        }
        private static void CheckAncestors(string path)
        {
            for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw Fail("PBF scratch must not traverse a symbolic link or junction.");
        }
        private void Verify()
        {
            if (!string.Equals(Path.GetDirectoryName(directory), parent, StringComparison.OrdinalIgnoreCase))
                throw Fail("PBF scratch escaped its working directory.");
            CheckAncestors(directory);
        }
        public string FilePath(string name)
        {
            if (Path.GetFileName(name) != name || name.Contains('/') || name.Contains('\\')) throw Fail("Invalid scratch filename.");
            return Path.Combine(directory, name);
        }
        public void Reserve(long bytes)
        {
            if (bytes < 0 || bytes > budget - allocated) throw new ProcessingLimitException("The PBF index exceeds its 16 GiB scratch disk budget.");
            if (allocated == 0 || bytes + uncheckedBytes >= 32L * 1024 * 1024)
            {
                var drive = new DriveInfo(Path.GetPathRoot(directory)!);
                if (drive.AvailableFreeSpace < bytes + FreeReserve) throw new ProcessingLimitException("Insufficient free disk space for the PBF index (128 MiB reserve required).");
                uncheckedBytes = 0;
            }
            else uncheckedBytes += bytes;
            allocated += bytes;
        }
        public void Delete(string path)
        {
            Verify();
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), directory, StringComparison.OrdinalIgnoreCase))
                throw Fail("PBF scratch deletion escaped its private directory.");
            var info = new FileInfo(path);
            if (!info.Exists) return;
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw Fail("Unexpected symbolic link in PBF scratch.");
            var bytes = info.Length;
            File.Delete(path);
            allocated -= bytes;
        }
        public void Dispose()
        {
            Verify();
            // No recursive deletion. Unexpected subdirectories/links are never followed.
            foreach (var path in Directory.EnumerateFiles(directory)) Delete(path);
            Directory.Delete(directory, false);
        }
    }

    // Identical 24-byte little-endian records for nodes (ID, lat bits, lon bits) and
    // ways (ID, byte offset in refs spool, reference count). No object graph per region.
    internal readonly record struct IndexRecord(long Id, long A, long B);
    private sealed class ExternalIndexBuilder(ScratchDirectory scratch, LargeReadOptions options,
        string kind, CancellationToken token, IProgress<OsmProgress>? progress) : IDisposable
    {
        private IndexRecord[] buffer = new IndexRecord[options.ChunkRecords];
        private int buffered, runs;
        public long Count { get; private set; }
        private string RunPath(int level, int run) => scratch.FilePath($"{kind}-{level}-{run}.idx");
        public void Add(IndexRecord record)
        {
            if ((Count & 1023) == 0) token.ThrowIfCancellationRequested();
            buffer[buffered++] = record;
            Count++;
            if (buffered == buffer.Length) Flush();
        }
        private void Flush()
        {
            if (buffered == 0) return;
            token.ThrowIfCancellationRequested();
            var comparisons = 0;
            Array.Sort(buffer, 0, buffered, Comparer<IndexRecord>.Create((a, b) =>
            {
                if ((++comparisons & 4095) == 0) token.ThrowIfCancellationRequested();
                return a.Id.CompareTo(b.Id);
            }));
            scratch.Reserve(buffered * 24L);
            using (var writer = new BinaryWriter(new FileStream(RunPath(0, runs++), FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536)))
                for (var i = 0; i < buffered; i++)
                {
                    if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                    if (i > 0 && buffer[i - 1].Id == buffer[i].Id) throw Fail($"Duplicate PBF {kind} ID.");
                    Write(writer, buffer[i]);
                }
            buffered = 0;
        }
        public MappedIndex Build()
        {
            Flush();
            buffer = [];
            var level = 0;
            while (runs > 1)
            {
                var nextRuns = 0;
                for (var start = 0; start < runs; start += options.MergeFanIn)
                {
                    IndexProgress(progress, "pbf-merge-" + kind + "s", token);
                    var count = Math.Min(options.MergeFanIn, runs - start);
                    Merge(level, start, count, RunPath(level + 1, nextRuns++));
                }
                level++;
                runs = nextRuns;
            }
            IndexProgress(progress, "pbf-merge-" + kind + "s", token);
            var path = RunPath(level, 0);
            if (runs == 0) using (File.Create(path)) { }
            return new MappedIndex(path, Count, options.WindowRecords, token);
        }
        private void Merge(int level, int start, int count, string output)
        {
            var readers = new BinaryReader?[count];
            try
            {
                long size = 0;
                var queue = new PriorityQueue<(IndexRecord Record, int Reader), long>();
                for (var i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var path = RunPath(level, start + i);
                    size = checked(size + new FileInfo(path).Length);
                    var reader = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536));
                    readers[i] = reader;
                    if (reader.BaseStream.Length > 0) { var record = Read(reader); queue.Enqueue((record, i), record.Id); }
                }
                // Reserve the entire output while all input runs still exist: peak disk, not net growth.
                scratch.Reserve(size);
                using var writer = new BinaryWriter(new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536));
                long previous = 0, written = 0;
                while (queue.TryDequeue(out var item, out _))
                {
                    if ((written & 1023) == 0) token.ThrowIfCancellationRequested();
                    if (written > 0 && previous == item.Record.Id) throw Fail($"Duplicate PBF {kind} ID.");
                    Write(writer, item.Record);
                    previous = item.Record.Id; written++;
                    var reader = readers[item.Reader]!;
                    if (reader.BaseStream.Position < reader.BaseStream.Length)
                    { var record = Read(reader); queue.Enqueue((record, item.Reader), record.Id); }
                }
            }
            finally { foreach (var reader in readers) reader?.Dispose(); }
            for (var i = 0; i < count; i++) scratch.Delete(RunPath(level, start + i));
        }
        private static IndexRecord Read(BinaryReader reader) => new(reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64());
        private static void Write(BinaryWriter writer, IndexRecord record) { writer.Write(record.Id); writer.Write(record.A); writer.Write(record.B); }
        public void Dispose() { buffer = []; }
    }

    internal sealed class MappedIndex : IDisposable
    {
        private const int MaxFenceKeys = 8192;
        private readonly MemoryMappedFile? mapping;
        private readonly long count;
        private readonly int windowRecords;
        private readonly long fenceStride;
        private readonly long[] fenceKeys;
        private readonly CancellationToken token;
        private readonly List<(long Start, MemoryMappedViewAccessor View, long Used)> views = [];
        private long clock;
        public MappedIndex(string path, long count, int windowRecords, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (count < 0 || windowRecords <= 0) throw Fail("Invalid PBF disk index bounds.");
            if (new FileInfo(path).Length != checked(count * 24)) throw Fail("Truncated PBF disk index.");
            this.count = count; this.windowRecords = windowRecords; this.token = token;
            // One bucket per mapped window by default (3052 buckets for 400m nodes).
            // Tiny test windows may need multiple windows per bucket to keep fences bounded.
            var windows = count / windowRecords + (count % windowRecords == 0 ? 0 : 1);
            var windowsPerFence = Math.Max(1, windows / MaxFenceKeys + (windows % MaxFenceKeys == 0 ? 0 : 1));
            fenceStride = checked(windowsPerFence * windowRecords);
            fenceKeys = new long[(int)(count / fenceStride + (count % fenceStride == 0 ? 0 : 1))];
            if (count > 0)
            {
                // Read only the first ID of each bucket without filling/evicting MMF views.
                // Complete this cancellable step before acquiring the mapping, so a failed
                // constructor leaves no mapped handles behind for scratch cleanup.
                using var reader = new BinaryReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess));
                for (var i = 0; i < fenceKeys.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    reader.BaseStream.Position = checked(i * fenceStride * 24);
                    fenceKeys[i] = reader.ReadInt64();
                }
            }
            token.ThrowIfCancellationRequested();
            if (count > 0) mapping = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        }
        private IndexRecord At(long position)
        {
            var start = position / windowRecords * windowRecords;
            var slot = -1;
            for (var i = 0; i < views.Count; i++) if (views[i].Start == start) { slot = i; break; }
            if (slot < 0)
            {
                if (views.Count == 8)
                {
                    var oldest = 0;
                    for (var i = 1; i < views.Count; i++) if (views[i].Used < views[oldest].Used) oldest = i;
                    views[oldest].View.Dispose(); views.RemoveAt(oldest);
                }
                var view = mapping!.CreateViewAccessor(start * 24, Math.Min(windowRecords, count - start) * 24, MemoryMappedFileAccess.Read);
                views.Add((start, view, ++clock)); slot = views.Count - 1;
            }
            var entry = views[slot]; views[slot] = (entry.Start, entry.View, ++clock);
            var offset = (position - start) * 24;
            return new(entry.View.ReadInt64(offset), entry.View.ReadInt64(offset + 8), entry.View.ReadInt64(offset + 16));
        }
        public bool TryGet(long id, out IndexRecord record)
        {
            token.ThrowIfCancellationRequested();
            // Upper bound in managed memory: the preceding fence owns this key.
            var left = 0; var right = fenceKeys.Length;
            while (left < right)
            {
                var middle = left + (right - left) / 2;
                if (fenceKeys[middle] <= id) left = middle + 1; else right = middle;
            }
            if (left == 0) { record = default; return false; }
            long low = (left - 1) * fenceStride, high = Math.Min(count, low + fenceStride) - 1;
            while (low <= high)
            {
                var middle = low + (high - low) / 2; var value = At(middle);
                if (value.Id == id) { record = value; return true; }
                if (value.Id < id) low = middle + 1; else high = middle - 1;
            }
            record = default; return false;
        }
        public void Dispose() { foreach (var view in views) view.View.Dispose(); views.Clear(); mapping?.Dispose(); }
    }

    private sealed class DiskNodeBuilder(ScratchDirectory scratch, LargeReadOptions options, int maximum,
        CancellationToken token, IProgress<OsmProgress>? progress) : INodeBuilder
    {
        private readonly ExternalIndexBuilder builder = new(scratch, options, "node", token, progress);
        public void Add(long id, double lat, double lon)
        {
            if (id <= 0 || !double.IsFinite(lat) || !double.IsFinite(lon) || lat is < -90 or > 90 || lon is < -180 or > 180)
                throw Fail("Invalid regional extract nodes.");
            LimitLong(builder.Count + 1, maximum);
            builder.Add(new(id, BitConverter.DoubleToInt64Bits(lat), BitConverter.DoubleToInt64Bits(lon)));
        }
        public INodeIndex Build(CancellationToken token) { token.ThrowIfCancellationRequested(); return new DiskNodeIndex(builder.Build()); }
        public void Dispose() => builder.Dispose();
    }
    private sealed class DiskNodeIndex(MappedIndex index) : INodeIndex
    {
        public GeoNode Get(long id) => index.TryGet(id, out var record)
            ? new(id, BitConverter.Int64BitsToDouble(record.A), BitConverter.Int64BitsToDouble(record.B)) : throw new MissingNodesException();
        public void Dispose() => index.Dispose();
    }
    private sealed class DiskWayStore : IWayStore
    {
        private readonly ScratchDirectory scratch;
        private readonly CancellationToken token;
        private readonly string refsPath;
        private readonly ExternalIndexBuilder builder;
        private BinaryWriter? writer;
        private BinaryReader? reader;
        private MappedIndex? index;
        public DiskWayStore(ScratchDirectory scratch, LargeReadOptions options, CancellationToken token, IProgress<OsmProgress>? progress)
        {
            this.scratch = scratch; this.token = token;
            builder = new(scratch, options, "way", token, progress);
            refsPath = scratch.FilePath("way-refs.bin");
            writer = new(new FileStream(refsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536));
        }
        public long Count => builder.Count;
        public void Add(long id, long[] references)
        {
            token.ThrowIfCancellationRequested();
            scratch.Reserve(references.Length * 8L);
            var destination = writer ?? throw new InvalidOperationException("Way index is already sealed.");
            var offset = destination.BaseStream.Position;
            for (var i = 0; i < references.Length; i++)
            { if ((i & 1023) == 0) token.ThrowIfCancellationRequested(); destination.Write(references[i]); }
            builder.Add(new(id, offset, references.Length));
        }
        public void Complete(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            writer!.Dispose(); writer = null;
            index = builder.Build();
            reader = new(new FileStream(refsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536));
        }
        public bool ContainsKey(long id) => index!.TryGet(id, out _);
        public long[] this[long id]
        {
            get
            {
                if (!index!.TryGet(id, out var record)) throw Fail("Missing PBF way ID in disk index.");
                var source = reader ?? throw new InvalidOperationException("Way index has not been sealed.");
                LimitLong(record.B, MaxPoints);
                if (record.A < 0 || record.A > source.BaseStream.Length - record.B * 8) throw Fail("Truncated PBF way reference spool.");
                source.BaseStream.Position = record.A;
                var refs = new long[(int)record.B];
                for (var i = 0; i < refs.Length; i++) { if ((i & 1023) == 0) token.ThrowIfCancellationRequested(); refs[i] = source.ReadInt64(); }
                return refs;
            }
        }
        public void Dispose() { reader?.Dispose(); writer?.Dispose(); index?.Dispose(); builder.Dispose(); }
    }
}
