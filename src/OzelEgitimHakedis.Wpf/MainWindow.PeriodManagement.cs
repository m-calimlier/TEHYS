using System.Globalization;
using System.IO;
using System.Windows;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

public partial class MainWindow
{
    private void RefreshPeriodManagementStatus()
    {
        try
        {
            ClosingPeriodTextBox.Clear();
            NewPeriodTextBox.Clear();
            StartNewPeriodButton.IsEnabled = true;
            PeriodManagementStatusText.Text = "Eski döneme dönmek için Yedeği İncele ve Geri Yükle işlemini kullanabilirsiniz.";
            var info = OehsPeriodTransitionService.ReadCurrentPeriod(OehsRuntimePaths.DataRoot);
            if (info is null) return;
            ClosingPeriodTextBox.Text = info.CurrentPeriod;
            var start = int.Parse(info.CurrentPeriod.AsSpan(0, 4), CultureInfo.InvariantCulture);
            NewPeriodTextBox.Text = FormattableString.Invariant($"{start + 1:0000}-{start + 2:0000}");
            PeriodManagementStatusText.Text = $"Aktif dönem: {info.CurrentPeriod}\nÖnceki dönem arşivi: {info.ArchivePath}\nEski döneme dönmek için Yedeği İncele ve Geri Yükle işlemini kullanın.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StartNewPeriodButton.IsEnabled = false;
            PeriodManagementStatusText.Text = "Dönem bilgisi okunamadı; yeni döneme geçiş durduruldu: " + ex.Message;
        }
    }

    private async void StartNewPeriod_Click(object sender, RoutedEventArgs e)
    {
        if (_dataLoadFailed || HasOpenWorkForRestore())
        {
            NightMessageBox.Show(this,
                "Önce açık düzenlemeleri kaydedin veya formları temizleyin. Veri yükleme hatası varsa dönem geçişi yapılamaz.",
                "TEHYS — Dönem Yönetimi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var oldPeriod = ClosingPeriodTextBox.Text.Trim();
        var newPeriod = NewPeriodTextBox.Text.Trim();
        try { OehsPeriodTransitionService.ValidatePeriods(oldPeriod, newPeriod); }
        catch (ArgumentException ex)
        {
            NightMessageBox.Show(this, ex.Message, "TEHYS — Eğitim Yılını Kontrol Edin", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Eski dönemin tam arşivini kaydet",
            Filter = "TEHYS dönem arşivi|*.oehs-backup.zip",
            FileName = $"TEHYS-{oldPeriod}-{DateTime.Now:yyyyMMdd-HHmmss}.oehs-backup.zip",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (File.Exists(dialog.FileName))
        {
            NightMessageBox.Show(this, "Var olan arşivin üzerine yazılmaz. Yeni bir dosya adı seçin.",
                "TEHYS — Arşiv Korundu", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var preserve = PreservePeriodSettingsCheckBox.IsChecked == true;
        var confirm = $"{oldPeriod} arşivlenecek, {newPeriod} boş başlatılacak.\n\n" +
            $"İKİ TAŞIMA TÜRÜ BİRLİKTE: {_contracts.Count} ihale, {_contractors.Count} firma, {_routes.Count} hat, {_progressHistory.Count} hakediş.\n" +
            "Taslaklar, PDF'ler ve arşiv kayıtları da eski dönem yedeğinde saklanacak. Yeni dönemde hesaplama sabitleri varsayılana dönecek.\n\n" +
            (preserve ? "İmzacı ve pencere ayarları korunacak.\n" : "İmzacı ve pencere ayarları varsayılana dönecek.\n") +
            $"Arşiv: {dialog.FileName}\n\nYedek doğrulanmadan mevcut kayıtlar kaldırılmaz. Devam edilsin mi?";
        if (NightMessageBox.Show(this, confirm, "TEHYS — Dönem Geçişini Onayla",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        IsEnabled = false;
        var committed = false;
        try
        {
            var service = new OehsPeriodTransitionService(OehsRuntimePaths.DataRoot, OehsRuntimePaths.DesktopRoot);
            var result = await Task.Run(() => service.Start(oldPeriod, newPeriod, dialog.FileName, preserve));
            committed = true;
            // A fresh window prevents old selections, caches or drafts from being saved into the new period.
            var nextWindow = new MainWindow();
            if (nextWindow._dataLoadFailed)
            {
                nextWindow.Close();
                throw new IOException("Yeni dönem verisi açılamadı. Programı kapatıp yeniden açın. Arşiv: " + result.ArchivePath);
            }
            NightMessageBox.Show(this,
                $"{oldPeriod} arşivlendi; {newPeriod} boş olarak hazırlandı.\n\nArşiv: {result.ArchivePath}\n\nEski döneme Yedeği İncele ve Geri Yükle ile dönebilirsiniz.",
                "TEHYS — Yeni Dönem Hazır", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.MainWindow = nextWindow;
            nextWindow.Show();
            Close();
        }
        catch (Exception ex)
        {
            // After any filesystem error require a clean reload before another save.
            _dataLoadFailed = true;
            SetHeaderSaveStatus("Kaydetme durduruldu — programı yeniden açın", isError: true);
            NightMessageBox.Show(this,
                (committed ? "Yeni dönem diskte hazır; eski ekran yeniden kullanılmayacak. Programı kapatıp yeniden açın.\n\n"
                    : "Dönem geçişi tamamlanamadı. Kayıtları ve varsa oluşturulan arşivi koruyun. Devam etmeden programı yeniden açın.\n\n") + ex.Message,
                "TEHYS — Dönem Geçişi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { if (!committed) IsEnabled = true; }
    }
}
