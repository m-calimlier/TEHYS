using OzelEgitimHakedis.Core.Services;
using Xunit;

namespace OzelEgitimHakedis.Tests;

public sealed class DesktopStorageMigrationTests
{
    [Fact]
    public void PreviousDesktopWorkspace_IsMigratedIntoTehysWithoutReplacingCurrentRecords()
    {
        using var f = new Fixture();
        var desktop = Path.GetDirectoryName(f.Paths.WorkspaceRoot)!;
        var legacy = Path.Combine(desktop, "OzelEgitimHakedisSistemi");
        f.Write(legacy, "Kayitlar/oehs-data.json", "old");
        f.Write(legacy, "Belgeler/Arsiv/report.pdf", "report");
        f.Write(legacy, "Yedekler/backup.zip", "backup");
        f.Write(f.Paths.DataRoot, "oehs-data.json", "current");

        OehsDesktopStorageMigration.Ensure(f.Paths, f.Local, f.Roaming);

        Assert.Equal(Path.Combine(desktop, "TEHYS"), f.Paths.WorkspaceRoot);
        Assert.Equal("current", File.ReadAllText(f.Paths.ApplicationDataFilePath));
        Assert.Equal("report", File.ReadAllText(Path.Combine(f.Paths.DesktopRoot, "Arsiv", "report.pdf")));
        Assert.Equal("backup", File.ReadAllText(Path.Combine(f.Paths.WorkspaceRoot, "Yedekler", "backup.zip")));
        Assert.Contains(Directory.GetFiles(Path.Combine(f.Paths.WorkspaceRoot, "Yedekler"), "*", SearchOption.AllDirectories),
            path => File.ReadAllText(path) == "old");
    }

    [Fact]
    public void PreviousDesktopRecords_TakePriorityOverOlderAppDataCopies()
    {
        using var f = new Fixture();
        var desktop = Path.GetDirectoryName(f.Paths.WorkspaceRoot)!;
        f.Write(Path.Combine(desktop, "OzelEgitimHakedisSistemi"), "Kayitlar/oehs-data.json", "desktop");
        f.Write(f.Local, "oehs-data.json", "older AppData");

        OehsDesktopStorageMigration.Ensure(f.Paths, f.Local, f.Roaming);

        Assert.Equal("desktop", File.ReadAllText(f.Paths.ApplicationDataFilePath));
        Assert.Contains(Directory.GetFiles(Path.Combine(f.Paths.WorkspaceRoot, "Yedekler"), "*", SearchOption.AllDirectories),
            path => File.ReadAllText(path) == "older AppData");
    }

    [Fact]
    public void Upgrade_MovesAndVerifiesOldRecordsSettingsAndDocumentsWithoutResettingThem()
    {
        using var f = new Fixture();
        f.Write(f.Local, "oehs-special-education.json", "old-records");
        f.Write(f.Roaming, "window-layout.json", "old-settings");
        f.Write(f.Paths.WorkspaceRoot, "Ciktilar/old.pdf", "old-pdf");
        f.Write(f.Paths.WorkspaceRoot, "Arsiv/Hakedisler/old.json", "old-archive");
        OehsDesktopStorageMigration.Ensure(f.Paths, f.Local, f.Roaming);
        Assert.Equal("old-records", File.ReadAllText(Path.Combine(f.Paths.DataRoot, "oehs-special-education.json")));
        Assert.Equal("old-settings", File.ReadAllText(Path.Combine(f.Paths.DataRoot, "window-layout.json")));
        Assert.Equal("old-pdf", File.ReadAllText(Path.Combine(f.Paths.DesktopRoot, "Ciktilar", "old.pdf")));
        Assert.Equal("old-archive", File.ReadAllText(Path.Combine(f.Paths.ArchiveFolderPath, "old.json")));
        Assert.False(Directory.Exists(f.Local)); Assert.False(Directory.Exists(f.Roaming));
        var count = Directory.GetFiles(f.Paths.WorkspaceRoot, "*", SearchOption.AllDirectories).Length;
        OehsDesktopStorageMigration.Ensure(f.Paths, f.Local, f.Roaming);
        Assert.Equal(count, Directory.GetFiles(f.Paths.WorkspaceRoot, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void ConflictingOldFile_IsRetainedInDesktopBackupWhileCurrentFileWins()
    {
        using var f = new Fixture();
        f.Write(f.Local, "oehs-special-education.json", "old");
        f.Write(f.Paths.DataRoot, "oehs-special-education.json", "current");
        OehsDesktopStorageMigration.Ensure(f.Paths, f.Local, f.Roaming);
        Assert.Equal("current", File.ReadAllText(Path.Combine(f.Paths.DataRoot, "oehs-special-education.json")));
        Assert.Contains(Directory.GetFiles(Path.Combine(f.Paths.WorkspaceRoot, "Yedekler"), "*", SearchOption.AllDirectories),
            path => File.ReadAllText(path) == "old");
    }

    [Fact]
    public void OrphanedRecoveryCopies_AreMovedEvenWhenTheOldLiveFolderNoLongerExists()
    {
        using var f=new Fixture();
        var parent=Path.GetDirectoryName(f.Local)!;
        f.Write(parent,"OEHS.before-123/old.json","previous records");
        f.Write(parent,"OEHS-Recovery-123.oehs-backup.zip","recovery copy");
        Assert.False(Directory.Exists(f.Local));
        OehsDesktopStorageMigration.Ensure(f.Paths,f.Local,f.Roaming);
        Assert.False(File.Exists(Path.Combine(parent,"OEHS-Recovery-123.oehs-backup.zip")));
        Assert.Contains(Directory.GetFiles(Path.Combine(f.Paths.WorkspaceRoot,"Yedekler"),"*",SearchOption.AllDirectories),
            path=>File.ReadAllText(path)=="recovery copy");
    }

    [Fact]
    public void FailedMigration_PreservesAllOriginalFiles()
    {
        using var f = new Fixture();
        f.Write(f.Local, "oehs-special-education.json", "records");
        f.Write(f.Paths.WorkspaceRoot, "Ciktilar/old.pdf", "pdf");
        f.Write(f.Paths.WorkspaceRoot, "Belgeler", "blocked directory");
        Assert.ThrowsAny<IOException>(() => OehsDesktopStorageMigration.Ensure(f.Paths, f.Local, f.Roaming));
        Assert.Equal("records", File.ReadAllText(Path.Combine(f.Local, "oehs-special-education.json")));
        Assert.Equal("pdf", File.ReadAllText(Path.Combine(f.Paths.WorkspaceRoot, "Ciktilar", "old.pdf")));
    }

    [Fact]
    public void OverlappingLegacyRoot_IsRejectedBeforeAnyFileChanges()
    {
        using var f = new Fixture(); f.Write(f.Paths.WorkspaceRoot, "sentinel.txt", "keep");
        Assert.Throws<IOException>(() => OehsDesktopStorageMigration.Ensure(f.Paths, f.Paths.WorkspaceRoot, f.Roaming));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(f.Paths.WorkspaceRoot, "sentinel.txt")));
    }

    [Fact]
    public void InstallingOverIsolatedPackage_ArchivesItsDataInsideDesktopBeforeMarkerRemoval()
    {
        using var f=new Fixture();
        var app=Path.Combine(Path.GetDirectoryName(f.Local)!, "OldPackage");
        f.Write(app,OehsRuntimePaths.TestPackageMarkerFileName,"isolated");
        f.Write(app,"DenemeVerisi/OEHS/oehs-special-education.json","old isolated records");
        OehsDesktopStorageMigration.Ensure(f.Paths,f.Local,f.Roaming,Path.Combine(app,"DenemeVerisi"));
        Assert.False(Directory.Exists(Path.Combine(app,"DenemeVerisi")));
        Assert.Contains(Directory.GetFiles(Path.Combine(f.Paths.WorkspaceRoot,"Yedekler"),"*",SearchOption.AllDirectories),
            path=>File.ReadAllText(path)=="old isolated records");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("tehys-desktop-migration-");
        public string Local => Path.Combine(_root.FullName, "Local", "OEHS");
        public string Roaming => Path.Combine(_root.FullName, "Roaming", "OEHS");
        public OehsRuntimePathSet Paths => OehsRuntimePaths.Resolve(Path.Combine(_root.FullName, "App"),
            Path.Combine(_root.FullName, "Local"), Path.Combine(_root.FullName, "Desktop"), false);
        public void Write(string root, string relative, string value)
        { var path=Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path,value); }
        public void Dispose() => _root.Delete(true);
    }
}
