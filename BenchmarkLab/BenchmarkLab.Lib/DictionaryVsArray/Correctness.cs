namespace BenchmarkLab.DictionaryVsArray;

public static class Correctness
{
    public static void Check(Entity[] array, Dictionary<int, Entity> dictionary,
        SortedDictionary<int, Entity> tree, IEnumerable<Query> queries)
    {
        foreach (var q in queries)
        {
            dictionary.TryGetValue(q.Key, out var expected);
            Entity?[] results = [Search.Linear(array, q.Key), Search.Binary(array, q.Key),
                Search.Interpolation(array, q.Key), Search.Exponential(array, q.Key),
                Search.Jump(array, q.Key), Search.Fibonacci(array, q.Key), Search.Standard(array, q.Probe),
                tree.TryGetValue(q.Key, out var found) ? found : null];
            for (int i = 0; i < results.Length; i++)
                if (!ReferenceEquals(expected, results[i]))
                    throw new InvalidOperationException($"Search {i} failed for key {q.Key}, N={array.Length}.");
        }
    }

    public static void ValidateAll()
    {
        foreach (int size in new[] { 0, 1, 2, 3, 5, 8, 9, 13, 16, 21, 34, 99, 100, 144, 257 })
        {
            var a = Enumerable.Range(0, size).Select(i => new Entity(i * 4 - 500, i)).ToArray();
            CheckCustom(a, Enumerable.Range(-505, size * 4 + 12));
        }
        CheckCustom([new Entity(int.MinValue, 1), new Entity(-1, 2), new Entity(0, 3), new Entity(int.MaxValue, 4)],
            [int.MinValue, int.MinValue + 1, -2, -1, 0, 1, int.MaxValue - 1, int.MaxValue]);
        foreach (var distribution in Enum.GetValues<KeyDistribution>())
        foreach (int n in DataSet.Sizes)
        {
            var a = DataSet.Create(n, distribution);
            var b = DataSet.Create(n, distribution);
            if (!a.Select(e => (e.Key, e.Value)).SequenceEqual(b.Select(e => (e.Key, e.Value))))
                throw new InvalidOperationException("Data is not reproducible.");
            if (a.Select(e => e.Key).Distinct().Count() != n) throw new InvalidOperationException("Duplicate key.");
            var dictionary = (Dictionary<int, Entity>)DataSet.Build(a, StructureKind.DictionaryPreallocated);
            var tree = (SortedDictionary<int, Entity>)DataSet.Build(a, StructureKind.SortedDictionary);
            foreach (var scenario in Enum.GetValues<QueryScenario>())
            {
                var queries = DataSet.Queries(a, scenario);
                if (!queries.Select(q => q.Key).SequenceEqual(DataSet.Queries(a, scenario).Select(q => q.Key)))
                    throw new InvalidOperationException("Queries are not reproducible.");
                int hits = queries.Count(q => dictionary.ContainsKey(q.Key));
                int inside = queries.Count(q => !dictionary.ContainsKey(q.Key) && q.Key > a[0].Key && q.Key < a[^1].Key);
                int expectedHits = scenario == QueryScenario.Hit ? 1024 : scenario == QueryScenario.Mixed ? 512 : 0;
                int expectedInside = scenario == QueryScenario.MissInside ? 1024 : scenario == QueryScenario.Mixed ? 256 : 0;
                if (hits != expectedHits || inside != expectedInside) throw new InvalidOperationException("Scenario composition.");
                if (scenario is QueryScenario.Mixed or QueryScenario.MissOutside)
                {
                    int below = queries.Count(q => q.Key < a[0].Key);
                    int above = queries.Count(q => q.Key > a[^1].Key);
                    if (below != above) throw new InvalidOperationException("Outside queries must cover both sides equally.");
                }
                Check(a, dictionary, tree, queries);
            }
            if (n <= 100) CheckCustom(a, a.Select(e => e.Key));
            Console.WriteLine($"Validated N={n}, {distribution}");
        }
        CheckAllocations();
    }

    private static void CheckCustom(Entity[] a, IEnumerable<int> keys)
    {
        var dictionary = a.ToDictionary(e => e.Key);
        var tree = new SortedDictionary<int, Entity>(dictionary);
        Check(a, dictionary, tree, keys.Select(k => new Query(k, new Entity(k, 0))));
    }

    private static void CheckAllocations()
    {
        var b = new LookupBenchmarks { N = 100, Distribution = KeyDistribution.Random, Scenario = QueryScenario.Mixed };
        b.Setup();
        Func<long>[] methods = [b.Dictionary, b.Linear, b.Binary, b.Interpolation, b.Exponential,
            b.Jump, b.Fibonacci, b.ArrayBinarySearch, b.SortedDictionary];
        foreach (var method in methods)
        {
            for (int i = 0; i < 100; i++) _ = method();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long checksum = 0;
            for (int i = 0; i < 100; i++) checksum += method();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(checksum);
            if (allocated != 0) throw new InvalidOperationException($"{method.Method.Name}: {allocated} search bytes allocated.");
        }
    }
}
