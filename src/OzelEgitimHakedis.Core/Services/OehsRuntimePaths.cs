using System;
using System.IO;

namespace OzelEgitimHakedis.Core.Services;

/// <summary>
/// Resolves OEHS persistence and output locations. A package with the explicit marker
/// runs in an isolated folder so trial data never mixes with the user's normal OEHS data.
/// </summary>
public static class OehsRuntimePaths
{
    public const string ProductName = "TEHYS";
    internal const string LegacyDesktopFolderName = "OzelEgitimHakedisSistemi";
    public const string TestPackageMarkerFileName = "OEHS_DENEME_PAKETI.flag";

    private static readonly OehsRuntimePathSet Current = Resolve(
        AppContext.BaseDirectory,
        GetLocalApplicationDataDirectory(),
        GetDesktopDirectory(),
        File.Exists(Path.Combine(AppContext.BaseDirectory, TestPackageMarkerFileName)));

    public static string DataRoot => Current.DataRoot;
    public static string ApplicationDataFilePath => Current.ApplicationDataFilePath;
    public static string ArchiveFolderPath => Current.ArchiveFolderPath;
    public static string DesktopRoot => Current.DesktopRoot;
    public static string OfficialDocumentSettingsFilePath => Current.OfficialDocumentSettingsFilePath;
    public static string WorkspaceRoot => Current.WorkspaceRoot;
    public static string BackupFolderPath => Path.Combine(WorkspaceRoot, "Yedekler");
    public static string WorkingFolderPath => Path.Combine(WorkspaceRoot, "Calisma");

    public static void PrepareDesktopStorage(string? installationDirectory = null)
    {
        if (IsIsolatedTestPackage) return;
        OehsDesktopStorageMigration.Ensure(Current,
            Path.Combine(GetLocalApplicationDataDirectory(), "OEHS"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OEHS"),
            installationDirectory is not null && File.Exists(Path.Combine(installationDirectory, TestPackageMarkerFileName))
                ? Path.Combine(installationDirectory, "DenemeVerisi") : null);
    }
    public static bool IsIsolatedTestPackage =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, TestPackageMarkerFileName));

    public static OehsRuntimePathSet Resolve(
        string baseDirectory,
        string localApplicationDataDirectory,
        string desktopDirectory,
        bool isolatedTestPackage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var normalizedBaseDirectory = Path.GetFullPath(baseDirectory);
        if (isolatedTestPackage)
        {
            var testDataRoot = Path.Combine(normalizedBaseDirectory, "DenemeVerisi");
            var legacyDataRoot = Path.Combine(testDataRoot, "OEHS");
            var dataRoot = Directory.Exists(legacyDataRoot) ? legacyDataRoot : Path.Combine(testDataRoot, ProductName, "Kayitlar");
            var legacyDesktopRoot = Path.Combine(testDataRoot, LegacyDesktopFolderName);
            var desktopRoot = Directory.Exists(legacyDesktopRoot) ? legacyDesktopRoot : Path.Combine(testDataRoot, ProductName, "Belgeler");
            return new OehsRuntimePathSet(
                DataRoot: dataRoot,
                ApplicationDataFilePath: Path.Combine(dataRoot, "oehs-data.json"),
                ArchiveFolderPath: Path.Combine(desktopRoot, "Arsiv", "Hakedisler"),
                DesktopRoot: desktopRoot,
                OfficialDocumentSettingsFilePath: Path.Combine(dataRoot, "official-document-settings.json"));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopDirectory);

        var workspace = Path.Combine(desktopDirectory, ProductName);
        var normalDataRoot = Path.Combine(workspace, "Kayitlar");
        var normalDesktopRoot = Path.Combine(workspace, "Belgeler");
        return new OehsRuntimePathSet(
            DataRoot: normalDataRoot,
            ApplicationDataFilePath: Path.Combine(normalDataRoot, "oehs-data.json"),
            ArchiveFolderPath: Path.Combine(normalDesktopRoot, "Arsiv", "Hakedisler"),
            DesktopRoot: normalDesktopRoot,
            OfficialDocumentSettingsFilePath: Path.Combine(normalDataRoot, "official-document-settings.json"));
    }

    private static string GetLocalApplicationDataDirectory()
    {
        var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(path))
            return path;

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
    }

    private static string GetDesktopDirectory()
    {
        var path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrWhiteSpace(path))
            return path;

        throw new IOException("Masaüstü klasörü bulunamadı. Klasör erişimini düzeltmeden program kullanılamaz.");
    }
}

public sealed record OehsRuntimePathSet(
    string DataRoot,
    string ApplicationDataFilePath,
    string ArchiveFolderPath,
    string DesktopRoot,
    string OfficialDocumentSettingsFilePath)
{
    public string WorkspaceRoot => Path.GetDirectoryName(DataRoot)!;
}
