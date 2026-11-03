using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;
public partial class MainWindow
{
    private bool _clearingRecordFilters;
    private PaymentFilter ReadRecordFilter(bool archive)
    {
        var date=((archive?ArchiveMonthFilter:HistoryMonthFilter).SelectedItem as ProgressPaymentMonthOption)?.PeriodDate;
        return new((archive?ArchiveSearchBox:HistorySearchBox).Text,date,
            (archive?ArchiveTenderFilter:HistoryTenderFilter).Text,(archive?ArchiveFirmFilter:HistoryFirmFilter).Text,
            RecordsProgram,Choice(archive?ArchiveStatusFilter:HistoryStatusFilter)=="Undated");
    }
    private IReadOnlyList<ProgressPaymentHistoryData> FilterHistory()
    {
        if(!_operationsReady) return ProgressPaymentReportService.OrderByPaymentPeriod(_progressHistory);
        UpdateRecordPeriodOptions(false);
        var filter=ReadRecordFilter(false);var status=Choice(HistoryStatusFilter);
        var archived=ReadOperationArchives().Select(a=>(a.ProgressPayment.Id,a.ProgressPayment.Input.ProgramType)).ToHashSet();
        var items=_progressHistory.GroupBy(ProgressPaymentBatchService.GroupKey).Where(g=>g.Any(p=>PaymentOperationsService.Matches(p,filter) &&
            (status!="Archived" || archived.Contains((p.Id,p.Input.ProgramType))) && (status!="Unarchived" || !archived.Contains((p.Id,p.Input.ProgramType))))).SelectMany(g=>g).ToArray();
        HistoryFilterSummary.Text=$"{ProgramLabel(filter.Program)} · {items.GroupBy(ProgressPaymentBatchService.GroupKey).Count()} / {_progressHistory.GroupBy(ProgressPaymentBatchService.GroupKey).Count()} kayıt"+
            (items.Length==0?(_progressHistory.Count==0?" · Henüz kayıt yok.":" · Filtreye uyan kayıt yok."):"");
        return ProgressPaymentReportService.OrderByPaymentPeriod(items);
    }
    private IReadOnlyList<ArchiveDisplayRow> FilterArchives()
    {
        var rows=ArchiveDisplayService.BuildRows(_archiveStore);
        if(!_operationsReady) return rows;
        UpdateRecordPeriodOptions(true);
        var filter=ReadRecordFilter(true);var status=Choice(ArchiveStatusFilter);
        var valid=new List<(ArchiveDisplayRow Row,ProgressPaymentArchive Archive)>();
        var invalid=new List<ArchiveDisplayRow>();
        foreach(var row in rows)
        {
            try {valid.Add((row,ProgressPaymentArchiveStore.Load(row.FullPath)));}
            catch(ProgressPaymentArchiveException) {invalid.Add(row);}
        }
        var latest=ArchiveShowRevisions.IsChecked==true?valid:valid.GroupBy(x=>(x.Archive.ProgressPayment.Id,x.Archive.ProgressPayment.Input.ProgramType))
            .Select(g=>g.OrderByDescending(x=>x.Archive.Revision).First()).ToList();
        var result=latest.GroupBy(x=>ProgressPaymentBatchService.GroupKey(x.Archive.ProgressPayment))
            .Where(g=>g.Any(x=>PaymentOperationsService.Matches(x.Archive.ProgressPayment,filter))&&status!="Invalid")
            .SelectMany(g=>g.Select(x=>x.Row)).ToList();
        // Unknown identities cannot safely be assigned to a program. Keep errors visible.
        if(status!="Valid") result.AddRange(invalid);
        var visibleCount=result.Count==0?0:GroupArchiveRows(result).Count;
        ArchiveFilterSummary.Text=$"{ProgramLabel(filter.Program)} · {visibleCount} sonuç · {invalid.Count} okunamayan dosya"+
            (result.Count==0?(rows.Count==0?" · Henüz arşiv yok.":" · Filtreye uyan arşiv yok."):"");
        return result;
    }
    private static string ProgramLabel(TransportationProgramType? program) => program==TransportationProgramType.BasicOrSecondary?"Temel Eğitim/Ortaöğretim Taşıma":"Özel Eğitim Taşıma";
    private void RecordsFilter_Changed(object sender,TextChangedEventArgs e) => RefreshRecordFilters();
    private void RecordsDateFilter_Changed(object sender,SelectionChangedEventArgs e) => RefreshRecordFilters();
    private void RecordsToggleFilter_Changed(object sender,RoutedEventArgs e) => RefreshRecordFilters();
    private void RefreshRecordFilters()
    {if(_operationsReady&&!_clearingRecordFilters&&!_updatingRecordPeriods) {UpdateHistoryView();UpdateArchiveView();}}
    private void ClearRecordFilters_Click(object sender,RoutedEventArgs e)
    {
        var archive=sender is Button{Tag:"Archive"};_clearingRecordFilters=true;
        try
        {
            (archive?ArchiveSearchBox:HistorySearchBox).Clear();(archive?ArchiveTenderFilter:HistoryTenderFilter).Clear();
            (archive?ArchiveFirmFilter:HistoryFirmFilter).Clear();(archive?ArchiveMonthFilter:HistoryMonthFilter).SelectedItem=null;
            (archive?ArchiveStatusFilter:HistoryStatusFilter).SelectedIndex=0;
            if(archive) ArchiveShowRevisions.IsChecked=false;
        }
        finally {_clearingRecordFilters=false;RefreshRecordFilters();}
    }
}
