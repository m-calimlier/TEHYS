namespace OzelEgitimHakedis.Core.Services;

public static class OehsFreshStartService
{
    // One-time clean-slate migration. Existing files are moved to a recoverable
    // backup, never permanently deleted by an automatic launch-time operation.
    public const string CurrentResetVersion = "2026-10-16-program-partitioned-storage-v3";

    public static bool EnsureFreshStart()
    {
        // Normal upgrades preserve existing records. Only an explicitly selected
        // installer clean installation may reset the user's desktop workspace.
        if (!OehsRuntimePaths.IsIsolatedTestPackage) return false;
        var reset = EnsureFreshStart(
            OehsRuntimePaths.DataRoot,
            OehsRuntimePaths.DesktopRoot);
        if (OehsRuntimePaths.IsIsolatedTestPackage)
            OehsIsolatedArchiveMigration.Ensure(AppContext.BaseDirectory);
        return reset;
    }

    public static bool EnsureFreshStart(string localDataRoot, string desktopRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localDataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopRoot);

        Directory.CreateDirectory(localDataRoot);

        var markerPath = Path.Combine(localDataRoot, ".fresh-start-version");
        if (File.Exists(markerPath))
        {
            var marker = File.ReadAllText(markerPath).Trim();
            if (string.Equals(marker, CurrentResetVersion, StringComparison.Ordinal))
                return false;
        }

        var dataFiles = new[]
        {
            Path.Combine(localDataRoot, "oehs-data.json"),
            Path.Combine(localDataRoot, "oehs-data.json.bak"),
            Path.Combine(localDataRoot, "oehs-data.json.tmp"),
            Path.Combine(localDataRoot, "oehs-special-education.json"),
            Path.Combine(localDataRoot, "oehs-special-education.json.bak"),
            Path.Combine(localDataRoot, "oehs-special-education.json.pending"),
            Path.Combine(localDataRoot, "oehs-special-education.json.pending.bak"),
            Path.Combine(localDataRoot, "oehs-special-education.json.rollback"),
            Path.Combine(localDataRoot, "oehs-basic-secondary.json"),
            Path.Combine(localDataRoot, "oehs-basic-secondary.json.bak"),
            Path.Combine(localDataRoot, "oehs-basic-secondary.json.pending"),
            Path.Combine(localDataRoot, "oehs-basic-secondary.json.pending.bak"),
            Path.Combine(localDataRoot, "oehs-basic-secondary.json.rollback")
        };

        var fullDesktopRoot = Path.GetFullPath(desktopRoot);
        var ownsDesktopRoot = Path.GetFileName(fullDesktopRoot)
            .Equals("OzelEgitimHakedisSistemi", StringComparison.OrdinalIgnoreCase);

        var existingDataFiles = dataFiles.Where(File.Exists).ToArray();
        var hasDesktopOutputs = ownsDesktopRoot && Directory.Exists(fullDesktopRoot);
        string? backupDataRoot = null;
        string? backupDesktopRoot = null;

        // Create the local backup first, and move rather than delete. Keep the
        // active filenames absent so the first launch starts with empty data.
        if (existingDataFiles.Length > 0)
        {
            backupDataRoot = Path.Combine(localDataRoot, "reset-backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupDataRoot);

            foreach (var file in existingDataFiles)
                File.Move(file, Path.Combine(backupDataRoot, Path.GetFileName(file)));
        }

        if (hasDesktopOutputs)
        {
            // A sibling path keeps Directory.Move on the same volume and makes
            // all generated archives/PDFs recoverable without crossing volumes.
            backupDesktopRoot = fullDesktopRoot + ".pre-reset-" + Guid.NewGuid().ToString("N");
            Directory.Move(fullDesktopRoot, backupDesktopRoot);
        }

        var markerTemporaryPath = markerPath + ".tmp";
        File.WriteAllText(markerTemporaryPath, CurrentResetVersion);
        File.Move(markerTemporaryPath, markerPath, overwrite: true);

        return true;
    }
}
