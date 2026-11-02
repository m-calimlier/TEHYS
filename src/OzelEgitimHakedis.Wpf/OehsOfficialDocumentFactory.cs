using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

internal sealed record OehsOfficialDocumentContext(
    string WorkName,
    string TenderNumber,
    string RouteNumber,
    string ContractorName,
    string ContractNumber,
    decimal ContractAmount,
    DateOnly? TenderDate,
    DateOnly? ContractDate,
    DateOnly? ContractStartDate,
    DateOnly? ContractEndDate,
    int ContractWorkDays,
    DateOnly? PeriodDate,
    int? ProgressPaymentNumber,
    ProgressPaymentCalculation Calculation,
    IReadOnlyList<ProgressPaymentHistoryData> Payments,
    ProgressPaymentReportTotals ReportTotals,
    decimal ReportNetPayable,
    decimal PreviousDeduction,
    DateOnly GeneratedDate,
    string PreparerName = "Aşkın KESKİNBALTA",
    string PreparerRole = "Memur",
    string ControllerName = "Halil ÖZENÇ",
    string ControllerRole = "İlçe Milli Eğitim Şube Müdürü",
    string ApproverName = "Özgür BAYSAL",
    string ApproverRole = "İlçe Milli Eğitim Müdürü",
    TransportationProgramType ProgramType = TransportationProgramType.SpecialEducation,
    string RouteName = "",
    bool IsCombined = false,
    decimal? RawTransportPriceDifference = null,
    decimal? ReportStampTax = null,
    decimal? ReportDeductions = null);

internal enum OehsOfficialDocumentKind
{
    HakedisRaporu,
    HizmetIsleriHakedisRaporu,
    KabulTutanagi,
    HakedisIcmali
}

internal sealed record OehsOfficialDocumentOutput(
    OehsOfficialDocumentKind Kind,
    string Title,
    string FileSuffix,
    FixedDocument Document);

/// <summary>
/// Builds fixed A4 pages from one shared data snapshot. Preview, printing and
/// PDF export all use these same page builders so data/layout do not drift apart.
/// </summary>
internal static class OehsOfficialDocumentFactory
{
    private const double PageWidth = 793.7007874d;   // A4 portrait, 96 DIP/inch
    private const double PageHeight = 1122.519685d;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Brush Black = Brushes.Black;
    private static readonly Brush White = Brushes.White;
    private static readonly Brush TotalGray = CreateFrozenTotalGray();

    private static SolidColorBrush CreateFrozenTotalGray()
    {
        var brush = new SolidColorBrush(Color.FromRgb(217, 217, 217));
        brush.Freeze();
        return brush;
    }
    private static readonly Brush LightGray = new SolidColorBrush(Color.FromRgb(242, 242, 242));

    public static FixedDocument BuildCombined(OehsOfficialDocumentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CreateDocument(
            BuildHakedisRaporu(context),
            BuildHizmetIsleriHakedisRaporu(context),
            BuildKabulTutanagi(context),
            BuildHakedisIcmali(context));
    }

    public static IReadOnlyList<OehsOfficialDocumentOutput> BuildSeparate(OehsOfficialDocumentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new[]
        {
            new OehsOfficialDocumentOutput(
                OehsOfficialDocumentKind.HakedisRaporu,
                "Hakediş Raporu",
                "01_Hakedis_Raporu",
                CreateDocument(BuildHakedisRaporu(context))),
            new OehsOfficialDocumentOutput(
                OehsOfficialDocumentKind.HizmetIsleriHakedisRaporu,
                "Hizmet İşleri Hakediş Raporu",
                "02_Hizmet_Isleri_Hakedis_Raporu",
                CreateDocument(BuildHizmetIsleriHakedisRaporu(context))),
            new OehsOfficialDocumentOutput(
                OehsOfficialDocumentKind.KabulTutanagi,
                "Hizmet İşleri Kabul Tutanağı",
                "03_Hizmet_Isleri_Kabul_Tutanagi",
                CreateDocument(BuildKabulTutanagi(context))),
            new OehsOfficialDocumentOutput(
                OehsOfficialDocumentKind.HakedisIcmali,
                "Hakediş İcmali",
                "04_Hakedis_Icmali",
                CreateDocument(BuildHakedisIcmali(context)))
        };
    }

    private static FixedDocument CreateDocument(params FixedPage[] pages)
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(PageWidth, PageHeight);
        foreach (var page in pages)
        {
            page.Measure(new Size(PageWidth, PageHeight));
            page.Arrange(new Rect(0, 0, PageWidth, PageHeight));
            page.UpdateLayout();

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(page);
            document.Pages.Add(pageContent);
        }
        return document;
    }

    private static FixedPage NewPage() => new()
    {
        Width = PageWidth,
        Height = PageHeight,
        Background = White,
        UseLayoutRounding = true,
        SnapsToDevicePixels = true
    };

    private static FixedPage BuildHakedisRaporu(OehsOfficialDocumentContext c)
    {
        var rawTransportPriceDifference = FindCurrentRawTransportPriceDifference(c);
        var page = NewPage();
        const double x = 67;
        const double w = 659;

        // The source workbook prints the title above the form; its outer rule
        // starts below the title and the side borders continue past the main table.
        AddOuterFrame(page, x, 56, w, 1014, openBottom: true);
        var paymentNo = c.ProgressPaymentNumber?.ToString(Turkish) ?? "-";
        var date = FormatDate(c.PeriodDate);

        AddText(page, "HAK EDİŞ RAPORU", x, 32, w, 25, 16, true, TextAlignment.Center);
        AddCell(page, BuildWorkTitle(c, includeRouteNumber: true, includeStudentSuffix: false), x, 60, 411, 87, 9.5, true, TextAlignment.Left, White, new Thickness(5, 5, 5, 3));
        AddCell(page, $"Hakediş No : {paymentNo}", x + 411, 60, w - 411, 87, 10.5, true, TextAlignment.Center);

        AddCell(page, $"{date} TARİHİNE KADAR YAPILAN İŞİN", x, 148, w, 24, 11, true, TextAlignment.Center, White);
        var amountRows = new (string Key, string Label, decimal Value)[]
        {
            ("A", "Sözleşme Fiyatları İle Yapılan İş", c.ReportTotals.CumulativeServiceAmount),
            ("B", "Fiyat Farkı Tutarı", c.ReportTotals.CumulativePriceDifference),
            ("C", "Toplam Tutar  ( A + B )", c.ReportTotals.CumulativeTotal)
        };
        var y = 172d;
        foreach (var row in amountRows)
        {
            var bold = row.Key == "C";
            AddCell(page, row.Key, x, y, 53, 29, 10, bold, TextAlignment.Center);
            AddCell(page, row.Label, x + 53, y, 356, 29, 10, bold, TextAlignment.Left);
            AddCell(page, FormatMoney(row.Value), x + 409, y, w - 409, 29, 10, true, TextAlignment.Right, White, new Thickness(3, 2, 8, 2));
            y += 29;
        }

        y += 34;
        var summaryRows = new (string Key, string Label, decimal Value, Brush Fill, double Height)[]
        {
            ("D", "Bir Önceki Hakedişlerin Toplam Tutarı", c.ReportTotals.PreviousTotal, White, 29),
            ("E", "Bu Hakedişin Tutarı ( C - D )", c.ReportTotals.CurrentTotal, White, 29),
            ("F", BuildVatRateLabel(c.Calculation, c.ProgramType), c.Calculation.Vat, White, 29),
            ("G", "Tahakkuk Tutarı", c.ReportTotals.CurrentTotal + c.Calculation.Vat, TotalGray, 32)
        };
        foreach (var row in summaryRows)
        {
            var bold = row.Key is "G" or "E";
            AddCell(page, row.Key, x, y, 53, row.Height, 10, bold, TextAlignment.Center, row.Fill);
            AddCell(page, row.Label, x + 53, y, 356, row.Height, 10, bold, TextAlignment.Left, row.Fill);
            var formattedValue = row.Key == "D" && c.ProgramType == TransportationProgramType.SpecialEducation && row.Value != 0m
                ? FormatMoney(row.Value, decimalPlaces: 3)
                : FormatMoney(row.Value);
            AddCell(page, formattedValue, x + 409, y, w - 409, row.Height, 10, true, TextAlignment.Right, row.Fill, new Thickness(3, 2, 8, 2));
            y += row.Height;
        }

        var deductions = new (string Key, string Label, decimal Value, double Height)[]
        {
            ("a)", "Gelir / Kurumlar Vergisi ( E x % .. )", 0m, 29),
            ("b)", BuildStampTaxLabel(c.Calculation), c.ReportStampTax ?? ProgressPaymentReportService.CalculateReportStampTax(c.Calculation, c.ProgramType, rawTransportPriceDifference), 29),
            ("c)", BuildWithholdingLabel(c.Calculation, c.ProgramType), c.Calculation.Withholding, 29),
            ("d)", "Sosyal Sigortalar Kurumu Kesintisi", c.Calculation.Deductions.SocialSecurityDebt, 29),
            ("e)", "İdare Makinesi Kiraları", 0m, 29),
            ("f)", "Gecikme Cezası", 0m, 29),
            ("g)", "Avans Mahsubu", 0m, 29),
            ("h)", "Bu Hakedişle Ödenen Fiyat Farkı Teminat Kesintisi", c.Calculation.AdditionalGuarantee, 37),
            ("ı)", "Vergi Borcu", c.Calculation.Deductions.TaxDebt, 29),
            ("j)", "İcra Borcu", 0m, 29),
            ("k)", "Ceza ve Diğer Kesintiler", c.Calculation.Deductions.InspectionPenalty, 29),
            ("l)", "Asgari Ücret Destek Tutarı Kesintisi", c.Calculation.Deductions.MinimumWageSupport, 29)
        };
        var deductionsHeight = deductions.Sum(d => d.Height);
        // Keep the section title vertically oriented and centered in the merged left cell.
        AddCell(page, "", x, y, 165, deductionsHeight, 9, false, TextAlignment.Center, White, new Thickness(0));
        AddRotatedText(page, "KESİNTİLER VE MAHSUPLAR", x, y, 165, deductionsHeight, 12, true, -90);

        foreach (var row in deductions)
        {
            AddCell(page, row.Key, x + 165, y, 40, row.Height, 9, false, TextAlignment.Center, White, new Thickness(2, 1, 2, 1));
            AddCell(page, row.Label, x + 205, y, 207, row.Height, 9, false, TextAlignment.Left, White, new Thickness(3, 1, 2, 1));
            AddCell(page, FormatMoney(row.Value), x + 412, y, w - 412, row.Height, 9, true, TextAlignment.Right, White, new Thickness(3, 1, 8, 1));
            y += row.Height;
        }

        AddCell(page, "H", x, y, 53, 28, 10, true, TextAlignment.Center, TotalGray);
        AddCell(page, "Kesintiler ve Mahsuplar Toplamı", x + 53, y, 356, 28, 10, true, TextAlignment.Left, TotalGray);
        AddCell(page, FormatMoney(c.ReportDeductions ?? ProgressPaymentReportService.CalculateReportTotalDeductions(c.Calculation, c.ProgramType, rawTransportPriceDifference)), x + 409, y, w - 409, 28, 10, true, TextAlignment.Right, TotalGray, new Thickness(3, 2, 8, 2));
        y += 28;
        AddCell(page, "I", x, y, 53, 28, 10, true, TextAlignment.Center, TotalGray);
        AddCell(page, "Yükleniciye Ödenecek Tutar  ( G - H )", x + 53, y, 356, 28, 10, true, TextAlignment.Left, TotalGray);
        AddCell(page, FormatMoney(c.ReportNetPayable), x + 409, y, w - 409, 28, 10, true, TextAlignment.Right, TotalGray, new Thickness(3, 2, 8, 2));

        y += 29;
        var footerY = Math.Max(y + 12, 790);
        AddText(page, "Yüklenici", x + 45, footerY, w - 90, 22, 10, true, TextAlignment.Left);
        AddText(page, c.ContractorName, x + 12, footerY + 23, w - 24, 36, 9.5, false, TextAlignment.Left);
        AddSignatures(page, footerY + 45, c, "M.Y.H.B.Y. Örnek No :4/10",
            leftX: 115, leftWidth: 205, rightX: 435, rightWidth: 200,
            nameOffset: 34, roleOffset: 53, dateOffset: 72, directorOffset: 108, titleOffset: 126, footerY: 1080);
        return page;
    }

    private static decimal FindCurrentRawTransportPriceDifference(OehsOfficialDocumentContext context)
    {
        if(context.RawTransportPriceDifference.HasValue)return context.RawTransportPriceDifference.Value;
        var currentPayment = context.Payments
            .Where(payment => payment.ProgressPaymentNumber == context.ProgressPaymentNumber)
            .Where(payment => payment.PeriodDate == context.PeriodDate)
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefault();

        return currentPayment?.Input.PriceDifference
               ?? context.Calculation.PriceDifferenceExcludingVat;
    }

    private static FixedPage BuildHizmetIsleriHakedisRaporu(OehsOfficialDocumentContext c)
    {
        var page = NewPage();
        const double x = 42;
        const double w = 710;

        // Frame encloses the form body; the signature block remains below it,
        // following the uploaded one-page reference PDF.
        AddCell(page, "", x, 25, w, 835, 9, false, TextAlignment.Left, White, new Thickness(0));
        AddText(page, "HİZMET İŞLERİ HAK EDİŞ RAPORU", x, 29, w, 30, 15, true, TextAlignment.Center);

        AddText(page, "Tarihi", x + 145, 81, 155, 19, 9.5, true, TextAlignment.Left);
        AddText(page, ":", x + 326, 81, 14, 19, 9.5, true, TextAlignment.Center);
        AddText(page, FormatDate(c.GeneratedDate), x + 366, 81, 360, 19, 9.5, false, TextAlignment.Left);
        AddText(page, "No.su", x + 145, 99, 155, 19, 9.5, true, TextAlignment.Left);
        AddText(page, ":", x + 326, 99, 14, 19, 9.5, true, TextAlignment.Center);
        AddText(page, string.IsNullOrWhiteSpace(c.RouteNumber) ? "-" : c.RouteNumber.Trim(), x + 366, 99, 360, 19, 9.5, false, TextAlignment.Left);
        AddText(page, "Uygulama Yılı", x + 145, 117, 155, 19, 9.5, true, TextAlignment.Left);
        AddText(page, ":", x + 326, 117, 14, 19, 9.5, true, TextAlignment.Center);
        AddText(page, (c.PeriodDate?.Year ?? c.GeneratedDate.Year).ToString(Turkish), x + 366, 117, 360, 19, 9.5, false, TextAlignment.Left);

        AddText(page, $"İKN: {c.TenderNumber}", x + 356, 151, w - 360, 20, 8.6, false, TextAlignment.Left);
        AddText(page, "Yapılan Hizmetin Adı", x + 2, 173, 310, 62, 9.5, true, TextAlignment.Left);
        AddText(page, ":", x + 324, 173, 14, 62, 9.5, true, TextAlignment.Center);
        var serviceName = BuildWorkTitle(c, includeRouteNumber: false, includeStudentSuffix: true);
        // Older snapshots can carry a separate contract work description; retain it
        // alongside the route title, without repeating the same derived name.
        if (!string.IsNullOrWhiteSpace(c.WorkName)
            && !string.Equals(c.WorkName.Trim(), serviceName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(c.WorkName.Trim(), c.RouteName.Trim(), StringComparison.OrdinalIgnoreCase))
            serviceName += "\n" + c.WorkName.Trim();
        AddText(page, serviceName, x + 356, 173, w - 360, 62, 8.6, true, TextAlignment.Left);

        var info = new (string Label, string Value)[]
        {
            ("Yapılan Hizmetin Etüt / Proje No.su", "Proje Dışıdır."),
            ("Yüklenicinin Adı / Ticari Unvanı", c.ContractorName),
            ("Sözleşme Bedeli", FormatMoney(c.ContractAmount)),
            ("İhale Tarihi", FormatDate(c.TenderDate)),
            ("Sözleşme Tarihi ve No.su", FormatDate(c.ContractDate)),
            ("Sözleşmenin Sayıştayca Tescil Tarihi ve No'su", "Tescile Dahil Değil"),
            ("Tescilin Yükleniciye Tebliğ Tarihi", "Tescile Dahil Değil"),
            ("İşyeri Teslim Tarihi", FormatDate(c.ContractStartDate)),
            ("Sözleşmeye Göre İşin Süresi", FormatWorkDays(c)),
            ("Sözleşmeye Göre İş Bitim Tarihi", FormatDate(c.ContractEndDate)),
            ("Verilen Avanslar Toplamı", "Avans verilmeyecektir."),
            ("Mahsubu Yapılan Avansın Toplam Tutarı", "Yoktur.")
        };
        var infoY = 239d;
        foreach (var row in info)
        {
            var height = row.Label == "Yüklenicinin Adı / Ticari Unvanı" ? 42d : 25d;
            AddText(page, row.Label, x + 2, infoY, 320, height, 9.3, true, TextAlignment.Left);
            AddText(page, ":", x + 324, infoY, 14, height, 9.3, true, TextAlignment.Center);
            AddText(page, row.Value, x + 356, infoY, w - 360, height, 9.3, false, TextAlignment.Left);
            infoY += height + 2;
        }

        var y = infoY + 15;
        var colWidths = new[] { 160d, 190d, 170d, w - 160d - 190d - 170d };
        var headings = new[]
        {
            "Sözleşme Bedeli",
            "Sözleşme Artış\nOnayının Tarihi No.su",
            "Ek Sözleşme\nBedeli",
            "Toplam Sözleşme\nBedeli"
        };
        var tx = x;
        for (var i = 0; i < colWidths.Length; i++)
        {
            AddCell(page, headings[i], tx, y, colWidths[i], 32, 8.8, false, TextAlignment.Center);
            tx += colWidths[i];
        }
        y += 32;
        for (var row = 0; row < 6; row++)
        {
            var values = row == 0
                ? new[] { FormatMoney(c.ContractAmount, false), "....................", "....................", FormatMoney(c.ContractAmount, false) }
                : new[] { "....................", "....................", "....................", "...................." };
            tx = x;
            for (var col = 0; col < colWidths.Length; col++)
            {
                AddCell(page, values[col], tx, y, colWidths[col], 19, 8.2, false, TextAlignment.Center);
                tx += colWidths[col];
            }
            y += 19;
        }

        AddCell(page, "Süre Uzatım Kararlarının", x, y, w, 20, 9.2, false, TextAlignment.Left);
        y += 20;
        var extensionWidths = new[] { 135d, 130d, 220d, w - 135d - 130d - 220d };
        var extensionHeads = new[] { "Tarih", "Sayısı", "Verilen Süre", "İş Bitim Tarihi" };
        tx = x;
        for (var i = 0; i < extensionWidths.Length; i++)
        {
            AddCell(page, extensionHeads[i], tx, y, extensionWidths[i], 22, 8.5, false, TextAlignment.Center);
            tx += extensionWidths[i];
        }
        y += 22;
        for (var row = 0; row < 4; row++)
        {
            tx = x;
            for (var col = 0; col < extensionWidths.Length; col++)
            {
                AddCell(page, ".................", tx, y, extensionWidths[col], 15, 8, false, TextAlignment.Center);
                tx += extensionWidths[col];
            }
            y += 15;
        }

        AddSignatures(page, 878, c, "M.Y.H.B.Y. Örnek No: 4",
            leftX: 100, leftWidth: 220, rightX: 480, rightWidth: 220,
            nameOffset: 40, roleOffset: 61, dateOffset: 121, directorOffset: 163, titleOffset: 181, footerY: 1080);
        return page;
    }

    private static FixedPage BuildKabulTutanagi(OehsOfficialDocumentContext c)
    {
        var page = NewPage();
        const double x = 50;
        const double w = 693;
        // Draw the white form frame before its contents; otherwise it covers the title.
        AddCell(page, "", x, 38, w, 414, 9, false, TextAlignment.Left, White, new Thickness(0));
        AddText(page, "HİZMET İŞLERİ KABUL TUTANAĞI", x, 40, w, 22, 15, true, TextAlignment.Center);
        AddText(page, $"İKN: {c.TenderNumber}", x, 18, w, 18, 8.6, false, TextAlignment.Left);

        var servicePeriod = ProgressPaymentAcceptancePeriodService.BuildServicePeriod(
            c.PeriodDate ?? c.GeneratedDate,
            c.ContractStartDate,
            c.ContractEndDate);
        var endDate = c.ContractEndDate;
        var rows = new (string Label, string Value, double Height)[]
        {
            ("İşin adı", BuildWorkTitle(c, includeRouteNumber: false, includeStudentSuffix: true), 112),
            ("Yüklenicinin adı/ticari unvanı", c.ContractorName, 35),
            ("Sözleşme tarihi", FormatDate(c.ContractDate), 35),
            ("Sözleşme bedeli", FormatMoney(c.ContractAmount), 35),
            ("Sözleşmeye göre işin süresi (takvim günü)", FormatAcceptanceDuration(c), 35),
            ("Sözleşmeye göre işin bitirilmesi gereken tarih", FormatDate(endDate), 35),
            ("Varsa süre uzatımları", "", 35),
            ("Süre uzatımı dahil işin bitirilmesi gereken tarih", FormatDate(endDate), 35),
            ("İşin bitirildiği tarih", FormatLongDate(servicePeriod.End), 35)
        };

        var y = 60d;
        foreach (var row in rows)
        {
            AddCell(page, row.Label, x, y, 310, row.Height, 9.5, true, TextAlignment.Left);
            AddCell(page, row.Value, x + 310, y, w - 310, row.Height, 9.5, false, TextAlignment.Left);
            y += row.Height;
        }

        var paragraphOne =
            $"İlçe Milli Eğitim Müdürlüğü'nün {BuildWorkTitle(c, includeRouteNumber: true, includeStudentSuffix: false)} yüklenicisi {c.ContractorName} arasında imzalanan sözleşme kapsamında gerçekleştirilen iş için, {c.PreparerRole} {c.PreparerName} ve {c.ControllerRole} {c.ControllerName} tarafından inceleme yapılmış yüklenici, {c.ContractorName} tarafından {servicePeriod.Start:dd.MM.yyyy} / {servicePeriod.End:dd.MM.yyyy} tarihleri arasında yapılan işlerin,";
        var paragraphTwo =
            "Sözleşme ve eklerine uygun olduğu ve kabule engel olabilecek eksik kusur ve arızaların bulunmadığı görülmüştür.";
        // The two source workbooks use different acceptance-form print layouts:
        // Basic/Secondary places the narrative immediately below the table;
        // Special Education leaves a larger gap. Keep both aligned to their source.
        var paragraphOneY = c.ProgramType == TransportationProgramType.BasicOrSecondary ? 448d : 501d;
        var paragraphTwoY = c.ProgramType == TransportationProgramType.BasicOrSecondary ? 618d : 651d;
        AddText(page, paragraphOne, x + 2, paragraphOneY, w - 4, 124, 9.5, false, TextAlignment.Justify);
        AddText(page, paragraphTwo, x + 2, paragraphTwoY, w - 4, 44, 9.5, false, TextAlignment.Justify);

        AddText(page, "Yüklenici", x + 28, 775, w - 56, 20, 10, true, TextAlignment.Center);
        AddText(page, c.ContractorName, x + 28, 796, w - 56, 30, 9.5, false, TextAlignment.Center);
        AddSignatures(page, 894, c, "",
            leftX: 100, leftWidth: 220, rightX: 480, rightWidth: 220,
            nameOffset: 35, roleOffset: 53, dateOffset: 88, directorOffset: 124, titleOffset: 142, footerY: 1087);
        return page;
    }

    private static FixedPage BuildHakedisIcmali(OehsOfficialDocumentContext c)
    {
        var page = NewPage();
        const double x = 20;
        const double w = 754;
        AddText(page, "HAKEDİŞ İCMALİ", x, 16, w, 28, 15, true, TextAlignment.Center);

        AddCell(page, BuildWorkTitle(c, includeRouteNumber: false, includeStudentSuffix: true), x, 47, 574, 70, 8.8, true, TextAlignment.Left, White, new Thickness(5, 3, 5, 2));
        AddCell(page, "Sayfa No : 1", x + 574, 47, w - 574, 23.3, 9.5, true, TextAlignment.Left);
        AddCell(page, $"Hakediş No : {c.ProgressPaymentNumber?.ToString(Turkish) ?? "-"}", x + 574, 70.3, w - 574, 23.3, 9.5, true, TextAlignment.Left);
        if(!c.IsCombined)AddCell(page, $"Hat No : {c.RouteNumber}", x + 574, 93.6, w - 574, 23.4, 9.5, true, TextAlignment.Left);

        var widths = new[] { 69d, 202d, 195d, 107d, w - 69d - 202d - 195d - 107d };
        var headers = new[] { "Sıra No", "İşin Cinsi", "İş Tutarı\n(a)", "Fiyat Farkı\nTutarı\n(b)", "Toplam\n(a+b)" };
        var y = 117d;
        var xx = x;
        for (var i = 0; i < widths.Length; i++)
        {
            AddCell(page, headers[i], xx, y, widths[i], 50, 9.5, true, TextAlignment.Center, White);
            xx += widths[i];
        }
        y += 50;

        var periods = ProgressPaymentIcmalService.BuildPeriods(c.PeriodDate ?? c.GeneratedDate);
        var icmalRows = new List<(DateOnly Month, decimal Service, decimal Difference)>();
        foreach (var month in periods)
        {
            var monthPayments = c.Payments
                // Reprinting an older payment must not include later issued periods.
                .Where(p => p.PeriodDate <= (c.PeriodDate ?? c.GeneratedDate))
                .Where(p => p.PeriodDate.HasValue &&
                            p.PeriodDate.Value.Year == month.Year &&
                            p.PeriodDate.Value.Month == month.Month)
                .ToArray();
            // Unlike the source workbook's blank Special Education July placeholder,
            // an entered July payment must appear in the generated icmal.
            var isLookupMonth = ProgressPaymentIcmalService.IsMonthInWorkbookLookupRange(
                month,
                c.ProgramType)
                || c.ProgramType == TransportationProgramType.SpecialEducation
                && month.Month == 7
                && month <= (c.PeriodDate ?? c.GeneratedDate);
            // Reportable components can cancel in the monthly totals. Keep their
            // month visible, while excluding deductions and out-of-program guide inputs.
            var hasReportableAmount = isLookupMonth && monthPayments.Any(payment =>
            {
                var input = payment.Input;
                return PaymentRatePeriodService.ServiceAmount(input.TransportPeriods,input.WorkDays,input.DailyPrice) != 0m
                       || input.DiscountAmount != 0m
                       || input.PriceDifference != 0m
                       || input.ProgramType == TransportationProgramType.SpecialEducation
                       && (PaymentRatePeriodService.ServiceAmount(input.GuidePeriods,input.GuideWorkDays,input.GuideDailyPrice) != 0m
                           || input.GuideDiscountAmount != 0m
                           || input.GuidePriceDifference != 0m);
            });
            // The source K (Basic) and K+T (Special) monthly totals include raw I/R
            // price differences. The icmal separates "İş Tutarı" from "Fiyat Farkı",
            // so show the contract-price base in column (a) and I/R once in column (b).
            var service = isLookupMonth
                ? monthPayments.Sum(ProgressPaymentReportService.CalculateWorkbookMonthlyPaymentAmount)
                : 0m;
            var difference = isLookupMonth
                ? monthPayments.Sum(ProgressPaymentReportService.CalculateWorkbookPriceDifference)
                : 0m;

            // Compact months with reportable components; genuinely empty months stay blank.
            if (hasReportableAmount)
                icmalRows.Add((month, service, difference));
        }

        var sumService = icmalRows.Sum(row => row.Service);
        var sumDifference = icmalRows.Sum(row => row.Difference);
        for (var index = 0; index < periods.Count; index++)
        {
            var values = index < icmalRows.Count
                ? new[]
                {
                    (index + 1).ToString(Turkish),
                    "Hizmet Alımı İşi " + icmalRows[index].Month.ToString("MMMM yyyy", Turkish),
                    FormatLira(icmalRows[index].Service),
                    FormatLira(icmalRows[index].Difference),
                    FormatLira(icmalRows[index].Service + icmalRows[index].Difference)
                }
                : new[] { (index + 1).ToString(Turkish), "", "", "", "" };

            xx = x;
            for (var col = 0; col < widths.Length; col++)
            {
                AddCell(page, values[col], xx, y, widths[col], 32, 9, col == 0, col == 0 ? TextAlignment.Center : col == 1 ? TextAlignment.Left : TextAlignment.Center,
                    White, new Thickness(4, 2, 4, 2));
                xx += widths[col];
            }
            y += 32;
        }

        var totalWidths = new[] { widths[0] + widths[1], widths[2], widths[3] };
        var totalX = new[]
        {
            x,
            x + widths[0] + widths[1],
            x + widths[0] + widths[1] + widths[2]
        };
        var totals = new[] { "Toplam", FormatLira(sumService), FormatLira(sumDifference) };
        for (var col = 0; col < totalWidths.Length; col++)
            AddCell(page, totals[col], totalX[col], y, totalWidths[col], 32, 9.5, true, TextAlignment.Center, TotalGray);

        // The workbook's total amount cell spans the monthly subtotal row and
        // the separate A/TOPLAM row on the right edge.
        AddCell(
            page,
            FormatLira(sumService + sumDifference),
            x + widths[0] + widths[1] + widths[2] + widths[3],
            y,
            widths[4],
            65,
            10,
            true,
            TextAlignment.Center,
            TotalGray);
        y += 32;
        AddCell(page, "A", x, y, widths[0], 33, 10, true, TextAlignment.Center, TotalGray);
        AddCell(page, "T  O  P  L  A  M", x + widths[0], y, widths[1] + widths[2] + widths[3], 33, 10, true, TextAlignment.Center, TotalGray);
        y += 51;

        AddCell(page, "", x, y, widths[0], 29, 9, false, TextAlignment.Center, White);
        AddCell(page, "Hakediş İçeriğinden Yapılan Kesintiler", x + widths[0], y, w - widths[0], 29, 10, true, TextAlignment.Center, White);
        y += 29;
        var amountX = x + w - widths[4];
        var labelX = x + widths[0];
        var labelWidth = w - widths[0] - widths[4];
        AddCell(page, "a)", x, y, widths[0], 29, 9.5, false, TextAlignment.Center);
        AddCell(page, "Bir Evvelki Hakedişten Yapılan Kesinti", labelX, y, labelWidth, 29, 9.5, false, TextAlignment.Left);
        AddCell(page, FormatMoney(c.PreviousDeduction, false), amountX, y, widths[4], 29, 9.5, false, TextAlignment.Right, White, new Thickness(3, 2, 8, 2));
        y += 29;
        AddCell(page, "b)", x, y, widths[0], 29, 9.5, false, TextAlignment.Center);
        AddCell(page, "Bu Hakedişten Yapılan Kesinti", labelX, y, labelWidth, 29, 9.5, false, TextAlignment.Left);
        AddCell(page, FormatMoney(c.Calculation.TotalDeductions, false), amountX, y, widths[4], 29, 9.5, false, TextAlignment.Right, White, new Thickness(3, 2, 8, 2));
        y += 29;
        AddCell(page, "B", x, y, widths[0], 30, 10, true, TextAlignment.Center, TotalGray);
        AddCell(page, "Toplam Kesinti Tutarı", labelX, y, labelWidth, 30, 10, true, TextAlignment.Right, TotalGray, new Thickness(3, 2, 8, 2));
        AddCell(page, FormatMoney(c.PreviousDeduction + c.Calculation.TotalDeductions, false), amountX, y, widths[4], 30, 10, true, TextAlignment.Right, TotalGray, new Thickness(3, 2, 8, 2));
        y += 46;
        AddCell(page, "C", x, y, widths[0], 30, 10, true, TextAlignment.Center, TotalGray);
        AddCell(page, "Toplam Hakediş Tutarı : ( A - B )", labelX, y, labelWidth, 30, 10, true, TextAlignment.Right, TotalGray, new Thickness(3, 2, 8, 2));
        AddCell(page, FormatMoney(sumService + sumDifference - c.PreviousDeduction - c.Calculation.TotalDeductions, false), amountX, y, widths[4], 30, 10, true, TextAlignment.Right, TotalGray, new Thickness(3, 2, 8, 2));

        AddSignatures(page, 858, c, "M.Y.H.B.Y. Örnek No :4/9",
            leftX: 80, leftWidth: 200, rightX: 540, rightWidth: 190,
            nameOffset: 37, roleOffset: 55, dateOffset: 92, directorOffset: 130, titleOffset: 148, footerY: 1063);
        return page;
    }

    private static void AddSignatures(
        FixedPage page,
        double y,
        OehsOfficialDocumentContext context,
        string footer,
        double leftX = 100,
        double leftWidth = 220,
        double rightX = 482,
        double rightWidth = 220,
        double nameOffset = 34,
        double roleOffset = 57,
        double dateOffset = 102,
        double directorOffset = 142,
        double titleOffset = 164,
        double footerY = PageHeight - 26)
    {
        AddText(page, "Düzenleyen", leftX, y, leftWidth, 24, 10, false, TextAlignment.Center);
        AddText(page, "Kontrol Eden", rightX, y, rightWidth, 24, 10, false, TextAlignment.Center);
        AddText(page, context.PreparerName, leftX, y + nameOffset, leftWidth, 25, 10, true, TextAlignment.Center);
        AddText(page, context.ControllerName, rightX, y + nameOffset, rightWidth, 25, 10, true, TextAlignment.Center);
        AddText(page, context.PreparerRole, leftX, y + roleOffset, leftWidth, 23, 9.5, false, TextAlignment.Center);
        AddText(page, context.ControllerRole, rightX - 20, y + roleOffset, rightWidth + 40, 23, 9.5, false, TextAlignment.Center);

        AddText(page, context.GeneratedDate.ToString("dd.MM.yyyy", Turkish), 270, y + dateOffset, 250, 24, 10, true, TextAlignment.Center);
        AddText(page, context.ApproverName, 270, y + directorOffset, 250, 23, 10, true, TextAlignment.Center);
        AddText(page, context.ApproverRole, 270, y + titleOffset, 250, 22, 9.5, false, TextAlignment.Center);
        if (!string.IsNullOrWhiteSpace(footer))
            AddText(page, footer, 22, footerY, 330, 17, 8, false, TextAlignment.Left);
    }

    private static ProgressPaymentCalculationConstants AppliedConstants(ProgressPaymentCalculation calculation) =>
        calculation.AppliedConstants ?? ProgressPaymentCalculationConstants.Defaults;

    private static string FormatPercent(decimal fraction) =>
        (fraction * 100m).ToString("0.###", Turkish);

    private static string BuildStampTaxLabel(ProgressPaymentCalculation calculation)
    {
        var rate = AppliedConstants(calculation).StampTaxRate;
        return $"Damga Vergisi (oran %{FormatPercent(rate)})";
    }

    private static string BuildVatRateLabel(
        ProgressPaymentCalculation calculation,
        TransportationProgramType programType)
    {
        var rates = AppliedConstants(calculation);
        var transport = $"taşıma %{FormatPercent(rates.TransportVatRate)}";
        return programType == TransportationProgramType.BasicOrSecondary
            ? $"KDV ({transport})"
            : $"KDV ({transport} + rehber %{FormatPercent(rates.GuideVatRate)})";
    }

    private static string BuildWithholdingLabel(
        ProgressPaymentCalculation calculation,
        TransportationProgramType programType)
    {
        var rates = AppliedConstants(calculation);
        static string Share(decimal value) => value switch
        {
            0.5m => "5/10",
            0.9m => "9/10",
            _ => $"%{FormatPercent(value)}"
        };

        var transport = $"taşıma {Share(rates.TransportWithholdingShare)}";
        return programType == TransportationProgramType.BasicOrSecondary
            ? $"KDV Tevkifatı ({transport})"
            : $"KDV Tevkifatı ({transport}; rehber {Share(rates.GuideWithholdingShare)})";
    }

    private static string BuildWorkTitle(
        OehsOfficialDocumentContext context,
        bool includeRouteNumber,
        bool includeStudentSuffix)
    {
        if(context.IsCombined)return context.WorkName;
        var work = (string.IsNullOrWhiteSpace(context.RouteName) ? context.WorkName : context.RouteName).Trim();
        var routeNumber = string.IsNullOrWhiteSpace(context.RouteNumber)
            ? "-"
            : context.RouteNumber.Trim();

        if (work.Length == 0)
            work = "Hizmet Alımı İşi";

        var existingPrefix = $"{routeNumber} Numaralı ";
        if (!string.Equals(routeNumber, "-", StringComparison.Ordinal) &&
            work.StartsWith(existingPrefix, StringComparison.OrdinalIgnoreCase))
        {
            work = work[existingPrefix.Length..].TrimStart();
        }

        // Normalize a route name that may already include its display suffix.
        if (work.EndsWith(" Hattı Öğrenci Taşıma İşi", StringComparison.OrdinalIgnoreCase))
            work = work[..^" Hattı Öğrenci Taşıma İşi".Length].TrimEnd();
        else if (work.EndsWith(" Hattı Taşıma İşi", StringComparison.OrdinalIgnoreCase))
            work = work[..^" Hattı Taşıma İşi".Length].TrimEnd();
        else if (work.EndsWith(" Hattı", StringComparison.OrdinalIgnoreCase))
            work = work[..^" Hattı".Length].TrimEnd();

        var prefix = includeRouteNumber && !string.Equals(routeNumber, "-", StringComparison.Ordinal)
            ? $"{routeNumber} Numaralı "
            : string.Empty;
        var suffix = includeStudentSuffix ? "Hattı Öğrenci Taşıma İşi" : "Hattı Taşıma İşi";
        return $"{prefix}{work} {suffix}";
    }

    private static string FormatWorkDays(OehsOfficialDocumentContext c) =>
        c.ProgramType == TransportationProgramType.BasicOrSecondary &&
        c.ContractStartDate.HasValue && c.ContractEndDate.HasValue
            ? $"{FormatDate(c.ContractStartDate)}-{FormatDate(c.ContractEndDate)} Tarihleri Arasıdır."
            : c.ContractWorkDays > 0
                ? $"{c.ContractWorkDays} İş Günü"
                : "-";

    private static string FormatAcceptanceDuration(OehsOfficialDocumentContext c) =>
        c.ContractWorkDays <= 0
            ? "-"
            : c.ProgramType == TransportationProgramType.BasicOrSecondary
                ? c.ContractWorkDays.ToString(Turkish)
                : $"{c.ContractWorkDays} İş Günü";


    private static string FormatDate(DateOnly? date) =>
        date?.ToString("dd.MM.yyyy", Turkish) ?? "-";

    private static string FormatLongDate(DateOnly? date) =>
        date?.ToDateTime(TimeOnly.MinValue).ToString("dd MMMM yyyy dddd", Turkish) ?? "-";

    private static string FormatMoney(decimal value, bool withCurrency = true, int decimalPlaces = 2)
    {
        var format = decimalPlaces == 3 ? "#,##0.000" : "#,##0.00";
        var formatted = value.ToString(format, Turkish);
        return withCurrency ? formatted + " TL" : formatted;
    }

    private static string FormatLira(decimal value) =>
        "₺" + value.ToString("#,##0.00", Turkish);

    private static void AddText(
        FixedPage page,
        string text,
        double x,
        double y,
        double width,
        double height,
        double fontSize,
        bool bold,
        TextAlignment alignment,
        Brush? foreground = null)
    {
        var block = MakeText(text, fontSize, bold, alignment, foreground, new Thickness(0));
        FitText(block,width,height);
        block.Width = width;
        block.Height = height;
        FixedPage.SetLeft(block, x);
        FixedPage.SetTop(block, y);
        page.Children.Add(block);
    }

    private static void AddOuterFrame(
        FixedPage page,
        double x,
        double y,
        double width,
        double height,
        bool openBottom = false)
    {
        var frame = new Border
        {
            Width = width,
            Height = height,
            BorderBrush = Black,
            BorderThickness = openBottom
                ? new Thickness(1.6, 1.6, 1.6, 0)
                : new Thickness(1.6),
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true
        };
        FixedPage.SetLeft(frame, x);
        FixedPage.SetTop(frame, y);
        page.Children.Add(frame);
    }

    private static void AddRotatedText(
        FixedPage page,
        string text,
        double x,
        double y,
        double cellWidth,
        double cellHeight,
        double fontSize,
        bool bold,
        double angle)
    {
        // Keep the original font, size and rotation. Place the rotated element
        // so its visual bounds are centered in the full merged deductions cell.
        // PdfVectorWriter explicitly supports RenderTransform rotation.
        var block = MakeText(text, fontSize, bold, TextAlignment.Center, Black, new Thickness(0));
        block.TextWrapping = TextWrapping.NoWrap;
        block.Width = cellHeight;
        block.Height = cellWidth;
        // Center the text line inside the rotated element on both axes:
        // TextAlignment centers along the long axis; VerticalAlignment centers
        // the line across the original cell width before the -90° rotation.
        block.VerticalAlignment = VerticalAlignment.Center;
        block.RenderTransformOrigin = new Point(0.5, 0.5);
        block.RenderTransform = new RotateTransform(angle);
        FixedPage.SetLeft(block, x + (cellWidth - cellHeight) / 2d);
        FixedPage.SetTop(block, y + (cellHeight - cellWidth) / 2d);
        page.Children.Add(block);
    }

    private static void AddCell(
        FixedPage page,
        string text,
        double x,
        double y,
        double width,
        double height,
        double fontSize,
        bool bold,
        TextAlignment alignment,
        Brush? background = null,
        Thickness? padding = null)
    {
        var block = MakeText(text, fontSize, bold, alignment, Black, new Thickness(0));
        var inset=padding ?? new Thickness(5,2,5,2);
        FitText(block,width-inset.Left-inset.Right-1.3,height-inset.Top-inset.Bottom-1.3);
        var border = new Border
        {
            Width = width,
            Height = height,
            Background = background ?? White,
            BorderBrush = Black,
            BorderThickness = new Thickness(0.65),
            Padding = padding ?? new Thickness(5, 2, 5, 2),
            Child = block,
            SnapsToDevicePixels = true
        };
        FixedPage.SetLeft(border, x);
        FixedPage.SetTop(border, y);
        page.Children.Add(border);
    }

    private static void FitText(TextBlock block,double width,double height)
    {
        block.Measure(new Size(Math.Max(1,width),double.PositiveInfinity));
        while(block.DesiredSize.Height>height && block.FontSize>5)
        {
            block.FontSize=Math.Max(5,block.FontSize-0.25);
            block.Measure(new Size(Math.Max(1,width),double.PositiveInfinity));
        }
    }

    private static TextBlock MakeText(
        string text,
        double fontSize,
        bool bold,
        TextAlignment alignment,
        Brush? foreground,
        Thickness margin) => new()
    {
        Text = text ?? string.Empty,
        FontFamily = new FontFamily("Arial"),
        FontSize = fontSize,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Foreground = foreground ?? Black,
        TextAlignment = alignment,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Margin = margin,
        UseLayoutRounding = true,
        SnapsToDevicePixels = true
    };
}
