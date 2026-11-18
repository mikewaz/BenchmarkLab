using BenchmarkLab.DictionaryVsArray;
using BenchmarkLab.ConsoleApp;
using Xunit;

namespace BenchmarkLab.Tests;

public class CorrectnessTests
{
    [Fact]
    public void BenchmarkMatrixContains432CasesWithoutRunningMeasurements()
    {
        var benchmarks = BenchmarkDotNet.Running.BenchmarkConverter.TypeToBenchmarks(typeof(LookupBenchmarks));
        Assert.Equal(432, benchmarks.BenchmarksCases.Length);
        Assert.All(benchmarks.BenchmarksCases, c => Assert.Equal(DataSet.QueryCount, c.Descriptor.OperationsPerInvoke));
    }

    [Fact]
    public void AllAlgorithmsAndScenarios() => Correctness.ValidateAll();

    [Fact]
    public void ConstructionSharesEntityReferences()
    {
        var source = DataSet.Create(100, KeyDistribution.Random);
        foreach (var kind in Enum.GetValues<StructureKind>())
        {
            object structure = DataSet.Build(source, kind);
            foreach (var e in source)
            {
                Entity? found = structure switch
                {
                    Entity[] a => Search.Binary(a, e.Key),
                    Dictionary<int, Entity> d => d[e.Key],
                    SortedDictionary<int, Entity> t => t[e.Key],
                    _ => throw new InvalidOperationException()
                };
                Assert.Same(e, found);
            }
        }
    }

    [Fact]
    public void RuntimeMemoryModelFieldsAreSupported()
    {
        // Only verifies diagnostics compatibility and allocation-based size model;
        // does not run retained-memory measurements or export measured results.
        var source = DataSet.Create(10, KeyDistribution.Random);
        foreach (var kind in Enum.GetValues<StructureKind>())
        {
            var model = MemoryDiagnostics.Estimate(DataSet.Build(source, kind), source.Length);
            Assert.True(model.Structure > 0);
            Assert.True(model.Capacity >= source.Length);
            if (kind is StructureKind.DictionaryPreallocated or StructureKind.DictionaryAutoGrow)
                Assert.True(model.Buckets > 0 && model.Entries > 0);
        }
    }

    [Fact]
    public void MissingResultsNeverProduceFinalReport()
    {
        string path = Path.Combine(Path.GetTempPath(), "BenchmarkLab-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        try
        {
            Assert.False(ReportWriter.Generate(path));
            Assert.False(File.Exists(Path.Combine(path, "BENCHMARK_RESULTS.md")));
            Assert.True(File.Exists(Path.Combine(path, "REPORT_INCOMPLETE.md")));
            Assert.False(ReportWriter.CompletePerformance([]));
            Assert.False(ReportWriter.CompleteMemory([]));
        }
        finally { Directory.Delete(path, true); }
    }
}
