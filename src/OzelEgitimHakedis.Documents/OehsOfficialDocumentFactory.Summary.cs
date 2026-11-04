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
    private static FixedPage BuildHakedisIcmali(OehsOfficialDocumentContext c, int pageNumber = 1)
    {
        var page = NewPage();
        var layout = OfficialPageLayouts.Summary;
        var x = layout.Left;
        var w = layout.Width;
        AddText(page, "HAKEDİŞ İCMALİ", x, layout.TitleTop, w, 28, 15, true, TextAlignment.Center);

        AddCell(page, BuildWorkTitle(c, includeRouteNumber: false, includeStudentSuffix: true), x, 47, 555, 87, 12.0, true, TextAlignment.Left, White, new Thickness(5, 3, 5, 2));
        AddCell(page, $"Sayfa No : {pageNumber}", x + 555, 47, w - 555, 43.5, 13.3, true, TextAlignment.Left);
        AddCell(page, $"Hakediş No : {c.ProgressPaymentNumber?.ToString(Turkish) ?? "-"}", x + 555, 90.5, w - 555, 43.5, 13.3, true, TextAlignment.Left);

        var widths = new[] { 69d, 185d, 196d, 107d, w - 69d - 185d - 196d - 107d };
        var headers = new[] { "Sıra No", "İşin Cinsi", "İş Tutarı\n(a)", "Fiyat Farkı\nTutarı\n(b)", "Toplam\n(a+b)" };
        var y = 134d;
        var xx = x;
        for (var i = 0; i < widths.Length; i++)
        {
            AddCell(page, headers[i], xx, y, widths[i], 50, 13.3, true, TextAlignment.Center, White);
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
                AddCell(page, values[col], xx, y, widths[col], 32, 13.3, col == 0, col == 0 ? TextAlignment.Center : col == 1 ? TextAlignment.Left : TextAlignment.Center,
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
            AddCell(page, totals[col], totalX[col], y, totalWidths[col], 32, 13.3, true, TextAlignment.Center, TotalGray);

        // The workbook's total amount cell spans the monthly subtotal row and
        // the separate A/TOPLAM row on the right edge.
        AddCell(
            page,
            FormatLira(sumService + sumDifference),
            x + widths[0] + widths[1] + widths[2] + widths[3],
            y,
            widths[4],
            65,
            13.3,
            true,
            TextAlignment.Center,
            TotalGray);
        y += 32;
        AddCell(page, "A", x, y, widths[0], 33, 13.3, true, TextAlignment.Center, TotalGray);
        AddCell(page, "T  O  P  L  A  M", x + widths[0], y, widths[1] + widths[2] + widths[3], 33, 13.3, true, TextAlignment.Center, TotalGray);
        y += 51;
        AddOuterFrame(page,x,layout.FrameTop,w,y-18-47);
        var deductionsTop=y;

        AddCell(page, "", x, y, widths[0], 29, 13.3, false, TextAlignment.Center, White);
        AddCell(page, "Hakediş İçeriğinden Yapılan Kesintiler", x + widths[0], y, w - widths[0], 29, 13.3, true, TextAlignment.Center, White);
        y += 29;
        var amountX = x + w - widths[4];
        var labelX = x + widths[0];
        var labelWidth = w - widths[0] - widths[4];
        AddCell(page, "a)", x, y, widths[0], 29, 13.3, false, TextAlignment.Center);
        AddCell(page, "Bir Evvelki Hakedişten Yapılan Kesinti", labelX, y, labelWidth, 29, 13.3, false, TextAlignment.Left);
        AddCell(page, FormatMoney(c.PreviousDeduction, false), amountX, y, widths[4], 29, 13.3, false, TextAlignment.Right, White, new Thickness(3, 2, 8, 2));
        y += 29;
        AddCell(page, "b)", x, y, widths[0], 29, 13.3, false, TextAlignment.Center);
        AddCell(page, "Bu Hakedişten Yapılan Kesinti", labelX, y, labelWidth, 29, 13.3, false, TextAlignment.Left);
        AddCell(page, FormatMoney(c.Calculation.TotalDeductions, false), amountX, y, widths[4], 29, 13.3, false, TextAlignment.Right, White, new Thickness(3, 2, 8, 2));
        y += 29;
        AddCell(page, "B", x, y, widths[0], 30, 13.3, true, TextAlignment.Center, TotalGray);
        AddCell(page, "Toplam Kesinti Tutarı", labelX, y, labelWidth, 30, 13.3, true, TextAlignment.Right, TotalGray, new Thickness(3, 2, 8, 2));
        AddCell(page, FormatMoney(c.PreviousDeduction + c.Calculation.TotalDeductions, false), amountX, y, widths[4], 30, 13.3, true, TextAlignment.Right, TotalGray, new Thickness(3, 2, 8, 2));
        y += 46;
        AddOuterFrame(page,x,deductionsTop,w,y-16-deductionsTop);
        AddCell(page, "C", x, y, widths[0], 30, 13.3, true, TextAlignment.Center, TotalGray);
        AddCell(page, "Toplam Hakediş Tutarı : ( A - B )", labelX, y, labelWidth, 30, 13.3, true, TextAlignment.Right, TotalGray, new Thickness(3, 2, 8, 2));
        AddCell(page, FormatMoney(sumService + sumDifference - c.PreviousDeduction - c.Calculation.TotalDeductions, false), amountX, y, widths[4], 30, 13.3, true, TextAlignment.Right, TotalGray, new Thickness(3, 2, 8, 2));

        AddOuterFrame(page,x,y,w,30);
        AddSignatures(page, 867, c, "M.Y.H.B.Y. Örnek No :4/9",
            leftX: 40, leftWidth: 220, rightX: 495, rightWidth: 220,
            nameOffset: 37, roleOffset: 55, dateOffset: 92, directorOffset: 130, titleOffset: 148, footerY: 1080);
        return page;
    }

}
