using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

public partial class MainWindow
{
    private ProgressPaymentHistoryData[] GetOutputBatchPayments(ProgressPaymentHistoryData selected)
    {
        static bool Same(string? a,string? b) => string.Equals(OehsIdentityService.NormalizeIdentity(a),OehsIdentityService.NormalizeIdentity(b),StringComparison.Ordinal);
        var identity=OehsIdentityService.NormalizeIdentity(selected.ContractorTaxOrIdentityNumber);
        if(identity.Length==0 || string.IsNullOrWhiteSpace(selected.TenderNumber) || string.IsNullOrWhiteSpace(selected.ContractNumber))return new[]{selected};
        return _progressHistory.Where(p=>p.Id!=selected.Id).Append(selected)
            .Where(p=>p.Input.ProgramType==selected.Input.ProgramType && p.PeriodDate==selected.PeriodDate
                && Same(p.TenderNumber,selected.TenderNumber) && Same(p.ContractNumber,selected.ContractNumber)
                && Same(p.ContractorTaxOrIdentityNumber,selected.ContractorTaxOrIdentityNumber))
            .Where(p=>selected.BatchId.HasValue ? p.BatchId==selected.BatchId
                : p.Id==selected.Id || !string.IsNullOrWhiteSpace(selected.BatchRouteNumbers)
                    && p.BatchId is null && p.CreatedAt==selected.CreatedAt && p.ProgressPaymentNumber==selected.ProgressPaymentNumber
                    && p.BatchRouteNumbers==selected.BatchRouteNumbers)
            .GroupBy(PaymentOperationsService.RouteKey).Select(g=>g.OrderByDescending(p=>p.CreatedAt).First())
            .OrderBy(p=>p.RouteNumber,StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

internal static class OehsCombinedDocumentContext
{
    internal static OehsOfficialDocumentContext Combine(IReadOnlyList<OehsOfficialDocumentContext> contexts)
    {
        var first=contexts[0];
        if(contexts.Count==1)return first;
        decimal Sum(Func<ProgressPaymentCalculation,decimal> field)=>contexts.Sum(c=>field(c.Calculation));
        decimal Ded(Func<DeductionBreakdown,decimal> field)=>contexts.Sum(c=>field(c.Calculation.Deductions));
        var calculation=first.Calculation with
        {
            ServiceAmount=Sum(c=>c.ServiceAmount),PriceDifferenceExcludingVat=Sum(c=>c.PriceDifferenceExcludingVat),
            TotalAmount=Sum(c=>c.TotalAmount),Vat=Sum(c=>c.Vat),Withholding=Sum(c=>c.Withholding),
            InvoiceAmountAfterWithholding=Sum(c=>c.InvoiceAmountAfterWithholding),StampTax=Sum(c=>c.StampTax),
            GrossAmount=Sum(c=>c.GrossAmount),TotalDeductions=Sum(c=>c.TotalDeductions),NetPayable=Sum(c=>c.NetPayable),
            AdditionalGuarantee=Sum(c=>c.AdditionalGuarantee),GuideServiceAmount=Sum(c=>c.GuideServiceAmount),
            GuidePriceDifferenceExcludingVat=Sum(c=>c.GuidePriceDifferenceExcludingVat),TransportNetAmount=Sum(c=>c.TransportNetAmount),
            GuideNetAmount=Sum(c=>c.GuideNetAmount),TransportVat=Sum(c=>c.TransportVat),GuideVat=Sum(c=>c.GuideVat),
            TransportWithholding=Sum(c=>c.TransportWithholding),GuideWithholding=Sum(c=>c.GuideWithholding),
            Deductions=new(Ded(d=>d.Withholding),Ded(d=>d.StampTax),Ded(d=>d.TaxDebt),Ded(d=>d.InspectionPenalty),
                Ded(d=>d.SocialSecurityDebt),Ded(d=>d.MinimumWageSupport),Ded(d=>d.AdditionalGuarantee))
        };
        var name=string.Join(" - ",contexts.Select(c=>StripSuffix(string.IsNullOrWhiteSpace(c.RouteName)?c.WorkName:c.RouteName)))+" Hattı Taşıma İşi";
        return first with
        {
            WorkName=name,RouteName=name,RouteNumber=string.Join("+",contexts.Select(c=>c.RouteNumber)),IsCombined=true,
            Calculation=calculation,ContractAmount=contexts.Sum(c=>c.ContractAmount),
            ReportStampTax=contexts.Sum(c=>ProgressPaymentReportService.CalculateReportStampTax(c.Calculation,c.ProgramType,RawDifference(c))),
            ReportDeductions=contexts.Sum(c=>ProgressPaymentReportService.CalculateReportTotalDeductions(c.Calculation,c.ProgramType,RawDifference(c))),
            Payments=contexts.SelectMany(c=>c.Payments).DistinctBy(p=>p.Id).ToArray(),
            ReportNetPayable=contexts.Sum(c=>c.ReportNetPayable),PreviousDeduction=contexts.Sum(c=>c.PreviousDeduction),
            ReportTotals=new(contexts.Sum(c=>c.ReportTotals.CumulativeServiceAmount),contexts.Sum(c=>c.ReportTotals.CumulativePriceDifference),
                contexts.Sum(c=>c.ReportTotals.CumulativeTotal),contexts.Sum(c=>c.ReportTotals.PreviousTotal),contexts.Sum(c=>c.ReportTotals.CurrentTotal))
                {CurrentPriceDifference=contexts.Sum(c=>c.ReportTotals.CurrentPriceDifference)},
            RawTransportPriceDifference=contexts.Sum(RawDifference)
        };
    }

    private static decimal RawDifference(OehsOfficialDocumentContext c)=>c.Payments
        .Where(p=>p.PeriodDate==c.PeriodDate && p.ProgressPaymentNumber==c.ProgressPaymentNumber)
        .OrderByDescending(p=>p.CreatedAt).FirstOrDefault()?.Input.PriceDifference ?? c.Calculation.PriceDifferenceExcludingVat;

    private static string StripSuffix(string name)
    {
        name=name.Trim();
        foreach(var suffix in new[]{" Hattı Öğrenci Taşıma İşi"," Hattı Taşıma İşi"," Hattı"})
            if(name.EndsWith(suffix,StringComparison.OrdinalIgnoreCase))return name[..^suffix.Length].TrimEnd();
        return name;
    }
}
