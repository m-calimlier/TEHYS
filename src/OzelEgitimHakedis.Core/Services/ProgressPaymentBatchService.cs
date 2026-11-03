using System.Globalization;
using System.Text.Json;
using OzelEgitimHakedis.Core.Models;

namespace OzelEgitimHakedis.Core.Services;

public static class ProgressPaymentBatchService
{
    public static string GroupKey(ProgressPaymentHistoryData payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        var identity=OehsIdentityService.NormalizeIdentity(payment.ContractorTaxOrIdentityNumber);
        if(identity.Length==0 || string.IsNullOrWhiteSpace(payment.TenderNumber) || string.IsNullOrWhiteSpace(payment.ContractNumber)
            || !payment.BatchId.HasValue && string.IsNullOrWhiteSpace(payment.BatchRouteNumbers))
            return $"{payment.Input.ProgramType}:{payment.Id}";
        return JsonSerializer.Serialize(new[]
        {
            OehsIdentityService.BuildContractKey(payment.TenderNumber,payment.ContractNumber,payment.Input.ProgramType),identity,
            payment.PeriodDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
            payment.BatchId?.ToString() ?? $"{payment.CreatedAt:O}:{payment.BatchRouteNumbers}"
        });
    }
}
