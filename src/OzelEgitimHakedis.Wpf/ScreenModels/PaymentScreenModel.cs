using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf.ScreenModels;

/// <summary>Payment workspace calculations and identity-scoped selection, independent of WPF controls.</summary>
internal static class PaymentScreenModel
{
    internal static ProgressPaymentCalculation Calculate(ProgressPaymentInput input,
        ProgressPaymentCalculationConstants constants) => ProgressPaymentCalculator.Calculate(input, constants);

    internal static ProgressPaymentHistoryData? LatestForRoute(IEnumerable<ProgressPaymentHistoryData> history,
        ContractDraftData contract, RouteData route) => history.Where(payment =>
            string.Equals(payment.TenderNumber, contract.TenderNumber, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(payment.ContractNumber, contract.ContractNumber, StringComparison.OrdinalIgnoreCase) &&
            payment.Input.ProgramType == route.ProgramType &&
            string.Equals(payment.RouteNumber, route.RouteNumber, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(OehsIdentityService.NormalizeIdentity(payment.ContractorTaxOrIdentityNumber),
                OehsIdentityService.NormalizeIdentity(route.ContractorTaxOrIdentityNumber), StringComparison.Ordinal))
        .OrderByDescending(payment => payment.PeriodDate ?? DateOnly.MinValue)
        .ThenByDescending(payment => payment.CreatedAt).FirstOrDefault();
}
