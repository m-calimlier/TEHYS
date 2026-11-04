using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;
using Xunit;

namespace OzelEgitimHakedis.Wpf.Tests;

public sealed partial class HakedisCreateHybridUiContractTests
{
    [Fact]
    public void VisualProposal_ShouldPreserveEveryNamedControlPosition()
    {
        RunOnSta(() =>
        {
            foreach (var target in new[] { "HomePanel", "ProgressPanel", "ReportsPanel", "RecordsPanel" })
            {
                var layout = LoadLayoutFragment(NamedElement(LoadMainWindowXaml(), "AppShellBorder"));
                foreach (var name in new[] { "HomePanel", "ProgressPanel", "ReportsPanel", "RecordsPanel" })
                    ((Grid)layout.FindName(name)).Visibility = name == target ? Visibility.Visible : Visibility.Collapsed;
                var values = new Dictionary<string, string>
                {
                    ["HomeNetValue"] = "207.012,10 ₺", ["HomeGrossValue"] = "219.312,85 ₺",
                    ["HomeDeductionsValue"] = "12.300,77 ₺", ["HomeVatValue"] = "19.937,53 ₺",
                    ["HomeDifferenceValue"] = "7.365,35 ₺", ["HomeTransportValue"] = "192.009,97 ₺", ["HomeRoutesValue"] = "4 / 6",
                    ["HomeFirmsValue"] = "3 firma", ["HomeRoutesCaption"] = "2 hat için kayıt yok"
                };
                foreach (var pair in values) if (layout.FindName(pair.Key) is TextBlock text) text.Text = pair.Value;
                ArrangeLayout(layout, 1366, 768);
                SavePaymentDetailsPreview(layout, "preserved-" + target + "-before.png");
                var controls = VisualDescendants<FrameworkElement>(layout).Where(e => !string.IsNullOrEmpty(e.Name) && e.IsVisible)
                    .ToDictionary(e => e, e => e.TransformToAncestor(layout).TransformBounds(new Rect(e.RenderSize)));
                ApplyProposalColors(layout);
                ArrangeLayout(layout, 1366, 768);
                foreach (var pair in controls)
                    Assert.Equal(pair.Value, pair.Key.TransformToAncestor(layout).TransformBounds(new Rect(pair.Key.RenderSize)));
                SavePaymentDetailsPreview(layout, "preserved-" + target + "-after.png");
            }
            var input = new ProgressPaymentInput(18, 1000, 0, 100, 100000, false, 0, 0, 0, GuideWorkDays: 18, GuideDailyPrice: 100);
            var period = new ProgressPaymentMonthOption(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
            var dialog = new PaymentPeriodChangeWindow(input, period, "Hat 05 · Cumhuriyet İlkokulu");
            var body = (FrameworkElement)dialog.Content;
            ArrangeLayout(body, 920, 650);
            SavePaymentDetailsPreview(body, "preserved-period-before.png");
            ApplyProposalColors(body);
            ArrangeLayout(body, 920, 650);
            SavePaymentDetailsPreview(body, "preserved-period-after.png");
            dialog.Close();
        });
    }

    private static void ApplyProposalColors(FrameworkElement root)
    {
        Brush? Map(Brush? brush)
        {
            if (brush is SolidColorBrush solid && solid.Color.R > 160 && solid.Color.G > 90 && solid.Color.G < 220 && solid.Color.B < 115)
                return new SolidColorBrush(Color.FromRgb(210, 177, 107));
            return brush;
        }
        foreach (var element in VisualDescendants<FrameworkElement>(root).Prepend(root))
        {
            if (element is Border border)
            {
                border.Background = Map(border.Background);
                if (border.BorderBrush is SolidColorBrush line && line.Color.R > 130 && line.Color.G > 70 && line.Color.B < 100)
                    border.BorderBrush = new SolidColorBrush(Color.FromRgb(87, 103, 121));
            }
            if (element is TextBlock text) text.Foreground = Map(text.Foreground);
            if (element is Button button && !button.Name.StartsWith("Nav", StringComparison.Ordinal) && button.Background is SolidColorBrush fill && fill.Color.R > 180 && fill.Color.G > 180 && fill.Color.B > 180)
            {
                button.Background = new SolidColorBrush(Color.FromRgb(226, 234, 243));
                button.Foreground = new SolidColorBrush(Color.FromRgb(16, 38, 61));
                button.BorderBrush = new SolidColorBrush(Color.FromRgb(114, 135, 156));
            }
        }
    }
}
