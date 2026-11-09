using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace OzelEgitimHakedis.Wpf.Tests;
public sealed class PracticalHomeTests
{
    private static string Root
    {
        get { var dir = new DirectoryInfo(AppContext.BaseDirectory); while(dir is not null && !File.Exists(Path.Combine(dir.FullName,"OzelEgitimHakedisSistemi.sln"))) dir=dir.Parent; return dir!.FullName; }
    }
    [Fact] public void Home_ContainsOnlyContextWorkListAndSelectedPaymentControl()
    {
        var doc=XDocument.Load(Path.Combine(Root,"src/OzelEgitimHakedis.Wpf/MainWindow.xaml"));
        XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";
        var home=doc.Descendants().Single(e=>(string?)e.Attribute(x+"Name")=="HomePanel");
        foreach(var name in new[]{"HomePeriodMonthComboBox","HomeMonthlyGrid","HomePreflightPanel","HomeSavePdfsButton"})
            Assert.Contains(home.Descendants(),e=>(string?)e.Attribute(x+"Name")==name);
        Assert.DoesNotContain(doc.Descendants(), e => (string?)e.Attribute(x+"Name") is "HomePreviewButton" or "PreviewReportsButton");
        Assert.DoesNotContain("HESAPLAMA MOTORU",home.ToString(),StringComparison.Ordinal);
        Assert.DoesNotContain("İŞ AKIŞI / İLERLEME ŞABLONU",home.ToString(),StringComparison.Ordinal);
        Assert.DoesNotContain("HomeVatTextBlock",home.ToString(),StringComparison.Ordinal);
    }
    [Fact] public void FiltersAndBackup_AreReachableWithoutNewNavigationModules()
    {
        var text=File.ReadAllText(Path.Combine(Root,"src/OzelEgitimHakedis.Wpf/MainWindow.xaml"));
        foreach(var name in new[]{"HistorySearchBox","ArchiveSearchBox","ArchiveShowRevisions","BackupCreateButton","BackupRestoreButton"})
            Assert.Contains("x:Name=\""+name+"\"",text,StringComparison.Ordinal);
    }
}
