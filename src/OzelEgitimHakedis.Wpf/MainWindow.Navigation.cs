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
    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button)
            return;

        var page = button.Tag?.ToString() ?? "Home";

        if (IsArchiveCorrection)
        {
            var correctionPage = _archiveCorrectionSection switch
            {
                ArchiveCorrectionSection.Contract => "Contract",
                ArchiveCorrectionSection.Contractor => "Contractors",
                ArchiveCorrectionSection.Route => "Lines",
                ArchiveCorrectionSection.ProgressPayment => "Progress",
                _ => string.Empty
            };

            if (!string.Equals(page, correctionPage, StringComparison.Ordinal) &&
                !(correctionPage == "Progress" && page == "Trace"))
            {
                NightMessageBox.Show(
                    "Arşiv düzeltmesi devam ediyor. Önce DÜZENLEME İPTAL veya düzenleme kaydetme işlemini tamamlayın.",
                    "OEHS — Düzenleme Devam Ediyor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }

        if (page == "Signatories" && _operationsReady)
        {
            try
            {
                LoadSignatoriesIntoFields();
            }
            catch (Exception ex)
            {
                NightMessageBox.Show(
                    ex.Message,
                    "OEHS — İmzacı Ayarları Okunamadı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        HomePanel.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
        CalculationTracePanel.Visibility = page == "Trace" ? Visibility.Visible : Visibility.Collapsed;
        ContractPanel.Visibility = page == "Contract" ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = page == "Progress" ? Visibility.Visible : Visibility.Collapsed;
        LinesPanel.Visibility = page == "Lines" ? Visibility.Visible : Visibility.Collapsed;
        ContractorsPanel.Visibility = page == "Contractors" ? Visibility.Visible : Visibility.Collapsed;
        RecordsPanel.Visibility = page is "History" or "Archives" ? Visibility.Visible : Visibility.Collapsed;
        SelectRecordsSection(page);
        if (page == "Trace" && _operationsReady) RefreshCalculationTraceLive();
        HistoryPanel.Visibility = page == "History" ? Visibility.Visible : Visibility.Collapsed;
        ArchivePanel.Visibility = page == "Archives" ? Visibility.Visible : Visibility.Collapsed;
        SummaryPanel.Visibility = page == "Summary" ? Visibility.Visible : Visibility.Collapsed;
        ReportsPanel.Visibility = page == "Reports" ? Visibility.Visible : Visibility.Collapsed;
        AcceptancePanel.Visibility = page == "Acceptance" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        SignatoriesPanel.Visibility = page == "Signatories" ? Visibility.Visible : Visibility.Collapsed;
        CalculationConstantsPanel.Visibility = page == "Constants" ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderPanel.Visibility = page is "Home" or "Contract" or "Progress" or "Lines" or "Contractors"
            or "History" or "Archives" or "Summary" or "Reports" or "Acceptance" or "Settings" or "Signatories" or "Constants" or "Trace"
            ? Visibility.Collapsed
            : Visibility.Visible;

        var module = page switch
        {
            "Contract" => "İhale / Sözleşme",
            "Progress" => "Hakediş Oluştur",
            "Lines" => "Hatlar",
            "Contractors" => "Yüklenici Firmalar",
            "History" => "Kayıtlar ve Arşiv",
            "Archives" => "Kayıtlar ve Arşiv",
            "Summary" => "İcmal",
            "Reports" => "Çıktılar",
            "Acceptance" => "Kabul Tutanağı",
            "Settings" => "Ayarlar",
            "Signatories" => "Resmî Belge İmzacıları",
            "Constants" => "Hesaplama Sabitleri",
            "Trace" => "Canlı Hesap Dökümü",
            _ => "Ana Ekran"
        };

        UpdateShellText(module);

        if (PlaceholderPanel.Visibility == Visibility.Visible)
            PlaceholderTitleTextBlock.Text = module;

        UpdateActiveNavigation(page);
    }

    private void UpdateShellText(string module)
    {
        HeaderModuleText.Text = module;
    }

    private void UpdateActiveNavigation(string page)
    {
        foreach (var button in new[]
        {
            NavHome, NavContract, NavSignatories, NavContractors, NavLines,
            NavProgress, NavReports, NavHistory, NavSettings
        })
        {
            var buttonPage = button.Tag?.ToString();
            button.Uid = buttonPage == page || (page == "Constants" && buttonPage == "Settings")
                || (page == "Archives" && buttonPage == "History")
                || (page == "Trace" && buttonPage == "Progress")
                ? "Active"
                : string.Empty;
        }
    }

}
