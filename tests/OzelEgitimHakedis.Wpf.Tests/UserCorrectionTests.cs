using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;
using Xunit;

namespace OzelEgitimHakedis.Wpf.Tests;

public sealed partial class OfficialDocumentOutputTests
{
    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(250, 100, 75, 40)]
    [InlineData(-50, 300, 0, 20)]
    public void HomeDifference_ShouldShowEnteredAmountsIndependentOfDiscounts(
        decimal difference, decimal discount, decimal guideDifference, decimal guideDiscount)
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            var input = new ProgressPaymentInput(10, 100, discount, difference, 100000, false, 0, 0, 0,
                GuidePriceDifference: guideDifference, GuideDiscountAmount: guideDiscount);
            var payment = CreatePayment(input);
            var route = new RouteData("01", "Hat", 100, ContractorTaxOrIdentityNumber: "12345678901");
            var rows = new[] { new MonthlyPaymentRow(route, null, null, payment, "Tamamlandı", Array.Empty<PaymentIssue>()) };
            InvokeEditor(window, "UpdateHomeFinanceCards", (object)rows);
            var expected = difference.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")) + " ₺";
            Assert.Equal(expected, EditorField<TextBlock>(window, "HomeDifferenceValue").Text);
            Assert.Equal(difference - discount, payment.Calculation.PriceDifferenceExcludingVat);
            Assert.Equal(payment.Calculation.NetPayable.ToString("N2", CultureInfo.GetCultureInfo("tr-TR")) + " ₺",
                EditorField<TextBlock>(window, "HomeNetValue").Text);
        });
    }

    [Theory]
    [InlineData(TransportationProgramType.SpecialEducation, 31)]
    [InlineData(TransportationProgramType.BasicOrSecondary, 20)]
    public void AcceptanceCompletion_ShouldUseSelectedPeriodEnd(TransportationProgramType program, int lastDay)
    {
        RunOnSta(() =>
        {
            var payment = CreatePayment(new ProgressPaymentInput(1, 100, 0, 0, 100000, true, 0, 0, 0, ProgramType: program));
            var context = CreateDocumentContext(payment) with
            {
                PeriodDate = new DateOnly(2026, 10, 31), ContractEndDate = new DateOnly(2026, 10, lastDay)
            };
            var page = (FixedPage)OehsOfficialDocumentFactory.BuildSeparate(context)
                .Single(o => o.Kind == OehsOfficialDocumentKind.KabulTutanagi).Document.Pages[0].Child;
            var text = GetCellTextAt(page, 360, 417);
            Assert.Equal(new DateOnly(2026, 10, lastDay).ToString("dd MMMM yyyy dddd", CultureInfo.GetCultureInfo("tr-TR")), text);
            // Earlier historical period must not use the later contract end.
            context = context with { ContractEndDate = new DateOnly(2027, 6, 30) };
            page = (FixedPage)OehsOfficialDocumentFactory.BuildSeparate(context)
                .Single(o => o.Kind == OehsOfficialDocumentKind.KabulTutanagi).Document.Pages[0].Child;
            Assert.Equal("31 Ekim 2026 Cumartesi", GetCellTextAt(page, 360, 417));
        });
    }

    [Theory]
    [InlineData(TransportationProgramType.SpecialEducation)]
    [InlineData(TransportationProgramType.BasicOrSecondary)]
    public void OfficialForms_ShouldFitLongServiceAndContractorInTheirSlots(TransportationProgramType program)
    {
        RunOnSta(() =>
        {
            const string route = "Çamlıca Mahallesi Yukarı Köyü Cumhuriyet İlkokulu ve Şehit Öğretmen Mehmet Akif Ersoy Özel Eğitim Uygulama Okulu Güzergâhı";
            const string contractor = "Sınırlı Sorumlu Yeşil Sivaslı Minibüsçüler Karayolu ile Yolcu Taşıyıcılar Kooperatifi";
            var payment = CreatePayment(new ProgressPaymentInput(1, 100, 0, 0, 100000, true, 0, 0, 0, ProgramType: program));
            var context = CreateDocumentContext(payment) with
            {
                ContractorName = contractor, RouteName = route, WorkName = OfficialTemplateProfile.DeriveWorkName(route)
            };
            var folder = Directory.CreateTempSubdirectory("oehs-corrections-");
            try
            {
                var paths = new List<string>();
                var exportPayment = payment with { ContractorName = contractor, RouteName = route, TenderNumber = "2026/1562223" };
                foreach (var output in OehsOfficialDocumentFactory.BuildSeparate(context))
                {
                    var path = OehsOutputPathService.BuildOfficialPdfFilePath(folder.FullName, exportPayment, output.FileSuffix);
                    paths.Add(path);
                    Assert.InRange(path.Length, 1, 240);
                    PdfVectorWriter.Save(output.Document, path);
                    using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
                    Assert.Single(pdf.GetPages());
                    if (output.Kind == OehsOfficialDocumentKind.HizmetIsleriHakedisRaporu)
                    {
                        Assert.Contains(WithoutWhitespace(context.WorkName),
                            WithoutWhitespace(PdfTextInRectangle(pdf.GetPage(1), new Rect(308, 151, 440, 86))), StringComparison.Ordinal);
                        var page = (FixedPage)output.Document.Pages[0].Child;
                        var block = page.Children.OfType<TextBlock>().Single(b => b.Text == contractor);
                        Assert.Equal(TextAlignment.Left, block.TextAlignment);
                        Assert.Equal(VerticalAlignment.Center, block.VerticalAlignment);
                        Assert.True(block.Height >= 35);
                    }
                    if (output.Kind == OehsOfficialDocumentKind.KabulTutanagi)
                    {
                        var page = (FixedPage)output.Document.Pages[0].Child;
                        var block = (TextBlock)page.Children.OfType<Border>()
                            .Single(b => FixedPage.GetLeft(b) == 360 && FixedPage.GetTop(b) == 172).Child;
                        Assert.Equal(TextAlignment.Left, block.TextAlignment);
                        Assert.Equal(VerticalAlignment.Center, block.VerticalAlignment);
                    }
                    var evidence = Environment.GetEnvironmentVariable("OEHS_CORRECTION_QA_OUTPUT");
                    if (!string.IsNullOrWhiteSpace(evidence))
                    {
                        Directory.CreateDirectory(evidence);
                        File.Copy(path, Path.Combine(evidence, program + "_" + output.FileSuffix + ".pdf"), true);
                    }
                }
                var savedFolder = (string)typeof(MainWindow).GetMethod("SaveRouteDocumentsArchiveMetadata",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .Invoke(null, new object[] { exportPayment, paths })!;
                Assert.Equal(Path.GetDirectoryName(paths[0]), savedFolder);
                var metadata = Assert.Single(Directory.GetFiles(savedFolder, "OEHS_PDF_Bilgi*.json"));
                using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(metadata));
                Assert.Equal(4, json.RootElement.GetProperty("PdfTargetPaths").GetArrayLength());
                Assert.All(paths, path => Assert.True(File.Exists(path)));
            }
            finally { folder.Delete(true); }
        });
    }
}

public sealed partial class HakedisCreateHybridUiContractTests
{
    [Fact]
    public void HomePeriod_ShouldRenderDisplayTextWhenCollapsed()
    {
        RunOnSta(() =>
        {
            var layout = LoadLayoutFragment(NamedElement(LoadMainWindowXaml(), "HomePanel"));
            var combo = (ComboBox)layout.FindName("HomePeriodMonthComboBox");
            var period = new ProgressPaymentMonthOption(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31));
            combo.ItemsSource = new[] { period };
            combo.SelectedIndex = 0;
            ArrangeLayout(layout, 1100, 800);
            Assert.Contains(VisualDescendants<TextBlock>(combo), b => b.Text == period.DisplayText);
            Assert.DoesNotContain(VisualDescendants<TextBlock>(combo), b => b.Text.Contains("ProgressPaymentMonthOption", StringComparison.Ordinal));
        });
    }
}
