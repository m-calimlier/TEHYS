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
    private void EditContractButton_Click(object sender, RoutedEventArgs e)
    {
        ClearArchiveCorrectionState();
        if (sender is not Button { Tag: ContractDraftData contract })
            return;

        _editingContractTender = contract.TenderNumber;
        _editingContractNumber = contract.ContractNumber;
        _editingContractProgramType = contract.ProgramType;
        ApplyContractToForm(contract);
        UpdateContractSelectors();
        SetContractEditorMode(true);
        NavigationButton_Click(NavContract, new RoutedEventArgs(Button.ClickEvent, NavContract));
        ContractStatusTextBlock.Text = $"Düzenleme modu: İKN {contract.TenderNumber}. Değiştirip DÜZENLE'e basın.";
    }

    private void CancelContractEditButton_Click(object sender, RoutedEventArgs e)
    {
        var archiveCorrection = IsArchiveCorrectionFor(ArchiveCorrectionSection.Contract);
        var original = !archiveCorrection && !string.IsNullOrWhiteSpace(_editingContractTender)
            ? FindContract(_editingContractTender, _editingContractNumber, _editingContractProgramType)
            : null;

        ClearArchiveCorrectionState();
        _editingContractTender = null;
        _editingContractNumber = null;
        _editingContractProgramType = null;

        if (archiveCorrection)
        {
            ClearContractFormOnly();
            NavigateToArchives();
            return;
        }

        if (original is not null) ApplyContractToForm(original);
        else ClearContractFormOnly();
        ContractStatusTextBlock.Text = "Sözleşme düzenlemesi iptal edildi; kayıt değiştirilmedi.";
    }

    private void DeleteContractButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContractDraftData contract })
            return;

        var routeCount = _routes.Count(x => IsRouteForContract(x, contract));
        var historyCount = _progressHistory.Count(x => IsHistoryForContract(x, contract));

        // Do not orphan legacy data that couldn't be assigned safely during migration.
        // If a tender has multiple contract parts and a legacy row lacks ContractNumber,
        // the UI cannot determine which part owns it, so deletion must be blocked.
        var ambiguousRouteCount = _routes.Count(x =>
            string.IsNullOrWhiteSpace(x.ContractNumber) &&
            string.Equals(x.TenderNumber.Trim(), contract.TenderNumber.Trim(), StringComparison.OrdinalIgnoreCase));
        var ambiguousHistoryCount = _progressHistory.Count(x =>
            string.IsNullOrWhiteSpace(x.ContractNumber) &&
            string.Equals(x.TenderNumber?.Trim(), contract.TenderNumber.Trim(), StringComparison.OrdinalIgnoreCase));

        if (routeCount > 0 || historyCount > 0 || ambiguousRouteCount > 0 || ambiguousHistoryCount > 0)
        {
            var ambiguous = ambiguousRouteCount + ambiguousHistoryCount;
            var ambiguityNote = ambiguous > 0
                ? $" Ayrıca {ambiguous} eski hat/hakediş kaydının sözleşme bağı belirsiz; önce bu kayıtların bağı netleştirilmelidir."
                : string.Empty;
            NightMessageBox.Show(
                $"İKN '{contract.TenderNumber}' / '{contract.WorkName}' kaydı {routeCount} bağlı hat ve {historyCount} bağlı hakedişle ilişkili. Önce ilişkili kayıtları düzeltin; kayıt silinmedi.{ambiguityNote}",
                "TEHYS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = NightMessageBox.Show(
            $"İKN '{contract.TenderNumber}' / '{contract.WorkName}' kaydı silinsin mi?",
            "Sözleşme Kaydını Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        var index = _contracts.IndexOf(contract);
        if (index < 0) return;
        _contracts.RemoveAt(index);
        if (!SavePersistedData())
        {
            _contracts.Insert(index, contract);
            RefreshViewsAfterPersistenceRollback();
            ContractStatusTextBlock.Text =
                "Sözleşme silinemedi: disk kaydı başarısız oldu; sözleşme tekrar listeye alındı.";
            return;
        }
        UpdateContractSelectors();
        UpdateHomeSummaryView();
        UpdateContractSummaryView();
    }

    private void SaveContractButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveEditing)
        {
            if (IsArchiveCorrectionFor(ArchiveCorrectionSection.Contract))
            {
                SaveArchivedContractCorrection();
            }
            else
            {
                ShowAlreadyRegistered("Arşiv düzeltmesi devam ediyor. Önce ilgili düzenleme panelindeki DÜZENLEME İPTAL işlemini tamamlayın.");
            }
            return;
        }

        ClearContractValidationVisuals();

        // A new contract must be explicitly assigned to a transport program.
        // Do not let the legacy fallback in GetSelectedTransportationProgramType()
        // silently classify an unselected program as Special Education.
        if (_editingContractTender is null &&
            !TryGetSelectedTransportationProgramType(out _))
        {
            ShowContractValidation("Önce TAŞIMA PROGRAMI alanından Özel Eğitim Taşıma veya Temel Eğitim/Ortaöğretim Taşıma programını seçin.");
            return;
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(TenderNumberTextBox.Text))
        {
            errors.Add("İhale / Dosya No");
            MarkInvalid(TenderNumberTextBox);
        }
        if (!TryReadLegacyContractAmount(ContractAmountTextBox.Text, out var contractAmount))
        {
            errors.Add("Eski sözleşme bedeli");
            MarkInvalid(ContractAmountTextBox);
        }
        var contractWorkDays = 0;
        if (!string.IsNullOrWhiteSpace(ContractWorkDaysTextBox.Text) &&
            (!int.TryParse(ContractWorkDaysTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out contractWorkDays) || contractWorkDays < 0))
        {
            errors.Add("Sözleşme İş Günü");
            MarkInvalid(ContractWorkDaysTextBox);
        }
        if (!TryReadLegacyContractAmount(ContractTransportDailyPriceTextBox.Text, out var transportDailyPrice))
        {
            errors.Add("Eski taşıma günlük fiyatı");
            MarkInvalid(ContractTransportDailyPriceTextBox);
        }
        if (!HasValidTenderDateText(StartDatePicker)) { errors.Add("Başlangıç tarihi metni"); MarkInvalid(StartDatePicker); }
        if (!HasValidTenderDateText(EndDatePicker)) { errors.Add("Bitiş tarihi metni"); MarkInvalid(EndDatePicker); }
        if (!HasValidTenderDateText(TenderDatePicker)) { errors.Add("İhale tarihi metni"); MarkInvalid(TenderDatePicker); }
        var startDate = StartDatePicker.SelectedDate;
        var endDate = EndDatePicker.SelectedDate;
        if (!startDate.HasValue)
        {
            errors.Add("Başlangıç Tarihi");
            MarkInvalid(StartDatePicker);
        }
        if (!endDate.HasValue)
        {
            errors.Add("Bitiş Tarihi");
            MarkInvalid(EndDatePicker);
        }
        if (startDate.HasValue && endDate.HasValue && endDate.Value.Date < startDate.Value.Date)
        {
            errors.Add("Başlangıç / Bitiş Tarihi");
            MarkInvalid(StartDatePicker);
            MarkInvalid(EndDatePicker);
        }

        if (errors.Count > 0)
        {
            ShowContractValidation("Lütfen şu alanları kontrol edin: " + string.Join(", ", errors) + ".");
            return;
        }

        var sourceTenderForEdit = _editingContractTender ?? TenderNumberTextBox.Text.Trim();
        var sourceContractNumberForEdit = _editingContractNumber ?? string.Empty;
        var oldContract = _editingContractTender is null
            ? null
            : FindContract(sourceTenderForEdit, sourceContractNumberForEdit, _editingContractProgramType ?? GetSelectedTransportationProgramType());

        if (_editingContractTender is not null && oldContract is null)
        {
            ShowAlreadyRegistered("Düzenlenen sözleşme artık bulunamıyor. Değişiklik uygulanmadı.");
            return;
        }

        var internalContractNumber = oldContract?.ContractNumber ?? "INTERNAL-" + Guid.NewGuid().ToString("N");
        ContractNumberTextBox.Text = internalContractNumber;

        var proposedContract = new ContractDraftData(
            TenderNumberTextBox.Text.Trim(),
            internalContractNumber,
            // Derived titles belong to presentation/output, not stored contracts.
            oldContract?.WorkName ?? string.Empty,
            // A new contract/part must not silently inherit a sibling contract's contractor.
            // Contractor identity is associated with the route and is entered/selected there.
            oldContract?.ContractorName ?? string.Empty,
            contractAmount,
            DateOnly.FromDateTime(startDate!.Value),
            DateOnly.FromDateTime(endDate!.Value),
            oldContract?.VatExempt ?? false,
            TenderDatePicker.SelectedDate.HasValue ? DateOnly.FromDateTime(TenderDatePicker.SelectedDate.Value) : null,
            ContractDatePicker.SelectedDate.HasValue ? DateOnly.FromDateTime(ContractDatePicker.SelectedDate.Value) : null,
            oldContract?.ContractorTaxOrIdentityNumber ?? string.Empty,
            oldContract?.BankAccountNumber ?? string.Empty,
            contractWorkDays,
            transportDailyPrice,
            oldContract?.ProgramType ??
                (TryGetSelectedTransportationProgramType(out var selectedProgramType)
                    ? selectedProgramType
                    : throw new InvalidOperationException("Yeni sözleşme için taşıma programı seçilmelidir.")));

        var duplicateTarget = _contracts.FirstOrDefault(x =>
            string.Equals(
                OehsIdentityService.NormalizeIdentity(x.TenderNumber),
                OehsIdentityService.NormalizeIdentity(proposedContract.TenderNumber),
                StringComparison.Ordinal) &&
            !ReferenceEquals(x, oldContract));

        if (duplicateTarget is not null && !ReferenceEquals(duplicateTarget, oldContract))
        {
            ShowAlreadyRegistered(
                $"İKN '{proposedContract.TenderNumber}' zaten kayıtlı. Aynı ihale için yeni kayıt açmayın; farklı yüklenicileri Hatlar bölümünde bu İKN'ye bağlayın.");
            return;
        }

        var contractsBeforeSave = _contracts.ToArray();
        var routesBeforeSave = _routes.ToArray();
        var hybridDraftsBeforeSave = SavedHybridPaymentDrafts.ToArray();
        var hybridKeysBeforeSave = _multiRouteDraftCache.ToArray();
        var historyBeforeSave = _progressHistory.ToArray();
        var editingContractTenderBeforeSave = _editingContractTender;
        var editingContractNumberBeforeSave = _editingContractNumber;

        if (oldContract is null)
        {
            _contracts.Add(proposedContract);
        }
        else
        {
            var affectedPaymentPeriods = _progressHistory
                .Where(x => IsHistoryForContract(x, oldContract))
                .Where(x => x.PeriodDate.HasValue)
                .Select(x => x.PeriodDate!.Value)
                .ToArray();

            if (affectedPaymentPeriods.Any(period =>
                    period < proposedContract.StartDate || period > proposedContract.EndDate))
            {
                ShowContractValidation(
                    "Sözleşme tarih aralığı bu sözleşmeye bağlı mevcut hakediş dönemlerini kapsamalıdır. " +
                    "Dönem aralığı dışına çıkan en az bir hakediş var.");
                MarkInvalid(StartDatePicker);
                MarkInvalid(EndDatePicker);
                return;
            }

            var routeData = _routes.Select(x => new RouteData(
                x.RouteNumber,
                x.Name,
                x.DailyPrice,
                x.GuideDailyPrice,
                x.TransportWorkDays,
                x.TenderNumber,
                x.ContractorTaxOrIdentityNumber,
                x.ContractNumber, x.ProgramType, x.ContractAmount, x.ContractDate)).ToArray();

            var proposedHistories = ProgressPaymentPropagationService.RecalculateForContractChange(
                _progressHistory.ToArray(), routeData, oldContract, proposedContract, _calculationConstants);
            var proposedTargetPayment = _historyCorrectionTargetId is Guid targetId
                ? proposedHistories.FirstOrDefault(x => IsOperationPayment(x, targetId))
                : null;
            if (!ConfirmLiveHistoryCorrection("Sözleşme bilgileri", proposedTargetPayment))
                return;

            var propagatedRoutes = TenderRoutePropagationService.UpdateContractIdentity(
                routeData, oldContract, proposedContract);
            var affectedRouteIndices = _routes
                .Select((route, routeIndex) => (route, routeIndex))
                .Where(x => IsRouteForContract(x.route, oldContract))
                .ToArray();

            var oldIndex = _contracts.IndexOf(oldContract);
            if (oldIndex < 0)
            {
                ShowAlreadyRegistered("Düzenlenen sözleşme listede bulunamadı. Değişiklik uygulanmadı.");
                return;
            }

            _contracts[oldIndex] = proposedContract;
            foreach (var entry in affectedRouteIndices)
            {
                var updatedRoute = propagatedRoutes[entry.routeIndex];
                var nextRoute = entry.route with
                {
                    TenderNumber = updatedRoute.TenderNumber,
                    ContractNumber = updatedRoute.ContractNumber,
                    DailyPrice = entry.route.DailyPrice,
                    GuideDailyPrice = updatedRoute.GuideDailyPrice,
                    ProgramType = updatedRoute.ProgramType
                };
                RekeyHybridDrafts(entry.route,nextRoute);
                _routes[entry.routeIndex] = nextRoute;
            }

            _progressHistory.Clear();
            foreach (var item in proposedHistories)
                _progressHistory.Add(item);
        }

        if (!SavePersistedData())
        {
            RestoreHybridDraftIdentitySnapshot(hybridDraftsBeforeSave,hybridKeysBeforeSave,routesBeforeSave);
            RestoreLiveDataCollections(
                contractsBeforeSave, _contractors.ToArray(), routesBeforeSave, historyBeforeSave);
            _editingContractTender = editingContractTenderBeforeSave;
            _editingContractNumber = editingContractNumberBeforeSave;
            SetContractEditorMode(!string.IsNullOrWhiteSpace(editingContractTenderBeforeSave));
            RefreshViewsAfterPersistenceRollback();
            ContractStatusTextBlock.Text =
                "Sözleşme kaydı diske yazılamadı. Bellekteki değişiklik geri alındı; form bilgileri korundu.";
            return;
        }

        _editingContractTender = null;
        _editingContractNumber = null;
        _editingContractProgramType = null;
        SetContractEditorMode(false);
        UpdateContractSelectors();
        UpdateRouteListView();
        UpdateRouteSummary();
        UpdateContractSummaryView();
        UpdateHomeSummaryView();
        UpdateAcceptanceView();
        if (_historyCorrectionSection == HistoryCorrectionSection.Contract)
            RefreshViewsAfterHistoryCorrection();
        ClearContractFormOnly();
        ClearContractValidationVisuals();

        ContractStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 104, 216));
        ContractStatusTextBlock.Text = $"İhale kaydedildi: {proposedContract.TenderNumber}.";
    }

}
