using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;
using OzelEgitimHakedis.Wpf;

namespace Tehys.Performance;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Provide a new output directory.");
        var output = Path.GetFullPath(args[0]);
        if (Directory.Exists(output)) throw new IOException("Output already exists; benchmark does not overwrite runs.");
        Directory.CreateDirectory(output);
        // Every store and PDF path is scoped to this new run, never to application/user storage.
        var results = new List<Measurement>();
        foreach (var count in new[] { 100, 1000, 10000 })
        {
            var folder = Path.Combine(output, count.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            var input = new ProgressPaymentInput(22, 1250, 0, 100, 1000000, false, 0, 0, 0);
            var calculation = ProgressPaymentCalculator.Calculate(input);
            var payments = Enumerable.Range(0, count).Select(i => new ProgressPaymentHistoryData(
                Guid.NewGuid(), new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc).AddSeconds(i), input,
                calculation, i / 100 + 1, new DateOnly(2026, 9, 30), (i % 100 + 1).ToString(CultureInfo.InvariantCulture),
                "Örnek hat " + (i % 100 + 1), "Örnek taşıma işi", "Örnek Yüklenici", "2026/1", "SOZ-1",
                ContractorTaxOrIdentityNumber: "12345678901")).ToArray();
            var data = OehsApplicationData.Empty with { ProgressPayments = payments };
            var storePath = Path.Combine(folder, "synthetic-data.json");
            var store = new OehsApplicationDataStore(storePath);
            var save = Measure(() => store.Save(data));
            OehsApplicationData loaded = data;
            var load = Median(() => loaded = store.Load());
            ProgressPaymentHistoryData[] list = Array.Empty<ProgressPaymentHistoryData>();
            var listing = Median(() => list = loaded.ProgressPayments!.OrderByDescending(p => p.PeriodDate)
                .ThenByDescending(p => p.CreatedAt).ToArray());
            ProgressPaymentHistoryData[] filtered = Array.Empty<ProgressPaymentHistoryData>();
            var filter = Median(() => filtered = list.Where(p => p.RouteNumber == "1").ToArray());
            var latest = filtered.OrderBy(p => p.CreatedAt).Last();
            ProgressPaymentReportTotals totals = ProgressPaymentReportService.Calculate(latest, filtered);
            var reporting = Median(() => totals = ProgressPaymentReportService.Calculate(latest, filtered));
            var context = new OehsOfficialDocumentContext("Örnek Hat Hattı Taşıma İşi", "2026/1", "1",
                "Örnek Yüklenici", "SOZ-1", 1000000, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15),
                new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 25), 181, latest.PeriodDate,
                latest.ProgressPaymentNumber, latest.Calculation, filtered, totals,
                ProgressPaymentReportService.CalculateReportNetPayable(latest, totals), 0, new DateOnly(2026, 11, 4));
            var pdfPath = Path.Combine(folder, "four-pages.pdf");
            // Includes real FixedDocument construction, layout and four-page vector PDF writing.
            var pdf = Median(() => PdfVectorWriter.Save(OehsOfficialDocumentFactory.BuildCombined(context), pdfPath));
            results.Add(new(count, filtered.Length, new FileInfo(storePath).Length, save, load, listing, filter, reporting, pdf));
            Console.WriteLine($"{count}: load {load:F2} ms, list {listing:F2} ms, filter {filter:F2} ms, PDF {pdf:F2} ms");
        }
        var report = new { GeneratedAtUtc = DateTime.UtcNow, Runtime = Environment.Version.ToString(),
            OS = Environment.OSVersion.VersionString, LogicalProcessors = Environment.ProcessorCount,
            Method = "Synthetic isolated records; median of 3 runs for load/list/filter/report/PDF; first save measured once. PDF uses selected route history. Warm process; excludes real MainWindow startup, disk archive enumeration and interactive grid rendering.", Results = results };
        File.WriteAllText(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        var csv = new StringBuilder("records,selected_route_records,data_bytes,save_ms,load_ms,list_ms,filter_ms,report_ms,pdf_ms\n");
        foreach (var row in results) csv.AppendLine(string.Join(",", row.Records, row.SelectedRouteRecords, row.DataBytes,
            F(row.SaveMs), F(row.LoadMs), F(row.ListMs), F(row.FilterMs), F(row.ReportMs), F(row.PdfMs)));
        File.WriteAllText(Path.Combine(output, "measurements.csv"), csv.ToString(), Encoding.UTF8);
    }

    private static string F(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
    private static double Measure(Action action) { var timer = Stopwatch.StartNew(); action(); return timer.Elapsed.TotalMilliseconds; }
    private static double Median(Action action) { var samples = new[] { Measure(action), Measure(action), Measure(action) }; Array.Sort(samples); return samples[1]; }
    private sealed record Measurement(int Records, int SelectedRouteRecords, long DataBytes, double SaveMs,
        double LoadMs, double ListMs, double FilterMs, double ReportMs, double PdfMs);
}
