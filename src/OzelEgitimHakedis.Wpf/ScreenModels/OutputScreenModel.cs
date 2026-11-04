using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf.ScreenModels;

internal sealed record OutputRecordModel(ProgressPaymentHistoryData Payment, decimal Gross, decimal Net);

/// <summary>Output batch membership, order and totals, independent of list controls.</summary>
internal static class OutputScreenModel
{
    internal static ProgressPaymentHistoryData[] BatchMembers(IEnumerable<ProgressPaymentHistoryData> history,
        ProgressPaymentHistoryData selected)
    {
        var key = ProgressPaymentBatchService.GroupKey(selected);
        return history.Where(payment => payment.Id != selected.Id).Append(selected)
            .Where(payment => ProgressPaymentBatchService.GroupKey(payment) == key)
            .GroupBy(PaymentOperationsService.RouteKey)
            .Select(group => group.OrderByDescending(payment => payment.CreatedAt).First())
            .OrderBy(payment => payment.RouteNumber, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static IReadOnlyList<OutputRecordModel> Records(IEnumerable<ProgressPaymentHistoryData> history)
    {
        var payments = history.ToArray();
        return payments.GroupBy(ProgressPaymentBatchService.GroupKey)
            .Select(group =>
            {
                var selected = group.OrderBy(payment => payment.RouteNumber, StringComparer.OrdinalIgnoreCase).First();
                var members = BatchMembers(group, selected);
                return new OutputRecordModel(selected, members.Sum(payment => payment.Calculation.GrossAmount),
                    members.Sum(payment => payment.Calculation.NetPayable));
            }).OrderByDescending(row => row.Payment.PeriodDate ?? DateOnly.MinValue)
            .ThenByDescending(row => row.Payment.CreatedAt).ToArray();
    }
}
