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
internal static partial class OehsOfficialDocumentFactory
{
    private const double PageWidth = OfficialPageLayouts.PageWidth;   // A4 portrait, 96 DIP/inch
    private const double PageHeight = OfficialPageLayouts.PageHeight;
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Brush Black = Brushes.Black;
    private static readonly Brush White = Brushes.White;
    private static readonly Brush TotalGray = CreateFrozenTotalGray();
    private static readonly Brush ReportTotalGray = CreateFrozenTotalGray(192);

    private static SolidColorBrush CreateFrozenTotalGray(byte shade = 217)
    {
        var brush = new SolidColorBrush(Color.FromRgb(shade, shade, shade));
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
            BuildHakedisIcmali(context, 4));
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
        double footerY = PageHeight - 26,
        double footerX = 22)
    {
        AddText(page, "Düzenleyen", leftX, y, leftWidth, 24, 13.3, false, TextAlignment.Center);
        AddText(page, "Kontrol Eden", rightX, y, rightWidth, 24, 13.3, false, TextAlignment.Center);
        AddText(page, context.PreparerName, leftX, y + nameOffset, leftWidth, 25, 13.3, false, TextAlignment.Center);
        AddText(page, context.ControllerName, rightX, y + nameOffset, rightWidth, 25, 13.3, false, TextAlignment.Center);
        AddText(page, context.PreparerRole, leftX, y + roleOffset, leftWidth, 23, 13.3, false, TextAlignment.Center);
        AddText(page, context.ControllerRole, rightX - 20, y + roleOffset, rightWidth + 40, 23, 13.3, false, TextAlignment.Center);

        AddText(page, context.GeneratedDate.ToString("dd.MM.yyyy", Turkish), 270, y + dateOffset, 250, 24, 13.3, true, TextAlignment.Center);
        AddText(page, context.ApproverName, 270, y + directorOffset, 250, 23, 13.3, false, TextAlignment.Center);
        AddText(page, context.ApproverRole, 270, y + titleOffset, 250, 22, 13.3, false, TextAlignment.Center);
        if (!string.IsNullOrWhiteSpace(footer))
            AddText(page, footer, footerX, footerY, 330, 17, 8, false, TextAlignment.Left);
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
        block.Measure(new Size(cellHeight,double.PositiveInfinity));
        block.Height = Math.Max(block.DesiredSize.Height,fontSize*1.6);
        // Center the text line inside the rotated element on both axes:
        // TextAlignment centers along the long axis; VerticalAlignment centers
        // the line across the original cell width before the -90° rotation.
        block.VerticalAlignment = VerticalAlignment.Center;
        block.RenderTransformOrigin = new Point(0.5, 0.5);
        block.RenderTransform = new RotateTransform(angle);
        FixedPage.SetLeft(block, x + (cellWidth - cellHeight) / 2d);
        FixedPage.SetTop(block, y + (cellHeight - block.Height) / 2d);
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
        FitText(block,width-inset.Left-inset.Right-2.5,height-inset.Top-inset.Bottom-2.5);
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
        while((block.DesiredSize.Height>height || block.Text.Length>20 && PdfVectorWriter.RequiredTextHeight(block,width)>height) && block.FontSize>5)
        {
            block.FontSize=Math.Max(5,block.FontSize-0.25);
            block.Measure(new Size(Math.Max(1,width),double.PositiveInfinity));
        }
    }

    private static void AddRule(FixedPage page,double x,double y,double width,double thickness)
    {
        var line=new Border {Width=width,Height=thickness,Background=Black};
        FixedPage.SetLeft(line,x);FixedPage.SetTop(line,y);page.Children.Add(line);
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
