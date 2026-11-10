using System.Runtime.ExceptionServices;
using System.Threading;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using System.Linq;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;
using PdfSharp.Pdf.IO;
using OzelEgitimHakedis.Wpf;
using Xunit;

namespace OzelEgitimHakedis.Wpf.Tests;

public sealed partial class OfficialDocumentOutputTests
{
    [Fact]
    public void OfficialPdf_ShouldOmitIknAndInternalContractIdFromServiceAndAcceptanceForms()
    {
        RunOnSta(() =>
        {
            var context=CreateDocumentContext(CreatePayment(new ProgressPaymentInput(1,100,0,0,100000,true,0,0,0))) with
                {ContractNumber="INTERNAL-private-key",TenderNumber="2026/4242"};
            var temporary=Directory.CreateTempSubdirectory("oehs-ikn-only-");
            try
            {
                foreach(var output in OehsOfficialDocumentFactory.BuildSeparate(context).Where(o=>o.Kind is OehsOfficialDocumentKind.HizmetIsleriHakedisRaporu or OehsOfficialDocumentKind.KabulTutanagi))
                {
                    var path=Path.Combine(temporary.FullName,output.FileSuffix+".pdf");PdfVectorWriter.Save(output.Document,path);
                    using var pdf=UglyToad.PdfPig.PdfDocument.Open(path);
                    Assert.DoesNotContain("2026/4242",pdf.GetPage(1).Text,StringComparison.Ordinal);
                    Assert.DoesNotContain("INTERNAL-",pdf.GetPage(1).Text,StringComparison.Ordinal);
                }
            }
            finally {temporary.Delete(true);}
        });
    }
    [Theory]
    [InlineData(TransportationProgramType.SpecialEducation)]
    [InlineData(TransportationProgramType.BasicOrSecondary)]
    public void OfficialPdfs_ShouldPrintOnlyDateInContractRows(TransportationProgramType program)
    {
        RunOnSta(() =>
        {
            var context = CreateDocumentContext(CreatePayment(new ProgressPaymentInput(
                16m, 19539.1608m, 0m, 7365.35m, 100000m, false, 0m, 0m, 0m, ProgramType: program))) with
            {
                TenderNumber = "2026/1009876",
                ContractNumber = "SOZ-2026-0042"
            };
            var temporaryDirectory = Directory.CreateTempSubdirectory("oehs-pdf-identifiers-");
            try
            {
                foreach (var output in OehsOfficialDocumentFactory.BuildSeparate(context)
                             .Where(output => output.Kind is OehsOfficialDocumentKind.HizmetIsleriHakedisRaporu
                                 or OehsOfficialDocumentKind.KabulTutanagi))
                {
                    var path = Path.Combine(temporaryDirectory.FullName, output.FileSuffix + ".pdf");
                    SaveAndAssertA4Pdf(output.Document, path);
                    using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
                    var page = pdf.GetPage(1);
                    var row = output.Kind == OehsOfficialDocumentKind.HizmetIsleriHakedisRaporu
                        ? new Rect(398, 312, 297, 22)
                        : new Rect(360, 212.6, 383, 34.6);
                    var contractText = PdfTextInRectangle(page, row);
                    Assert.Contains("15.08.2026", contractText, StringComparison.Ordinal);
                    Assert.DoesNotContain("SOZ-2026-0042", contractText, StringComparison.Ordinal);
                    Assert.DoesNotContain("İKN", contractText, StringComparison.Ordinal);
                    Assert.DoesNotContain("2026/1009876", page.Text, StringComparison.Ordinal);
                }
            }
            finally
            {
                temporaryDirectory.Delete(recursive: true);
            }
        });
    }

    [Theory]
    [InlineData(TransportationProgramType.SpecialEducation)]
    [InlineData(TransportationProgramType.BasicOrSecondary)]
    public void OfficialPdfs_ShouldRetainCompleteLongButFittingContractorAndRouteNames(TransportationProgramType program)
    {
        RunOnSta(() =>
        {
            const string contractor = "ÖĞRENCİ TAŞIMACILIĞI TURİZM GIDA İNŞAAT SANAYİ VE TİCARET LİMİTED ŞİRKETİ";
            const string route = "Yukarı Mahalle Cumhuriyet İlkokulu Çevre Yolu Merkez";
            var context = CreateDocumentContext(CreatePayment(new ProgressPaymentInput(
                1m, 100m, 0m, 0m, 100000m, false, 0m, 0m, 0m, ProgramType: program))) with
            {
                ContractorName = contractor, RouteName = route
            };
            var temporaryDirectory = Directory.CreateTempSubdirectory("oehs-pdf-long-fields-");
            try
            {
                foreach (var output in OehsOfficialDocumentFactory.BuildSeparate(context))
                {
                    var path = Path.Combine(temporaryDirectory.FullName, output.FileSuffix + ".pdf");
                    SaveAndAssertA4Pdf(output.Document, path);
                    using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
                    var text = WithoutWhitespace(pdf.GetPage(1).Text);
                    Assert.Contains(WithoutWhitespace(route), text, StringComparison.Ordinal);
                    if (output.Kind != OehsOfficialDocumentKind.HakedisIcmali)
                        Assert.Contains(WithoutWhitespace(contractor), text, StringComparison.Ordinal);
                }
            }
            finally
            {
                temporaryDirectory.Delete(recursive: true);
            }
        });
    }

    [Theory]
    [InlineData("contractor")]
    [InlineData("route")]
    public void OfficialPdfs_ShouldRejectUnrepresentableLongFieldsWithoutWritingTruncatedFiles(string field)
    {
        RunOnSta(() =>
        {
            var context = CreateDocumentContext(CreatePayment(new ProgressPaymentInput(
                1m, 100m, 0m, 0m, 100000m, false, 0m, 0m, 0m)));
            var longValue = string.Join(" ", Enumerable.Repeat("UZUNALAN Öğrenci Taşımacılığı", 100)) + " SONALAN";
            context = field == "contractor"
                ? context with { ContractorName = longValue }
                : context with { RouteName = longValue };
            var temporaryDirectory = Directory.CreateTempSubdirectory("oehs-pdf-overflow-");
            try
            {
                foreach (var output in OehsOfficialDocumentFactory.BuildSeparate(context)
                             .Where(output => field == "route" || output.Kind != OehsOfficialDocumentKind.HakedisIcmali))
                {
                    var path = Path.Combine(temporaryDirectory.FullName, output.FileSuffix + ".pdf");
                    var exception = Assert.Throws<PdfDocumentLayoutException>(() => PdfVectorWriter.Save(output.Document, path));
                    Assert.Contains("PDF", exception.Message, StringComparison.Ordinal);
                    Assert.Contains("sığmıyor", exception.Message, StringComparison.Ordinal);
                    Assert.Contains("UZUNALAN", exception.Message, StringComparison.Ordinal);
                    Assert.False(File.Exists(path), "A failed layout must not publish a truncated PDF.");
                }
            }
            finally
            {
                temporaryDirectory.Delete(recursive: true);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PdfWriter_ShouldKeepWrappedTextInsideItsLayoutSlot(bool singleLongWord)
    {
        RunOnSta(() =>
        {
            var text = singleLongWord
                ? "UZUNGUZERGAHABCDEFGHIJKLMNOPQRSTUVWXYZSONALAN"
                : "Uzun güzergâh başlangıcı ve öğrenci taşıma hizmeti sonu";
            var block = new TextBlock
            {
                Text = text, Width = 120, Height = 160, FontSize = 12,
                FontFamily = new FontFamily("Arial"), TextWrapping = TextWrapping.Wrap
            };
            var document = CreateTextDocument(block);
            var temporaryDirectory = Directory.CreateTempSubdirectory("oehs-pdf-wrap-");
            try
            {
                var path = Path.Combine(temporaryDirectory.FullName, "wrapped.pdf");
                PdfVectorWriter.Save(document, path);
                using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
                var page = pdf.GetPage(1);
                Assert.Equal(WithoutWhitespace(text), WithoutWhitespace(page.Text));
                Assert.Equal(WithoutWhitespace(text), WithoutWhitespace(PdfTextInRectangle(page, new Rect(20, 20, 120, 160))));
                Assert.True(page.Letters.Select(letter => Math.Round(letter.StartBaseLine.Y, 1)).Distinct().Count() > 1);
            }
            finally
            {
                temporaryDirectory.Delete(recursive: true);
            }
        });
    }

    [Theory]
    [InlineData(HorizontalAlignment.Center, 80d)]
    [InlineData(HorizontalAlignment.Right, 140d)]
    public void PdfWriter_ShouldPreserveTextBlockAlignmentWithinItsSlot(HorizontalAlignment alignment, double expectedLeft)
    {
        RunOnSta(() =>
        {
            var block = new TextBlock
            {
                Text = "METIN", Width = 80, Height = 30, FontSize = 12,
                FontFamily = new FontFamily("Arial"), HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Top
            };
            var document = CreateTextDocument(new Border { Width = 200, Height = 60, Child = block });
            var temporaryDirectory = Directory.CreateTempSubdirectory("oehs-pdf-alignment-");
            try
            {
                var path = Path.Combine(temporaryDirectory.FullName, "aligned.pdf");
                PdfVectorWriter.Save(document, path);
                using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
                Assert.Equal("METIN", PdfTextInRectangle(pdf.GetPage(1), new Rect(expectedLeft, 20, 80, 30)));
            }
            finally
            {
                temporaryDirectory.Delete(recursive: true);
            }
        });
    }

    [Theory]
    [InlineData(false, 60d, 80d)]
    [InlineData(true, 187d, 91d)]
    public void PdfWriter_ShouldPreserveTextBlockRenderTranslation(bool nested, double expectedLeft, double expectedTop)
    {
        RunOnSta(() =>
        {
            var block = new TextBlock
            {
                Text = "METIN", Width = 80, Height = 30, FontSize = 12,
                FontFamily = new FontFamily("Arial"), HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top, RenderTransform = new TranslateTransform(40, 60)
            };
            var document = CreateTextDocument(nested
                ? new Border { Width = 200, Height = 60, Child = block, RenderTransform = new TranslateTransform(7, 11) }
                : block);
            var temporaryDirectory = Directory.CreateTempSubdirectory("oehs-pdf-transform-");
            try
            {
                var path = Path.Combine(temporaryDirectory.FullName, "translated.pdf");
                PdfVectorWriter.Save(document, path);
                using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
                Assert.Equal("METIN", PdfTextInRectangle(pdf.GetPage(1), new Rect(expectedLeft, expectedTop, 80, 30)));
            }
            finally
            {
                temporaryDirectory.Delete(recursive: true);
            }
        });
    }

    [Fact]
    public void PdfWriter_ShouldKeepDecomposedTurkishAccentsTogetherAcrossWrappedLines()
    {
        RunOnSta(() =>
        {
            const string textElement = "G\u0306"; // Decomposed Turkish Ğ: base letter plus combining breve.
            var text = string.Concat(Enumerable.Repeat(textElement, 24));
            var document = CreateTextDocument(new TextBlock
            {
                Text = text, Width = 26, Height = 500, FontSize = 14,
                FontFamily = new FontFamily("Arial"), TextWrapping = TextWrapping.Wrap
            });
            var temporaryDirectory = Directory.CreateTempSubdirectory("oehs-pdf-unicode-");
            try
            {
                var path = Path.Combine(temporaryDirectory.FullName, "unicode.pdf");
                PdfVectorWriter.Save(document, path);
                using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
                var page = pdf.GetPage(1);
                Assert.Equal(text, page.Text);
                Assert.Equal(text, PdfTextInRectangle(page, new Rect(20, 20, 26, 500)));
                var lines = page.Letters.GroupBy(letter => Math.Round(letter.StartBaseLine.Y, 1))
                    .Select(line => string.Concat(line.Select(letter => letter.Value))).ToArray();
                Assert.True(lines.Length > 1);
                foreach (var line in lines)
                    Assert.Equal(string.Concat(Enumerable.Repeat(textElement, line.Length / textElement.Length)), line);
            }
            finally
            {
                temporaryDirectory.Delete(recursive: true);
            }
        });
    }

    [Theory]
    [InlineData("height")]
    [InlineData("width")]
    [InlineData("zero-size")]
    public void PdfWriter_ShouldRejectOverflowBeforeReplacingAnExistingDocument(string overflow)
    {
        RunOnSta(() =>
        {
            var block = new TextBlock
            {
                Text = "METIN sığmayan satır\nSONALAN", Width = overflow == "width" ? 8 : 120,
                Height = overflow == "height" ? 12 : overflow == "zero-size" ? 0 : 80,
                FontSize = 12, FontFamily = new FontFamily("Arial"), TextWrapping = TextWrapping.NoWrap
            };
            var document = CreateTextDocument(block);
            var temporaryDirectory = Directory.CreateTempSubdirectory("oehs-pdf-existing-");
            try
            {
                var path = Path.Combine(temporaryDirectory.FullName, "existing.pdf");
                var original = new byte[] { 1, 2, 3, 4 };
                File.WriteAllBytes(path, original);
                var exception = Assert.Throws<PdfDocumentLayoutException>(() => PdfVectorWriter.Save(document, path));
                Assert.Contains("sığmıyor", exception.Message, StringComparison.Ordinal);
                Assert.Contains("METIN", exception.Message, StringComparison.Ordinal);
                Assert.Equal(original, File.ReadAllBytes(path));
            }
            finally
            {
                temporaryDirectory.Delete(recursive: true);
            }
        });
    }

    private static FixedDocument CreateTextDocument(FrameworkElement element)
    {
        var page = new FixedPage { Width = 793.7007874, Height = 1122.519685 };
        FixedPage.SetLeft(element, 20);
        FixedPage.SetTop(element, 20);
        page.Children.Add(element);
        var document = new FixedDocument();
        var content = new PageContent { Child = page };
        document.Pages.Add(content);
        return document;
    }

    private static string PdfTextInRectangle(UglyToad.PdfPig.Content.Page page, Rect dipRectangle) =>
        string.Concat(page.Letters.Where(letter =>
                letter.BoundingBox.Left >= dipRectangle.Left * 0.75 - 0.5
                && letter.BoundingBox.Right <= dipRectangle.Right * 0.75 + 0.5
                && letter.BoundingBox.Bottom >= page.Height - dipRectangle.Bottom * 0.75 - 0.5
                && letter.BoundingBox.Top <= page.Height - dipRectangle.Top * 0.75 + 0.5)
            .Select(letter => letter.Value));

    private static string WithoutWhitespace(string value) =>
        string.Concat(value.Where(character => !char.IsWhiteSpace(character)));

    [Theory]
    [InlineData(false, "tr-TR")]
    [InlineData(true, "tr-TR")]
    [InlineData(true, "en-US")]
    public void PaymentSelectionAndRecalculation_PreservesExcelPrecisionAndProducesOfficialReportNet(bool selectRoute, string culture)
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var input = new ProgressPaymentInput(16m, 19539.1608m, 0m, 7365.35m, 100000m,
                false, 0m, 0m, 0m, ProgramType: TransportationProgramType.BasicOrSecondary);
            var payment = CreatePayment(input);
            EditorField<IList>(window, "_progressHistory").Add(payment);
            InvokeEditor(window, "BeginProgressHistoryEdit", payment);
            if (selectRoute)
            {
                var routes = EditorField<ListBox>(window, "ProgressRouteComboBox");
                routes.Items.Add(CreateRoute(input));
                routes.SelectedIndex = 0;
                InvokeEditor(window, "UpdateProgressRouteDefaults");
                EditorField<TextBox>(window, "WorkDaysTextBox").Text = "16";
            }

            var preview = new object?[] { null, null, null };
            Assert.True((bool)InvokeEditor(window, "TryCreateHistoryCorrectionPreview", preview)!, preview[2]?.ToString());
            var recalculated = payment with
            {
                Input = Assert.IsType<ProgressPaymentInput>(preview[0]),
                Calculation = Assert.IsType<ProgressPaymentCalculation>(preview[1])
            };
            var context = CreateDocumentContext(recalculated);

            Assert.Equal(332516.08m, context.ReportNetPayable);
            Assert.Equal(19539.1608m, recalculated.Input.DailyPrice);
            Assert.Equal(332516.074511856m, recalculated.Calculation.NetPayable);
            var output = OehsOfficialDocumentFactory.BuildSeparate(context)
                .Single(document => document.Kind == OehsOfficialDocumentKind.HakedisRaporu);
            Assert.Equal("332.516,08 TL", GetCellTextAt((FixedPage)output.Document.Pages[0].Child, 476d, 785d));
        });
    }

    [Theory]
    [InlineData("history")]
    [InlineData("archive")]
    [InlineData("cancel")]
    [InlineData("refresh")]
    public void PaymentEditors_ShouldPreserveEveryCalculationAmount(string path)
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            var input = new ProgressPaymentInput(16m, 19539.1608m, 1.1234m, 7365.3543m,
                100000.1234m, false, 2.1234m, 3.1234m, 4.1234m, 5.1234m,
                10.1234m, 100.1234m, 6.1234m, 7.1234m);
            var payment = CreatePayment(input);
            EditorField<IList>(window, "_progressHistory").Add(payment);
            if (path == "archive")
            {
                var archive = ArchiveProgressEditorContextTests.CreateArchive() with { ProgressPayment = payment };
                SetEditorField(window, "_editingArchiveSourcePath", "unused-archive.json");
                SetEditorField(window, "_editingArchiveSnapshot", archive);
            }
            InvokeEditor(window, "BeginProgressHistoryEdit", payment);
            if (path == "cancel")
                InvokeEditor(window, "CancelProgressEditButton_Click", window, new RoutedEventArgs());
            if (path == "refresh")
                InvokeEditor(window, "RefreshViewsAfterHistoryCorrection");

            var expected = new (string Field, decimal Amount)[]
            {
                ("DailyPriceTextBox", 19539.1608m), ("GuideDailyPriceTextBox", 100.1234m),
                ("DiscountTextBox", 1.1234m), ("PriceDifferenceTextBox", 7365.3543m),
                ("GuideDiscountTextBox", 6.1234m), ("GuidePriceDifferenceTextBox", 7.1234m),
                ("TaxDebtTextBox", 2.1234m), ("SocialSecurityDebtTextBox", 3.1234m),
                ("MinimumWageSupportTextBox", 4.1234m), ("InspectionPenaltyTextBox", 5.1234m)
            };
            foreach (var (field, amount) in expected)
                Assert.Equal(amount, decimal.Parse(EditorField<TextBox>(window, field).Text, CultureInfo.CurrentCulture));

            if (path is "history" or "archive")
            {
                var preview = new object?[] { null, null, null };
                var method = path == "archive" ? "TryCreateArchiveCorrectionPreview" : "TryCreateHistoryCorrectionPreview";
                Assert.True((bool)InvokeEditor(window, method, preview)!, preview[2]?.ToString());
                var updated = Assert.IsType<ProgressPaymentInput>(preview[0]);
                Assert.Equal(input, updated);
            }
        });
    }

    [Theory]
    [InlineData("contract")]
    [InlineData("route")]
    [InlineData("routeCancel")]
    public void ContractAndRouteEditors_ShouldPreserveDailyPricesAndContractAmount(string path)
    {
        RunOnSta(() =>
        {
            var window = CreateEditorHarness();
            if (path == "contract")
            {
                var contract = ArchiveProgressEditorContextTests.CreateArchive().Contract with
                {
                    ContractAmount = 100000.1234m, TransportDailyPrice = 19539.1608m
                };
                InvokeEditor(window, "ApplyContractToForm", contract);
                Assert.Equal(100000.1234m, decimal.Parse(EditorField<TextBox>(window, "ContractAmountTextBox").Text, CultureInfo.CurrentCulture));
                Assert.Equal(19539.1608m, decimal.Parse(EditorField<TextBox>(window, "ContractTransportDailyPriceTextBox").Text, CultureInfo.CurrentCulture));
                return;
            }

            var route = CreateRoute(new ProgressPaymentInput(16m, 19539.1608m, 0m, 0m,
                100000m, false, 0m, 0m, 0m, GuideDailyPrice: 100.1234m));
            InvokeEditor(window, "EditRouteButton_Click", new Button { Tag = route }, new RoutedEventArgs());
            if (path == "routeCancel")
            {
                EditorField<TextBox>(window, "RouteGuideDailyPriceTextBox").Text = "1";
                InvokeEditor(window, "CancelRouteEditButton_Click", window, new RoutedEventArgs());
                Assert.Empty(EditorField<TextBox>(window, "RouteGuideDailyPriceTextBox").Text);
                // Cancellation clears the draft; reopening must retain the saved precision.
                InvokeEditor(window, "EditRouteButton_Click", new Button { Tag = route }, new RoutedEventArgs());
            }
            Assert.Equal(100.1234m, decimal.Parse(EditorField<TextBox>(window, "RouteGuideDailyPriceTextBox").Text, CultureInfo.CurrentCulture));
        });
    }

    [Theory]
    [InlineData(100, 0, 0, "₺-100,00")]
    [InlineData(0, 1, 100, "₺100,00")]
    public void Icmal_ShouldIncludeNegativeServiceCorrectionsAndGuideOnlyService(
        decimal discount, decimal guideDays, decimal guidePrice, string expectedService)
    {
        RunOnSta(() =>
        {
            var input = new ProgressPaymentInput(0m, 0m, discount, 0m, 100000m,
                true, 0m, 0m, 0m, GuideWorkDays: guideDays, GuideDailyPrice: guidePrice);
            var output = OehsOfficialDocumentFactory.BuildSeparate(CreateDocumentContext(CreatePayment(input)))
                .Single(document => document.Kind == OehsOfficialDocumentKind.HakedisIcmali);
            var page = (FixedPage)output.Document.Pages[0].Child;

            Assert.Equal("Hizmet Alımı İşi Ekim 2026", GetCellTextAt(page, 89d, 184d));
            Assert.Equal(expectedService, GetCellTextAt(page, 274d, 184d));
            Assert.Equal(expectedService, GetCellTextAt(page, 577d, 536d));
        });
    }

    [Theory]
    [InlineData("transport-service/guide-discount")]
    [InlineData("guide-service/transport-discount")]
    [InlineData("price-differences")]
    public void Icmal_ShouldKeepMonthWhenReportableComponentsCancel(string cancellation)
    {
        RunOnSta(() =>
        {
            var input = new ProgressPaymentInput(0m, 0m, 0m, 0m, 100000m,
                true, 0m, 0m, 0m, ProgramType: TransportationProgramType.SpecialEducation);
            input = cancellation switch
            {
                "transport-service/guide-discount" => input with
                {
                    WorkDays = 1m, DailyPrice = 100m, GuideDiscountAmount = 100m
                },
                "guide-service/transport-discount" => input with
                {
                    GuideWorkDays = 1m, GuideDailyPrice = 100m, DiscountAmount = 100m
                },
                "price-differences" => input with
                {
                    PriceDifference = 100m, GuidePriceDifference = -100m
                },
                _ => throw new ArgumentOutOfRangeException(nameof(cancellation))
            };
            var output = OehsOfficialDocumentFactory.BuildSeparate(CreateDocumentContext(CreatePayment(input)))
                .Single(document => document.Kind == OehsOfficialDocumentKind.HakedisIcmali);
            var page = (FixedPage)output.Document.Pages[0].Child;

            Assert.Equal("Hizmet Alımı İşi Ekim 2026", GetCellTextAt(page, 89d, 184d));
            Assert.Equal("₺0,00", GetCellTextAt(page, 274d, 184d));
            Assert.Equal("₺0,00", GetCellTextAt(page, 470d, 184d));
            Assert.Equal("₺0,00", GetCellTextAt(page, 577d, 184d));
            Assert.Equal("₺0,00", GetCellTextAt(page, 577d, 536d));
            Assert.Equal(string.Empty, GetCellTextAt(page, 89d, 216d));
        });
    }

    [Theory]
    [InlineData(TransportationProgramType.SpecialEducation, 100, "₺100,00")]
    [InlineData(TransportationProgramType.SpecialEducation, -100, "₺-100,00")]
    [InlineData(TransportationProgramType.BasicOrSecondary, 100, "₺100,00")]
    public void Icmal_ShouldIncludePriceDifferenceOnlyMonth(TransportationProgramType program, decimal difference, string expectedTotal)
    {
        RunOnSta(() =>
        {
            var input = new ProgressPaymentInput(0m, 0m, 0m, difference, 100000m,
                true, 0m, 0m, 0m, ProgramType: program);
            var output = OehsOfficialDocumentFactory.BuildSeparate(CreateDocumentContext(CreatePayment(input)))
                .Single(document => document.Kind == OehsOfficialDocumentKind.HakedisIcmali);
            var page = (FixedPage)output.Document.Pages[0].Child;

            Assert.Equal("Hizmet Alımı İşi Ekim 2026", GetCellTextAt(page, 89d, 184d));
            Assert.Equal("₺0,00", GetCellTextAt(page, 274d, 184d));
            Assert.Equal(expectedTotal, GetCellTextAt(page, 577d, 184d));
            Assert.Equal(expectedTotal, GetCellTextAt(page, 577d, 536d));
            Assert.Equal(string.Empty, GetCellTextAt(page, 89d, 216d));
        });
    }

    [Theory]
    [InlineData(TransportationProgramType.BasicOrSecondary, 100, 0, "100,00 TL")]
    [InlineData(TransportationProgramType.BasicOrSecondary, 0, 0, "0,00 TL")]
    [InlineData(TransportationProgramType.SpecialEducation, 100, 25, "125,00 TL")]
    [InlineData(TransportationProgramType.SpecialEducation, -100, 25, "-75,00 TL")]
    public void HakedisRaporu_B_ShouldShowCurrentManualPriceDifferencesNotDeductions(
        TransportationProgramType program, decimal transportDifference, decimal guideDifference, string expected)
    {
        RunOnSta(() =>
        {
            var input = new ProgressPaymentInput(1m, 1000m, 0m, transportDifference, 100000m,
                true, 50m, 0m, 0m, ProgramType: program, GuidePriceDifference: guideDifference);
            var payment = CreatePayment(input);
            Assert.NotEqual(0m, payment.Calculation.TotalDeductions);
            var output = OehsOfficialDocumentFactory.BuildSeparate(CreateDocumentContext(payment))
                .Single(document => document.Kind == OehsOfficialDocumentKind.HakedisRaporu);
            var page = (FixedPage)output.Document.Pages[0].Child;

            Assert.Equal("B", GetCellTextAt(page, 67d, 211d));
            Assert.Equal("Fiyat Farkı Tutarı", GetCellTextAt(page, 120d, 211d));
            Assert.Equal(expected, GetCellTextAt(page, 476d, 211d));

            var directory = Directory.CreateTempSubdirectory("oehs-report-b-");
            try
            {
                var path = Path.Combine(directory.FullName, "report.pdf");
                PdfVectorWriter.Save(output.Document, path);
                using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
                Assert.Equal(WithoutWhitespace(expected),
                    WithoutWhitespace(PdfTextInRectangle(pdf.GetPage(1), new Rect(476d, 211d, 250d, 28d))));
            }
            finally
            {
                directory.Delete(recursive: true);
            }
        });
    }

    [Theory]
    [InlineData(0, "₺0,00", "₺1.000,00")]
    [InlineData(100, "₺100,00", "₺1.100,00")]
    public void BasicIcmal_ShouldNotAddDeductionsToManualPriceDifferenceOrMonthlyTotal(
        decimal difference, string expectedDifference, string expectedTotal)
    {
        RunOnSta(() =>
        {
            var input = new ProgressPaymentInput(1m, 1000m, 0m, difference, 100000m,
                true, 50m, 0m, 0m, ProgramType: TransportationProgramType.BasicOrSecondary);
            var payment = CreatePayment(input);
            Assert.True(payment.Calculation.TotalDeductions > 0m);
            var output = OehsOfficialDocumentFactory.BuildSeparate(CreateDocumentContext(payment))
                .Single(document => document.Kind == OehsOfficialDocumentKind.HakedisIcmali);
            var page = (FixedPage)output.Document.Pages[0].Child;

            Assert.Equal(expectedDifference, GetCellTextAt(page, 470d, 184d));
            Assert.Equal(expectedTotal, GetCellTextAt(page, 577d, 184d));
            Assert.Equal(expectedTotal, GetCellTextAt(page, 577d, 536d));
        });
    }

    [Fact]
    public void SpecialIcmal_ShouldShowCurrentJulyManualPriceDifferences()
    {
        RunOnSta(() =>
        {
            var input = new ProgressPaymentInput(1m, 1000m, 0m, 100m, 100000m,
                true, 50m, 0m, 0m, ProgramType: TransportationProgramType.SpecialEducation)
                with { GuidePriceDifference = 25m };
            var payment = CreatePayment(input) with { PeriodDate = new DateOnly(2027, 7, 31) };
            var context = CreateDocumentContext(payment) with
            {
                PeriodDate = payment.PeriodDate,
                ContractEndDate = new DateOnly(2027, 7, 31)
            };
            var output = OehsOfficialDocumentFactory.BuildSeparate(context)
                .Single(document => document.Kind == OehsOfficialDocumentKind.HakedisIcmali);
            var page = (FixedPage)output.Document.Pages[0].Child;

            Assert.Equal("Hizmet Alımı İşi Temmuz 2027", GetCellTextAt(page, 89d, 184d));
            Assert.Equal("₺125,00", GetCellTextAt(page, 470d, 184d));
            Assert.Equal("₺1.125,00", GetCellTextAt(page, 577d, 184d));
        });
    }

    [Theory]
    [InlineData(TransportationProgramType.SpecialEducation)]
    [InlineData(TransportationProgramType.BasicOrSecondary)]
    public void Icmal_ShouldNotCreateMonthForDeductionsAlone(TransportationProgramType program)
    {
        RunOnSta(() =>
        {
            var input = new ProgressPaymentInput(0m, 19539.1608m, 0m, 0m, 100000m,
                true, 50m, 0m, 0m, ProgramType: program);
            var output = OehsOfficialDocumentFactory.BuildSeparate(CreateDocumentContext(CreatePayment(input)))
                .Single(document => document.Kind == OehsOfficialDocumentKind.HakedisIcmali);
            var page = (FixedPage)output.Document.Pages[0].Child;

            Assert.Equal(string.Empty, GetCellTextAt(page, 89d, 184d));
            Assert.Equal("₺0,00", GetCellTextAt(page, 577d, 536d));
        });
    }

    private static ProgressPaymentHistoryData CreatePayment(ProgressPaymentInput input) =>
        new(Guid.NewGuid(), new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc),
            input, ProgressPaymentCalculator.Calculate(input), 1, new DateOnly(2026, 10, 31),
            "01", "Hat 01", "Hat 01", "Firma", "IHALE-01", "SOZ-01",
            ContractorTaxOrIdentityNumber: "12345678901");

    private static OehsOfficialDocumentContext CreateDocumentContext(ProgressPaymentHistoryData payment)
    {
        var payments = new[] { payment };
        var totals = ProgressPaymentReportService.Calculate(payment, payments);
        return new OehsOfficialDocumentContext("Hat 01", "IHALE-01", "01", "Firma", "SOZ-01",
            payment.Input.ContractAmount, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15),
            new DateOnly(2026, 9, 1), new DateOnly(2027, 6, 30), 180,
            payment.PeriodDate, payment.ProgressPaymentNumber, payment.Calculation, payments, totals,
            ProgressPaymentReportService.CalculateReportNetPayable(payment, totals), 0m,
            new DateOnly(2026, 10, 31), ProgramType: payment.Input.ProgramType);
    }

    private static MainWindow CreateEditorHarness()
    {
        // Exercise real editor handlers without constructor-driven persistence or window setup.
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        foreach (var field in typeof(MainWindow).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
        {
            if (typeof(FrameworkElement).IsAssignableFrom(field.FieldType)
                || field.FieldType.IsGenericType &&
                (field.FieldType.GetGenericTypeDefinition() == typeof(System.Collections.ObjectModel.ObservableCollection<>)
                 || field.FieldType.GetGenericTypeDefinition() == typeof(RouteDraftCache<>)))
                field.SetValue(window, Activator.CreateInstance(field.FieldType));
        }
        SetEditorField(window, "_calculationConstants", ProgressPaymentCalculationConstants.Defaults);
        EditorField<ListBox>(window, "ProgressRouteComboBox").SelectionMode = SelectionMode.Extended;
        EditorField<Button>(window, "NavProgress").Tag = "Progress";
        return window;
    }

    private static object CreateRoute(ProgressPaymentInput input) =>
        Activator.CreateInstance(typeof(MainWindow).GetNestedType("RouteDraft", BindingFlags.NonPublic)!,
            "01", "Hat 01", input.DailyPrice, input.GuideDailyPrice,
            input.WorkDays, "IHALE-01", "12345678901", "Firma", "SOZ-01", input.ProgramType, input.ContractAmount, null)!;

    private static T EditorField<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(window)!;

    private static void SetEditorField(MainWindow window, string name, object value) =>
        typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, value);

    private static object? InvokeEditor(MainWindow window, string name, params object?[] arguments)
    {
        try
        {
            return typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    [Fact]
    public void SignatorySettingsStore_ShouldCreateEditableDefaults_WhenFileIsMissing()
    {
        var temporaryDirectory = Directory.CreateTempSubdirectory("oehs-document-settings-");
        try
        {
            var settingsPath = Path.Combine(temporaryDirectory.FullName, "official-document-settings.json");
            var settings = OehsOfficialDocumentSettingsStore.Load(settingsPath);

            Assert.True(File.Exists(settingsPath));
            Assert.Equal("", settings.PreparerName);
            Assert.Equal("", settings.ControllerName);
            Assert.Equal("", settings.ApproverName);
            Assert.Contains("preparerName", File.ReadAllText(settingsPath), StringComparison.Ordinal);
        }
        finally
        {
            temporaryDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void SignatorySettingsStore_ShouldUseCustomValues_AndPreserveBlankValues()
    {
        var temporaryDirectory = Directory.CreateTempSubdirectory("oehs-document-settings-");
        try
        {
            var settingsPath = Path.Combine(temporaryDirectory.FullName, "official-document-settings.json");
            File.WriteAllText(settingsPath, """
            {
              "preparerName": "  Örnek Düzenleyen  ",
              "preparerRole": "",
              "controllerName": "Örnek Kontrol",
              "controllerRole": "Kontrol Görevlisi",
              "approverName": "Örnek Onaylayan",
              "approverRole": "Kurum Müdürü"
            }
            """);

            var settings = OehsOfficialDocumentSettingsStore.Load(settingsPath);

            Assert.Equal("Örnek Düzenleyen", settings.PreparerName);
            Assert.Equal("", settings.PreparerRole);
            Assert.Equal("Örnek Kontrol", settings.ControllerName);
            Assert.Equal("Kontrol Görevlisi", settings.ControllerRole);
            Assert.Equal("Örnek Onaylayan", settings.ApproverName);
            Assert.Equal("Kurum Müdürü", settings.ApproverRole);
        }
        finally
        {
            temporaryDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void SignatorySettingsStore_ShouldReportInvalidJson()
    {
        var temporaryDirectory = Directory.CreateTempSubdirectory("oehs-document-settings-");
        try
        {
            var settingsPath = Path.Combine(temporaryDirectory.FullName, "official-document-settings.json");
            File.WriteAllText(settingsPath, "{ invalid json");

            var exception = Assert.Throws<InvalidDataException>(
                () => OehsOfficialDocumentSettingsStore.Load(settingsPath));

            Assert.Contains(settingsPath, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            temporaryDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void FourOfficialTemplates_ShouldExportAsSinglePageA4VectorPdfs()
    {
        RunOnSta(() =>
        {
            var previousInput = new ProgressPaymentInput(
                WorkDays: 1,
                DailyPrice: 100m,
                DiscountAmount: 0m,
                PriceDifference: 20m,
                ContractAmount: 100000m,
                IsVatExempt: true,
                TaxDebt: 0m,
                SocialSecurityDebt: 0m,
                MinimumWageSupport: 0m,
                InspectionPenalty: 0m);

            var noDeductions = new DeductionBreakdown(0m, 0m, 0m, 0m, 0m, 0m, 0m);
            var previousCalculation = new ProgressPaymentCalculation(
                ServiceAmount: 100m,
                PriceDifferenceExcludingVat: 20m,
                TotalAmount: 120m,
                Vat: 0m,
                Withholding: 0m,
                InvoiceAmountAfterWithholding: 120m,
                StampTax: 0m,
                GrossAmount: 120m,
                TotalDeductions: 0m,
                NetPayable: 120m,
                AdditionalGuarantee: 0m,
                Deductions: noDeductions);

            var previousPayment = new ProgressPaymentHistoryData(
                Id: Guid.NewGuid(),
                CreatedAt: new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc),
                Input: previousInput,
                Calculation: previousCalculation,
                ProgressPaymentNumber: 1,
                PeriodDate: new DateOnly(2026, 9, 30),
                RouteNumber: "1",
                RouteName: "Örnek Güzergâh",
                WorkName: "Örnek Güzergâh",
                ContractorName: "Örnek Yüklenici",
                TenderNumber: "TEST-IHALE",
                ContractNumber: "TEST-SOZ",
                TenderDate: new DateOnly(2026, 8, 25),
                ContractDate: new DateOnly(2026, 8, 25),
                ContractStartDate: new DateOnly(2026, 9, 1),
                ContractEndDate: new DateOnly(2027, 6, 30),
                ContractorTaxOrIdentityNumber: "12345678901");

            var input = new ProgressPaymentInput(
                WorkDays: 1,
                DailyPrice: 100m,
                DiscountAmount: 0m,
                PriceDifference: 10m,
                ContractAmount: 100000m,
                IsVatExempt: true,
                TaxDebt: 2m,
                SocialSecurityDebt: 0m,
                MinimumWageSupport: 0m,
                InspectionPenalty: 2m);

            var calculation = new ProgressPaymentCalculation(
                ServiceAmount: 100m,
                PriceDifferenceExcludingVat: 10m,
                TotalAmount: 110m,
                Vat: 0m,
                Withholding: 0m,
                InvoiceAmountAfterWithholding: 110m,
                StampTax: 1m,
                GrossAmount: 110m,
                TotalDeductions: 5m,
                NetPayable: 105m,
                AdditionalGuarantee: 0m,
                Deductions: new DeductionBreakdown(0m, 1m, 2m, 2m, 0m, 0m, 0m));

            var currentPayment = new ProgressPaymentHistoryData(
                Id: Guid.NewGuid(),
                CreatedAt: new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc),
                Input: input,
                Calculation: calculation,
                ProgressPaymentNumber: 2,
                PeriodDate: new DateOnly(2026, 10, 31),
                RouteNumber: "1",
                RouteName: "Örnek Güzergâh",
                WorkName: "Örnek Güzergâh",
                ContractorName: "Örnek Yüklenici",
                TenderNumber: "TEST-IHALE",
                ContractNumber: "TEST-SOZ",
                TenderDate: new DateOnly(2026, 8, 25),
                ContractDate: new DateOnly(2026, 8, 25),
                ContractStartDate: new DateOnly(2026, 9, 1),
                ContractEndDate: new DateOnly(2027, 6, 30),
                ContractorTaxOrIdentityNumber: "12345678901");

            var payments = new[] { previousPayment, currentPayment };
            var reportTotals = ProgressPaymentReportService.Calculate(currentPayment, payments);
            var reportNetPayable = ProgressPaymentReportService.CalculateReportNetPayable(
                currentPayment,
                reportTotals);
            var previousDeduction = ProgressPaymentIcmalService.FindPreviousDeduction(
                payments,
                currentPayment);

            var context = new OehsOfficialDocumentContext(
                WorkName: "Örnek Güzergâh",
                TenderNumber: "TEST-IHALE",
                RouteNumber: "1",
                ContractorName: "Örnek Yüklenici",
                ContractNumber: "TEST-SOZ",
                ContractAmount: 100000m,
                TenderDate: new DateOnly(2026, 8, 25),
                ContractDate: new DateOnly(2026, 8, 25),
                ContractStartDate: new DateOnly(2026, 9, 1),
                ContractEndDate: new DateOnly(2027, 6, 30),
                ContractWorkDays: 180,
                PeriodDate: new DateOnly(2026, 10, 31),
                ProgressPaymentNumber: 2,
                Calculation: calculation,
                Payments: ProgressPaymentReportService.OrderByPaymentPeriod(payments),
                ReportTotals: reportTotals,
                ReportNetPayable: reportNetPayable,
                PreviousDeduction: previousDeduction,
                GeneratedDate: new DateOnly(2026, 10, 31),
                PreparerName: "TEST DÜZENLEYEN",
                PreparerRole: "TEST MEMUR",
                ControllerName: "TEST KONTROL EDEN",
                ControllerRole: "TEST ŞUBE MÜDÜRÜ",
                ApproverName: "TEST ONAYLAYAN",
                ApproverRole: "TEST MÜDÜR");
            var outputs = OehsOfficialDocumentFactory.BuildSeparate(context);
            Assert.Equal(4, outputs.Count);
            Assert.Equal(
                new[]
                {
                    OehsOfficialDocumentKind.HakedisRaporu,
                    OehsOfficialDocumentKind.HizmetIsleriHakedisRaporu,
                    OehsOfficialDocumentKind.KabulTutanagi,
                    OehsOfficialDocumentKind.HakedisIcmali
                }.OrderBy(kind => kind),
                outputs.Select(output => output.Kind).OrderBy(kind => kind));

            // Every official output must be a single A4 portrait page; a valid
            // first report must not hide a missing, duplicated, or oversized sibling.
            foreach (var output in outputs)
            {
                Assert.Single(output.Document.Pages);
                var page = Assert.IsType<FixedPage>(output.Document.Pages[0].Child);
                Assert.True(page.Width > 0 && page.Height > page.Width,
                    $"{output.Kind} must use a portrait page, got {page.Width} x {page.Height}.");
            }

            // Verify that the test PDFs use the same previous/current period data
            // as the application report services rather than empty hard-coded history.
            var paymentOutput = Assert.Single(
                outputs.Where(output => output.Kind == OehsOfficialDocumentKind.HakedisRaporu));
            var paymentPage = Assert.IsType<FixedPage>(paymentOutput.Document.Pages[0].Child);
            var paymentText = GetPageText(paymentPage);

            // Regression guard: the rotated deductions heading must share the
            // center of the full-height merged cell, without changing its font or angle.
            var deductionsCell = Assert.Single(paymentPage.Children.OfType<Border>()
                .Where(border => Math.Abs(border.Width - 165d) < 0.01 && border.Height > 300d));
            var deductionsTitle = Assert.Single(paymentPage.Children.OfType<TextBlock>()
                .Where(block => block.Text == "KESİNTİLER VE MAHSUPLAR"));
            Assert.IsType<RotateTransform>(deductionsTitle.RenderTransform);
            Assert.Equal(-90d, ((RotateTransform)deductionsTitle.RenderTransform).Angle);
            Assert.Equal(VerticalAlignment.Center, deductionsTitle.VerticalAlignment);
            Assert.Equal(TextAlignment.Center, deductionsTitle.TextAlignment);
            Assert.InRange(deductionsTitle.Height,1d,deductionsCell.Width);
            Assert.Equal(deductionsCell.Height, deductionsTitle.Width, 2);
            Assert.Equal(
                FixedPage.GetLeft(deductionsCell) + deductionsCell.Width / 2d,
                FixedPage.GetLeft(deductionsTitle) + deductionsTitle.Width / 2d,
                2);
            Assert.Equal(
                FixedPage.GetTop(deductionsCell) + deductionsCell.Height / 2d,
                FixedPage.GetTop(deductionsTitle) + deductionsTitle.Height / 2d,
                2);

            // Excel D6 = prior + current monthly K+T, less current I+R.
            // D7 contains only the current month's raw I+R.
            // With two 100 TL base periods, 20 TL previous F and 10 TL current F:
            // A=220, B=10, C=230, D=120, E=110, F=0.
            // The workbook's D11 already includes the current raw price difference, so G/D13=110.
            Assert.Equal("220,00 TL", GetCellTextAt(paymentPage, 476d, 183d)); // A
            Assert.Equal("10,00 TL", GetCellTextAt(paymentPage, 476d, 211d));  // B
            Assert.Equal("230,00 TL", GetCellTextAt(paymentPage, 476d, 239d)); // C = A + B
            Assert.Equal("120,000 TL", GetCellTextAt(paymentPage, 476d, 301d)); // Special Education D keeps the source three-decimal precision
            Assert.Equal("110,00 TL", GetCellTextAt(paymentPage, 476d, 329d)); // E = C - D
            Assert.Equal("0,00 TL", GetCellTextAt(paymentPage, 476d, 357d));   // F
            Assert.Equal("110,00 TL", GetCellTextAt(paymentPage, 476d, 385d)); // G = D11 + D12
            Assert.Equal("5,00 TL", GetCellTextAt(paymentPage, 476d, 757d));   // H
            Assert.Equal("105,00 TL", GetCellTextAt(paymentPage, 476d, 785d)); // I = G - H
            Assert.Equal("Bu Hakedişin Tutarı ( C - D )", GetCellTextAt(paymentPage, 120d, 329d));
            Assert.Contains("Hakediş No : 2", paymentText, StringComparison.Ordinal);
            Assert.Contains("KESİNTİLER VE MAHSUPLAR", paymentText, StringComparison.Ordinal);
            Assert.Contains("KDV (taşıma %10 + rehber %20)", paymentText, StringComparison.Ordinal);
            Assert.Contains("KDV Tevkifatı (taşıma 5/10; rehber 9/10)", paymentText, StringComparison.Ordinal);
            Assert.Contains("Damga Vergisi (oran %0,948)", paymentText, StringComparison.Ordinal);

            // The report must expose the exact payment period and contractor identity,
            // not only the computed monetary cells.
            Assert.Contains("Örnek Yüklenici", paymentText, StringComparison.Ordinal);
            Assert.Contains("31.10.2026", paymentText, StringComparison.Ordinal);

            var serviceReport = Assert.Single(
                outputs.Where(output => output.Kind == OehsOfficialDocumentKind.HizmetIsleriHakedisRaporu));
            var serviceReportText = GetPageText(
                Assert.IsType<FixedPage>(serviceReport.Document.Pages[0].Child));
            Assert.Contains("Örnek Yüklenici", serviceReportText, StringComparison.Ordinal);
            Assert.Contains("100.000,00 TL", serviceReportText, StringComparison.Ordinal);
            Assert.Contains("25.08.2026", serviceReportText, StringComparison.Ordinal);
            Assert.DoesNotContain("TEST-SOZ", serviceReportText, StringComparison.Ordinal);
            Assert.DoesNotContain("İKN: TEST-IHALE", serviceReportText, StringComparison.Ordinal);
            Assert.Contains("180 İş Günü", serviceReportText, StringComparison.Ordinal);
            Assert.Contains("Tescile Dahil Değil", serviceReportText, StringComparison.Ordinal);
            Assert.Contains("Avans verilmeyecektir.", serviceReportText, StringComparison.Ordinal);
            Assert.Contains("Yoktur.", serviceReportText, StringComparison.Ordinal);
            Assert.Contains("M.Y.H.B.Y. Örnek No: 4", serviceReportText, StringComparison.Ordinal);
            Assert.Contains("Sözleşme Tarihi ve No.su", serviceReportText, StringComparison.Ordinal);
            var generatedDateContext = context with { GeneratedDate = new DateOnly(2026, 10, 19) };
            var generatedService = Assert.Single(OehsOfficialDocumentFactory.BuildSeparate(generatedDateContext)
                .Where(output => output.Kind == OehsOfficialDocumentKind.HizmetIsleriHakedisRaporu));
            var generatedServiceText = GetPageText(Assert.IsType<FixedPage>(generatedService.Document.Pages[0].Child));
            Assert.Contains("19.10.2026", generatedServiceText, StringComparison.Ordinal);
            Assert.DoesNotContain("31.10.2026", generatedServiceText, StringComparison.Ordinal);
            Assert.DoesNotContain("M.Y.H.B.Y. Örnek No: 5", serviceReportText, StringComparison.Ordinal);

            var specialIcmal = Assert.Single(outputs.Where(output => output.Kind == OehsOfficialDocumentKind.HakedisIcmali));
            var specialIcmalText = GetPageText(Assert.IsType<FixedPage>(specialIcmal.Document.Pages[0].Child));
            Assert.Contains("₺100,00", specialIcmalText, StringComparison.Ordinal); // base service excludes raw I/R
            Assert.Contains("₺20,00", specialIcmalText, StringComparison.Ordinal);  // raw I/R shown once
            Assert.Contains("₺120,00", specialIcmalText, StringComparison.Ordinal); // row total matches K+T
            Assert.DoesNotContain("₺140,00", specialIcmalText, StringComparison.Ordinal);
            // Price differences are displayed separately from the service base,
            // and each component is included exactly once in the row total.
            Assert.Contains("₺20,00", specialIcmalText, StringComparison.Ordinal);
            Assert.Contains("₺10,00", specialIcmalText, StringComparison.Ordinal);

            var basicCalculation = ProgressPaymentCalculator.Calculate(
                input with { ProgramType = TransportationProgramType.BasicOrSecondary },
                ProgressPaymentCalculationConstants.Defaults);
            var basicContext = context with
            {
                ProgramType = TransportationProgramType.BasicOrSecondary,
                Calculation = basicCalculation
            };
            var basicOutputs = OehsOfficialDocumentFactory.BuildSeparate(basicContext);
            Assert.Equal(4, basicOutputs.Count);
            foreach (var output in basicOutputs)
            {
                var outputPage = Assert.IsType<FixedPage>(output.Document.Pages[0].Child);
                var textBlocks = GetPageText(outputPage);

                // Keep the Basic/Secondary document family isolated but subject
                // to the same cross-document identity and period acceptance gate.
                Assert.Contains("Örnek Güzergâh", textBlocks, StringComparison.Ordinal);
                switch (output.Kind)
                {
                    case OehsOfficialDocumentKind.HakedisRaporu:
                    case OehsOfficialDocumentKind.HizmetIsleriHakedisRaporu:
                        Assert.Contains("31.10.2026", textBlocks, StringComparison.Ordinal);
                        Assert.Contains("Örnek Yüklenici", textBlocks, StringComparison.Ordinal);
                        break;
                    case OehsOfficialDocumentKind.KabulTutanagi:
                        Assert.Contains("01.10.2026 / 31.10.2026", textBlocks, StringComparison.Ordinal);
                        Assert.Contains("Örnek Yüklenici", textBlocks, StringComparison.Ordinal);
                        break;
                    case OehsOfficialDocumentKind.HakedisIcmali:
                        Assert.Contains("Hizmet Alımı İşi Eylül 2026", textBlocks, StringComparison.Ordinal);
                        Assert.Contains("Hizmet Alımı İşi Ekim 2026", textBlocks, StringComparison.Ordinal);
                        break;
                }
            }

            var basicServiceReport = Assert.Single(
                basicOutputs.Where(output => output.Kind == OehsOfficialDocumentKind.HizmetIsleriHakedisRaporu));
            var basicServiceReportText = GetPageText(
                Assert.IsType<FixedPage>(basicServiceReport.Document.Pages[0].Child));
            Assert.Contains("01.09.2026-30.06.2027 Tarihleri Arasıdır.", basicServiceReportText, StringComparison.Ordinal);
            Assert.DoesNotContain("180 İş Günü", basicServiceReportText, StringComparison.Ordinal);
            Assert.Contains("Sözleşme Tarihi ve No.su", basicServiceReportText, StringComparison.Ordinal);
            Assert.Contains("M.Y.H.B.Y. Örnek No: 4", basicServiceReportText, StringComparison.Ordinal);

            // July is a real period for Basic/Secondary, unlike the current
            // Special Education lookup range where July remains a zero placeholder.
            var julyInput = input with
            {
                ProgramType = TransportationProgramType.BasicOrSecondary
            };
            var julyCalculation = ProgressPaymentCalculator.Calculate(julyInput);
            var julyPayment = currentPayment with
            {
                Id = Guid.NewGuid(),
                CreatedAt = new DateTime(2027, 7, 31, 12, 0, 0, DateTimeKind.Utc),
                Input = julyInput,
                Calculation = julyCalculation,
                ProgressPaymentNumber = 9,
                PeriodDate = new DateOnly(2027, 7, 31)
            };
            var basicJulyTotals = ProgressPaymentReportService.Calculate(
                julyPayment,
                new[] { julyPayment });
            var basicJulyContext = basicContext with
            {
                PeriodDate = new DateOnly(2027, 7, 31),
                ContractEndDate = new DateOnly(2027, 7, 31),
                Payments = new[] { julyPayment },
                ReportTotals = basicJulyTotals,
                ReportNetPayable = ProgressPaymentReportService.CalculateReportNetPayable(
                    julyPayment,
                    basicJulyTotals),
                PreviousDeduction = 0m
            };
            var basicJulyIcmal = Assert.Single(
                OehsOfficialDocumentFactory.BuildSeparate(basicJulyContext)
                    .Where(output => output.Kind == OehsOfficialDocumentKind.HakedisIcmali));
            var basicJulyIcmalText = GetPageText(
                Assert.IsType<FixedPage>(basicJulyIcmal.Document.Pages[0].Child));
            Assert.Contains("Hizmet Alımı İşi Temmuz 2027", basicJulyIcmalText, StringComparison.Ordinal);
            Assert.Contains("₺100,00", basicJulyIcmalText, StringComparison.Ordinal); // base service
            Assert.Contains("₺10,00", basicJulyIcmalText, StringComparison.Ordinal); // entered price difference
            Assert.Contains("₺110,00", basicJulyIcmalText, StringComparison.Ordinal); // total counted once

            var basicPayment = Assert.Single(
                basicOutputs.Where(output => output.Kind == OehsOfficialDocumentKind.HakedisRaporu));
            var basicPaymentText = GetPageText(
                Assert.IsType<FixedPage>(basicPayment.Document.Pages[0].Child));
            Assert.Contains("KDV (taşıma %10)", basicPaymentText, StringComparison.Ordinal);
            Assert.DoesNotContain("rehber %0", basicPaymentText, StringComparison.Ordinal);
            Assert.Contains("KDV Tevkifatı (taşıma 5/10)", basicPaymentText, StringComparison.Ordinal);
            Assert.DoesNotContain("rehber 0/10", basicPaymentText, StringComparison.Ordinal);

            var acceptanceOutputForMapping = Assert.Single(
                outputs.Where(output => output.Kind == OehsOfficialDocumentKind.KabulTutanagi));
            var acceptanceMappingPage = Assert.IsType<FixedPage>(
                acceptanceOutputForMapping.Document.Pages[0].Child);
            var acceptanceMappingText = GetPageText(acceptanceMappingPage);
            // The source template retains its official label; duration formatting is program-specific.
            Assert.Contains("Sözleşmeye göre işin süresi (takvim günü)", acceptanceMappingText, StringComparison.Ordinal);
            Assert.Contains("180 İş Günü", acceptanceMappingText, StringComparison.Ordinal);
            Assert.Contains("30.06.2027", acceptanceMappingText, StringComparison.Ordinal);
            Assert.Contains("31 Ekim 2026 Cumartesi", acceptanceMappingText, StringComparison.Ordinal);
            Assert.DoesNotContain("Tescile Dahil Değil", acceptanceMappingText, StringComparison.Ordinal);
            Assert.DoesNotContain("Avans verilmeyecektir.", acceptanceMappingText, StringComparison.Ordinal);
            Assert.DoesNotContain("Yoktur.", acceptanceMappingText, StringComparison.Ordinal);
            var acceptanceNarrative = Assert.Single(acceptanceMappingPage.Children
                .OfType<TextBlock>()
                .Where(block => block.Text.StartsWith(
                    "İlçe Milli Eğitim Müdürlüğü",
                    StringComparison.Ordinal)));
            Assert.Contains("TEST MEMUR TEST DÜZENLEYEN", acceptanceNarrative.Text, StringComparison.Ordinal);
            Assert.Contains("TEST ŞUBE MÜDÜRÜ TEST KONTROL EDEN", acceptanceNarrative.Text, StringComparison.Ordinal);
            Assert.Contains("01.10.2026 / 31.10.2026", acceptanceNarrative.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Aşkın KESKİNBALTA", acceptanceNarrative.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Halil ÖZENÇ", acceptanceNarrative.Text, StringComparison.Ordinal);

            var icmalOutput = Assert.Single(
                outputs.Where(output => output.Kind == OehsOfficialDocumentKind.HakedisIcmali));
            var icmalText = GetPageText(Assert.IsType<FixedPage>(icmalOutput.Document.Pages[0].Child));
            Assert.Contains("Hizmet Alımı İşi Eylül 2026", icmalText, StringComparison.Ordinal);
            Assert.Contains("Hizmet Alımı İşi Ekim 2026", icmalText, StringComparison.Ordinal);
            Assert.Contains("₺120,00", icmalText, StringComparison.Ordinal); // service base + I/R once
            Assert.Contains("₺110,00", icmalText, StringComparison.Ordinal); // service base + I/R once
            Assert.Contains("₺100,00", icmalText, StringComparison.Ordinal); // service base excludes price difference
            Assert.Contains("₺20,00", icmalText, StringComparison.Ordinal);  // September price difference appears once
            Assert.Contains("₺10,00", icmalText, StringComparison.Ordinal);  // October price difference appears once
            Assert.Contains("₺230,00", icmalText, StringComparison.Ordinal); // cumulative amount without duplicate I/R
            Assert.DoesNotContain("₺260,00", icmalText, StringComparison.Ordinal);

            // The source Excel Hakediş Raporu contains signature drawings; all four
            // PDF templates therefore use the configured signatories.
            var contractorReportPage = Assert.IsType<FixedPage>(paymentOutput.Document.Pages[0].Child);
            Assert.Contains("Örnek Yüklenici", GetPageText(contractorReportPage), StringComparison.Ordinal);
            Assert.DoesNotContain("M.Y.H.B.Y. Örnek No :4/10", GetPageText(contractorReportPage), StringComparison.Ordinal);

            foreach (var output in outputs)
            {
                var outputPage = Assert.IsType<FixedPage>(output.Document.Pages[0].Child);
                var textBlocks = GetPageText(outputPage);

                // Cross-document consistency guard: all four outputs must refer to
                // the same route/work, even though their financial layouts differ.
                Assert.Contains("Örnek Güzergâh", textBlocks, StringComparison.Ordinal);
                switch (output.Kind)
                {
                    case OehsOfficialDocumentKind.HakedisRaporu:
                    case OehsOfficialDocumentKind.HizmetIsleriHakedisRaporu:
                        Assert.Contains("31.10.2026", textBlocks, StringComparison.Ordinal);
                        Assert.Contains("Örnek Yüklenici", textBlocks, StringComparison.Ordinal);
                        break;
                    case OehsOfficialDocumentKind.KabulTutanagi:
                        Assert.Contains("01.10.2026 / 31.10.2026", textBlocks, StringComparison.Ordinal);
                        Assert.Contains("Örnek Yüklenici", textBlocks, StringComparison.Ordinal);
                        break;
                    case OehsOfficialDocumentKind.HakedisIcmali:
                        Assert.Contains("Hizmet Alımı İşi Eylül 2026", textBlocks, StringComparison.Ordinal);
                        Assert.Contains("Hizmet Alımı İşi Ekim 2026", textBlocks, StringComparison.Ordinal);
                        break;
                }

                Assert.Contains("TEST DÜZENLEYEN", textBlocks, StringComparison.Ordinal);
                Assert.Contains("TEST MEMUR", textBlocks, StringComparison.Ordinal);
                Assert.Contains("TEST KONTROL EDEN", textBlocks, StringComparison.Ordinal);
                Assert.Contains("TEST ŞUBE MÜDÜRÜ", textBlocks, StringComparison.Ordinal);
                Assert.Contains("TEST ONAYLAYAN", textBlocks, StringComparison.Ordinal);
                Assert.Contains("TEST MÜDÜR", textBlocks, StringComparison.Ordinal);
            }

            var reportOuterFrame = Assert.Single(contractorReportPage.Children
                .OfType<Border>()
                .Where(border =>
                    Math.Abs(FixedPage.GetLeft(border) - 67d) < 0.01 &&
                    Math.Abs(FixedPage.GetTop(border) - 71d) < 0.01 &&
                    Math.Abs(border.Width - 659d) < 0.01 &&
                    Math.Abs(border.Height - 1002d) < 0.01));
            Assert.Equal(1.6d, reportOuterFrame.BorderThickness.Bottom);
            var reportTitle = Assert.Single(contractorReportPage.Children
                .OfType<TextBlock>()
                .Where(block => block.Text == "HAK EDİŞ RAPORU"));
            Assert.True(
                FixedPage.GetTop(reportTitle) < FixedPage.GetTop(reportOuterFrame),
                "The Hakediş Raporu title must sit above the source workbook's top form rule.");
            
            var eRowCells = contractorReportPage.Children
                .OfType<Border>()
                .Where(border => Math.Abs(FixedPage.GetTop(border) - 329d) < 0.01)
                .ToArray();
            Assert.Equal(3, eRowCells.Length);
            Assert.All(eRowCells, border =>
                Assert.Equal(Colors.White, Assert.IsType<SolidColorBrush>(border.Background).Color));

            // The transparent outer frame must not cover the acceptance title.
            var acceptanceOutput = Assert.Single(
                outputs.Where(output => output.Kind == OehsOfficialDocumentKind.KabulTutanagi));
            var acceptancePage = Assert.IsType<FixedPage>(acceptanceOutput.Document.Pages[0].Child);
            var acceptanceFrame = Assert.Single(acceptancePage.Children
                .OfType<Border>()
                .Where(border =>
                    Math.Abs(FixedPage.GetLeft(border) - 50d) < 0.01 &&
                    Math.Abs(FixedPage.GetTop(border) - 38d) < 0.01));
            var acceptanceTitle = Assert.Single(acceptancePage.Children
                .OfType<TextBlock>()
                .Where(block => block.Text == "HİZMET İŞLERİ KABUL TUTANAĞI"));
            Assert.Equal(Colors.Transparent,Assert.IsType<SolidColorBrush>(acceptanceFrame.Background).Color);
            Assert.Equal(new Thickness(1.6),acceptanceFrame.BorderThickness);
            Assert.Contains(acceptanceTitle,acceptancePage.Children.OfType<TextBlock>());

            var configuredOutputDirectory = Environment.GetEnvironmentVariable("OEHS_PDF_SMOKE_OUTPUT_DIR");
            var cleanupTemporaryDirectory = string.IsNullOrWhiteSpace(configuredOutputDirectory);
            var temporaryDirectory = cleanupTemporaryDirectory
                ? Directory.CreateTempSubdirectory("oehs-pdf-smoke-").FullName
                : Path.GetFullPath(configuredOutputDirectory!);
            Directory.CreateDirectory(temporaryDirectory);

            var exportedPdfPaths = new List<string>(capacity: 8);

            try
            {
                foreach (var output in outputs)
                {
                    Assert.Single(output.Document.Pages);
                    var page = Assert.IsType<FixedPage>(output.Document.Pages[0].Child);
                    Assert.Equal(793.7008d, page.Width, 2);
                    Assert.Equal(1122.5197d, page.Height, 2);

                    var pdfPath = Path.Combine(temporaryDirectory, output.FileSuffix + ".pdf");
                    SaveAndAssertA4Pdf(output.Document, pdfPath);
                    exportedPdfPaths.Add(pdfPath);
                }

                // Export a separate four-document set for Basic/Secondary transport too.
                // Prefix the filenames so both program types can be reviewed side by side.
                foreach (var output in basicOutputs)
                {
                    Assert.Single(output.Document.Pages);
                    var page = Assert.IsType<FixedPage>(output.Document.Pages[0].Child);
                    Assert.Equal(793.7008d, page.Width, 2);
                    Assert.Equal(1122.5197d, page.Height, 2);

                    var pdfPath = Path.Combine(
                        temporaryDirectory,
                        "Temel_Ortaogretim_" + output.FileSuffix + ".pdf");
                    SaveAndAssertA4Pdf(output.Document, pdfPath);
                    exportedPdfPaths.Add(pdfPath);
                }

                // Acceptance requires four official documents for each distinct
                // calculation core: eight unique, materialized PDFs in total.
                Assert.Equal(8, exportedPdfPaths.Count);
                Assert.Equal(8, exportedPdfPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
                Assert.All(exportedPdfPaths, pdfPath =>
                {
                    Assert.True(File.Exists(pdfPath), $"Expected PDF was not written: {pdfPath}");
                    Assert.True(new FileInfo(pdfPath).Length > 1000, $"PDF is unexpectedly small: {pdfPath}");
                });
            }
            finally
            {
                if (cleanupTemporaryDirectory && Directory.Exists(temporaryDirectory))
                    Directory.Delete(temporaryDirectory, recursive: true);
            }
        });
    }

    private static void SaveAndAssertA4Pdf(FixedDocument document, string pdfPath)
    {
        PdfVectorWriter.Save(document, pdfPath);

        Assert.True(File.Exists(pdfPath));
        Assert.True(new FileInfo(pdfPath).Length > 1000);

        using var pdf = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
        Assert.Single(pdf.Pages);
        Assert.Equal(595.2756d, pdf.Pages[0].Width.Point, 1);
        Assert.Equal(841.8898d, pdf.Pages[0].Height.Point, 1);

        using var extracted = UglyToad.PdfPig.PdfDocument.Open(pdfPath);
        var extractedPage = extracted.GetPage(1);
        var extractedText = WithoutWhitespace(extractedPage.Text);
        var sourcePage = (FixedPage)document.Pages[0].Child;
        foreach (var block in sourcePage.Children.Cast<UIElement>().Select(element => element switch
                 {
                     TextBlock directText => directText,
                     Border { Child: TextBlock borderText } => borderText,
                     _ => null
                 }).OfType<TextBlock>())
            Assert.Contains(WithoutWhitespace(block.Text), extractedText, StringComparison.Ordinal);
        Assert.All(extractedPage.Letters, letter =>
        {
            Assert.InRange(letter.BoundingBox.Left, -0.5, extractedPage.Width + 0.5);
            Assert.InRange(letter.BoundingBox.Right, -0.5, extractedPage.Width + 0.5);
            Assert.InRange(letter.BoundingBox.Bottom, -0.5, extractedPage.Height + 0.5);
            Assert.InRange(letter.BoundingBox.Top, -0.5, extractedPage.Height + 0.5);
        });
    }

    private static string GetPageText(FixedPage page) =>
        string.Join(
            Environment.NewLine,
            page.Children.Cast<UIElement>().SelectMany(element => element switch
            {
                TextBlock directText => new[] { directText.Text },
                Border { Child: TextBlock borderText } => new[] { borderText.Text },
                _ => Array.Empty<string>()
            }));

    private static string? GetCellTextAt(FixedPage page, double left, double top) =>
        page.Children.OfType<Border>()
            .Where(border =>
                Math.Abs(FixedPage.GetLeft(border) - left) < 0.01 &&
                Math.Abs(FixedPage.GetTop(border) - top) < 0.01)
            .Select(border => (border.Child as TextBlock)?.Text)
            .SingleOrDefault();

    private static void RunOnSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured is not null)
            ExceptionDispatchInfo.Capture(captured).Throw();
    }
}
