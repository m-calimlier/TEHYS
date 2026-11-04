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
    private void UpdateAcceptanceView()
    {
        if (_dataStore is null)
            return;

        if (AcceptanceContractTextBlock is null)
            return;

        if (_progressHistory.Count == 0)
        {
            AcceptanceContractTextBlock.Text = "Kayıt yok";
            AcceptanceProgressTextBlock.Text = "Kayıt yok";
            AcceptanceNetTextBlock.Text = FormatMoney(0);
            AcceptanceDetailTextBlock.Text = "Sözleşme ve hakediş kaydı oluşturulduğunda bilgiler burada görünür.";
            return;
        }

        var selectedRoute = ReportRouteComboBox.SelectedItem as RouteDraft;
        var latest = _progressHistory
            .Where(x => selectedRoute is null ||
                        (string.Equals(x.TenderNumber, selectedRoute.TenderNumber, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(x.ContractNumber, selectedRoute.ContractNumber, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(x.RouteNumber, selectedRoute.RouteNumber, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(
                        OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
                        OehsIdentityService.NormalizeIdentity(selectedRoute.ContractorTaxOrIdentityNumber),
                        StringComparison.Ordinal)))
            .OrderByDescending(x => x.PeriodDate ?? DateOnly.MinValue)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefault();

        if (latest is null)
        {
            AcceptanceContractTextBlock.Text = "Kayıt yok";
            AcceptanceProgressTextBlock.Text = "Kayıt yok";
            AcceptanceNetTextBlock.Text = FormatMoney(0);
            AcceptanceDetailTextBlock.Text = selectedRoute is null
                ? "Hakediş kaydı oluşturulduğunda bilgiler burada görünür."
                : "Seçilen hat için henüz hakediş kaydı yok.";
            return;
        }

        AcceptanceContractTextBlock.Text = string.IsNullOrWhiteSpace(latest.ContractorName)
            ? (string.IsNullOrWhiteSpace(CurrentContractorName) ? "Sözleşme bilgisi" : CurrentContractorName.Trim())
            : latest.ContractorName.Trim();

        AcceptanceProgressTextBlock.Text = latest.ProgressPaymentNumber.HasValue
            ? $"Hakediş No {latest.ProgressPaymentNumber.Value}"
            : "Son hakediş";

        AcceptanceNetTextBlock.Text = FormatMoney(latest.Calculation.NetPayable);

        var period = latest.PeriodDate.HasValue
            ? latest.PeriodDate.Value.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture)
            : "-";

        AcceptanceDetailTextBlock.Text =
            $"Dönem: {period} • Brüt: {FormatMoney(latest.Calculation.GrossAmount)} • " +
            $"Net: {FormatMoney(latest.Calculation.NetPayable)} • " +
            $"Toplam kesinti: {FormatMoney(latest.Calculation.TotalDeductions)}";
    }

}
