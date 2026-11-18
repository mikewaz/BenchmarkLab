using System.Globalization;
using System.Text;
using System.Text.Json;
using BenchmarkLab.DictionaryVsArray;

namespace BenchmarkLab.ConsoleApp;

public sealed record MemoryRow(int N, KeyDistribution Distribution, StructureKind Structure, int Capacity, int UnusedCapacity,
    double MeasuredStructureBytes, double MeasuredStructureMin, double MeasuredStructureMax,
    double MeasuredTotalBytes, double MeasuredTotalMin, double MeasuredTotalMax,
    double EstimatedStructureBytes, double EstimatedTotalBytes, double EstimatedBucketsBytes, double EstimatedEntriesBytes,
    double BuildAllocatedBytes, double EntityAllocatedBytes,
    double MeasuredStructureBytesPerElement, double MeasuredTotalBytesPerElement,
    double EstimatedStructureBytesPerElement, double EstimatedTotalBytesPerElement,
    double? MeasuredStructureRatioVsArray, double? MeasuredTotalRatioVsArray,
    double EstimatedStructureRatioVsArray, double EstimatedTotalRatioVsArray);

public static class ReportWriter
{
    private static readonly string[] Algorithms = ["Dictionary", "Linear", "Binary", "Interpolation", "Exponential", "Jump", "Fibonacci", "ArrayBinarySearch", "SortedDictionary"];
    private static double Median(IEnumerable<double> values) { var a = values.Order().ToArray(); return a[a.Length / 2]; }
    private static double? Ratio(double a, double b) => a > 0 && b > 0 ? a / b : null;
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string F(double? value) => value.HasValue ? F(value.Value) : "N/A";

    public static bool CompletePerformance(IReadOnlyCollection<PerformanceRow> rows) =>
        rows.Count == 432 && rows.All(r => double.IsFinite(r.MeanNs) && r.MeanNs > 0) &&
        DataSet.Sizes.All(n => Enum.GetValues<KeyDistribution>().All(d => Algorithms.All(a =>
            Enum.GetValues<QueryScenario>().All(s => rows.Count(r => r.N == n && r.Distribution == d && r.Algorithm == a && r.Scenario == s) == 1))));

    public static bool CompleteMemory(IReadOnlyCollection<MemorySample> rows) => rows.Count == 240 &&
        DataSet.Sizes.All(n => Enum.GetValues<KeyDistribution>().All(d => Enum.GetValues<StructureKind>().All(k =>
            Enumerable.Range(0, 5).All(i => rows.Count(r => r.N == n && r.Distribution == d && r.Structure == k && r.Repeat == i) == 1))));

    public static List<MemoryRow> SummarizeMemory(List<MemorySample> samples)
    {
        var rows = new List<MemoryRow>();
        foreach (var g in samples.GroupBy(s => (s.N, s.Distribution, s.Structure)))
        {
            var first = g.First();
            var array = samples.Where(s => s.N == first.N && s.Distribution == first.Distribution && s.Structure == StructureKind.Array).ToArray();
            double ms = Median(g.Select(s => s.MeasuredStructureBytes)), mt = Median(g.Select(s => s.MeasuredTotalBytes));
            rows.Add(new(first.N, first.Distribution, first.Structure, first.Capacity, first.UnusedCapacity,
                ms, g.Min(s => s.MeasuredStructureBytes), g.Max(s => s.MeasuredStructureBytes),
                mt, g.Min(s => s.MeasuredTotalBytes), g.Max(s => s.MeasuredTotalBytes),
                first.EstimatedStructureBytes, first.EstimatedTotalBytes, first.EstimatedBucketsBytes, first.EstimatedEntriesBytes,
                Median(g.Select(s => s.BuildAllocatedBytes)), Median(g.Select(s => s.EntityAllocatedBytes)),
                ms / first.N, mt / first.N, (double)first.EstimatedStructureBytes / first.N, (double)first.EstimatedTotalBytes / first.N,
                Ratio(ms, Median(array.Select(s => s.MeasuredStructureBytes))), Ratio(mt, Median(array.Select(s => s.MeasuredTotalBytes))),
                (double)first.EstimatedStructureBytes / array[0].EstimatedStructureBytes, (double)first.EstimatedTotalBytes / array[0].EstimatedTotalBytes));
        }
        return rows;
    }

    public static bool Generate(string output)
    {
        string[] required = ["performance.complete", "memory.complete", "performance.json", "memory-samples.json", "performance-environment.json", "memory-environment.json"];
        var missing = required.Where(f => !File.Exists(Path.Combine(output, f))).ToArray();
        if (missing.Length != 0) return Incomplete(output, "Missing completed results: " + string.Join(", ", missing));
        var p = JsonSerializer.Deserialize<List<PerformanceRow>>(File.ReadAllText(Path.Combine(output, "performance.json")))!;
        var samples = JsonSerializer.Deserialize<List<MemorySample>>(File.ReadAllText(Path.Combine(output, "memory-samples.json")))!;
        if (!CompletePerformance(p) || !CompleteMemory(samples)) return Incomplete(output, "Incomplete or duplicate matrix entries.");
        var m = SummarizeMemory(samples);
        var text = new StringBuilder("# Dictionary vs Sorted Array — результаты\n\n");
        text.AppendLine("Все значения получены при запуске измерений. Mean/Median/P95 — ns на один поиск. P95 описывает распределение средних времён пакетов, а не задержки отдельных запросов. Quick предназначен для предварительной оценки; параметры запуска приведены ниже.\n");
        foreach (var name in new[] { "performance", "memory" })
            text.AppendLine("## Среда " + name + "\n\n```json\n" + File.ReadAllText(Path.Combine(output, name + "-environment.json")) + "\n```\n");
        text.AppendLine("## Методика\n\n" + Methodology + "\n");
        text.AppendLine("## A. Производительность\n\n| N | Distribution | Algorithm | Scenario | Mean ns | Median ns | P95 ns | op/s | B/op | Dictionary speedup vs algorithm |\n|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in p.OrderBy(r => r.N).ThenBy(r => r.Distribution).ThenBy(r => r.Scenario).ThenBy(r => r.Algorithm))
        {
            var baseline = p.Single(b => b.N == r.N && b.Distribution == r.Distribution && b.Scenario == r.Scenario && b.Algorithm == "Dictionary");
            text.AppendLine($"| {r.N} | {r.Distribution} | {r.Algorithm} | {r.Scenario} | {F(r.MeanNs)} | {F(r.MedianNs)} | {F(r.P95Ns)} | {F(r.OperationsPerSecond)} | {F(r.AllocatedBytesPerOperation)} | {F(r.MeanNs / baseline.MeanNs)}x |");
        }
        text.AppendLine("\nЗначение speedup больше 1 означает, что Dictionary быстрее указанного алгоритма.\n");
        text.AppendLine("## B. Память\n\n| N | Distribution | Structure | Capacity | Unused | Measured structure KiB | Measured total KiB | Estimated structure KiB | Estimated total KiB | Measured structure B/element | Measured total B/element | Measured structure ratio | Estimated structure ratio |\n|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in m)
            text.AppendLine($"| {r.N} | {r.Distribution} | {r.Structure} | {(r.Structure == StructureKind.SortedDictionary ? "N/A" : r.Capacity)} | {r.UnusedCapacity} | {F(r.MeasuredStructureBytes / 1024)} | {F(r.MeasuredTotalBytes / 1024)} | {F(r.EstimatedStructureBytes / 1024)} | {F(r.EstimatedTotalBytes / 1024)} | {F(r.MeasuredStructureBytesPerElement)} | {F(r.MeasuredTotalBytesPerElement)} | {F(r.MeasuredStructureRatioVsArray)} | {F(r.EstimatedStructureRatioVsArray)} |");
        text.AppendLine("\nПолные B/element, отношения Total, диапазоны повторов, аллокации и компоненты Dictionary находятся в memory.csv; исходные повторы — memory-samples.csv.\n");
        text.AppendLine("## Сравнения по измеренным данным\n");
        foreach (var g in p.GroupBy(r => (r.N, r.Distribution, r.Scenario)))
        {
            var best = g.Where(r => r.Algorithm is not ("Dictionary" or "SortedDictionary")).MinBy(r => r.MeanNs)!;
            var fastest = g.MinBy(r => r.MeanNs)!;
            text.AppendLine($"- N={g.Key.N}, {g.Key.Distribution}, {g.Key.Scenario}: минимальный Mean у {fastest.Algorithm} ({F(fastest.MeanNs)} ns); среди массивов — {best.Algorithm} ({F(best.MeanNs)} ns).");
        }
        foreach (var g in m.GroupBy(r => (r.N, r.Distribution)))
        {
            var a = g.Single(r => r.Structure == StructureKind.Array);
            foreach (var d in g.Where(r => r.Structure is StructureKind.DictionaryPreallocated or StructureKind.DictionaryAutoGrow))
                text.AppendLine($"- N={d.N}, {d.Distribution}, {d.Structure}: дополнительная Measured Structure Memory {F(d.MeasuredStructureBytes - a.MeasuredStructureBytes)} B; buckets {F(d.EstimatedBucketsBytes)} B и entries {F(d.EstimatedEntriesBytes)} B (Estimated); расхождение Measured − Estimated {F(d.MeasuredStructureBytes - d.EstimatedStructureBytes)} B; свободно {d.UnusedCapacity}; аллокации построения {F(d.BuildAllocatedBytes)} B.");
            var pre = g.Single(r => r.Structure == StructureKind.DictionaryPreallocated);
            var auto = g.Single(r => r.Structure == StructureKind.DictionaryAutoGrow);
            text.AppendLine($"- N={g.Key.N}, {g.Key.Distribution}: AutoGrow − Preallocated: Measured Structure {F(auto.MeasuredStructureBytes - pre.MeasuredStructureBytes)} B; Estimated Structure {F(auto.EstimatedStructureBytes - pre.EstimatedStructureBytes)} B; аллокации построения {F(auto.BuildAllocatedBytes - pre.BuildAllocatedBytes)} B; Capacity {auto.Capacity} против {pre.Capacity}.");
        }
        text.AppendLine("\n### Влияние распределения (отношение Mean к Sequential)\n");
        foreach (var r in p.Where(r => r.Distribution != KeyDistribution.Sequential))
        {
            var sequential = p.Single(b => b.N == r.N && b.Distribution == KeyDistribution.Sequential && b.Algorithm == r.Algorithm && b.Scenario == r.Scenario);
            text.AppendLine($"- N={r.N}, {r.Algorithm}, {r.Scenario}: {r.Distribution}/Sequential = {F(r.MeanNs / sequential.MeanNs)}x.");
        }
        text.AppendLine("\nДля выбора при частом поиске сравнивайте Mean выбранного сценария с Structure/Total Memory того же N и распределения. Таблицы дают отдельные сравнения по размерам и распределениям; универсальная рекомендация и статистическая значимость небольших различий из этих измерений не следуют. Расхождения памяти могут включать шум GC и ошибки аддитивной модели; точную причину без анализа heap dump установить нельзя.\n");
        text.AppendLine("## Графики\n");
        foreach (var d in Enum.GetValues<KeyDistribution>())
        foreach (var s in Enum.GetValues<QueryScenario>())
        {
            string name = $"speed-{d}-{s}.svg";
            Plot(Path.Combine(output, name), $"{d} / {s}: Mean ns (log axes)", p.Where(r => r.Distribution == d && r.Scenario == s).Select(r => (r.Algorithm, r.N, r.MeanNs)));
            text.AppendLine($"![{d} {s}]({name})\n");
        }
        foreach (var d in Enum.GetValues<KeyDistribution>())
        {
            string name = $"memory-{d}.svg";
            Plot(Path.Combine(output, name), $"{d}: Structure memory bytes (log axes)", m.Where(r => r.Distribution == d).SelectMany(r => new[] { (r.Structure + " measured", r.N, r.MeasuredStructureBytes), (r.Structure + " estimated", r.N, r.EstimatedStructureBytes) }));
            text.AppendLine($"![Memory {d}]({name})\n");
            name = $"total-memory-{d}.svg";
            Plot(Path.Combine(output, name), $"{d}: Total memory bytes (log axes)", m.Where(r => r.Distribution == d).SelectMany(r => new[] { (r.Structure + " measured", r.N, r.MeasuredTotalBytes), (r.Structure + " estimated", r.N, r.EstimatedTotalBytes) }));
            text.AppendLine($"![Total memory {d}]({name})\n");
            name = $"ratio-{d}.svg";
            Plot(Path.Combine(output, name), $"{d}: Dictionary/array structure ratio", m.Where(r => r.Distribution == d && r.Structure is StructureKind.DictionaryPreallocated or StructureKind.DictionaryAutoGrow)
                .SelectMany(r => new[] { (r.Structure + " measured", r.N, r.MeasuredStructureRatioVsArray ?? double.NaN), (r.Structure + " estimated", r.N, r.EstimatedStructureRatioVsArray) }));
            text.AppendLine($"![Ratio {d}]({name})\n");
        }
        File.WriteAllText(Path.Combine(output, "BENCHMARK_RESULTS.md"), text.ToString());
        File.Delete(Path.Combine(output, "REPORT_INCOMPLETE.md"));
        return true;
    }
    private static bool Incomplete(string output, string reason)
    {
        File.WriteAllText(Path.Combine(output, "REPORT_INCOMPLETE.md"), reason + "\nNo results were invented. Run both measurement modes before report.");
        Console.Error.WriteLine(reason);
        return false;
    }
    public const string Methodology = """
    Данные и запросы детерминированы (seed 1729), подготовка исключена из поиска. Все алгоритмы массива используют один Entity[]; Dictionary и дерево ссылаются на те же Entity. Sequential означает чётные ключи с шагом 2, чтобы Miss Inside существовал. Random выбирает уникальные чётные ключи в широком диапазоне. Clustered помещает примерно 90% ключей в узкую область и 10% — в широкую удалённую область. Попадания распределены по рангам. Miss Outside поровну ниже минимума и выше максимума. Mixed: 50% Hit, 25% Miss Inside, 25% Miss Outside.

    Measured Memory — разность GC.GetTotalMemory(true) после полных сборок в изолированном процессе, а не точный размер структуры. Для небольших структур используются независимые копии, суммарно около 8 MiB расчётной памяти структуры; фиксированные контейнеры корней входят в базовую линию. Structure исключает Entity, которые удерживаются базовой линией; Total включает новые Entity без временных массивов. Пять отдельных процессов дают медиану и диапазон; отрицательные значения не обнуляются, отношения для неположительных значений недоступны. Измерение не включает стек, JIT, native memory и общий working set.

    Estimated Memory — аддитивная модель, откалиброванная allocation-пробами текущего runtime: shallow-размер объектов и размер массивов с заголовками и выравниванием. Для дерева: оболочка SortedDictionary, TreeSet, принадлежащий ему comparer и N узлов. Общие singleton-comparer исключены. Для Dictionary: оболочка, _buckets и _entries, включая свободную ёмкость. Это модель, а не обход удерживаемого heap. Reflection применяется только в диагностике. Зависимость от внутренних полей runtime проверяется; несовместимый runtime завершает диагностику ошибкой.

    BuildAllocatedBytes измеряет все аллокации построения структуры после прогрева; EntityAllocatedBytes — создание самих Entity. Временные массивы генератора и объектов запросов не относятся к структуре. Аллокации включают освобождаемые промежуточные массивы AutoGrow и не равны удерживаемой памяти. KiB = 1024 B. Построение словаря и дерева идёт в порядке сортировки ключей; удалений нет.
    """;
    private static void Plot(string path, string title, IEnumerable<(string Series, int N, double Value)> data)
    {
        var groups = data.Where(p => double.IsFinite(p.Value) && p.Value > 0).GroupBy(p => p.Series).ToArray();
        var all = groups.SelectMany(g => g).ToArray();
        if (all.Length == 0) throw new InvalidOperationException("No positive chart values.");
        double min = Math.Log10(all.Min(p => p.Value)), max = Math.Log10(all.Max(p => p.Value));
        if (max - min < 0.1) max = min + 0.1;
        string[] colors = ["#2563eb", "#dc2626", "#059669", "#9333ea", "#d97706", "#0891b2", "#be185d", "#475569", "#65a30d"];
        var svg = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1100\" height=\"450\" viewBox=\"0 0 1100 450\"><rect width=\"1100\" height=\"450\" fill=\"white\"/><g font-family=\"sans-serif\" font-size=\"12\">");
        svg.Append($"<text x=\"70\" y=\"24\">{title}</text><path d=\"M70 50 V370 H700\" stroke=\"black\" fill=\"none\"/>");
        for (int i = 0; i <= 4; i++) { double v = min + (max - min) * i / 4; svg.Append($"<text x=\"4\" y=\"{F(374 - i * 80)}\">{F(Math.Pow(10, v))}</text>"); }
        foreach (int n in DataSet.Sizes) svg.Append($"<text x=\"{F(70 + (Math.Log10(n) - 1) / 5 * 630)}\" y=\"392\">{n}</text>");
        svg.Append("<text x=\"350\" y=\"420\">Number of elements (log10)</text>");
        for (int i = 0; i < groups.Length; i++)
        {
            string color = colors[i % colors.Length];
            string points = string.Join(" ", groups[i].OrderBy(p => p.N).Select(p => F(70 + (Math.Log10(p.N) - 1) / 5 * 630) + "," + F(370 - (Math.Log10(p.Value) - min) / (max - min) * 320)));
            svg.Append($"<polyline points=\"{points}\" stroke=\"{color}\" fill=\"none\" stroke-width=\"2\"/><text x=\"720\" y=\"{60 + i * 25}\" fill=\"{color}\">{groups[i].Key}</text>");
        }
        svg.Append("</g></svg>"); File.WriteAllText(path, svg.ToString());
    }
}

