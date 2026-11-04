using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

public sealed class PaymentCalculationDetailsWindow : Window
{
    public PaymentCalculationDetailsWindow(ProgressPaymentHistoryData payment, IEnumerable<ProgressPaymentHistoryData> history)
    {
        Title = "TEHYS — Hakediş hesap ayrıntıları";
        Width = 1080; Height = 720; MinWidth = 620; MinHeight = 400;
        MaxWidth = SystemParameters.WorkArea.Width; MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(8, 32, 57));
        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new TextBlock
        {
            Text = $"Hakediş {payment.ProgressPaymentNumber?.ToString(CultureInfo.InvariantCulture) ?? "—"} · Hat {payment.RouteNumber ?? "—"} · {payment.RouteName}\n{payment.ContractorName} · İKN {payment.TenderNumber} · Dönem {payment.PeriodDate?.ToString("MM.yyyy", CultureInfo.InvariantCulture) ?? "belirsiz"}\nNet ödeme: {payment.Calculation.NetPayable.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} TL",
            Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        };
        root.Children.Add(heading);
        var note = new TextBlock
        {
            Text = $"KDV istisnası: {(payment.Input.IsVatExempt ? "Evet" : "Hayır")}. Tutarlar kaydedilmiş hesap üzerinden tam hassasiyetle gösterilir; çıktılar iki ondalık basamaktır. Resmî rapor toplamları mevcut kayıt geçmişine göre açıklanır.\n" +
                   (payment.Calculation.AppliedConstants is null ? "Bu eski kayıtta kullanılan oranlar saklanmamış; güncel oranlar yerine geçirilmedi. Raporun eski kayıt uyumluluk hesabı varsayılan damga oranını kullanabilir." : "Oranlar bu hakedişle birlikte kaydedilen oranlardır."),
            Foreground = new SolidColorBrush(Color.FromRgb(244, 194, 71)), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(note, 1); root.Children.Add(note);
        var grid = new DataGrid
        {
            Name = "CalculationDetailsGrid", IsReadOnly = true, AutoGenerateColumns = false,
            CanUserAddRows = false, CanUserDeleteRows = false, CanUserReorderColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column, Background=NightTheme.Surface, Foreground = NightTheme.Text,
            RowBackground=NightTheme.Surface, AlternatingRowBackground = NightTheme.Alternate,
            ItemsSource = PaymentCalculationDetailsService.Build(payment, history),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            EnableRowVirtualization = true, MinRowHeight = 36
        };
        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(7, 6, 7, 6)));
        foreach (var (title, path, width) in new[] { ("Bölüm", "Section", 110d), ("Hesap kalemi", "Name", 220d), ("İşlem / formül", "Formula", 400d), ("Değer (gün / % / TL)", "ValueText", 220d) })
            grid.Columns.Add(new DataGridTextColumn { Header = title, Binding = new Binding(path), Width = width, ElementStyle = textStyle });
        Grid.SetRow(grid, 2); root.Children.Add(grid);
        var close = new Button { Content = "Kapat", IsCancel = true, MinWidth = 110, Padding = new Thickness(14, 8, 14, 8), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 3); root.Children.Add(close);
        Content = root;
    }
}
