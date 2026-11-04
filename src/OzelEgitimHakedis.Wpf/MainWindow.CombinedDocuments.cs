using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

public partial class MainWindow
{
    private ProgressPaymentHistoryData[] GetOutputBatchPayments(ProgressPaymentHistoryData selected) =>
        OutputScreenModel.BatchMembers(_progressHistory, selected);
}
