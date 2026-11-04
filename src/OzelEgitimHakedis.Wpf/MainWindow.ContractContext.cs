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
    private ContractorDraft? FindContractor(
        string taxOrIdentityNumber,
        TransportationProgramType? programType = null)
    {
        if (string.IsNullOrWhiteSpace(taxOrIdentityNumber))
            return null;

        var identity = OehsIdentityService.NormalizeIdentity(taxOrIdentityNumber);
        var matches = _contractors.Where(x =>
            string.Equals(
                OehsIdentityService.NormalizeIdentity(x.TaxOrIdentityNumber),
                identity,
                StringComparison.Ordinal));
        return programType.HasValue
            ? matches.FirstOrDefault(x => x.ProgramType == programType.Value)
            : matches.FirstOrDefault();
    }

    private static string BuildContractorKey(ContractorDraft contractor) =>
        $"{contractor.ProgramType}|{OehsIdentityService.NormalizeIdentity(contractor.TaxOrIdentityNumber)}";

    private ContractorDraft? FindContractorByKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var separator = key.IndexOf('|', StringComparison.Ordinal);
        if (separator < 0 || !Enum.TryParse<TransportationProgramType>(key[..separator], out var programType))
            return FindContractor(key);

        return FindContractor(key[(separator + 1)..], programType);
    }

    private ContractDraftData? GetSelectedContract()
    {
        return HomeTenderListBox.SelectedItem as ContractDraftData
            ?? RouteTenderListBox.SelectedItem as ContractDraftData
            ?? ProgressTenderListBox.SelectedItem as ContractDraftData
            ?? FindContract(TenderNumberTextBox.Text, ContractNumberTextBox.Text, GetSelectedTransportationProgramType())
            ?? _contracts.FirstOrDefault();
    }

    private ContractDraftData? FindContract(
        string? tenderNumber,
        string? contractNumber,
        TransportationProgramType? programType = null)
    {
        if (string.IsNullOrWhiteSpace(tenderNumber))
            return null;

        var tenderContracts = _contracts.Where(x =>
            string.Equals(x.TenderNumber.Trim(), tenderNumber.Trim(), StringComparison.OrdinalIgnoreCase) &&
            (!programType.HasValue || x.ProgramType == programType.Value)).ToArray();
        if (!string.IsNullOrWhiteSpace(contractNumber))
        {
            var matches = tenderContracts.Where(x =>
                string.Equals(x.ContractNumber.Trim(), contractNumber.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        // Legacy records can only be assigned automatically while tender identity is unique.
        return tenderContracts.Length == 1 ? tenderContracts[0] : null;
    }

    private bool IsHistoryForContract(ProgressPaymentHistoryData item, ContractDraftData contract)
    {
        if (!string.Equals(item.TenderNumber?.Trim(), contract.TenderNumber.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (item.Input.ProgramType != contract.ProgramType)
            return false;
        if (!string.IsNullOrWhiteSpace(item.ContractNumber))
            return string.Equals(item.ContractNumber.Trim(), contract.ContractNumber.Trim(), StringComparison.OrdinalIgnoreCase);

        return _contracts.Count(x =>
            x.ProgramType == contract.ProgramType &&
            string.Equals(x.TenderNumber.Trim(), contract.TenderNumber.Trim(), StringComparison.OrdinalIgnoreCase)) == 1;
    }

    private bool IsRouteForContract(RouteDraft route, ContractDraftData contract)
    {
        if (!string.Equals(route.TenderNumber.Trim(), contract.TenderNumber.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (route.ProgramType != contract.ProgramType)
            return false;
        if (!string.IsNullOrWhiteSpace(route.ContractNumber))
            return string.Equals(route.ContractNumber.Trim(), contract.ContractNumber.Trim(), StringComparison.OrdinalIgnoreCase);

        return _contracts.Count(x =>
            x.ProgramType == contract.ProgramType &&
            string.Equals(x.TenderNumber.Trim(), contract.TenderNumber.Trim(), StringComparison.OrdinalIgnoreCase)) == 1;
    }

    private void ApplyContractToForm(ContractDraftData contract)
    {
        ResetTenderDateValidation();
        _suppressSelectionEvents = true;
        try
        {
            TenderNumberTextBox.Text = contract.TenderNumber;
            ContractNumberTextBox.Text = contract.ContractNumber;
            ContractAmountTextBox.Text = FormatCalculationInput(contract.ContractAmount);
            ContractWorkDaysTextBox.Text = contract.ContractWorkDays.ToString(CultureInfo.CurrentCulture);
            ContractTransportDailyPriceTextBox.Text = FormatCalculationInput(contract.TransportDailyPrice);
            StartDatePicker.SelectedDate = contract.StartDate.ToDateTime(TimeOnly.MinValue);
            EndDatePicker.SelectedDate = contract.EndDate.ToDateTime(TimeOnly.MinValue);
            TenderDatePicker.SelectedDate = contract.TenderDate?.ToDateTime(TimeOnly.MinValue);
            ContractDatePicker.SelectedDate = contract.ContractDate?.ToDateTime(TimeOnly.MinValue);
            TransportProgramTypeComboBox.SelectedItem = TransportProgramTypeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag?.ToString(),
                    contract.ProgramType.ToString(),
                    StringComparison.OrdinalIgnoreCase));

        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateContractSummaryView();
    }

    private void ClearContractFormOnly()
    {
        ResetTenderDateValidation();
        TenderNumberTextBox.Clear();
        ContractNumberTextBox.Clear();
        ContractWorkDaysTextBox.Clear();
        ContractTransportDailyPriceTextBox.Clear();
        ContractAmountTextBox.Clear();
        StartDatePicker.SelectedDate = null;
        EndDatePicker.SelectedDate = null;
        TenderDatePicker.SelectedDate = null;
        ContractDatePicker.SelectedDate = null;
    }

    private bool TryGetSelectedTransportationProgramType(out TransportationProgramType programType)
    {
        var tag = (TransportProgramTypeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return Enum.TryParse<TransportationProgramType>(tag, ignoreCase: true, out programType);
    }

    private TransportationProgramType GetSelectedTransportationProgramType()
    {
        // Existing selector/summary code needs a stable value during initial view
        // setup. Save handlers use TryGetSelectedTransportationProgramType and
        // explicitly reject missing selection before creating official records.
        return TryGetSelectedTransportationProgramType(out var programType)
            ? programType
            : TransportationProgramType.SpecialEducation;
    }

    private static bool HasGuideStaff(TransportationProgramType programType) =>
        programType == TransportationProgramType.SpecialEducation;

    private string GetContractWorkName(ContractDraftData contract)
    {
        if (_editingArchiveSnapshot is { } archive && ReferenceEquals(contract, archive.Contract))
            return OfficialTemplateProfile.DeriveWorkName(archive.Route.Name, contract.WorkName);

        return ContractScreenModel.WorkName(contract,
            _routes.Where(route => IsRouteForContract(route, contract)).Select(route => route.Name));
    }

    private void RefreshContractWorkNameDisplays()
    {
        // Keep selectors bound to the original contracts; only presentation changes.
        _contractWorkNameConverter.UpdateTitles(_contracts.Distinct()
            .ToDictionary(contract => contract, GetContractWorkName));
        UpdateHomeTenderSummary(HomeTenderListBox.SelectedItem as ContractDraftData);
    }

    private void UpdateOfficialTemplateLabels(TransportationProgramType programType)
    {
        var profile = OfficialTemplateProfile.For(programType);
        CalculationSheetNameTextBlock.Text = profile.CalculationSheetName;
        ServiceReportSheetNameTextBlock.Text = OfficialTemplateProfile.For(
            (ReportRouteComboBox.SelectedItem as RouteDraft)?.ProgramType ?? programType).ServiceReportSheetName;
        RouteNameLabel.Text = profile.RouteNameLabel;
        ProgressRouteNameLabel.Text = profile.RouteNameLabel;
        PriceDifferenceLabel.Text = profile.TransportPriceDifferenceLabel;
        TransportPriceDifferenceResultLabel.Text = profile.TransportPriceDifferenceLabel;
        GuidePriceDifferenceLabel.Text = profile.GuidePriceDifferenceLabel ?? string.Empty;
        GuidePriceDifferenceResultLabel.Text = profile.GuidePriceDifferenceLabel ?? string.Empty;
        var guideVisibility = profile.GuidePriceDifferenceLabel is null ? Visibility.Collapsed : Visibility.Visible;
        GuidePriceDifferenceResultLabel.Visibility = guideVisibility;
        GuidePriceDifferenceResultTextBlock.Visibility = guideVisibility;
        NetPayableLabel.Text = profile.NetPayableLabel;
        NetControlLabel.Text = profile.NetPayableLabel;
        ProgressNetSummaryLabel.Text = profile.NetPayableLabel;

        foreach (var column in MultiRoutePaymentGrid.Columns.OfType<DataGridBoundColumn>())
        {
            var path = (column.Binding as System.Windows.Data.Binding)?.Path.Path;
            if (path == nameof(MultiRoutePaymentDraftRow.PriceDifference))
                column.Header = profile.TransportPriceDifferenceLabel;
            if (path == nameof(MultiRoutePaymentDraftRow.GuidePriceDifference))
            {
                column.Header = profile.GuidePriceDifferenceLabel ?? string.Empty;
                column.Visibility = guideVisibility;
            }
        }
    }

    private void UpdateRouteProgramTypeFields(ContractDraftData? contract)
    {
        ApplyRouteProgramFields(contract?.ProgramType);
    }

    private void ApplyRouteProgramFields(TransportationProgramType? program)
    {
        RouteNameLabel.Text = OfficialTemplateProfile.For(program ?? GetSelectedTransportationProgramType()).RouteNameLabel;
        var visible = program.HasValue && HasGuideStaff(program.Value);
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RouteGuideDailyPriceLabel.Visibility = visibility;
        RouteGuideDailyPriceTextBox.Visibility = visibility;
        RouteGuideFieldPanel.Visibility = visibility;
        if (RouteDailyPriceTextBox.Parent is FrameworkElement pricePanel)
            Grid.SetColumnSpan(pricePanel, visible ? 1 : 3);

        if (!visible)
        {
            RouteGuideDailyPriceTextBox.Text = "0";
        }
    }

    private void UpdateProgressProgramTypeFields(ContractDraftData? contract)
    {
        UpdateOfficialTemplateLabels(contract?.ProgramType ?? GetSelectedTransportationProgramType());
        var visible = contract is not null && HasGuideStaff(contract.ProgramType);
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ProgressGuideWorkDaysLabel.Visibility = visibility;
        GuideWorkDaysTextBox.Visibility = visibility;
        ProgressGuideDailyPriceLabel.Visibility = visibility;
        GuideDailyPriceTextBox.Visibility = visibility;
        GuideDiscountLabel.Visibility = visibility;
        GuideDiscountTextBox.Visibility = visibility;
        GuidePriceDifferenceLabel.Visibility = visibility;
        GuidePriceDifferenceTextBox.Visibility = visibility;
        SetMinimumWageSupportInputMode(contract?.ProgramType);

        if (!visible)
        {
            GuideWorkDaysTextBox.Text = "0";
            GuideDailyPriceTextBox.Text = "0";
            GuideDiscountTextBox.Text = "0";
            GuidePriceDifferenceTextBox.Text = "0";
        }
    }

    private void SetMinimumWageSupportInputMode(
        TransportationProgramType? programType,
        bool restoreSavedValue = false,
        decimal savedAmount = 0m)
    {
        var hasContract = programType.HasValue;
        var visibility = hasContract ? Visibility.Visible : Visibility.Collapsed;

        MinimumWageSupportLabel.Visibility = visibility;
        MinimumWageSupportTextBox.Visibility = visibility;
        MinimumWageSupportEnabledCheckBox.Visibility = Visibility.Collapsed;

        _suppressMinimumWageSupportToggle = true;
        try
        {
            MinimumWageSupportEnabledCheckBox.IsChecked = true;
            MinimumWageSupportTextBox.IsEnabled = hasContract;
            if(restoreSavedValue)MinimumWageSupportTextBox.Text=FormatCalculationInput(savedAmount);
        }
        finally
        {
            _suppressMinimumWageSupportToggle = false;
        }
    }

    private void RestoreMinimumWageSupportInputState(
        TransportationProgramType programType,
        decimal savedAmount) =>
        SetMinimumWageSupportInputMode(programType, restoreSavedValue: true, savedAmount: savedAmount);

    private void MinimumWageSupportEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _suppressMinimumWageSupportToggle)
            return;

        MinimumWageSupportTextBox.IsEnabled = MinimumWageSupportLabel.Visibility==Visibility.Visible;
    }

}
