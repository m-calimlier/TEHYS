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
    private ProgressPaymentHistoryData? GetLastCalculatedProgress()
    {
        if (_lastCalculatedHistoryId is Guid id)
            return _progressHistory.FirstOrDefault(x => x.Id == id);

        return GetSelectedProgressLatestPayment();
    }

    private void ArchiveCurrentProgressButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment) &&
            _editingArchiveSnapshot is not null &&
            !string.IsNullOrWhiteSpace(_editingArchiveSourcePath))
        {
            SaveArchiveCorrectionSnapshot(_editingArchiveSnapshot, "Arşiv");
            return;
        }

        var item = GetLastCalculatedProgress();
        if (item is null)
        {
            NightMessageBox.Show("Arşivlenecek tamamlanmış bir hakediş bulunamadı. Önce HAKEDİŞ HESAPLA/KAYDET düğmesine basın.", "TEHYS",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Multi-route calculations share an exact CreatedAt, contract, period and
        // payment number. Archive every route snapshot in one click, independently.
        var batch = _progressHistory
            .Where(x => item.BatchId.HasValue
                ? x.BatchId == item.BatchId
                : x.CreatedAt == item.CreatedAt &&
                  string.Equals(x.TenderNumber, item.TenderNumber, StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(x.ContractNumber, item.ContractNumber, StringComparison.OrdinalIgnoreCase) &&
                  x.ProgressPaymentNumber == item.ProgressPaymentNumber &&
                  x.PeriodDate == item.PeriodDate &&
                  x.Input.ProgramType == item.Input.ProgramType)
            .OrderBy(x => x.RouteNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (batch.Length == 0)
            batch = new[] { item };

        var archiveResult = ProgressPaymentArchiveBatchService.Execute(
            batch,
            payment =>
            {
                if (TryArchiveProgressItem(payment, out var path, out _))
                    return (Success: true, Path: (string?)path, Error: (string?)null);

                return (Success: false, Path: (string?)null,
                    Error: $"Hat {payment.RouteNumber}: arşiv kaydı oluşturulamadı.");
            },
            path => File.Delete(path));

        if (!archiveResult.Success)
        {
            ArchiveStatusTextBlock.Text = archiveResult.RollbackFailures.Count == 0
                ? "ARŞİV İŞLEMİ GERİ ALINDI"
                : "KISMİ ARŞİV HATASI";

            if (archiveResult.RollbackFailures.Count > 0)
            {
                var rollbackDetails = string.Join(Environment.NewLine, archiveResult.RollbackFailures);
                var rollbackMessage = "Çoklu hat arşivleme tamamlanamadı. Bazı yeni dosyalar geri alınamadı; lütfen arşiv klasörünü kontrol edin."
                    + Environment.NewLine + rollbackDetails;
                CalculationStatusTextBlock.Text = rollbackMessage;
                NightMessageBox.Show(rollbackMessage, "OEHS — Arşiv Geri Alma Uyarısı",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                CalculationStatusTextBlock.Text =
                    "Çoklu hat arşivleme tamamlanamadı. Bu işlemde oluşturulan önceki arşiv dosyaları geri alındı.";
            }

            UpdateArchiveView();
            return;
        }

        var archivedPaths = archiveResult.WrittenPaths.ToList();

        _lastCalculatedHistoryId = null;
        UpdateArchiveView();
        NavigateToArchives();

        var routeSummary = string.Join(", ", batch.Select(x => x.RouteNumber).Where(x => !string.IsNullOrWhiteSpace(x)));
        var batchTitle = item.BatchDisplayName ?? $"Hakediş No {item.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? "-"}";
        var message = batch.Length == 1
            ? $"{batchTitle} arşive eklendi.\n\n{Path.GetFileName(archivedPaths[0])}"
            : $"{batch.Length} hatlık aylık kayıt arşivlendi.\n{batchTitle}\nHatlar: {routeSummary}\n\nHat ayrıntıları arşivde korunmuştur. Resmî çıktılar seçili hatların toplamıyla tek belge takımı olarak hazırlanır.";
        ArchiveStatusTextBlock.Text = "ARŞİV OLUŞTURULDU";
        ClearProgressEntryFields();
        SetProgressEditorMode(false);
        CalculationStatusTextBlock.Text = message;
        NightMessageBox.Show(message, "OEHS — Arşiv Bilgisi", MessageBoxButton.OK, MessageBoxImage.Information);
        _editingArchiveSourcePath = null;
    }

    private bool TryArchiveProgressItem(
        ProgressPaymentHistoryData item,
        out string archivePath,
        out int revision)
    {
        archivePath = string.Empty;
        revision = 0;

        var contract = FindContract(item.TenderNumber, item.ContractNumber, item.Input.ProgramType);
        var route = _routes.FirstOrDefault(x =>
            string.Equals(
                OehsIdentityService.BuildRouteKey(
                    x.TenderNumber, x.RouteNumber, x.ContractorTaxOrIdentityNumber, x.ContractNumber, x.ProgramType),
                OehsIdentityService.BuildRouteKey(
                    item.TenderNumber ?? string.Empty, item.RouteNumber ?? string.Empty,
                    item.ContractorTaxOrIdentityNumber ?? string.Empty, item.ContractNumber ?? string.Empty,
                    item.Input.ProgramType),
                StringComparison.Ordinal));

        var contractor = route is null
            ? FindContractor(item.ContractorTaxOrIdentityNumber ?? string.Empty, item.Input.ProgramType)
            : FindContractor(route.ContractorTaxOrIdentityNumber, route.ProgramType);
        if (contract is null || route is null || contractor is null)
        {
            NightMessageBox.Show($"Hat {item.RouteNumber}: arşiv için ihale, hat ve yüklenici snapshot'ı çözümlenemedi. Kaydı kontrol edin.",
                "TEHYS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var routeData = new RouteData(
            route.RouteNumber, route.Name, route.DailyPrice, route.GuideDailyPrice, route.TransportWorkDays, route.TenderNumber,
            route.ContractorTaxOrIdentityNumber, route.ContractNumber, route.ProgramType, route.ContractAmount, route.ContractDate);

        var prior = _progressHistory
            .Where(x => x.Input.ProgramType == item.Input.ProgramType)
            .Where(x => string.Equals(
                OehsIdentityService.BuildRouteKey(
                    x.TenderNumber ?? string.Empty, x.RouteNumber ?? string.Empty,
                    x.ContractorTaxOrIdentityNumber ?? string.Empty, x.ContractNumber ?? string.Empty,
                    x.Input.ProgramType),
                OehsIdentityService.BuildRouteKey(
                    route.TenderNumber, route.RouteNumber, route.ContractorTaxOrIdentityNumber,
                    route.ContractNumber, route.ProgramType),
                StringComparison.Ordinal))
            .Where(x => x.Id != item.Id)
            .Where(x => x.PeriodDate.HasValue && (!item.PeriodDate.HasValue || x.PeriodDate.Value < item.PeriodDate.Value))
            .OrderBy(x => x.PeriodDate)
            .ThenBy(x => x.CreatedAt)
            .ToArray();

        var archive = new ProgressPaymentArchive(
            "1.0", 1, DateTime.UtcNow, "ActiveRecord", contract,
            new Contractor(
                contractor.Name,
                OehsIdentityService.NormalizeIdentity(contractor.TaxOrIdentityNumber),
                contractor.Address, contractor.Phone, contractor.Email,
                contractor.BankAccountNumber, contractor.ContactPerson, contractor.ProgramType, contractor.VatExempt),
            routeData, item, prior,
            OehsFileNameService.BuildProgressPdfFileName(
                item.TenderNumber ?? string.Empty, item.ContractNumber ?? string.Empty,
                route.RouteNumber, item.ProgressPaymentNumber ?? 1,
                item.PeriodDate ?? DateOnly.FromDateTime(DateTime.Today)),
            ProgressPaymentReportService.RequiredDocumentNames);

        try
        {
            archivePath = _archiveStore.Save(archive);
            revision = ExtractArchiveRevision(archivePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProgressPaymentArchiveException)
        {
            ArchiveStatusTextBlock.Text = "ARŞİV HATASI";
            CalculationStatusTextBlock.Text = $"Hat {item.RouteNumber}: {ex.Message}";
            NightMessageBox.Show(CalculationStatusTextBlock.Text, "OEHS — Arşiv Hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void CorrectCurrentProgressButton_Click(object sender, RoutedEventArgs e)
    {
        var item = GetLastCalculatedProgress();
        if (item is null)
        {
            NightMessageBox.Show("Düzeltilecek hakediş bulunamadı.", "TEHYS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BeginHistoryOrArchiveCorrection(item);
    }

    private void EditProgressWithTarget(ProgressPaymentHistoryData item)
    {
        var target = AskHistoryEditTarget(item);
        if (!target.HasValue)
        {
            ClearArchiveCorrectionState();
            return;
        }

        var selection = ProgressCorrectionTargetService.Select(target.Value, item);
        switch (selection.Target)
        {
            case ProgressCorrectionTarget.Tender:
                BeginContractHistoryEdit(item);
                break;
            case ProgressCorrectionTarget.Contractor:
                BeginContractorHistoryEdit(item);
                break;
            case ProgressCorrectionTarget.Route:
                BeginRouteHistoryEdit(item);
                break;
            case ProgressCorrectionTarget.ProgressPayment:
                BeginProgressHistoryEdit(item);
                break;
            case ProgressCorrectionTarget.Cancel:
                ClearArchiveCorrectionState();
                break;
        }
    }

    private void UpdateArchiveView()
    {
        var selectedPath = (ArchiveItemsControl.SelectedItem as ArchiveRow)?.FullPath;
        _archiveRows.Clear();

        foreach (var row in GroupArchiveRows(FilterArchives()))
        {
            _archiveRows.Add(new ArchiveRow(
                row.FullPath,
                row.DisplayTitle,
                row.DetailText,
                row.NetPayableText,
                row.RevisionText));
        }

        ArchiveCountTextBlock.Text = _archiveRows.Count.ToString(CultureInfo.CurrentCulture);

        ArchiveItemsControl.SelectedItem = _archiveRows.FirstOrDefault(row => row.FullPath == selectedPath);
        ArchiveStatusTextBlock.Text = "İşlem yapmak için bir arşiv satırı seçin. Önceki revizyonlar korunur.";
        ArchiveEmptyStateBorder.Visibility = _archiveRows.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void LoadArchiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ArchiveRow row })
            return;

        var chosen=ChooseArchiveMember(row);
        if(chosen is null)return;
        row=chosen;

        if (sender is Button loadButton &&
            string.Equals(loadButton.Content?.ToString(), "ÇAĞIR", StringComparison.OrdinalIgnoreCase))
        {
            ClearArchiveCorrectionState();
        }

        ProgressPaymentArchive archive;
        try
        {
            archive = ProgressPaymentArchiveStore.Load(row.FullPath);
        }
        catch (ProgressPaymentArchiveException ex)
        {
            NightMessageBox.Show(ex.Message, "TEHYS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var contractsBeforeRestore = _contracts.ToArray();
            var contractorsBeforeRestore = _contractors.ToArray();
            var routesBeforeRestore = _routes.ToArray();
            var historyBeforeRestore = _progressHistory.ToArray();

            var currentData = new OehsApplicationData(
                _contracts.FirstOrDefault(),
                _routes.Select(x => new RouteData(
                    x.RouteNumber,
                    x.Name,
                    x.DailyPrice,
                    x.GuideDailyPrice,
                    x.TransportWorkDays,
                    x.TenderNumber,
                    x.ContractorTaxOrIdentityNumber,
                    x.ContractNumber, x.ProgramType, x.ContractAmount, x.ContractDate)).ToArray(),
                _contractors.Select(x => new Contractor(
                    x.Name,
                    OehsIdentityService.NormalizeIdentity(x.TaxOrIdentityNumber),
                    x.Address,
                    x.Phone,
                    x.Email,
                    x.BankAccountNumber,
                    x.ContactPerson,
                    x.ProgramType,
                    x.VatExempt)).ToArray(),
                _progressHistory.ToArray(),
                _contracts.ToArray());

            var restored = ProgressPaymentArchiveRestoreService.Restore(currentData, archive);

            _contracts.Clear();
            foreach (var contract in restored.Data.Contracts ?? Array.Empty<ContractDraftData>())
                _contracts.Add(contract);

            _contractors.Clear();
            foreach (var contractor in restored.Data.Contractors ?? Array.Empty<Contractor>())
                _contractors.Add(new ContractorDraft(
                    contractor.Name,
                    OehsIdentityService.NormalizeIdentity(contractor.TaxOrIdentityNumber),
                    contractor.Address,
                    contractor.Phone,
                    contractor.Email,
                    contractor.BankAccountNumber,
                    contractor.ContactPerson,
                    contractor.ProgramType,
                    contractor.VatExempt ?? HasLegacyContractVatExemption(contractor.TaxOrIdentityNumber, contractor.ProgramType)));

            _routes.Clear();
            foreach (var route in restored.Data.Routes ?? Array.Empty<RouteData>())
            {
                var contractor = FindContractor(route.ContractorTaxOrIdentityNumber, route.ProgramType);
                _routes.Add(new RouteDraft(
                    route.RouteNumber,
                    route.Name,
                    route.DailyPrice,
                    route.GuideDailyPrice,
                    route.TransportWorkDays,
                    route.TenderNumber,
                    OehsIdentityService.NormalizeIdentity(route.ContractorTaxOrIdentityNumber),
                    contractor?.Name ?? archive.Contractor.Name,
                    route.ContractNumber,
                    route.ProgramType,
                    route.ContractAmount ?? FindContract(route.TenderNumber, route.ContractNumber, route.ProgramType)?.ContractAmount,
                    route.ContractDate ?? FindContract(route.TenderNumber, route.ContractNumber, route.ProgramType)?.ContractDate));
            }

            _progressHistory.Clear();
            foreach (var item in restored.Data.ProgressPayments ?? Array.Empty<ProgressPaymentHistoryData>())
                _progressHistory.Add(item);

            if (!SavePersistedData())
            {
                RestoreLiveDataCollections(
                    contractsBeforeRestore,
                    contractorsBeforeRestore,
                    routesBeforeRestore,
                    historyBeforeRestore);
                RefreshViewsAfterPersistenceRollback();
                NightMessageBox.Show(
                    "Arşiv dosyası okundu ancak aktif OEHS verisi diske kaydedilemedi. Birleştirme geri alındı; mevcut aktif kayıtlar korundu.",
                    "OEHS — Arşiv Geri Yükleme Tamamlanamadı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Refresh master lists before selecting the editor context. Refreshing
            // afterwards clears its contractor, route and period selections.
            UpdateContractSelectors();
            UpdateHistoryView();
            UpdateArchiveView();
            UpdateHomeSummaryView();
            UpdateAcceptanceView();
            BeginProgressHistoryEdit(archive.ProgressPayment);
            _lastCalculatedHistoryId = archive.ProgressPayment.Id;

            CalculationStatusTextBlock.Text =
                restored.AddedAnything
                    ? $"Arşiv çağrıldı ve aktif veriyle birleştirildi. {restored.Message} Kaydetmeden önce ön kontrol yapılacaktır."
                    : $"Arşiv çağrıldı. {restored.Message} Kaydetmeden önce ön kontrol yapılacaktır.";
        }
        catch (Exception ex) when (ex is ProgressPaymentArchiveException or IOException or UnauthorizedAccessException)
        {
            NightMessageBox.Show($"Arşiv geri yüklenemedi.\n\n{ex.Message}", "TEHYS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void EditArchiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ArchiveRow row } && ChooseArchiveMember(row) is {} chosen)
            BeginCorrectionFromArchive(chosen);
    }

    private void BeginCorrectionFromArchive(ArchiveRow row)
    {
        try
        {
            var archive = ProgressPaymentArchiveStore.Load(row.FullPath);
            ClearLiveHistoryCorrectionContext();
            _editingArchiveSourcePath = row.FullPath;
            _periodEditingId = null; _periodEditingInput = null;
            _editingArchiveSnapshot = archive;
            _archiveCorrectionSection = ArchiveCorrectionSection.None;

            EditProgressWithTarget(archive.ProgressPayment);
        }
        catch (ProgressPaymentArchiveException ex)
        {
            ClearArchiveCorrectionState();
            NightMessageBox.Show(ex.Message, "TEHYS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenArchiveFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ArchiveRow row })
            return;

        var folder = Path.GetDirectoryName(row.FullPath);
        OpenRecordsFolder(folder, archive: true);
    }

    private void UpdateHistoryView()
    {
        var selected = HistoryItemsControl.SelectedItem as HistoryRow;
        _historyRows.Clear();

        var orderedHistory = FilterHistory();

        foreach (var group in orderedHistory.GroupBy(ProgressPaymentBatchService.GroupKey))
        {
            var members=group.OrderBy(x=>x.RouteNumber,StringComparer.OrdinalIgnoreCase).ToArray();
            var item = members[0];
            var recordedAt = item.CreatedAt.Kind == DateTimeKind.Utc ? item.CreatedAt.ToLocalTime() : item.CreatedAt;
            var batchTitle = item.BatchDisplayName;
            var displayNumber = !string.IsNullOrWhiteSpace(batchTitle)
                ? $"{batchTitle} • Hat {item.RouteNumber ?? "-"} • Hakediş No {item.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? "-"}"
                : item.ProgressPaymentNumber.HasValue
                    ? $"Hakediş No {item.ProgressPaymentNumber.Value} • İhale/Dosya: {item.TenderNumber ?? "-"} • Hat: {item.RouteNumber ?? "-"}"
                    : $"Hesaplama • İhale/Dosya: {item.TenderNumber ?? "-"} • Hat: {item.RouteNumber ?? "-"}";
            if(members.Length>1)displayNumber=GroupRecordTitle(members);
            var periodText = item.PeriodDate.HasValue
                ? $"Dönem: {CultureInfo.GetCultureInfo("tr-TR").TextInfo.ToTitleCase(item.PeriodDate.Value.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR")))} • Yüklenici: {item.ContractorName ?? "-"} • Kayıt: {recordedAt:dd.MM.yyyy HH:mm}"
                : $"Yüklenici: {item.ContractorName ?? "-"} • Kayıt: {recordedAt:dd.MM.yyyy HH:mm}";

            _historyRows.Add(new HistoryRow(
                item.Id,
                displayNumber,
                periodText,
                FormatMoney(members.Sum(p=>p.Calculation.TotalAmount)),
                FormatMoney(members.Sum(p=>p.Calculation.NetPayable)), item.Input.ProgramType));
        }

        HistoryCountTextBlock.Text = _historyRows.Count.ToString(CultureInfo.CurrentCulture);
        HistoryNetTotalTextBlock.Text = FormatMoney(orderedHistory.Sum(x => x.Calculation.NetPayable));
        HistoryItemsControl.SelectedItem = _historyRows.FirstOrDefault(row => selected is not null && row.Id == selected.Id && row.ProgramType == selected.ProgramType);
        HistoryOperationStatusTextBlock.Text = string.Empty;
        HistoryOperationStatusTextBlock.Visibility = Visibility.Collapsed;
        HistoryEmptyStateBorder.Visibility = orderedHistory.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private ProgressPaymentHistoryData? GetHistoryItemFromRow(object? tag)
    {
        return tag is HistoryRow row
            ? _progressHistory.FirstOrDefault(x => x.Id == row.Id && x.Input.ProgramType == row.ProgramType)
            : null;
    }

    private static string GetHistoryArchiveFolder(ProgressPaymentHistoryData item) =>
        OutputDirectoryForPayment(item);

    private void OpenHistoryFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var item = GetHistoryItemFromRow((sender as Button)?.Tag);
        if (item is null)
            return;

        OpenRecordsFolder(GetHistoryArchiveFolder(item), archive: false);
    }

    private void EditHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        ClearArchiveCorrectionState();
        if (sender is Button { Tag: HistoryRow row })
        {
            var item = ChooseHistoryMember(row);
            if (item is not null)
                BeginHistoryOrArchiveCorrection(item);
        }
    }

    private void BeginHistoryOrArchiveCorrection(ProgressPaymentHistoryData item)
    {
        _historyOperationProgram = item.Input.ProgramType;
        ClearLiveHistoryCorrectionContext();
        ArchiveRow? matchingArchiveRow = null;
        var matchingRevision = 0;

        try
        {
            foreach (var file in _archiveStore.List())
            {
                ProgressPaymentArchive archive;
                try
                {
                    archive = ProgressPaymentArchiveStore.Load(file.FullName);
                }
                catch (ProgressPaymentArchiveException)
                {
                    // Invalid archives remain visible in the archive panel, but cannot be edited as a snapshot.
                    continue;
                }

                if (archive.ProgressPayment.Id != item.Id || archive.ProgressPayment.Input.ProgramType != item.Input.ProgramType || archive.Revision <= matchingRevision)
                    continue;

                matchingRevision = archive.Revision;
                var payment = archive.ProgressPayment;
                matchingArchiveRow = new ArchiveRow(
                    file.FullName,
                    $"İhale/Dosya: {payment.TenderNumber ?? "-"} • Hat {payment.RouteNumber ?? "-"} • Hakediş {payment.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? "-"}",
                    $"Dönem: {payment.PeriodDate?.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture) ?? "-"} • Yüklenici: {archive.Contractor.Name}",
                    FormatMoney(payment.Calculation.NetPayable),
                    $"v{archive.Revision:00}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProgressPaymentArchiveException)
        {
            NightMessageBox.Show(
                $"Hakedişin arşiv kaydı aranamadı; herhangi bir kayıt değiştirilmedi.\n\n{ex.Message}",
                "OEHS — Arşiv Arama Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (matchingArchiveRow is not null)
        {
            BeginCorrectionFromArchive(matchingArchiveRow);
            return;
        }

        // If this history row has never been archived, keep the regular live-record editing flow.
        EditProgressWithTarget(item);
    }

    private ProgressCorrectionTarget? AskHistoryEditTarget(ProgressPaymentHistoryData item)
    {
        ProgressCorrectionTarget? selected = null;
        var dialog = new Window
        {
            Title = !string.IsNullOrWhiteSpace(_editingArchiveSourcePath) ? "Arşiv Kaydı Düzelt" : "Hakediş Düzelt",
            Owner = this,
            Width = 500,
            Height = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = NightTheme.Surface
        };

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = !string.IsNullOrWhiteSpace(_editingArchiveSourcePath)
                ? "Arşiv kaydında hangi bölümü düzeltmek istiyorsunuz?"
                : "Hangi bölümde düzeltme yapmak istiyorsunuz?",
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = NightTheme.Text,
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"İhale/Dosya: {item.TenderNumber ?? "-"} • Hat: {item.RouteNumber ?? "-"} • Hakediş: {item.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? "-"}",
            FontSize = 12.5,
            Foreground = NightTheme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        foreach (var option in new[]
        {
            ("İHALE BİLGİLERİ", ProgressCorrectionTarget.Tender),
            ("YÜKLENİCİ FİRMA", ProgressCorrectionTarget.Contractor),
            ("HAT BİLGİLERİ", ProgressCorrectionTarget.Route),
            ("HAKEDİŞ GİRDİLERİ", ProgressCorrectionTarget.ProgressPayment),
            ("VAZGEÇ", ProgressCorrectionTarget.Cancel)
        })
        {
            var button = new Button
            {
                Content = option.Item1,
                Width = 420,
                Height = 42,
                Margin = new Thickness(0, 4, 0, 4),
                Style = (Style)FindResource(option.Item2 == ProgressCorrectionTarget.Cancel ? "SecondaryButton" : "ModernButton")
            };
            button.Click += (_, _) =>
            {
                selected = option.Item2;
                dialog.DialogResult = true;
                dialog.Close();
            };
            panel.Children.Add(button);
        }

        dialog.Content = panel;
        dialog.ShowDialog();
        return selected;
    }

    private void BeginContractHistoryEdit(ProgressPaymentHistoryData item)
    {
        if (IsArchiveEditing)
        {
            _suppressArchiveDirtyTracking = true;
            _archiveCorrectionSection = ArchiveCorrectionSection.Contract;
            var archivedContract = _editingArchiveSnapshot!.Contract;
            _editingContractTender = null;
            ApplyContractToForm(archivedContract);
            NavigationButton_Click(NavContract, new RoutedEventArgs(Button.ClickEvent, NavContract));
            // Panel geçişinden sonra editör modunu son kez uygula.
            SetContractEditorMode(true);
            ContractStatusTextBlock.Text =
                "ARŞİV DÜZELTME MODU: İhale bilgilerini güncelleyin. DÜZENLE mevcut arşiv revision'ını aynı kayıt üzerinde güncelleyecektir.";
            _suppressArchiveDirtyTracking = false;
            return;
        }

        var contract = FindContract(item.TenderNumber, item.ContractNumber, item.Input.ProgramType);
        if (contract is null)
        {
            ShowAlreadyRegistered("Bu hakedişin bağlı olduğu ihale kaydı bulunamadı.");
            return;
        }

        _editingContractTender = contract.TenderNumber;
        ApplyContractToForm(contract);
        SetContractEditorMode(true);
        NavigationButton_Click(NavContract, new RoutedEventArgs(Button.ClickEvent, NavContract));
        ContractStatusTextBlock.Text = "Hakedişten gelen düzenleme: yalnızca İhale Bilgileri alanlarını düzeltin ve DÜZENLE'e basın.";
        SetLiveHistoryCorrectionContext(item.Id, HistoryCorrectionSection.Contract);
    }

    private void BeginContractorHistoryEdit(ProgressPaymentHistoryData item)
    {
        if (IsArchiveEditing)
        {
            _suppressArchiveDirtyTracking = true;
            _archiveCorrectionSection = ArchiveCorrectionSection.Contractor;
            var archivedContractor = _editingArchiveSnapshot!.Contractor;
            _editingContractorKey = null;
            ContractorNameTextBox.Text = archivedContractor.Name;
            ContractorTaxTextBox.Text = archivedContractor.TaxOrIdentityNumber;
            ContractorBankTextBox.Text = archivedContractor.BankAccountNumber;
            ContractorVatExemptCheckBox.IsChecked = archivedContractor.VatExempt ?? _editingArchiveSnapshot.ProgressPayment.Input.IsVatExempt;
            ClearContractorValidation();
            NavigationButton_Click(NavContractors, new RoutedEventArgs(Button.ClickEvent, NavContractors));
            // Panel geçişinden sonra editör modunu son kez uygula.
            SetContractorEditorMode(true);
            ContractorStatusTextBlock.Text =
                "ARŞİV DÜZELTME MODU: firma bilgilerini güncelleyin. Değişiklikleri kaydettiğinizde bu arşivin yeni revizyonu oluşturulur.";
            _suppressArchiveDirtyTracking = false;
            return;
        }

        var contractor = FindContractor(item.ContractorTaxOrIdentityNumber ?? string.Empty, item.Input.ProgramType);

        if (contractor is null)
        {
            ShowAlreadyRegistered("Bu hakedişin bağlı olduğu yüklenici firma bulunamadı.");
            return;
        }

        _editingContractorKey = BuildContractorKey(contractor);
        SetContractorEditorMode(true);
        ContractorNameTextBox.Text = contractor.Name;
        ContractorTaxTextBox.Text = contractor.TaxOrIdentityNumber;
        ContractorBankTextBox.Text = contractor.BankAccountNumber;
        ContractorVatExemptCheckBox.IsChecked = contractor.VatExempt;
        ClearContractorValidation();
        NavigationButton_Click(NavContractors, new RoutedEventArgs(Button.ClickEvent, NavContractors));
        ContractorStatusTextBlock.Text = "Hakedişten gelen düzenleme: Firma bilgilerini değiştirip DEĞİŞİKLİKLERİ KAYDET düğmesini kullanın.";
        SetLiveHistoryCorrectionContext(item.Id, HistoryCorrectionSection.Contractor);
    }

    private void BeginRouteHistoryEdit(ProgressPaymentHistoryData item)
    {
        ResetRouteDateValidation();
        if (IsArchiveEditing)
        {
            _suppressArchiveDirtyTracking = true;
            _archiveCorrectionSection = ArchiveCorrectionSection.Route;
            var archive = _editingArchiveSnapshot!;
            RouteTenderListBox.SelectedItem = null;
            RouteContractorListBox.ItemsSource = Array.Empty<ContractorDraft>();
            RouteContractorListBox.SelectedItem = null;

            RouteNumberTextBox.Text = archive.Route.RouteNumber;
            RouteNameTextBox.Text = archive.Route.Name;
            RouteDailyPriceTextBox.Text = FormatCalculationInput(archive.Route.DailyPrice);
            RouteContractAmountTextBox.Text = FormatCalculationInput(archive.Route.ContractAmount ?? archive.ProgressPayment.Input.ContractAmount);
            RouteContractDatePicker.SelectedDate = (archive.Route.ContractDate ?? archive.ProgressPayment.ContractDate ?? archive.Contract.ContractDate)?.ToDateTime(TimeOnly.MinValue);
            RouteGuideDailyPriceTextBox.Text = FormatCalculationInput(archive.Route.GuideDailyPrice);
            ApplyRouteProgramFields(archive.Contract.ProgramType);
            RouteNumberTextBox.IsReadOnly = false;
            RouteSelectedTenderTextBlock.Text = $"Arşiv ihale: {archive.Route.TenderNumber}";
            RouteSelectedContractorTextBlock.Text = $"Arşiv yüklenici: {archive.Contractor.Name}";

            _editingRoute = null;
            NavigationButton_Click(NavLines, new RoutedEventArgs(Button.ClickEvent, NavLines));
            // Panel açıldıktan sonra editör modunu son kez uygula. Böylece panel geçişi
            // veya seçim olayları DÜZENLE / DÜZENLEME İPTAL durumunu bozamaz.
            SetRouteEditorMode(true, archiveCorrection: true);
            RouteStatusTextBlock.Text =
                "ARŞİV DÜZELTME MODU: Hat No dahil ilgili alanları değiştirebilirsiniz. Değişiklikleri kaydettiğinizde yeni arşiv revizyonu oluşturulur; yeni hat oluşturulmaz.";
            _suppressArchiveDirtyTracking = false;
            return;
        }

        var historyRouteKey = OehsIdentityService.BuildRouteKey(
            item.TenderNumber ?? string.Empty,
            item.RouteNumber ?? string.Empty,
            item.ContractorTaxOrIdentityNumber ?? string.Empty,
            item.ContractNumber ?? string.Empty,
            item.Input.ProgramType);

        var route = _routes.FirstOrDefault(x =>
            string.Equals(
                OehsIdentityService.BuildRouteKey(
                    x.TenderNumber,
                    x.RouteNumber,
                    x.ContractorTaxOrIdentityNumber,
                    x.ContractNumber,
                    x.ProgramType),
                historyRouteKey,
                StringComparison.Ordinal));

        if (route is null)
        {
            ShowAlreadyRegistered("Bu hakedişin bağlı olduğu hat bulunamadı.");
            return;
        }

        _editingRoute = route;

        var contract = FindContract(route.TenderNumber, route.ContractNumber, route.ProgramType);
        var contractor = FindContractor(route.ContractorTaxOrIdentityNumber, route.ProgramType);

        _suppressSelectionEvents = true;
        try
        {
            RouteTenderListBox.SelectedItem = contract;
            RouteContractorListBox.ItemsSource = contractor is null
                ? Array.Empty<ContractorDraft>()
                : new[] { contractor };
            RouteContractorListBox.SelectedItem = contractor;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        RouteNumberTextBox.Text = route.RouteNumber;
        RouteNumberTextBox.IsReadOnly = false;
        RouteNameTextBox.Text = route.Name;
        RouteDailyPriceTextBox.Text = FormatCalculationInput(route.DailyPrice);
        RouteContractAmountTextBox.Text = FormatCalculationInput(route.ContractAmount ?? item.Input.ContractAmount);
        RouteContractDatePicker.SelectedDate = (route.ContractDate ?? item.ContractDate)?.ToDateTime(TimeOnly.MinValue);
        RouteGuideDailyPriceTextBox.Text = FormatCalculationInput(route.GuideDailyPrice);
        SetRouteEditorMode(true);
        ApplyRouteProgramFields(route.ProgramType);
        NavigationButton_Click(NavLines, new RoutedEventArgs(Button.ClickEvent, NavLines));
        RouteStatusTextBlock.Text = "Hakedişten gelen düzenleme: Hat bilgilerini değiştirin ve DÜZENLE'e basın.";
        SetLiveHistoryCorrectionContext(item.Id, HistoryCorrectionSection.Route);
    }

    private void BeginProgressHistoryEdit(ProgressPaymentHistoryData item)
    {
        if (IsArchiveEditing)
        {
            _suppressArchiveDirtyTracking = true;
            _archiveCorrectionSection = ArchiveCorrectionSection.ProgressPayment;
            var archive = _editingArchiveSnapshot!;
            var payment = archive.ProgressPayment;
            _editingHistoryId = null;
            LoadArchiveProgressEditorContext(ArchiveProgressEditorContext.Create(archive));
            ProgressContractorTextBlock.Text = archive.Contractor.Name;
            ProgressRouteRelationTextBlock.Text =
                $"Arşiv ihale: {payment.TenderNumber} • Yüklenici: {archive.Contractor.Name} • Hat: {payment.RouteNumber} — {payment.RouteName}";
            WorkDaysTextBox.Text = payment.Input.WorkDays.ToString(CultureInfo.CurrentCulture);
            DailyPriceTextBox.Text = FormatCalculationInput(payment.Input.DailyPrice);
            GuideWorkDaysTextBox.Text = payment.Input.GuideWorkDays.ToString(CultureInfo.CurrentCulture);
            GuideDailyPriceTextBox.Text = FormatCalculationInput(payment.Input.GuideDailyPrice);
            DiscountTextBox.Text = FormatCalculationInput(payment.Input.DiscountAmount);
            GuideDiscountTextBox.Text = FormatCalculationInput(payment.Input.GuideDiscountAmount);
            PriceDifferenceTextBox.Text = FormatCalculationInput(payment.Input.PriceDifference);
            GuidePriceDifferenceTextBox.Text = FormatCalculationInput(payment.Input.GuidePriceDifference);
            TaxDebtTextBox.Text = FormatCalculationInput(payment.Input.TaxDebt);
            SocialSecurityDebtTextBox.Text = FormatCalculationInput(payment.Input.SocialSecurityDebt);
            MinimumWageSupportTextBox.Text = FormatCalculationInput(payment.Input.MinimumWageSupport);
            RestoreMinimumWageSupportInputState(payment.Input.ProgramType, payment.Input.MinimumWageSupport);
            InspectionPenaltyTextBox.Text = FormatCalculationInput(payment.Input.InspectionPenalty);
            ProgressNumberTextBox.Text = payment.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            SelectProgressPeriod(payment.PeriodDate);

            NavigationButton_Click(NavProgress, new RoutedEventArgs(Button.ClickEvent, NavProgress));
            // Panel geçişinden sonra editör modunu son kez uygula.
            SetProgressEditorMode(true, archiveCorrection: true);
            CalculationStatusTextBlock.Text =
                "ARŞİV DÜZELTME MODU: Hakediş girdilerini güncelleyin. DÜZENLE mevcut arşiv revision'ını aynı kayıt üzerinde güncelleyecektir.";
            _suppressArchiveDirtyTracking = false;
            return;
        }

        var contract = FindContract(item.TenderNumber, item.ContractNumber, item.Input.ProgramType);
        if (contract is not null)
        {
            _suppressSelectionEvents = true;
            try
            {
                ProgressTenderListBox.SelectedItem = contract;
                ProgressContractorComboBox.ItemsSource = _contractors
                    .Where(x => x.ProgramType == item.Input.ProgramType).ToArray();
                ProgressContractorComboBox.SelectedItem = FindContractor(
                    item.ContractorTaxOrIdentityNumber ?? string.Empty, item.Input.ProgramType);
            }
            finally
            {
                _suppressSelectionEvents = false;
            }
            UpdateProgressContractorAndRoutes(contract);
        }

        _periodEditingId = null; _periodEditingInput = null;
        _editingHistoryId = item.Id;
        _historyOperationProgram = item.Input.ProgramType;
        var historyRouteKey = OehsIdentityService.BuildRouteKey(
            item.TenderNumber ?? string.Empty,
            item.RouteNumber ?? string.Empty,
            item.ContractorTaxOrIdentityNumber ?? string.Empty,
            item.ContractNumber ?? string.Empty,
            item.Input.ProgramType);

        ProgressRouteComboBox.SelectedItem = _routes.FirstOrDefault(x =>
            string.Equals(
                OehsIdentityService.BuildRouteKey(
                    x.TenderNumber,
                    x.RouteNumber,
                    x.ContractorTaxOrIdentityNumber,
                    x.ContractNumber,
                    x.ProgramType),
                historyRouteKey,
                StringComparison.Ordinal));

        WorkDaysTextBox.Text = item.Input.WorkDays.ToString(CultureInfo.CurrentCulture);
        DailyPriceTextBox.Text = FormatCalculationInput(item.Input.DailyPrice);
        GuideWorkDaysTextBox.Text = item.Input.GuideWorkDays.ToString(CultureInfo.CurrentCulture);
        GuideDailyPriceTextBox.Text = FormatCalculationInput(item.Input.GuideDailyPrice);
        DiscountTextBox.Text = FormatCalculationInput(item.Input.DiscountAmount);
        GuideDiscountTextBox.Text = FormatCalculationInput(item.Input.GuideDiscountAmount);
        PriceDifferenceTextBox.Text = FormatCalculationInput(item.Input.PriceDifference);
        GuidePriceDifferenceTextBox.Text = FormatCalculationInput(item.Input.GuidePriceDifference);
        TaxDebtTextBox.Text = FormatCalculationInput(item.Input.TaxDebt);
        SocialSecurityDebtTextBox.Text = FormatCalculationInput(item.Input.SocialSecurityDebt);
        MinimumWageSupportTextBox.Text = FormatCalculationInput(item.Input.MinimumWageSupport);
        RestoreMinimumWageSupportInputState(item.Input.ProgramType, item.Input.MinimumWageSupport);
        InspectionPenaltyTextBox.Text = FormatCalculationInput(item.Input.InspectionPenalty);
        ProgressNumberTextBox.Text = item.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        SelectProgressPeriod(item.PeriodDate);

        SetProgressEditorMode(true);
        NavigationButton_Click(NavProgress, new RoutedEventArgs(Button.ClickEvent, NavProgress));
        CalculationStatusTextBlock.Text = "Düzenleme modu: hakediş girdilerini değiştirin ve DÜZENLE'e basın.";
        SetLiveHistoryCorrectionContext(item.Id, HistoryCorrectionSection.ProgressPayment);
    }

    private void CancelProgressEditButton_Click(object sender, RoutedEventArgs e)
    {
        var archiveCorrection = IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment);
        var original = !archiveCorrection && _editingHistoryId is Guid id
            ? _progressHistory.FirstOrDefault(x => IsOperationPayment(x, id))
            : null;

        ClearArchiveCorrectionState();
        _editingHistoryId = null;

        if (archiveCorrection)
        {
            ProgressValidationBanner.Visibility = Visibility.Collapsed;
            NavigateToArchives();
            return;
        }

        if (original is not null)
        {
            WorkDaysTextBox.Text = original.Input.WorkDays.ToString(CultureInfo.CurrentCulture);
            DailyPriceTextBox.Text = FormatCalculationInput(original.Input.DailyPrice);
            GuideWorkDaysTextBox.Text = original.Input.GuideWorkDays.ToString(CultureInfo.CurrentCulture);
            GuideDailyPriceTextBox.Text = FormatCalculationInput(original.Input.GuideDailyPrice);
            DiscountTextBox.Text = FormatCalculationInput(original.Input.DiscountAmount);
            GuideDiscountTextBox.Text = FormatCalculationInput(original.Input.GuideDiscountAmount);
            PriceDifferenceTextBox.Text = FormatCalculationInput(original.Input.PriceDifference);
            GuidePriceDifferenceTextBox.Text = FormatCalculationInput(original.Input.GuidePriceDifference);
            TaxDebtTextBox.Text = FormatCalculationInput(original.Input.TaxDebt);
            SocialSecurityDebtTextBox.Text = FormatCalculationInput(original.Input.SocialSecurityDebt);
            MinimumWageSupportTextBox.Text = FormatCalculationInput(original.Input.MinimumWageSupport);
            RestoreMinimumWageSupportInputState(original.Input.ProgramType, original.Input.MinimumWageSupport);
            InspectionPenaltyTextBox.Text = FormatCalculationInput(original.Input.InspectionPenalty);
            ProgressNumberTextBox.Text = original.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            SelectProgressPeriod(original.PeriodDate);
        }

        SetProgressEditorMode(false);
        CalculationStatusTextBlock.Text = "Hakediş düzenlemesi iptal edildi; kayıt değiştirilmedi.";
    }

    private void DeleteHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HistoryRow row }) return;
        var item = ChooseHistoryMember(row);
        if (item is null) return;

        var title = item.ProgressPaymentNumber.HasValue
            ? $"Hakediş No {item.ProgressPaymentNumber.Value}"
            : "Hakediş Hesaplama";
        var result = NightMessageBox.Show(
            $"{title} kaydı silinsin mi? Bu işlem geri alınamaz.",
            "Hakediş Kaydını Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var index = _progressHistory.IndexOf(item);
        var previousEditingHistoryId = _editingHistoryId;
        if (index < 0) return;

        _progressHistory.RemoveAt(index);
        if (_editingHistoryId == item.Id) _editingHistoryId = null;

        if (!SavePersistedData())
        {
            _progressHistory.Insert(index, item);
            _editingHistoryId = previousEditingHistoryId;
            RefreshViewsAfterPersistenceRollback();
            CalculationStatusTextBlock.Text =
                "Hakediş silinmedi: disk kaydı başarısız oldu; kayıt tekrar listeye alındı.";
            return;
        }

        UpdateHistoryView();
        UpdateSummaryView();
        UpdateReportView();
        UpdateHomeSummaryView();
        UpdateAcceptanceView();
        CalculationStatusTextBlock.Text = $"{title} kaydı silindi.";
    }

}
