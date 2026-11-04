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
    private void LoadPersistedData()
    {
        try
        {
            var data = _dataStore.Load();
            _savedHybridPaymentDrafts = (data.HybridPaymentDrafts ?? Array.Empty<HybridPaymentDraftData>()).ToList();
            var loadedConstants = data.CalculationConstants ?? ProgressPaymentCalculationConstants.Defaults;
            if (!loadedConstants.TryValidate(out var constantsError))
            {
                throw new OehsApplicationDataStoreException(
                    "Kaydedilmiş hesaplama sabitleri geçersiz. Güvenlik nedeniyle kayıt ve hesaplama durduruldu: " + constantsError,
                    new InvalidDataException(constantsError));
            }

            _calculationConstants = loadedConstants;
            _calculationConstantsHistory =
                (data.CalculationConstantsHistory ?? Array.Empty<ProgressPaymentCalculationConstantsChange>()).ToList();

            _contracts.Clear();
            var persistedContracts = data.Contracts ?? Array.Empty<ContractDraftData>();
            if (persistedContracts.Count > 0)
            {
                foreach (var contract in persistedContracts) _contracts.Add(contract);
            }
            else if (data.Contract is not null)
            {
                _contracts.Add(data.Contract);
            }

            _contractors.Clear();
            foreach (var contractor in data.Contractors ?? Array.Empty<Contractor>())
            {
                // The program-scoped store performs legacy migration once, then
                // returns each contractor in exactly one program partition.
                _contractors.Add(new ContractorDraft(
                    contractor.Name,
                    OehsIdentityService.NormalizeIdentity(contractor.TaxOrIdentityNumber),
                    contractor.Address, contractor.Phone, contractor.Email,
                    contractor.BankAccountNumber, contractor.ContactPerson, contractor.ProgramType,
                    contractor.VatExempt ?? HasLegacyContractVatExemption(contractor.TaxOrIdentityNumber, contractor.ProgramType)));
            }

            _routes.Clear();
            foreach (var route in data.Routes ?? Array.Empty<RouteData>())
            {
                var tender = string.IsNullOrWhiteSpace(route.TenderNumber)
                    ? _contracts.FirstOrDefault()?.TenderNumber ?? string.Empty
                    : route.TenderNumber;
                var tenderContracts = _contracts.Where(x =>
                    x.ProgramType == route.ProgramType &&
                    string.Equals(x.TenderNumber, tender, StringComparison.OrdinalIgnoreCase)).ToArray();

                var contractorTax = OehsIdentityService.NormalizeIdentity(route.ContractorTaxOrIdentityNumber);
                var routeContract = !string.IsNullOrWhiteSpace(route.ContractNumber)
                    ? FindContract(tender, route.ContractNumber, route.ProgramType)
                    : tenderContracts.Where(x => x.ProgramType == route.ProgramType).ToArray() is var programContracts && programContracts.Length == 1
                        ? programContracts[0]
                        : null;

                if (routeContract is null && string.IsNullOrWhiteSpace(route.ContractNumber) && !string.IsNullOrWhiteSpace(contractorTax))
                {
                    var contractorMatches = tenderContracts.Where(x =>
                        string.Equals(
                            OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
                            contractorTax,
                            StringComparison.Ordinal)).ToArray();
                    if (contractorMatches.Length == 1)
                        routeContract = contractorMatches[0];
                }

                if (string.IsNullOrWhiteSpace(contractorTax))
                    contractorTax = OehsIdentityService.NormalizeIdentity(routeContract?.ContractorTaxOrIdentityNumber ?? string.Empty);

                var contractorName = FindContractor(contractorTax, routeContract?.ProgramType ?? route.ProgramType)?.Name ?? routeContract?.ContractorName ?? string.Empty;
                var contractNumber = !string.IsNullOrWhiteSpace(route.ContractNumber)
                    ? route.ContractNumber
                    : routeContract?.ContractNumber ?? string.Empty;

                _routes.Add(new RouteDraft(
                    route.RouteNumber, route.Name, route.DailyPrice,
                    route.GuideDailyPrice, route.TransportWorkDays,
                    tender, contractorTax, contractorName, contractNumber,
                    routeContract?.ProgramType ?? route.ProgramType,
                    route.ContractAmount ?? routeContract?.ContractAmount,
                    route.ContractDate ?? routeContract?.ContractDate));
            }

            _progressHistory.Clear();
            foreach (var history in data.ProgressPayments ?? Array.Empty<ProgressPaymentHistoryData>())
            {
                var contract = FindContract(history.TenderNumber, history.ContractNumber, history.Input.ProgramType);
                if (contract is null && string.IsNullOrWhiteSpace(history.ContractNumber))
                {
                    var tenderContracts = _contracts.Where(x =>
                        string.Equals(x.TenderNumber, history.TenderNumber, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (tenderContracts.Length == 1)
                        contract = tenderContracts[0];
                    else if (!string.IsNullOrWhiteSpace(history.ContractorTaxOrIdentityNumber))
                    {
                        var identity = OehsIdentityService.NormalizeIdentity(history.ContractorTaxOrIdentityNumber);
                        var contractorMatches = tenderContracts.Where(x =>
                            string.Equals(OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber), identity, StringComparison.Ordinal))
                            .ToArray();
                        if (contractorMatches.Length == 1)
                            contract = contractorMatches[0];
                    }
                }

                var contractorTax = OehsIdentityService.NormalizeIdentity(
                    history.ContractorTaxOrIdentityNumber ?? contract?.ContractorTaxOrIdentityNumber ?? string.Empty);
                var contractor = FindContractor(contractorTax, history.Input.ProgramType);
                var contractNumber = !string.IsNullOrWhiteSpace(history.ContractNumber)
                    ? history.ContractNumber
                    : contract?.ContractNumber ?? string.Empty;

                _progressHistory.Add(history with
                {
                    ContractNumber = contractNumber,
                    ContractorTaxOrIdentityNumber = string.IsNullOrWhiteSpace(contractorTax)
                        ? history.ContractorTaxOrIdentityNumber
                        : contractorTax,
                    ContractorName = contractor?.Name ?? history.ContractorName ?? contract?.ContractorName,
                    BankAccountNumber = contractor?.BankAccountNumber ?? history.BankAccountNumber ?? contract?.BankAccountNumber
                });
            }

            ClearContractFormOnly();
            _dataLoadFailed = false;
            SetHeaderSaveStatus("Veri yüklendi", isError: false);
            UpdateContractSelectors();
            UpdateRouteListView();
            UpdateContractSummaryView();
            UpdateHomeSummaryView();
        }
        catch (OehsApplicationDataStoreException ex)
        {
            _dataLoadFailed = true;
            SetHeaderSaveStatus("Veri yükleme hatası", isError: true);
            _contracts.Clear();
            _contractors.Clear();
            _routes.Clear();
            _progressHistory.Clear();

            NightMessageBox.Show(
                $"{ex.Message}\n\nOEHS güvenlik nedeniyle mevcut bozuk/veri dosyasının üzerine yazmayacak.",
                "OEHS — Veri Güvenliği", MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateContractSelectors();
            UpdateRouteListView();
            UpdateContractSummaryView();
            UpdateHomeSummaryView();
        }
    }

    private void SetHeaderSaveStatus(string status, bool isError)
    {
        if (HeaderSaveStatusTextBlock is null || HeaderSaveStatusDot is null)
            return;

        var foreground = isError
            ? System.Windows.Media.Color.FromRgb(255, 190, 190)
            : System.Windows.Media.Color.FromRgb(221, 246, 231);
        var indicator = isError
            ? System.Windows.Media.Color.FromRgb(230, 65, 78)
            : System.Windows.Media.Color.FromRgb(67, 199, 122);

        HeaderSaveStatusTextBlock.Text = status;
        HeaderSaveStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(foreground);
        HeaderSaveStatusDot.Fill = new System.Windows.Media.SolidColorBrush(indicator);
    }

    private bool SavePersistedData(bool showErrorDialog = true)
    {
        if (_dataLoadFailed)
        {
            SetHeaderSaveStatus("Kaydetme durduruldu", isError: true);
            if (showErrorDialog) NightMessageBox.Show(
                "OEHS veri dosyası güvenli olarak yüklenemedi. Veri kaybını önlemek için kaydetme durduruldu.",
                "OEHS — Kaydetme Durduruldu", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        var savedDraftsBeforeSave = SavedHybridPaymentDrafts.ToArray();
        ReconcileSavedHybridSources();
        var activeContract = GetSelectedContract() ?? _contracts.FirstOrDefault();
        var data = new OehsApplicationData(
            activeContract,
            _routes.Select(x => new RouteData(
                x.RouteNumber, x.Name, x.DailyPrice, x.GuideDailyPrice,
                x.TransportWorkDays, x.TenderNumber, x.ContractorTaxOrIdentityNumber, x.ContractNumber, x.ProgramType, x.ContractAmount, x.ContractDate)).ToArray(),
            _contractors.Select(x => new Contractor(
                x.Name, x.TaxOrIdentityNumber, x.Address, x.Phone, x.Email,
                x.BankAccountNumber, x.ContactPerson, x.ProgramType, x.VatExempt)).ToArray(),
            _progressHistory.ToArray(),
            _contracts.ToArray(),
            _calculationConstants,
            _calculationConstantsHistory.ToArray(),
            SavedHybridPaymentDrafts.ToArray());

        try
        {
            _dataStore.Save(data);
            SetHeaderSaveStatus("Kaydedildi", isError: false);
            return true;
        }
        catch (Exception ex) when (ex is OehsApplicationDataStoreException or IOException or UnauthorizedAccessException)
        {
            _savedHybridPaymentDrafts = savedDraftsBeforeSave.ToList();
            SetHeaderSaveStatus("Kaydetme hatası", isError: true);
            if (showErrorDialog) NightMessageBox.Show(
                $"{ex.Message}\n\nMevcut veri dosyası korunmuştur. OEHS değişikliklerinizi kaydetmedi.",
                "OEHS — Veri Kaydetme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void RestoreLiveDataCollections(
        IReadOnlyList<ContractDraftData> contracts,
        IReadOnlyList<ContractorDraft> contractors,
        IReadOnlyList<RouteDraft> routes,
        IReadOnlyList<ProgressPaymentHistoryData> history)
    {
        _contracts.Clear();
        foreach (var item in contracts) _contracts.Add(item);

        _contractors.Clear();
        foreach (var item in contractors) _contractors.Add(item);

        _routes.Clear();
        foreach (var item in routes) _routes.Add(item);

        _progressHistory.Clear();
        foreach (var item in history) _progressHistory.Add(item);
    }

    private void RefreshViewsAfterPersistenceRollback()
    {
        UpdateContractSelectors();
        UpdateContractorSummary();
        UpdateRouteSummary();
        UpdateRouteListView();
        UpdateContractSummaryView();
        UpdateHistoryView();
        UpdateSummaryView();
        UpdateReportView();
        UpdateHomeSummaryView();
        UpdateAcceptanceView();
    }

    private void ClearContractButton_Click(object sender, RoutedEventArgs e)
    {
        ClearArchiveCorrectionState();
        _editingContractTender = null;
        _editingContractNumber = null;
        _editingContractProgramType = null;
        ClearContractFormOnly();
        UpdateContractSummaryView();
        ContractStatusTextBlock.Text = "Yeni ihale/sözleşme kaydı için alanlar temizlendi.";
    }

    private static string OehsDesktopRoot => OehsOutputPathService.DesktopRoot;

}
