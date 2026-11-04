using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace OzelEgitimHakedis.Wpf.Tests;

public sealed partial class HakedisCreateHybridUiContractTests
{
    [Fact]
    public void MidnightTheme_TableSelectionAndInputText_ShouldStayReadable()
    {
        RunOnSta(() =>
        {
            var root = LoadLayoutFragment(NamedElement(LoadMainWindowXaml(), "ProgressPanel"));
            var grid = new DataGrid { Style = (Style)root.FindResource("ModernDataGrid") };
            Assert.Equal("#FF09283F", ((SolidColorBrush)grid.Background).Color.ToString());
            var row = new DataGridRow { Style = grid.RowStyle };
            Assert.Equal("#FFF0F6FC", ((SolidColorBrush)row.Foreground).Color.ToString());
            row.IsSelected = true;
            Assert.Equal("#FF08223A", ((SolidColorBrush)row.Foreground).Color.ToString());
            var input = new TextBox { Style = (Style)root.FindResource("FieldTextBox") };
            Assert.Equal("#FFF0F6FC", ((SolidColorBrush)input.Foreground).Color.ToString());
        });
    }
}
