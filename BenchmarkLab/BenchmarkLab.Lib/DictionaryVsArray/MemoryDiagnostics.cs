using System.Reflection;
using System.Runtime.CompilerServices;

namespace BenchmarkLab.DictionaryVsArray;

public sealed record MemorySample(int N, KeyDistribution Distribution, StructureKind Structure, int Repeat,
    int Replicas, int Capacity, int UnusedCapacity, long EstimatedBucketsBytes, long EstimatedEntriesBytes,
    long EstimatedStructureBytes, long EstimatedTotalBytes, double MeasuredStructureBytes,
    double MeasuredTotalBytes, double BuildAllocatedBytes, double EntityAllocatedBytes);

public static class MemoryDiagnostics
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static object? Field(object o, string name)
    {
        for (Type? t = o.GetType(); t != null; t = t.BaseType)
            if (t.GetField(name, Fields | BindingFlags.DeclaredOnly) is { } f) return f.GetValue(o);
        throw new NotSupportedException($"Runtime field {o.GetType()}.{name} unavailable.");
    }

    // Allocation probes calibrate an additive size MODEL, not a retained-heap measurement.
    private static long Shallow(Type type)
    {
        _ = RuntimeHelpers.GetUninitializedObject(type);
        long before = GC.GetAllocatedBytesForCurrentThread();
        object value = RuntimeHelpers.GetUninitializedObject(type);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(value);
        return bytes;
    }

    private static long ArrayBytes(Array a)
    {
        _ = a.Clone();
        long before = GC.GetAllocatedBytesForCurrentThread();
        object clone = a.Clone();
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(clone);
        return bytes;
    }

    public static (long Structure, long Buckets, long Entries, int Capacity) Estimate(object value, int n)
    {
        if (value is Entity[] a) return (ArrayBytes(a), 0, 0, a.Length);
        if (value is Dictionary<int, Entity> dictionary)
        {
            var buckets = (Array)Field(dictionary, "_buckets")!;
            var entries = (Array)Field(dictionary, "_entries")!;
            long b = ArrayBytes(buckets), e = ArrayBytes(entries);
            return (Shallow(value.GetType()) + b + e, b, e, entries.Length);
        }
        var set = Field(value, "_set")!;
        var root = Field(set, "root");
        var comparer = Field(set, "comparer")!;
        long nodes = root == null ? 0 : n * Shallow(root.GetType());
        return (Shallow(value.GetType()) + Shallow(set.GetType()) + Shallow(comparer.GetType()) + nodes, 0, 0, n);
    }

    public static MemorySample Measure(int n, KeyDistribution distribution, StructureKind kind, int repeat)
    {
        var template = DataSet.Create(n, distribution);
        object warm = DataSet.Build(template, kind);
        var model = Estimate(warm, n);
        long entitySize = Shallow(typeof(Entity));
        int replicas = (int)Math.Clamp((8L * 1024 * 1024 + model.Structure - 1) / model.Structure, 1, 100_000);
        // Fixed root arrays belong to the harness and exist before every baseline.
        var roots = new object?[replicas];
        var sources = new Entity[]?[replicas];
        FillSources(sources, template);
        long baseline = Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        FillStructures(roots, sources, kind);
        double allocated = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / (double)replicas;
        long withStructures = Collect();
        double measuredStructure = (withStructures - baseline) / (double)replicas;
        Array.Clear(roots);
        Array.Clear(sources);
        _ = Collect();

        // template is part of both baselines; total uses independent Entity instances.
        baseline = Collect();
        long entityBefore = GC.GetAllocatedBytesForCurrentThread();
        FillSources(sources, template);
        long sourceAllocated = GC.GetAllocatedBytesForCurrentThread() - entityBefore;
        FillStructures(roots, sources, kind);
        Array.Clear(sources); // no temporary Entity[] is counted as retained memory
        long withTotal = Collect();
        double measuredTotal = (withTotal - baseline) / (double)replicas;
        long sourceArrayBytes = ArrayBytes(template);
        double entityAllocated = sourceAllocated / (double)replicas - sourceArrayBytes;
        GC.KeepAlive(roots);
        GC.KeepAlive(sources);
        GC.KeepAlive(template);
        GC.KeepAlive(warm);
        return new(n, distribution, kind, repeat, replicas, model.Capacity, model.Capacity - n,
            model.Buckets, model.Entries, model.Structure, model.Structure + n * entitySize,
            measuredStructure, measuredTotal, allocated, entityAllocated);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FillSources(Entity[]?[] sources, Entity[] template)
    {
        for (int r = 0; r < sources.Length; r++)
        {
            var a = new Entity[template.Length];
            for (int i = 0; i < a.Length; i++) a[i] = new Entity(template[i].Key, template[i].Value);
            sources[r] = a;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void FillStructures(object?[] roots, Entity[]?[] sources, StructureKind kind)
    {
        for (int i = 0; i < roots.Length; i++) roots[i] = DataSet.Build(sources[i]!, kind);
    }

    private static long Collect()
    {
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        return GC.GetTotalMemory(true);
    }
}
