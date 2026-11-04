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
    private bool IsArchiveCorrectionFor(ArchiveCorrectionSection section) =>
        IsArchiveCorrection && _archiveCorrectionSection == section;

    private void ResetEditorUiToCreateMode()
    {
        SaveContractButton.Content = "İHALEYİ KAYDET";
        CancelContractEditButton.Visibility = Visibility.Collapsed;
        ClearContractButton.IsEnabled = true;
        ClearContractButton.Visibility = Visibility.Visible;
        ContractEditorTitleTextBlock.Text = "İhale bilgileri";
        ContractEditorSubtitleTextBlock.Text = "Programı seçin, İKN ve hizmet tarihlerini girin.";

        SetContractorEditorMode(false);

        AddRouteButton.Content = "HAT EKLE";
        CancelRouteEditButton.Visibility = Visibility.Collapsed;
        ClearRouteButton.IsEnabled = true;
        ClearRouteButton.Visibility = Visibility.Visible;
        RouteEditorTitleTextBlock.Text = "YENİ HAT";
        RouteEditorSubtitleTextBlock.Text = "Hat bilgilerini girin";
        RouteSelectionCard.Visibility = Visibility.Visible;
        RouteNumberTextBox.IsReadOnly = false;

        CalculateProgressButton.Content = "HAKEDİŞ HESAPLA/KAYDET";
        CancelProgressEditButton.Visibility = Visibility.Collapsed;
        ProgressEditorTitleTextBlock.Text = "DÖNEM HESAP BİLGİLERİ";
        ProgressTenderListBox.IsEnabled = true;
        ProgressRouteComboBox.IsEnabled = true;
    }

    private void SetContractEditorMode(bool editing)
    {
        SaveContractButton.Content = editing ? "DEĞİŞİKLİKLERİ KAYDET" : "İHALEYİ KAYDET";
        CancelContractEditButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        ClearContractButton.IsEnabled = !editing;
        ClearContractButton.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        ContractEditorTitleTextBlock.Text = editing ? "İhale kaydını düzenle" : "İhale bilgileri";
        ContractEditorSubtitleTextBlock.Text = editing
            ? "Mevcut kaydı düzenliyorsunuz. Vazgeçerseniz DÜZENLEME İPTAL düğmesini kullanın."
            : "Programı seçin, İKN ve hizmet tarihlerini girin.";
    }

    private void SetContractorEditorMode(bool editing)
    {
        SaveContractorButton.Content = editing ? "DEĞİŞİKLİKLERİ KAYDET" : "FİRMAYI KAYDET";
        CancelContractorEditButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        ClearContractorButton.IsEnabled = !editing;
        ClearContractorButton.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        ContractorEditorTitleTextBlock.Text = editing ? "Firma kaydını düzenle" : "Firma bilgileri";
        ContractorProgramComboBox.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        ContractorEditingProgramTextBlock.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        var program = IsArchiveCorrectionFor(ArchiveCorrectionSection.Contractor)
            ? _editingArchiveSnapshot!.Contract.ProgramType
            : FindContractorByKey(_editingContractorKey)?.ProgramType;
        ContractorEditingProgramTextBlock.Text = program == TransportationProgramType.BasicOrSecondary
            ? "Temel Eğitim/Ortaöğretim Taşıma" : program.HasValue ? "Özel Eğitim Taşıma" : string.Empty;
    }

    private void SetRouteEditorMode(bool editing, bool archiveCorrection = false)
    {
        AddRouteButton.Content = editing ? "DEĞİŞİKLİKLERİ KAYDET" : "HAT KAYDET";
        CancelRouteEditButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        ClearRouteButton.IsEnabled = !editing;
        ClearRouteButton.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        RouteEditorTitleTextBlock.Text = editing
            ? (archiveCorrection ? "ARŞİVDEKİ HAT DÜZENLE" : "HAT DÜZENLE")
            : "YENİ HAT";
        RouteEditorSubtitleTextBlock.Text = editing
            ? (archiveCorrection
                ? "Bu alan yalnızca seçtiğiniz arşiv kaydını günceller; yeni hat oluşturulmaz."
                : "Mevcut hattı düzenliyorsunuz. Vazgeçerseniz DÜZENLEME İPTAL düğmesini kullanın.")
            : "Hat bilgilerini girin";
        RouteSelectionCard.Visibility = archiveCorrection ? Visibility.Collapsed : Visibility.Visible;
        RouteNumberTextBox.IsReadOnly = false;
    }

    private void SetProgressEditorMode(bool editing, bool archiveCorrection = false)
    {
        RefreshPeriodInputReadOnly();
        CalculateProgressButton.Content = editing ? "DÜZENLE" : "HAKEDİŞ HESAPLA/KAYDET";
        SinglePaymentFieldsGrid.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        CancelProgressEditButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        if (editing)
        {
            ArchiveCurrentProgressButton.Visibility = Visibility.Collapsed;
            CorrectCurrentProgressButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            var hasCalculatedProgress = GetLastCalculatedProgress() is not null;
            ArchiveCurrentProgressButton.Visibility =
                hasCalculatedProgress ? Visibility.Visible : Visibility.Collapsed;
            CorrectCurrentProgressButton.Visibility =
                hasCalculatedProgress ? Visibility.Visible : Visibility.Collapsed;
        }

        ProgressEditorTitleTextBlock.Text = editing
            ? (archiveCorrection ? "ARŞİV HAKEDİŞ DÜZENLE" : "HAKEDİŞ DÜZENLE")
            : "HAT BAZINDA HAKEDİŞ GİRDİLERİ";
        ProgressTenderListBox.IsEnabled = !(editing && archiveCorrection);
        ProgressRouteComboBox.IsEnabled = !(editing && archiveCorrection);
        if (!editing && ProgressRouteComboBox.SelectedItems.Count > 0) UpdateMultiRouteDraftRows();
    }

    private void ClearArchiveCorrectionState()
    {
        if (_archiveCorrectionSection == ArchiveCorrectionSection.ProgressPayment)
            RestoreLiveProgressEditorSelectorContext();

        _editingArchiveSourcePath = null;
        _editingArchiveSnapshot = null;
        _archiveCorrectionSection = ArchiveCorrectionSection.None;
        _suppressArchiveDirtyTracking = false;
        ClearLiveHistoryCorrectionContext();
        ResetEditorUiToCreateMode();
    }

    private void LoadArchiveProgressEditorContext(ArchiveProgressEditorContext context)
    {
        var snapshotContractor = new ContractorDraft(
            context.Contractor.Name,
            OehsIdentityService.NormalizeIdentity(context.Contractor.TaxOrIdentityNumber),
            context.Contractor.Address,
            context.Contractor.Phone,
            context.Contractor.Email,
            context.Contractor.BankAccountNumber,
            context.Contractor.ContactPerson,
            context.Contractor.ProgramType,
            context.Contractor.VatExempt ?? false);
        var snapshotRoute = new RouteDraft(
            context.Route.RouteNumber,
            context.Route.Name,
            context.Route.DailyPrice,
            context.Route.GuideDailyPrice,
            context.Route.TransportWorkDays,
            context.Route.TenderNumber,
            OehsIdentityService.NormalizeIdentity(context.Route.ContractorTaxOrIdentityNumber),
            context.Contractor.Name,
            context.Route.ContractNumber,
            context.Contract.ProgramType,
            context.Route.ContractAmount ?? _editingArchiveSnapshot?.ProgressPayment.Input.ContractAmount,
            context.Route.ContractDate ?? _editingArchiveSnapshot?.ProgressPayment.ContractDate);

        _suppressSelectionEvents = true;
        try
        {
            _multiRoutePaymentRows.Clear();
            _multiRouteDraftCache.Clear();
            ProgressTenderListBox.ItemsSource = new[] { context.Contract };
            ProgressTenderListBox.SelectedItem = context.Contract;
            ProgressContractorComboBox.ItemsSource = new[] { snapshotContractor };
            ProgressContractorComboBox.SelectedItem = snapshotContractor;
            ProgressRouteComboBox.ItemsSource = new[] { snapshotRoute };
            ProgressRouteComboBox.SelectedItems.Clear();
            ProgressRouteComboBox.SelectedItem = snapshotRoute;
            UpdateProgressProgramTypeFields(context.Contract);
            UpdateProgressPeriodOptions(context.Contract, preserveSelection: false);
            SelectProgressPeriod(context.PeriodDate);
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateMultiRouteDraftRows();
    }

    private void RestoreLiveProgressEditorSelectorContext()
    {
        _suppressSelectionEvents = true;
        try
        {
            ProgressTenderListBox.ItemsSource = _contracts;
            ProgressTenderListBox.SelectedItem = null;
            ProgressContractorComboBox.ItemsSource = Array.Empty<ContractorDraft>();
            ProgressContractorComboBox.SelectedItem = null;
            ProgressPeriodMonthComboBox.ItemsSource = Array.Empty<ProgressPaymentMonthOption>();
            ProgressPeriodMonthComboBox.SelectedItem = null;
            ProgressRouteComboBox.ItemsSource = Array.Empty<RouteDraft>();
            ProgressRouteComboBox.SelectedItems.Clear();
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        _multiRoutePaymentRows.Clear();
        _multiRouteDraftCache.Clear();
        _lastProgressDefaultsRouteKey = null;
        _lastSelectedPaymentMonthKey = null;
    }

}
