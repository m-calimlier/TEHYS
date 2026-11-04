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
    private bool SaveArchiveCorrectionSnapshot(ProgressPaymentArchive updatedArchive, string sectionName)
    {
        var correctionSection = _archiveCorrectionSection;

        if (IsArchiveCorrection && !ArchiveCorrectionInputsDiffer())
        {
            SetArchiveCorrectionStatus("Herhangi bir değişiklik algılanmadı; arşiv kaydı değiştirilmedi.", warning: false);
            return false;
        }

        try
        {
            // Recalculate from saved inputs before asking permission to replace the archive.
            var recalculatedPayment = updatedArchive.ProgressPayment with
            {
                Calculation = PaymentScreenModel.Calculate(updatedArchive.ProgressPayment.Input, _calculationConstants)
            };
            updatedArchive = updatedArchive with
            {
                ProgressPayment = recalculatedPayment,
                PdfFileName = BuildArchivePdfFileName(recalculatedPayment)
            };

            var originalArchive = _editingArchiveSnapshot!;
            var newNetPayable = recalculatedPayment.Calculation.NetPayable;
            var differences = PaymentOperationsService.CompareFields(originalArchive.Contract, updatedArchive.Contract, "Sözleşme")
                .Concat(CompareContractorFields(originalArchive.Contractor, updatedArchive.Contractor))
                .Concat(PaymentOperationsService.CompareFields(originalArchive.Route, updatedArchive.Route, "Hat")).ToArray();
            var confirmation = ShowPaymentComparison("OEHS — Değişiklik Onayı",
                $"{sectionName} değişiklikleri uygulansın mı? Yeni revizyon oluşturulacak; v{originalArchive.Revision:00} korunacak.",
                originalArchive.ProgressPayment, recalculatedPayment, differences);

            if (confirmation != MessageBoxResult.Yes)
            {
                CalculationStatusTextBlock.Text = "Değişiklikler uygulanmadı; arşiv kaydı korunuyor.";
                ArchiveStatusTextBlock.Text = "DEĞİŞİKLİK İPTAL EDİLDİ";
                SetArchiveCorrectionStatus("Değişiklikler iptal edildi; arşiv kaydı korunuyor. Alanlarınızı gözden geçirip yeniden onaylayabilirsiniz.", warning: true);
                return false;
            }

            if (string.IsNullOrWhiteSpace(_editingArchiveSourcePath))
                throw new ProgressPaymentArchiveException("Güncellenecek arşiv dosyası belirlenemedi.");

            updatedArchive = updatedArchive with
            {
                ArchivedAt = DateTime.UtcNow,
                Source = "ArchiveCorrection"
            };
            var finalPath = ProgressPaymentArchiveStore.SaveRevision(_editingArchiveSourcePath, updatedArchive);
            updatedArchive = ProgressPaymentArchiveStore.Load(finalPath);
            recalculatedPayment = updatedArchive.ProgressPayment;

            var historyMirrorUpdated = false;
            var historyMirrorSaved = true;
            var matchingHistoryIndex = _progressHistory
                .Select((item, index) => (item, index))
                .Where(pair => pair.item.Id == recalculatedPayment.Id && pair.item.Input.ProgramType == recalculatedPayment.Input.ProgramType)
                .Select(pair => pair.index)
                .DefaultIfEmpty(-1)
                .First();

            if (matchingHistoryIndex >= 0)
            {
                // Keep the editable history row synchronized with its authoritative archive snapshot.
                // If the secondary JSON save fails, roll the in-memory mirror back instead of
                // displaying unsaved data as though it were persisted. The archive revision remains
                // authoritative and the warning below reports that the mirror could not be saved.
                var previousHistoryItem = _progressHistory[matchingHistoryIndex];
                _progressHistory[matchingHistoryIndex] = recalculatedPayment;
                historyMirrorUpdated = true;
                historyMirrorSaved = SavePersistedData();
                if (!historyMirrorSaved)
                {
                    _progressHistory[matchingHistoryIndex] = previousHistoryItem;
                    historyMirrorUpdated = false;
                }
                UpdateHistoryView();
                UpdateSummaryView();
                UpdateReportView();
                UpdateHomeSummaryView();
                UpdateAcceptanceView();
            }

            ArchiveStatusTextBlock.Text = $"YENİ ARŞİV REVİZYONU v{updatedArchive.Revision:00} OLUŞTURULDU";
            CalculationStatusTextBlock.Text =
                !historyMirrorSaved
                    ? $"{sectionName} düzeltmesi yeni arşiv revision'ı olarak kaydedildi; ancak ana hakediş geçmişi diske yazılamadı. Kayıt durumunu kontrol edin."
                    : historyMirrorUpdated
                        ? $"{sectionName} değişiklikleri yeni arşiv revision'ı ve ana hakediş geçmişine kaydedildi. Önceki revisionlar korundu."
                        : $"{sectionName} değişiklikleri yeni arşiv revision'ı v{updatedArchive.Revision:00} olarak kaydedildi. Önceki revisionlar korundu.";

            _suppressArchiveDirtyTracking = true;
            try
            {
                ClearArchivedEditorFields(correctionSection);
                ClearArchiveCorrectionState();
            }
            finally
            {
                _suppressArchiveDirtyTracking = false;
            }
            UpdateArchiveView();
            NavigateToArchives();

            // Panel navigation and selection refreshes can restore previous payment
            // editor values. Clear the hakediş inputs after navigation as a final guard.
            if (correctionSection == ArchiveCorrectionSection.ProgressPayment)
            {
                ClearProgressEntryFields();
                SetProgressEditorMode(false);
            }

            NightMessageBox.Show(
                $"{sectionName} değişiklikleri başarıyla uygulandı.\n\n" +
                $"Yeni hesaplanan net tutar: {FormatMoney(newNetPayable)}\n" +
                $"Yeni arşiv revision'ı: v{updatedArchive.Revision:00}. Önceki revisionlar korunmuştur.\n" +
                (!historyMirrorSaved
                    ? "DİKKAT: Arşiv güncellendi ancak ana hakediş geçmişi diske yazılamadı. Üstteki kayıt durumunu kontrol edin.\n\n"
                    : historyMirrorUpdated
                        ? "Ana hakediş geçmişi de güncellendi.\n\n"
                        : "Bu arşiv için ana hakediş geçmişinde eşleşen satır yoktu; arşiv dosyası güncellendi.\n\n") +
                $"Dosya: {Path.GetFileName(finalPath)}",
                "OEHS — Arşiv Güncellendi",
                MessageBoxButton.OK,
                historyMirrorSaved ? MessageBoxImage.Information : MessageBoxImage.Warning);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProgressPaymentArchiveException or ArgumentOutOfRangeException)
        {
            ArchiveStatusTextBlock.Text = "ARŞİV HATASI";
            CalculationStatusTextBlock.Text = ex.Message;
            NightMessageBox.Show(
                $"Arşiv güncellenemedi.\n\n{ex.Message}",
                "OEHS — Arşiv Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private void ClearArchivedEditorFields(ArchiveCorrectionSection section)
    {
        switch (section)
        {
            case ArchiveCorrectionSection.Contract:
                _editingContractTender = null;
                ClearContractFormOnly();
                ClearContractValidationVisuals();
                ContractStatusTextBlock.Text = "Arşiv sözleşmesi güncellendi; giriş alanları temizlendi.";
                break;

            case ArchiveCorrectionSection.Contractor:
                _editingContractorKey = null;
                ContractorNameTextBox.Clear();
                ContractorTaxTextBox.Clear();
                ContractorBankTextBox.Clear();
                ContractorStatusTextBlock.Text = "Arşiv yüklenicisi güncellendi; giriş alanları temizlendi.";
                break;

            case ArchiveCorrectionSection.Route:
                _editingRoute = null;
                RouteNumberTextBox.Clear();
                RouteNameTextBox.Clear();
                RouteGuideDailyPriceTextBox.Clear();
                ClearRouteValidationVisuals();
                RouteStatusTextBlock.Text = "Arşiv hattı güncellendi; giriş alanları temizlendi.";
                break;

            case ArchiveCorrectionSection.ProgressPayment:
                _editingHistoryId = null;
                ClearProgressEntryFields();
                CalculationStatusTextBlock.Text = "Arşiv hakedişi güncellendi; giriş alanları temizlendi.";
                break;
        }
    }

    private void SaveArchivedContractorCorrection()
    {
        var archive = _editingArchiveSnapshot!;
        ClearContractorValidation();

        var name = ContractorNameTextBox.Text.Trim();
        var tax = OehsIdentityService.NormalizeIdentity(ContractorTaxTextBox.Text);

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(tax))
        {
            if (string.IsNullOrWhiteSpace(name)) MarkInvalid(ContractorNameTextBox);
            if (string.IsNullOrWhiteSpace(tax)) MarkInvalid(ContractorTaxTextBox);
            ShowContractorValidation("Firma adı ve TC / Vergi No zorunludur.");
            return;
        }

        var updatedContractor = new Contractor(
            name,
            tax,
            archive.Contractor.Address,
            archive.Contractor.Phone,
            archive.Contractor.Email,
            ContractorBankTextBox.Text.Trim(),
            archive.Contractor.ContactPerson,
            archive.Contract.ProgramType,
            ContractorVatExemptCheckBox.IsChecked == true);

        var updatedContract = archive.Contract with
        {
            ContractorName = name,
            ContractorTaxOrIdentityNumber = tax,
            BankAccountNumber = updatedContractor.BankAccountNumber
        };

        var payment = archive.ProgressPayment with
        {
            ContractorName = name,
            ContractorTaxOrIdentityNumber = tax,
            BankAccountNumber = updatedContractor.BankAccountNumber
        };

        var route = archive.Route with { ContractorTaxOrIdentityNumber = tax };

        var updatedPreviousPayments = archive.PreviousPayments
            .Select(previous => previous with
            {
                ContractorName = name,
                ContractorTaxOrIdentityNumber = tax,
                BankAccountNumber = updatedContractor.BankAccountNumber
            })
            .ToArray();

        SaveArchiveCorrectionSnapshot(
            archive with
            {
                Contract = updatedContract,
                Contractor = updatedContractor,
                Route = route,
                ProgressPayment = payment,
                PreviousPayments = updatedPreviousPayments,
                PdfFileName = BuildArchivePdfFileName(payment)
            },
            "Yüklenici firma");
    }

    private void SaveArchivedContractCorrection()
    {
        var archive = _editingArchiveSnapshot!;
        ClearContractValidationVisuals();

        var errors = new List<Control>();
        if (string.IsNullOrWhiteSpace(TenderNumberTextBox.Text)) errors.Add(TenderNumberTextBox);
        if (!TryReadLegacyContractAmount(ContractAmountTextBox.Text, out var contractAmount)) errors.Add(ContractAmountTextBox);
        if (!int.TryParse(ContractWorkDaysTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var contractWorkDays) || contractWorkDays < 0) errors.Add(ContractWorkDaysTextBox);
        if (!TryReadLegacyContractAmount(ContractTransportDailyPriceTextBox.Text, out var transportDailyPrice)) errors.Add(ContractTransportDailyPriceTextBox);
        if (!HasValidTenderDateText(StartDatePicker)) errors.Add(StartDatePicker);
        if (!HasValidTenderDateText(EndDatePicker)) errors.Add(EndDatePicker);
        if (!HasValidTenderDateText(TenderDatePicker)) errors.Add(TenderDatePicker);

        DateOnly? startDate = StartDatePicker.SelectedDate.HasValue
            ? DateOnly.FromDateTime(StartDatePicker.SelectedDate.Value)
            : null;
        DateOnly? endDate = EndDatePicker.SelectedDate.HasValue
            ? DateOnly.FromDateTime(EndDatePicker.SelectedDate.Value)
            : null;

        if (!startDate.HasValue) errors.Add(StartDatePicker);
        if (!endDate.HasValue) errors.Add(EndDatePicker);

        if (endDate < startDate)
        {
            errors.Add(StartDatePicker);
            errors.Add(EndDatePicker);
        }

        var archivePaymentPeriods = archive.PreviousPayments
            .Where(x => x.PeriodDate.HasValue)
            .Select(x => x.PeriodDate!.Value)
            .ToList();

        if (archive.ProgressPayment.PeriodDate is DateOnly currentPaymentPeriod)
            archivePaymentPeriods.Add(currentPaymentPeriod);

        if (startDate.HasValue && endDate.HasValue && archivePaymentPeriods.Any(paymentPeriod =>
                paymentPeriod < startDate || paymentPeriod > endDate))
        {
            errors.Add(StartDatePicker);
            errors.Add(EndDatePicker);
        }

        if (errors.Count > 0)
        {
            foreach (var control in errors) MarkInvalid(control);
            ContractStatusTextBlock.Text = "Arşiv düzeltmesi kaydedilmedi. Kırmızı alanları kontrol edin.";
            return;
        }

        var updatedContract = archive.Contract with
        {
            TenderNumber = TenderNumberTextBox.Text.Trim(),
            ContractNumber = archive.Contract.ContractNumber,
            // WorkName is a derived display; retain the archived legacy fallback.
            ContractAmount = contractAmount,
            StartDate = startDate!.Value,
            EndDate = endDate!.Value,
            VatExempt = archive.Contract.VatExempt,
            TenderDate = TenderDatePicker.SelectedDate.HasValue ? DateOnly.FromDateTime(TenderDatePicker.SelectedDate.Value) : archive.Contract.TenderDate,
            ContractDate = ContractDatePicker.SelectedDate.HasValue ? DateOnly.FromDateTime(ContractDatePicker.SelectedDate.Value) : archive.Contract.ContractDate,
            ContractWorkDays = contractWorkDays,
            TransportDailyPrice = transportDailyPrice,
            // An archive correction edits the archived contract; it must not change
            // the education program based on a possibly blank/global UI selector.
            ProgramType = archive.Contract.ProgramType
        };

        var hasGuide = HasGuideStaff(updatedContract.ProgramType);
        var input = archive.ProgressPayment.Input with
        {
            IsVatExempt = archive.ProgressPayment.Input.IsVatExempt,
            GuideWorkDays = hasGuide ? archive.ProgressPayment.Input.GuideWorkDays : 0,
            GuideDailyPrice = hasGuide ? archive.ProgressPayment.Input.GuideDailyPrice : 0m,
            GuideDiscountAmount = hasGuide ? archive.ProgressPayment.Input.GuideDiscountAmount : 0m,
            GuidePriceDifference = hasGuide ? archive.ProgressPayment.Input.GuidePriceDifference : 0m,
            MinimumWageSupport = archive.ProgressPayment.Input.MinimumWageSupport,
            ProgramType = updatedContract.ProgramType
        };

        var payment = archive.ProgressPayment with
        {
            Input = input,
            Calculation = PaymentScreenModel.Calculate(input, _calculationConstants),
            WorkName = updatedContract.WorkName,
            TenderNumber = updatedContract.TenderNumber,
            ContractNumber = updatedContract.ContractNumber,
            TenderDate = updatedContract.TenderDate,
            ContractStartDate = updatedContract.StartDate,
            ContractEndDate = updatedContract.EndDate
        };

        var route = archive.Route with
        {
            TenderNumber = updatedContract.TenderNumber,
            ContractNumber = updatedContract.ContractNumber,
            GuideDailyPrice = hasGuide ? archive.Route.GuideDailyPrice : 0m,
            ProgramType = updatedContract.ProgramType
        };

        var updatedPreviousPayments = archive.PreviousPayments
            .Select(previous =>
            {
                var previousHasGuide = HasGuideStaff(updatedContract.ProgramType);
                var previousInput = previous.Input with
                {
                    IsVatExempt = archive.ProgressPayment.Input.IsVatExempt,
                    GuideWorkDays = previousHasGuide ? previous.Input.GuideWorkDays : 0,
                    GuideDailyPrice = previousHasGuide ? previous.Input.GuideDailyPrice : 0m,
                    GuideDiscountAmount = previousHasGuide ? previous.Input.GuideDiscountAmount : 0m,
                    GuidePriceDifference = previousHasGuide ? previous.Input.GuidePriceDifference : 0m,
                    MinimumWageSupport = previous.Input.MinimumWageSupport,
                    ProgramType = updatedContract.ProgramType
                };

                return previous with
                {
                    Input = previousInput,
                    Calculation = PaymentScreenModel.Calculate(previousInput, _calculationConstants),
                    WorkName = updatedContract.WorkName,
                    TenderNumber = updatedContract.TenderNumber,
                    ContractNumber = updatedContract.ContractNumber,
                    TenderDate = updatedContract.TenderDate,
                    ContractStartDate = updatedContract.StartDate,
                    ContractEndDate = updatedContract.EndDate
                };
            })
            .ToArray();

        SaveArchiveCorrectionSnapshot(
            archive with
            {
                Contract = updatedContract,
                Route = route,
                ProgressPayment = payment,
                PreviousPayments = updatedPreviousPayments,
                PdfFileName = BuildArchivePdfFileName(payment)
            },
            "İhale bilgileri");
    }

    private void SaveArchivedRouteCorrection()
    {
        var archive = _editingArchiveSnapshot!;
        ClearRouteValidationVisuals();

        var routeNumber = RouteNumberTextBox.Text.Trim();
        var routeName = RouteNameTextBox.Text.Trim();

        var errors = new List<Control>();
        if (string.IsNullOrWhiteSpace(routeNumber)) errors.Add(RouteNumberTextBox);
        if (string.IsNullOrWhiteSpace(routeName)) errors.Add(RouteNameTextBox);
        if (!TurkishMoneyInput.TryParseCalculationAmount(RouteDailyPriceTextBox.Text, out var routeDailyPrice) || routeDailyPrice < 0) errors.Add(RouteDailyPriceTextBox);
        if (!TurkishMoneyInput.TryParseCents(RouteContractAmountTextBox.Text, out var routeContractAmount) || routeContractAmount < 0) errors.Add(RouteContractAmountTextBox);
        if (!RouteContractDatePicker.SelectedDate.HasValue || !HasValidTenderDateText(RouteContractDatePicker)) errors.Add(RouteContractDatePicker);
        var hasGuide = HasGuideStaff(archive.Contract.ProgramType);
        var guideDailyPrice = 0m;
        if (hasGuide)
        {
            if (!TurkishMoneyInput.TryParseCalculationAmount(RouteGuideDailyPriceTextBox.Text, out guideDailyPrice) || guideDailyPrice < 0) errors.Add(RouteGuideDailyPriceTextBox);
        }

        if (errors.Count > 0)
        {
            foreach (var control in errors) MarkInvalid(control);
            RouteStatusTextBlock.Text = "Arşiv düzeltmesi kaydedilmedi. Kırmızı alanları kontrol edin.";
            return;
        }

        var updatedRoute = archive.Route with
        {
            RouteNumber = routeNumber,
            Name = routeName,
            DailyPrice = routeDailyPrice,
            ContractAmount = routeContractAmount,
            ContractDate = DateOnly.FromDateTime(RouteContractDatePicker.SelectedDate!.Value),
            GuideDailyPrice = hasGuide ? guideDailyPrice : 0m,
            ProgramType = archive.Contract.ProgramType
        };

        var input = archive.ProgressPayment.Input with
        {
            DailyPrice = (!archive.ProgressPayment.Input.HasPeriodChanges && archive.ProgressPayment.Input.TransportPeriods is null) && archive.Route.DailyPrice != routeDailyPrice ? routeDailyPrice : archive.ProgressPayment.Input.DailyPrice,
            ContractAmount = (archive.Route.ContractAmount ?? archive.ProgressPayment.Input.ContractAmount) != routeContractAmount ? routeContractAmount : archive.ProgressPayment.Input.ContractAmount,
            GuideDailyPrice = hasGuide ? ((!archive.ProgressPayment.Input.HasPeriodChanges && archive.ProgressPayment.Input.GuidePeriods is null) && archive.Route.GuideDailyPrice != guideDailyPrice ? guideDailyPrice : archive.ProgressPayment.Input.GuideDailyPrice) : 0m,
            GuideDiscountAmount = hasGuide ? archive.ProgressPayment.Input.GuideDiscountAmount : 0m,
            GuidePriceDifference = hasGuide ? archive.ProgressPayment.Input.GuidePriceDifference : 0m,
            MinimumWageSupport = archive.ProgressPayment.Input.MinimumWageSupport,
            ProgramType = archive.Contract.ProgramType
        };

        var payment = archive.ProgressPayment with
        {
            Input = input,
            Calculation = PaymentScreenModel.Calculate(input, _calculationConstants),
            RouteNumber = routeNumber,
            RouteName = routeName,
            ContractDate = (archive.Route.ContractDate ?? archive.ProgressPayment.ContractDate ?? archive.Contract.ContractDate) != DateOnly.FromDateTime(RouteContractDatePicker.SelectedDate!.Value)
                ? DateOnly.FromDateTime(RouteContractDatePicker.SelectedDate.Value) : archive.ProgressPayment.ContractDate
        };

        var updatedPreviousPayments = archive.PreviousPayments
            .Select(previous => previous with
            {
                RouteNumber = routeNumber,
                RouteName = routeName
            })
            .ToArray();

        SaveArchiveCorrectionSnapshot(
            archive with
            {
                Route = updatedRoute,
                ProgressPayment = payment,
                PreviousPayments = updatedPreviousPayments,
                PdfFileName = BuildArchivePdfFileName(payment)
            },
            "Hat bilgileri");
    }

}
