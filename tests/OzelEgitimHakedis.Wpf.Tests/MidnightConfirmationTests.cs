using System.Windows;
using Xunit;
namespace OzelEgitimHakedis.Wpf.Tests;
public sealed class MidnightConfirmationTests
{
    [Theory]
    [InlineData(MessageBoxButton.YesNo, MessageBoxResult.No)]
    [InlineData(MessageBoxButton.YesNoCancel, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButton.OKCancel, MessageBoxResult.Cancel)]
    [InlineData(MessageBoxButton.OK, MessageBoxResult.OK)]
    public void ClosingAThemedConfirmation_ShouldNeverApproveAnAction(MessageBoxButton buttons, MessageBoxResult expected)
        => Assert.Equal(expected, NightMessageBox.ClosingResult(buttons));
}
