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
    private void OutputTenderListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents)
            return;

        UpdateOutputRoutesForContract(OutputTenderListBox.SelectedItem as ContractDraftData);
        UpdateReportView();
        UpdateAcceptanceView();
    }

    private void UpdateOutputRoutesForContract(ContractDraftData? contract)
    {
        var routes = contract is null
            ? Array.Empty<RouteDraft>()
            : _routes.Where(x => IsRouteForContract(x, contract))
                .GroupBy(x=>string.IsNullOrWhiteSpace(x.ContractorTaxOrIdentityNumber)?HybridRouteKey(x):OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber))
                .Select(g=>g.First()).ToArray();

        var previous = ReportRouteComboBox.SelectedItem as RouteDraft;
        ReportRouteComboBox.ItemsSource = routes;
        if (previous is not null)
            ReportRouteComboBox.SelectedItem = routes.FirstOrDefault(x =>
                string.Equals(x.ContractNumber, previous.ContractNumber, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
                    OehsIdentityService.NormalizeIdentity(previous.ContractorTaxOrIdentityNumber),
                    StringComparison.Ordinal));
        if (ReportRouteComboBox.SelectedItem is null && routes.Length > 0)
            ReportRouteComboBox.SelectedIndex = 0;
    }

    private void ReportRouteComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
            return;

        UpdateReportView();
        UpdateAcceptanceView();
    }

    private string CurrentContractorName =>
        (ReportRouteComboBox.SelectedItem as RouteDraft)?.ContractorName
        ?? (ProgressRouteComboBox.SelectedItem as RouteDraft)?.ContractorName
        ?? string.Empty;

    private ProgressPaymentHistoryData? GetSelectedProgressLatestPayment()
    {
        var contract = ProgressTenderListBox.SelectedItem as ContractDraftData;
        var route = ProgressRouteComboBox.SelectedItem as RouteDraft;
        if (contract is null || route is null)
            return null;

        return PaymentScreenModel.LatestForRoute(_progressHistory, contract,
            new RouteData(route.RouteNumber, route.Name, route.DailyPrice,
                TenderNumber: route.TenderNumber, ContractorTaxOrIdentityNumber: route.ContractorTaxOrIdentityNumber,
                ContractNumber: route.ContractNumber, ProgramType: route.ProgramType));
    }

    private ProgressPaymentHistoryData? GetSelectedRouteLatestPayment() => GetSelectedRoutePayments().LastOrDefault();

    private List<ProgressPaymentHistoryData> GetSelectedRoutePayments()
    {
        if (!TryGetOutputRoute(out var route)) return new List<ProgressPaymentHistoryData>();
        return _progressHistory.Where(payment => IsOutputPaymentForRoute(payment, route!))
            .OrderBy(payment => payment.PeriodDate ?? DateOnly.MinValue).ThenBy(payment => payment.CreatedAt).ToList();
    }



}
