using BenchmarkDotNet.Attributes;

namespace BenchmarkLab.DictionaryVsArray;

[MemoryDiagnoser]
public class LookupBenchmarks
{
    [Params(10, 100, 10_000, 1_000_000)]
    public int N { get; set; }
    [ParamsAllValues] public KeyDistribution Distribution { get; set; }
    [ParamsAllValues] public QueryScenario Scenario { get; set; }
    private Entity[] array = null!;
    private Query[] queries = null!;
    private Dictionary<int, Entity> dictionary = null!;
    private SortedDictionary<int, Entity> tree = null!;

    [GlobalSetup]
    public void Setup()
    {
        array = DataSet.Create(N, Distribution);
        dictionary = (Dictionary<int, Entity>)DataSet.Build(array, StructureKind.DictionaryPreallocated);
        tree = (SortedDictionary<int, Entity>)DataSet.Build(array, StructureKind.SortedDictionary);
        queries = DataSet.Queries(array, Scenario);
        Correctness.Check(array, dictionary, tree, queries);
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = DataSet.QueryCount)]
    public long Dictionary()
    {
        long checksum = 0;
        foreach (var q in queries)
        {
            Entity? found = dictionary.TryGetValue(q.Key, out var e) ? e : null;
            checksum += found is null ? -1L : found.Value;
        }
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = DataSet.QueryCount)]
    public long Linear()
    {
        long checksum = 0;
        foreach (var q in queries)
        {
            Entity? found = Search.Linear(array, q.Key);
            checksum += found is null ? -1L : found.Value;
        }
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = DataSet.QueryCount)]
    public long Binary()
    {
        long checksum = 0;
        foreach (var q in queries)
        {
            Entity? found = Search.Binary(array, q.Key);
            checksum += found is null ? -1L : found.Value;
        }
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = DataSet.QueryCount)]
    public long Interpolation()
    {
        long checksum = 0;
        foreach (var q in queries)
        {
            Entity? found = Search.Interpolation(array, q.Key);
            checksum += found is null ? -1L : found.Value;
        }
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = DataSet.QueryCount)]
    public long Exponential()
    {
        long checksum = 0;
        foreach (var q in queries)
        {
            Entity? found = Search.Exponential(array, q.Key);
            checksum += found is null ? -1L : found.Value;
        }
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = DataSet.QueryCount)]
    public long Jump()
    {
        long checksum = 0;
        foreach (var q in queries)
        {
            Entity? found = Search.Jump(array, q.Key);
            checksum += found is null ? -1L : found.Value;
        }
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = DataSet.QueryCount)]
    public long Fibonacci()
    {
        long checksum = 0;
        foreach (var q in queries)
        {
            Entity? found = Search.Fibonacci(array, q.Key);
            checksum += found is null ? -1L : found.Value;
        }
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = DataSet.QueryCount)]
    public long ArrayBinarySearch()
    {
        long checksum = 0;
        foreach (var q in queries)
        {
            Entity? found = Search.Standard(array, q.Probe);
            checksum += found is null ? -1L : found.Value;
        }
        return checksum;
    }

    [Benchmark(OperationsPerInvoke = DataSet.QueryCount)]
    public long SortedDictionary()
    {
        long checksum = 0;
        foreach (var q in queries)
        {
            Entity? found = tree.TryGetValue(q.Key, out var e) ? e : null;
            checksum += found is null ? -1L : found.Value;
        }
        return checksum;
    }

}

