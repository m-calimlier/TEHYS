using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Documents;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

public partial class MainWindow : Window
{
    private readonly ContractWorkNameConverter _contractWorkNameConverter;

    // Pencere, ekran çalışma alanını doğrudan kullanır; eski Viewbox tasarım oranı
    // yatay alanı daraltıp metinleri sıkıştırıyordu.

    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private HwndSource? _windowSource;
    private readonly ObservableCollection<ContractDraftData> _contracts = new();
    private readonly ObservableCollection<RouteDraft> _routes = new();
    private readonly ObservableCollection<ContractorDraft> _contractors = new();
    private readonly ObservableCollection<ProgressPaymentHistoryData> _progressHistory = new();
    private readonly ObservableCollection<MultiRoutePaymentDraftRow> _multiRoutePaymentRows = new();
    // Holds unsaved route drafts even while a route is temporarily deselected.
    // The visible collection remains limited to the current selection.
    private readonly RouteDraftCache<MultiRoutePaymentDraftRow> _multiRouteDraftCache = new();
    private string? _lastProgressDefaultsRouteKey;
    private readonly ObservableCollection<HistoryRow> _historyRows = new();
    private readonly ObservableCollection<SummaryRow> _summaryRows = new();
    private readonly ObservableCollection<ReportRow> _reportRows = new();
    private Guid? _editingHistoryId;
    private string? _editingContractTender;
    private string? _editingContractNumber;
    private TransportationProgramType? _editingContractProgramType;
    private string? _editingContractorKey;
    private RouteDraft? _editingRoute;
    private bool _suppressSelectionEvents;
    private bool _suppressMinimumWageSupportToggle;
    private bool _suppressArchiveDirtyTracking;
    private Guid? _historyCorrectionTargetId;
    private HistoryCorrectionSection _historyCorrectionSection;
    private readonly OehsProgramScopedApplicationDataStore _dataStore = new();
    private ProgressPaymentCalculationConstants _calculationConstants =
        ProgressPaymentCalculationConstants.Defaults;
    private List<ProgressPaymentCalculationConstantsChange> _calculationConstantsHistory = new();
    private readonly ProgressPaymentArchiveStore _archiveStore = new();
    private readonly ObservableCollection<ArchiveRow> _archiveRows = new();
    private Guid? _lastCalculatedHistoryId;
    private string? _lastSelectedPaymentMonthKey;
    private string? _editingArchiveSourcePath;
    private ProgressPaymentArchive? _editingArchiveSnapshot;
    private ArchiveCorrectionSection _archiveCorrectionSection;
    private bool _dataLoadFailed;
    private WindowLayoutPreference _windowLayoutPreference = WindowLayoutPreference.Default;
    private bool _suppressWindowLayoutSelectionChanged;
    private const double DocumentContentWidth = PdfLayoutService.SafeContentWidth;
    private static readonly double[] ThreeColumnWidths = { 58d, 438d, 194d };
    private static readonly double[] ThreeValueWidths = { 45d, 360d, 135d };
    private static readonly double[] FourColumnWidths = { 55d, 308d, 163d, 164d };
    private static readonly double[] FiveColumnWidths = { 40d, 278d, 124d, 124d, 124d };
    private static readonly string[] MainReportHeaders = { "", "Kalem", "Tutar" };
    private static readonly string[] DeductionHeaders = { "", "KESİNTİLER VE MAHSUPLAR", "Tutar" };
    private static readonly string[] ContractHeaders = { "Sözleşme Bedeli", "Sözleşme Artış Onayının Tarihi No.su", "Ek Sözleşme Bedeli", "Toplam Sözleşme Bedeli" };
    private static readonly string[] ExtensionHeaders = { "Tarih", "Sayısı", "Verilen Süre", "İş Bitim Tarihi" };
    private static readonly string[] IcmalHeaders = { "Sıra No", "İşin Cinsi", "İş Tutarı (a)", "Fiyat Farkı Tutarı (b)", "Toplam (a+b)" };
    private static readonly string[] OfficialDocumentNames =
        ProgressPaymentReportService.RequiredDocumentNames.ToArray();



    public MainWindow()
    {
        InitializeComponent();
        _contractWorkNameConverter = (ContractWorkNameConverter)Resources["ContractWorkNameConverter"];
        ResetEditorUiToCreateMode();
        ConfigureWindow();
        ConfigureZeroValueInputs();
        ConfigureContractValidationInputs();
        ConfigureArchiveCorrectionChangeTracking();
        ConfigureLiveCalculationTrace();
        SourceInitialized += MainWindow_SourceInitialized;
        StartDatePicker.SelectedDate = DateTime.Today;
        EndDatePicker.SelectedDate = DateTime.Today;
        TaxDebtTextBox.Text = "0";
        SocialSecurityDebtTextBox.Text = "0";
        MinimumWageSupportTextBox.Text = "0";
        SetMinimumWageSupportInputMode(null);
        InspectionPenaltyTextBox.Text = "0";
        DiscountTextBox.Text = "0";
        GuideWorkDaysTextBox.Text = "0";
        GuideDailyPriceTextBox.Text = "0";
        GuideDiscountTextBox.Text = "0";
        GuidePriceDifferenceTextBox.Text = "0";
        UpdateShellText("Ana Ekran");
        RoutesListBox.ItemsSource = _routes;
        ContractorsListBox.ItemsSource = _contractors;
        HomeTenderListBox.ItemsSource = _contracts;
        RouteTenderListBox.ItemsSource = _contracts;
        ProgressTenderListBox.ItemsSource = _contracts;
        HistoryItemsControl.ItemsSource = _historyRows;
        ArchiveItemsControl.ItemsSource = _archiveRows;
        SummaryItemsControl.ItemsSource = _summaryRows;
        ReportItemsControl.ItemsSource = _reportRows;
        ProgressRouteComboBox.ItemsSource = _routes;
        MultiRoutePaymentGrid.ItemsSource = _multiRoutePaymentRows;
        ReportRouteComboBox.ItemsSource = _routes;
        OutputTenderListBox.ItemsSource = _contracts;
        OehsFreshStartService.EnsureFreshStart();
        LoadPersistedData();
        PopulateCalculationConstantsPanel();

        UpdateContractSelectors();
        if (_routes.Count > 0)
            ReportRouteComboBox.SelectedIndex = 0;

        UpdateRouteSummary();
        UpdateContractorSummary();
        UpdateContractSummaryView();
        UpdateHomeSummaryView();
        UpdateProgressRouteDefaults();
        UpdateHistoryView();
        UpdateSummaryView();
        UpdateReportView();
        UpdateAcceptanceView();
        UpdateArchiveView();

        // Açılışta kayıtlı veriler yalnızca listelerde/özetlerde görünür; giriş formları boş başlar.
        ClearContractFormOnly();
        ClearProgressEntryFields();
        UpdateProgressRouteDefaults();
        InitializeOperations();
    }

    private enum ArchiveCorrectionSection
    {
        None,
        Contract,
        Contractor,
        Route,
        ProgressPayment
    }

    private enum HistoryCorrectionSection
    {
        None,
        Contract,
        Contractor,
        Route,
        ProgressPayment
    }

    // Arşiv düzeltme işlemi başladıysa bölüm durumu yanlışlıkla sıfırlansa bile
    // canlı kayıt oluşturma akışına düşülmemelidir. Bu bayrak kayıt kaynağını kilitler.
    private bool IsArchiveEditing =>
        !string.IsNullOrWhiteSpace(_editingArchiveSourcePath) &&
        _editingArchiveSnapshot is not null;

    private bool IsArchiveCorrection =>
        IsArchiveEditing &&
        _archiveCorrectionSection != ArchiveCorrectionSection.None;

    private sealed record RouteDraft(
        string RouteNumber,
        string Name,
        decimal DailyPrice,
        decimal GuideDailyPrice = 0m,
        decimal TransportWorkDays = 0,
        string TenderNumber = "",
        string ContractorTaxOrIdentityNumber = "",
        string ContractorName = "",
        string ContractNumber = "",
        TransportationProgramType ProgramType = TransportationProgramType.SpecialEducation,
        decimal? ContractAmount = null,
        DateOnly? ContractDate = null)
    {
        public string TransportWorkDaysText => $"Döneme Esas THGS: {TransportWorkDays}";
        public decimal TransportAnnualPrice => TransportWorkDays * DailyPrice;
        public string TransportAnnualPriceText => $"Döneme Esas Taşıma: {TransportAnnualPrice:N2} ₺";
        public string DailyPriceText => $"Taşıma Günlük: {DailyPrice:N2} ₺";
        public string GuideDailyPriceText => $"Rehber: {GuideDailyPrice:N2} ₺";
        public string TransportSummaryText => $"Döneme Esas THGS: {TransportWorkDays} gün × {DailyPrice:N2} ₺";
        public string GuideSummaryText => $"Rehber Personel Günlük Fiyat: {GuideDailyPrice:N2} ₺";
        public string ServiceSummaryText => $"Taşıma {TransportWorkDays} gün • Rehber günlük {GuideDailyPrice:N2} ₺";
        public string RelationshipText => $"İKN: {TenderNumber} • Yüklenici: {ContractorName}";
        public string PanelTitleText => $"Hat {RouteNumber} — {Name}";
        public string PanelTransportText => $"Taşıma günlük fiyatı: {FormatCalculationInput(DailyPrice)} ₺ / gün";
        public string PanelGuideText => HasGuideStaff(ProgramType) ? $"Rehber günlük: {FormatCalculationInput(GuideDailyPrice)} ₺" : string.Empty;
        public string PanelContractText => $"Sözleşme: {(ContractAmount.HasValue ? FormatMoney(ContractAmount.Value) : "belirtilmedi")} • Tarih: {ContractDate?.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("tr-TR")) ?? "belirtilmedi"}";
        public string DisplayText => string.IsNullOrWhiteSpace(ContractorName)
            ? $"Hat {RouteNumber} — {Name}"
            : $"Hat {RouteNumber} — {Name} • {ContractorName}";
    }

    private sealed record ContractorDraft(
        string Name,
        string TaxOrIdentityNumber,
        string Address = "",
        string Phone = "",
        string Email = "",
        string BankAccountNumber = "",
        string ContactPerson = "",
        TransportationProgramType ProgramType = TransportationProgramType.SpecialEducation,
        bool VatExempt = false)
    {
        public string VatExemptText => VatExempt ? "KDV İstisnası: VAR" : "KDV İstisnası: YOK";
        public string ProgramDisplayText => ProgramType == TransportationProgramType.BasicOrSecondary ? "Temel Eğitim/Ortaöğretim Taşıma" : "Özel Eğitim Taşıma";
        public string BankDisplayText => string.IsNullOrWhiteSpace(BankAccountNumber) ? "Banka bilgisi girilmedi" : $"Banka / IBAN: {BankAccountNumber}";
    }

    private sealed record ArchiveRow(
        string FullPath,
        string DisplayTitle,
        string DetailText,
        string NetPayableText,
        string RevisionText)
    {
        public bool IsReadable => !string.Equals(RevisionText, "GEÇERSİZ", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record HistoryRow(
        Guid Id,
        string DisplayTitle,
        string CreatedAtText,
        string TotalAmountText,
        string NetPayableText,
        TransportationProgramType ProgramType);

    private sealed record SummaryRow(
        string Title,
        string PeriodText,
        string GrossText,
        string NetText);

    private sealed record ReportRow(
        ProgressPaymentHistoryData Payment,
        string Title,
        string PeriodText,
        string GrossText,
        string NetText);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint ptReserved;
        public NativePoint ptMaxSize;
        public NativePoint ptMaxPosition;
        public NativePoint ptMinTrackSize;
        public NativePoint ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
}
