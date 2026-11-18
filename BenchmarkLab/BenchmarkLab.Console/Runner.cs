using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkLab.DictionaryVsArray;

namespace BenchmarkLab.ConsoleApp;

public static class Runner
{
    public static int Run(string[] args)
    {
        try { return Execute(args); }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static int Execute(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help")
        {
            Console.WriteLine("Modes: validate | performance [--profile Quick|Full] [--launches N] [--warmups N] [--iterations N] [--output DIR] [BDN options: --filter *Binary*] | memory [--output DIR] | report [--output DIR]");
            return 0;
        }
        if (args[0] == "validate") { Correctness.ValidateAll(); Console.WriteLine("All correctness/allocation checks passed."); return 0; }
#if DEBUG
        if (args[0] is "performance" or "memory" or "memory-worker")
            throw new InvalidOperationException("Measurements require a Release build.");
#endif
        if (args[0] == "memory-worker")
        {
            Console.WriteLine(JsonSerializer.Serialize(MemoryDiagnostics.Measure(int.Parse(args[1]),
                Enum.Parse<KeyDistribution>(args[2]), Enum.Parse<StructureKind>(args[3]), int.Parse(args[4]))));
            return 0;
        }
        var remaining = args.Skip(1).ToList();
        string output = Path.GetFullPath(Take(remaining, "--output", "artifacts/dictionary-vs-array"));
        Directory.CreateDirectory(output);
        switch (args[0])
        {
            case "performance": Performance(remaining, output); break;
            case "memory":
                if (remaining.Count != 0) throw new ArgumentException("Unknown memory options.");
                Memory(output); break;
            case "report":
                if (remaining.Count != 0) throw new ArgumentException("Unknown report options.");
                return ReportWriter.Generate(output) ? 0 : 2;
            default: throw new ArgumentException($"Unknown mode: {args[0]}");
        }
        return 0;
    }
    private static string Take(List<string> args, string option, string fallback)
    {
        int index = args.IndexOf(option);
        if (index < 0) return fallback;
        if (index + 1 == args.Count) throw new ArgumentException($"Missing value: {option}");
        string value = args[index + 1]; args.RemoveRange(index, 2); return value;
    }
    private static int Positive(string text)
    {
        if (!int.TryParse(text, out int n) || n < 1) throw new ArgumentException("Counts must be positive integers.");
        return n;
    }
    private static void Performance(List<string> args, string output)
    {
        string profile = Take(args, "--profile", "Full");
        if (profile is not ("Quick" or "Full")) throw new ArgumentException("Profile must be Quick or Full.");
        bool full = profile == "Full";
        int launches = Positive(Take(args, "--launches", full ? "3" : "1"));
        int warmups = Positive(Take(args, "--warmups", full ? "5" : "3"));
        int iterations = Positive(Take(args, "--iterations", full ? "20" : "5"));
        var job = Job.Default.WithId(profile).WithLaunchCount(launches).WithWarmupCount(warmups).WithIterationCount(iterations);
        var config = ManualConfig.Create(DefaultConfig.Instance).AddJob(job)
            .AddColumn(StatisticColumn.Median, StatisticColumn.P95, StatisticColumn.OperationsPerSecond)
            .WithArtifactsPath(Path.Combine(output, "BenchmarkDotNet"));
        File.Delete(Path.Combine(output, "performance.complete"));
        File.Delete(Path.Combine(output, "BENCHMARK_RESULTS.md"));
        WriteJson(output, "performance-environment", new {
            Timestamp = DateTimeOffset.UtcNow, Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount = Environment.ProcessorCount, ServerGC = System.Runtime.GCSettings.IsServerGC,
            CpuIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            Profile = profile, LaunchCount = launches, WarmupCount = warmups, IterationCount = iterations,
            Seed = DataSet.Seed, QueriesPerInvocation = DataSet.QueryCount, Arguments = args });
        if (!args.Contains("--filter")) args.AddRange(["--filter", "*"]);
        var summaries = BenchmarkSwitcher.FromTypes([typeof(LookupBenchmarks)]).Run(args.ToArray(), config);
        var rows = new List<PerformanceRow>();
        foreach (var summary in summaries)
        foreach (var report in summary.Reports)
        {
            if (!report.Success || report.ResultStatistics is not { } s) continue;
            var c = report.BenchmarkCase;
            object Param(string name) => c.Parameters.Items.Single(p => p.Name == name).Value;
            rows.Add(new((int)Param("N"), (KeyDistribution)Param("Distribution"), c.Descriptor.WorkloadMethod.Name,
                (QueryScenario)Param("Scenario"), s.Mean, s.Median, s.Percentiles.P95, 1e9 / s.Mean,
                report.GcStats.GetBytesAllocatedPerOperation(c)));
        }
        Csv.Write(Path.Combine(output, "performance.csv"), rows);
        WriteJson(output, "performance", rows);
        if (ReportWriter.CompletePerformance(rows)) File.WriteAllText(Path.Combine(output, "performance.complete"), "432 successful cases");
        else Console.WriteLine($"Incomplete performance matrix: {rows.Count}/432. No completion marker written.");
    }
    private static void Memory(string output)
    {
        File.Delete(Path.Combine(output, "memory.complete"));
        File.Delete(Path.Combine(output, "BENCHMARK_RESULTS.md"));
        var samples = new List<MemorySample>();
        foreach (int n in DataSet.Sizes)
        foreach (var distribution in Enum.GetValues<KeyDistribution>())
        foreach (var kind in Enum.GetValues<StructureKind>())
        for (int repeat = 0; repeat < 5; repeat++)
        {
            Console.WriteLine($"Memory: N={n}, {distribution}, {kind}, repeat {repeat + 1}/5");
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (string arg in new[] { Assembly.GetExecutingAssembly().Location, "memory-worker", n.ToString(CultureInfo.InvariantCulture), distribution.ToString(), kind.ToString(), repeat.ToString(CultureInfo.InvariantCulture) })
                start.ArgumentList.Add(arg);
            using var worker = Process.Start(start) ?? throw new InvalidOperationException("Cannot start memory worker.");
            var stdout = worker.StandardOutput.ReadToEndAsync();
            var stderr = worker.StandardError.ReadToEndAsync();
            worker.WaitForExit();
            if (worker.ExitCode != 0) throw new InvalidOperationException(stderr.GetAwaiter().GetResult());
            samples.Add(JsonSerializer.Deserialize<MemorySample>(stdout.GetAwaiter().GetResult())!);
            Csv.Write(Path.Combine(output, "memory-samples.csv"), samples);
        }
        WriteJson(output, "memory-samples", samples);
        Csv.Write(Path.Combine(output, "memory.csv"), ReportWriter.SummarizeMemory(samples));
        WriteJson(output, "memory-environment", new {
            Timestamp = DateTimeOffset.UtcNow, Runtime = RuntimeInformation.FrameworkDescription,
            OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            CpuIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
            ProcessorCount = Environment.ProcessorCount,
            ServerGC = System.Runtime.GCSettings.IsServerGC, Repeats = 5, BatchTargetBytes = 8 * 1024 * 1024, Seed = DataSet.Seed });
        File.WriteAllText(Path.Combine(output, "memory.complete"), "240 isolated measurements");
    }
    private static void WriteJson<T>(string output, string name, T value) =>
        File.WriteAllText(Path.Combine(output, name + ".json"), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
}
public sealed record PerformanceRow(int N, KeyDistribution Distribution, string Algorithm, QueryScenario Scenario,
    double MeanNs, double MedianNs, double P95Ns, double OperationsPerSecond, double? AllocatedBytesPerOperation);
public static class Csv
{
    public static void Write<T>(string path, IEnumerable<T> rows)
    {
        var properties = typeof(T).GetProperties();
        using var writer = new StreamWriter(path);
        writer.WriteLine(string.Join(",", properties.Select(p => p.Name)));
        foreach (var row in rows) writer.WriteLine(string.Join(",", properties.Select(p => Escape(p.GetValue(row)))));
    }
    private static string Escape(object? value)
    {
        string s = value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}

