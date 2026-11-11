using OzelEgitimHakedis.Core.Services;
using Xunit;

namespace OzelEgitimHakedis.Tests;

public sealed class OehsRuntimePathsTests
{
    [Fact]
    public void IsolatedArchivesMustBeInsideFullBackupOutputRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "tehys-backup-paths");
        var paths = OehsRuntimePaths.Resolve(Path.Combine(root, "Package"), Path.Combine(root, "Local"), Path.Combine(root, "Desktop"), true);
        Assert.StartsWith(paths.DesktopRoot + Path.DirectorySeparatorChar, paths.ArchiveFolderPath, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void IsolatedTestPackage_ShouldUseOnlyItsOwnDataAndOutputFolders()
    {
        var packageRoot = Path.Combine(Path.GetTempPath(), "OEHS-Package", "Release");
        var localAppData = Path.Combine(Path.GetTempPath(), "OEHS-User", "AppData");
        var desktop = Path.Combine(Path.GetTempPath(), "OEHS-User", "Desktop");

        var paths = OehsRuntimePaths.Resolve(packageRoot, localAppData, desktop, isolatedTestPackage: true);

        var testDataRoot = Path.Combine(Path.GetFullPath(packageRoot), "DenemeVerisi");
        Assert.Equal(Path.Combine(testDataRoot, "TEHYS", "Kayitlar"), paths.DataRoot);
        Assert.Equal(Path.Combine(testDataRoot, "TEHYS", "Kayitlar", "oehs-data.json"), paths.ApplicationDataFilePath);
        Assert.Equal(Path.Combine(testDataRoot, "TEHYS", "Belgeler", "Arsiv", "Hakedisler"), paths.ArchiveFolderPath);
        Assert.Equal(Path.Combine(testDataRoot, "TEHYS", "Belgeler"), paths.DesktopRoot);
        Assert.Equal(Path.Combine(testDataRoot, "TEHYS", "Kayitlar", "official-document-settings.json"), paths.OfficialDocumentSettingsFilePath);

        Assert.DoesNotContain(localAppData, paths.ApplicationDataFilePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(desktop, paths.ArchiveFolderPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(desktop, paths.DesktopRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StandardLaunch_ShouldKeepAllUserDataUnderDesktopWorkspace()
    {
        var packageRoot = Path.Combine(Path.GetTempPath(), "OEHS-Package");
        var localAppData = Path.Combine(Path.GetTempPath(), "OEHS-User", "AppData");
        var desktop = Path.Combine(Path.GetTempPath(), "OEHS-User", "Desktop");

        var paths = OehsRuntimePaths.Resolve(packageRoot, localAppData, desktop, isolatedTestPackage: false);

        var expectedDataRoot = Path.Combine(desktop, "TEHYS", "Kayitlar");
        var expectedDesktopRoot = Path.Combine(desktop, "TEHYS", "Belgeler");
        Assert.Equal(expectedDataRoot, paths.DataRoot);
        Assert.Equal(Path.Combine(expectedDataRoot, "oehs-data.json"), paths.ApplicationDataFilePath);
        Assert.Equal(Path.Combine(expectedDesktopRoot, "Arsiv", "Hakedisler"), paths.ArchiveFolderPath);
        Assert.Equal(expectedDesktopRoot, paths.DesktopRoot);
        Assert.Equal(Path.Combine(expectedDataRoot, "official-document-settings.json"), paths.OfficialDocumentSettingsFilePath);
    }
}
