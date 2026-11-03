using System.Windows;
using System.Windows.Controls;

namespace OzelEgitimHakedis.Wpf;

public partial class MainWindow
{
    private void ViewHistoryCalculationButton_Click(object sender, RoutedEventArgs e)
    {
        var payment = ChooseHistoryMember((sender as Button)?.Tag);
        if (payment is null) return;
        new PaymentCalculationDetailsWindow(payment, _progressHistory.ToArray()) { Owner = this }.ShowDialog();
    }
}
