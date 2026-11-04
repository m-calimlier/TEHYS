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
    private void AddRouteButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveEditing)
        {
            if (IsArchiveCorrectionFor(ArchiveCorrectionSection.Route))
                SaveArchivedRouteCorrection();
            else
                ShowAlreadyRegistered("Arşiv düzeltmesi devam ediyor. Önce ilgili düzenleme panelindeki DÜZENLEME İPTAL işlemini tamamlayın.");
            return;
        }

        ClearRouteValidationVisuals();
        var contract = RouteTenderListBox.SelectedItem as ContractDraftData;
        var contractor = RouteContractorListBox.SelectedItem as ContractorDraft;
        var routeNumber = RouteNumberTextBox.Text.Trim();
        var routeName = RouteNameTextBox.Text.Trim();
        var errors = new List<string>();

        if (contract is null) errors.Add("İhale / Sözleşme seçimi");
        if (contractor is null) errors.Add("Yüklenici firma seçimi");
        if (_editingRoute is not null && !_routes.Contains(_editingRoute))
        {
            ShowRouteValidation("Düzenlenen hat bulunamadı. Düzenlemeyi iptal edip yeni kayıt açın; işlem kaydedilmedi.");
            return;
        }
        if (contract is not null && contractor is not null && contractor.ProgramType != contract.ProgramType)
        {
            errors.Add("İhale ile yüklenicinin taşıma programı aynı olmalıdır");
            MarkInvalid(RouteContractorListBox);
        }
        if (_editingRoute is not null && contract is not null && _editingRoute.ProgramType != contract.ProgramType)
        {
            errors.Add("Kayıtlı hat farklı taşıma programına taşınamaz; diğer program için yeni hat açın");
            MarkInvalid(RouteTenderListBox);
        }
        if (string.IsNullOrWhiteSpace(routeNumber))
        {
            errors.Add("Hat No");
            MarkInvalid(RouteNumberTextBox);
        }
        if (string.IsNullOrWhiteSpace(routeName))
        {
            errors.Add("Hat Adı");
            MarkInvalid(RouteNameTextBox);
        }
        if (!TurkishMoneyInput.TryParseCalculationAmount(RouteDailyPriceTextBox.Text, out var routeDailyPrice) || routeDailyPrice < 0)
        {
            errors.Add("Taşıma Günlük Fiyatı");
            MarkInvalid(RouteDailyPriceTextBox);
        }
        if (!TurkishMoneyInput.TryParseCents(RouteContractAmountTextBox.Text, out var routeContractAmount) || routeContractAmount < 0)
        {
            errors.Add("Sözleşme Bedeli");
            MarkInvalid(RouteContractAmountTextBox);
        }
        if (!RouteContractDatePicker.SelectedDate.HasValue || !HasValidTenderDateText(RouteContractDatePicker))
        {
            errors.Add("Sözleşme Tarihi");
            MarkInvalid(RouteContractDatePicker);
        }
        var hasGuide = HasGuideStaff(contract?.ProgramType ?? TransportationProgramType.SpecialEducation);
        var guideDailyPrice = 0m;
        if (hasGuide)
        {
            if (!TurkishMoneyInput.TryParseCalculationAmount(RouteGuideDailyPriceTextBox.Text, out guideDailyPrice) || guideDailyPrice < 0)
            {
                errors.Add("Rehber Personel Günlük Fiyat");
                MarkInvalid(RouteGuideDailyPriceTextBox);
            }
        }
        if (errors.Count > 0)
        {
            ShowRouteValidation("Lütfen kırmızı işaretli alanları tamamlayın: " + string.Join(", ", errors) + ".");
            return;
        }

        var proposed = new RouteDraft(
            routeNumber,
            routeName,
            routeDailyPrice,
            hasGuide ? guideDailyPrice : 0m,
            _editingRoute?.TransportWorkDays ?? 0m,
            contract!.TenderNumber,
            OehsIdentityService.NormalizeIdentity(contractor!.TaxOrIdentityNumber),
            contractor.Name,
            contract.ContractNumber,
            contract.ProgramType,
            routeContractAmount,
            DateOnly.FromDateTime(RouteContractDatePicker.SelectedDate!.Value));

        var duplicate = _routes.FirstOrDefault(x =>
            x.ProgramType == proposed.ProgramType &&
            string.Equals(x.TenderNumber, proposed.TenderNumber, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ContractNumber, proposed.ContractNumber, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
                OehsIdentityService.NormalizeIdentity(proposed.ContractorTaxOrIdentityNumber),
                StringComparison.Ordinal) &&
            string.Equals(x.RouteNumber, proposed.RouteNumber, StringComparison.OrdinalIgnoreCase) &&
            !ReferenceEquals(x, _editingRoute));
        if (duplicate is not null)
        {
            MarkInvalid(RouteNumberTextBox);
            ShowRouteValidation($"Bu hat zaten kayıtlı: İKN {proposed.TenderNumber} / Hat {proposed.RouteNumber}. Listeden DÜZENLE düğmesini kullanın.");
            return;
        }

        var proposedRouteData = new RouteData(
            proposed.RouteNumber,
            proposed.Name,
            proposed.DailyPrice,
            proposed.GuideDailyPrice,
            proposed.TransportWorkDays,
            proposed.TenderNumber,
            proposed.ContractorTaxOrIdentityNumber,
            proposed.ContractNumber, proposed.ProgramType, proposed.ContractAmount, proposed.ContractDate);
        var existingRouteData = _routes
            .Where(x => !ReferenceEquals(x, _editingRoute))
            .Select(x => new RouteData(
                x.RouteNumber,
                x.Name,
                x.DailyPrice,
                x.GuideDailyPrice,
                x.TransportWorkDays,
                x.TenderNumber,
                x.ContractorTaxOrIdentityNumber,
                x.ContractNumber, x.ProgramType, x.ContractAmount, x.ContractDate))
            .ToArray();
        var ownerConflict = RouteAssignmentValidationService.FindOwnerConflict(
            existingRouteData,
            proposedRouteData);
        if (ownerConflict is not null)
        {
            var existingOwner = FindContractor(ownerConflict.ContractorTaxOrIdentityNumber, ownerConflict.ProgramType)?.Name;
            var ownerLabel = string.IsNullOrWhiteSpace(existingOwner)
                ? "başka bir yüklenici"
                : $"'{existingOwner}'";
            ShowRouteValidation(
                $"Aynı İKN ve Hat No farklı yüklenicilere atanamaz. İKN: {proposed.TenderNumber}, " +
                $"Hat No: {proposed.RouteNumber}; mevcut yüklenici: {ownerLabel}.");
            return;
        }

        var routesBeforeSave = _routes.ToArray();
        var historyBeforeSave = _progressHistory.ToArray();
        var editingRouteBeforeSave = _editingRoute;
        var routeDraftsBeforeSave = SavedHybridPaymentDrafts.ToArray();
        var hybridRouteKeysBeforeSave = _multiRouteDraftCache.ToArray();

        if (_editingRoute is null)
        {
            _routes.Add(proposed);
        }
        else
        {
            var old = _editingRoute;
            var index = _routes.IndexOf(old);
            if (index >= 0)
            {
                try
                {
                    var propagated = BuildRoutePropagation(old, proposed);
                    var proposedTargetPayment = _historyCorrectionTargetId is Guid targetId
                        ? propagated.FirstOrDefault(x => IsOperationPayment(x, targetId))
                        : null;
                    if (!ConfirmLiveHistoryCorrection("Hat bilgileri", proposedTargetPayment))
                        return;

                    RekeyHybridDrafts(old,proposed);
                    _routes[index] = proposed;
                    _progressHistory.Clear();
                    foreach (var item in propagated)
                        _progressHistory.Add(item);
                }
                catch (ProgressPaymentPropagationException ex)
                {
                    RouteStatusTextBlock.Text = $"Hat kaydedilmedi: {ex.Message}";
                    return;
                }
            }
            _editingRoute = null;
        }

        if (!SavePersistedData())
        {
            _routes.Clear();
            foreach (var item in routesBeforeSave) _routes.Add(item);
            _progressHistory.Clear();
            foreach (var item in historyBeforeSave) _progressHistory.Add(item);
            RestoreHybridDraftIdentitySnapshot(routeDraftsBeforeSave,hybridRouteKeysBeforeSave,routesBeforeSave);
            _editingRoute = editingRouteBeforeSave;
            RefreshViewsAfterPersistenceRollback();
            SetRouteEditorMode(editingRouteBeforeSave is not null);
            RouteStatusTextBlock.Text =
                "Hat kaydı diske yazılamadı. Bellekteki değişiklik geri alındı; form bilgileri korundu.";
            return;
        }

        UpdateRouteSummary();
        UpdateRouteListView();
        UpdateProgressContractorAndRoutes(ProgressTenderListBox.SelectedItem as ContractDraftData, preserveSelection: false);
        UpdateOutputRoutesForContract(contract);
        UpdateContractSummaryView();
        UpdateHomeSummaryView();
        if (_historyCorrectionSection == HistoryCorrectionSection.Route)
            RefreshViewsAfterHistoryCorrection();
        ClearRouteButton_Click(sender, e);
        RouteStatusTextBlock.Text = $"Hat kaydedildi: {routeNumber} — {routeName}.";
    }

    private IReadOnlyList<ProgressPaymentHistoryData> BuildRoutePropagation(RouteDraft oldRoute, RouteDraft updatedRoute)
    {
        var oldData = new RouteData(
            oldRoute.RouteNumber, oldRoute.Name, oldRoute.DailyPrice,
            oldRoute.GuideDailyPrice, oldRoute.TransportWorkDays,
            oldRoute.TenderNumber, oldRoute.ContractorTaxOrIdentityNumber, oldRoute.ContractNumber, oldRoute.ProgramType, oldRoute.ContractAmount, oldRoute.ContractDate);
        var newData = new RouteData(
            updatedRoute.RouteNumber, updatedRoute.Name, updatedRoute.DailyPrice,
            updatedRoute.GuideDailyPrice, updatedRoute.TransportWorkDays,
            updatedRoute.TenderNumber, updatedRoute.ContractorTaxOrIdentityNumber, updatedRoute.ContractNumber, updatedRoute.ProgramType, updatedRoute.ContractAmount, updatedRoute.ContractDate);

        var contractor = FindContractor(updatedRoute.ContractorTaxOrIdentityNumber, updatedRoute.ProgramType);
        var contractorSnapshot = contractor is null
            ? new Contractor(updatedRoute.ContractorName, updatedRoute.ContractorTaxOrIdentityNumber, ProgramType: updatedRoute.ProgramType)
            : new Contractor(
                contractor.Name,
                contractor.TaxOrIdentityNumber,
                contractor.Address,
                contractor.Phone,
                contractor.Email,
                contractor.BankAccountNumber,
                contractor.ContactPerson,
                contractor.ProgramType,
                contractor.VatExempt);

        return ProgressPaymentPropagationService.RecalculateForRouteChange(
            _progressHistory.ToArray(), oldData, newData, contractorSnapshot, _calculationConstants);
    }

    private void ShowRouteValidation(string message)
    {
        RouteValidationMessageTextBlock.Text = message;
        RouteValidationBanner.Visibility = Visibility.Visible;
        RouteStatusTextBlock.Text = "İşlem tamamlanamadı; yukarıdaki açıklamayı kontrol edin.";
        RouteStatusTextBlock.Foreground = System.Windows.Media.Brushes.LightSteelBlue;
    }

    private void ClearRouteValidationVisuals()
    {
        RouteStatusTextBlock.Foreground = System.Windows.Media.Brushes.LightSteelBlue;
        foreach (var control in new Control[]
        {
            RouteNumberTextBox,
            RouteNameTextBox,
            RouteDailyPriceTextBox,
            RouteContractAmountTextBox,
            RouteContractDatePicker,

            RouteGuideDailyPriceTextBox
        })
            ClearInvalid(control);
        ClearInvalid(RouteTenderListBox);
        ClearInvalid(RouteContractorListBox);

        RouteValidationBanner.Visibility = Visibility.Collapsed;
        RouteValidationMessageTextBlock.Text = string.Empty;
    }

    private void RemoveRouteButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveEditing)
        {
            ShowRouteValidation("Arşiv düzeltmesi sürerken kayıtlı hat silinemez. Önce düzenlemeyi tamamlayın veya iptal edin.");
            return;
        }
        if (sender is not Button { Tag: RouteDraft route })
            return;

        var historyCount = CountRouteHistoryReferences(route);

        if (historyCount > 0)
        {
            ShowRouteValidation($"Bu hat için {historyCount} hakediş kaydı bulunduğu için hat silinmedi.");
            return;
        }

        var result = NightMessageBox.Show(
            $"'{route.RouteNumber} — {route.Name}' hattı silinsin mi? Bu işlem geri alınamaz.",
            "Hat Kaydını Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        RemoveRouteAfterConfirmation(route);
    }

    private void RemoveRouteAfterConfirmation(RouteDraft route)
    {
        var index = _routes.IndexOf(route);
        if (index < 0) return;
        var routesBefore = _routes.ToArray();
        var draftsBefore = SavedHybridPaymentDrafts.ToArray();
        var cacheBefore = _multiRouteDraftCache.ToArray();
        var wasEditing = ReferenceEquals(_editingRoute, route);
        var key = HybridRouteKey(route);
        _routes.RemoveAt(index);
        SavedHybridPaymentDrafts.RemoveAll(draft => draft.RouteKey == key);
        _multiRouteDraftCache.Remove(key);

        if (!SavePersistedData(showErrorDialog: false))
        {
            _routes.Insert(index, route);
            RestoreHybridDraftIdentitySnapshot(draftsBefore, cacheBefore, routesBefore);
            RefreshViewsAfterPersistenceRollback();
            ShowRouteValidation($"Hat silinemedi: {route.RouteNumber}; disk kaydı başarısız, hat ve taslakları geri alındı.");
            return;
        }

        if (wasEditing) ClearRouteButton_Click(this, new RoutedEventArgs());
        UpdateRouteSummary();
        UpdateRouteListView();
        UpdateProgressContractorAndRoutes(ProgressTenderListBox.SelectedItem as ContractDraftData);
        UpdateOutputRoutesForContract(OutputTenderListBox.SelectedItem as ContractDraftData);
        UpdateHomeSummaryView();
        RouteStatusTextBlock.Text = $"Hat silindi: {route.RouteNumber}.";
    }

    private void EditRouteButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveEditing)
        {
            ShowRouteValidation("Arşiv düzeltmesi sürüyor. Önce değişiklikleri kaydedin veya düzenlemeyi iptal edin.");
            return;
        }
        if (sender is not System.Windows.Controls.Button { Tag: RouteDraft route })
            return;
        ClearArchiveCorrectionState();
        ResetRouteDateValidation();
        ClearRouteValidationVisuals();

        _editingRoute = route;
        SetRouteEditorMode(true);
        var contract = FindContract(route.TenderNumber, route.ContractNumber, route.ProgramType);
        var contractor = FindContractor(route.ContractorTaxOrIdentityNumber, route.ProgramType);

        _suppressSelectionEvents = true;
        try
        {
            RouteTenderListBox.SelectedItem = contract;
            RouteContractorListBox.ItemsSource = contractor is null ? Array.Empty<ContractorDraft>() : new[] { contractor };
            RouteContractorListBox.SelectedItem = contractor;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        RouteNumberTextBox.Text = route.RouteNumber;
        RouteNameTextBox.Text = route.Name;
        RouteDailyPriceTextBox.Text = FormatCalculationInput(route.DailyPrice);
        RouteContractAmountTextBox.Text = FormatCalculationInput(route.ContractAmount ?? 0m);
        RouteContractDatePicker.SelectedDate = route.ContractDate?.ToDateTime(TimeOnly.MinValue);
        RouteGuideDailyPriceTextBox.Text = FormatCalculationInput(route.GuideDailyPrice);
        ApplyRouteProgramFields(route.ProgramType);
        RouteSelectedTenderTextBlock.Text = contract is null ? "İhale bulunamadı" : $"İKN: {contract.TenderNumber}";
        RouteSelectedContractorTextBlock.Text = contractor is null ? "Yüklenici bulunamadı" : $"Yüklenici: {contractor.Name}";
        RouteStatusTextBlock.Text = $"Düzenleme modu: İKN {route.TenderNumber} / Hat {route.RouteNumber}. DÜZENLE ile kaydedin.";
    }

    private void CancelRouteEditButton_Click(object sender, RoutedEventArgs e)
    {
        var archiveCorrection = IsArchiveCorrectionFor(ArchiveCorrectionSection.Route);
        ClearArchiveCorrectionState();
        _editingRoute = null;
        ClearRouteEditorFields();
        SetRouteEditorMode(false);
        if (archiveCorrection)
        {
            NavigateToArchives();
            return;
        }
        RouteStatusTextBlock.Text = "Hat düzenlemesi iptal edildi; kayıt değiştirilmedi.";
    }

    private void ClearRouteButton_Click(object sender, RoutedEventArgs e)
    {
        ClearArchiveCorrectionState();
        _editingRoute = null;
        RouteNumberTextBox.IsReadOnly = false;
        ClearRouteEditorFields();
        // Keep the chosen tender/contractor filters so the saved route remains
        // visible in the saved-record list; only the entry fields are reset.
        UpdateRouteListView();
        UpdateRouteSummary();
        SetRouteEditorMode(false);
    }

    private void UpdateRouteSummary()
    {
        RefreshRoutePanelList();
    }

}
