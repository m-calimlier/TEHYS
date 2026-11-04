using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Documents;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

public partial class MainWindow
{
    private void UpdateSummaryView()
    {
        _summaryRows.Clear();

        var ordered = _progressHistory
            .OrderBy(x => x.PeriodDate ?? DateOnly.MaxValue)
            .ThenBy(x => x.ProgressPaymentNumber ?? int.MaxValue)
            .ThenBy(x => x.CreatedAt)
            .ToArray();

        foreach (var item in ordered)
        {
            var title = !string.IsNullOrWhiteSpace(item.BatchDisplayName)
                ? $"{item.BatchDisplayName} • Hat {item.RouteNumber ?? "-"} • Hakediş No {item.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? "-"}"
                : item.ProgressPaymentNumber.HasValue
                    ? $"Hakediş No {item.ProgressPaymentNumber.Value}"
                    : "Hakediş Hesaplama";

            var period = item.PeriodDate.HasValue
                ? $"Dönem: {CultureInfo.GetCultureInfo("tr-TR").TextInfo.ToTitleCase(item.PeriodDate.Value.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR")))}"
                : $"Kayıt: {item.CreatedAt:dd.MM.yyyy HH:mm}";

            _summaryRows.Add(new SummaryRow(
                title,
                period,
                FormatMoney(item.Calculation.GrossAmount),
                FormatMoney(item.Calculation.NetPayable)));
        }

        var latest = _progressHistory
            .Where(x => x.PeriodDate.HasValue)
            .OrderByDescending(x => x.PeriodDate)
            .FirstOrDefault();

        SummaryLatestPeriodTextBlock.Text = latest?.PeriodDate?.ToString("MM.yyyy", CultureInfo.CurrentCulture) ?? "—";
        SummaryCountTextBlock.Text = $"{_progressHistory.Count.ToString(CultureInfo.CurrentCulture)} hakediş";
        SummaryGrossTotalTextBlock.Text = FormatMoney(_progressHistory.Sum(x => x.Calculation.GrossAmount));
        SummaryNetTotalTextBlock.Text = FormatMoney(_progressHistory.Sum(x => x.Calculation.NetPayable));
        SummaryWithholdingTextBlock.Text = FormatMoney(_progressHistory.Sum(x => x.Calculation.Deductions.Withholding));
        SummaryStampTextBlock.Text = FormatMoney(_progressHistory.Sum(x => x.Calculation.Deductions.StampTax));
        SummaryOtherDeductionsTextBlock.Text = FormatMoney(_progressHistory.Sum(x =>
            x.Calculation.Deductions.TaxDebt
            + x.Calculation.Deductions.SocialSecurityDebt
            + x.Calculation.Deductions.MinimumWageSupport
            + x.Calculation.Deductions.InspectionPenalty
            + x.Calculation.Deductions.AdditionalGuarantee));

        SummaryEmptyStateBorder.Visibility = _summaryRows.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateReportView()
    {
        RefreshOutputRecords();
    }

}
