using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

internal sealed class PaymentPeriodEntry : INotifyPropertyChanged
{
    private string _days, _price, _difference;
    public PaymentPeriodEntry(decimal days = 0, decimal price = 0, decimal difference = 0, string description = "")
    { _days=days.ToString("0.####################",CultureInfo.GetCultureInfo("tr-TR")); _price=TurkishMoneyInput.Format(price); _difference=TurkishMoneyInput.Format(difference); Description=description; }
    public string DaysText { get=>_days; set { _days=value;Notify(); } }
    public string DailyPriceText { get=>_price; set { _price=value;Notify(); } }
    public string PriceDifferenceText { get=>_difference; set { _difference=value;Notify(); } }
    public string Description { get; set; }
    public string AmountText => TryRead(out var part) ? (part.WorkDays*part.DailyPrice).ToString("N2",CultureInfo.GetCultureInfo("tr-TR"))+" ₺" : "Kontrol edin";
    public bool TryRead(out PaymentRatePeriod part)
    {
        part = new(0,0);
        if (!TurkishMoneyInput.TryParseWorkDays(DaysText,out var days) || days<0 ||
            !TurkishMoneyInput.TryParseCalculationAmount(DailyPriceText,out var price) || price<0 ||
            !TurkishMoneyInput.TryParseCalculationAmount(PriceDifferenceText,out var difference)) return false;
        try { _=days*price; part=new(days,price,difference,Description.Trim()); return true; }
        catch(OverflowException) {return false;}
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify() {PropertyChanged?.Invoke(this,new(string.Empty));}
}

internal sealed class PaymentPeriodChangeWindow : Window
{
    private readonly ProgressPaymentInput _original;
    private readonly ProgressPaymentMonthOption _period;
    private readonly ProgressPaymentCalculationConstants _constants;
    private readonly TextBlock _error = new() { Foreground=Brushes.OrangeRed,TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,8,0,8) };
    private readonly TextBlock _transportTotal = new(), _guideTotal = new();
    private readonly List<DataGrid> _grids = new();
    public ObservableCollection<PaymentPeriodEntry> TransportRows { get; } = new();
    public ObservableCollection<PaymentPeriodEntry> GuideRows { get; } = new();
    public ProgressPaymentInput? ResultInput { get; private set; }

    public PaymentPeriodChangeWindow(ProgressPaymentInput input, ProgressPaymentMonthOption period, string routeTitle,
        ProgressPaymentCalculationConstants? constants = null)
    {
        _original=input; _period=period; _constants=constants??ProgressPaymentCalculationConstants.Defaults;
        Title="Dönem içi değişiklik — "+routeTitle; Width=920; Height=710; MinWidth=650; MinHeight=480;
        MaxHeight=Math.Max(480,SystemParameters.WorkArea.Height-40); MaxWidth=Math.Max(650,SystemParameters.WorkArea.Width-40);
        WindowStartupLocation=WindowStartupLocation.CenterOwner; Background=new SolidColorBrush(Color.FromRgb(8,35,59));
        Foreground=Brushes.White; FontFamily=new FontFamily("Segoe UI"); FontSize=13;
        Load(TransportRows,input.TransportPeriods,input.WorkDays,input.DailyPrice,input.PriceDifference);
        if(input.ProgramType==TransportationProgramType.SpecialEducation)
            Load(GuideRows,input.GuidePeriods,input.GuideWorkDays,input.GuideDailyPrice,input.GuidePriceDifference);
        var root=new DockPanel();
        var footer=new StackPanel(); DockPanel.SetDock(footer,Dock.Bottom);
        footer.Children.Add(_error);
        var buttons=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right };
        var cancel=new Button { Content="Vazgeç",IsCancel=true,MinWidth=105,Padding=new Thickness(12,8,12,8),Margin=new Thickness(0,0,8,0) };
        cancel.Click+=(_,_)=>Close();
        var apply=new Button { Content="Uygula",MinWidth=115,Padding=new Thickness(12,8,12,8) };
        if (Application.Current?.TryFindResource("ModernButton") is Style primary) apply.Style = primary;
        apply.Click+=(_,_)=> { if(TryApplyChanges()) DialogResult=true; };
        buttons.Children.Add(cancel);buttons.Children.Add(apply);footer.Children.Add(buttons);root.Children.Add(footer);
        var body=new StackPanel();
        body.Children.Add(new TextBlock { Text=routeTitle,FontSize=19,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text=period.DisplayText,Margin=new Thickness(0,4,0,8) });
        body.Children.Add(new TextBlock { Text="Her fiyat bölümü için gerçek çalışma gününü, günlük fiyatı ve fiyat farkını girin. İndirim ve diğer kesintiler ana hakedişte bir kez uygulanır. Sonraki aylara yalnız son günlük fiyat önerilir.",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,0,0,12) });
        body.Children.Add(Section("TAŞIMA",TransportRows,_transportTotal));
        if(input.ProgramType==TransportationProgramType.SpecialEducation) body.Children.Add(Section("REHBER PERSONEL",GuideRows,_guideTotal));
        root.Children.Add(new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
        Content=new Border { Background=Background, Padding=new Thickness(18), Child=root };
        RefreshTotals();
    }

    private static void Load(ObservableCollection<PaymentPeriodEntry> rows,IReadOnlyList<PaymentRatePeriod>? parts,decimal days,decimal price,decimal difference)
    {
        foreach(var p in parts ?? new[]{new PaymentRatePeriod(days,price,difference)})
            rows.Add(new(p.WorkDays,p.DailyPrice,p.PriceDifference,p.Description));
    }
    private StackPanel Section(string title,ObservableCollection<PaymentPeriodEntry> rows,TextBlock total)
    {
        var panel=new StackPanel { Margin=new Thickness(0,0,0,16) };
        panel.Children.Add(new TextBlock { Text=title,Foreground=Brushes.Gold,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,6) });
        var grid=new DataGrid { ItemsSource=rows,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,
            CanUserSortColumns=false,MaxHeight=195,MinHeight=105,RowHeight=31,Foreground=NightTheme.Text,Background=NightTheme.Surface,SelectionMode=DataGridSelectionMode.Single };
        void Column(string header,string path,double width,bool readOnly=false) => grid.Columns.Add(new DataGridTextColumn
        { Header=header,Binding=new Binding(path){Mode=readOnly?BindingMode.OneWay:BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=width,IsReadOnly=readOnly });
        Column("Gün",nameof(PaymentPeriodEntry.DaysText),90);
        Column("Günlük fiyat (TL)",nameof(PaymentPeriodEntry.DailyPriceText),140);
        Column("Fiyat farkı (KDV hariç)",nameof(PaymentPeriodEntry.PriceDifferenceText),165);
        Column("Hizmet tutarı",nameof(PaymentPeriodEntry.AmountText),135,true);
        Column("Açıklama (isteğe bağlı)",nameof(PaymentPeriodEntry.Description),215);
        panel.Children.Add(grid); _grids.Add(grid);
        var actions=new WrapPanel { Margin=new Thickness(0,7,0,6) };
        void ActionButton(string text,Action action)
        {
            var button=new Button { Content=text,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,8,0) };
            button.Click+=(_,_)=> {grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);action();RefreshTotals();}; actions.Children.Add(button);
        }
        ActionButton("+ Bölüm ekle",()=>
        {
            var price=rows.LastOrDefault() is {} last && last.TryRead(out var part)?part.DailyPrice:0m;
            rows.Add(new(0,price));
        });
        ActionButton("Seçili bölümü kaldır",()=> {if(rows.Count>1 && grid.SelectedItem is PaymentPeriodEntry selected)rows.Remove(selected);});
        ActionButton("Tek fiyata dön",()=>
        {
            if(!Read(rows,out var parts)) return;
            rows.Clear();rows.Add(new(parts.Sum(p=>p.WorkDays),parts[^1].DailyPrice,parts.Sum(p=>p.PriceDifference)));
        });
        panel.Children.Add(actions); total.Foreground=Brushes.LightSteelBlue; total.TextWrapping=TextWrapping.Wrap; panel.Children.Add(total);
        void Observe(PaymentPeriodEntry row) => row.PropertyChanged+=EntryChanged;
        foreach(var row in rows) Observe(row);
        rows.CollectionChanged+=(_,e)=> {if(e.NewItems is not null)foreach(PaymentPeriodEntry row in e.NewItems)Observe(row);RefreshTotals();};
        return panel;
    }
    private void EntryChanged(object? sender,PropertyChangedEventArgs e)=>RefreshTotals();
    private static bool Read(ObservableCollection<PaymentPeriodEntry> rows,out PaymentRatePeriod[] parts)
    {
        var result=new List<PaymentRatePeriod>();
        foreach(var row in rows) {if(!row.TryRead(out var part)){parts=Array.Empty<PaymentRatePeriod>();return false;}result.Add(part);}
        parts=result.ToArray();return parts.Length>0;
    }
    private void RefreshTotals()
    {
        string Text(ObservableCollection<PaymentPeriodEntry> rows)
        {
            if(!Read(rows,out var parts))return "Gün ve tutarları kontrol edin. Günlük fiyat en fazla dört ondalık basamak olabilir.";
            try {return $"{parts.Sum(p=>p.WorkDays)} gün · {parts.Length} bölüm · Hizmet: {parts.Sum(p=>p.WorkDays*p.DailyPrice):N2} TL · Fiyat farkı: {parts.Sum(p=>p.PriceDifference):N2} TL";}
            catch(OverflowException){return "Toplam hesaplanabilir sayı aralığını aşıyor.";}
        }
        _transportTotal.Text=Text(TransportRows);
        if(GuideRows.Count>0)_guideTotal.Text=Text(GuideRows);
    }
    internal bool TryApplyChanges()
    {
        ResultInput=null;
        foreach(var grid in _grids)
            if(!grid.CommitEdit(DataGridEditingUnit.Cell,true)||!grid.CommitEdit(DataGridEditingUnit.Row,true))return false;
        if(!Read(TransportRows,out var transport)||(_original.ProgramType==TransportationProgramType.SpecialEducation&&!Read(GuideRows,out _)))
        {_error.Text="Gün, günlük fiyat ve fiyat farkını kontrol edin. Gün ve günlük fiyat negatif olamaz.";return false;}
        Read(GuideRows,out var guide);
        try
        {
            var result=PaymentRatePeriodService.Apply(_original,transport,_original.ProgramType==TransportationProgramType.SpecialEducation?guide:null);
            var error=ProgressPaymentMonthService.GetCalendarCapacityError(_period,result.WorkDays,result.GuideWorkDays);
            if(error is not null){_error.Text=error;return false;}
            _=ProgressPaymentCalculator.Calculate(result,_constants);
            ResultInput=result;_error.Text="";return true;
        }
        catch(Exception ex)when(ex is ArgumentException or OverflowException){_error.Text=ex.Message;return false;}
    }
}
