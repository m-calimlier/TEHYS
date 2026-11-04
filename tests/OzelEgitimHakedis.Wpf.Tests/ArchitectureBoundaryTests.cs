using Xunit;
namespace OzelEgitimHakedis.Wpf.Tests;
public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void Documents_ShouldBeIndependentOfDesktopShell()
    {
        var assembly=typeof(PdfVectorWriter).Assembly;
        Assert.Equal("OzelEgitimHakedis.Documents",assembly.GetName().Name);
        Assert.Equal(assembly,typeof(OehsOfficialDocumentFactory).Assembly);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(),a=>a.Name=="OEHS");
    }
    [Theory]
    [InlineData("ContractScreenModel")]
    [InlineData("PaymentScreenModel")]
    [InlineData("OutputScreenModel")]
    public void ScreenModels_ShouldBeIndependentOfWindows(string name)
    {
        var type=typeof(MainWindow).Assembly.GetType("OzelEgitimHakedis.Wpf.ScreenModels."+name);
        Assert.NotNull(type);
        Assert.False(typeof(System.Windows.DependencyObject).IsAssignableFrom(type));
    }
}
