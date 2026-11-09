using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace OzelEgitimHakedis.Wpf.Tests;

public sealed partial class HakedisCreateHybridUiContractTests
{
    [Theory]
    [InlineData(847, 622)]
    [InlineData(1113, 670)]
    [InlineData(1667, 982)]
    public void PeriodManagement_FitsSettingsAndPreservesSettingsByDefault(double width, double height)
    {
        RunOnSta(() =>
        {
            var layout = LoadLayoutFragment(NamedElement(LoadMainWindowXaml(), "SettingsPanel"));
            ArrangeLayout(layout, width, height);
            var preserve = (CheckBox)layout.FindName("PreservePeriodSettingsCheckBox");
            Assert.True(preserve.IsChecked);
            foreach (var name in new[] { "ClosingPeriodTextBox", "NewPeriodTextBox", "StartNewPeriodButton" })
            {
                var control = Assert.IsAssignableFrom<FrameworkElement>(layout.FindName(name));
                Assert.True(control.ActualWidth > 0);
                var bounds = control.TransformToAncestor(layout).TransformBounds(new Rect(control.RenderSize));
                Assert.InRange(bounds.Right, 1, width + 1);
            }
            Assert.Equal(9, ((TextBox)layout.FindName("ClosingPeriodTextBox")).MaxLength);
            Assert.Equal(9, ((TextBox)layout.FindName("NewPeriodTextBox")).MaxLength);
            SavePaymentDetailsPreview(layout, "period-management-" + width.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + ".png");
        });
    }
}
