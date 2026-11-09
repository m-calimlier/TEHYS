using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;
using Xunit;

namespace OzelEgitimHakedis.Wpf.Tests;

public sealed partial class HakedisCreateHybridUiContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutputQueries_RequireMatchingTenderAndRoute(bool wrongTender)
    {
        RunOnSta(() =>
        {
            var window = PrepareOutputPanel();
            if (wrongTender)
            {
                var combo = Field<ComboBox>(window, "OutputTenderListBox");
                combo.Items.Add(ArchiveProgressEditorContextTests.CreateArchive().Contract with { TenderNumber = "OTHER" }); combo.SelectedIndex = 1;
            }
            else Field<ComboBox>(window, "ReportRouteComboBox").SelectedItem = null;
            Assert.Null(Invoke(window, "GetSelectedRouteLatestPayment"));
            Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(Invoke(window, "GetSelectedRoutePayments")).Cast<object>());
        });
    }

    [Fact]
    public void OutputSummary_WithoutRouteMustNotShowGlobalHistory()
    {
        RunOnSta(() =>
        {
            var window = PrepareOutputPanel(); Field<ComboBox>(window, "ReportRouteComboBox").SelectedItem = null;
            Invoke(window, "UpdateReportView");
            Assert.Equal("0", Field<TextBlock>(window, "ReportCountTextBlock").Text);
            Assert.Empty(Collection(window, "_reportRows"));
            Assert.False(Field<Button>(window, "SaveReportsPdfsButton").IsEnabled);
            Assert.False(Field<Button>(window, "PrintReportsButton").IsEnabled);
        });
    }

    [Fact]
    public void OutputSummary_SeparatesSameRouteIdentityAcrossPrograms()
    {
        RunOnSta(() =>
        {
            var window = PrepareOutputPanel();
            var payment = (ProgressPaymentHistoryData)Collection(window, "_progressHistory")[0]!;
            Collection(window, "_progressHistory").Add(payment with { Id = Guid.NewGuid(), Input = payment.Input with { ProgramType = TransportationProgramType.BasicOrSecondary } });
            Invoke(window, "UpdateReportView");
            Assert.Single(Collection(window, "_reportRows").Cast<object>());
            Assert.Equal("1", Field<TextBlock>(window, "ReportCountTextBlock").Text);
        });
    }

    [Fact]
    public void OutputTarget_UsesSavedPaymentSnapshotLikePdfWriter()
    {
        RunOnSta(() =>
        {
            var window = PrepareOutputPanel(); var payment = (ProgressPaymentHistoryData)Collection(window, "_progressHistory")[0]!;
            Invoke(window, "UpdateReportView");
            var target = OehsOutputPathService.BuildOfficialPdfDirectory(OehsRuntimePaths.DesktopRoot, payment);
            Assert.Contains(target, Field<TextBlock>(window, "ReportPdfTargetTextBlock").Text, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static MainWindow PrepareOutputPanel()
    {
        var window = CreateEditorHarness(); var contract = ArchiveProgressEditorContextTests.CreateArchive().Contract;
        Collection(window, "_contracts").Add(contract);
        var route = CreateRoute("01", "Merkez İlkokulu", "12345678901"); Collection(window, "_routes").Add(route);
        Collection(window, "_contractors").Add(CreateFirm("Güncel firma", "12345678901"));
        var tender = Field<ComboBox>(window, "OutputTenderListBox"); tender.Items.Add(contract); tender.SelectedItem = contract;
        var routes = Field<ComboBox>(window, "ReportRouteComboBox"); routes.Items.Add(route); routes.SelectedItem = route;
        Collection(window, "_progressHistory").Add(CreateOutputPayment(new DateOnly(2026, 9, 30), 1, 1000m));
        return window;
    }

    private static ProgressPaymentHistoryData CreateOutputPayment(DateOnly period, int number, decimal dailyPrice)
    {
        var input = new ProgressPaymentInput(20m, dailyPrice, 0m, 0m, 100000m, false, 0m, 0m, 0m);
        return new(Guid.NewGuid(), DateTime.UtcNow, input, ProgressPaymentCalculator.Calculate(input), number, period,
            "01", "Merkez İlkokulu", ContractorName: "Kayıtlı firma", TenderNumber: "IHALE-01", ContractNumber: "SOZ-01",
            ContractDate: new DateOnly(2026, 1, 1), ContractorTaxOrIdentityNumber: "12345678901");
    }

    [Theory]
    [InlineData(TransportationProgramType.SpecialEducation)]
    [InlineData(TransportationProgramType.BasicOrSecondary)]
    public void OutputSelection_OlderPeriodControlsSummaryAndTarget(TransportationProgramType program)
    {
        RunOnSta(() =>
        {
            var window = PrepareOutputPanel();
            var contract = ((ContractDraftData)Collection(window, "_contracts")[0]!) with { ProgramType = program };
            Collection(window, "_contracts")[0] = contract;
            var route = CreateRoute("01", "Merkez İlkokulu", "12345678901", program); Collection(window, "_routes")[0] = route;
            Collection(window, "_contractors")[0] = CreatePanelFirm("Firma", "12345678901", program, "", false);
            var tender = Field<ComboBox>(window, "OutputTenderListBox"); tender.Items.Clear(); tender.Items.Add(contract); tender.SelectedItem = contract;
            var routes = Field<ComboBox>(window, "ReportRouteComboBox"); routes.Items.Clear(); routes.Items.Add(route); routes.SelectedItem = route;
            var old = WithOutputProgram(CreateOutputPayment(new DateOnly(2026, 9, 30), 1, 1000m), program);
            var recent = WithOutputProgram(CreateOutputPayment(new DateOnly(2026, 10, 31), 2, 2000m), program);
            var history = Collection(window, "_progressHistory"); history.Clear(); history.Add(old); history.Add(recent);
            Invoke(window, "UpdateReportView");
            Assert.Same(recent, Invoke(window, "GetSelectedOutputPayment"));
            SelectOutputPayment(window, old);
            Assert.Same(old, Invoke(window, "GetSelectedOutputPayment"));
            var context = Assert.IsType<OehsOfficialDocumentContext>(Invoke(window, "BuildSelectedOfficialDocumentContext"));
            var icmal = OehsOfficialDocumentFactory.BuildSeparate(context).Single(output => output.Kind == OehsOfficialDocumentKind.HakedisIcmali);
            var page = Assert.IsType<System.Windows.Documents.FixedPage>(icmal.Document.Pages[0].Child);
            var text = string.Join("\n", VisualDescendants<TextBlock>(page).Select(block => block.Text));
            Assert.Contains("Eylül 2026", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Ekim 2026", text, StringComparison.Ordinal);
            Assert.Contains("₺20.000,00", text, StringComparison.Ordinal);
            Assert.DoesNotContain("₺60.000,00", text, StringComparison.Ordinal);
            Assert.Contains("Eylül", Field<TextBlock>(window, "ReportDetailTitleTextBlock").Text, StringComparison.Ordinal);
            Assert.Equal(old.Calculation.NetPayable.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")) + " ₺", Field<TextBlock>(window, "ReportNetTextBlock").Text);
            Assert.True(Field<Button>(window, "SaveReportsPdfsButton").IsEnabled);
            history.Add(WithOutputProgram(CreateOutputPayment(new DateOnly(2026, 11, 30), 3, 3000m), program));
            Invoke(window, "UpdateReportView");
            Assert.Same(old, Invoke(window, "GetSelectedOutputPayment"));
        });
    }

    [Fact]
    public void OutputSelection_ChangedPaymentRequiresRecheckingBeforeExport()
    {
        RunOnSta(() =>
        {
            var window = PrepareOutputPanel(); Invoke(window, "UpdateReportView");
            var original = (ProgressPaymentHistoryData)Collection(window, "_progressHistory")[0]!;
            var updated = CreateOutputPayment(original.PeriodDate!.Value, 1, 2000m) with { Id = original.Id };
            Collection(window, "_progressHistory")[0] = updated;
            Assert.Null(Invoke(window, "GetSelectedOutputPayment"));
            Assert.False(Assert.IsType<bool>(Invoke(window, "EnsureRouteForOutput")));
            Assert.Same(updated, Invoke(window, "GetSelectedOutputPayment"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutputSelection_IncompleteRecordDisablesDocumentActions(bool missingFirm)
    {
        RunOnSta(() =>
        {
            var window = PrepareOutputPanel();
            if (missingFirm) Collection(window, "_contractors").Clear();
            else Collection(window, "_progressHistory")[0] = ((ProgressPaymentHistoryData)Collection(window, "_progressHistory")[0]!) with { PeriodDate = null };
            Invoke(window, "UpdateReportView");
            Assert.False(Field<Button>(window, "SaveReportsPdfsButton").IsEnabled);
            Assert.False(Field<Button>(window, "PrintReportsButton").IsEnabled);
            Assert.Contains(missingFirm ? "Yüklenici" : "dönemi", Field<TextBlock>(window, "ReportReadinessTextBlock").Text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void OutputSelection_ClearDoesNotSilentlyChooseLatestRecord()
    {
        RunOnSta(() =>
        {
            var window = PrepareOutputPanel(); Invoke(window, "UpdateReportView");
            Field<ListBox>(window, "ReportItemsControl").SelectedItem = null;
            Invoke(window, "RefreshOutputSelection", true);
            Assert.Null(Invoke(window, "GetSelectedOutputPayment"));
            Assert.False(Field<Button>(window, "SaveReportsPdfsButton").IsEnabled);
        });
    }

    [Theory]
    [InlineData(1024, 600)]
    [InlineData(1024, 768)]
    [InlineData(1366, 600)]
    [InlineData(1366, 768)]
    [InlineData(1920, 1080)]
    public void OutputPanel_SelectorsAndActionBarFitSupportedViewports(double width, double height)
    {
        RunOnSta(() =>
        {
            var layout = LoadLayoutFragment(NamedElement(LoadMainWindowXaml(), "ReportsPanel")); layout.Background = Brushes.White;
            var contract = ArchiveProgressEditorContextTests.CreateArchive().Contract;
            var tender = (ComboBox)layout.FindName("OutputTenderListBox"); tender.Items.Add(contract); tender.SelectedItem = contract;
            var routes = (ComboBox)layout.FindName("ReportRouteComboBox"); var route = CreateRoute("01", "Merkez ve çevre köylerden Atatürk İlkokuluna taşıma hattı", "12345678901"); routes.Items.Add(route); routes.SelectedItem = route;
            var model = PrepareOutputPanel(); Collection(model, "_progressHistory").Add(CreateOutputPayment(new DateOnly(2026, 10, 31), 2, 2000m)); Invoke(model, "UpdateReportView");
            var records = (ListBox)layout.FindName("ReportItemsControl"); records.ItemsSource = Collection(model, "_reportRows"); records.SelectedIndex = 0;
            ((TextBlock)layout.FindName("ReportCountTextBlock")).Text = "2";
            ((TextBlock)layout.FindName("ReportGrossTextBlock")).Text = "44.000,00 ₺";
            ((TextBlock)layout.FindName("ReportNetTextBlock")).Text = "41.620,80 ₺";
            ((TextBlock)layout.FindName("ReportDetailTitleTextBlock")).Text = "Ekim 2026 • Hakediş No 2";
            ((TextBlock)layout.FindName("ReportDetailMetaTextBlock")).Text = "Özel Eğitim\nYüklenici: Örnek Öğrenci Taşımacılığı Limited Şirketi\nHat 01: Merkez İlkokulu";
            ((TextBlock)layout.FindName("ReportReadinessTextBlock")).Text = "Seçili kayıt için dört belge hazır.";
            ((Border)layout.FindName("ReportEmptyStateBorder")).Visibility = Visibility.Collapsed;
            ArrangeLayout(layout, width - 253, height - 98);
            Assert.True(tender.ActualWidth >= 200); Assert.Equal(tender.ActualHeight, routes.ActualHeight);
            foreach (var button in VisualDescendants<Button>(layout).Where(button => button.Name is "SaveReportsPdfsButton" or "PrintReportsButton" or "OpenReportsFolderButton"))
            {
                var bounds = button.TransformToAncestor(layout).TransformBounds(new Rect(button.RenderSize));
                Assert.InRange(bounds.Bottom, 1, layout.ActualHeight + 1); Assert.InRange(bounds.Right, 1, layout.ActualWidth + 1);
                Assert.True(button.ActualHeight >= 36);
            }
            SavePaymentDetailsPreview(layout, "output-" + width.ToString("0", CultureInfo.InvariantCulture) + "x" + height.ToString("0", CultureInfo.InvariantCulture) + ".png");
        });
    }

    private static ProgressPaymentHistoryData WithOutputProgram(ProgressPaymentHistoryData payment, TransportationProgramType program)
    {
        var input = payment.Input with { ProgramType = program };
        return payment with { Input = input, Calculation = ProgressPaymentCalculator.Calculate(input) };
    }

    private static void SelectOutputPayment(MainWindow window, ProgressPaymentHistoryData payment)
    {
        var row = Collection(window, "_reportRows").Cast<object>().Single(item => ReferenceEquals(item.GetType().GetProperty("Payment")!.GetValue(item), payment));
        Field<ListBox>(window, "ReportItemsControl").SelectedItem = row;
        Invoke(window, "RefreshOutputSelection", true);
    }
}
