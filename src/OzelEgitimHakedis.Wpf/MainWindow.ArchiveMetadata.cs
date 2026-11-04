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
    private static string SafeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return string.IsNullOrWhiteSpace(value) ? "OEHS" : value;
    }

    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    private static string BuildArchiveKey(ProgressPaymentHistoryData item)
    {
        var tender = SafeFileName(string.IsNullOrWhiteSpace(item.TenderNumber) ? "Ihale" : item.TenderNumber);
        var contractor = OehsFileNameService.BuildContractorFilePart(
            item.ContractorName,
            item.ContractorTaxOrIdentityNumber);
        var route = SafeFileName(string.IsNullOrWhiteSpace(item.RouteNumber) ? "Hat" : item.RouteNumber);
        var progress = item.ProgressPaymentNumber?.ToString(CultureInfo.InvariantCulture) ?? "Yeni";
        var period = item.PeriodDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Donemsiz";
        return $"{tender}_Yuklenici-{contractor}_{route}_Hakedis-{progress}_{period}_{item.Id:N}";
    }

    private static string SaveRouteDocumentsArchiveMetadata(
        ProgressPaymentHistoryData latest,
        IReadOnlyList<string> pdfPaths)
    {
        ArgumentNullException.ThrowIfNull(latest);
        ArgumentNullException.ThrowIfNull(pdfPaths);
        if (pdfPaths.Count is not (1 or 4))
            throw new ArgumentException("Arşiv meta verisi için bir birleşik PDF veya dört eski PDF gerekir.", nameof(pdfPaths));

        var documentFolder = Path.GetDirectoryName(Path.GetFullPath(pdfPaths[0]))!;
        Directory.CreateDirectory(documentFolder);

        var fileNames = pdfPaths.Select(Path.GetFileName).ToArray();
        var metadataSuffix = latest.ProgressPaymentNumber.HasValue && latest.PeriodDate.HasValue
            ? $"_{latest.ProgressPaymentNumber.Value:00}_{latest.PeriodDate.Value:yyyy-MM-dd}"
            : "_Yeni";

        var metadataPath = Path.Combine(documentFolder, $"OEHS_PDF_Bilgi{metadataSuffix}.json");
        var metadata = JsonSerializer.Serialize(new
        {
            latest.Id,
            latest.ProgressPaymentNumber,
            latest.PeriodDate,
            latest.RouteNumber,
            latest.RouteName,
            latest.WorkName,
            latest.ContractorName,
            latest.BankAccountNumber,
            TransportDailyPrice = latest.Input.DailyPrice,
            PdfFileName = fileNames[0],
            PdfFileNames = fileNames,
            PdfTargetPaths = pdfPaths.ToArray(),
            PdfGenerator = "OEHS FixedDocument / PdfVectorWriter (DoğruTemin port)",
            Documents = OfficialDocumentNames,
            SavedAt = DateTime.Now
        }, JsonWriteOptions);

        var tempPath = metadataPath + ".tmp";
        File.WriteAllText(tempPath, metadata);
        File.Move(tempPath, metadataPath, overwrite: true);
        return documentFolder;
    }

    private void ShowPdfSaveInfoDialog(string targetFolder, string fileName, string targetPath)
    {
        var dialog = new Window
        {
            Title = "OEHS — PDF Kayıt Bilgisi",
            Owner = this,
            Width = 680,
            Height = fileName.Contains(Environment.NewLine, StringComparison.Ordinal) ? 470 : 390,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = NightTheme.Surface
        };

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = "PDF BAŞARIYLA KAYDEDİLDİ",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = NightTheme.Text
        });
        panel.Children.Add(new TextBlock
        {
            Text = fileName.Contains(Environment.NewLine, StringComparison.Ordinal)
                ? "Dört resmî belge tek PDF dosyasında oluşturuldu. Yazıcı kullanılmadı; dosyalar vektör tabanlı belge motoruyla OEHS kayıt klasörüne kaydedildi."
                : "Dört belge belirtilen sırayla tek PDF dosyasında oluşturuldu. Bu işlemde yazıcı kullanılmadı; dosya doğrudan OEHS kayıt klasörüne kaydedildi.",
            FontSize = 13.5,
            Foreground = NightTheme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 16)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "KAYIT KLASÖRÜ",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = NightTheme.Accent
        });
        panel.Children.Add(new TextBlock
        {
            Text = targetFolder,
            FontSize = 13.5,
            Foreground = NightTheme.Text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12)
        });
        panel.Children.Add(new TextBlock
        {
            Text = fileName.Contains(Environment.NewLine, StringComparison.Ordinal) ? "OLUŞTURULAN PDF DOSYALARI" : "ÖNERİLEN DOSYA ADI",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = NightTheme.Accent
        });
        panel.Children.Add(new TextBlock
        {
            Text = fileName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = NightTheme.Text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 18)
        });

        panel.Children.Add(new TextBlock
        {
            Text = fileName.Contains(Environment.NewLine, StringComparison.Ordinal)
                ? $"İlk PDF dosyası: {targetPath}"
                : $"Oluşturulan dosya: {targetPath}",
            FontSize = 11.5,
            Foreground = NightTheme.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var folderButton = new Button
        {
            Content = "KLASÖRÜ AÇ",
            Width = 150,
            Height = 42,
            Style = (Style)FindResource("SecondaryButton"),
            Margin = new Thickness(0, 0, 10, 0)
        };
        folderButton.Click += (_, _) =>
        {
            Directory.CreateDirectory(targetFolder);
            Process.Start(new ProcessStartInfo { FileName = targetFolder, UseShellExecute = true });
            dialog.DialogResult = true;
            dialog.Close();
        };

        var okButton = new Button
        {
            Content = "TAMAM",
            Width = 120,
            Height = 42,
            Style = (Style)FindResource("ModernButton")
        };
        okButton.Click += (_, _) =>
        {
            dialog.DialogResult = true;
            dialog.Close();
        };

        buttons.Children.Add(folderButton);
        buttons.Children.Add(okButton);
        panel.Children.Add(buttons);

        dialog.Content = panel;
        dialog.ShowDialog();
    }

    private static int ExtractArchiveRevision(string path)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            Path.GetFileNameWithoutExtension(path),
            @"_v(?<revision>\d+)$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success &&
               int.TryParse(
                   match.Groups["revision"].Value,
                   System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var revision)
            ? revision
            : 1;
    }

}
