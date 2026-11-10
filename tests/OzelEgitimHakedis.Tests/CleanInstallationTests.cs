using OzelEgitimHakedis.Core.Services;
using Xunit;

namespace OzelEgitimHakedis.Tests;

public sealed class CleanInstallationTests
{
    [Fact]
    public void CleanProfile_RemovesOnlyOwnedLocationsAndKeepsOtherFiles()
    {
        using var f=new Fixture();
        f.Write("Local/OEHS/oehs-special-education.json");
        f.Write("Roaming/OEHS/window-layout.json");
        f.Write("Desktop/OzelEgitimHakedisSistemi/Yedekler/old.zip");
        f.Write("Desktop/OzelEgitimHakedisSistemi.pre-reset-123/old.pdf");
        f.Write("Local/OEHS.before-123/old.json");
        f.Write("Local/OEHS-Recovery-20260101-123.oehs-backup.zip");
        f.Write("Desktop/unrelated.txt"); f.Write("Local/another-program/settings.json");
        OehsCleanInstallService.CleanProfile(f.Path("Local"),f.Path("Roaming"),f.Path("Desktop"));
        Assert.False(Directory.Exists(f.Path("Local/OEHS")));
        Assert.False(Directory.Exists(f.Path("Roaming/OEHS")));
        Assert.False(Directory.Exists(f.Path("Desktop/OzelEgitimHakedisSistemi")));
        Assert.False(Directory.Exists(f.Path("Desktop/OzelEgitimHakedisSistemi.pre-reset-123")));
        Assert.False(File.Exists(f.Path("Local/OEHS-Recovery-20260101-123.oehs-backup.zip")));
        Assert.True(File.Exists(f.Path("Desktop/unrelated.txt")));
        Assert.True(File.Exists(f.Path("Local/another-program/settings.json")));
    }

    [Fact]
    public void EmptyOrOverlappingProfileParents_AreRejectedBeforeDeletion()
    {
        using var f=new Fixture(); f.Write("Local/OEHS/keep.json");
        Assert.Throws<ArgumentException>(()=>OehsCleanInstallService.CleanProfile("",f.Path("Roaming"),f.Path("Desktop")));
        Assert.Throws<ArgumentException>(()=>OehsCleanInstallService.CleanProfile(f.Path("Local"),f.Path("Local/OEHS"),f.Path("Desktop")));
        Assert.True(File.Exists(f.Path("Local/OEHS/keep.json")));
    }

    [Fact]
    public void CustomInstallationFolder_OnlyKnownProgramFilesAreRemoved()
    {
        using var f=new Fixture(); f.Write("App/TEHYS.exe"); f.Write("App/notes.txt");
        f.Write("App/DenemeVerisi/old.json");
        OehsCleanInstallService.CleanProfile(f.Path("Local"),f.Path("Roaming"),f.Path("Desktop"),f.Path("App"));
        Assert.False(File.Exists(f.Path("App/TEHYS.exe")));
        Assert.False(Directory.Exists(f.Path("App/DenemeVerisi")));
        Assert.True(File.Exists(f.Path("App/notes.txt")));
    }

    [Fact]
    public void JunctionInsideOwnedFolder_DoesNotDeleteItsExternalTarget()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var f=new Fixture();
        f.Write("Outside/keep.txt"); f.Write("Local/OEHS/owned.txt");
        var link=f.Path("Local/OEHS/linked-folder");
        var start=new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments="/c mklink /J \""+link+"\" \""+f.Path("Outside")+"\"",
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true
        };
        using(var process=System.Diagnostics.Process.Start(start)!)
        { process.WaitForExit(); Assert.Equal(0,process.ExitCode); }
        OehsCleanInstallService.CleanProfile(f.Path("Local"),f.Path("Roaming"),f.Path("Desktop"));
        Assert.True(File.Exists(f.Path("Outside/keep.txt")));
        Assert.False(Directory.Exists(f.Path("Local/OEHS")));
    }

    private sealed class Fixture:IDisposable
    {
        private readonly DirectoryInfo _root=Directory.CreateTempSubdirectory("tehys-clean-install-");
        public string Path(string relative)=>System.IO.Path.Combine(_root.FullName,relative.Replace('/',System.IO.Path.DirectorySeparatorChar));
        public void Write(string relative){var path=Path(relative);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);File.WriteAllText(path,"test-only");}
        public void Dispose()=>_root.Delete(true);
    }
}
