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
    private static bool IsBeforePayment(ProgressPaymentHistoryData item, ProgressPaymentHistoryData current)
    {
        if (current.PeriodDate.HasValue && item.PeriodDate.HasValue)
            return item.PeriodDate.Value < current.PeriodDate.Value;

        return item.CreatedAt < current.CreatedAt;
    }

    private OehsOfficialDocumentContext? BuildSelectedOfficialDocumentContext()
    {
        if (_homeOutputPayment is null && !EnsureRouteForOutput()) return null;
        var latest = _homeOutputPayment ?? GetSelectedOutputPayment();
        if (latest is null || GetOutputBatchPayments(latest).Any(payment=>!EnsurePaymentPreflight(payment))) return null;
        return BuildOfficialDocumentContextForPayment(latest);
    }

    private OehsOfficialDocumentContext? BuildOfficialDocumentContextForPayment(ProgressPaymentHistoryData latest)
    {
        var members = GetOutputBatchPayments(latest);
        var contexts = members.Select(BuildSingleRouteDocumentContext).ToArray();
        if (contexts.Any(context => context is null)) return null;
        return OehsCombinedDocumentContext.Combine(contexts.Select(context => context!).ToArray());
    }

    private OehsOfficialDocumentContext? BuildSingleRouteDocumentContext(ProgressPaymentHistoryData latest)
    {
        ArgumentNullException.ThrowIfNull(latest);

        var routeKey = OehsIdentityService.BuildRouteKey(
            latest.TenderNumber ?? string.Empty,
            latest.RouteNumber ?? string.Empty,
            latest.ContractorTaxOrIdentityNumber ?? string.Empty,
            latest.ContractNumber ?? string.Empty,
            latest.Input.ProgramType);
        var route = _routes.FirstOrDefault(candidate => string.Equals(
            OehsIdentityService.BuildRouteKey(
                candidate.TenderNumber,
                candidate.RouteNumber,
                candidate.ContractorTaxOrIdentityNumber,
                candidate.ContractNumber,
                candidate.ProgramType),
            routeKey,
            StringComparison.Ordinal));
        if (route is null)
            return null;

        var payments = _progressHistory
            .Where(x => string.Equals(
                OehsIdentityService.BuildRouteKey(
                    x.TenderNumber ?? string.Empty,
                    x.RouteNumber ?? string.Empty,
                    x.ContractorTaxOrIdentityNumber ?? string.Empty,
                    x.ContractNumber ?? string.Empty,
                    x.Input.ProgramType),
                routeKey,
                StringComparison.Ordinal))
            .OrderBy(x => x.PeriodDate ?? DateOnly.MinValue)
            .ThenBy(x => x.CreatedAt)
            .ToList();
        if (!payments.Any(x => x.Id == latest.Id))
            payments.Add(latest);
        payments = ProgressPaymentReportService.OrderByPaymentPeriod(payments).ToList();

        var reportTotals = ProgressPaymentReportService.Calculate(latest, payments);
        var reportNetPayable = ProgressPaymentReportService.CalculateReportNetPayable(latest, reportTotals);
        var previousDeduction = ProgressPaymentIcmalService.FindPreviousDeduction(payments, latest);
        var signatories = OehsOfficialDocumentSettingsStore.Load();

        var contract = FindContract(
            latest.TenderNumber ?? route.TenderNumber,
            latest.ContractNumber ?? route.ContractNumber,
            latest.Input.ProgramType);

        var contractStartDate = latest.ContractStartDate ?? contract?.StartDate;
        var contractEndDate = latest.ContractEndDate ?? contract?.EndDate;
        var tenderDate = latest.TenderDate ?? contract?.TenderDate;
        var contractDate = latest.ContractDate ?? contract?.ContractDate;

        // Do not label a calendar-day subtraction as contract workdays.
        // If the authoritative workday count is absent, official forms show "-".
        var duration = contract?.ContractWorkDays ?? 0;
        var contractorName = string.IsNullOrWhiteSpace(latest.ContractorName)
            ? (string.IsNullOrWhiteSpace(contract?.ContractorName) ? "-" : contract.ContractorName.Trim())
            : latest.ContractorName.Trim();

        return new OehsOfficialDocumentContext(
            WorkName: OfficialTemplateProfile.DeriveWorkName(
                string.IsNullOrWhiteSpace(latest.RouteName) ? route.Name : latest.RouteName,
                string.IsNullOrWhiteSpace(latest.WorkName) ? contract?.WorkName : latest.WorkName),
            TenderNumber: latest.TenderNumber ?? route.TenderNumber,
            RouteNumber: latest.RouteNumber ?? route.RouteNumber,
            ContractorName: contractorName,
            ContractNumber: latest.ContractNumber ?? contract?.ContractNumber ?? string.Empty,
            ContractAmount: latest.Input.ContractAmount,
            TenderDate: tenderDate,
            ContractDate: contractDate,
            ContractStartDate: contractStartDate,
            ContractEndDate: contractEndDate,
            ContractWorkDays: duration,
            PeriodDate: latest.PeriodDate,
            ProgressPaymentNumber: latest.ProgressPaymentNumber,
            Calculation: latest.Calculation,
            Payments: payments,
            ReportTotals: reportTotals,
            ReportNetPayable: reportNetPayable,
            PreviousDeduction: previousDeduction,
            GeneratedDate: DateOnly.FromDateTime(DateTime.Today),
            PreparerName: signatories.PreparerName,
            PreparerRole: signatories.PreparerRole,
            ControllerName: signatories.ControllerName,
            ControllerRole: signatories.ControllerRole,
            ApproverName: signatories.ApproverName,
            ApproverRole: signatories.ApproverRole,
            ProgramType: latest.Input.ProgramType,
            RouteName: string.IsNullOrWhiteSpace(latest.RouteName) ? route.Name : latest.RouteName.Trim());
    }

    private FixedDocument? BuildSelectedRouteOutputDocument()
    {
        var context = BuildSelectedOfficialDocumentContext();
        return context is null ? null : OehsOfficialDocumentFactory.BuildCombined(context);
    }

    private void PrintAllRouteDocumentsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var document = BuildSelectedRouteOutputDocument();
            if (document is null)
                return;

            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
                return;

            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            paginator.ComputePageCount();
            dialog.PrintDocument(paginator, "OEHS Hakediş Belgeleri");
        }
        catch (Exception ex)
        {
            NightMessageBox.Show(
                $"Yazdırma işlemi başlatılamadı.\n\n{ex.Message}",
                "OEHS — Yazdırma",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool EnsureRouteForOutput()
    {
        if (GetSelectedOutputPayment() is not null) return true;
        UpdateReportView();
        ShowOutputStatus("İhale, yüklenici ve hakediş seçimini kontrol edin. Kayıt güncellendiyse bilgileri tekrar doğrulayıp işlemi yineleyin.", warning: true);
        return false;
    }

    private void CreateSaveAllRouteDocumentsButton_Click(object sender, RoutedEventArgs e)
    {
        string? stagingFolder = null;
        var preserveStagingFolderForRecovery = false;
        try
        {
            var context = BuildSelectedOfficialDocumentContext();
            if (context is null) return;
            var latest = _homeOutputPayment ?? GetSelectedOutputPayment();
            if (latest is null) return;
            var targetFolder = OehsOutputPathService.BuildOfficialPdfDirectory(OehsRuntimePaths.DesktopRoot, latest);
            Directory.CreateDirectory(targetFolder);

            var outputs = new[] { OehsOfficialDocumentFactory.BuildCombined(context) };
            if (outputs[0].Pages.Count != 4)
                throw new InvalidOperationException("Birleşik PDF için dört resmî belge sayfası gerekir.");

            var targetPaths = new[] { OehsOutputPathService.BuildPaymentDocumentsPdfFilePath(OehsRuntimePaths.DesktopRoot, latest) };

            var existingPaths = targetPaths.Where(File.Exists).ToArray();
            if (existingPaths.Length > 0)
            {
                var existingNames = string.Join(
                    Environment.NewLine,
                    existingPaths.Select(Path.GetFileName));

                var choice = NightMessageBox.Show(
                    this,
                    "Bu hakediş için aynı isimde PDF dosyaları zaten var. Onay verirsen yalnızca listelenen mevcut dosyalar yenileriyle değiştirilecek. Devam etmeden önce var olan dosyaların yedeği geçici olarak alınacak.\n\n" +
                    existingNames +
                    "\n\nMevcut PDF dosyalarının üzerine yazılsın mı?",
                    "OEHS — Mevcut PDF Dosyaları",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (choice != MessageBoxResult.Yes)
                    return;
            }

            // Stage the combined four-page PDF before replacing the final file.
            // Retain the original for rollback if publishing the new file fails.
            stagingFolder = Path.Combine(Path.GetTempPath(), "oehs-pdf-" + Guid.NewGuid().ToString("N"));
            var stagedPaths = outputs
                .Select((output, index) => Path.Combine(stagingFolder, $"{index}.pdf"))
                .ToArray();

            for (var index = 0; index < outputs.Length; index++)
            {
                PdfVectorWriter.Save(outputs[index], stagedPaths[index]);
                if (!File.Exists(stagedPaths[index]) || new FileInfo(stagedPaths[index]).Length < 500)
                    throw new IOException($"PDF dosyası doğrulanamadı: {Path.GetFileName(stagedPaths[index])}");
            }

            var backupFolder = Path.Combine(stagingFolder, "originals");
            Directory.CreateDirectory(backupFolder);
            var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var targetPath in existingPaths)
            {
                var backupPath = Path.Combine(backupFolder, $"{originals.Count}.pdf");
                File.Copy(targetPath, backupPath, overwrite: false);
                originals[targetPath] = backupPath;
            }

            var replacedPaths = new List<string>();
            try
            {
                for (var index = 0; index < targetPaths.Length; index++)
                {
                    File.Move(stagedPaths[index], targetPaths[index], overwrite: true);
                    replacedPaths.Add(targetPaths[index]);
                }
            }
            catch
            {
                // Best-effort rollback if an individual target could not be replaced.
                var rollbackFailed = false;
                foreach (var targetPath in replacedPaths.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (originals.TryGetValue(targetPath, out var backupPath) && File.Exists(backupPath))
                            File.Copy(backupPath, targetPath, overwrite: true);
                        else if (File.Exists(targetPath))
                            File.Delete(targetPath);
                    }
                    catch
                    {
                        rollbackFailed = true;
                    }
                }

                if (rollbackFailed)
                    preserveStagingFolderForRecovery = true;

                throw;
            }

            var metadataSaved = true;
            string? metadataError = null;
            try
            {
                foreach(var member in GetOutputBatchPayments(latest))
                {
                    SaveRouteDocumentsArchiveMetadata(member, targetPaths);
                    OutputEvidenceStore.Save(OehsRuntimePaths.DesktopRoot, member.Id, BuildOutputFingerprint(context), targetPaths, member.Input.ProgramType);
                }
                RefreshHomeOperations();
            }
            catch (Exception ex)
            {
                metadataSaved = false;
                metadataError = ex.Message;
            }

            CalculationStatusTextBlock.Text =
                $"Dört sayfalı tek PDF oluşturuldu: {Path.GetFileName(targetPaths[0])}.";

            if (!metadataSaved)
            {
                NightMessageBox.Show(
                    this,
                    "Dört sayfalı PDF oluşturuldu; ancak arşiv bilgi dosyası güncellenemedi. PDF kayıt klasöründedir. Arşiv bilgi dosyası hatası:\n\n" + metadataError,
                    "OEHS — PDF Kaydedildi, Arşiv Bilgisi Uyarısı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            var savedNames = string.Join(Environment.NewLine, targetPaths.Select(Path.GetFileName));
            if (_homeOutputPayment is null)
            {
                RefreshOutputSelection(clearOperationStatus: false);
                ShowOutputStatus(metadataSaved ? "Dört sayfalı tek PDF kaydedildi."
                    : $"Dört sayfalı tek PDF kaydedildi; arşiv bilgi kaydı tamamlanamadı: {metadataError}", warning: !metadataSaved);
            }
            ShowPdfSaveInfoDialog(targetFolder, savedNames, targetPaths[0]);
        }
        catch (Exception ex)
        {
            var recoveryNote = preserveStagingFolderForRecovery && !string.IsNullOrWhiteSpace(stagingFolder)
                ? $"\n\nGeri alma işlemi tam tamamlanamadı. Kurtarma kopyaları şu klasörde korundu:\n{stagingFolder}"
                : string.Empty;
            if (_homeOutputPayment is null)
                ShowOutputStatus($"PDF kaydedilemedi: {ex.Message}{recoveryNote}", warning: true);
            else
                NightMessageBox.Show($"Resmî PDF belgeleri oluşturulurken hata oluştu.\n\n{ex.Message}{recoveryNote}",
                    "OEHS PDF Çıktı Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!preserveStagingFolderForRecovery &&
                !string.IsNullOrWhiteSpace(stagingFolder) &&
                Directory.Exists(stagingFolder))
            {
                try
                {
                    Directory.Delete(stagingFolder, recursive: true);
                }
                catch
                {
                    // Any leftover staging/originals files are safer than masking the
                    // actual save result. The folder name makes them easy to identify.
                }
            }
        }
    }

private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

}
