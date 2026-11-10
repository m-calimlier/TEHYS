using System.IO;
using Xunit;

namespace OzelEgitimHakedis.Wpf.Tests;

public sealed class SterileDistributionTests
{
    [Fact]
    public void ExcelDocuments_DoNotContainPreloadedInstitutionSignatories()
    {
        var folder=Directory.CreateTempSubdirectory("tehys-empty-excel-");
        try
        {
            var path=Path.Combine(folder.FullName,"test.xlsx");
            var payment=ArchiveProgressEditorContextTests.CreateArchive().ProgressPayment;
            ExcelDocumentExporter.Save(path,"01","Test Hat","Test Firma","Test İş",payment);
            using var zip=System.IO.Compression.ZipFile.OpenRead(path);
            using var reader=new StreamReader(zip.GetEntry("xl/worksheets/sheet3.xml")!.Open());
            var xml=reader.ReadToEnd();
            Assert.DoesNotContain("Aşkın KESKİNBALTA",xml,StringComparison.Ordinal);
            Assert.DoesNotContain("Halil ÖZENÇ",xml,StringComparison.Ordinal);
            Assert.DoesNotContain("Özgür BAYSAL",xml,StringComparison.Ordinal);
        }
        finally {folder.Delete(true);}
    }

    [Fact]
    public void FreshInstallation_HasNoPreloadedSignatoryNamesOrRoles()
    {
        var folder=Directory.CreateTempSubdirectory("tehys-empty-install-");
        try
        {
            var settings=OehsOfficialDocumentSettingsStore.Load(Path.Combine(folder.FullName,"settings.json"));
            Assert.All(new[]{settings.PreparerName,settings.PreparerRole,settings.ControllerName,settings.ControllerRole,
                settings.ApproverName,settings.ApproverRole},value=>Assert.True(string.IsNullOrWhiteSpace(value)));
        }
        finally {folder.Delete(true);}
    }

    [Fact]
    public void ExistingConfiguredSignatories_ArePreservedWhenLoading()
    {
        var folder=Directory.CreateTempSubdirectory("tehys-existing-settings-");
        try
        {
            var path=Path.Combine(folder.FullName,"settings.json");
            var settings=new OehsOfficialDocumentSignatories("Test Düzenleyen","Test Görev 1","Test Kontrol","Test Görev 2","Test Onay","Test Görev 3");
            OehsOfficialDocumentSettingsStore.Save(settings,path);
            Assert.Equal(settings,OehsOfficialDocumentSettingsStore.Load(path));
        }
        finally {folder.Delete(true);}
    }
}
