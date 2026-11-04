using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

internal static partial class OehsOfficialDocumentFactory
{
    private static FixedPage BuildHakedisRaporu(OehsOfficialDocumentContext c)
    {
        var rawTransportPriceDifference = FindCurrentRawTransportPriceDifference(c);
        var page = NewPage();
        var layout = OfficialPageLayouts.PaymentReport;
        var x = layout.Left;
        var w = layout.Width;

        // The source workbook prints the title above the form; its outer rule
        // starts below the title and the side borders continue past the main table.
        AddOuterFrame(page, x, layout.FrameTop, w, 1002);
        var paymentNo = c.ProgressPaymentNumber?.ToString(Turkish) ?? "-";
        var date = FormatDate(c.PeriodDate);

        AddText(page, "HAK EDİŞ RAPORU", x, layout.TitleTop, w, 23, 16, true, TextAlignment.Center);
        AddCell(page, BuildWorkTitle(c, includeRouteNumber: true, includeStudentSuffix: false), x, 71, 411, 87, 9.5, true, TextAlignment.Left, White, new Thickness(5, 5, 5, 3));
        AddCell(page, $"Hakediş No : {paymentNo}", x + 411, 71, w - 411, 87, 13.3, true, TextAlignment.Center);

        AddCell(page, $"{date} TARİHİNE KADAR YAPILAN İŞİN", x, 158, w, 25, 13.3, true, TextAlignment.Center, White);
        var amountRows = new (string Key, string Label, decimal Value)[]
        {
            ("A", "Sözleşme Fiyatları İle Yapılan İş", c.ReportTotals.CumulativeServiceAmount),
            ("B", "Fiyat Farkı Tutarı", c.ReportTotals.CumulativePriceDifference),
            ("C", "Toplam Tutar  ( A + B )", c.ReportTotals.CumulativeTotal)
        };
        var y = 183d;
        foreach (var row in amountRows)
        {
            var bold = row.Key == "C";
            AddCell(page, row.Key, x, y, 53, 28, 13.3, bold, TextAlignment.Center);
            AddCell(page, row.Label, x + 53, y, 356, 28, 13.3, bold, TextAlignment.Left);
            AddCell(page, FormatMoney(row.Value), x + 409, y, w - 409, 28, 13.3, true, TextAlignment.Right, White, new Thickness(3, 2, 8, 2));
            y += 28;
        }

        y += 34;
        var summaryRows = new (string Key, string Label, decimal Value, Brush Fill, double Height)[]
        {
            ("D", "Bir Önceki Hakedişlerin Toplam Tutarı", c.ReportTotals.PreviousTotal, White, 28),
            ("E", "Bu Hakedişin Tutarı ( C - D )", c.ReportTotals.CurrentTotal, White, 28),
            ("F", BuildVatRateLabel(c.Calculation, c.ProgramType), c.Calculation.Vat, White, 28),
            ("G", "Tahakkuk Tutarı", c.ReportTotals.CurrentTotal + c.Calculation.Vat, ReportTotalGray, 32)
        };
        foreach (var row in summaryRows)
        {
            var bold = row.Key is "G" or "E";
            AddCell(page, row.Key, x, y, 53, row.Height, 13.3, bold, TextAlignment.Center, row.Fill);
            AddCell(page, row.Label, x + 53, y, 356, row.Height, 13.3, bold, TextAlignment.Left, row.Fill);
            var formattedValue = row.Key == "D" && c.ProgramType == TransportationProgramType.SpecialEducation && row.Value != 0m
                ? FormatMoney(row.Value, decimalPlaces: 3)
                : FormatMoney(row.Value);
            AddCell(page, formattedValue, x + 409, y, w - 409, row.Height, 13.3, true, TextAlignment.Right, row.Fill, new Thickness(3, 2, 8, 2));
            y += row.Height;
        }

        var deductions = new (string Key, string Label, decimal Value, double Height)[]
        {
            ("a)", "Gelir / Kurumlar Vergisi ( E x % .. )", 0m, 28),
            ("b)", BuildStampTaxLabel(c.Calculation), c.ReportStampTax ?? ProgressPaymentReportService.CalculateReportStampTax(c.Calculation, c.ProgramType, rawTransportPriceDifference), 28),
            ("c)", BuildWithholdingLabel(c.Calculation, c.ProgramType), c.Calculation.Withholding, 28),
            ("d)", "Sosyal Sigortalar Kurumu Kesintisi", c.Calculation.Deductions.SocialSecurityDebt, 28),
            ("e)", "İdare Makinesi Kiraları", 0m, 28),
            ("f)", "Gecikme Cezası", 0m, 28),
            ("g)", "Avans Mahsubu", 0m, 28),
            ("h)", "Bu Hakedişle Ödenen Fiyat Farkı Teminat Kesintisi", c.Calculation.AdditionalGuarantee, 32),
            ("ı)", "Vergi Borcu", c.Calculation.Deductions.TaxDebt, 28),
            ("j)", "İcra Borcu", 0m, 28),
            ("k)", "Ceza ve Diğer Kesintiler", c.Calculation.Deductions.InspectionPenalty, 28),
            ("l)", "Asgari Ücret Destek Tutarı Kesintisi", c.Calculation.Deductions.MinimumWageSupport, 28)
        };
        var deductionsHeight = deductions.Sum(d => d.Height);
        // Keep the section title vertically oriented and centered in the merged left cell.
        AddCell(page, "", x, y, 165, deductionsHeight, 9, false, TextAlignment.Center, White, new Thickness(0));
        AddRotatedText(page, "KESİNTİLER VE MAHSUPLAR", x, y, 165, deductionsHeight, 18.7, true, -90);

        foreach (var row in deductions)
        {
            AddCell(page, row.Key + " " + row.Label, x + 165, y, 247, row.Height, 13.3, false, TextAlignment.Left, White, new Thickness(3, 1, 2, 1));
            AddCell(page, FormatMoney(row.Value), x + 412, y, w - 412, row.Height, 13.3, true, TextAlignment.Right, White, new Thickness(3, 1, 8, 1));
            y += row.Height;
        }

        AddCell(page, "H", x, y, 53, 28, 13.3, true, TextAlignment.Center, ReportTotalGray);
        AddCell(page, "Kesintiler ve Mahsuplar Toplamı", x + 53, y, 356, 28, 13.3, true, TextAlignment.Left, ReportTotalGray);
        AddCell(page, FormatMoney(c.ReportDeductions ?? ProgressPaymentReportService.CalculateReportTotalDeductions(c.Calculation, c.ProgramType, rawTransportPriceDifference)), x + 409, y, w - 409, 28, 13.3, true, TextAlignment.Right, ReportTotalGray, new Thickness(3, 2, 8, 2));
        y += 28;
        AddCell(page, "I", x, y, 53, 28, 13.3, true, TextAlignment.Center, ReportTotalGray);
        AddCell(page, "Yükleniciye Ödenecek Tutar  ( G - H )", x + 53, y, 356, 28, 13.3, true, TextAlignment.Left, ReportTotalGray);
        AddCell(page, FormatMoney(c.ReportNetPayable), x + 409, y, w - 409, 28, 13.3, true, TextAlignment.Right, ReportTotalGray, new Thickness(3, 2, 8, 2));

        // The original form includes the contractor and all signatures inside its frame.
        AddText(page, "Yüklenici", x + 12, 830, 140, 23, 13.3, true, TextAlignment.Center);
        AddText(page, c.ContractorName, x + 4, 859, 160, 42, 12, false, TextAlignment.Center);
        AddSignatures(page, 929, c, "",
            leftX: 115, leftWidth: 205, rightX: 435, rightWidth: 200,
            nameOffset: 30, roleOffset: 45, dateOffset: 75, directorOffset: 105, titleOffset: 120);
        return page;
    }

}
