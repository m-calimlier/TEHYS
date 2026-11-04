using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

public partial class MainWindow
{
    private Dictionary<string,ArchiveDisplayRow[]>? _archiveGroupMembers;

    private List<ArchiveDisplayRow> GroupArchiveRows(IReadOnlyList<ArchiveDisplayRow> rows)
    {
        _archiveGroupMembers ??= new(StringComparer.OrdinalIgnoreCase);
        _archiveGroupMembers.Clear();
        var valid=new List<(ArchiveDisplayRow Row,ProgressPaymentArchive Archive)>();
        var invalid=new List<ArchiveDisplayRow>();
        foreach(var row in rows)
        {
            try {valid.Add((row,ProgressPaymentArchiveStore.Load(row.FullPath)));}
            catch(ProgressPaymentArchiveException) {invalid.Add(row);}
        }
        var showRevisions=ArchiveShowRevisions.IsChecked==true;
        if(!showRevisions)valid=valid.GroupBy(x=>(x.Archive.ProgressPayment.Id,x.Archive.ProgressPayment.Input.ProgramType))
            .Select(g=>g.OrderByDescending(x=>x.Archive.Revision).First()).ToList();
        var result=new List<ArchiveDisplayRow>();
        foreach(var group in valid.GroupBy(x=>(ProgressPaymentBatchService.GroupKey(x.Archive.ProgressPayment),Revision:showRevisions?x.Archive.Revision:0)))
        {
            var members=group.OrderBy(x=>x.Archive.ProgressPayment.RouteNumber,StringComparer.OrdinalIgnoreCase).ToArray();
            var first=members[0];
            var payments=members.Select(x=>x.Archive.ProgressPayment).ToArray();
            _archiveGroupMembers[first.Row.FullPath]=members.Select(x=>x.Row).ToArray();
            result.Add(members.Length==1?first.Row:first.Row with
            {
                DisplayTitle=GroupRecordTitle(payments),
                DetailText=$"{OutputPeriodText(payments[0])} • İKN: {payments[0].TenderNumber} • {payments.Length} hat",
                NetPayableText=FormatMoney(payments.Sum(p=>p.Calculation.NetPayable)),
                RevisionText=showRevisions?first.Row.RevisionText:"Güncel revizyonlar"
            });
        }
        result.AddRange(invalid);
        return result;
    }

    private static string GroupRecordTitle(IReadOnlyList<ProgressPaymentHistoryData> members) =>
        $"{members[0].ContractorName} • Hakediş No {members[0].ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? "—"} • {members.Count} hat";

    private ProgressPaymentHistoryData? ChooseHistoryMember(object? tag)
    {
        var payment=GetHistoryItemFromRow(tag);
        if(payment is null)return null;
        return PickRecordMember(GetOutputBatchPayments(payment),p=>$"Hat {p.RouteNumber} — {p.RouteName}");
    }

    private ArchiveRow? ChooseArchiveMember(ArchiveRow row)
    {
        if(_archiveGroupMembers is null || !_archiveGroupMembers.TryGetValue(row.FullPath,out var members) || members.Length<2)return row;
        var chosen=PickRecordMember(members,r=>
        {
            var p=ProgressPaymentArchiveStore.Load(r.FullPath).ProgressPayment;
            return $"Hat {p.RouteNumber} — {p.RouteName} ({r.RevisionText})";
        });
        return chosen is null?null:new ArchiveRow(chosen.FullPath,chosen.DisplayTitle,chosen.DetailText,chosen.NetPayableText,chosen.RevisionText);
    }

    private T? PickRecordMember<T>(IReadOnlyList<T> members,Func<T,string> title) where T:class
    {
        if(members.Count==1)return members[0];
        if(members.Count==0)return null;
        var dialog=new Window {Title="Hat ayrıntısını seç",Width=650,Height=380,MinWidth=450,MinHeight=280,
            WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(6,28,50)),Owner=this};
        var panel=new DockPanel {Margin=new Thickness(18)};
        var heading=new TextBlock {Text="Bu kayıttaki hangi hat için işlem yapmak istiyorsunuz?",Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,14)};
        DockPanel.SetDock(heading,Dock.Top);panel.Children.Add(heading);
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0)};
        var cancel=new Button {Content="Vazgeç",IsCancel=true,Padding=new Thickness(16,7,16,7),Margin=new Thickness(0,0,8,0)};
        var open=new Button {Content="Seçili hatla devam et",IsDefault=true,Padding=new Thickness(16,7,16,7)};
        buttons.Children.Add(cancel);buttons.Children.Add(open);DockPanel.SetDock(buttons,Dock.Bottom);panel.Children.Add(buttons);
        var list=new ListBox {ItemsSource=members.Select(title).ToArray(),SelectedIndex=0};panel.Children.Add(list);
        open.Click+=(_,_)=>{if(list.SelectedIndex>=0)dialog.DialogResult=true;};
        dialog.Content=panel;
        return dialog.ShowDialog()==true?members[list.SelectedIndex]:null;
    }
}
