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
    private void SaveContractorButton_Click(object sender, RoutedEventArgs e)
    {
        ClearContractorValidation();
        if (IsArchiveEditing)
        {
            if (IsArchiveCorrectionFor(ArchiveCorrectionSection.Contractor))
            {
                SaveArchivedContractorCorrection();
            }
            else
            {
                ShowAlreadyRegistered("Arşiv düzeltmesi devam ediyor. Önce ilgili düzenleme panelindeki DÜZENLEME İPTAL işlemini tamamlayın.");
            }
            return;
        }


        var name = ContractorNameTextBox.Text.Trim();
        var tax = OehsIdentityService.NormalizeIdentity(ContractorTaxTextBox.Text);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(tax))
        {
            if (string.IsNullOrWhiteSpace(name)) MarkInvalid(ContractorNameTextBox);
            if (string.IsNullOrWhiteSpace(tax)) MarkInvalid(ContractorTaxTextBox);
            ShowContractorValidation("Firma adı ve TC / Vergi No zorunludur.");
            return;
        }

        var editingContractor = FindContractorByKey(_editingContractorKey);
        if (!string.IsNullOrWhiteSpace(_editingContractorKey) && editingContractor is null)
        {
            ShowContractorValidation("Düzenlenen firma bulunamadı. Düzenlemeyi iptal edip yeni kayıt açın; firma kaydedilmedi.");
            return;
        }
        if (editingContractor is null &&
            !TryGetSelectedTransportationProgramType(out _))
        {
            MarkInvalid(ContractorProgramComboBox);
            ShowContractorValidation("Önce TAŞIMA PROGRAMI alanından Özel Eğitim Taşıma veya Temel Eğitim/Ortaöğretim Taşıma programını seçin. Firma kaydedilmedi.");
            return;
        }

        var contractorProgramType = editingContractor?.ProgramType ??
            (TryGetSelectedTransportationProgramType(out var selectedContractorProgram)
                ? selectedContractorProgram
                : throw new InvalidOperationException("Yeni firma için taşıma programı seçilmelidir."));
        var duplicate = _contractors.FirstOrDefault(x =>
            x.ProgramType == contractorProgramType &&
            string.Equals(
                OehsIdentityService.NormalizeIdentity(x.TaxOrIdentityNumber),
                tax,
                StringComparison.Ordinal) &&
            !ReferenceEquals(x, editingContractor));

        if (duplicate is not null)
        {
            MarkInvalid(ContractorTaxTextBox);
            ShowContractorValidation($"Bu TC / Vergi No ile '{duplicate.Name}' aynı programda zaten kayıtlı. Listeden DÜZENLE düğmesini kullanın; mükerrer firma oluşturulmadı.");
            return;
        }

        var updated = new ContractorDraft(
            name,
            tax,
            editingContractor?.Address ?? string.Empty,
            editingContractor?.Phone ?? string.Empty,
            editingContractor?.Email ?? string.Empty,
            ContractorBankTextBox.Text.Trim(),
            editingContractor?.ContactPerson ?? string.Empty,
            contractorProgramType,
            ContractorVatExemptCheckBox.IsChecked == true);

        if (!ConfirmLiveHistoryCorrection("Yüklenici bilgileri"))
            return;

        var contractsBeforeSave = _contracts.ToArray();
        var contractorsBeforeSave = _contractors.ToArray();
        var routesBeforeSave = _routes.ToArray();
        var hybridDraftsBeforeSave = SavedHybridPaymentDrafts.ToArray();
        var hybridKeysBeforeSave = _multiRouteDraftCache.ToArray();
        var historyBeforeSave = _progressHistory.ToArray();
        var editingContractorKeyBeforeSave = _editingContractorKey;

        if (_editingContractorKey is null)
        {
            _contractors.Add(updated);
        }
        else
        {
            var existing = FindContractorByKey(_editingContractorKey);

            if (existing is null)
                _contractors.Add(updated);
            else
            {
                var index = _contractors.IndexOf(existing);
                try
                {
                    UpdateReferencesForContractorEdit(existing, updated);
                    _contractors[index] = updated;
                }
                catch (ProgressPaymentPropagationException ex)
                {
                    ContractorStatusTextBlock.Text = $"Firma kaydedilmedi: {ex.Message}";
                    return;
                }
            }
        }

        if (!SavePersistedData())
        {
            RestoreHybridDraftIdentitySnapshot(hybridDraftsBeforeSave,hybridKeysBeforeSave,routesBeforeSave);
            RestoreLiveDataCollections(
                contractsBeforeSave, contractorsBeforeSave, routesBeforeSave, historyBeforeSave);
            _editingContractorKey = editingContractorKeyBeforeSave;
            RefreshViewsAfterPersistenceRollback();
            SetContractorEditorMode(!string.IsNullOrWhiteSpace(editingContractorKeyBeforeSave));
            ContractorStatusTextBlock.Text =
                "Firma kaydı diske yazılamadı. Bellekteki değişiklik geri alındı; form bilgileri yeniden deneyebilmeniz için korundu.";
            return;
        }

        _editingContractorKey = null;
        UpdateContractorSummary();
        UpdateContractSelectors();
        UpdateHomeSummaryView();
        if (_historyCorrectionSection == HistoryCorrectionSection.Contractor)
            RefreshViewsAfterHistoryCorrection();

        ClearContractorButton_Click(sender, e);
        ContractorStatusTextBlock.Text = $"Firma ve KDV istisnası kaydedildi: {name}.";
    }

    private bool IsVatExemptForCurrentFirm(ContractDraftData? contract)
    {
        if (_editingArchiveSnapshot is not null)
            return _editingArchiveSnapshot.ProgressPayment.Input.IsVatExempt;

        if (ProgressContractorComboBox.SelectedItem is ContractorDraft selectedFirm)
            return selectedFirm.VatExempt;

        return contract?.VatExempt == true;
    }

    private bool HasLegacyContractVatExemption(string taxOrIdentityNumber, TransportationProgramType programType)
    {
        var normalizedIdentity = OehsIdentityService.NormalizeIdentity(taxOrIdentityNumber);
        return _contracts.Any(contract =>
            contract.VatExempt && contract.ProgramType == programType &&
            string.Equals(
                OehsIdentityService.NormalizeIdentity(contract.ContractorTaxOrIdentityNumber),
                normalizedIdentity,
                StringComparison.Ordinal));
    }

    private void ContractorVatExemptCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _suppressSelectionEvents || IsArchiveEditing)
            return;

        var selectedState = ContractorVatExemptCheckBox.IsChecked == true ? "İSTİSNA VAR" : "İSTİSNA YOK";
        ContractorStatusTextBlock.Text =
            $"KDV istisnası firma kartında {selectedState} olarak seçildi; kalıcı olması için FİRMA KAYDET düğmesine basın.";
    }

    private void EditContractorButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveEditing)
        {
            ShowContractorValidation("Arşiv düzeltmesi sürüyor. Önce değişiklikleri kaydedin veya düzenlemeyi iptal edin.");
            return;
        }
        if (sender is not Button { Tag: ContractorDraft contractor })
            return;
        ClearArchiveCorrectionState();
        ClearContractorValidation();

        _editingContractorKey = BuildContractorKey(contractor);
        SetContractorEditorMode(true);
        ContractorNameTextBox.Text = contractor.Name;
        ContractorTaxTextBox.Text = contractor.TaxOrIdentityNumber;
        ContractorBankTextBox.Text = contractor.BankAccountNumber;
        ContractorVatExemptCheckBox.IsChecked = contractor.VatExempt;
        ContractorStatusTextBlock.Text = $"Düzenlenen firma: {contractor.Name}. Programı korunur; değişiklikleri kaydederek tamamlayın.";
    }

    private void CancelContractorEditButton_Click(object sender, RoutedEventArgs e)
    {
        var archiveCorrection = IsArchiveCorrectionFor(ArchiveCorrectionSection.Contractor);
        ClearArchiveCorrectionState();
        _editingContractorKey = null;
        ClearContractorEditorFields();
        SetContractorEditorMode(false);

        if (archiveCorrection)
        {
            NavigateToArchives();
            return;
        }

        ContractorStatusTextBlock.Text = "Firma düzenlemesi iptal edildi; kayıt değiştirilmedi.";
    }

    private void DeleteContractorButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveEditing)
        {
            ShowContractorValidation("Arşiv düzeltmesi sürerken kayıtlı firma silinemez. Önce düzenlemeyi tamamlayın veya iptal edin.");
            return;
        }
        if (sender is not Button { Tag: ContractorDraft contractor })
            return;
        ClearContractorValidation();

        var normalizedContractorIdentity = OehsIdentityService.NormalizeIdentity(contractor.TaxOrIdentityNumber);
        var usedByContract = _contracts.Any(x => x.ProgramType == contractor.ProgramType && string.Equals(
            OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
            normalizedContractorIdentity, StringComparison.Ordinal));
        var usedByRoute = _routes.Any(x => x.ProgramType == contractor.ProgramType && string.Equals(
            OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
            normalizedContractorIdentity, StringComparison.Ordinal));
        var usedByHistory = _progressHistory.Any(x => x.Input.ProgramType == contractor.ProgramType && string.Equals(
            OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
            normalizedContractorIdentity, StringComparison.Ordinal));

        if (usedByContract || usedByRoute || usedByHistory)
        {
            ShowContractorValidation($"'{contractor.Name}' kayıtlı ihale, hat veya hakedişte kullanılıyor. Önce ilişkili kayıtları düzeltin; firma silinmedi.");
            return;
        }

        var result = NightMessageBox.Show(
            $"'{contractor.Name}' firması silinsin mi? Bu işlem geri alınamaz.",
            "Yüklenici Firmayı Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        var wasEditingDeletedFirm = ReferenceEquals(FindContractorByKey(_editingContractorKey), contractor);
        var index = _contractors.IndexOf(contractor);
        if (index < 0) return;
        _contractors.RemoveAt(index);

        if (!SavePersistedData())
        {
            _contractors.Insert(index, contractor);
            RefreshViewsAfterPersistenceRollback();
            ContractorStatusTextBlock.Text =
                "Firma silinemedi: disk kaydı başarısız oldu; firma tekrar listeye alındı.";
            return;
        }

        UpdateContractorSummary();
        UpdateContractSelectors();
        UpdateHomeSummaryView();
        if (wasEditingDeletedFirm)
            ClearContractorButton_Click(sender, e);
        ContractorStatusTextBlock.Text = $"Firma silindi: {contractor.Name}.";
    }

    private void ClearContractorButton_Click(object sender, RoutedEventArgs e)
    {
        ClearArchiveCorrectionState();
        _editingContractorKey = null;
        ClearContractorEditorFields();
        SetContractorEditorMode(false);
        ContractorStatusTextBlock.Text = "Yeni firma kaydı için alanlar temizlendi.";
    }

    private void UpdateContractorSummary()
    {
        RefreshContractorList();
    }

    private void TransportProgramTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _suppressSelectionEvents || IsArchiveEditing)
            return;

        UpdateContractSelectors();
        UpdateRouteListView();
        UpdateContractSummaryView();
        UpdateHomeSummaryView();
        UpdateContractorSummary();
        UpdateRouteSummary();
    }

    private void UpdateReferencesForContractorEdit(ContractorDraft oldContractor, ContractorDraft updated)
    {
        var oldCore = new Contractor(
            oldContractor.Name,
            oldContractor.TaxOrIdentityNumber,
            oldContractor.Address,
            oldContractor.Phone,
            oldContractor.Email,
            oldContractor.BankAccountNumber,
            oldContractor.ContactPerson,
            oldContractor.ProgramType,
            oldContractor.VatExempt);

        var newCore = new Contractor(
            updated.Name,
            updated.TaxOrIdentityNumber,
            updated.Address,
            updated.Phone,
            updated.Email,
            updated.BankAccountNumber,
            updated.ContactPerson,
            updated.ProgramType,
            updated.VatExempt);

        // Önce tüm türetilmiş hakedişler doğrulanır; hata halinde hiçbir kaynak
        // koleksiyonu değiştirilmez.
        var propagated = ProgressPaymentPropagationService.RecalculateForContractorChange(
            _progressHistory.ToArray(),
            oldCore,
            newCore);

        var normalizedOld = OehsIdentityService.NormalizeIdentity(oldContractor.TaxOrIdentityNumber);
        var routeUpdates = _routes
            .Select((route, index) => (route, index))
            .Where(x => x.route.ProgramType == oldContractor.ProgramType)
            .Where(x => string.Equals(
                OehsIdentityService.NormalizeIdentity(x.route.ContractorTaxOrIdentityNumber),
                normalizedOld,
                StringComparison.Ordinal))
            .Select(x => (x.index, route: x.route with
            {
                ContractorTaxOrIdentityNumber = newCore.TaxOrIdentityNumber,
                ContractorName = newCore.Name
            }))
            .ToArray();

        foreach (var item in routeUpdates)
        {
            RekeyHybridDrafts(_routes[item.index],item.route);
            _routes[item.index] = item.route;
        }

        for (var index = 0; index < _contracts.Count; index++)
        {
            var contract = _contracts[index];
            if (contract.ProgramType == oldContractor.ProgramType &&
                string.Equals(
                    OehsIdentityService.NormalizeIdentity(contract.ContractorTaxOrIdentityNumber),
                    normalizedOld,
                    StringComparison.Ordinal))
            {
                _contracts[index] = contract with
                {
                    ContractorName = newCore.Name,
                    ContractorTaxOrIdentityNumber = newCore.TaxOrIdentityNumber,
                    BankAccountNumber = newCore.BankAccountNumber
                };
            }
        }

        _progressHistory.Clear();
        foreach (var item in propagated)
            _progressHistory.Add(item);
    }

}
