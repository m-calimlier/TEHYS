using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;
using OzelEgitimHakedis.Wpf;
using Xunit;

namespace OzelEgitimHakedis.Wpf.Tests;

public sealed partial class HakedisCreateHybridUiContractTests
{
    [Fact]
    public void PayrollSamples_HybridRowsAndFormInputsPreserveEveryExcelAmount()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(LoadProjectFile("tests", "OzelEgitimHakedis.Tests", "Fixtures", "payroll-2026-10-26.json")));
        RunOnSta(() =>
        {
            var saved = new List<ProgressPaymentHistoryData>();
            var parse = typeof(MainWindow).GetMethod("TryParseRequiredMoney", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            foreach (var sample in fixture.RootElement.GetProperty("Samples").EnumerateArray())
            {
                var input = sample.GetProperty("Input").Deserialize<ProgressPaymentInput>()!;
                var row = new MainWindow.MultiRoutePaymentDraftRow();
                row.Configure(input.ProgramType, input.ContractAmount, input.IsVatExempt, ProgressPaymentCalculationConstants.Defaults);
                row.LoadInput(input, true);
                Assert.Equal(input, row.CreateInput());
                foreach (var amount in new[] { input.DailyPrice, input.PriceDifference, input.GuideDailyPrice, input.GuidePriceDifference })
                {
                    object?[] args = [amount.ToString("0.####################", System.Globalization.CultureInfo.GetCultureInfo("tr-TR")), 0m];
                    Assert.True((bool)parse.Invoke(null, args)!);
                    Assert.Equal(amount, args[1]);
                }
                var actual = row.Preview!;
                saved.Add(new(Guid.NewGuid(), DateTime.Now, row.CreateInput(), actual,
                    PeriodDate: new DateOnly(2026, 10, 31), RouteNumber: saved.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), TenderNumber: "QA-IKN"));
                var net = sample.GetProperty("Expected").EnumerateArray().Single(c => c.GetProperty("Property").GetString() == "NetPayable").GetProperty("Value").GetDecimal();
                Assert.True(Math.Abs(net - actual.NetPayable) < 0.0000001m);
            }
            var temp = Directory.CreateTempSubdirectory("oehs-payroll-acceptance-");
            try
            {
                var store = new OehsProgramScopedApplicationDataStore(temp.FullName);
                store.Save(OehsApplicationData.Empty with { ProgressPayments = saved });
                var restored = store.Load().ProgressPayments!;
                Assert.Equal(42, restored.Count);
                foreach (var before in saved)
                {
                    var after = restored.Single(p => p.Id == before.Id);
                    Assert.Equal(before.Input, after.Input);
                    Assert.Equal(before.Calculation, after.Calculation);
                }
            }
            finally { temp.Delete(true); }
        });
    }

    [Fact]
    public void SavedHistoryGrid_UsesBottomActionsOnlyForSelectedRowAndWrapsLongDetails()
    {
        RunOnSta(() =>
        {
            var document = LoadMainWindowXaml();
            var source = NamedElement(document, "HistoryPanel");
            var actions = NamedElement(document, "HistoryActionsPanel");
            var events = actions.Descendants().Where(e => e.Name.LocalName == "Button").Select(e => (string?)e.Attribute("Click")).ToArray();
            Assert.Contains("EditHistoryButton_Click", events);
            Assert.Contains("DeleteHistoryButton_Click", events);
            Assert.Contains("ViewHistoryCalculationButton_Click", events);
            var layout = LoadLayoutFragment(source);
            var grid = (DataGrid)layout.FindName("HistoryItemsControl");
            ((Border)layout.FindName("HistoryEmptyStateBorder")).Visibility = Visibility.Collapsed;
            var entries = Enumerable.Range(1, 6).Select(i => new { Id = Guid.NewGuid(), DisplayTitle = $"Hat {i} · Çok uzun güzergâh bilgisi ve hakediş numarası ile ihale kayıt numarası 2026/1592223", CreatedAtText = "Dönem Ekim 2026 · Yüklenici: Uzun unvanlı taşıma kooperatifi · Kayıt 26.10.2026 20:00", TotalAmountText = "20.417,33 TL", NetPayableText = "21.194,13 TL" }).ToArray();
            grid.ItemsSource = entries;
            ArrangeLayout(layout, 740, 850);
            Assert.True(grid.IsReadOnly);
            Assert.DoesNotContain(VisualDescendants<Button>(grid), b => b.Content is string s && s == "Hesaplamayı Görüntüle");
            var panel = (WrapPanel)layout.FindName("HistoryActionsPanel");
            var buttons = panel.Children.OfType<Button>().ToArray();
            Assert.Equal(4, buttons.Length);
            Assert.All(buttons, b => Assert.False(b.IsEnabled));
            grid.SelectedItem = entries[2]; layout.UpdateLayout();
            Assert.All(buttons, b => { Assert.True(b.IsEnabled); Assert.Same(entries[2], b.Tag); });
            foreach (var button in buttons)
            {
                var bounds = button.TransformToAncestor(panel).TransformBounds(new Rect(button.RenderSize));
                Assert.InRange(bounds.Right, 1, panel.ActualWidth);
            }
            var cell = (TextBlock)grid.Columns[0].GetCellContent(entries[0]);
            Assert.Equal(TextWrapping.Wrap, cell.TextWrapping);
            Assert.Equal(TextTrimming.None, cell.TextTrimming);
            Assert.True(cell.ActualHeight > cell.FontSize * 2);
            grid.SelectedItem = null; layout.UpdateLayout();
            Assert.All(buttons, b => { Assert.False(b.IsEnabled); Assert.Null(b.Tag); });
            SavePaymentDetailsPreview(layout, "history-actions.png");
        });
    }

    [Fact]
    public void CalculationDetailsWindow_ShowsSavedCalculationAndReachableFinalRows()
    {
        RunOnSta(() =>
        {
            var input = new ProgressPaymentInput(8m, 2446.9323m, 0m, 841.87m, 100000m, false, 0m, 0m, 0m);
            var payment = new ProgressPaymentHistoryData(Guid.NewGuid(), DateTime.Now, input, ProgressPaymentCalculator.Calculate(input), 1,
                new DateOnly(2026, 10, 31), "06", "Deneme taşıma hattı", ContractorName: "Deneme yüklenicisi", TenderNumber: "2026/TEST");
            var window = new PaymentCalculationDetailsWindow(payment, [payment]) { ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout();
                var grid = Assert.Single(VisualDescendants<DataGrid>(window));
                Assert.True(grid.IsReadOnly);
                Assert.Equal(4, grid.Columns.Count);
                var rows = grid.Items.Cast<PaymentCalculationDetail>().ToArray();
                Assert.Contains(rows, r => r.Name == "PDF net ödeme");
                grid.ScrollIntoView(rows.Last()); window.UpdateLayout();
                Assert.NotNull(grid.ItemContainerGenerator.ContainerFromItem(rows.Last()));
                grid.ScrollIntoView(rows.First()); window.UpdateLayout();
                SavePaymentDetailsPreview(window, "calculation-details.png");
            }
            finally { window.Close(); }
        });
    }

    private static void SavePaymentDetailsPreview(FrameworkElement element, string name)
    {
        var directory = Path.Combine(Path.GetDirectoryName(LoadProjectFile("TEHYS.sln"))!, ".superpowers", "payment-details-2026-10-26");
        Directory.CreateDirectory(directory);
        var image = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
    }
}
