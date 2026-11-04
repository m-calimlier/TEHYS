using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

public partial class MainWindow
{
    private TransportationProgramType? _historyOperationProgram;
    private bool IsOperationPayment(ProgressPaymentHistoryData p, Guid id) => p.Id == id && (!_historyOperationProgram.HasValue || p.Input.ProgramType == _historyOperationProgram);
    private bool _operationsReady;
    private bool _homeRefreshing;
    private ProgressPaymentHistoryData? _homeOutputPayment;
    private List<MonthlyPaymentRow> _monthlyRows = new();
    private static string Choice(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
    private TransportationProgramType? HomeProgram => Enum.TryParse<TransportationProgramType>(Choice(HomeProgramComboBox),out var p) ? p : null;

    private void InitializeOperations()
    {
        if(TryGetSelectedTransportationProgramType(out var program)) HomeProgramComboBox.SelectedIndex=program==TransportationProgramType.SpecialEducation?0:1;
        else if(_contracts.Select(c=>c.ProgramType).Distinct().Count()==1)
            HomeProgramComboBox.SelectedIndex=_contracts[0].ProgramType==TransportationProgramType.SpecialEducation?0:1;
        _operationsReady=true;
        RefreshHomeOperations();UpdateHistoryView();UpdateArchiveView();
        var lastBackup=Path.Combine(OehsRuntimePaths.DataRoot,"last-backup.txt");
        if(File.Exists(lastBackup)) BackupStatusText.Text=File.ReadAllText(lastBackup);
    }
    private OehsApplicationData SnapshotData() => new(GetSelectedContract(),
        _routes.Select(ToCoreRoute).ToArray(),_contractors.Select(c=>new Contractor(c.Name,c.TaxOrIdentityNumber,
            c.Address,c.Phone,c.Email,c.BankAccountNumber,c.ContactPerson,c.ProgramType,c.VatExempt)).ToArray(),
        _progressHistory.ToArray(),_contracts.ToArray(),_calculationConstants,_calculationConstantsHistory.ToArray(),SavedHybridPaymentDrafts.ToArray());
    private static RouteData ToCoreRoute(RouteDraft r) => new(r.RouteNumber,r.Name,r.DailyPrice,
        r.GuideDailyPrice,r.TransportWorkDays,r.TenderNumber,r.ContractorTaxOrIdentityNumber,r.ContractNumber,r.ProgramType,r.ContractAmount,r.ContractDate);
    private List<ProgressPaymentArchive> ReadOperationArchives()
    {
        var result=new List<ProgressPaymentArchive>();
        foreach(var file in _archiveStore.List())
        {
            try {result.Add(ProgressPaymentArchiveStore.Load(file.FullName));}
            catch(ProgressPaymentArchiveException) { /* Invalid files stay visible on the archive page. */ }
        }
        return result;
    }
    private IReadOnlyList<PaymentIssue> PaymentIssues(ProgressPaymentHistoryData payment)
    {
        var c=FindContract(payment.TenderNumber??"",payment.ContractNumber??"",payment.Input.ProgramType);
        var r=_routes.FirstOrDefault(r=>PaymentOperationsService.RouteKey(ToCoreRoute(r))==PaymentOperationsService.RouteKey(payment));
        var f=_contractors.FirstOrDefault(f=>f.ProgramType==payment.Input.ProgramType &&
            OehsIdentityService.NormalizeIdentity(f.TaxOrIdentityNumber)==OehsIdentityService.NormalizeIdentity(payment.ContractorTaxOrIdentityNumber??""));
        return PaymentOperationsService.Validate(payment,c,r is null?null:ToCoreRoute(r),f is null?null:new Contractor(f.Name,f.TaxOrIdentityNumber,ProgramType:f.ProgramType));
    }
    private bool EnsurePaymentPreflight(ProgressPaymentHistoryData payment)
    {
        var issues=PaymentIssues(payment);
        if(issues.Count==0) return true;
        NightMessageBox.Show(this,"Belge oluşturmadan önce şu bilgileri tamamlayın:\n\n"+string.Join("\n",issues.Select(i=>"• "+i.Message)),
            "OEHS — Belge kontrolü",MessageBoxButton.OK,MessageBoxImage.Information);
        return false;
    }
    private static string BuildOutputFingerprint(OehsOfficialDocumentContext context) =>
        OutputEvidenceStore.HashContext(JsonSerializer.Serialize(context with {GeneratedDate=new DateOnly(2000,1,1)})+
            "|template-operations-6-original-pdf-layout|"+typeof(PdfVectorWriter).Assembly.GetName().Version);
    private bool HasCurrentOutput(ProgressPaymentHistoryData payment)
    {
        try
        {
            var context=BuildOfficialDocumentContextForPayment(payment);
            return context is not null && OutputEvidenceStore.IsCurrent(OehsRuntimePaths.DesktopRoot,payment.Id,BuildOutputFingerprint(context),payment.Input.ProgramType);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) {return false;}
    }
    private void RefreshHomeOperations()
    {
        if(!_operationsReady || _homeRefreshing || HomeMonthlyGrid is null) return;
        _homeRefreshing=true;
        try
        {
            var selectedKey=(HomeMonthlyGrid.SelectedItem as MonthlyPaymentRow) is {} selected?PaymentOperationsService.RouteKey(selected.Route):null;
            var program=HomeProgram;
            var previousTender=HomeTenderListBox.SelectedItem as ContractDraftData;
            HomeTenderListBox.ItemsSource=_contracts.Where(c=>c.ProgramType==program).ToArray();
            HomeTenderListBox.SelectedItem=previousTender is not null && previousTender.ProgramType==program?previousTender:null;
            UpdateHomePeriodOptions();
            var month=HomeSelectedPeriod;
            _monthlyRows=program.HasValue && month.HasValue?PaymentOperationsService.BuildMonth(SnapshotData(),program.Value,
                month.Value,ReadOperationArchives(),HasCurrentOutput).ToList():new();
            if(HomeTenderListBox.SelectedItem is ContractDraftData c)
                _monthlyRows=_monthlyRows.Where(r=>r.Contract==c).ToList();
            UpdateHomeFinanceCards(_monthlyRows);
            var status=Choice(HomeStatusComboBox);
            var visible=_monthlyRows.Where(r=>(status.Length==0 || r.Group==status) &&
                PaymentOperationsService.Contains(r.Firm+" "+r.RouteTitle+" "+r.Tender,HomeSearchBox.Text)).ToArray();
            HomeMonthlyGrid.ItemsSource=visible;
            HomeMonthlyGrid.SelectedItem=visible.FirstOrDefault(r=>PaymentOperationsService.RouteKey(r.Route)==selectedKey);
            HomeListMessage.Text=program is null?"Önce bir taşıma programı seçin.":month is null?"Bir hakediş dönemi seçin.":
                visible.Length==0?(_monthlyRows.Count==0?"Bu dönemde beklenen hat yok. Sözleşme ve hat tanımlarını kontrol edin.":"Filtreye uyan kayıt yok."):
                $"{month:MMMM yyyy} · {visible.Length} hat gösteriliyor / {_monthlyRows.Count} hat";
            HomeSetupButton.Visibility=program.HasValue&&_monthlyRows.Count==0?Visibility.Visible:Visibility.Collapsed;
            HomeSetupButton.Content=_contracts.Any(c=>c.ProgramType==program)?"Hatları ve sözleşme tarihlerini kontrol et":"Sözleşme oluştur";
            UpdateHomeTenderSummary(HomeTenderListBox.SelectedItem as ContractDraftData);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _monthlyRows.Clear();
            UpdateHomeFinanceCards(_monthlyRows);
            HomeFinanceScopeText.Text="Dönem özeti yüklenemedi. Listeyi yenileyin.";
            HomeMonthlyGrid.ItemsSource=null;HomeListMessage.Text="Aylık liste yüklenemedi: "+ex.Message;
        }
        finally {_homeRefreshing=false;RefreshSelectedPayment();}
    }
    private void RefreshSelectedPayment()
    {
        if(!_operationsReady) return;
        HomePreviewButton.IsEnabled=false;HomeSavePdfsButton.IsEnabled=false;HomeNextActionButton.IsEnabled=false;
        HomePaymentAmounts.Text="";HomePaymentDetails.Text="";HomeIssuesList.ItemsSource=null;
        if(HomeMonthlyGrid.SelectedItem is not MonthlyPaymentRow row)
        {HomePaymentSummary.Text="Kontrol etmek için listeden bir hat seçin.";return;}
        HomeNextActionButton.IsEnabled=true;
        HomeNextActionButton.Content=row.Payment is null?"Hakediş oluştur":row.Issues.Count>0?"Eksik bilgiyi tamamla":row.Status=="Kontrole hazır"?"Hakedişi aç / arşivle":"Hakedişi aç";
        var p=row.Payment;
        HomePaymentSummary.Text=$"{row.Firm} · {row.RouteTitle}\nİKN: {row.Tender} · "+(p is null?"Bu ay için hakediş yok.":$"{p.PeriodDate:MMMM yyyy} · Hakediş {p.ProgressPaymentNumber}");
        HomeIssuesList.ItemsSource=row.Issues;
        if(p is null) return;
        try
        {
            var context=BuildOfficialDocumentContextForPayment(p);
            var guide=p.Input.ProgramType==TransportationProgramType.SpecialEducation?$"\nRehber: {p.Input.GuideWorkDays} gün × {p.Input.GuideDailyPrice} ₺":"";
            HomePaymentAmounts.Text=$"Taşıma: {p.Input.WorkDays} gün × {p.Input.DailyPrice} ₺{guide}\n"+
                $"Fiyat farkı: {FormatMoney(p.Calculation.CombinedPriceDifferenceExcludingVat)} · Kesinti: {FormatMoney(p.Calculation.TotalDeductions)}\n"+
                $"PDF net ödeme: {(context is null?"Bağlam eksik":FormatMoney(context.ReportNetPayable))}";
            var s=OehsOfficialDocumentSettingsStore.Load();
            HomePaymentDetails.Text=$"Düzenleyen: {s.PreparerName} · {s.PreparerRole}\nKontrol: {s.ControllerName} · {s.ControllerRole}\nOnay: {s.ApproverName} · {s.ApproverRole}\n"+
                "Hesaplanan net: "+FormatMoney(p.Calculation.NetPayable)+"\nKullanılan oranlar: "+DescribeRates(p.Calculation.AppliedConstants??_calculationConstants);
            HomePreviewButton.IsEnabled=row.Issues.Count==0 && context is not null;
            HomeSavePdfsButton.IsEnabled=HomePreviewButton.IsEnabled;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {HomePaymentDetails.Text="Belge kontrolü tamamlanamadı: "+ex.Message;}
    }
    private void HomeFilter_Changed(object sender,SelectionChangedEventArgs e)
    {
        RefreshHomeOperations();
        if(_operationsReady && ReferenceEquals(sender,HomeProgramComboBox)) RefreshRecordFilters();
    }
    private void HomeSearch_Changed(object sender,TextChangedEventArgs e) => RefreshHomeOperations();
    private void HomeMonthlyGrid_SelectionChanged(object sender,SelectionChangedEventArgs e) {if(!_homeRefreshing) RefreshSelectedPayment();}
    private void HomeRefresh_Click(object sender,RoutedEventArgs e) => RefreshHomeOperations();
    private void HomeSetup_Click(object sender,RoutedEventArgs e)
    {
        if(HomeProgram is not {} program || IsArchiveCorrection) return;
        if(HasOpenWorkForRestore()) {NightMessageBox.Show(this,"Önce açık formu kaydedin veya temizleyin.","OEHS",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        TransportProgramTypeComboBox.SelectedItem=TransportProgramTypeComboBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i=>i.Tag?.ToString()==program.ToString());
        NavigationButton_Click(_contracts.Any(c=>c.ProgramType==program)?NavLines:NavContract,new RoutedEventArgs());
    }
    private void HomeAllTenders_Click(object sender,RoutedEventArgs e) {HomeTenderListBox.SelectedItem=null;RefreshHomeOperations();}
    private void HomeIssue_Click(object sender,RoutedEventArgs e)
    {if(sender is Button {Tag:PaymentIssue issue}) OpenHomeTarget(issue.Target);}
    private void HomeNextAction_Click(object sender,RoutedEventArgs e)
    {if(HomeMonthlyGrid.SelectedItem is MonthlyPaymentRow row) OpenHomeTarget(row.Issues.Count > 0 ? row.Issues[0].Target : "Progress");}
    private void OpenHomeTarget(string target)
    {
        if(HomeMonthlyGrid.SelectedItem is not MonthlyPaymentRow row || IsArchiveCorrection) return;
        if(_editingHistoryId.HasValue || _editingRoute is not null || _editingContractTender is not null || _editingContractorKey is not null)
        {NightMessageBox.Show(this,"Önce açık düzenlemeyi kaydedin veya iptal edin.","OEHS",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        if(target=="Contract" && row.Contract is not null) {EditContractButton_Click(new Button{Tag=row.Contract},new RoutedEventArgs());return;}
        if(target=="Lines")
        {
            var route=_routes.FirstOrDefault(r=>PaymentOperationsService.RouteKey(ToCoreRoute(r))==PaymentOperationsService.RouteKey(row.Route));
            if(route is not null) {EditRouteButton_Click(new Button{Tag=route},new RoutedEventArgs());NavigationButton_Click(NavLines,new RoutedEventArgs());return;}
        }
        if(target=="Contractors" && row.Contractor is not null)
        {
            var firm=_contractors.FirstOrDefault(f=>f.ProgramType==row.Route.ProgramType && f.TaxOrIdentityNumber==row.Contractor.TaxOrIdentityNumber);
            if(firm is not null) {EditContractorButton_Click(new Button{Tag=firm},new RoutedEventArgs());NavigationButton_Click(NavContractors,new RoutedEventArgs());return;}
        }
        if(target=="Progress" && row.Payment is not null)
        {BeginHistoryOrArchiveCorrection(row.Payment);return;}
        NavigationButton_Click(new Button{Tag=target},new RoutedEventArgs());
        if(target=="Progress")
        {
            ProgressTenderListBox.SelectedItem=row.Contract;
            ProgressContractorComboBox.SelectedItem=_contractors.FirstOrDefault(c=>c.ProgramType==row.Route.ProgramType&&c.TaxOrIdentityNumber==row.Route.ContractorTaxOrIdentityNumber);
            ProgressRouteComboBox.SelectedItem=_routes.FirstOrDefault(r=>PaymentOperationsService.RouteKey(ToCoreRoute(r))==PaymentOperationsService.RouteKey(row.Route));
            if(HomeSelectedPeriod.HasValue) SelectProgressPeriod(HomeSelectedPeriod.Value);
        }
    }
    private void HomeOutput_Click(object sender,RoutedEventArgs e)
    {
        if(HomeMonthlyGrid.SelectedItem is not MonthlyPaymentRow {Payment:{} selected}) return;
        var payment=_progressHistory.FirstOrDefault(p=>p.Id==selected.Id&&p.Input.ProgramType==selected.Input.ProgramType);
        if(payment is not null && payment!=selected)
        {NightMessageBox.Show(this,"Seçili hakediş değişmiş. Güncel tutarları yeniden kontrol edin.","OEHS",MessageBoxButton.OK,MessageBoxImage.Information);RefreshHomeOperations();return;}
        if(payment is null || !EnsurePaymentPreflight(payment)) {RefreshHomeOperations();return;}
        _homeOutputPayment=payment;
        try {if(sender is Button {Tag:"Save"}) CreateSaveAllRouteDocumentsButton_Click(sender,e);else PreviewAllRouteDocumentsButton_Click(sender,e);}
        finally {_homeOutputPayment=null;RefreshHomeOperations();}
    }
}
