using System.Text.Json;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;
using Xunit;

namespace OzelEgitimHakedis.Tests;

public sealed class PayrollWorkbookAcceptanceTests
{
    [Fact]
    public void AllPayrollRows_MatchExcelAmounts_WithBlankAndAlternativeInvoiceDisplayReportedSeparately()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TEHYS.sln"))) root = root.Parent;
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!.FullName,
            "tests/OzelEgitimHakedis.Tests/Fixtures/payroll-2026-10-26.json")));
        var matched = 0;
        var invoiceVariants = 0;
        var missingInvoiceReferences = 0;
        var netTotals = new Dictionary<(string File, TransportationProgramType Program), decimal>();
        foreach (var sample in fixture.RootElement.GetProperty("Samples").EnumerateArray())
        {
            var input = sample.GetProperty("Input").Deserialize<ProgressPaymentInput>()!;
            var calculation = ProgressPaymentCalculator.Calculate(input);
            var key = (sample.GetProperty("File").GetString()!, input.ProgramType);
            netTotals[key] = netTotals.GetValueOrDefault(key) + calculation.NetPayable;
            foreach (var cell in sample.GetProperty("Expected").EnumerateArray())
            {
                var actual = (decimal)typeof(ProgressPaymentCalculation).GetProperty(cell.GetProperty("Property").GetString()!)!.GetValue(calculation)!;
                var expected = cell.GetProperty("Value").GetDecimal();
                decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
                var scope = $"{sample.GetProperty("File")} / {sample.GetProperty("Sheet")}!{cell.GetProperty("Cell")}";
                if (cell.GetProperty("ReferenceMissing").GetBoolean())
                {
                    Assert.Equal(JsonValueKind.Null, cell.GetProperty("Formula").ValueKind);
                    missingInvoiceReferences++;
                }
                else if (cell.GetProperty("SourceInvoiceVariant").GetBoolean())
                {
                    // The workbook presents the invoice base without adding withholding.
                    // This is a source presentation variant, not a missing withholding formula.
                    Assert.Equal(Round(calculation.TotalAmount), Round(expected));
                    Assert.NotEqual(Round(expected), Round(actual));
                    invoiceVariants++;
                }
                else
                {
                    Assert.True(Round(expected) == Round(actual), $"{scope}: Excel={expected}, program={actual}");
                    // Also compare unrounded intermediates: allow only Excel binary floating-point noise.
                    Assert.True(Math.Abs(expected - actual) < 0.0000001m, scope);
                    matched++;
                }
            }
        }
        Assert.Equal(42, fixture.RootElement.GetProperty("Samples").GetArrayLength());
        Assert.Equal(422, matched);
        Assert.Equal(2, invoiceVariants);
        Assert.Equal(2, missingInvoiceReferences);
        Assert.Equal(4, netTotals.Count);
        foreach (var ((_, program), net) in netTotals)
        {
            var expectedNet = program == TransportationProgramType.BasicOrSecondary ? 452706.15335656m : 154815.542094912m;
            Assert.True(Math.Abs(expectedNet - net) < 0.0000001m);
        }
    }
}
