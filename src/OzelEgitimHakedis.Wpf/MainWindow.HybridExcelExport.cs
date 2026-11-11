using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using OzelEgitimHakedis.Core.Models;

namespace OzelEgitimHakedis.Wpf;

public partial class MainWindow
{
    private static readonly string[] PeriodExcelDetailHeader = { "Dönem içi hesap ayrıntısı" };
    private static readonly (string Header,Func<ProgressPaymentCalculation,decimal> Value)[] HybridCalculatedExcelColumns =
    {
        ("Taşıma Hizmet Tutarı (TL)",c=>c.ServiceAmount),
        ("Taşıma Fiyat Farkı (KDV Hariç) (TL)",c=>c.PriceDifferenceExcludingVat),
        ("Rehber Hizmet Tutarı (TL)",c=>c.GuideServiceAmount),
        ("Rehber Fiyat Farkı (KDV Hariç) (TL)",c=>c.GuidePriceDifferenceExcludingVat),
        ("Taşıma Hakediş Tutarı (TL)",c=>c.TransportNetAmount),
        ("Rehber Hakediş Tutarı (TL)",c=>c.GuideNetAmount),
        ("Toplam Hakediş (KDV Hariç) (TL)",c=>c.TotalAmount),
        ("KDV (TL)",c=>c.Vat),
        ("KDV Tevkifatı (TL)",c=>c.Withholding),
        ("Fatura Tutarı (Tevkifat Sonrası) (TL)",c=>c.InvoiceAmountAfterWithholding),
        ("Damga Vergisi (TL)",c=>c.StampTax),
        ("Ek Kesin Teminat (TL)",c=>c.AdditionalGuarantee),
        ("Kesinti: Vergi Borcu (TL)",c=>c.Deductions.TaxDebt),
        ("Kesinti: SGK Borcu (TL)",c=>c.Deductions.SocialSecurityDebt),
        ("Kesinti: Asgari Ücret Desteği (TL)",c=>c.Deductions.MinimumWageSupport),
        ("Kesinti: Denetim Cezası (TL)",c=>c.Deductions.InspectionPenalty),
        ("Brüt Tutar (KDV Dahil) (TL)",c=>c.GrossAmount),
        ("Toplam Kesinti (TL)",c=>c.TotalDeductions),
        ("Net Ödenecek (TL)",c=>c.NetPayable)
    };
    private void HybridExportExcelButton_Click(object sender, RoutedEventArgs e)
    {
        if(!CommitHybridExportEdits()) return;
        var dialog=new WorkspaceSaveFileDialog
        {
            Filter="Excel çalışma kitabı (*.xlsx)|*.xlsx",DefaultExt=".xlsx",AddExtension=true,
            FileName="Secili-Hatlar.xlsx"
        };
        if(dialog.ShowDialog(this)==true) SaveHybridTableExcel(dialog.FileName);
    }

    private bool CommitHybridExportEdits() =>
        MultiRoutePaymentGrid.CommitEdit(DataGridEditingUnit.Cell,true) &&
        MultiRoutePaymentGrid.CommitEdit(DataGridEditingUnit.Row,true) && !HasGridValidationError(MultiRoutePaymentGrid);

    private bool SaveHybridTableExcel(string path)
    {
        if(!CommitHybridExportEdits() || _multiRoutePaymentRows.Count==0) return false;
        var drafts=_multiRoutePaymentRows.ToArray();
        var calculations=drafts.Select(row=>row.Preview).ToArray();
        if(calculations.Any(c=>c is null))return false;
        var columns=MultiRoutePaymentGrid.Columns.Where(c=>c.Visibility==Visibility.Visible && c is DataGridBoundColumn).OrderBy(c=>c.DisplayIndex).ToArray();
        if(columns.Length==0) return false;
        var properties=columns.Select(c=>(c as DataGridBoundColumn)?.Binding as Binding)
            .Select(b=>b?.Path?.Path).ToArray();
        if(properties.Any(p=>p is null || typeof(MultiRoutePaymentDraftRow).GetProperty(p) is null)) return false;
        var hasVisibleNet=properties.Contains(nameof(MultiRoutePaymentDraftRow.NetPayableText));
        var calculatedColumns=HybridCalculatedExcelColumns.Where(c=>!hasVisibleNet || c.Header!="Net Ödenecek (TL)").ToArray();
        var includePeriodDetails=drafts.Any(row=>row.HasPeriodChanges);
        var headers=columns.Select(c=>c.Header?.ToString()??"").Concat(calculatedColumns.Select(c=>c.Header))
            .Concat(includePeriodDetails?PeriodExcelDetailHeader:Array.Empty<string>()).ToArray();
        IReadOnlyList<object?>[] values=drafts.Select((row,index)=>(IReadOnlyList<object?>)properties.Select(p=>
            p==nameof(MultiRoutePaymentDraftRow.NetPayableText)?(object?)new ExcelDocumentExporter.TableMoney(calculations[index]!.NetPayable):
            typeof(MultiRoutePaymentDraftRow).GetProperty(p!)!.GetValue(row))
            .Concat(calculatedColumns.Select(c=>(object?)new ExcelDocumentExporter.TableMoney(c.Value(calculations[index]!))))
            .Concat(includePeriodDetails?new object?[]{row.PeriodCalculationText}:Array.Empty<object?>()).ToArray()).ToArray();
        try
        {
            ExcelDocumentExporter.SaveTable(path,headers,values);
            HybridSelectionSummaryTextBlock.Text="Görünen tablo ve hesaplanan hakediş tutarları Excel dosyasına aktarıldı.";
            return true;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or System.Xml.XmlException)
        {
            HybridSelectionSummaryTextBlock.Text="Excel dosyası kaydedilemedi: "+ex.Message;
            return false;
        }
    }
}
