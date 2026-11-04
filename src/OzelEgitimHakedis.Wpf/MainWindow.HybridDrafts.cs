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
    private void ProgressRouteComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _suppressSelectionEvents)
            return;

        // Keep every selected route's own monthly draft when the selection changes.
        UpdateMultiRouteDraftRows();
        UpdateProgressRouteDefaults();
    }

    private void UpdateMultiRouteDraftRows()
    {
        if (!IsInitialized) return;
        UpdateHybridRouteDraftRows();
    }

    private void UpdateHybridRouteDraftRows()
    {
        if (!TryCommitHybridGrid()) return;
        var selectedRoutes = ProgressRouteComboBox.SelectedItems.OfType<RouteDraft>().ToArray();
        var showMultiRouteGrid = selectedRoutes.Length > 0 &&
                                 !_editingHistoryId.HasValue &&
                                 !IsArchiveEditing;
        if (!showMultiRouteGrid)
        {
            foreach (var row in _multiRoutePaymentRows)
                _multiRouteDraftCache[row.RouteKey] = row;
            _multiRoutePaymentRows.Clear();
            MultiRouteEntryBorder.Visibility = Visibility.Collapsed;
            RefreshHybridUpdateButton();
            SetGlobalMonetaryFieldsVisible(true);
            return;
        }

        // Cache all drafts for this unsaved payment form, not just currently
        // selected routes. The grid/output path below still uses selected routes only.
        foreach (var visibleRow in _multiRoutePaymentRows)
            _multiRouteDraftCache[visibleRow.RouteKey] = visibleRow;
        var existingRows = _multiRouteDraftCache;
        var nextRows = new List<MultiRoutePaymentDraftRow>();
        foreach (var route in selectedRoutes.OrderBy(x => x.RouteNumber, StringComparer.OrdinalIgnoreCase))
        {
            var routeKey = OehsIdentityService.BuildRouteKey(
                route.TenderNumber,
                route.RouteNumber,
                route.ContractorTaxOrIdentityNumber,
                route.ContractNumber,
                route.ProgramType);
            if (existingRows.TryGetValue(routeKey, out var existing))
            {
                ConfigureHybridRow(existing, route);
                nextRows.Add(existing);
                continue;
            }

            var hasGuide = HasGuideStaff(route.ProgramType);
            var row = new MultiRoutePaymentDraftRow
            {
                RouteKey = routeKey,
                RouteNumber = route.RouteNumber,
                RouteName = route.Name,
                MinimumWageSupportEnabled =
                    route.ProgramType != TransportationProgramType.BasicOrSecondary,
                // Work days are entered only in this period-specific hybrid row.
                WorkDays = 0m,
                DailyPrice = route.DailyPrice,
                GuideWorkDays = 0,
                GuideDailyPrice = hasGuide ? route.GuideDailyPrice : 0m
            };

            SuggestPeriodDailyPrices(row, route);
            RestoreSavedHybridInput(row);
            ConfigureHybridRow(row, route);
            row.AcceptChanges();
            _multiRouteDraftCache[routeKey] = row;
            nextRows.Add(row);
        }

        _multiRoutePaymentRows.Clear();
        foreach (var row in nextRows)
            _multiRoutePaymentRows.Add(row);

        MultiRouteEntryBorder.Visibility = Visibility.Visible;
        SetGlobalMonetaryFieldsVisible(false);
        ApplyHybridColumnView();
        RefreshHybridTotals();
        RefreshHybridUpdateButton();
        SyncHybridSelectedRowFields();
    }

    private void SetGlobalMonetaryFieldsVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        var selectedProgram = (ProgressTenderListBox.SelectedItem as ContractDraftData)?.ProgramType;
        var showGuide = visible && selectedProgram.HasValue && HasGuideStaff(selectedProgram.Value);
        SinglePaymentFieldsGrid.Visibility = visible && (_editingHistoryId.HasValue || IsArchiveEditing)
            ? Visibility.Visible : Visibility.Collapsed;
        WorkDaysLabel.Visibility = visibility;
        WorkDaysTextBox.Visibility = visibility;
        DailyPriceLabel.Visibility = visibility;
        DailyPriceTextBox.Visibility = visibility;
        ProgressGuideWorkDaysLabel.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        GuideWorkDaysTextBox.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        ProgressGuideDailyPriceLabel.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        GuideDailyPriceTextBox.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        DiscountLabel.Visibility = visibility;
        DiscountTextBox.Visibility = visibility;
        PriceDifferenceLabel.Visibility = visibility;
        PriceDifferenceTextBox.Visibility = visibility;
        GuideDiscountLabel.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        GuideDiscountTextBox.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        GuidePriceDifferenceLabel.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        GuidePriceDifferenceTextBox.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        TaxDebtLabel.Visibility = visibility;
        TaxDebtTextBox.Visibility = visibility;
        SocialSecurityDebtLabel.Visibility = visibility;
        SocialSecurityDebtTextBox.Visibility = visibility;
        MinimumWageSupportLabel.Visibility = visibility;
        MinimumWageSupportTextBox.Visibility = visibility;
        MinimumWageSupportEnabledCheckBox.Visibility =
            visible && selectedProgram == TransportationProgramType.BasicOrSecondary
                ? Visibility.Visible
                : Visibility.Collapsed;

        var supportToggleColumn = MultiRoutePaymentGrid.Columns.FirstOrDefault(column =>
            string.Equals(column.Header?.ToString(), "Asgari Ücret Desteği Kullan", StringComparison.Ordinal));
        if (supportToggleColumn is not null)
        {
            supportToggleColumn.Visibility =
                !visible && selectedProgram == TransportationProgramType.BasicOrSecondary
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        InspectionPenaltyLabel.Visibility = visibility;
        InspectionPenaltyTextBox.Visibility = visibility;
    }

    private void UpdateProgressRouteDefaults()
    {
        var selectedProgram = (ProgressTenderListBox.SelectedItem as ContractDraftData)?.ProgramType
            ?? TransportationProgramType.SpecialEducation;
        ManualAdditionalGuaranteeTextBox.IsEnabled = true;
        ManualAdditionalGuaranteeTextBox.Text = FormatMoney(0m);
        ManualAdditionalGuaranteeLabel.Text = "Ek Kesin Teminat (Hesaplanan)";
        GuaranteeRuleHintTextBlock.Text = selectedProgram == TransportationProgramType.BasicOrSecondary
            ? "Temel Eğitim/Ortaöğretim Taşıma: kaynak Excel formülü = I × %6"
            : "Özel Eğitim Taşıma: kaynak Excel formülü = (I × %6) + (R × %6)";
        var selectedRoutes = ProgressRouteComboBox.SelectedItems.OfType<RouteDraft>().ToArray();
        if (selectedRoutes.Length > 1)
        {
            var contract = ProgressTenderListBox.SelectedItem as ContractDraftData;
            var contractorNames = selectedRoutes
                .Select(x => x.ContractorName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            ProgressContractorTextBlock.Text = contractorNames.Length == 1
                ? contractorNames[0]
                : contract?.ContractorName ?? "Yükleniciler";
            ProgressRouteRelationTextBlock.Text =
                $"İKN: {contract?.TenderNumber ?? selectedRoutes[0].TenderNumber} • {selectedRoutes.Length} hat seçildi: " +
                string.Join(", ", selectedRoutes.Select(x => x.RouteNumber).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            UpdateAutomaticProgressNumber();
            return;
        }

        if (selectedRoutes.Length == 0 || ProgressRouteComboBox.SelectedItem is not RouteDraft route)
        {
            ProgressContractorTextBlock.Text = "Seçilmedi";
            ProgressRouteRelationTextBlock.Text = "Hat seçiniz.";
            _lastProgressDefaultsRouteKey = null;
            ProgressNumberTextBox.Clear();
            return;
        }

        ProgressContractorTextBlock.Text = string.IsNullOrWhiteSpace(route.ContractorName)
            ? "Firma seçilmemiş"
            : route.ContractorName;
        ProgressRouteRelationTextBlock.Text = $"İhale: {route.TenderNumber} • Yüklenici: {route.ContractorName} • Hat: {route.RouteNumber} — {route.Name}";

        var routeKey = OehsIdentityService.BuildRouteKey(
            route.TenderNumber, route.RouteNumber, route.ContractorTaxOrIdentityNumber,
            route.ContractNumber, route.ProgramType);
        var routeChanged = !string.Equals(_lastProgressDefaultsRouteKey, routeKey, StringComparison.Ordinal);
        if (routeChanged && !_editingHistoryId.HasValue && !IsArchiveEditing)
        {
            // A route change must never carry the previous route's monthly
            // discounts/debts/price differences into a new route.
            DiscountTextBox.Text = "0";
            PriceDifferenceTextBox.Text = "0";
            GuideDiscountTextBox.Text = "0";
            GuidePriceDifferenceTextBox.Text = "0";
            TaxDebtTextBox.Text = "0";
            SocialSecurityDebtTextBox.Text = "0";
            MinimumWageSupportTextBox.Text = "0";
            InspectionPenaltyTextBox.Text = "0";
        }

        DailyPriceTextBox.Text = FormatCalculationInput(route.DailyPrice);
        GuideDailyPriceTextBox.Text = FormatCalculationInput(
            HasGuideStaff(route.ProgramType) ? route.GuideDailyPrice : 0m);

        SyncHybridSelectedRowFields();
        _lastProgressDefaultsRouteKey = routeKey;

        if (!_editingHistoryId.HasValue && !IsArchiveEditing)
            UpdateAutomaticProgressNumber();
    }

}
