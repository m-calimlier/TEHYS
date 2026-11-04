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
    private void UpdateContractSelectors()
    {
        RefreshContractWorkNameDisplays();
        var selectedProgram = GetSelectedTransportationProgramType();
        var scopedContracts = _contracts.Where(x => x.ProgramType == selectedProgram).ToArray();
        var selectedTender = TenderNumberTextBox.Text.Trim();
        var selectedContractNumber = ContractNumberTextBox.Text.Trim();
        var previousHome = HomeTenderListBox.SelectedItem as ContractDraftData;
        var previousRoute = RouteTenderListBox.SelectedItem as ContractDraftData;
        var previousProgress = ProgressTenderListBox.SelectedItem as ContractDraftData;
        var previousOutput = OutputTenderListBox.SelectedItem as ContractDraftData;

        var contract = FindContract(selectedTender, selectedContractNumber, selectedProgram)
                       ?? (previousRoute is null ? null : FindContract(previousRoute.TenderNumber, previousRoute.ContractNumber, selectedProgram))
                       ?? scopedContracts.FirstOrDefault();
        var homeContract = previousHome is null
            ? contract
            : FindContract(previousHome.TenderNumber, previousHome.ContractNumber, previousHome.ProgramType) ?? contract;
        var progressContract = previousProgress is null
            ? null
            : FindContract(previousProgress.TenderNumber, previousProgress.ContractNumber, previousProgress.ProgramType);
        var outputContract = previousOutput is null
            ? contract
            : FindContract(previousOutput.TenderNumber, previousOutput.ContractNumber, previousOutput.ProgramType) ?? contract;

        _suppressSelectionEvents = true;
        try
        {
            HomeTenderListBox.ItemsSource = _contracts.ToArray();
            RouteTenderListBox.ItemsSource = _contracts.ToArray();
            ProgressTenderListBox.ItemsSource = _contracts.ToArray();
            OutputTenderListBox.ItemsSource = _contracts.ToArray();
            RefreshTenderList();
            RefreshContractorList();

            HomeTenderListBox.SelectedItem = homeContract;
            RouteTenderListBox.SelectedItem = contract;
            ProgressTenderListBox.SelectedItem = progressContract;
            OutputTenderListBox.SelectedItem = outputContract;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateContractorSummary();
        if (contract is not null)
        {
            UpdateRouteContractorSelector(contract);
            UpdateOutputRoutesForContract(outputContract);
            UpdateHomeTenderSummary(homeContract);
        }
        else
        {
            RouteContractorListBox.ItemsSource = Array.Empty<ContractorDraft>();
            RoutesListBox.ItemsSource = Array.Empty<RouteDraft>();
        }

        UpdateProgressContractorAndRoutes(progressContract, preserveSelection: false);
    }

    private void UpdateRouteContractorSelector(ContractDraftData? contract)
    {
        _suppressSelectionEvents = true;
        try
        {
            RouteContractorListBox.ItemsSource = contract is null
                ? Array.Empty<ContractorDraft>()
                : _contractors.Where(x => x.ProgramType == contract.ProgramType).ToArray();
            RouteContractorListBox.SelectedIndex = -1;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateRouteProgramTypeFields(contract);
        UpdateRouteListView();
        RouteSelectedTenderTextBlock.Text = contract is null ? "İhale: seçilmedi" : $"İhale: {contract.TenderNumber}";
        RouteSelectedContractorTextBlock.Text = "Yüklenici: firma seçilmemiş";
    }

    private void UpdateRouteListView()
    {
        RefreshContractWorkNameDisplays();
        RefreshRoutePanelList();
    }

    private void UpdateProgressPeriodOptions(ContractDraftData? contract, bool preserveSelection)
    {
        var previousPeriod = preserveSelection
            ? (ProgressPeriodMonthComboBox.SelectedItem as ProgressPaymentMonthOption)?.PeriodDate
            : null;

        var options = contract is null
            ? Array.Empty<ProgressPaymentMonthOption>()
            : ProgressPaymentMonthService.BuildOptions(contract.StartDate, contract.EndDate).ToArray();

        ProgressPeriodMonthComboBox.ItemsSource = options;
        ProgressPeriodMonthComboBox.SelectedItem = previousPeriod.HasValue
            ? ProgressPaymentMonthService.FindForPeriod(options, previousPeriod.Value)
            : null;
    }

    private void ProgressPeriodMonthComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _suppressSelectionEvents || _editingHistoryId.HasValue || IsArchiveEditing)
            return;

        var selected = ProgressPeriodMonthComboBox.SelectedItem as ProgressPaymentMonthOption;
        var monthKey = selected?.StartDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var transition = ResolveMonthlyDraftPeriodTransition(_lastSelectedPaymentMonthKey, monthKey);
        if (transition.ResetMonthlyDraft)
            ResetMonthlyInputsForNewMonth();
        else if (_lastSelectedPaymentMonthKey is null && monthKey is not null)
            RestoreHybridInputsForSelectedPeriod();

        // Programmatic clearing of a ComboBox can produce a transient null between
        // period selections. Forgetting the old month here can leak a cached route's
        // unsaved October values into a newly selected November claim.
        _lastSelectedPaymentMonthKey = transition.LastMonthKey;
        UpdateAutomaticProgressNumber();
    }

    internal static (string? LastMonthKey, bool ResetMonthlyDraft) ResolveMonthlyDraftPeriodTransition(
        string? previousMonthKey, string? selectedMonthKey)
    {
        if (selectedMonthKey is null)
            return (previousMonthKey, false);

        var changed = previousMonthKey is not null &&
            !string.Equals(previousMonthKey, selectedMonthKey, StringComparison.Ordinal);
        return (selectedMonthKey, changed);
    }

    private void ResetMonthlyInputsForNewMonth()
    {
        WorkDaysTextBox.Text = "0";
        GuideWorkDaysTextBox.Text = "0";
        DiscountTextBox.Text = "0";
        PriceDifferenceTextBox.Text = "0";
        GuideDiscountTextBox.Text = "0";
        GuidePriceDifferenceTextBox.Text = "0";
        TaxDebtTextBox.Text = "0";
        SocialSecurityDebtTextBox.Text = "0";
        MinimumWageSupportTextBox.Text = "0";
        SetMinimumWageSupportInputMode((ProgressTenderListBox.SelectedItem as ContractDraftData)?.ProgramType);
        InspectionPenaltyTextBox.Text = "0";

        // A month change starts a fresh draft for every route, including routes
        // temporarily deselected from the visible grid. Otherwise their cached
        // prior-month adjustments could reappear when selected again.
        _multiRouteDraftCache.ResetAll(row =>
        {
            row.ClearPeriodChanges();
            row.WorkDays = 0;
            row.DiscountAmount = 0m;
            row.PriceDifference = 0m;
            row.GuideWorkDays = 0;
            row.GuideDiscountAmount = 0m;
            row.GuidePriceDifference = 0m;
            row.TaxDebt = 0m;
            row.SocialSecurityDebt = 0m;
            var draftRoute = _routes.FirstOrDefault(route =>
                string.Equals(
                    OehsIdentityService.BuildRouteKey(
                        route.TenderNumber,
                        route.RouteNumber,
                        route.ContractorTaxOrIdentityNumber,
                        route.ContractNumber,
                        route.ProgramType),
                    row.RouteKey,
                    StringComparison.Ordinal));
            row.MinimumWageSupportEnabled =
                draftRoute is not null &&
                draftRoute.ProgramType != TransportationProgramType.BasicOrSecondary;
            row.MinimumWageSupport = 0m;
            row.InspectionPenalty = 0m;
            if (draftRoute is not null)
            {
                row.RouteName = draftRoute.Name;
                row.WorkDays = 0m;
                row.DailyPrice = draftRoute.DailyPrice;
                row.GuideDailyPrice = HasGuideStaff(draftRoute.ProgramType) ? draftRoute.GuideDailyPrice : 0m;
            }
            if (draftRoute is not null) SuggestPeriodDailyPrices(row, draftRoute);
            RestoreSavedHybridInput(row);
            row.AcceptChanges();
        });
        RefreshHybridUpdateButton();

        _lastCalculatedHistoryId = null;
        ArchiveCurrentProgressButton.Visibility = Visibility.Collapsed;
        CorrectCurrentProgressButton.Visibility = Visibility.Collapsed;
        ClearProgressValidationVisuals();
        ProgressValidationBanner.Visibility = Visibility.Collapsed;
        ProgressValidationMessageTextBlock.Text = string.Empty;
        ServiceAmountResultTextBlock.Text = FormatMoney(0m);
        GuideServiceAmountResultTextBlock.Text = FormatMoney(0m);
        TransportPriceDifferenceResultTextBlock.Text = FormatMoney(0m);
        GuidePriceDifferenceResultTextBlock.Text = FormatMoney(0m);
        CombinedPriceDifferenceResultTextBlock.Text = FormatMoney(0m);
        TotalAmountResultTextBlock.Text = FormatMoney(0m);
        VatResultTextBlock.Text = FormatMoney(0m);
        DeductionsResultTextBlock.Text = FormatMoney(0m);
        GrossResultTextBlock.Text = FormatMoney(0m);
        NetResultTextBlock.Text = FormatMoney(0m);
        NetControlResultTextBlock.Text = FormatMoney(0m);
        WithholdingResultTextBlock.Text = FormatMoney(0m);
        StampTaxResultTextBlock.Text = FormatMoney(0m);
        TaxDebtResultTextBlock.Text = FormatMoney(0m);
        SocialSecurityDebtResultTextBlock.Text = FormatMoney(0m);
        MinimumWageSupportResultTextBlock.Text = FormatMoney(0m);
        InspectionPenaltyResultTextBlock.Text = FormatMoney(0m);
        AdditionalGuaranteeResultTextBlock.Text = FormatMoney(0m);
        ProgressWorkDaysSummaryTextBlock.Text = "0";
        ProgressWorkDaysCardTextBlock.Text = "0 gün";
        ProgressNetSummaryTextBlock.Text = FormatMoney(0m);
        ProgressCalculationSummaryTextBlock.Text = "HAZIR";
        CalculationStatusTextBlock.Text = "Yeni ay seçildi. Bu aya ait iş günlerini, fiyat farklarını, iskontoları ve kesintileri yeniden girin.";
    }

    private void UpdateAutomaticProgressNumber()
    {
        var contract = ProgressTenderListBox.SelectedItem as ContractDraftData;
        var selected = ProgressPeriodMonthComboBox.SelectedItem as ProgressPaymentMonthOption;
        var selectedRoutes = ProgressRouteComboBox.SelectedItems.OfType<RouteDraft>().ToArray();
        if (contract is null || selected is null || selectedRoutes.Length == 0)
        {
            ProgressNumberTextBox.Clear();
            return;
        }

        var routeNumbers = selectedRoutes
            .Select(route => new
            {
                Route = route,
                Number = MonthlyProgressPaymentIdentityService.SuggestNextRouteNumber(
                    _progressHistory,
                    contract.TenderNumber,
                    contract.ContractNumber,
                    route.ContractorTaxOrIdentityNumber,
                    route.RouteNumber,
                    contract.ProgramType,
                    selected.PeriodDate)
            })
            .OrderBy(x => x.Route.RouteNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // A combined batch can contain routes at different points in their payment
        // history, so each route has its own Excel-compatible official number.
        ProgressNumberTextBox.Text = routeNumbers.Length == 1
            ? routeNumbers[0].Number.ToString(CultureInfo.CurrentCulture)
            : string.Join(" • ", routeNumbers.Select(x => $"{x.Route.RouteNumber}: {x.Number}"));
    }

    private void SelectProgressPeriod(DateOnly? periodDate)
    {
        var options = ProgressPeriodMonthComboBox.ItemsSource as IEnumerable<ProgressPaymentMonthOption>
                      ?? Array.Empty<ProgressPaymentMonthOption>();
        ProgressPeriodMonthComboBox.SelectedItem = periodDate.HasValue
            ? ProgressPaymentMonthService.FindForPeriod(options, periodDate.Value)
            : null;
    }

    private static bool ProgressPeriodDiffers(ComboBox comboBox, DateOnly? expected)
    {
        if (!expected.HasValue)
            return comboBox.SelectedItem is not null;

        if (comboBox.SelectedItem is not ProgressPaymentMonthOption actual)
            return true;

        // Historical records can carry any day in a month. The UI is month-based,
        // so compare the calendar month rather than the clipped period end date.
        return actual.StartDate.Year != expected.Value.Year
               || actual.StartDate.Month != expected.Value.Month;
    }

    private void UpdateProgressContractorAndRoutes(ContractDraftData? contract, bool preserveSelection = true)
    {
        UpdateProgressProgramTypeFields(contract);
        UpdateProgressPeriodOptions(contract, preserveSelection);
        if (contract is null)
        {
            _suppressSelectionEvents = true;
            try
            {
                ProgressContractorComboBox.ItemsSource = Array.Empty<ContractorDraft>();
                ProgressContractorComboBox.SelectedItem = null;
                ProgressContractorTextBlock.Text = "Firma seçiniz";
                ProgressRouteComboBox.ItemsSource = Array.Empty<RouteDraft>();
                ProgressRouteComboBox.SelectedItems.Clear();
                ProgressRouteRelationTextBlock.Text = "İhale, sözleşme, firma ve hat seçiniz.";
            }
            finally
            {
                _suppressSelectionEvents = false;
            }
            UpdateMultiRouteDraftRows();
            UpdateProgressRouteDefaults();
            return;
        }

        var contractRoutes = _routes.Where(x => IsRouteForContract(x, contract)).ToArray();
        // Do not silently treat routes without a tax/identity number as eligible for
        // contractor selection: matching by a blank identifier could mix businesses.
        // Keep them out of the payment selection until their identity data is fixed,
        // and explicitly disclose the excluded count in the UI below.
        var routesMissingContractorIdentity = contractRoutes.Count(x =>
            string.IsNullOrWhiteSpace(x.ContractorTaxOrIdentityNumber));
        var previousContractor = preserveSelection
            ? ProgressContractorComboBox.SelectedItem as ContractorDraft
            : null;
        var firms = contractRoutes
            .Where(x => !string.IsNullOrWhiteSpace(x.ContractorTaxOrIdentityNumber))
            .GroupBy(x => OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber), StringComparer.Ordinal)
            .Select(group =>
            {
                var route = group.First();
                return _contractors.FirstOrDefault(x =>
                           x.ProgramType == contract.ProgramType &&
                           string.Equals(
                               OehsIdentityService.NormalizeIdentity(x.TaxOrIdentityNumber),
                               group.Key,
                               StringComparison.Ordinal))
                       ?? new ContractorDraft(
                           string.IsNullOrWhiteSpace(route.ContractorName) ? "Firma adı tanımlanmamış" : route.ContractorName,
                           route.ContractorTaxOrIdentityNumber,
                           ProgramType: route.ProgramType);
            })
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        _suppressSelectionEvents = true;
        try
        {
            ProgressContractorComboBox.ItemsSource = firms;
            var matchedPrevious = previousContractor is null
                ? null
                : firms.FirstOrDefault(x => string.Equals(
                    OehsIdentityService.NormalizeIdentity(x.TaxOrIdentityNumber),
                    OehsIdentityService.NormalizeIdentity(previousContractor.TaxOrIdentityNumber),
                    StringComparison.Ordinal));
            ProgressContractorComboBox.SelectedItem = matchedPrevious
                ?? (firms.Length == 1 ? firms[0] : null);

            var selectedFirm = ProgressContractorComboBox.SelectedItem as ContractorDraft;
            var visibleRoutes = selectedFirm is null
                ? Array.Empty<RouteDraft>()
                : contractRoutes.Where(x => string.Equals(
                    OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
                    OehsIdentityService.NormalizeIdentity(selectedFirm.TaxOrIdentityNumber),
                    StringComparison.Ordinal)).ToArray();

            var previousSelectedKeys = preserveSelection
                ? ProgressRouteComboBox.SelectedItems.OfType<RouteDraft>()
                    .Select(x => OehsIdentityService.BuildRouteKey(
                        x.TenderNumber, x.RouteNumber, x.ContractorTaxOrIdentityNumber, x.ContractNumber, x.ProgramType))
                    .ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            ProgressRouteComboBox.ItemsSource = visibleRoutes;
            ProgressRouteComboBox.SelectedItems.Clear();
            foreach (var route in visibleRoutes)
            {
                var key = OehsIdentityService.BuildRouteKey(
                    route.TenderNumber, route.RouteNumber, route.ContractorTaxOrIdentityNumber, route.ContractNumber, route.ProgramType);
                if (previousSelectedKeys.Contains(key))
                    ProgressRouteComboBox.SelectedItems.Add(route);
            }
            ProgressContractorTextBlock.Text = selectedFirm is null
                ? (firms.Length == 0 ? "Bu sözleşmede firmaya bağlı hat bulunamadı." : "Devam etmek için firmayı seçin.")
                : $"{selectedFirm.Name} • {visibleRoutes.Length} hat";
            var routeSelectionMessage = selectedFirm is null
                ? (firms.Length == 0 ? "Vergi/T.C. kimlik numarası doğrulanmış firma bulunamadı." : "Önce yüklenici firmayı seçiniz.")
                : visibleRoutes.Length == 0 ? "Bu firmaya bağlı hat bulunamadı." : "Yalnızca seçilen firmaya ait hatlar listeleniyor.";
            ProgressRouteRelationTextBlock.Text = routesMissingContractorIdentity > 0
                ? $"{routeSelectionMessage} UYARI: {routesMissingContractorIdentity} hat firma vergi/T.C. kimlik numarası eksik olduğu için seçime alınmadı; ödeme öncesi hat kaydını düzeltin."
                : routeSelectionMessage;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateMultiRouteDraftRows();
        UpdateProgressRouteDefaults();
    }

    private void ProgressContractorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _suppressSelectionEvents || IsArchiveEditing)
            return;

        var contract = ProgressTenderListBox.SelectedItem as ContractDraftData;
        var contractor = ProgressContractorComboBox.SelectedItem as ContractorDraft;
        var previousKeys = ProgressRouteComboBox.SelectedItems.OfType<RouteDraft>()
            .Select(x => OehsIdentityService.BuildRouteKey(
                x.TenderNumber, x.RouteNumber, x.ContractorTaxOrIdentityNumber, x.ContractNumber, x.ProgramType))
            .ToHashSet(StringComparer.Ordinal);

        var routes = contract is null || contractor is null
            ? Array.Empty<RouteDraft>()
            : _routes.Where(x =>
                IsRouteForContract(x, contract) &&
                string.Equals(
                    OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
                    OehsIdentityService.NormalizeIdentity(contractor.TaxOrIdentityNumber),
                    StringComparison.Ordinal)).ToArray();

        _suppressSelectionEvents = true;
        try
        {
            ProgressRouteComboBox.ItemsSource = routes;
            ProgressRouteComboBox.SelectedItems.Clear();
            foreach (var route in routes)
            {
                var key = OehsIdentityService.BuildRouteKey(
                    route.TenderNumber, route.RouteNumber, route.ContractorTaxOrIdentityNumber, route.ContractNumber, route.ProgramType);
                if (previousKeys.Contains(key))
                    ProgressRouteComboBox.SelectedItems.Add(route);
            }
            ProgressContractorTextBlock.Text = contractor is null
                ? "Devam etmek için firmayı seçin."
                : $"{contractor.Name} • {routes.Length} hat";
            ProgressRouteRelationTextBlock.Text = contractor is null
                ? "Önce yüklenici firmayı seçiniz."
                : routes.Length == 0 ? "Bu firmaya bağlı hat bulunamadı." : "Yalnızca seçilen firmaya ait hatlar listeleniyor.";
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateMultiRouteDraftRows();
        UpdateProgressRouteDefaults();
    }

    private void UpdateHomeTenderSummary(ContractDraftData? contract)
    {
        if (contract is null)
        {
            HomeSelectedTenderSummaryTextBlock.Text = "İhale/sözleşme seçiniz.";
            return;
        }
        HomeSelectedTenderSummaryTextBlock.Text =
            $"İKN {contract.TenderNumber} • {contract.ProgramDisplayText} • " +
            $"{contract.StartDate:dd.MM.yyyy}–{contract.EndDate:dd.MM.yyyy} • " +
            $"{_routes.Count(x => IsRouteForContract(x, contract))} hat";
    }

}
