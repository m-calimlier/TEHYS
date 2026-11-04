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
    private void PopulateCalculationConstantsPanel()
    {
        if (TransportVatRateTextBox is null)
            return;
        TransportVatRateTextBox.Text = FormatPercentageInput(_calculationConstants.TransportVatRate);
        GuideVatRateTextBox.Text = FormatPercentageInput(_calculationConstants.GuideVatRate);
        TransportWithholdingShareTextBox.Text = FormatPercentageInput(_calculationConstants.TransportWithholdingShare);
        GuideWithholdingShareTextBox.Text = FormatPercentageInput(_calculationConstants.GuideWithholdingShare);
        StampTaxRateTextBox.Text = FormatPercentageInput(_calculationConstants.StampTaxRate);
        AdditionalGuaranteeRateTextBox.Text = FormatPercentageInput(_calculationConstants.AdditionalGuaranteeRate);
        UpdateCalculationConstantsAuditDisplay();
    }

    private static string FormatPercentageInput(decimal fraction) =>
        (fraction * 100m).ToString("0.###", CultureInfo.CurrentCulture);

    private static bool TryParsePercentageInput(string? text, out decimal fraction)
    {
        fraction = 0m;
        if (!TryParseDecimal(text, out var percent) || percent < 0m || percent > 100m)
            return false;
        fraction = percent / 100m;
        return true;
    }

    private void UpdateCalculationConstantsAuditDisplay()
    {
        if (CalculationConstantsAuditTextBlock is null)
            return;
        if (_calculationConstantsHistory.Count == 0)
        {
            CalculationConstantsAuditTextBlock.Text =
                "Henüz sabit değişikliği kaydedilmedi. Başlangıç değerleri kaynak Excel'deki oranlardır; mevzuat uygunluğu teyidi değildir.";
            return;
        }

        CalculationConstantsAuditTextBlock.Text = string.Join(
            Environment.NewLine + Environment.NewLine,
            _calculationConstantsHistory.OrderByDescending(x => x.ChangedAtUtc).Take(5).Select(x =>
                $"{x.ChangedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture)} • {x.ChangedBy}" +
                $"{Environment.NewLine}Gerekçe: {x.Reason}" +
                $"{Environment.NewLine}Taşıma KDV: %{FormatPercentageInput(x.Previous.TransportVatRate)} → %{FormatPercentageInput(x.Current.TransportVatRate)}" +
                $"; Rehber KDV: %{FormatPercentageInput(x.Previous.GuideVatRate)} → %{FormatPercentageInput(x.Current.GuideVatRate)}" +
                $"{Environment.NewLine}Taşıma tevkifat payı: %{FormatPercentageInput(x.Previous.TransportWithholdingShare)} → %{FormatPercentageInput(x.Current.TransportWithholdingShare)}" +
                $"; Rehber tevkifat payı: %{FormatPercentageInput(x.Previous.GuideWithholdingShare)} → %{FormatPercentageInput(x.Current.GuideWithholdingShare)}" +
                $"{Environment.NewLine}Damga vergisi: %{FormatPercentageInput(x.Previous.StampTaxRate)} → %{FormatPercentageInput(x.Current.StampTaxRate)}" +
                $"; Ek kesin teminat: %{FormatPercentageInput(x.Previous.AdditionalGuaranteeRate)} → %{FormatPercentageInput(x.Current.AdditionalGuaranteeRate)}"));
    }

    private void SaveCalculationConstantsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParsePercentageInput(TransportVatRateTextBox.Text, out var transportVat) ||
            !TryParsePercentageInput(GuideVatRateTextBox.Text, out var guideVat) ||
            !TryParsePercentageInput(TransportWithholdingShareTextBox.Text, out var transportWithholding) ||
            !TryParsePercentageInput(GuideWithholdingShareTextBox.Text, out var guideWithholding) ||
            !TryParsePercentageInput(StampTaxRateTextBox.Text, out var stampTax) ||
            !TryParsePercentageInput(AdditionalGuaranteeRateTextBox.Text, out var additionalGuarantee))
        {
            NightMessageBox.Show("Bütün oranları yüzde olarak girin. Her alan %0 ile %100 arasında olmalıdır (ör. 10 veya 0,948).",
                "OEHS — Sabit Doğrulama", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var changedBy = CalculationConstantsChangedByTextBox.Text.Trim();
        var reason = CalculationConstantsReasonTextBox.Text.Trim();
        if (changedBy.Length == 0 || reason.Length < 5)
        {
            NightMessageBox.Show("Sabit değişikliğini kaydetmek için işlemi yapan kişinin adını ve en az 5 karakterlik gerekçeyi girin.",
                "OEHS — Değişiklik Kaydı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var proposed = new ProgressPaymentCalculationConstants(
            transportVat, guideVat, transportWithholding, guideWithholding, stampTax, additionalGuarantee);
        if (!proposed.TryValidate(out var validationError))
        {
            NightMessageBox.Show(validationError, "OEHS — Sabit Doğrulama", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (proposed == _calculationConstants)
        {
            NightMessageBox.Show("Girilen sabitlerde değişiklik yok. Yeni kayıt oluşturulmadı.", "OEHS",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var previous = _calculationConstants;
        var confirmation = NightMessageBox.Show(
            "Hesaplama sabitleri değiştirilecek:" + Environment.NewLine + Environment.NewLine +
            $"Taşıma KDV: %{FormatPercentageInput(previous.TransportVatRate)} → %{FormatPercentageInput(proposed.TransportVatRate)}" + Environment.NewLine +
            $"Rehber KDV: %{FormatPercentageInput(previous.GuideVatRate)} → %{FormatPercentageInput(proposed.GuideVatRate)}" + Environment.NewLine +
            $"Taşıma tevkifat payı: %{FormatPercentageInput(previous.TransportWithholdingShare)} → %{FormatPercentageInput(proposed.TransportWithholdingShare)}" + Environment.NewLine +
            $"Rehber tevkifat payı: %{FormatPercentageInput(previous.GuideWithholdingShare)} → %{FormatPercentageInput(proposed.GuideWithholdingShare)}" + Environment.NewLine +
            $"Damga vergisi: %{FormatPercentageInput(previous.StampTaxRate)} → %{FormatPercentageInput(proposed.StampTaxRate)}" + Environment.NewLine +
            $"Ek kesin teminat: %{FormatPercentageInput(previous.AdditionalGuaranteeRate)} → %{FormatPercentageInput(proposed.AdditionalGuaranteeRate)}" + Environment.NewLine + Environment.NewLine +
            $"İşlemi yapan: {changedBy}" + Environment.NewLine + $"Gerekçe: {reason}" + Environment.NewLine + Environment.NewLine +
            "Onaylanırsa yeni sabitler kalıcı kaydedilecek ve yeni hesaplamalarda kullanılacak. Mevcut geçmiş/arşiv tutarları denetim izi korunarak otomatik değiştirilmeyecek.",
            "OEHS — Hesaplama Sabitleri Onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        _calculationConstants = proposed;
        _calculationConstantsHistory.Add(new ProgressPaymentCalculationConstantsChange(
            DateTime.UtcNow, changedBy, reason, previous, proposed));
        if (!SavePersistedData())
        {
            _calculationConstants = previous;
            _calculationConstantsHistory.RemoveAt(_calculationConstantsHistory.Count - 1);
            PopulateCalculationConstantsPanel();
            return;
        }

        PopulateCalculationConstantsPanel();
        CalculationConstantsReasonTextBox.Clear();
        RefreshCalculationTraceLive();
        NightMessageBox.Show("Sabitler kaydedildi. Yeni hesaplamalar güncel sabitleri kullanacak; geçmiş hakedişler yalnızca açık düzeltme/yeniden hesaplama işleminde değişir.",
            "OEHS — Sabitler Kaydedildi", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ResetCalculationConstantsFieldsButton_Click(object sender, RoutedEventArgs e)
    {
        var defaults = ProgressPaymentCalculationConstants.Defaults;
        TransportVatRateTextBox.Text = FormatPercentageInput(defaults.TransportVatRate);
        GuideVatRateTextBox.Text = FormatPercentageInput(defaults.GuideVatRate);
        TransportWithholdingShareTextBox.Text = FormatPercentageInput(defaults.TransportWithholdingShare);
        GuideWithholdingShareTextBox.Text = FormatPercentageInput(defaults.GuideWithholdingShare);
        StampTaxRateTextBox.Text = FormatPercentageInput(defaults.StampTaxRate);
        AdditionalGuaranteeRateTextBox.Text = FormatPercentageInput(defaults.AdditionalGuaranteeRate);
        NightMessageBox.Show("Kaynak Excel başlangıç değerleri forma yüklendi; kaydetmek için yine gerekçeli onay gerekir.",
            "OEHS — Başlangıç Değerleri", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LoadSignatoriesIntoFields()
    {
        var signatories = OehsOfficialDocumentSettingsStore.Load();
        SignatoryPreparerNameTextBox.Text = signatories.PreparerName;
        SignatoryPreparerRoleTextBox.Text = signatories.PreparerRole;
        SignatoryControllerNameTextBox.Text = signatories.ControllerName;
        SignatoryControllerRoleTextBox.Text = signatories.ControllerRole;
        SignatoryApproverNameTextBox.Text = signatories.ApproverName;
        SignatoryApproverRoleTextBox.Text = signatories.ApproverRole;
    }

    private void LoadSignatoriesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadSignatoriesIntoFields();
            NightMessageBox.Show(
                "Kayıtlı imzacı bilgileri yeniden yüklendi.",
                "OEHS — İmzacı Ayarları",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            NightMessageBox.Show(
                ex.Message,
                "OEHS — İmzacı Ayarları Okunamadı",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveSignatoriesButton_Click(object sender, RoutedEventArgs e)
    {
        var values = new[]
        {
            SignatoryPreparerNameTextBox.Text,
            SignatoryPreparerRoleTextBox.Text,
            SignatoryControllerNameTextBox.Text,
            SignatoryControllerRoleTextBox.Text,
            SignatoryApproverNameTextBox.Text,
            SignatoryApproverRoleTextBox.Text
        };

        if (values.Any(string.IsNullOrWhiteSpace))
        {
            NightMessageBox.Show(
                "Lütfen üç imzacının da adını ve görev/unvan bilgisini doldurun. Boş alanla kayıt yapılmadı.",
                "OEHS — Eksik İmza Bilgisi",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (values.Any(value => value.Trim().Length > 150))
        {
            NightMessageBox.Show(
                "İsim ve görev/unvan alanları 150 karakteri aşamaz.",
                "OEHS — İmza Bilgisi Çok Uzun",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var signatories = new OehsOfficialDocumentSignatories(
                PreparerName: values[0].Trim(),
                PreparerRole: values[1].Trim(),
                ControllerName: values[2].Trim(),
                ControllerRole: values[3].Trim(),
                ApproverName: values[4].Trim(),
                ApproverRole: values[5].Trim());

            OehsOfficialDocumentSettingsStore.Save(signatories);
            LoadSignatoriesIntoFields();

            NightMessageBox.Show(
                "İmzacı bilgileri kaydedildi. Yeni oluşturulacak dört resmî belge bu bilgileri kullanacaktır. Önceden oluşturulmuş PDF dosyaları otomatik değişmez; gerekiyorsa yeniden oluşturun.",
                "OEHS — İmzacı Ayarları Kaydedildi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            NightMessageBox.Show(
                "İmzacı ayarları kaydedilemedi. Dosya izinlerini ve ayar dosyasını kontrol edin.\n\n" + ex.Message,
                "OEHS — Kayıt Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

}
