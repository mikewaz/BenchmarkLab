namespace BenchmarkLab.DictionaryVsArray;

public enum KeyDistribution { Sequential, Random, Clustered }
public enum QueryScenario { Hit, MissInside, MissOutside, Mixed }
public enum StructureKind { Array, DictionaryPreallocated, DictionaryAutoGrow, SortedDictionary }

public readonly record struct Query(int Key, Entity Probe);

public static class DataSet
{
    public const int Seed = 1729;
    public const int QueryCount = 1024;
    public static readonly int[] Sizes = [10, 100, 10_000, 1_000_000];

    public static Entity[] Create(int count, KeyDistribution distribution)
    {
        if (count < 0 || count > 1_000_000) throw new ArgumentOutOfRangeException(nameof(count));
        var rng = new Random(Seed);
        var keys = new HashSet<int>();
        var result = new Entity[count];
        for (int i = 0; i < count; i++)
        {
            int key;
            // Even keys guarantee odd-key misses inside every interval. Cluster widths
            // are deliberately unequal; all arithmetic stays away from int boundaries.
            do
            {
                key = distribution switch
                {
                    KeyDistribution.Sequential => i * 2,
                    KeyDistribution.Random => rng.Next(-500_000_000, 500_000_000) * 2,
                    KeyDistribution.Clustered => (rng.Next(10) < 9
                        ? rng.Next(-2_000_000, 2_000_000)
                        : rng.Next(100_000_000, 400_000_000)) * 2,
                    _ => throw new ArgumentOutOfRangeException(nameof(distribution))
                };
            } while (!keys.Add(key));
            result[i] = new Entity(key, rng.Next());
        }
        Array.Sort(result, Search.KeyComparer);
        return result;
    }

    public static Query[] Queries(Entity[] sorted, QueryScenario scenario)
    {
        if (sorted.Length < 2) throw new ArgumentException("Query datasets require at least two keys.");
        var rng = new Random(Seed + (int)scenario);
        var queries = new Query[QueryCount];
        for (int i = 0; i < queries.Length; i++)
        {
            // Stratification guarantees coverage of low, middle and high ranks.
            int rank = (int)((long)i * sorted.Length / queries.Length);
            QueryScenario kind = scenario == QueryScenario.Mixed
                ? (i % 4) switch { 0 or 1 => QueryScenario.Hit, 2 => QueryScenario.MissInside, _ => QueryScenario.MissOutside }
                : scenario;
            int key = kind switch
            {
                QueryScenario.Hit => sorted[rank].Key,
                QueryScenario.MissInside => sorted[Math.Min(rank, sorted.Length - 2)].Key + 1,
                QueryScenario.MissOutside => (scenario == QueryScenario.Mixed ? i / 4 : i) % 2 == 0
                    ? sorted[0].Key - 1 : sorted[^1].Key + 1,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
            queries[i] = new Query(key, new Entity(key, 0));
        }
        rng.Shuffle(queries);
        return queries;
    }

    public static object Build(Entity[] source, StructureKind kind)
    {
        if (kind == StructureKind.Array) return (Entity[])source.Clone();
        if (kind == StructureKind.SortedDictionary)
        {
            var tree = new SortedDictionary<int, Entity>();
            foreach (var e in source) tree.Add(e.Key, e);
            return tree;
        }
        var dictionary = kind == StructureKind.DictionaryPreallocated
            ? new Dictionary<int, Entity>(source.Length) : new Dictionary<int, Entity>();
        foreach (var e in source) dictionary.Add(e.Key, e);
        return dictionary;
    }
}
