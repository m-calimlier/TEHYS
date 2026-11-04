using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;
internal sealed class PaymentComparisonDialog : Window
{
    internal PaymentComparisonDialog(string title,string scope,IReadOnlyList<PaymentDifference> differences,string totals)
    {
        Title=title;Width=820;Height=560;MinWidth=580;MinHeight=380;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=NightTheme.Surface;
        var root=new Grid{Margin=new Thickness(20)};
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=new TextBlock{Text=scope,FontSize=15,Foreground=NightTheme.Text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
        root.Children.Add(heading);
        var table=new DataGrid{IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,ItemsSource=differences,
            Background=NightTheme.Surface,Foreground=NightTheme.Text,HeadersVisibility=DataGridHeadersVisibility.Column};
        foreach(var (label,path) in new[]{("Alan","Field"),("Önce","Before"),("Sonra","After")})
        {
            var textStyle=new Style(typeof(TextBlock));textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty,TextWrapping.Wrap));
            table.Columns.Add(new DataGridTextColumn{Header=label,Binding=new Binding(path),Width=new DataGridLength(1,DataGridLengthUnitType.Star),ElementStyle=textStyle});
        }
        Grid.SetRow(table,1);root.Children.Add(table);
        var summary=new TextBlock{Text=totals,FontSize=14,FontWeight=FontWeights.SemiBold,Foreground=NightTheme.Text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};
        Grid.SetRow(summary,2);root.Children.Add(summary);
        var buttons=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=new Button{Content="İptal",IsCancel=true,MinWidth=110,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0)};
        var confirm=new Button{Content="Değişiklikleri kaydet",MinWidth=180,Padding=new Thickness(12,8,12,8)};
        if (Application.Current?.TryFindResource("ModernButton") is Style primary) confirm.Style = primary;
        confirm.Click+=(_,_)=>DialogResult=true;buttons.Children.Add(cancel);buttons.Children.Add(confirm);
        Grid.SetRow(buttons,3);root.Children.Add(buttons);Content=root;
    }
}
