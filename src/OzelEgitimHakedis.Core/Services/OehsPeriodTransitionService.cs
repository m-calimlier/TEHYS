using System.Globalization;
using System.Text.Json;
using OzelEgitimHakedis.Core.Models;

namespace OzelEgitimHakedis.Core.Services;

public sealed record OehsPeriodInfo(string PreviousPeriod, string CurrentPeriod, DateTime StartedAtUtc, string ArchivePath);
public sealed record OehsPeriodTransitionResult(string ArchivePath, string RecoveryPath, BackupSummary ArchiveSummary);

/// <summary>Archives both programs before installing an empty period through the verified restore transaction.</summary>
public sealed class OehsPeriodTransitionService
{
    private readonly string _data;
    private readonly string _outputs;
    private static readonly string[] PreservedSettings =
        { "official-document-settings.json", "window-layout.json", "window-layout-preference.json" };

    public OehsPeriodTransitionService(string dataRoot, string outputRoot)
    {
        _data = Path.GetFullPath(dataRoot);
        _outputs = Path.GetFullPath(outputRoot);
        _ = new OehsBackupService(_data, _outputs); // Reject overlapping roots before doing any work.
    }

    public static void ValidatePeriods(string oldPeriod, string newPeriod)
    {
        static int StartYear(string value)
        {
            if (value is null || value.Length != 9 || value[4] != '-' ||
                !int.TryParse(value.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
                !int.TryParse(value.AsSpan(5, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var end) ||
                start < 1900 || start > 9997 || end != start + 1)
                throw new ArgumentException("Eğitim yılını 2025-2026 biçiminde, birbirini izleyen iki yıl olarak girin.");
            return start;
        }
        if (StartYear(newPeriod) != StartYear(oldPeriod) + 1)
            throw new ArgumentException("Yeni dönem, arşivlenecek eğitim yılından hemen sonraki yıl olmalıdır.");
    }

    public static OehsPeriodInfo? ReadCurrentPeriod(string dataRoot)
    {
        var path = Path.Combine(dataRoot, "period-info.json");
        if (!File.Exists(path)) return null;
        try
        {
            var info = JsonSerializer.Deserialize<OehsPeriodInfo>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Dönem bilgisi boş.");
            ValidatePeriods(info.PreviousPeriod, info.CurrentPeriod);
            return info;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        { throw new InvalidDataException("Kaydedilmiş dönem bilgisi okunamadı.", ex); }
    }

    public OehsPeriodTransitionResult Start(string oldPeriod, string newPeriod, string archivePath,
        bool preserveSettings, Action<string>? beforeReplace = null)
    {
        ValidatePeriods(oldPeriod, newPeriod);
        var current = ReadCurrentPeriod(_data);
        if (current is not null && current.CurrentPeriod != oldPeriod)
            throw new InvalidOperationException("Arşivlenecek dönem, kayıtlı aktif dönemle eşleşmiyor.");
        archivePath = Path.GetFullPath(archivePath);
        if (File.Exists(archivePath)) throw new IOException("Bu arşiv dosyası zaten var. Farklı bir dosya adı seçin.");
        var service = new OehsBackupService(_data, _outputs);
        // The package service rejects paths inside either live root.
        var pendingArchive = archivePath + "." + Guid.NewGuid().ToString("N") + ".pending";
        var stage = OehsBackupService.CreateWorkingDirectory(Path.Combine(Path.GetDirectoryName(_data)!, "Calisma"), "new-period");
        try
        {
            service.Create(pendingArchive);
            File.Move(pendingArchive, archivePath, overwrite: false);
            var summary = OehsBackupService.Inspect(archivePath);

            var newData = Path.Combine(stage, "data");
            var newOutputs = Path.Combine(stage, "outputs");
            new OehsProgramScopedApplicationDataStore(newData).Save(OehsApplicationData.Empty);
            File.WriteAllText(Path.Combine(newData, ".fresh-start-version"), OehsFreshStartService.CurrentResetVersion);
            File.WriteAllText(Path.Combine(newData, "period-info.json"), JsonSerializer.Serialize(
                new OehsPeriodInfo(oldPeriod, newPeriod, DateTime.UtcNow, archivePath)));
            if (preserveSettings)
                foreach (var name in PreservedSettings)
                    if (File.Exists(Path.Combine(_data, name)))
                        File.Copy(Path.Combine(_data, name), Path.Combine(newData, name));
            var emptyPackage = Path.Combine(stage, "new-period.oehs-backup.zip");
            new OehsBackupService(newData, newOutputs).Create(emptyPackage);
            // Restore stages both roots, creates a recovery package, and rolls back on failure.
            var recovery = service.Restore(emptyPackage, beforeReplace);
            return new(archivePath, recovery, summary);
        }
        finally
        {
            try { if (File.Exists(pendingArchive)) File.Delete(pendingArchive); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            try { Directory.Delete(stage, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
