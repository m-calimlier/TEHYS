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
    private void HomeTenderListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionEvents) UpdateHomeSummaryView();
    }

    private void UpdateHomeSummaryView()
    {
        UpdateHomeTenderSummary(HomeTenderListBox.SelectedItem as ContractDraftData);
        RefreshHomeOperations();
    }

    private void UpdateContractSummaryView()
    {
        var lookupTender = _editingContractTender ?? TenderNumberTextBox.Text.Trim();
        var lookupContractNumber = _editingContractNumber ?? ContractNumberTextBox.Text.Trim();
        var contract = FindContract(lookupTender, lookupContractNumber, _editingContractProgramType ?? GetSelectedTransportationProgramType());
        var hasContract = contract is not null;

        ContractStatusSummaryTextBlock.Text = hasContract ? "KAYITLI" : "TASLAK";
        ContractAmountSummaryTextBlock.Text =
            TurkishMoneyInput.TryParse(ContractAmountTextBox.Text, out var amount) ? FormatMoney(amount) : FormatMoney(0);
        ContractStartSummaryTextBlock.Text =
            StartDatePicker.SelectedDate?.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture) ?? "—";
        ContractEndSummaryTextBlock.Text =
            EndDatePicker.SelectedDate?.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture) ?? "—";

        ContractSummaryTenderTextBlock.Text = string.IsNullOrWhiteSpace(TenderNumberTextBox.Text)
            ? "Bekleniyor" : TenderNumberTextBox.Text.Trim();

        ContractSummaryContractTextBlock.Text = GetSelectedTransportationProgramType() == TransportationProgramType.BasicOrSecondary
            ? "Temel Eğitim/Ortaöğretim Taşıma"
            : "Özel Eğitim Taşıma";

        ContractSummaryAmountTextBlock.Text = TurkishMoneyInput.TryParse(ContractAmountTextBox.Text, out var summaryAmount)
            ? FormatMoney(summaryAmount) : FormatMoney(0);

        var workDaysText = int.TryParse(ContractWorkDaysTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var workDays)
            ? workDays.ToString(CultureInfo.CurrentCulture) : "0";
        ContractSummaryWorkDaysTextBlock.Text = $"{workDaysText} gün";
        var routeCount = contract is null ? 0 : _routes.Count(x => IsRouteForContract(x, contract));
        ContractSummaryVatTextBlock.Text =
            $"{(ContractorVatExemptCheckBox.IsChecked == true ? "KDV İstisnası" : "KDV Uygulanır")} • {routeCount} hat";
    }

    private void HomeTenderListBox_RefreshSelection()
    {
        if (HomeTenderListBox.SelectedItem is null && _contracts.Count > 0)
            HomeTenderListBox.SelectedIndex = 0;
    }

    private void RouteTenderListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents)
            return;

        var contract = RouteTenderListBox.SelectedItem as ContractDraftData;
        _suppressSelectionEvents = true;
        try
        {
            UpdateRouteContractorSelector(contract);
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateRouteSummary();
    }

    private void RouteContractorListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents)
            return;

        UpdateRouteListView();
        var contract = RouteTenderListBox.SelectedItem as ContractDraftData;
        var contractor = RouteContractorListBox.SelectedItem as ContractorDraft;
        RouteSelectedTenderTextBlock.Text = contract is null ? "İhale: seçilmedi" : $"İhale: {contract.TenderNumber}";
        RouteSelectedContractorTextBlock.Text = contractor is null ? "Yüklenici: seçilmedi" : $"Yüklenici: {contractor.Name}";
    }

    private void ProgressTenderListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents || IsArchiveEditing)
            return;

        _lastCalculatedHistoryId = null;
        ClearProgressEntryFields(clearTenderSelection: false);
        UpdateProgressContractorAndRoutes(
            ProgressTenderListBox.SelectedItem as ContractDraftData,
            preserveSelection: false);
    }

}
