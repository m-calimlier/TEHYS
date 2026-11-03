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

    private bool IsArchiveCorrectionFor(ArchiveCorrectionSection section) =>
        IsArchiveCorrection && _archiveCorrectionSection == section;

    private void ResetEditorUiToCreateMode()
    {
        SaveContractButton.Content = "İHALEYİ KAYDET";
        CancelContractEditButton.Visibility = Visibility.Collapsed;
        ClearContractButton.IsEnabled = true;
        ClearContractButton.Visibility = Visibility.Visible;
        ContractEditorTitleTextBlock.Text = "İhale bilgileri";
        ContractEditorSubtitleTextBlock.Text = "Programı seçin, İKN ve hizmet tarihlerini girin.";

        SetContractorEditorMode(false);

        AddRouteButton.Content = "HAT EKLE";
        CancelRouteEditButton.Visibility = Visibility.Collapsed;
        ClearRouteButton.IsEnabled = true;
        ClearRouteButton.Visibility = Visibility.Visible;
        RouteEditorTitleTextBlock.Text = "YENİ HAT";
        RouteEditorSubtitleTextBlock.Text = "Hat bilgilerini girin";
        RouteSelectionCard.Visibility = Visibility.Visible;
        RouteNumberTextBox.IsReadOnly = false;

        CalculateProgressButton.Content = "HAKEDİŞ HESAPLA/KAYDET";
        CancelProgressEditButton.Visibility = Visibility.Collapsed;
        ProgressEditorTitleTextBlock.Text = "DÖNEM HESAP BİLGİLERİ";
        ProgressTenderListBox.IsEnabled = true;
        ProgressRouteComboBox.IsEnabled = true;
    }

    private void SetContractEditorMode(bool editing)
    {
        SaveContractButton.Content = editing ? "DEĞİŞİKLİKLERİ KAYDET" : "İHALEYİ KAYDET";
        CancelContractEditButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        ClearContractButton.IsEnabled = !editing;
        ClearContractButton.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        ContractEditorTitleTextBlock.Text = editing ? "İhale kaydını düzenle" : "İhale bilgileri";
        ContractEditorSubtitleTextBlock.Text = editing
            ? "Mevcut kaydı düzenliyorsunuz. Vazgeçerseniz DÜZENLEME İPTAL düğmesini kullanın."
            : "Programı seçin, İKN ve hizmet tarihlerini girin.";
    }

    private void SetContractorEditorMode(bool editing)
    {
        SaveContractorButton.Content = editing ? "DEĞİŞİKLİKLERİ KAYDET" : "FİRMAYI KAYDET";
        CancelContractorEditButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        ClearContractorButton.IsEnabled = !editing;
        ClearContractorButton.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        ContractorEditorTitleTextBlock.Text = editing ? "Firma kaydını düzenle" : "Firma bilgileri";
        ContractorProgramComboBox.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        ContractorEditingProgramTextBlock.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        var program = IsArchiveCorrectionFor(ArchiveCorrectionSection.Contractor)
            ? _editingArchiveSnapshot!.Contract.ProgramType
            : FindContractorByKey(_editingContractorKey)?.ProgramType;
        ContractorEditingProgramTextBlock.Text = program == TransportationProgramType.BasicOrSecondary
            ? "Temel Eğitim/Ortaöğretim Taşıma" : program.HasValue ? "Özel Eğitim Taşıma" : string.Empty;
    }

    private void SetRouteEditorMode(bool editing, bool archiveCorrection = false)
    {
        AddRouteButton.Content = editing ? "DEĞİŞİKLİKLERİ KAYDET" : "HAT KAYDET";
        CancelRouteEditButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        ClearRouteButton.IsEnabled = !editing;
        ClearRouteButton.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        RouteEditorTitleTextBlock.Text = editing
            ? (archiveCorrection ? "ARŞİVDEKİ HAT DÜZENLE" : "HAT DÜZENLE")
            : "YENİ HAT";
        RouteEditorSubtitleTextBlock.Text = editing
            ? (archiveCorrection
                ? "Bu alan yalnızca seçtiğiniz arşiv kaydını günceller; yeni hat oluşturulmaz."
                : "Mevcut hattı düzenliyorsunuz. Vazgeçerseniz DÜZENLEME İPTAL düğmesini kullanın.")
            : "Hat bilgilerini girin";
        RouteSelectionCard.Visibility = archiveCorrection ? Visibility.Collapsed : Visibility.Visible;
        RouteNumberTextBox.IsReadOnly = false;
    }

    private void SetProgressEditorMode(bool editing, bool archiveCorrection = false)
    {
        RefreshPeriodInputReadOnly();
        CalculateProgressButton.Content = editing ? "DÜZENLE" : "HAKEDİŞ HESAPLA/KAYDET";
        SinglePaymentFieldsGrid.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        CancelProgressEditButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;

        if (editing)
        {
            ArchiveCurrentProgressButton.Visibility = Visibility.Collapsed;
            CorrectCurrentProgressButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            var hasCalculatedProgress = GetLastCalculatedProgress() is not null;
            ArchiveCurrentProgressButton.Visibility =
                hasCalculatedProgress ? Visibility.Visible : Visibility.Collapsed;
            CorrectCurrentProgressButton.Visibility =
                hasCalculatedProgress ? Visibility.Visible : Visibility.Collapsed;
        }

        ProgressEditorTitleTextBlock.Text = editing
            ? (archiveCorrection ? "ARŞİV HAKEDİŞ DÜZENLE" : "HAKEDİŞ DÜZENLE")
            : "HAT BAZINDA HAKEDİŞ GİRDİLERİ";
        ProgressTenderListBox.IsEnabled = !(editing && archiveCorrection);
        ProgressRouteComboBox.IsEnabled = !(editing && archiveCorrection);
        if (!editing && ProgressRouteComboBox.SelectedItems.Count > 0) UpdateMultiRouteDraftRows();
    }

    private void ClearArchiveCorrectionState()
    {
        if (_archiveCorrectionSection == ArchiveCorrectionSection.ProgressPayment)
            RestoreLiveProgressEditorSelectorContext();

        _editingArchiveSourcePath = null;
        _editingArchiveSnapshot = null;
        _archiveCorrectionSection = ArchiveCorrectionSection.None;
        _suppressArchiveDirtyTracking = false;
        ClearLiveHistoryCorrectionContext();
        ResetEditorUiToCreateMode();
    }

    private void LoadArchiveProgressEditorContext(ArchiveProgressEditorContext context)
    {
        var snapshotContractor = new ContractorDraft(
            context.Contractor.Name,
            OehsIdentityService.NormalizeIdentity(context.Contractor.TaxOrIdentityNumber),
            context.Contractor.Address,
            context.Contractor.Phone,
            context.Contractor.Email,
            context.Contractor.BankAccountNumber,
            context.Contractor.ContactPerson,
            context.Contractor.ProgramType,
            context.Contractor.VatExempt ?? false);
        var snapshotRoute = new RouteDraft(
            context.Route.RouteNumber,
            context.Route.Name,
            context.Route.DailyPrice,
            context.Route.GuideDailyPrice,
            context.Route.TransportWorkDays,
            context.Route.TenderNumber,
            OehsIdentityService.NormalizeIdentity(context.Route.ContractorTaxOrIdentityNumber),
            context.Contractor.Name,
            context.Route.ContractNumber,
            context.Contract.ProgramType,
            context.Route.ContractAmount ?? _editingArchiveSnapshot?.ProgressPayment.Input.ContractAmount,
            context.Route.ContractDate ?? _editingArchiveSnapshot?.ProgressPayment.ContractDate);

        _suppressSelectionEvents = true;
        try
        {
            _multiRoutePaymentRows.Clear();
            _multiRouteDraftCache.Clear();
            ProgressTenderListBox.ItemsSource = new[] { context.Contract };
            ProgressTenderListBox.SelectedItem = context.Contract;
            ProgressContractorComboBox.ItemsSource = new[] { snapshotContractor };
            ProgressContractorComboBox.SelectedItem = snapshotContractor;
            ProgressRouteComboBox.ItemsSource = new[] { snapshotRoute };
            ProgressRouteComboBox.SelectedItems.Clear();
            ProgressRouteComboBox.SelectedItem = snapshotRoute;
            UpdateProgressProgramTypeFields(context.Contract);
            UpdateProgressPeriodOptions(context.Contract, preserveSelection: false);
            SelectProgressPeriod(context.PeriodDate);
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateMultiRouteDraftRows();
    }

    private void RestoreLiveProgressEditorSelectorContext()
    {
        _suppressSelectionEvents = true;
        try
        {
            ProgressTenderListBox.ItemsSource = _contracts;
            ProgressTenderListBox.SelectedItem = null;
            ProgressContractorComboBox.ItemsSource = Array.Empty<ContractorDraft>();
            ProgressContractorComboBox.SelectedItem = null;
            ProgressPeriodMonthComboBox.ItemsSource = Array.Empty<ProgressPaymentMonthOption>();
            ProgressPeriodMonthComboBox.SelectedItem = null;
            ProgressRouteComboBox.ItemsSource = Array.Empty<RouteDraft>();
            ProgressRouteComboBox.SelectedItems.Clear();
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        _multiRoutePaymentRows.Clear();
        _multiRouteDraftCache.Clear();
        _lastProgressDefaultsRouteKey = null;
        _lastSelectedPaymentMonthKey = null;
    }

    private void ConfigureWindow()
    {
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = true;
        Background = System.Windows.Media.Brushes.White;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        _windowLayoutPreference = WindowLayoutPreferenceStore.Load();
        ApplyWindowLayoutPreference(_windowLayoutPreference);
        UpdateMaximizeRestoreButton();
    }

    private void ApplyWindowLayoutPreference(WindowLayoutPreference preference)
    {
        _windowLayoutPreference = preference;

        if (preference.GetRequestedSize() is not { } requestedSize)
        {
            WindowState = WindowState.Maximized;
            SelectWindowLayoutMode(preference.Mode);
            return;
        }

        WindowState = WindowState.Normal;
        var workArea = SystemParameters.WorkArea;

        Width = Math.Min(requestedSize.Width, workArea.Width);
        Height = Math.Min(requestedSize.Height, workArea.Height);

        Left = workArea.Left + ((workArea.Width - Width) / 2.0);
        Top = workArea.Top + ((workArea.Height - Height) / 2.0);
        SelectWindowLayoutMode(preference.Mode);
    }

    private void SelectWindowLayoutMode(WindowLayoutMode mode)
    {
        if (WindowLayoutModeComboBox is null)
            return;

        _suppressWindowLayoutSelectionChanged = true;
        try
        {
            WindowLayoutModeComboBox.SelectedValue = mode.ToString();
        }
        finally
        {
            _suppressWindowLayoutSelectionChanged = false;
        }
    }

    private void WindowLayoutModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressWindowLayoutSelectionChanged ||
            WindowLayoutModeComboBox.SelectedValue is not string selectedValue ||
            !Enum.TryParse<WindowLayoutMode>(selectedValue, out var mode))
        {
            return;
        }

        var preference = new WindowLayoutPreference(mode);
        try
        {
            WindowLayoutPreferenceStore.Save(preference);
            ApplyWindowLayoutPreference(preference);
            WindowLayoutStatusText.Text = "Pencere tercihi kaydedildi. Windows ekran çözünürlüğü değişmedi.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SelectWindowLayoutMode(_windowLayoutPreference.Mode);
            WindowLayoutStatusText.Text = $"Pencere tercihi kaydedilemedi; önceki seçim korundu. {error.Message}";
        }
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WindowMessageHook);
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo || lParam == IntPtr.Zero)
            return IntPtr.Zero;

        var minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);

        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MonitorInfo
            {
                cbSize = Marshal.SizeOf<MonitorInfo>()
            };

            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                var work = monitorInfo.rcWork;
                var bounds = monitorInfo.rcMonitor;
                minMax.ptMaxPosition.x = work.left - bounds.left;
                minMax.ptMaxPosition.y = work.top - bounds.top;
                minMax.ptMaxSize.x = work.right - work.left;
                minMax.ptMaxSize.y = work.bottom - work.top;
            }
        }

        Marshal.StructureToPtr(minMax, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    private void UpdateMaximizeRestoreButton()
    {
        if (MaximizeRestoreButton is null || MaximizeRestoreIcon is null)
            return;

        var maximized = WindowState == WindowState.Maximized;
        MaximizeRestoreButton.ToolTip = maximized ? "Geri yükle" : "Tam ekran";
        MaximizeRestoreIcon.Text = maximized ? "\uE923" : "\uE922";
    }

    private void UpdateAcceptanceView()
    {
        if (_dataStore is null)
            return;

        if (AcceptanceContractTextBlock is null)
            return;

        if (_progressHistory.Count == 0)
        {
            AcceptanceContractTextBlock.Text = "Kayıt yok";
            AcceptanceProgressTextBlock.Text = "Kayıt yok";
            AcceptanceNetTextBlock.Text = FormatMoney(0);
            AcceptanceDetailTextBlock.Text = "Sözleşme ve hakediş kaydı oluşturulduğunda bilgiler burada görünür.";
            return;
        }

        var selectedRoute = ReportRouteComboBox.SelectedItem as RouteDraft;
        var latest = _progressHistory
            .Where(x => selectedRoute is null ||
                        (string.Equals(x.TenderNumber, selectedRoute.TenderNumber, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(x.ContractNumber, selectedRoute.ContractNumber, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(x.RouteNumber, selectedRoute.RouteNumber, StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(
                        OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
                        OehsIdentityService.NormalizeIdentity(selectedRoute.ContractorTaxOrIdentityNumber),
                        StringComparison.Ordinal)))
            .OrderByDescending(x => x.PeriodDate ?? DateOnly.MinValue)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefault();

        if (latest is null)
        {
            AcceptanceContractTextBlock.Text = "Kayıt yok";
            AcceptanceProgressTextBlock.Text = "Kayıt yok";
            AcceptanceNetTextBlock.Text = FormatMoney(0);
            AcceptanceDetailTextBlock.Text = selectedRoute is null
                ? "Hakediş kaydı oluşturulduğunda bilgiler burada görünür."
                : "Seçilen hat için henüz hakediş kaydı yok.";
            return;
        }

        AcceptanceContractTextBlock.Text = string.IsNullOrWhiteSpace(latest.ContractorName)
            ? (string.IsNullOrWhiteSpace(CurrentContractorName) ? "Sözleşme bilgisi" : CurrentContractorName.Trim())
            : latest.ContractorName.Trim();

        AcceptanceProgressTextBlock.Text = latest.ProgressPaymentNumber.HasValue
            ? $"Hakediş No {latest.ProgressPaymentNumber.Value}"
            : "Son hakediş";

        AcceptanceNetTextBlock.Text = FormatMoney(latest.Calculation.NetPayable);

        var period = latest.PeriodDate.HasValue
            ? latest.PeriodDate.Value.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture)
            : "-";

        AcceptanceDetailTextBlock.Text =
            $"Dönem: {period} • Brüt: {FormatMoney(latest.Calculation.GrossAmount)} • " +
            $"Net: {FormatMoney(latest.Calculation.NetPayable)} • " +
            $"Toplam kesinti: {FormatMoney(latest.Calculation.TotalDeductions)}";
    }

    private void PopulateCalculationConstantsPanel()
    {
        if (TransportVatRateTextBox is null)
            return;
        TransportVatRateTextBox.Text = FormatPercentageInput(_calculationConstants.TransportVatRate);
        GuideVatRateTextBox.Text = FormatPercentageInput(_calculationConstants.GuideVatRate);
        TransportWithholdingShareTextBox.Text = FormatPercentageInput(_calculationConstants.TransportWithholdingShare);
        GuideWithholdingShareTextBox.Text = FormatPercentageInput(_calculationConstants.GuideWithholdingShare);
        StampTaxRateTextBox.Text = FormatPercentageInput(_calculationConstants.StampTaxRate);
        AdditionalGuaranteeRateTextBox.Text = FormatPercentageInput(_calculationConstants.AdditionalGuaranteeRate);
        UpdateCalculationConstantsAuditDisplay();
    }

    private static string FormatPercentageInput(decimal fraction) =>
        (fraction * 100m).ToString("0.###", CultureInfo.CurrentCulture);

    private static bool TryParsePercentageInput(string? text, out decimal fraction)
    {
        fraction = 0m;
        if (!TryParseDecimal(text, out var percent) || percent < 0m || percent > 100m)
            return false;
        fraction = percent / 100m;
        return true;
    }

    private void UpdateCalculationConstantsAuditDisplay()
    {
        if (CalculationConstantsAuditTextBlock is null)
            return;
        if (_calculationConstantsHistory.Count == 0)
        {
            CalculationConstantsAuditTextBlock.Text =
                "Henüz sabit değişikliği kaydedilmedi. Başlangıç değerleri kaynak Excel'deki oranlardır; mevzuat uygunluğu teyidi değildir.";
            return;
        }

        CalculationConstantsAuditTextBlock.Text = string.Join(
            Environment.NewLine + Environment.NewLine,
            _calculationConstantsHistory.OrderByDescending(x => x.ChangedAtUtc).Take(5).Select(x =>
                $"{x.ChangedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture)} • {x.ChangedBy}" +
                $"{Environment.NewLine}Gerekçe: {x.Reason}" +
                $"{Environment.NewLine}Taşıma KDV: %{FormatPercentageInput(x.Previous.TransportVatRate)} → %{FormatPercentageInput(x.Current.TransportVatRate)}" +
                $"; Rehber KDV: %{FormatPercentageInput(x.Previous.GuideVatRate)} → %{FormatPercentageInput(x.Current.GuideVatRate)}" +
                $"{Environment.NewLine}Taşıma tevkifat payı: %{FormatPercentageInput(x.Previous.TransportWithholdingShare)} → %{FormatPercentageInput(x.Current.TransportWithholdingShare)}" +
                $"; Rehber tevkifat payı: %{FormatPercentageInput(x.Previous.GuideWithholdingShare)} → %{FormatPercentageInput(x.Current.GuideWithholdingShare)}" +
                $"{Environment.NewLine}Damga vergisi: %{FormatPercentageInput(x.Previous.StampTaxRate)} → %{FormatPercentageInput(x.Current.StampTaxRate)}" +
                $"; Ek kesin teminat: %{FormatPercentageInput(x.Previous.AdditionalGuaranteeRate)} → %{FormatPercentageInput(x.Current.AdditionalGuaranteeRate)}"));
    }

    private void SaveCalculationConstantsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParsePercentageInput(TransportVatRateTextBox.Text, out var transportVat) ||
            !TryParsePercentageInput(GuideVatRateTextBox.Text, out var guideVat) ||
            !TryParsePercentageInput(TransportWithholdingShareTextBox.Text, out var transportWithholding) ||
            !TryParsePercentageInput(GuideWithholdingShareTextBox.Text, out var guideWithholding) ||
            !TryParsePercentageInput(StampTaxRateTextBox.Text, out var stampTax) ||
            !TryParsePercentageInput(AdditionalGuaranteeRateTextBox.Text, out var additionalGuarantee))
        {
            MessageBox.Show("Bütün oranları yüzde olarak girin. Her alan %0 ile %100 arasında olmalıdır (ör. 10 veya 0,948).",
                "OEHS — Sabit Doğrulama", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var changedBy = CalculationConstantsChangedByTextBox.Text.Trim();
        var reason = CalculationConstantsReasonTextBox.Text.Trim();
        if (changedBy.Length == 0 || reason.Length < 5)
        {
            MessageBox.Show("Sabit değişikliğini kaydetmek için işlemi yapan kişinin adını ve en az 5 karakterlik gerekçeyi girin.",
                "OEHS — Değişiklik Kaydı", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var proposed = new ProgressPaymentCalculationConstants(
            transportVat, guideVat, transportWithholding, guideWithholding, stampTax, additionalGuarantee);
        if (!proposed.TryValidate(out var validationError))
        {
            MessageBox.Show(validationError, "OEHS — Sabit Doğrulama", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (proposed == _calculationConstants)
        {
            MessageBox.Show("Girilen sabitlerde değişiklik yok. Yeni kayıt oluşturulmadı.", "OEHS",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var previous = _calculationConstants;
        var confirmation = MessageBox.Show(
            "Hesaplama sabitleri değiştirilecek:" + Environment.NewLine + Environment.NewLine +
            $"Taşıma KDV: %{FormatPercentageInput(previous.TransportVatRate)} → %{FormatPercentageInput(proposed.TransportVatRate)}" + Environment.NewLine +
            $"Rehber KDV: %{FormatPercentageInput(previous.GuideVatRate)} → %{FormatPercentageInput(proposed.GuideVatRate)}" + Environment.NewLine +
            $"Taşıma tevkifat payı: %{FormatPercentageInput(previous.TransportWithholdingShare)} → %{FormatPercentageInput(proposed.TransportWithholdingShare)}" + Environment.NewLine +
            $"Rehber tevkifat payı: %{FormatPercentageInput(previous.GuideWithholdingShare)} → %{FormatPercentageInput(proposed.GuideWithholdingShare)}" + Environment.NewLine +
            $"Damga vergisi: %{FormatPercentageInput(previous.StampTaxRate)} → %{FormatPercentageInput(proposed.StampTaxRate)}" + Environment.NewLine +
            $"Ek kesin teminat: %{FormatPercentageInput(previous.AdditionalGuaranteeRate)} → %{FormatPercentageInput(proposed.AdditionalGuaranteeRate)}" + Environment.NewLine + Environment.NewLine +
            $"İşlemi yapan: {changedBy}" + Environment.NewLine + $"Gerekçe: {reason}" + Environment.NewLine + Environment.NewLine +
            "Onaylanırsa yeni sabitler kalıcı kaydedilecek ve yeni hesaplamalarda kullanılacak. Mevcut geçmiş/arşiv tutarları denetim izi korunarak otomatik değiştirilmeyecek.",
            "OEHS — Hesaplama Sabitleri Onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        _calculationConstants = proposed;
        _calculationConstantsHistory.Add(new ProgressPaymentCalculationConstantsChange(
            DateTime.UtcNow, changedBy, reason, previous, proposed));
        if (!SavePersistedData())
        {
            _calculationConstants = previous;
            _calculationConstantsHistory.RemoveAt(_calculationConstantsHistory.Count - 1);
            PopulateCalculationConstantsPanel();
            return;
        }

        PopulateCalculationConstantsPanel();
        CalculationConstantsReasonTextBox.Clear();
        RefreshCalculationTraceLive();
        MessageBox.Show("Sabitler kaydedildi. Yeni hesaplamalar güncel sabitleri kullanacak; geçmiş hakedişler yalnızca açık düzeltme/yeniden hesaplama işleminde değişir.",
            "OEHS — Sabitler Kaydedildi", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ResetCalculationConstantsFieldsButton_Click(object sender, RoutedEventArgs e)
    {
        var defaults = ProgressPaymentCalculationConstants.Defaults;
        TransportVatRateTextBox.Text = FormatPercentageInput(defaults.TransportVatRate);
        GuideVatRateTextBox.Text = FormatPercentageInput(defaults.GuideVatRate);
        TransportWithholdingShareTextBox.Text = FormatPercentageInput(defaults.TransportWithholdingShare);
        GuideWithholdingShareTextBox.Text = FormatPercentageInput(defaults.GuideWithholdingShare);
        StampTaxRateTextBox.Text = FormatPercentageInput(defaults.StampTaxRate);
        AdditionalGuaranteeRateTextBox.Text = FormatPercentageInput(defaults.AdditionalGuaranteeRate);
        MessageBox.Show("Kaynak Excel başlangıç değerleri forma yüklendi; kaydetmek için yine gerekçeli onay gerekir.",
            "OEHS — Başlangıç Değerleri", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LoadSignatoriesIntoFields()
    {
        var signatories = OehsOfficialDocumentSettingsStore.Load();
        SignatoryPreparerNameTextBox.Text = signatories.PreparerName;
        SignatoryPreparerRoleTextBox.Text = signatories.PreparerRole;
        SignatoryControllerNameTextBox.Text = signatories.ControllerName;
        SignatoryControllerRoleTextBox.Text = signatories.ControllerRole;
        SignatoryApproverNameTextBox.Text = signatories.ApproverName;
        SignatoryApproverRoleTextBox.Text = signatories.ApproverRole;
    }

    private void LoadSignatoriesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadSignatoriesIntoFields();
            MessageBox.Show(
                "Kayıtlı imzacı bilgileri yeniden yüklendi.",
                "OEHS — İmzacı Ayarları",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "OEHS — İmzacı Ayarları Okunamadı",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveSignatoriesButton_Click(object sender, RoutedEventArgs e)
    {
        var values = new[]
        {
            SignatoryPreparerNameTextBox.Text,
            SignatoryPreparerRoleTextBox.Text,
            SignatoryControllerNameTextBox.Text,
            SignatoryControllerRoleTextBox.Text,
            SignatoryApproverNameTextBox.Text,
            SignatoryApproverRoleTextBox.Text
        };

        if (values.Any(string.IsNullOrWhiteSpace))
        {
            MessageBox.Show(
                "Lütfen üç imzacının da adını ve görev/unvan bilgisini doldurun. Boş alanla kayıt yapılmadı.",
                "OEHS — Eksik İmza Bilgisi",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (values.Any(value => value.Trim().Length > 150))
        {
            MessageBox.Show(
                "İsim ve görev/unvan alanları 150 karakteri aşamaz.",
                "OEHS — İmza Bilgisi Çok Uzun",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var signatories = new OehsOfficialDocumentSignatories(
                PreparerName: values[0].Trim(),
                PreparerRole: values[1].Trim(),
                ControllerName: values[2].Trim(),
                ControllerRole: values[3].Trim(),
                ApproverName: values[4].Trim(),
                ApproverRole: values[5].Trim());

            OehsOfficialDocumentSettingsStore.Save(signatories);
            LoadSignatoriesIntoFields();

            MessageBox.Show(
                "İmzacı bilgileri kaydedildi. Yeni oluşturulacak dört resmî belge bu bilgileri kullanacaktır. Önceden oluşturulmuş PDF dosyaları otomatik değişmez; gerekiyorsa yeniden oluşturun.",
                "OEHS — İmzacı Ayarları Kaydedildi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "İmzacı ayarları kaydedilemedi. Dosya izinlerini ve ayar dosyasını kontrol edin.\n\n" + ex.Message,
                "OEHS — Kayıt Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button)
            return;

        var page = button.Tag?.ToString() ?? "Home";

        if (IsArchiveCorrection)
        {
            var correctionPage = _archiveCorrectionSection switch
            {
                ArchiveCorrectionSection.Contract => "Contract",
                ArchiveCorrectionSection.Contractor => "Contractors",
                ArchiveCorrectionSection.Route => "Lines",
                ArchiveCorrectionSection.ProgressPayment => "Progress",
                _ => string.Empty
            };

            if (!string.Equals(page, correctionPage, StringComparison.Ordinal) &&
                !(correctionPage == "Progress" && page == "Trace"))
            {
                MessageBox.Show(
                    "Arşiv düzeltmesi devam ediyor. Önce DÜZENLEME İPTAL veya düzenleme kaydetme işlemini tamamlayın.",
                    "OEHS — Düzenleme Devam Ediyor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }

        if (page == "Signatories" && _operationsReady)
        {
            try
            {
                LoadSignatoriesIntoFields();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "OEHS — İmzacı Ayarları Okunamadı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        HomePanel.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
        CalculationTracePanel.Visibility = page == "Trace" ? Visibility.Visible : Visibility.Collapsed;
        ContractPanel.Visibility = page == "Contract" ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = page == "Progress" ? Visibility.Visible : Visibility.Collapsed;
        LinesPanel.Visibility = page == "Lines" ? Visibility.Visible : Visibility.Collapsed;
        ContractorsPanel.Visibility = page == "Contractors" ? Visibility.Visible : Visibility.Collapsed;
        RecordsPanel.Visibility = page is "History" or "Archives" ? Visibility.Visible : Visibility.Collapsed;
        SelectRecordsSection(page);
        if (page == "Trace" && _operationsReady) RefreshCalculationTraceLive();
        HistoryPanel.Visibility = page == "History" ? Visibility.Visible : Visibility.Collapsed;
        ArchivePanel.Visibility = page == "Archives" ? Visibility.Visible : Visibility.Collapsed;
        SummaryPanel.Visibility = page == "Summary" ? Visibility.Visible : Visibility.Collapsed;
        ReportsPanel.Visibility = page == "Reports" ? Visibility.Visible : Visibility.Collapsed;
        AcceptancePanel.Visibility = page == "Acceptance" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        SignatoriesPanel.Visibility = page == "Signatories" ? Visibility.Visible : Visibility.Collapsed;
        CalculationConstantsPanel.Visibility = page == "Constants" ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderPanel.Visibility = page is "Home" or "Contract" or "Progress" or "Lines" or "Contractors"
            or "History" or "Archives" or "Summary" or "Reports" or "Acceptance" or "Settings" or "Signatories" or "Constants" or "Trace"
            ? Visibility.Collapsed
            : Visibility.Visible;

        var module = page switch
        {
            "Contract" => "İhale / Sözleşme",
            "Progress" => "Hakediş Oluştur",
            "Lines" => "Hatlar",
            "Contractors" => "Yüklenici Firmalar",
            "History" => "Kayıtlar ve Arşiv",
            "Archives" => "Kayıtlar ve Arşiv",
            "Summary" => "İcmal",
            "Reports" => "Çıktılar",
            "Acceptance" => "Kabul Tutanağı",
            "Settings" => "Ayarlar",
            "Signatories" => "Resmî Belge İmzacıları",
            "Constants" => "Hesaplama Sabitleri",
            "Trace" => "Canlı Hesap Dökümü",
            _ => "Ana Ekran"
        };

        UpdateShellText(module);

        if (PlaceholderPanel.Visibility == Visibility.Visible)
            PlaceholderTitleTextBlock.Text = module;

        UpdateActiveNavigation(page);
    }

    private void UpdateShellText(string module)
    {
        HeaderModuleText.Text = module;
    }

    private void UpdateActiveNavigation(string page)
    {
        foreach (var button in new[]
        {
            NavHome, NavContract, NavSignatories, NavContractors, NavLines,
            NavProgress, NavReports, NavHistory, NavSettings
        })
        {
            var buttonPage = button.Tag?.ToString();
            button.Uid = buttonPage == page || (page == "Constants" && buttonPage == "Settings")
                || (page == "Archives" && buttonPage == "History")
                || (page == "Trace" && buttonPage == "Progress")
                ? "Active"
                : string.Empty;
        }
    }

    private ContractorDraft? FindContractor(
        string taxOrIdentityNumber,
        TransportationProgramType? programType = null)
    {
        if (string.IsNullOrWhiteSpace(taxOrIdentityNumber))
            return null;

        var identity = OehsIdentityService.NormalizeIdentity(taxOrIdentityNumber);
        var matches = _contractors.Where(x =>
            string.Equals(
                OehsIdentityService.NormalizeIdentity(x.TaxOrIdentityNumber),
                identity,
                StringComparison.Ordinal));
        return programType.HasValue
            ? matches.FirstOrDefault(x => x.ProgramType == programType.Value)
            : matches.FirstOrDefault();
    }

    private static string BuildContractorKey(ContractorDraft contractor) =>
        $"{contractor.ProgramType}|{OehsIdentityService.NormalizeIdentity(contractor.TaxOrIdentityNumber)}";

    private ContractorDraft? FindContractorByKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var separator = key.IndexOf('|', StringComparison.Ordinal);
        if (separator < 0 || !Enum.TryParse<TransportationProgramType>(key[..separator], out var programType))
            return FindContractor(key);

        return FindContractor(key[(separator + 1)..], programType);
    }

    private ContractDraftData? GetSelectedContract()
    {
        return HomeTenderListBox.SelectedItem as ContractDraftData
            ?? RouteTenderListBox.SelectedItem as ContractDraftData
            ?? ProgressTenderListBox.SelectedItem as ContractDraftData
            ?? FindContract(TenderNumberTextBox.Text, ContractNumberTextBox.Text, GetSelectedTransportationProgramType())
            ?? _contracts.FirstOrDefault();
    }

    private ContractDraftData? FindContract(
        string? tenderNumber,
        string? contractNumber,
        TransportationProgramType? programType = null)
    {
        if (string.IsNullOrWhiteSpace(tenderNumber))
            return null;

        var tenderContracts = _contracts.Where(x =>
            string.Equals(x.TenderNumber.Trim(), tenderNumber.Trim(), StringComparison.OrdinalIgnoreCase) &&
            (!programType.HasValue || x.ProgramType == programType.Value)).ToArray();
        if (!string.IsNullOrWhiteSpace(contractNumber))
        {
            var matches = tenderContracts.Where(x =>
                string.Equals(x.ContractNumber.Trim(), contractNumber.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        // Legacy records can only be assigned automatically while tender identity is unique.
        return tenderContracts.Length == 1 ? tenderContracts[0] : null;
    }

    private bool IsHistoryForContract(ProgressPaymentHistoryData item, ContractDraftData contract)
    {
        if (!string.Equals(item.TenderNumber?.Trim(), contract.TenderNumber.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (item.Input.ProgramType != contract.ProgramType)
            return false;
        if (!string.IsNullOrWhiteSpace(item.ContractNumber))
            return string.Equals(item.ContractNumber.Trim(), contract.ContractNumber.Trim(), StringComparison.OrdinalIgnoreCase);

        return _contracts.Count(x =>
            x.ProgramType == contract.ProgramType &&
            string.Equals(x.TenderNumber.Trim(), contract.TenderNumber.Trim(), StringComparison.OrdinalIgnoreCase)) == 1;
    }

    private bool IsRouteForContract(RouteDraft route, ContractDraftData contract)
    {
        if (!string.Equals(route.TenderNumber.Trim(), contract.TenderNumber.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (route.ProgramType != contract.ProgramType)
            return false;
        if (!string.IsNullOrWhiteSpace(route.ContractNumber))
            return string.Equals(route.ContractNumber.Trim(), contract.ContractNumber.Trim(), StringComparison.OrdinalIgnoreCase);

        return _contracts.Count(x =>
            x.ProgramType == contract.ProgramType &&
            string.Equals(x.TenderNumber.Trim(), contract.TenderNumber.Trim(), StringComparison.OrdinalIgnoreCase)) == 1;
    }

    private void ApplyContractToForm(ContractDraftData contract)
    {
        ResetTenderDateValidation();
        _suppressSelectionEvents = true;
        try
        {
            TenderNumberTextBox.Text = contract.TenderNumber;
            ContractNumberTextBox.Text = contract.ContractNumber;
            ContractAmountTextBox.Text = FormatCalculationInput(contract.ContractAmount);
            ContractWorkDaysTextBox.Text = contract.ContractWorkDays.ToString(CultureInfo.CurrentCulture);
            ContractTransportDailyPriceTextBox.Text = FormatCalculationInput(contract.TransportDailyPrice);
            StartDatePicker.SelectedDate = contract.StartDate.ToDateTime(TimeOnly.MinValue);
            EndDatePicker.SelectedDate = contract.EndDate.ToDateTime(TimeOnly.MinValue);
            TenderDatePicker.SelectedDate = contract.TenderDate?.ToDateTime(TimeOnly.MinValue);
            ContractDatePicker.SelectedDate = contract.ContractDate?.ToDateTime(TimeOnly.MinValue);
            TransportProgramTypeComboBox.SelectedItem = TransportProgramTypeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(
                    item.Tag?.ToString(),
                    contract.ProgramType.ToString(),
                    StringComparison.OrdinalIgnoreCase));

        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateContractSummaryView();
    }

    private void ClearContractFormOnly()
    {
        ResetTenderDateValidation();
        TenderNumberTextBox.Clear();
        ContractNumberTextBox.Clear();
        ContractWorkDaysTextBox.Clear();
        ContractTransportDailyPriceTextBox.Clear();
        ContractAmountTextBox.Clear();
        StartDatePicker.SelectedDate = null;
        EndDatePicker.SelectedDate = null;
        TenderDatePicker.SelectedDate = null;
        ContractDatePicker.SelectedDate = null;
    }

    private bool TryGetSelectedTransportationProgramType(out TransportationProgramType programType)
    {
        var tag = (TransportProgramTypeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return Enum.TryParse<TransportationProgramType>(tag, ignoreCase: true, out programType);
    }

    private TransportationProgramType GetSelectedTransportationProgramType()
    {
        // Existing selector/summary code needs a stable value during initial view
        // setup. Save handlers use TryGetSelectedTransportationProgramType and
        // explicitly reject missing selection before creating official records.
        return TryGetSelectedTransportationProgramType(out var programType)
            ? programType
            : TransportationProgramType.SpecialEducation;
    }

    private static bool HasGuideStaff(TransportationProgramType programType) =>
        programType == TransportationProgramType.SpecialEducation;

    private string GetContractWorkName(ContractDraftData contract)
    {
        if (_editingArchiveSnapshot is { } archive && ReferenceEquals(contract, archive.Contract))
            return OfficialTemplateProfile.DeriveWorkName(archive.Route.Name, contract.WorkName);

        var names = _routes.Where(route => IsRouteForContract(route, contract))
            .Where(route => !string.IsNullOrWhiteSpace(route.Name))
            .Select(route => OfficialTemplateProfile.DeriveWorkName(route.Name))
            .Distinct(StringComparer.Ordinal).ToArray();
        return names.Length == 0 ? contract.WorkName : string.Join(Environment.NewLine, names);
    }

    private void RefreshContractWorkNameDisplays()
    {
        // Keep selectors bound to the original contracts; only presentation changes.
        _contractWorkNameConverter.UpdateTitles(_contracts.Distinct()
            .ToDictionary(contract => contract, GetContractWorkName));
        UpdateHomeTenderSummary(HomeTenderListBox.SelectedItem as ContractDraftData);
    }

    private void UpdateOfficialTemplateLabels(TransportationProgramType programType)
    {
        var profile = OfficialTemplateProfile.For(programType);
        CalculationSheetNameTextBlock.Text = profile.CalculationSheetName;
        ServiceReportSheetNameTextBlock.Text = OfficialTemplateProfile.For(
            (ReportRouteComboBox.SelectedItem as RouteDraft)?.ProgramType ?? programType).ServiceReportSheetName;
        RouteNameLabel.Text = profile.RouteNameLabel;
        ProgressRouteNameLabel.Text = profile.RouteNameLabel;
        PriceDifferenceLabel.Text = profile.TransportPriceDifferenceLabel;
        TransportPriceDifferenceResultLabel.Text = profile.TransportPriceDifferenceLabel;
        GuidePriceDifferenceLabel.Text = profile.GuidePriceDifferenceLabel ?? string.Empty;
        GuidePriceDifferenceResultLabel.Text = profile.GuidePriceDifferenceLabel ?? string.Empty;
        var guideVisibility = profile.GuidePriceDifferenceLabel is null ? Visibility.Collapsed : Visibility.Visible;
        GuidePriceDifferenceResultLabel.Visibility = guideVisibility;
        GuidePriceDifferenceResultTextBlock.Visibility = guideVisibility;
        NetPayableLabel.Text = profile.NetPayableLabel;
        NetControlLabel.Text = profile.NetPayableLabel;
        ProgressNetSummaryLabel.Text = profile.NetPayableLabel;

        foreach (var column in MultiRoutePaymentGrid.Columns.OfType<DataGridBoundColumn>())
        {
            var path = (column.Binding as System.Windows.Data.Binding)?.Path.Path;
            if (path == nameof(MultiRoutePaymentDraftRow.PriceDifference))
                column.Header = profile.TransportPriceDifferenceLabel;
            if (path == nameof(MultiRoutePaymentDraftRow.GuidePriceDifference))
            {
                column.Header = profile.GuidePriceDifferenceLabel ?? string.Empty;
                column.Visibility = guideVisibility;
            }
        }
    }

    private void UpdateRouteProgramTypeFields(ContractDraftData? contract)
    {
        ApplyRouteProgramFields(contract?.ProgramType);
    }

    private void ApplyRouteProgramFields(TransportationProgramType? program)
    {
        RouteNameLabel.Text = OfficialTemplateProfile.For(program ?? GetSelectedTransportationProgramType()).RouteNameLabel;
        var visible = program.HasValue && HasGuideStaff(program.Value);
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RouteGuideDailyPriceLabel.Visibility = visibility;
        RouteGuideDailyPriceTextBox.Visibility = visibility;
        RouteGuideFieldPanel.Visibility = visibility;
        if (RouteDailyPriceTextBox.Parent is FrameworkElement pricePanel)
            Grid.SetColumnSpan(pricePanel, visible ? 1 : 3);

        if (!visible)
        {
            RouteGuideDailyPriceTextBox.Text = "0";
        }
    }

    private void UpdateProgressProgramTypeFields(ContractDraftData? contract)
    {
        UpdateOfficialTemplateLabels(contract?.ProgramType ?? GetSelectedTransportationProgramType());
        var visible = contract is not null && HasGuideStaff(contract.ProgramType);
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ProgressGuideWorkDaysLabel.Visibility = visibility;
        GuideWorkDaysTextBox.Visibility = visibility;
        ProgressGuideDailyPriceLabel.Visibility = visibility;
        GuideDailyPriceTextBox.Visibility = visibility;
        GuideDiscountLabel.Visibility = visibility;
        GuideDiscountTextBox.Visibility = visibility;
        GuidePriceDifferenceLabel.Visibility = visibility;
        GuidePriceDifferenceTextBox.Visibility = visibility;
        SetMinimumWageSupportInputMode(contract?.ProgramType);

        if (!visible)
        {
            GuideWorkDaysTextBox.Text = "0";
            GuideDailyPriceTextBox.Text = "0";
            GuideDiscountTextBox.Text = "0";
            GuidePriceDifferenceTextBox.Text = "0";
        }
    }

    private void SetMinimumWageSupportInputMode(
        TransportationProgramType? programType,
        bool restoreSavedValue = false,
        decimal savedAmount = 0m)
    {
        var hasContract = programType.HasValue;
        var visibility = hasContract ? Visibility.Visible : Visibility.Collapsed;

        MinimumWageSupportLabel.Visibility = visibility;
        MinimumWageSupportTextBox.Visibility = visibility;
        MinimumWageSupportEnabledCheckBox.Visibility = Visibility.Collapsed;

        _suppressMinimumWageSupportToggle = true;
        try
        {
            MinimumWageSupportEnabledCheckBox.IsChecked = true;
            MinimumWageSupportTextBox.IsEnabled = hasContract;
            if(restoreSavedValue)MinimumWageSupportTextBox.Text=FormatCalculationInput(savedAmount);
        }
        finally
        {
            _suppressMinimumWageSupportToggle = false;
        }
    }

    private void RestoreMinimumWageSupportInputState(
        TransportationProgramType programType,
        decimal savedAmount) =>
        SetMinimumWageSupportInputMode(programType, restoreSavedValue: true, savedAmount: savedAmount);

    private void MinimumWageSupportEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _suppressMinimumWageSupportToggle)
            return;

        MinimumWageSupportTextBox.IsEnabled = MinimumWageSupportLabel.Visibility==Visibility.Visible;
    }

    private void UpdateContractSelectors()
    {
        RefreshContractWorkNameDisplays();
        var selectedProgram = GetSelectedTransportationProgramType();
        var scopedContracts = _contracts.Where(x => x.ProgramType == selectedProgram).ToArray();
        var selectedTender = TenderNumberTextBox.Text.Trim();
        var selectedContractNumber = ContractNumberTextBox.Text.Trim();
        var previousHome = HomeTenderListBox.SelectedItem as ContractDraftData;
        var previousRoute = RouteTenderListBox.SelectedItem as ContractDraftData;
        var previousProgress = ProgressTenderListBox.SelectedItem as ContractDraftData;
        var previousOutput = OutputTenderListBox.SelectedItem as ContractDraftData;

        var contract = FindContract(selectedTender, selectedContractNumber, selectedProgram)
                       ?? (previousRoute is null ? null : FindContract(previousRoute.TenderNumber, previousRoute.ContractNumber, selectedProgram))
                       ?? scopedContracts.FirstOrDefault();
        var homeContract = previousHome is null
            ? contract
            : FindContract(previousHome.TenderNumber, previousHome.ContractNumber, previousHome.ProgramType) ?? contract;
        var progressContract = previousProgress is null
            ? null
            : FindContract(previousProgress.TenderNumber, previousProgress.ContractNumber, previousProgress.ProgramType);
        var outputContract = previousOutput is null
            ? contract
            : FindContract(previousOutput.TenderNumber, previousOutput.ContractNumber, previousOutput.ProgramType) ?? contract;

        _suppressSelectionEvents = true;
        try
        {
            HomeTenderListBox.ItemsSource = _contracts.ToArray();
            RouteTenderListBox.ItemsSource = _contracts.ToArray();
            ProgressTenderListBox.ItemsSource = _contracts.ToArray();
            OutputTenderListBox.ItemsSource = _contracts.ToArray();
            RefreshTenderList();
            RefreshContractorList();

            HomeTenderListBox.SelectedItem = homeContract;
            RouteTenderListBox.SelectedItem = contract;
            ProgressTenderListBox.SelectedItem = progressContract;
            OutputTenderListBox.SelectedItem = outputContract;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateContractorSummary();
        if (contract is not null)
        {
            UpdateRouteContractorSelector(contract);
            UpdateOutputRoutesForContract(outputContract);
            UpdateHomeTenderSummary(homeContract);
        }
        else
        {
            RouteContractorListBox.ItemsSource = Array.Empty<ContractorDraft>();
            RoutesListBox.ItemsSource = Array.Empty<RouteDraft>();
        }

        UpdateProgressContractorAndRoutes(progressContract, preserveSelection: false);
    }

    private void UpdateRouteContractorSelector(ContractDraftData? contract)
    {
        _suppressSelectionEvents = true;
        try
        {
            RouteContractorListBox.ItemsSource = contract is null
                ? Array.Empty<ContractorDraft>()
                : _contractors.Where(x => x.ProgramType == contract.ProgramType).ToArray();
            RouteContractorListBox.SelectedIndex = -1;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateRouteProgramTypeFields(contract);
        UpdateRouteListView();
        RouteSelectedTenderTextBlock.Text = contract is null ? "İhale: seçilmedi" : $"İhale: {contract.TenderNumber}";
        RouteSelectedContractorTextBlock.Text = "Yüklenici: firma seçilmemiş";
    }

    private void UpdateRouteListView()
    {
        RefreshContractWorkNameDisplays();
        RefreshRoutePanelList();
    }

    private void UpdateProgressPeriodOptions(ContractDraftData? contract, bool preserveSelection)
    {
        var previousPeriod = preserveSelection
            ? (ProgressPeriodMonthComboBox.SelectedItem as ProgressPaymentMonthOption)?.PeriodDate
            : null;

        var options = contract is null
            ? Array.Empty<ProgressPaymentMonthOption>()
            : ProgressPaymentMonthService.BuildOptions(contract.StartDate, contract.EndDate).ToArray();

        ProgressPeriodMonthComboBox.ItemsSource = options;
        ProgressPeriodMonthComboBox.SelectedItem = previousPeriod.HasValue
            ? ProgressPaymentMonthService.FindForPeriod(options, previousPeriod.Value)
            : null;
    }

    private void ProgressPeriodMonthComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _suppressSelectionEvents || _editingHistoryId.HasValue || IsArchiveEditing)
            return;

        var selected = ProgressPeriodMonthComboBox.SelectedItem as ProgressPaymentMonthOption;
        var monthKey = selected?.StartDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var transition = ResolveMonthlyDraftPeriodTransition(_lastSelectedPaymentMonthKey, monthKey);
        if (transition.ResetMonthlyDraft)
            ResetMonthlyInputsForNewMonth();
        else if (_lastSelectedPaymentMonthKey is null && monthKey is not null)
            RestoreHybridInputsForSelectedPeriod();

        // Programmatic clearing of a ComboBox can produce a transient null between
        // period selections. Forgetting the old month here can leak a cached route's
        // unsaved October values into a newly selected November claim.
        _lastSelectedPaymentMonthKey = transition.LastMonthKey;
        UpdateAutomaticProgressNumber();
    }

    internal static (string? LastMonthKey, bool ResetMonthlyDraft) ResolveMonthlyDraftPeriodTransition(
        string? previousMonthKey, string? selectedMonthKey)
    {
        if (selectedMonthKey is null)
            return (previousMonthKey, false);

        var changed = previousMonthKey is not null &&
            !string.Equals(previousMonthKey, selectedMonthKey, StringComparison.Ordinal);
        return (selectedMonthKey, changed);
    }

    private void ResetMonthlyInputsForNewMonth()
    {
        WorkDaysTextBox.Text = "0";
        GuideWorkDaysTextBox.Text = "0";
        DiscountTextBox.Text = "0";
        PriceDifferenceTextBox.Text = "0";
        GuideDiscountTextBox.Text = "0";
        GuidePriceDifferenceTextBox.Text = "0";
        TaxDebtTextBox.Text = "0";
        SocialSecurityDebtTextBox.Text = "0";
        MinimumWageSupportTextBox.Text = "0";
        SetMinimumWageSupportInputMode((ProgressTenderListBox.SelectedItem as ContractDraftData)?.ProgramType);
        InspectionPenaltyTextBox.Text = "0";

        // A month change starts a fresh draft for every route, including routes
        // temporarily deselected from the visible grid. Otherwise their cached
        // prior-month adjustments could reappear when selected again.
        _multiRouteDraftCache.ResetAll(row =>
        {
            row.ClearPeriodChanges();
            row.WorkDays = 0;
            row.DiscountAmount = 0m;
            row.PriceDifference = 0m;
            row.GuideWorkDays = 0;
            row.GuideDiscountAmount = 0m;
            row.GuidePriceDifference = 0m;
            row.TaxDebt = 0m;
            row.SocialSecurityDebt = 0m;
            var draftRoute = _routes.FirstOrDefault(route =>
                string.Equals(
                    OehsIdentityService.BuildRouteKey(
                        route.TenderNumber,
                        route.RouteNumber,
                        route.ContractorTaxOrIdentityNumber,
                        route.ContractNumber,
                        route.ProgramType),
                    row.RouteKey,
                    StringComparison.Ordinal));
            row.MinimumWageSupportEnabled =
                draftRoute is not null &&
                draftRoute.ProgramType != TransportationProgramType.BasicOrSecondary;
            row.MinimumWageSupport = 0m;
            row.InspectionPenalty = 0m;
            if (draftRoute is not null)
            {
                row.RouteName = draftRoute.Name;
                row.WorkDays = 0m;
                row.DailyPrice = draftRoute.DailyPrice;
                row.GuideDailyPrice = HasGuideStaff(draftRoute.ProgramType) ? draftRoute.GuideDailyPrice : 0m;
            }
            if (draftRoute is not null) SuggestPeriodDailyPrices(row, draftRoute);
            RestoreSavedHybridInput(row);
            row.AcceptChanges();
        });
        RefreshHybridUpdateButton();

        _lastCalculatedHistoryId = null;
        ArchiveCurrentProgressButton.Visibility = Visibility.Collapsed;
        CorrectCurrentProgressButton.Visibility = Visibility.Collapsed;
        ClearProgressValidationVisuals();
        ProgressValidationBanner.Visibility = Visibility.Collapsed;
        ProgressValidationMessageTextBlock.Text = string.Empty;
        ServiceAmountResultTextBlock.Text = FormatMoney(0m);
        GuideServiceAmountResultTextBlock.Text = FormatMoney(0m);
        TransportPriceDifferenceResultTextBlock.Text = FormatMoney(0m);
        GuidePriceDifferenceResultTextBlock.Text = FormatMoney(0m);
        CombinedPriceDifferenceResultTextBlock.Text = FormatMoney(0m);
        TotalAmountResultTextBlock.Text = FormatMoney(0m);
        VatResultTextBlock.Text = FormatMoney(0m);
        DeductionsResultTextBlock.Text = FormatMoney(0m);
        GrossResultTextBlock.Text = FormatMoney(0m);
        NetResultTextBlock.Text = FormatMoney(0m);
        NetControlResultTextBlock.Text = FormatMoney(0m);
        WithholdingResultTextBlock.Text = FormatMoney(0m);
        StampTaxResultTextBlock.Text = FormatMoney(0m);
        TaxDebtResultTextBlock.Text = FormatMoney(0m);
        SocialSecurityDebtResultTextBlock.Text = FormatMoney(0m);
        MinimumWageSupportResultTextBlock.Text = FormatMoney(0m);
        InspectionPenaltyResultTextBlock.Text = FormatMoney(0m);
        AdditionalGuaranteeResultTextBlock.Text = FormatMoney(0m);
        ProgressWorkDaysSummaryTextBlock.Text = "0";
        ProgressWorkDaysCardTextBlock.Text = "0 gün";
        ProgressNetSummaryTextBlock.Text = FormatMoney(0m);
        ProgressCalculationSummaryTextBlock.Text = "HAZIR";
        CalculationStatusTextBlock.Text = "Yeni ay seçildi. Bu aya ait iş günlerini, fiyat farklarını, iskontoları ve kesintileri yeniden girin.";
    }

    private void UpdateAutomaticProgressNumber()
    {
        var contract = ProgressTenderListBox.SelectedItem as ContractDraftData;
        var selected = ProgressPeriodMonthComboBox.SelectedItem as ProgressPaymentMonthOption;
        var selectedRoutes = ProgressRouteComboBox.SelectedItems.OfType<RouteDraft>().ToArray();
        if (contract is null || selected is null || selectedRoutes.Length == 0)
        {
            ProgressNumberTextBox.Clear();
            return;
        }

        var routeNumbers = selectedRoutes
            .Select(route => new
            {
                Route = route,
                Number = MonthlyProgressPaymentIdentityService.SuggestNextRouteNumber(
                    _progressHistory,
                    contract.TenderNumber,
                    contract.ContractNumber,
                    route.ContractorTaxOrIdentityNumber,
                    route.RouteNumber,
                    contract.ProgramType,
                    selected.PeriodDate)
            })
            .OrderBy(x => x.Route.RouteNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // A combined batch can contain routes at different points in their payment
        // history, so each route has its own Excel-compatible official number.
        ProgressNumberTextBox.Text = routeNumbers.Length == 1
            ? routeNumbers[0].Number.ToString(CultureInfo.CurrentCulture)
            : string.Join(" • ", routeNumbers.Select(x => $"{x.Route.RouteNumber}: {x.Number}"));
    }

    private void SelectProgressPeriod(DateOnly? periodDate)
    {
        var options = ProgressPeriodMonthComboBox.ItemsSource as IEnumerable<ProgressPaymentMonthOption>
                      ?? Array.Empty<ProgressPaymentMonthOption>();
        ProgressPeriodMonthComboBox.SelectedItem = periodDate.HasValue
            ? ProgressPaymentMonthService.FindForPeriod(options, periodDate.Value)
            : null;
    }

    private static bool ProgressPeriodDiffers(ComboBox comboBox, DateOnly? expected)
    {
        if (!expected.HasValue)
            return comboBox.SelectedItem is not null;

        if (comboBox.SelectedItem is not ProgressPaymentMonthOption actual)
            return true;

        // Historical records can carry any day in a month. The UI is month-based,
        // so compare the calendar month rather than the clipped period end date.
        return actual.StartDate.Year != expected.Value.Year
               || actual.StartDate.Month != expected.Value.Month;
    }

    private void UpdateProgressContractorAndRoutes(ContractDraftData? contract, bool preserveSelection = true)
    {
        UpdateProgressProgramTypeFields(contract);
        UpdateProgressPeriodOptions(contract, preserveSelection);
        if (contract is null)
        {
            _suppressSelectionEvents = true;
            try
            {
                ProgressContractorComboBox.ItemsSource = Array.Empty<ContractorDraft>();
                ProgressContractorComboBox.SelectedItem = null;
                ProgressContractorTextBlock.Text = "Firma seçiniz";
                ProgressRouteComboBox.ItemsSource = Array.Empty<RouteDraft>();
                ProgressRouteComboBox.SelectedItems.Clear();
                ProgressRouteRelationTextBlock.Text = "İhale, sözleşme, firma ve hat seçiniz.";
            }
            finally
            {
                _suppressSelectionEvents = false;
            }
            UpdateMultiRouteDraftRows();
            UpdateProgressRouteDefaults();
            return;
        }

        var contractRoutes = _routes.Where(x => IsRouteForContract(x, contract)).ToArray();
        // Do not silently treat routes without a tax/identity number as eligible for
        // contractor selection: matching by a blank identifier could mix businesses.
        // Keep them out of the payment selection until their identity data is fixed,
        // and explicitly disclose the excluded count in the UI below.
        var routesMissingContractorIdentity = contractRoutes.Count(x =>
            string.IsNullOrWhiteSpace(x.ContractorTaxOrIdentityNumber));
        var previousContractor = preserveSelection
            ? ProgressContractorComboBox.SelectedItem as ContractorDraft
            : null;
        var firms = contractRoutes
            .Where(x => !string.IsNullOrWhiteSpace(x.ContractorTaxOrIdentityNumber))
            .GroupBy(x => OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber), StringComparer.Ordinal)
            .Select(group =>
            {
                var route = group.First();
                return _contractors.FirstOrDefault(x =>
                           x.ProgramType == contract.ProgramType &&
                           string.Equals(
                               OehsIdentityService.NormalizeIdentity(x.TaxOrIdentityNumber),
                               group.Key,
                               StringComparison.Ordinal))
                       ?? new ContractorDraft(
                           string.IsNullOrWhiteSpace(route.ContractorName) ? "Firma adı tanımlanmamış" : route.ContractorName,
                           route.ContractorTaxOrIdentityNumber,
                           ProgramType: route.ProgramType);
            })
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        _suppressSelectionEvents = true;
        try
        {
            ProgressContractorComboBox.ItemsSource = firms;
            var matchedPrevious = previousContractor is null
                ? null
                : firms.FirstOrDefault(x => string.Equals(
                    OehsIdentityService.NormalizeIdentity(x.TaxOrIdentityNumber),
                    OehsIdentityService.NormalizeIdentity(previousContractor.TaxOrIdentityNumber),
                    StringComparison.Ordinal));
            ProgressContractorComboBox.SelectedItem = matchedPrevious
                ?? (firms.Length == 1 ? firms[0] : null);

            var selectedFirm = ProgressContractorComboBox.SelectedItem as ContractorDraft;
            var visibleRoutes = selectedFirm is null
                ? Array.Empty<RouteDraft>()
                : contractRoutes.Where(x => string.Equals(
                    OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
                    OehsIdentityService.NormalizeIdentity(selectedFirm.TaxOrIdentityNumber),
                    StringComparison.Ordinal)).ToArray();

            var previousSelectedKeys = preserveSelection
                ? ProgressRouteComboBox.SelectedItems.OfType<RouteDraft>()
                    .Select(x => OehsIdentityService.BuildRouteKey(
                        x.TenderNumber, x.RouteNumber, x.ContractorTaxOrIdentityNumber, x.ContractNumber, x.ProgramType))
                    .ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            ProgressRouteComboBox.ItemsSource = visibleRoutes;
            ProgressRouteComboBox.SelectedItems.Clear();
            foreach (var route in visibleRoutes)
            {
                var key = OehsIdentityService.BuildRouteKey(
                    route.TenderNumber, route.RouteNumber, route.ContractorTaxOrIdentityNumber, route.ContractNumber, route.ProgramType);
                if (previousSelectedKeys.Contains(key))
                    ProgressRouteComboBox.SelectedItems.Add(route);
            }
            ProgressContractorTextBlock.Text = selectedFirm is null
                ? (firms.Length == 0 ? "Bu sözleşmede firmaya bağlı hat bulunamadı." : "Devam etmek için firmayı seçin.")
                : $"{selectedFirm.Name} • {visibleRoutes.Length} hat";
            var routeSelectionMessage = selectedFirm is null
                ? (firms.Length == 0 ? "Vergi/T.C. kimlik numarası doğrulanmış firma bulunamadı." : "Önce yüklenici firmayı seçiniz.")
                : visibleRoutes.Length == 0 ? "Bu firmaya bağlı hat bulunamadı." : "Yalnızca seçilen firmaya ait hatlar listeleniyor.";
            ProgressRouteRelationTextBlock.Text = routesMissingContractorIdentity > 0
                ? $"{routeSelectionMessage} UYARI: {routesMissingContractorIdentity} hat firma vergi/T.C. kimlik numarası eksik olduğu için seçime alınmadı; ödeme öncesi hat kaydını düzeltin."
                : routeSelectionMessage;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateMultiRouteDraftRows();
        UpdateProgressRouteDefaults();
    }

    private void ProgressContractorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _suppressSelectionEvents || IsArchiveEditing)
            return;

        var contract = ProgressTenderListBox.SelectedItem as ContractDraftData;
        var contractor = ProgressContractorComboBox.SelectedItem as ContractorDraft;
        var previousKeys = ProgressRouteComboBox.SelectedItems.OfType<RouteDraft>()
            .Select(x => OehsIdentityService.BuildRouteKey(
                x.TenderNumber, x.RouteNumber, x.ContractorTaxOrIdentityNumber, x.ContractNumber, x.ProgramType))
            .ToHashSet(StringComparer.Ordinal);

        var routes = contract is null || contractor is null
            ? Array.Empty<RouteDraft>()
            : _routes.Where(x =>
                IsRouteForContract(x, contract) &&
                string.Equals(
                    OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
                    OehsIdentityService.NormalizeIdentity(contractor.TaxOrIdentityNumber),
                    StringComparison.Ordinal)).ToArray();

        _suppressSelectionEvents = true;
        try
        {
            ProgressRouteComboBox.ItemsSource = routes;
            ProgressRouteComboBox.SelectedItems.Clear();
            foreach (var route in routes)
            {
                var key = OehsIdentityService.BuildRouteKey(
                    route.TenderNumber, route.RouteNumber, route.ContractorTaxOrIdentityNumber, route.ContractNumber, route.ProgramType);
                if (previousKeys.Contains(key))
                    ProgressRouteComboBox.SelectedItems.Add(route);
            }
            ProgressContractorTextBlock.Text = contractor is null
                ? "Devam etmek için firmayı seçin."
                : $"{contractor.Name} • {routes.Length} hat";
            ProgressRouteRelationTextBlock.Text = contractor is null
                ? "Önce yüklenici firmayı seçiniz."
                : routes.Length == 0 ? "Bu firmaya bağlı hat bulunamadı." : "Yalnızca seçilen firmaya ait hatlar listeleniyor.";
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateMultiRouteDraftRows();
        UpdateProgressRouteDefaults();
    }

    private void UpdateHomeTenderSummary(ContractDraftData? contract)
    {
        if (contract is null)
        {
            HomeSelectedTenderSummaryTextBlock.Text = "İhale/sözleşme seçiniz.";
            return;
        }
        HomeSelectedTenderSummaryTextBlock.Text =
            $"İKN {contract.TenderNumber} • {contract.ProgramDisplayText} • " +
            $"{contract.StartDate:dd.MM.yyyy}–{contract.EndDate:dd.MM.yyyy} • " +
            $"{_routes.Count(x => IsRouteForContract(x, contract))} hat";
    }

    private static void ShowAlreadyRegistered(string message)
    {
        MessageBox.Show(message, "OEHS", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string BuildArchivePdfFileName(ProgressPaymentHistoryData payment)
    {
        if (payment.ProgressPaymentNumber.HasValue && payment.PeriodDate.HasValue)
        {
            return OehsFileNameService.BuildProgressPdfFileName(
                payment.TenderNumber ?? string.Empty,
                payment.ContractNumber ?? string.Empty,
                payment.RouteNumber ?? string.Empty,
                payment.ProgressPaymentNumber.Value,
                payment.PeriodDate.Value,
                payment.Input.ProgramType,
                payment.BatchRouteNumbers);
        }

        return "OEHS_Hakediş.pdf";
    }

    private void ConfigureArchiveCorrectionChangeTracking()
    {
        foreach (var textBox in new TextBox[]
        {
            TenderNumberTextBox, ContractNumberTextBox,
            ContractAmountTextBox, ContractWorkDaysTextBox, ContractTransportDailyPriceTextBox,
            ContractorNameTextBox, ContractorTaxTextBox, ContractorBankTextBox,
            RouteNumberTextBox, RouteNameTextBox, RouteDailyPriceTextBox, RouteContractAmountTextBox,
            RouteGuideDailyPriceTextBox,
            WorkDaysTextBox, DailyPriceTextBox, GuideWorkDaysTextBox, GuideDailyPriceTextBox,
            DiscountTextBox, GuideDiscountTextBox, PriceDifferenceTextBox, GuidePriceDifferenceTextBox,
            TaxDebtTextBox, SocialSecurityDebtTextBox, MinimumWageSupportTextBox, InspectionPenaltyTextBox,
            ProgressNumberTextBox
        })
        {
            textBox.TextChanged += ArchiveCorrectionInput_TextChanged;
        }

        foreach (var datePicker in new DatePicker[]
        {
            StartDatePicker, EndDatePicker, TenderDatePicker, ContractDatePicker, RouteContractDatePicker
        })
        {
            datePicker.SelectedDateChanged += ArchiveCorrectionInput_DateChanged;
        }

        ProgressPeriodMonthComboBox.SelectionChanged += ArchiveCorrectionInput_DateChanged;
        ContractorVatExemptCheckBox.Checked += ArchiveCorrectionInput_Toggled;
        ContractorVatExemptCheckBox.Unchecked += ArchiveCorrectionInput_Toggled;
    }

    private void ArchiveCorrectionInput_TextChanged(object? sender, TextChangedEventArgs e) =>
        RefreshArchiveCorrectionStatus();

    private void ArchiveCorrectionInput_DateChanged(object? sender, SelectionChangedEventArgs e) =>
        RefreshArchiveCorrectionStatus();

    private void ArchiveCorrectionInput_Toggled(object? sender, RoutedEventArgs e) =>
        RefreshArchiveCorrectionStatus();

    private static bool TextDiffers(TextBox textBox, string? expected) =>
        !string.Equals(textBox.Text.Trim(), expected?.Trim() ?? string.Empty, StringComparison.Ordinal);

    private static bool DecimalDiffers(TextBox textBox, decimal expected) =>
        !TryParseDecimalOrZero(textBox.Text, out var actual) || actual != expected;

    private static bool IntDiffers(TextBox textBox, int expected)
    {
        if (string.IsNullOrWhiteSpace(textBox.Text))
            return expected != 0;

        return !int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var actual) ||
               actual != expected;
    }

    private static bool TryParseWorkDays(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        var separatorIndex = trimmed.LastIndexOf(separator, StringComparison.Ordinal);
        if (separatorIndex >= 0)
        {
            var fractionalDigits = trimmed.Length - separatorIndex - separator.Length;
            if (fractionalDigits > 20)
                return false;
        }

        return decimal.TryParse(
            trimmed,
            NumberStyles.Number,
            CultureInfo.CurrentCulture,
            out value);
    }

    private static bool DecimalWorkDaysDiffer(TextBox textBox, decimal expected)
    {
        if (string.IsNullOrWhiteSpace(textBox.Text))
            return expected != 0m;

        return !decimal.TryParse(textBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var actual) ||
               actual != expected;
    }

    private static bool NullableIntDiffers(TextBox textBox, int? expected)
    {
        if (string.IsNullOrWhiteSpace(textBox.Text))
            return expected.HasValue;

        return !int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var actual) ||
               !expected.HasValue || actual != expected.Value;
    }

    private static bool DatePickerDiffers(DatePicker picker, DateOnly? expected)
    {
        DateOnly? actual = picker.SelectedDate.HasValue
            ? DateOnly.FromDateTime(picker.SelectedDate.Value)
            : null;
        return actual != expected;
    }

    private bool ArchiveCorrectionInputsDiffer()
    {
        if (_editingArchiveSnapshot is not ProgressPaymentArchive archive)
            return false;

        return _archiveCorrectionSection switch
        {
            ArchiveCorrectionSection.Contract =>
                TextDiffers(TenderNumberTextBox, archive.Contract.TenderNumber) ||
                TextDiffers(ContractNumberTextBox, archive.Contract.ContractNumber) ||
                DecimalDiffers(ContractAmountTextBox, archive.Contract.ContractAmount) ||
                IntDiffers(ContractWorkDaysTextBox, archive.Contract.ContractWorkDays) ||
                DecimalDiffers(ContractTransportDailyPriceTextBox, archive.Contract.TransportDailyPrice) ||
                DatePickerDiffers(StartDatePicker, archive.Contract.StartDate) ||
                DatePickerDiffers(EndDatePicker, archive.Contract.EndDate) ||
                DatePickerDiffers(TenderDatePicker, archive.Contract.TenderDate) ||
                DatePickerDiffers(ContractDatePicker, archive.Contract.ContractDate),

            ArchiveCorrectionSection.Contractor =>
                TextDiffers(ContractorNameTextBox, archive.Contractor.Name) ||
                !string.Equals(
                    OehsIdentityService.NormalizeIdentity(ContractorTaxTextBox.Text),
                    OehsIdentityService.NormalizeIdentity(archive.Contractor.TaxOrIdentityNumber),
                    StringComparison.Ordinal) ||
                TextDiffers(ContractorBankTextBox, archive.Contractor.BankAccountNumber),

            ArchiveCorrectionSection.Route =>
                TextDiffers(RouteNumberTextBox, archive.Route.RouteNumber) ||
                TextDiffers(RouteNameTextBox, archive.Route.Name) ||
                DecimalDiffers(RouteDailyPriceTextBox, archive.Route.DailyPrice) ||
                DecimalDiffers(RouteContractAmountTextBox, archive.Route.ContractAmount ?? archive.ProgressPayment.Input.ContractAmount) ||
                DatePickerDiffers(RouteContractDatePicker, archive.Route.ContractDate ?? archive.ProgressPayment.ContractDate ?? archive.Contract.ContractDate) ||
                DecimalDiffers(RouteGuideDailyPriceTextBox, archive.Route.GuideDailyPrice),

            ArchiveCorrectionSection.ProgressPayment =>
                DecimalWorkDaysDiffer(WorkDaysTextBox, archive.ProgressPayment.Input.WorkDays) ||
                DecimalDiffers(DailyPriceTextBox, archive.ProgressPayment.Input.DailyPrice) ||
                DecimalWorkDaysDiffer(GuideWorkDaysTextBox, archive.ProgressPayment.Input.GuideWorkDays) ||
                DecimalDiffers(GuideDailyPriceTextBox, archive.ProgressPayment.Input.GuideDailyPrice) ||
                DecimalDiffers(DiscountTextBox, archive.ProgressPayment.Input.DiscountAmount) ||
                DecimalDiffers(GuideDiscountTextBox, archive.ProgressPayment.Input.GuideDiscountAmount) ||
                DecimalDiffers(PriceDifferenceTextBox, archive.ProgressPayment.Input.PriceDifference) ||
                DecimalDiffers(GuidePriceDifferenceTextBox, archive.ProgressPayment.Input.GuidePriceDifference) ||
                DecimalDiffers(TaxDebtTextBox, archive.ProgressPayment.Input.TaxDebt) ||
                DecimalDiffers(SocialSecurityDebtTextBox, archive.ProgressPayment.Input.SocialSecurityDebt) ||
                DecimalDiffers(MinimumWageSupportTextBox, archive.ProgressPayment.Input.MinimumWageSupport) ||
                DecimalDiffers(InspectionPenaltyTextBox, archive.ProgressPayment.Input.InspectionPenalty) ||
                NullableIntDiffers(ProgressNumberTextBox, archive.ProgressPayment.ProgressPaymentNumber) ||
                ProgressPeriodDiffers(ProgressPeriodMonthComboBox, archive.ProgressPayment.PeriodDate),

            _ => false
        };
    }

    private bool TryCreateArchiveCorrectionPreview(
        out ProgressPaymentInput input,
        out ProgressPaymentCalculation calculation,
        out string message)
    {
        var archive = _editingArchiveSnapshot!;
        input = archive.ProgressPayment.Input;
        calculation = archive.ProgressPayment.Calculation;
        message = string.Empty;

        switch (_archiveCorrectionSection)
        {
            case ArchiveCorrectionSection.Contract:
                if (!HasValidTenderDateText(StartDatePicker) || !HasValidTenderDateText(EndDatePicker) || !HasValidTenderDateText(TenderDatePicker))
                {
                    message = "İhale ve hizmet tarihlerinin metnini kontrol edin.";
                    return false;
                }
                input = archive.ProgressPayment.Input;
                break;

            case ArchiveCorrectionSection.Contractor:
                // Firma bilgileri hesap girdisini değiştirmez; aynı girdilerle net tutarı yeniden göster.
                break;

            case ArchiveCorrectionSection.Route:
                if (!TurkishMoneyInput.TryParseCalculationAmount(RouteDailyPriceTextBox.Text, out var routePrice) || routePrice < 0 ||
                    !TurkishMoneyInput.TryParseCents(RouteContractAmountTextBox.Text, out var routeAmount) || routeAmount < 0 ||
                    !RouteContractDatePicker.SelectedDate.HasValue || !HasValidTenderDateText(RouteContractDatePicker))
                {
                    message = "Hat fiyatı, sözleşme bedeli ve tarihini kontrol edin.";
                    return false;
                }
                if (!TurkishMoneyInput.TryParseCalculationAmount(RouteGuideDailyPriceTextBox.Text, out var guideDailyPrice) || guideDailyPrice < 0)
                {
                    message = "Rehber günlük fiyatını geçerli bir tutar olarak girin.";
                    return false;
                }
                input = input with
                {
                    DailyPrice = (!input.HasPeriodChanges && input.TransportPeriods is null) && archive.Route.DailyPrice != routePrice ? routePrice : input.DailyPrice,
                    ContractAmount = (archive.Route.ContractAmount ?? input.ContractAmount) != routeAmount ? routeAmount : input.ContractAmount,
                    GuideDailyPrice = (!input.HasPeriodChanges && input.GuidePeriods is null) && archive.Route.GuideDailyPrice != guideDailyPrice ? guideDailyPrice : input.GuideDailyPrice
                };
                break;

            case ArchiveCorrectionSection.ProgressPayment:
                if (!TryParseWorkDays(WorkDaysTextBox.Text, out var workDays) || workDays < 0)
                {
                    message = "Taşıma iş gününü kontrol edin (0 veya daha büyük; en fazla 20 ondalık basamak).";
                    return false;
                }
                if (!TryParseWorkDays(GuideWorkDaysTextBox.Text, out var paymentGuideWorkDays) || paymentGuideWorkDays < 0)
                {
                    message = "Rehber iş gününü kontrol edin.";
                    return false;
                }
                if (!TryParseRequiredMoney(DailyPriceTextBox.Text, out var dailyPrice) || dailyPrice < 0)
                {
                    message = "Taşıma günlük fiyatını kontrol edin.";
                    return false;
                }
                if (!TryParseRequiredMoney(GuideDailyPriceTextBox.Text, out var paymentGuideDailyPrice) || paymentGuideDailyPrice < 0)
                {
                    message = "Rehber günlük fiyatını kontrol edin.";
                    return false;
                }
                if (!TryParseDecimalOrZero(DiscountTextBox.Text, out var discount) ||
                    !TryParseDecimalOrZero(GuideDiscountTextBox.Text, out var guideDiscount) ||
                    !TryParseDecimalOrZero(PriceDifferenceTextBox.Text, out var priceDifference) ||
                    !TryParseDecimalOrZero(GuidePriceDifferenceTextBox.Text, out var guidePriceDifference))
                {
                    message = "İskonto ve fiyat farkı alanlarını tutar olarak girin.";
                    return false;
                }
                if (!TryParseDecimalOrZero(TaxDebtTextBox.Text, out var taxDebt) || taxDebt < 0 ||
                    !TryParseDecimalOrZero(SocialSecurityDebtTextBox.Text, out var socialSecurityDebt) || socialSecurityDebt < 0 ||
                    !TryParseDecimalOrZero(MinimumWageSupportTextBox.Text, out var minimumWageSupport) || minimumWageSupport < 0 ||
                    !TryParseDecimalOrZero(InspectionPenaltyTextBox.Text, out var inspectionPenalty) || inspectionPenalty < 0)
                {
                    message = "Borç, destek ve ceza alanları negatif olamaz; tutarları kontrol edin.";
                    return false;
                }
                input = input with
                {
                    WorkDays = workDays,
                    DailyPrice = dailyPrice,
                    GuideWorkDays = paymentGuideWorkDays,
                    GuideDailyPrice = paymentGuideDailyPrice,
                    DiscountAmount = discount,
                    GuideDiscountAmount = guideDiscount,
                    PriceDifference = priceDifference,
                    GuidePriceDifference = guidePriceDifference,
                    TaxDebt = taxDebt,
                    SocialSecurityDebt = socialSecurityDebt,
                    MinimumWageSupport = minimumWageSupport,
                    InspectionPenalty = inspectionPenalty
                };
                break;

            default:
                message = "Düzenleme bölümü belirlenemedi.";
                return false;
        }

        try
        {
            input = AttachEditingPeriods(input);
            calculation = ProgressPaymentCalculator.Calculate(input, _calculationConstants);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            message = ex.Message;
            return false;
        }
    }

    private void UpdateProgressCalculationDisplay(ProgressPaymentCalculation result, decimal workDays)
    {
        ServiceAmountResultTextBlock.Text = FormatMoney(result.ServiceAmount);
        GuideServiceAmountResultTextBlock.Text = FormatMoney(result.GuideServiceAmount);
        TransportPriceDifferenceResultTextBlock.Text = FormatMoney(result.PriceDifferenceExcludingVat);
        GuidePriceDifferenceResultTextBlock.Text = FormatMoney(result.GuidePriceDifferenceExcludingVat);
        CombinedPriceDifferenceResultTextBlock.Text = FormatMoney(result.CombinedPriceDifferenceExcludingVat);
        TotalAmountResultTextBlock.Text = FormatMoney(result.TotalAmount);
        VatResultTextBlock.Text = FormatMoney(result.Vat);
        DeductionsResultTextBlock.Text = FormatMoney(result.TotalDeductions);
        GrossResultTextBlock.Text = FormatMoney(result.GrossAmount);
        NetResultTextBlock.Text = FormatMoney(result.NetPayable);
        NetControlResultTextBlock.Text = FormatMoney(result.NetPayable);
        WithholdingResultTextBlock.Text = FormatMoney(result.Withholding);
        StampTaxResultTextBlock.Text = FormatMoney(result.StampTax);
        TaxDebtResultTextBlock.Text = FormatMoney(result.Deductions.TaxDebt);
        SocialSecurityDebtResultTextBlock.Text = FormatMoney(result.Deductions.SocialSecurityDebt);
        MinimumWageSupportResultTextBlock.Text = FormatMoney(result.Deductions.MinimumWageSupport);
        InspectionPenaltyResultTextBlock.Text = FormatMoney(result.Deductions.InspectionPenalty);
        AdditionalGuaranteeResultTextBlock.Text = FormatMoney(result.AdditionalGuarantee);
        ProgressWorkDaysSummaryTextBlock.Text = workDays.ToString(CultureInfo.CurrentCulture);

        ProgressNetSummaryTextBlock.Text = FormatMoney(result.NetPayable);
    }

    private void SetArchiveCorrectionStatus(string message, bool warning)
    {
        var brush = new System.Windows.Media.SolidColorBrush(
            warning
                ? System.Windows.Media.Color.FromRgb(230, 163, 40)
                : System.Windows.Media.Color.FromRgb(36, 104, 216));

        switch (_archiveCorrectionSection)
        {
            case ArchiveCorrectionSection.Contract:
                ContractStatusTextBlock.Text = message;
                ContractStatusTextBlock.Foreground = brush;
                ContractStatusTextBlock.FontWeight = warning ? FontWeights.Bold : FontWeights.SemiBold;
                break;
            case ArchiveCorrectionSection.Contractor:
                ContractorStatusTextBlock.Text = message;
                ContractorStatusTextBlock.Foreground = brush;
                ContractorStatusTextBlock.FontWeight = warning ? FontWeights.Bold : FontWeights.SemiBold;
                break;
            case ArchiveCorrectionSection.Route:
                RouteStatusTextBlock.Text = message;
                RouteStatusTextBlock.Foreground = brush;
                RouteStatusTextBlock.FontWeight = warning ? FontWeights.Bold : FontWeights.SemiBold;
                break;
            case ArchiveCorrectionSection.ProgressPayment:
                CalculationStatusTextBlock.Text = message;
                CalculationStatusTextBlock.Foreground = brush;
                CalculationStatusTextBlock.FontWeight = warning ? FontWeights.Bold : FontWeights.SemiBold;
                break;
        }
    }

    private void ClearLiveHistoryCorrectionContext()
    {
        _historyCorrectionTargetId = null;
        _historyCorrectionSection = HistoryCorrectionSection.None;
    }

    private void SetLiveHistoryCorrectionContext(Guid targetId, HistoryCorrectionSection section)
    {
        _historyCorrectionTargetId = targetId;
        _historyCorrectionSection = section;
        RefreshArchiveCorrectionStatus();
    }

    private ProgressPaymentHistoryData? GetLiveHistoryCorrectionTarget() =>
        _historyCorrectionTargetId is Guid id
            ? _progressHistory.FirstOrDefault(x => IsOperationPayment(x, id))
            : null;

    private void SetHistoryCorrectionStatus(string message, bool warning)
    {
        var brush = new System.Windows.Media.SolidColorBrush(
            warning
                ? System.Windows.Media.Color.FromRgb(230, 163, 40)
                : System.Windows.Media.Color.FromRgb(36, 104, 216));

        switch (_historyCorrectionSection)
        {
            case HistoryCorrectionSection.Contract:
                ContractStatusTextBlock.Text = message;
                ContractStatusTextBlock.Foreground = brush;
                ContractStatusTextBlock.FontWeight = warning ? FontWeights.Bold : FontWeights.SemiBold;
                break;
            case HistoryCorrectionSection.Contractor:
                ContractorStatusTextBlock.Text = message;
                ContractorStatusTextBlock.Foreground = brush;
                ContractorStatusTextBlock.FontWeight = warning ? FontWeights.Bold : FontWeights.SemiBold;
                break;
            case HistoryCorrectionSection.Route:
                RouteStatusTextBlock.Text = message;
                RouteStatusTextBlock.Foreground = brush;
                RouteStatusTextBlock.FontWeight = warning ? FontWeights.Bold : FontWeights.SemiBold;
                break;
            case HistoryCorrectionSection.ProgressPayment:
                CalculationStatusTextBlock.Text = message;
                CalculationStatusTextBlock.Foreground = brush;
                CalculationStatusTextBlock.FontWeight = warning ? FontWeights.Bold : FontWeights.SemiBold;
                break;
        }
    }

    private bool HistoryCorrectionInputsDiffer()
    {
        var item = GetLiveHistoryCorrectionTarget();
        if (item is null)
            return false;

        return _historyCorrectionSection switch
        {
            HistoryCorrectionSection.Contract =>
                _contracts.FirstOrDefault(x =>
                    string.Equals(x.TenderNumber, item.TenderNumber, StringComparison.OrdinalIgnoreCase)) is not ContractDraftData contract ||
                TextDiffers(TenderNumberTextBox, contract.TenderNumber) ||
                TextDiffers(ContractNumberTextBox, contract.ContractNumber) ||
                DecimalDiffers(ContractAmountTextBox, contract.ContractAmount) ||
                IntDiffers(ContractWorkDaysTextBox, contract.ContractWorkDays) ||
                DecimalDiffers(ContractTransportDailyPriceTextBox, contract.TransportDailyPrice) ||
                DatePickerDiffers(StartDatePicker, contract.StartDate) ||
                DatePickerDiffers(EndDatePicker, contract.EndDate) ||
                DatePickerDiffers(TenderDatePicker, contract.TenderDate) ||
                DatePickerDiffers(ContractDatePicker, contract.ContractDate),

            HistoryCorrectionSection.Contractor =>
                FindContractor(item.ContractorTaxOrIdentityNumber ?? string.Empty, item.Input.ProgramType) is not ContractorDraft contractor ||
                TextDiffers(ContractorNameTextBox, contractor.Name) ||
                !string.Equals(
                    OehsIdentityService.NormalizeIdentity(ContractorTaxTextBox.Text),
                    OehsIdentityService.NormalizeIdentity(contractor.TaxOrIdentityNumber),
                    StringComparison.Ordinal) ||
                TextDiffers(ContractorBankTextBox, contractor.BankAccountNumber) ||
                (ContractorVatExemptCheckBox.IsChecked == true) != contractor.VatExempt,

            HistoryCorrectionSection.Route =>
                _routes.FirstOrDefault(x =>
                    string.Equals(
                        OehsIdentityService.BuildRouteKey(x.TenderNumber, x.RouteNumber, x.ContractorTaxOrIdentityNumber, x.ContractNumber, x.ProgramType),
                        OehsIdentityService.BuildRouteKey(
                            item.TenderNumber ?? string.Empty,
                            item.RouteNumber ?? string.Empty,
                            item.ContractorTaxOrIdentityNumber ?? string.Empty,
                            item.ContractNumber ?? string.Empty,
                            item.Input.ProgramType),
                        StringComparison.Ordinal)) is not RouteDraft route ||
                TextDiffers(RouteNumberTextBox, route.RouteNumber) ||
                TextDiffers(RouteNameTextBox, route.Name) ||
                DecimalDiffers(RouteDailyPriceTextBox, route.DailyPrice) ||
                DecimalDiffers(RouteContractAmountTextBox, route.ContractAmount ?? item.Input.ContractAmount) ||
                DatePickerDiffers(RouteContractDatePicker, route.ContractDate ?? item.ContractDate) ||
                DecimalDiffers(RouteGuideDailyPriceTextBox, route.GuideDailyPrice),

            HistoryCorrectionSection.ProgressPayment =>
                DecimalWorkDaysDiffer(WorkDaysTextBox, item.Input.WorkDays) ||
                DecimalDiffers(DailyPriceTextBox, item.Input.DailyPrice) ||
                DecimalWorkDaysDiffer(GuideWorkDaysTextBox, item.Input.GuideWorkDays) ||
                DecimalDiffers(GuideDailyPriceTextBox, item.Input.GuideDailyPrice) ||
                DecimalDiffers(DiscountTextBox, item.Input.DiscountAmount) ||
                DecimalDiffers(GuideDiscountTextBox, item.Input.GuideDiscountAmount) ||
                DecimalDiffers(PriceDifferenceTextBox, item.Input.PriceDifference) ||
                DecimalDiffers(GuidePriceDifferenceTextBox, item.Input.GuidePriceDifference) ||
                DecimalDiffers(TaxDebtTextBox, item.Input.TaxDebt) ||
                DecimalDiffers(SocialSecurityDebtTextBox, item.Input.SocialSecurityDebt) ||
                DecimalDiffers(MinimumWageSupportTextBox, item.Input.MinimumWageSupport) ||
                DecimalDiffers(InspectionPenaltyTextBox, item.Input.InspectionPenalty) ||
                NullableIntDiffers(ProgressNumberTextBox, item.ProgressPaymentNumber) ||
                ProgressPeriodDiffers(ProgressPeriodMonthComboBox, item.PeriodDate),

            _ => false
        };
    }

    private bool TryCreateHistoryCorrectionPreview(
        out ProgressPaymentInput input,
        out ProgressPaymentCalculation calculation,
        out string message)
    {
        var item = GetLiveHistoryCorrectionTarget();
        input = item?.Input ?? default!;
        calculation = item?.Calculation ?? default!;
        message = string.Empty;
        if (item is null)
        {
            message = "Düzenlenen hakediş geçmişinde bulunamadı. Kayıt değiştirilmedi.";
            return false;
        }

        switch (_historyCorrectionSection)
        {
            case HistoryCorrectionSection.Contract:
                if (string.IsNullOrWhiteSpace(TenderNumberTextBox.Text))
                {
                    message = "İhale Kayıt Numarası (İKN) zorunludur.";
                    return false;
                }
                if (!HasValidTenderDateText(StartDatePicker) || !HasValidTenderDateText(EndDatePicker) || !HasValidTenderDateText(TenderDatePicker))
                {
                    message = "İhale ve hizmet tarihlerinin metnini kontrol edin.";
                    return false;
                }
                input = item.Input;
                break;

            case HistoryCorrectionSection.Contractor:
                // Contractor identity/address changes do not alter the calculation inputs.
                input = item.Input;
                break;

            case HistoryCorrectionSection.Route:
                if (string.IsNullOrWhiteSpace(RouteNumberTextBox.Text) || string.IsNullOrWhiteSpace(RouteNameTextBox.Text))
                {
                    message = "Hat No ve hat adı zorunludur.";
                    return false;
                }
                if (!TurkishMoneyInput.TryParseCalculationAmount(RouteDailyPriceTextBox.Text, out var routePrice) || routePrice < 0 ||
                    !TurkishMoneyInput.TryParseCents(RouteContractAmountTextBox.Text, out var routeAmount) || routeAmount < 0 ||
                    !RouteContractDatePicker.SelectedDate.HasValue || !HasValidTenderDateText(RouteContractDatePicker))
                {
                    message = "Hat fiyatı, sözleşme bedeli ve tarihini kontrol edin.";
                    return false;
                }
                if (!TurkishMoneyInput.TryParseCalculationAmount(RouteGuideDailyPriceTextBox.Text, out var guideDailyPrice) || guideDailyPrice < 0)
                {
                    message = "Rehber günlük fiyatını geçerli bir tutar olarak girin.";
                    return false;
                }
                input = item.Input with
                {
                    DailyPrice = (!item.Input.HasPeriodChanges && item.Input.TransportPeriods is null) && _editingRoute?.DailyPrice != routePrice ? routePrice : item.Input.DailyPrice,
                    ContractAmount = (_editingRoute?.ContractAmount ?? item.Input.ContractAmount) != routeAmount ? routeAmount : item.Input.ContractAmount,
                    GuideDailyPrice = (!item.Input.HasPeriodChanges && item.Input.GuidePeriods is null) && _editingRoute?.GuideDailyPrice != guideDailyPrice ? guideDailyPrice : item.Input.GuideDailyPrice
                };
                break;

            case HistoryCorrectionSection.ProgressPayment:
                if (!TryParseWorkDays(WorkDaysTextBox.Text, out var workDays) || workDays < 0)
                {
                    message = "Taşıma iş gününü kontrol edin (0 veya daha büyük; en fazla 20 ondalık basamak).";
                    return false;
                }
                if (!TryParseWorkDays(GuideWorkDaysTextBox.Text, out var paymentGuideWorkDays) || paymentGuideWorkDays < 0)
                {
                    message = "Rehber iş gününü kontrol edin.";
                    return false;
                }
                if (!TryParseRequiredMoney(DailyPriceTextBox.Text, out var dailyPrice) || dailyPrice < 0)
                {
                    message = "Taşıma günlük fiyatını kontrol edin.";
                    return false;
                }
                if (!TryParseRequiredMoney(GuideDailyPriceTextBox.Text, out var paymentGuideDailyPrice) || paymentGuideDailyPrice < 0)
                {
                    message = "Rehber günlük fiyatını kontrol edin.";
                    return false;
                }
                if (!TryParseDecimalOrZero(DiscountTextBox.Text, out var discount) ||
                    !TryParseDecimalOrZero(GuideDiscountTextBox.Text, out var guideDiscount) ||
                    !TryParseDecimalOrZero(PriceDifferenceTextBox.Text, out var priceDifference) ||
                    !TryParseDecimalOrZero(GuidePriceDifferenceTextBox.Text, out var guidePriceDifference))
                {
                    message = "İskonto ve fiyat farkı alanlarını tutar olarak girin.";
                    return false;
                }
                if (!TryParseDecimalOrZero(TaxDebtTextBox.Text, out var taxDebt) || taxDebt < 0 ||
                    !TryParseDecimalOrZero(SocialSecurityDebtTextBox.Text, out var socialSecurityDebt) || socialSecurityDebt < 0 ||
                    !TryParseDecimalOrZero(MinimumWageSupportTextBox.Text, out var minimumWageSupport) || minimumWageSupport < 0 ||
                    !TryParseDecimalOrZero(InspectionPenaltyTextBox.Text, out var inspectionPenalty) || inspectionPenalty < 0)
                {
                    message = "Borç, destek ve ceza alanları negatif olamaz; tutarları kontrol edin.";
                    return false;
                }
                input = item.Input with
                {
                    WorkDays = workDays,
                    DailyPrice = dailyPrice,
                    GuideWorkDays = paymentGuideWorkDays,
                    GuideDailyPrice = paymentGuideDailyPrice,
                    DiscountAmount = discount,
                    GuideDiscountAmount = guideDiscount,
                    PriceDifference = priceDifference,
                    GuidePriceDifference = guidePriceDifference,
                    TaxDebt = taxDebt,
                    SocialSecurityDebt = socialSecurityDebt,
                    MinimumWageSupport = minimumWageSupport,
                    InspectionPenalty = inspectionPenalty
                };
                break;

            default:
                message = "Düzenleme bölümü belirlenemedi.";
                return false;
        }

        try
        {
            input = AttachEditingPeriods(input);
            calculation = ProgressPaymentCalculator.Calculate(input, _calculationConstants);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            message = ex.Message;
            return false;
        }
    }

    private void RefreshHistoryCorrectionStatus()
    {
        var item = GetLiveHistoryCorrectionTarget();
        if (item is null)
            return;

        if (!HistoryCorrectionInputsDiffer())
        {
            UpdateProgressCalculationDisplay(item.Calculation, item.Input.WorkDays);
            ProgressCalculationSummaryTextBlock.Text = "KAYITLI HESAP";
            SetHistoryCorrectionStatus("Hakediş düzeltme modu açık. Henüz değişiklik yok.", warning: false);
            return;
        }

        if (!TryCreateHistoryCorrectionPreview(out var input, out var calculation, out var message))
        {
            ProgressCalculationSummaryTextBlock.Text = "KONTROL GEREKLİ";
            SetHistoryCorrectionStatus(
                $"DEĞİŞİKLİK ALGILANDI — {message} Net ödeme önizlemesi için alanı düzeltin.",
                warning: true);
            return;
        }

        UpdateProgressCalculationDisplay(calculation, input.WorkDays);
        ProgressCalculationSummaryTextBlock.Text = "ÖNİZLEME • KAYDEDİLMEDİ";
        SetHistoryCorrectionStatus(
            $"DEĞİŞİKLİK ALGILANDI — Net ödeme: {FormatMoney(item.Calculation.NetPayable)} → " +
            $"{FormatMoney(calculation.NetPayable)}. DÜZENLE düğmesine basıp onayladığınızda mevcut hakediş kaydı güncellenecek.",
            warning: true);
    }

    private bool ConfirmLiveHistoryCorrection(string sectionName, ProgressPaymentHistoryData? proposedPayment = null)
    {
        if (_historyCorrectionTargetId is not Guid targetId ||
            _historyCorrectionSection == HistoryCorrectionSection.None ||
            !HistoryCorrectionInputsDiffer())
            return true;

        var original = _progressHistory.FirstOrDefault(x => IsOperationPayment(x, targetId));
        if (original is null)
        {
            SetHistoryCorrectionStatus("Düzenlenen hakediş artık geçmiş listesinde bulunmuyor; kayıt değiştirilmedi.", warning: true);
            return false;
        }

        if (proposedPayment is null)
        {
            if (!TryCreateHistoryCorrectionPreview(out var previewInput, out var preview, out var message))
            {
                SetHistoryCorrectionStatus($"Düzeltme onaylanamadı: {message}", warning: true);
                return false;
            }
            proposedPayment = original with { Input = previewInput, Calculation = preview };
        }
        var confirmation = ShowPaymentComparison("OEHS — Hakediş Düzeltme Onayı",
            $"{sectionName} değişiklikleri uygulansın mı? Mevcut canlı kayıt güncellenecek.",
            original, proposedPayment, LiveEditorDifferences(original));

        if (confirmation == MessageBoxResult.Yes)
            return true;

        SetHistoryCorrectionStatus("Değişiklikler iptal edildi; mevcut hakediş kaydı korunuyor. Alanları düzenleyip yeniden onaylayabilirsiniz.", warning: true);
        return false;
    }

    private void RefreshViewsAfterHistoryCorrection()
    {
        var targetId = _historyCorrectionTargetId;
        UpdateHistoryView();
        UpdateSummaryView();
        UpdateReportView();
        UpdateHomeSummaryView();
        UpdateAcceptanceView();

        if (targetId is Guid id && _progressHistory.FirstOrDefault(x => IsOperationPayment(x, id)) is ProgressPaymentHistoryData item)
        {
            _lastCalculatedHistoryId = id;

            var contract = FindContract(item.TenderNumber, item.ContractNumber, item.Input.ProgramType);
            _suppressSelectionEvents = true;
            try
            {
                ProgressTenderListBox.SelectedItem = contract;
                ProgressRouteComboBox.ItemsSource = contract is null
                    ? Array.Empty<RouteDraft>()
                    : _routes.Where(x => IsRouteForContract(x, contract)).ToArray();
                ProgressRouteComboBox.SelectedItem = _routes.FirstOrDefault(x =>
                    string.Equals(x.TenderNumber, item.TenderNumber, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.ContractNumber, item.ContractNumber, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.RouteNumber, item.RouteNumber, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
                        OehsIdentityService.NormalizeIdentity(item.ContractorTaxOrIdentityNumber),
                        StringComparison.Ordinal));
            }
            finally
            {
                _suppressSelectionEvents = false;
            }

            ProgressContractorTextBlock.Text = item.ContractorName ?? "Seçilmedi";
            ProgressRouteRelationTextBlock.Text = $"İhale: {item.TenderNumber} • Hat: {item.RouteNumber} — {item.RouteName}";
            WorkDaysTextBox.Text = item.Input.WorkDays.ToString(CultureInfo.CurrentCulture);
            DailyPriceTextBox.Text = FormatCalculationInput(item.Input.DailyPrice);
            GuideWorkDaysTextBox.Text = item.Input.GuideWorkDays.ToString(CultureInfo.CurrentCulture);
            GuideDailyPriceTextBox.Text = FormatCalculationInput(item.Input.GuideDailyPrice);
            DiscountTextBox.Text = FormatCalculationInput(item.Input.DiscountAmount);
            GuideDiscountTextBox.Text = FormatCalculationInput(item.Input.GuideDiscountAmount);
            PriceDifferenceTextBox.Text = FormatCalculationInput(item.Input.PriceDifference);
            GuidePriceDifferenceTextBox.Text = FormatCalculationInput(item.Input.GuidePriceDifference);
            TaxDebtTextBox.Text = FormatCalculationInput(item.Input.TaxDebt);
            SocialSecurityDebtTextBox.Text = FormatCalculationInput(item.Input.SocialSecurityDebt);
            MinimumWageSupportTextBox.Text = FormatCalculationInput(item.Input.MinimumWageSupport);
            RestoreMinimumWageSupportInputState(item.Input.ProgramType, item.Input.MinimumWageSupport);
            InspectionPenaltyTextBox.Text = FormatCalculationInput(item.Input.InspectionPenalty);
            ProgressNumberTextBox.Text = item.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            SelectProgressPeriod(item.PeriodDate);
            UpdateProgressCalculationDisplay(item.Calculation, item.Input.WorkDays);
            ProgressCalculationSummaryTextBlock.Text = "TAMAM";
            SetProgressEditorMode(false);
        }

        ClearLiveHistoryCorrectionContext();
    }

    private void RefreshArchiveCorrectionStatus()
    {
        if (_suppressArchiveDirtyTracking)
            return;

        if (IsArchiveCorrection && _editingArchiveSnapshot is not null)
        {
            var archive = _editingArchiveSnapshot;
            if (!ArchiveCorrectionInputsDiffer())
            {
                UpdateProgressCalculationDisplay(archive.ProgressPayment.Calculation, archive.ProgressPayment.Input.WorkDays);
                ProgressCalculationSummaryTextBlock.Text = "KAYITLI HESAP";
                SetArchiveCorrectionStatus("Arşiv düzeltme modu açık. Henüz değişiklik yok.", warning: false);
                return;
            }

            if (!TryCreateArchiveCorrectionPreview(out var archiveInput, out var archiveCalculation, out var archiveMessage))
            {
                ProgressCalculationSummaryTextBlock.Text = "KONTROL GEREKLİ";
                SetArchiveCorrectionStatus(
                    $"DEĞİŞİKLİK ALGILANDI — {archiveMessage} Net ödeme önizlemesi için alanı düzeltin.",
                    warning: true);
                return;
            }

            UpdateProgressCalculationDisplay(archiveCalculation, archiveInput.WorkDays);
            ProgressCalculationSummaryTextBlock.Text = "ÖNİZLEME • KAYDEDİLMEDİ";
            SetArchiveCorrectionStatus(
                $"DEĞİŞİKLİK ALGILANDI — Net ödeme: {FormatMoney(archive.ProgressPayment.Calculation.NetPayable)} → " +
                $"{FormatMoney(archiveCalculation.NetPayable)}. DÜZENLE düğmesine basıp onayladığınızda mevcut arşiv kaydı güncellenir.",
                warning: true);
            return;
        }

        if (_historyCorrectionTargetId.HasValue && _historyCorrectionSection != HistoryCorrectionSection.None)
            RefreshHistoryCorrectionStatus();
    }

    private static bool TryParseDecimalOrZero(string? text, out decimal value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = 0m;
            return true;
        }

        return TryParseRequiredMoney(text, out value);
    }

    private static bool TryParseRequiredMoney(string? text, out decimal value)
    {
        value = 0m;
        var input = text?.Trim();
        if (string.IsNullOrEmpty(input)) return false;
        return TurkishMoneyInput.TryParseCalculationAmount(input, out value);
    }

    private bool SaveArchiveCorrectionSnapshot(ProgressPaymentArchive updatedArchive, string sectionName)
    {
        var correctionSection = _archiveCorrectionSection;

        if (IsArchiveCorrection && !ArchiveCorrectionInputsDiffer())
        {
            SetArchiveCorrectionStatus("Herhangi bir değişiklik algılanmadı; arşiv kaydı değiştirilmedi.", warning: false);
            return false;
        }

        try
        {
            // Recalculate from saved inputs before asking permission to replace the archive.
            var recalculatedPayment = updatedArchive.ProgressPayment with
            {
                Calculation = ProgressPaymentCalculator.Calculate(updatedArchive.ProgressPayment.Input, _calculationConstants)
            };
            updatedArchive = updatedArchive with
            {
                ProgressPayment = recalculatedPayment,
                PdfFileName = BuildArchivePdfFileName(recalculatedPayment)
            };

            var originalArchive = _editingArchiveSnapshot!;
            var newNetPayable = recalculatedPayment.Calculation.NetPayable;
            var differences = PaymentOperationsService.CompareFields(originalArchive.Contract, updatedArchive.Contract, "Sözleşme")
                .Concat(CompareContractorFields(originalArchive.Contractor, updatedArchive.Contractor))
                .Concat(PaymentOperationsService.CompareFields(originalArchive.Route, updatedArchive.Route, "Hat")).ToArray();
            var confirmation = ShowPaymentComparison("OEHS — Değişiklik Onayı",
                $"{sectionName} değişiklikleri uygulansın mı? Yeni revizyon oluşturulacak; v{originalArchive.Revision:00} korunacak.",
                originalArchive.ProgressPayment, recalculatedPayment, differences);

            if (confirmation != MessageBoxResult.Yes)
            {
                CalculationStatusTextBlock.Text = "Değişiklikler uygulanmadı; arşiv kaydı korunuyor.";
                ArchiveStatusTextBlock.Text = "DEĞİŞİKLİK İPTAL EDİLDİ";
                SetArchiveCorrectionStatus("Değişiklikler iptal edildi; arşiv kaydı korunuyor. Alanlarınızı gözden geçirip yeniden onaylayabilirsiniz.", warning: true);
                return false;
            }

            if (string.IsNullOrWhiteSpace(_editingArchiveSourcePath))
                throw new ProgressPaymentArchiveException("Güncellenecek arşiv dosyası belirlenemedi.");

            updatedArchive = updatedArchive with
            {
                ArchivedAt = DateTime.UtcNow,
                Source = "ArchiveCorrection"
            };
            var finalPath = ProgressPaymentArchiveStore.SaveRevision(_editingArchiveSourcePath, updatedArchive);
            updatedArchive = ProgressPaymentArchiveStore.Load(finalPath);
            recalculatedPayment = updatedArchive.ProgressPayment;

            var historyMirrorUpdated = false;
            var historyMirrorSaved = true;
            var matchingHistoryIndex = _progressHistory
                .Select((item, index) => (item, index))
                .Where(pair => pair.item.Id == recalculatedPayment.Id && pair.item.Input.ProgramType == recalculatedPayment.Input.ProgramType)
                .Select(pair => pair.index)
                .DefaultIfEmpty(-1)
                .First();

            if (matchingHistoryIndex >= 0)
            {
                // Keep the editable history row synchronized with its authoritative archive snapshot.
                // If the secondary JSON save fails, roll the in-memory mirror back instead of
                // displaying unsaved data as though it were persisted. The archive revision remains
                // authoritative and the warning below reports that the mirror could not be saved.
                var previousHistoryItem = _progressHistory[matchingHistoryIndex];
                _progressHistory[matchingHistoryIndex] = recalculatedPayment;
                historyMirrorUpdated = true;
                historyMirrorSaved = SavePersistedData();
                if (!historyMirrorSaved)
                {
                    _progressHistory[matchingHistoryIndex] = previousHistoryItem;
                    historyMirrorUpdated = false;
                }
                UpdateHistoryView();
                UpdateSummaryView();
                UpdateReportView();
                UpdateHomeSummaryView();
                UpdateAcceptanceView();
            }

            ArchiveStatusTextBlock.Text = $"YENİ ARŞİV REVİZYONU v{updatedArchive.Revision:00} OLUŞTURULDU";
            CalculationStatusTextBlock.Text =
                !historyMirrorSaved
                    ? $"{sectionName} düzeltmesi yeni arşiv revision'ı olarak kaydedildi; ancak ana hakediş geçmişi diske yazılamadı. Kayıt durumunu kontrol edin."
                    : historyMirrorUpdated
                        ? $"{sectionName} değişiklikleri yeni arşiv revision'ı ve ana hakediş geçmişine kaydedildi. Önceki revisionlar korundu."
                        : $"{sectionName} değişiklikleri yeni arşiv revision'ı v{updatedArchive.Revision:00} olarak kaydedildi. Önceki revisionlar korundu.";

            _suppressArchiveDirtyTracking = true;
            try
            {
                ClearArchivedEditorFields(correctionSection);
                ClearArchiveCorrectionState();
            }
            finally
            {
                _suppressArchiveDirtyTracking = false;
            }
            UpdateArchiveView();
            NavigateToArchives();

            // Panel navigation and selection refreshes can restore previous payment
            // editor values. Clear the hakediş inputs after navigation as a final guard.
            if (correctionSection == ArchiveCorrectionSection.ProgressPayment)
            {
                ClearProgressEntryFields();
                SetProgressEditorMode(false);
            }

            MessageBox.Show(
                $"{sectionName} değişiklikleri başarıyla uygulandı.\n\n" +
                $"Yeni hesaplanan net tutar: {FormatMoney(newNetPayable)}\n" +
                $"Yeni arşiv revision'ı: v{updatedArchive.Revision:00}. Önceki revisionlar korunmuştur.\n" +
                (!historyMirrorSaved
                    ? "DİKKAT: Arşiv güncellendi ancak ana hakediş geçmişi diske yazılamadı. Üstteki kayıt durumunu kontrol edin.\n\n"
                    : historyMirrorUpdated
                        ? "Ana hakediş geçmişi de güncellendi.\n\n"
                        : "Bu arşiv için ana hakediş geçmişinde eşleşen satır yoktu; arşiv dosyası güncellendi.\n\n") +
                $"Dosya: {Path.GetFileName(finalPath)}",
                "OEHS — Arşiv Güncellendi",
                MessageBoxButton.OK,
                historyMirrorSaved ? MessageBoxImage.Information : MessageBoxImage.Warning);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProgressPaymentArchiveException or ArgumentOutOfRangeException)
        {
            ArchiveStatusTextBlock.Text = "ARŞİV HATASI";
            CalculationStatusTextBlock.Text = ex.Message;
            MessageBox.Show(
                $"Arşiv güncellenemedi.\n\n{ex.Message}",
                "OEHS — Arşiv Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private void ClearArchivedEditorFields(ArchiveCorrectionSection section)
    {
        switch (section)
        {
            case ArchiveCorrectionSection.Contract:
                _editingContractTender = null;
                ClearContractFormOnly();
                ClearContractValidationVisuals();
                ContractStatusTextBlock.Text = "Arşiv sözleşmesi güncellendi; giriş alanları temizlendi.";
                break;

            case ArchiveCorrectionSection.Contractor:
                _editingContractorKey = null;
                ContractorNameTextBox.Clear();
                ContractorTaxTextBox.Clear();
                ContractorBankTextBox.Clear();
                ContractorStatusTextBlock.Text = "Arşiv yüklenicisi güncellendi; giriş alanları temizlendi.";
                break;

            case ArchiveCorrectionSection.Route:
                _editingRoute = null;
                RouteNumberTextBox.Clear();
                RouteNameTextBox.Clear();
                RouteGuideDailyPriceTextBox.Clear();
                ClearRouteValidationVisuals();
                RouteStatusTextBlock.Text = "Arşiv hattı güncellendi; giriş alanları temizlendi.";
                break;

            case ArchiveCorrectionSection.ProgressPayment:
                _editingHistoryId = null;
                ClearProgressEntryFields();
                CalculationStatusTextBlock.Text = "Arşiv hakedişi güncellendi; giriş alanları temizlendi.";
                break;
        }
    }

    private void SaveArchivedContractorCorrection()
    {
        var archive = _editingArchiveSnapshot!;
        ClearContractorValidation();

        var name = ContractorNameTextBox.Text.Trim();
        var tax = OehsIdentityService.NormalizeIdentity(ContractorTaxTextBox.Text);

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(tax))
        {
            if (string.IsNullOrWhiteSpace(name)) MarkInvalid(ContractorNameTextBox);
            if (string.IsNullOrWhiteSpace(tax)) MarkInvalid(ContractorTaxTextBox);
            ShowContractorValidation("Firma adı ve TC / Vergi No zorunludur.");
            return;
        }

        var updatedContractor = new Contractor(
            name,
            tax,
            archive.Contractor.Address,
            archive.Contractor.Phone,
            archive.Contractor.Email,
            ContractorBankTextBox.Text.Trim(),
            archive.Contractor.ContactPerson,
            archive.Contract.ProgramType,
            ContractorVatExemptCheckBox.IsChecked == true);

        var updatedContract = archive.Contract with
        {
            ContractorName = name,
            ContractorTaxOrIdentityNumber = tax,
            BankAccountNumber = updatedContractor.BankAccountNumber
        };

        var payment = archive.ProgressPayment with
        {
            ContractorName = name,
            ContractorTaxOrIdentityNumber = tax,
            BankAccountNumber = updatedContractor.BankAccountNumber
        };

        var route = archive.Route with { ContractorTaxOrIdentityNumber = tax };

        var updatedPreviousPayments = archive.PreviousPayments
            .Select(previous => previous with
            {
                ContractorName = name,
                ContractorTaxOrIdentityNumber = tax,
                BankAccountNumber = updatedContractor.BankAccountNumber
            })
            .ToArray();

        SaveArchiveCorrectionSnapshot(
            archive with
            {
                Contract = updatedContract,
                Contractor = updatedContractor,
                Route = route,
                ProgressPayment = payment,
                PreviousPayments = updatedPreviousPayments,
                PdfFileName = BuildArchivePdfFileName(payment)
            },
            "Yüklenici firma");
    }

    private void SaveArchivedContractCorrection()
    {
        var archive = _editingArchiveSnapshot!;
        ClearContractValidationVisuals();

        var errors = new List<Control>();
        if (string.IsNullOrWhiteSpace(TenderNumberTextBox.Text)) errors.Add(TenderNumberTextBox);
        if (!TryReadLegacyContractAmount(ContractAmountTextBox.Text, out var contractAmount)) errors.Add(ContractAmountTextBox);
        if (!int.TryParse(ContractWorkDaysTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var contractWorkDays) || contractWorkDays < 0) errors.Add(ContractWorkDaysTextBox);
        if (!TryReadLegacyContractAmount(ContractTransportDailyPriceTextBox.Text, out var transportDailyPrice)) errors.Add(ContractTransportDailyPriceTextBox);
        if (!HasValidTenderDateText(StartDatePicker)) errors.Add(StartDatePicker);
        if (!HasValidTenderDateText(EndDatePicker)) errors.Add(EndDatePicker);
        if (!HasValidTenderDateText(TenderDatePicker)) errors.Add(TenderDatePicker);

        DateOnly? startDate = StartDatePicker.SelectedDate.HasValue
            ? DateOnly.FromDateTime(StartDatePicker.SelectedDate.Value)
            : null;
        DateOnly? endDate = EndDatePicker.SelectedDate.HasValue
            ? DateOnly.FromDateTime(EndDatePicker.SelectedDate.Value)
            : null;

        if (!startDate.HasValue) errors.Add(StartDatePicker);
        if (!endDate.HasValue) errors.Add(EndDatePicker);

        if (endDate < startDate)
        {
            errors.Add(StartDatePicker);
            errors.Add(EndDatePicker);
        }

        var archivePaymentPeriods = archive.PreviousPayments
            .Where(x => x.PeriodDate.HasValue)
            .Select(x => x.PeriodDate!.Value)
            .ToList();

        if (archive.ProgressPayment.PeriodDate is DateOnly currentPaymentPeriod)
            archivePaymentPeriods.Add(currentPaymentPeriod);

        if (startDate.HasValue && endDate.HasValue && archivePaymentPeriods.Any(paymentPeriod =>
                paymentPeriod < startDate || paymentPeriod > endDate))
        {
            errors.Add(StartDatePicker);
            errors.Add(EndDatePicker);
        }

        if (errors.Count > 0)
        {
            foreach (var control in errors) MarkInvalid(control);
            ContractStatusTextBlock.Text = "Arşiv düzeltmesi kaydedilmedi. Kırmızı alanları kontrol edin.";
            return;
        }

        var updatedContract = archive.Contract with
        {
            TenderNumber = TenderNumberTextBox.Text.Trim(),
            ContractNumber = archive.Contract.ContractNumber,
            // WorkName is a derived display; retain the archived legacy fallback.
            ContractAmount = contractAmount,
            StartDate = startDate!.Value,
            EndDate = endDate!.Value,
            VatExempt = archive.Contract.VatExempt,
            TenderDate = TenderDatePicker.SelectedDate.HasValue ? DateOnly.FromDateTime(TenderDatePicker.SelectedDate.Value) : archive.Contract.TenderDate,
            ContractDate = ContractDatePicker.SelectedDate.HasValue ? DateOnly.FromDateTime(ContractDatePicker.SelectedDate.Value) : archive.Contract.ContractDate,
            ContractWorkDays = contractWorkDays,
            TransportDailyPrice = transportDailyPrice,
            // An archive correction edits the archived contract; it must not change
            // the education program based on a possibly blank/global UI selector.
            ProgramType = archive.Contract.ProgramType
        };

        var hasGuide = HasGuideStaff(updatedContract.ProgramType);
        var input = archive.ProgressPayment.Input with
        {
            IsVatExempt = archive.ProgressPayment.Input.IsVatExempt,
            GuideWorkDays = hasGuide ? archive.ProgressPayment.Input.GuideWorkDays : 0,
            GuideDailyPrice = hasGuide ? archive.ProgressPayment.Input.GuideDailyPrice : 0m,
            GuideDiscountAmount = hasGuide ? archive.ProgressPayment.Input.GuideDiscountAmount : 0m,
            GuidePriceDifference = hasGuide ? archive.ProgressPayment.Input.GuidePriceDifference : 0m,
            MinimumWageSupport = archive.ProgressPayment.Input.MinimumWageSupport,
            ProgramType = updatedContract.ProgramType
        };

        var payment = archive.ProgressPayment with
        {
            Input = input,
            Calculation = ProgressPaymentCalculator.Calculate(input, _calculationConstants),
            WorkName = updatedContract.WorkName,
            TenderNumber = updatedContract.TenderNumber,
            ContractNumber = updatedContract.ContractNumber,
            TenderDate = updatedContract.TenderDate,
            ContractStartDate = updatedContract.StartDate,
            ContractEndDate = updatedContract.EndDate
        };

        var route = archive.Route with
        {
            TenderNumber = updatedContract.TenderNumber,
            ContractNumber = updatedContract.ContractNumber,
            GuideDailyPrice = hasGuide ? archive.Route.GuideDailyPrice : 0m,
            ProgramType = updatedContract.ProgramType
        };

        var updatedPreviousPayments = archive.PreviousPayments
            .Select(previous =>
            {
                var previousHasGuide = HasGuideStaff(updatedContract.ProgramType);
                var previousInput = previous.Input with
                {
                    IsVatExempt = archive.ProgressPayment.Input.IsVatExempt,
                    GuideWorkDays = previousHasGuide ? previous.Input.GuideWorkDays : 0,
                    GuideDailyPrice = previousHasGuide ? previous.Input.GuideDailyPrice : 0m,
                    GuideDiscountAmount = previousHasGuide ? previous.Input.GuideDiscountAmount : 0m,
                    GuidePriceDifference = previousHasGuide ? previous.Input.GuidePriceDifference : 0m,
                    MinimumWageSupport = previous.Input.MinimumWageSupport,
                    ProgramType = updatedContract.ProgramType
                };

                return previous with
                {
                    Input = previousInput,
                    Calculation = ProgressPaymentCalculator.Calculate(previousInput, _calculationConstants),
                    WorkName = updatedContract.WorkName,
                    TenderNumber = updatedContract.TenderNumber,
                    ContractNumber = updatedContract.ContractNumber,
                    TenderDate = updatedContract.TenderDate,
                    ContractStartDate = updatedContract.StartDate,
                    ContractEndDate = updatedContract.EndDate
                };
            })
            .ToArray();

        SaveArchiveCorrectionSnapshot(
            archive with
            {
                Contract = updatedContract,
                Route = route,
                ProgressPayment = payment,
                PreviousPayments = updatedPreviousPayments,
                PdfFileName = BuildArchivePdfFileName(payment)
            },
            "İhale bilgileri");
    }

    private void SaveArchivedRouteCorrection()
    {
        var archive = _editingArchiveSnapshot!;
        ClearRouteValidationVisuals();

        var routeNumber = RouteNumberTextBox.Text.Trim();
        var routeName = RouteNameTextBox.Text.Trim();

        var errors = new List<Control>();
        if (string.IsNullOrWhiteSpace(routeNumber)) errors.Add(RouteNumberTextBox);
        if (string.IsNullOrWhiteSpace(routeName)) errors.Add(RouteNameTextBox);
        if (!TurkishMoneyInput.TryParseCalculationAmount(RouteDailyPriceTextBox.Text, out var routeDailyPrice) || routeDailyPrice < 0) errors.Add(RouteDailyPriceTextBox);
        if (!TurkishMoneyInput.TryParseCents(RouteContractAmountTextBox.Text, out var routeContractAmount) || routeContractAmount < 0) errors.Add(RouteContractAmountTextBox);
        if (!RouteContractDatePicker.SelectedDate.HasValue || !HasValidTenderDateText(RouteContractDatePicker)) errors.Add(RouteContractDatePicker);
        var hasGuide = HasGuideStaff(archive.Contract.ProgramType);
        var guideDailyPrice = 0m;
        if (hasGuide)
        {
            if (!TurkishMoneyInput.TryParseCalculationAmount(RouteGuideDailyPriceTextBox.Text, out guideDailyPrice) || guideDailyPrice < 0) errors.Add(RouteGuideDailyPriceTextBox);
        }

        if (errors.Count > 0)
        {
            foreach (var control in errors) MarkInvalid(control);
            RouteStatusTextBlock.Text = "Arşiv düzeltmesi kaydedilmedi. Kırmızı alanları kontrol edin.";
            return;
        }

        var updatedRoute = archive.Route with
        {
            RouteNumber = routeNumber,
            Name = routeName,
            DailyPrice = routeDailyPrice,
            ContractAmount = routeContractAmount,
            ContractDate = DateOnly.FromDateTime(RouteContractDatePicker.SelectedDate!.Value),
            GuideDailyPrice = hasGuide ? guideDailyPrice : 0m,
            ProgramType = archive.Contract.ProgramType
        };

        var input = archive.ProgressPayment.Input with
        {
            DailyPrice = (!archive.ProgressPayment.Input.HasPeriodChanges && archive.ProgressPayment.Input.TransportPeriods is null) && archive.Route.DailyPrice != routeDailyPrice ? routeDailyPrice : archive.ProgressPayment.Input.DailyPrice,
            ContractAmount = (archive.Route.ContractAmount ?? archive.ProgressPayment.Input.ContractAmount) != routeContractAmount ? routeContractAmount : archive.ProgressPayment.Input.ContractAmount,
            GuideDailyPrice = hasGuide ? ((!archive.ProgressPayment.Input.HasPeriodChanges && archive.ProgressPayment.Input.GuidePeriods is null) && archive.Route.GuideDailyPrice != guideDailyPrice ? guideDailyPrice : archive.ProgressPayment.Input.GuideDailyPrice) : 0m,
            GuideDiscountAmount = hasGuide ? archive.ProgressPayment.Input.GuideDiscountAmount : 0m,
            GuidePriceDifference = hasGuide ? archive.ProgressPayment.Input.GuidePriceDifference : 0m,
            MinimumWageSupport = archive.ProgressPayment.Input.MinimumWageSupport,
            ProgramType = archive.Contract.ProgramType
        };

        var payment = archive.ProgressPayment with
        {
            Input = input,
            Calculation = ProgressPaymentCalculator.Calculate(input, _calculationConstants),
            RouteNumber = routeNumber,
            RouteName = routeName,
            ContractDate = (archive.Route.ContractDate ?? archive.ProgressPayment.ContractDate ?? archive.Contract.ContractDate) != DateOnly.FromDateTime(RouteContractDatePicker.SelectedDate!.Value)
                ? DateOnly.FromDateTime(RouteContractDatePicker.SelectedDate.Value) : archive.ProgressPayment.ContractDate
        };

        var updatedPreviousPayments = archive.PreviousPayments
            .Select(previous => previous with
            {
                RouteNumber = routeNumber,
                RouteName = routeName
            })
            .ToArray();

        SaveArchiveCorrectionSnapshot(
            archive with
            {
                Route = updatedRoute,
                ProgressPayment = payment,
                PreviousPayments = updatedPreviousPayments,
                PdfFileName = BuildArchivePdfFileName(payment)
            },
            "Hat bilgileri");
    }

    private void SaveContractorButton_Click(object sender, RoutedEventArgs e)
    {
        ClearContractorValidation();
        if (IsArchiveEditing)
        {
            if (IsArchiveCorrectionFor(ArchiveCorrectionSection.Contractor))
            {
                SaveArchivedContractorCorrection();
            }
            else
            {
                ShowAlreadyRegistered("Arşiv düzeltmesi devam ediyor. Önce ilgili düzenleme panelindeki DÜZENLEME İPTAL işlemini tamamlayın.");
            }
            return;
        }


        var name = ContractorNameTextBox.Text.Trim();
        var tax = OehsIdentityService.NormalizeIdentity(ContractorTaxTextBox.Text);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(tax))
        {
            if (string.IsNullOrWhiteSpace(name)) MarkInvalid(ContractorNameTextBox);
            if (string.IsNullOrWhiteSpace(tax)) MarkInvalid(ContractorTaxTextBox);
            ShowContractorValidation("Firma adı ve TC / Vergi No zorunludur.");
            return;
        }

        var editingContractor = FindContractorByKey(_editingContractorKey);
        if (!string.IsNullOrWhiteSpace(_editingContractorKey) && editingContractor is null)
        {
            ShowContractorValidation("Düzenlenen firma bulunamadı. Düzenlemeyi iptal edip yeni kayıt açın; firma kaydedilmedi.");
            return;
        }
        if (editingContractor is null &&
            !TryGetSelectedTransportationProgramType(out _))
        {
            MarkInvalid(ContractorProgramComboBox);
            ShowContractorValidation("Önce TAŞIMA PROGRAMI alanından Özel Eğitim Taşıma veya Temel Eğitim/Ortaöğretim Taşıma programını seçin. Firma kaydedilmedi.");
            return;
        }

        var contractorProgramType = editingContractor?.ProgramType ??
            (TryGetSelectedTransportationProgramType(out var selectedContractorProgram)
                ? selectedContractorProgram
                : throw new InvalidOperationException("Yeni firma için taşıma programı seçilmelidir."));
        var duplicate = _contractors.FirstOrDefault(x =>
            x.ProgramType == contractorProgramType &&
            string.Equals(
                OehsIdentityService.NormalizeIdentity(x.TaxOrIdentityNumber),
                tax,
                StringComparison.Ordinal) &&
            !ReferenceEquals(x, editingContractor));

        if (duplicate is not null)
        {
            MarkInvalid(ContractorTaxTextBox);
            ShowContractorValidation($"Bu TC / Vergi No ile '{duplicate.Name}' aynı programda zaten kayıtlı. Listeden DÜZENLE düğmesini kullanın; mükerrer firma oluşturulmadı.");
            return;
        }

        var updated = new ContractorDraft(
            name,
            tax,
            editingContractor?.Address ?? string.Empty,
            editingContractor?.Phone ?? string.Empty,
            editingContractor?.Email ?? string.Empty,
            ContractorBankTextBox.Text.Trim(),
            editingContractor?.ContactPerson ?? string.Empty,
            contractorProgramType,
            ContractorVatExemptCheckBox.IsChecked == true);

        if (!ConfirmLiveHistoryCorrection("Yüklenici bilgileri"))
            return;

        var contractsBeforeSave = _contracts.ToArray();
        var contractorsBeforeSave = _contractors.ToArray();
        var routesBeforeSave = _routes.ToArray();
        var hybridDraftsBeforeSave = SavedHybridPaymentDrafts.ToArray();
        var hybridKeysBeforeSave = _multiRouteDraftCache.ToArray();
        var historyBeforeSave = _progressHistory.ToArray();
        var editingContractorKeyBeforeSave = _editingContractorKey;

        if (_editingContractorKey is null)
        {
            _contractors.Add(updated);
        }
        else
        {
            var existing = FindContractorByKey(_editingContractorKey);

            if (existing is null)
                _contractors.Add(updated);
            else
            {
                var index = _contractors.IndexOf(existing);
                try
                {
                    UpdateReferencesForContractorEdit(existing, updated);
                    _contractors[index] = updated;
                }
                catch (ProgressPaymentPropagationException ex)
                {
                    ContractorStatusTextBlock.Text = $"Firma kaydedilmedi: {ex.Message}";
                    return;
                }
            }
        }

        if (!SavePersistedData())
        {
            RestoreHybridDraftIdentitySnapshot(hybridDraftsBeforeSave,hybridKeysBeforeSave,routesBeforeSave);
            RestoreLiveDataCollections(
                contractsBeforeSave, contractorsBeforeSave, routesBeforeSave, historyBeforeSave);
            _editingContractorKey = editingContractorKeyBeforeSave;
            RefreshViewsAfterPersistenceRollback();
            SetContractorEditorMode(!string.IsNullOrWhiteSpace(editingContractorKeyBeforeSave));
            ContractorStatusTextBlock.Text =
                "Firma kaydı diske yazılamadı. Bellekteki değişiklik geri alındı; form bilgileri yeniden deneyebilmeniz için korundu.";
            return;
        }

        _editingContractorKey = null;
        UpdateContractorSummary();
        UpdateContractSelectors();
        UpdateHomeSummaryView();
        if (_historyCorrectionSection == HistoryCorrectionSection.Contractor)
            RefreshViewsAfterHistoryCorrection();

        ClearContractorButton_Click(sender, e);
        ContractorStatusTextBlock.Text = $"Firma ve KDV istisnası kaydedildi: {name}.";
    }

    private bool IsVatExemptForCurrentFirm(ContractDraftData? contract)
    {
        if (_editingArchiveSnapshot is not null)
            return _editingArchiveSnapshot.ProgressPayment.Input.IsVatExempt;

        if (ProgressContractorComboBox.SelectedItem is ContractorDraft selectedFirm)
            return selectedFirm.VatExempt;

        return contract?.VatExempt == true;
    }

    private bool HasLegacyContractVatExemption(string taxOrIdentityNumber, TransportationProgramType programType)
    {
        var normalizedIdentity = OehsIdentityService.NormalizeIdentity(taxOrIdentityNumber);
        return _contracts.Any(contract =>
            contract.VatExempt && contract.ProgramType == programType &&
            string.Equals(
                OehsIdentityService.NormalizeIdentity(contract.ContractorTaxOrIdentityNumber),
                normalizedIdentity,
                StringComparison.Ordinal));
    }

    private void ContractorVatExemptCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || _suppressSelectionEvents || IsArchiveEditing)
            return;

        var selectedState = ContractorVatExemptCheckBox.IsChecked == true ? "İSTİSNA VAR" : "İSTİSNA YOK";
        ContractorStatusTextBlock.Text =
            $"KDV istisnası firma kartında {selectedState} olarak seçildi; kalıcı olması için FİRMA KAYDET düğmesine basın.";
    }

    private void EditContractorButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveEditing)
        {
            ShowContractorValidation("Arşiv düzeltmesi sürüyor. Önce değişiklikleri kaydedin veya düzenlemeyi iptal edin.");
            return;
        }
        if (sender is not Button { Tag: ContractorDraft contractor })
            return;
        ClearArchiveCorrectionState();
        ClearContractorValidation();

        _editingContractorKey = BuildContractorKey(contractor);
        SetContractorEditorMode(true);
        ContractorNameTextBox.Text = contractor.Name;
        ContractorTaxTextBox.Text = contractor.TaxOrIdentityNumber;
        ContractorBankTextBox.Text = contractor.BankAccountNumber;
        ContractorVatExemptCheckBox.IsChecked = contractor.VatExempt;
        ContractorStatusTextBlock.Text = $"Düzenlenen firma: {contractor.Name}. Programı korunur; değişiklikleri kaydederek tamamlayın.";
    }

    private void CancelContractorEditButton_Click(object sender, RoutedEventArgs e)
    {
        var archiveCorrection = IsArchiveCorrectionFor(ArchiveCorrectionSection.Contractor);
        ClearArchiveCorrectionState();
        _editingContractorKey = null;
        ClearContractorEditorFields();
        SetContractorEditorMode(false);

        if (archiveCorrection)
        {
            NavigateToArchives();
            return;
        }

        ContractorStatusTextBlock.Text = "Firma düzenlemesi iptal edildi; kayıt değiştirilmedi.";
    }

    private void DeleteContractorButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveEditing)
        {
            ShowContractorValidation("Arşiv düzeltmesi sürerken kayıtlı firma silinemez. Önce düzenlemeyi tamamlayın veya iptal edin.");
            return;
        }
        if (sender is not Button { Tag: ContractorDraft contractor })
            return;
        ClearContractorValidation();

        var normalizedContractorIdentity = OehsIdentityService.NormalizeIdentity(contractor.TaxOrIdentityNumber);
        var usedByContract = _contracts.Any(x => x.ProgramType == contractor.ProgramType && string.Equals(
            OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
            normalizedContractorIdentity, StringComparison.Ordinal));
        var usedByRoute = _routes.Any(x => x.ProgramType == contractor.ProgramType && string.Equals(
            OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
            normalizedContractorIdentity, StringComparison.Ordinal));
        var usedByHistory = _progressHistory.Any(x => x.Input.ProgramType == contractor.ProgramType && string.Equals(
            OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
            normalizedContractorIdentity, StringComparison.Ordinal));

        if (usedByContract || usedByRoute || usedByHistory)
        {
            ShowContractorValidation($"'{contractor.Name}' kayıtlı ihale, hat veya hakedişte kullanılıyor. Önce ilişkili kayıtları düzeltin; firma silinmedi.");
            return;
        }

        var result = MessageBox.Show(
            $"'{contractor.Name}' firması silinsin mi? Bu işlem geri alınamaz.",
            "Yüklenici Firmayı Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        var wasEditingDeletedFirm = ReferenceEquals(FindContractorByKey(_editingContractorKey), contractor);
        var index = _contractors.IndexOf(contractor);
        if (index < 0) return;
        _contractors.RemoveAt(index);

        if (!SavePersistedData())
        {
            _contractors.Insert(index, contractor);
            RefreshViewsAfterPersistenceRollback();
            ContractorStatusTextBlock.Text =
                "Firma silinemedi: disk kaydı başarısız oldu; firma tekrar listeye alındı.";
            return;
        }

        UpdateContractorSummary();
        UpdateContractSelectors();
        UpdateHomeSummaryView();
        if (wasEditingDeletedFirm)
            ClearContractorButton_Click(sender, e);
        ContractorStatusTextBlock.Text = $"Firma silindi: {contractor.Name}.";
    }

    private void ClearContractorButton_Click(object sender, RoutedEventArgs e)
    {
        ClearArchiveCorrectionState();
        _editingContractorKey = null;
        ClearContractorEditorFields();
        SetContractorEditorMode(false);
        ContractorStatusTextBlock.Text = "Yeni firma kaydı için alanlar temizlendi.";
    }

    private void UpdateContractorSummary()
    {
        RefreshContractorList();
    }

    private void TransportProgramTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _suppressSelectionEvents || IsArchiveEditing)
            return;

        UpdateContractSelectors();
        UpdateRouteListView();
        UpdateContractSummaryView();
        UpdateHomeSummaryView();
        UpdateContractorSummary();
        UpdateRouteSummary();
    }

    private void UpdateReferencesForContractorEdit(ContractorDraft oldContractor, ContractorDraft updated)
    {
        var oldCore = new Contractor(
            oldContractor.Name,
            oldContractor.TaxOrIdentityNumber,
            oldContractor.Address,
            oldContractor.Phone,
            oldContractor.Email,
            oldContractor.BankAccountNumber,
            oldContractor.ContactPerson,
            oldContractor.ProgramType,
            oldContractor.VatExempt);

        var newCore = new Contractor(
            updated.Name,
            updated.TaxOrIdentityNumber,
            updated.Address,
            updated.Phone,
            updated.Email,
            updated.BankAccountNumber,
            updated.ContactPerson,
            updated.ProgramType,
            updated.VatExempt);

        // Önce tüm türetilmiş hakedişler doğrulanır; hata halinde hiçbir kaynak
        // koleksiyonu değiştirilmez.
        var propagated = ProgressPaymentPropagationService.RecalculateForContractorChange(
            _progressHistory.ToArray(),
            oldCore,
            newCore);

        var normalizedOld = OehsIdentityService.NormalizeIdentity(oldContractor.TaxOrIdentityNumber);
        var routeUpdates = _routes
            .Select((route, index) => (route, index))
            .Where(x => x.route.ProgramType == oldContractor.ProgramType)
            .Where(x => string.Equals(
                OehsIdentityService.NormalizeIdentity(x.route.ContractorTaxOrIdentityNumber),
                normalizedOld,
                StringComparison.Ordinal))
            .Select(x => (x.index, route: x.route with
            {
                ContractorTaxOrIdentityNumber = newCore.TaxOrIdentityNumber,
                ContractorName = newCore.Name
            }))
            .ToArray();

        foreach (var item in routeUpdates)
        {
            RekeyHybridDrafts(_routes[item.index],item.route);
            _routes[item.index] = item.route;
        }

        for (var index = 0; index < _contracts.Count; index++)
        {
            var contract = _contracts[index];
            if (contract.ProgramType == oldContractor.ProgramType &&
                string.Equals(
                    OehsIdentityService.NormalizeIdentity(contract.ContractorTaxOrIdentityNumber),
                    normalizedOld,
                    StringComparison.Ordinal))
            {
                _contracts[index] = contract with
                {
                    ContractorName = newCore.Name,
                    ContractorTaxOrIdentityNumber = newCore.TaxOrIdentityNumber,
                    BankAccountNumber = newCore.BankAccountNumber
                };
            }
        }

        _progressHistory.Clear();
        foreach (var item in propagated)
            _progressHistory.Add(item);
    }

    private void HomeTenderListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppressSelectionEvents) UpdateHomeSummaryView();
    }

    private void UpdateHomeSummaryView()
    {
        UpdateHomeTenderSummary(HomeTenderListBox.SelectedItem as ContractDraftData);
        RefreshHomeOperations();
    }

    private void UpdateContractSummaryView()
    {
        var lookupTender = _editingContractTender ?? TenderNumberTextBox.Text.Trim();
        var lookupContractNumber = _editingContractNumber ?? ContractNumberTextBox.Text.Trim();
        var contract = FindContract(lookupTender, lookupContractNumber, _editingContractProgramType ?? GetSelectedTransportationProgramType());
        var hasContract = contract is not null;

        ContractStatusSummaryTextBlock.Text = hasContract ? "KAYITLI" : "TASLAK";
        ContractAmountSummaryTextBlock.Text =
            TurkishMoneyInput.TryParse(ContractAmountTextBox.Text, out var amount) ? FormatMoney(amount) : FormatMoney(0);
        ContractStartSummaryTextBlock.Text =
            StartDatePicker.SelectedDate?.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture) ?? "—";
        ContractEndSummaryTextBlock.Text =
            EndDatePicker.SelectedDate?.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture) ?? "—";

        ContractSummaryTenderTextBlock.Text = string.IsNullOrWhiteSpace(TenderNumberTextBox.Text)
            ? "Bekleniyor" : TenderNumberTextBox.Text.Trim();

        ContractSummaryContractTextBlock.Text = GetSelectedTransportationProgramType() == TransportationProgramType.BasicOrSecondary
            ? "Temel Eğitim/Ortaöğretim Taşıma"
            : "Özel Eğitim Taşıma";

        ContractSummaryAmountTextBlock.Text = TurkishMoneyInput.TryParse(ContractAmountTextBox.Text, out var summaryAmount)
            ? FormatMoney(summaryAmount) : FormatMoney(0);

        var workDaysText = int.TryParse(ContractWorkDaysTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var workDays)
            ? workDays.ToString(CultureInfo.CurrentCulture) : "0";
        ContractSummaryWorkDaysTextBlock.Text = $"{workDaysText} gün";
        var routeCount = contract is null ? 0 : _routes.Count(x => IsRouteForContract(x, contract));
        ContractSummaryVatTextBlock.Text =
            $"{(ContractorVatExemptCheckBox.IsChecked == true ? "KDV İstisnası" : "KDV Uygulanır")} • {routeCount} hat";
    }

    private void HomeTenderListBox_RefreshSelection()
    {
        if (HomeTenderListBox.SelectedItem is null && _contracts.Count > 0)
            HomeTenderListBox.SelectedIndex = 0;
    }

    private void RouteTenderListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents)
            return;

        var contract = RouteTenderListBox.SelectedItem as ContractDraftData;
        _suppressSelectionEvents = true;
        try
        {
            UpdateRouteContractorSelector(contract);
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        UpdateRouteSummary();
    }

    private void RouteContractorListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents)
            return;

        UpdateRouteListView();
        var contract = RouteTenderListBox.SelectedItem as ContractDraftData;
        var contractor = RouteContractorListBox.SelectedItem as ContractorDraft;
        RouteSelectedTenderTextBlock.Text = contract is null ? "İhale: seçilmedi" : $"İhale: {contract.TenderNumber}";
        RouteSelectedContractorTextBlock.Text = contractor is null ? "Yüklenici: seçilmedi" : $"Yüklenici: {contractor.Name}";
    }

    private void ProgressTenderListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents || IsArchiveEditing)
            return;

        _lastCalculatedHistoryId = null;
        ClearProgressEntryFields(clearTenderSelection: false);
        UpdateProgressContractorAndRoutes(
            ProgressTenderListBox.SelectedItem as ContractDraftData,
            preserveSelection: false);
    }

    private void AddRouteButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveEditing)
        {
            if (IsArchiveCorrectionFor(ArchiveCorrectionSection.Route))
                SaveArchivedRouteCorrection();
            else
                ShowAlreadyRegistered("Arşiv düzeltmesi devam ediyor. Önce ilgili düzenleme panelindeki DÜZENLEME İPTAL işlemini tamamlayın.");
            return;
        }

        ClearRouteValidationVisuals();
        var contract = RouteTenderListBox.SelectedItem as ContractDraftData;
        var contractor = RouteContractorListBox.SelectedItem as ContractorDraft;
        var routeNumber = RouteNumberTextBox.Text.Trim();
        var routeName = RouteNameTextBox.Text.Trim();
        var errors = new List<string>();

        if (contract is null) errors.Add("İhale / Sözleşme seçimi");
        if (contractor is null) errors.Add("Yüklenici firma seçimi");
        if (_editingRoute is not null && !_routes.Contains(_editingRoute))
        {
            ShowRouteValidation("Düzenlenen hat bulunamadı. Düzenlemeyi iptal edip yeni kayıt açın; işlem kaydedilmedi.");
            return;
        }
        if (contract is not null && contractor is not null && contractor.ProgramType != contract.ProgramType)
        {
            errors.Add("İhale ile yüklenicinin taşıma programı aynı olmalıdır");
            MarkInvalid(RouteContractorListBox);
        }
        if (_editingRoute is not null && contract is not null && _editingRoute.ProgramType != contract.ProgramType)
        {
            errors.Add("Kayıtlı hat farklı taşıma programına taşınamaz; diğer program için yeni hat açın");
            MarkInvalid(RouteTenderListBox);
        }
        if (string.IsNullOrWhiteSpace(routeNumber))
        {
            errors.Add("Hat No");
            MarkInvalid(RouteNumberTextBox);
        }
        if (string.IsNullOrWhiteSpace(routeName))
        {
            errors.Add("Hat Adı");
            MarkInvalid(RouteNameTextBox);
        }
        if (!TurkishMoneyInput.TryParseCalculationAmount(RouteDailyPriceTextBox.Text, out var routeDailyPrice) || routeDailyPrice < 0)
        {
            errors.Add("Taşıma Günlük Fiyatı");
            MarkInvalid(RouteDailyPriceTextBox);
        }
        if (!TurkishMoneyInput.TryParseCents(RouteContractAmountTextBox.Text, out var routeContractAmount) || routeContractAmount < 0)
        {
            errors.Add("Sözleşme Bedeli");
            MarkInvalid(RouteContractAmountTextBox);
        }
        if (!RouteContractDatePicker.SelectedDate.HasValue || !HasValidTenderDateText(RouteContractDatePicker))
        {
            errors.Add("Sözleşme Tarihi");
            MarkInvalid(RouteContractDatePicker);
        }
        var hasGuide = HasGuideStaff(contract?.ProgramType ?? TransportationProgramType.SpecialEducation);
        var guideDailyPrice = 0m;
        if (hasGuide)
        {
            if (!TurkishMoneyInput.TryParseCalculationAmount(RouteGuideDailyPriceTextBox.Text, out guideDailyPrice) || guideDailyPrice < 0)
            {
                errors.Add("Rehber Personel Günlük Fiyat");
                MarkInvalid(RouteGuideDailyPriceTextBox);
            }
        }
        if (errors.Count > 0)
        {
            ShowRouteValidation("Lütfen kırmızı işaretli alanları tamamlayın: " + string.Join(", ", errors) + ".");
            return;
        }

        var proposed = new RouteDraft(
            routeNumber,
            routeName,
            routeDailyPrice,
            hasGuide ? guideDailyPrice : 0m,
            _editingRoute?.TransportWorkDays ?? 0m,
            contract!.TenderNumber,
            OehsIdentityService.NormalizeIdentity(contractor!.TaxOrIdentityNumber),
            contractor.Name,
            contract.ContractNumber,
            contract.ProgramType,
            routeContractAmount,
            DateOnly.FromDateTime(RouteContractDatePicker.SelectedDate!.Value));

        var duplicate = _routes.FirstOrDefault(x =>
            x.ProgramType == proposed.ProgramType &&
            string.Equals(x.TenderNumber, proposed.TenderNumber, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ContractNumber, proposed.ContractNumber, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
                OehsIdentityService.NormalizeIdentity(proposed.ContractorTaxOrIdentityNumber),
                StringComparison.Ordinal) &&
            string.Equals(x.RouteNumber, proposed.RouteNumber, StringComparison.OrdinalIgnoreCase) &&
            !ReferenceEquals(x, _editingRoute));
        if (duplicate is not null)
        {
            MarkInvalid(RouteNumberTextBox);
            ShowRouteValidation($"Bu hat zaten kayıtlı: İKN {proposed.TenderNumber} / Hat {proposed.RouteNumber}. Listeden DÜZENLE düğmesini kullanın.");
            return;
        }

        var proposedRouteData = new RouteData(
            proposed.RouteNumber,
            proposed.Name,
            proposed.DailyPrice,
            proposed.GuideDailyPrice,
            proposed.TransportWorkDays,
            proposed.TenderNumber,
            proposed.ContractorTaxOrIdentityNumber,
            proposed.ContractNumber, proposed.ProgramType, proposed.ContractAmount, proposed.ContractDate);
        var existingRouteData = _routes
            .Where(x => !ReferenceEquals(x, _editingRoute))
            .Select(x => new RouteData(
                x.RouteNumber,
                x.Name,
                x.DailyPrice,
                x.GuideDailyPrice,
                x.TransportWorkDays,
                x.TenderNumber,
                x.ContractorTaxOrIdentityNumber,
                x.ContractNumber, x.ProgramType, x.ContractAmount, x.ContractDate))
            .ToArray();
        var ownerConflict = RouteAssignmentValidationService.FindOwnerConflict(
            existingRouteData,
            proposedRouteData);
        if (ownerConflict is not null)
        {
            var existingOwner = FindContractor(ownerConflict.ContractorTaxOrIdentityNumber, ownerConflict.ProgramType)?.Name;
            var ownerLabel = string.IsNullOrWhiteSpace(existingOwner)
                ? "başka bir yüklenici"
                : $"'{existingOwner}'";
            ShowRouteValidation(
                $"Aynı İKN ve Hat No farklı yüklenicilere atanamaz. İKN: {proposed.TenderNumber}, " +
                $"Hat No: {proposed.RouteNumber}; mevcut yüklenici: {ownerLabel}.");
            return;
        }

        var routesBeforeSave = _routes.ToArray();
        var historyBeforeSave = _progressHistory.ToArray();
        var editingRouteBeforeSave = _editingRoute;
        var routeDraftsBeforeSave = SavedHybridPaymentDrafts.ToArray();
        var hybridRouteKeysBeforeSave = _multiRouteDraftCache.ToArray();

        if (_editingRoute is null)
        {
            _routes.Add(proposed);
        }
        else
        {
            var old = _editingRoute;
            var index = _routes.IndexOf(old);
            if (index >= 0)
            {
                try
                {
                    var propagated = BuildRoutePropagation(old, proposed);
                    var proposedTargetPayment = _historyCorrectionTargetId is Guid targetId
                        ? propagated.FirstOrDefault(x => IsOperationPayment(x, targetId))
                        : null;
                    if (!ConfirmLiveHistoryCorrection("Hat bilgileri", proposedTargetPayment))
                        return;

                    RekeyHybridDrafts(old,proposed);
                    _routes[index] = proposed;
                    _progressHistory.Clear();
                    foreach (var item in propagated)
                        _progressHistory.Add(item);
                }
                catch (ProgressPaymentPropagationException ex)
                {
                    RouteStatusTextBlock.Text = $"Hat kaydedilmedi: {ex.Message}";
                    return;
                }
            }
            _editingRoute = null;
        }

        if (!SavePersistedData())
        {
            _routes.Clear();
            foreach (var item in routesBeforeSave) _routes.Add(item);
            _progressHistory.Clear();
            foreach (var item in historyBeforeSave) _progressHistory.Add(item);
            RestoreHybridDraftIdentitySnapshot(routeDraftsBeforeSave,hybridRouteKeysBeforeSave,routesBeforeSave);
            _editingRoute = editingRouteBeforeSave;
            RefreshViewsAfterPersistenceRollback();
            SetRouteEditorMode(editingRouteBeforeSave is not null);
            RouteStatusTextBlock.Text =
                "Hat kaydı diske yazılamadı. Bellekteki değişiklik geri alındı; form bilgileri korundu.";
            return;
        }

        UpdateRouteSummary();
        UpdateRouteListView();
        UpdateProgressContractorAndRoutes(ProgressTenderListBox.SelectedItem as ContractDraftData, preserveSelection: false);
        UpdateOutputRoutesForContract(contract);
        UpdateContractSummaryView();
        UpdateHomeSummaryView();
        if (_historyCorrectionSection == HistoryCorrectionSection.Route)
            RefreshViewsAfterHistoryCorrection();
        ClearRouteButton_Click(sender, e);
        RouteStatusTextBlock.Text = $"Hat kaydedildi: {routeNumber} — {routeName}.";
    }

    private IReadOnlyList<ProgressPaymentHistoryData> BuildRoutePropagation(RouteDraft oldRoute, RouteDraft updatedRoute)
    {
        var oldData = new RouteData(
            oldRoute.RouteNumber, oldRoute.Name, oldRoute.DailyPrice,
            oldRoute.GuideDailyPrice, oldRoute.TransportWorkDays,
            oldRoute.TenderNumber, oldRoute.ContractorTaxOrIdentityNumber, oldRoute.ContractNumber, oldRoute.ProgramType, oldRoute.ContractAmount, oldRoute.ContractDate);
        var newData = new RouteData(
            updatedRoute.RouteNumber, updatedRoute.Name, updatedRoute.DailyPrice,
            updatedRoute.GuideDailyPrice, updatedRoute.TransportWorkDays,
            updatedRoute.TenderNumber, updatedRoute.ContractorTaxOrIdentityNumber, updatedRoute.ContractNumber, updatedRoute.ProgramType, updatedRoute.ContractAmount, updatedRoute.ContractDate);

        var contractor = FindContractor(updatedRoute.ContractorTaxOrIdentityNumber, updatedRoute.ProgramType);
        var contractorSnapshot = contractor is null
            ? new Contractor(updatedRoute.ContractorName, updatedRoute.ContractorTaxOrIdentityNumber, ProgramType: updatedRoute.ProgramType)
            : new Contractor(
                contractor.Name,
                contractor.TaxOrIdentityNumber,
                contractor.Address,
                contractor.Phone,
                contractor.Email,
                contractor.BankAccountNumber,
                contractor.ContactPerson,
                contractor.ProgramType,
                contractor.VatExempt);

        return ProgressPaymentPropagationService.RecalculateForRouteChange(
            _progressHistory.ToArray(), oldData, newData, contractorSnapshot, _calculationConstants);
    }

    private void ShowRouteValidation(string message)
    {
        RouteValidationMessageTextBlock.Text = message;
        RouteValidationBanner.Visibility = Visibility.Visible;
        RouteStatusTextBlock.Text = "İşlem tamamlanamadı; yukarıdaki açıklamayı kontrol edin.";
        RouteStatusTextBlock.Foreground = System.Windows.Media.Brushes.LightSteelBlue;
    }

    private void ClearRouteValidationVisuals()
    {
        RouteStatusTextBlock.Foreground = System.Windows.Media.Brushes.LightSteelBlue;
        foreach (var control in new Control[]
        {
            RouteNumberTextBox,
            RouteNameTextBox,
            RouteDailyPriceTextBox,
            RouteContractAmountTextBox,
            RouteContractDatePicker,

            RouteGuideDailyPriceTextBox
        })
            ClearInvalid(control);
        ClearInvalid(RouteTenderListBox);
        ClearInvalid(RouteContractorListBox);

        RouteValidationBanner.Visibility = Visibility.Collapsed;
        RouteValidationMessageTextBlock.Text = string.Empty;
    }

    private void RemoveRouteButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveEditing)
        {
            ShowRouteValidation("Arşiv düzeltmesi sürerken kayıtlı hat silinemez. Önce düzenlemeyi tamamlayın veya iptal edin.");
            return;
        }
        if (sender is not Button { Tag: RouteDraft route })
            return;

        var historyCount = CountRouteHistoryReferences(route);

        if (historyCount > 0)
        {
            ShowRouteValidation($"Bu hat için {historyCount} hakediş kaydı bulunduğu için hat silinmedi.");
            return;
        }

        var result = MessageBox.Show(
            $"'{route.RouteNumber} — {route.Name}' hattı silinsin mi? Bu işlem geri alınamaz.",
            "Hat Kaydını Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        RemoveRouteAfterConfirmation(route);
    }

    private void RemoveRouteAfterConfirmation(RouteDraft route)
    {
        var index = _routes.IndexOf(route);
        if (index < 0) return;
        var routesBefore = _routes.ToArray();
        var draftsBefore = SavedHybridPaymentDrafts.ToArray();
        var cacheBefore = _multiRouteDraftCache.ToArray();
        var wasEditing = ReferenceEquals(_editingRoute, route);
        var key = HybridRouteKey(route);
        _routes.RemoveAt(index);
        SavedHybridPaymentDrafts.RemoveAll(draft => draft.RouteKey == key);
        _multiRouteDraftCache.Remove(key);

        if (!SavePersistedData(showErrorDialog: false))
        {
            _routes.Insert(index, route);
            RestoreHybridDraftIdentitySnapshot(draftsBefore, cacheBefore, routesBefore);
            RefreshViewsAfterPersistenceRollback();
            ShowRouteValidation($"Hat silinemedi: {route.RouteNumber}; disk kaydı başarısız, hat ve taslakları geri alındı.");
            return;
        }

        if (wasEditing) ClearRouteButton_Click(this, new RoutedEventArgs());
        UpdateRouteSummary();
        UpdateRouteListView();
        UpdateProgressContractorAndRoutes(ProgressTenderListBox.SelectedItem as ContractDraftData);
        UpdateOutputRoutesForContract(OutputTenderListBox.SelectedItem as ContractDraftData);
        UpdateHomeSummaryView();
        RouteStatusTextBlock.Text = $"Hat silindi: {route.RouteNumber}.";
    }

    private void EditRouteButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveEditing)
        {
            ShowRouteValidation("Arşiv düzeltmesi sürüyor. Önce değişiklikleri kaydedin veya düzenlemeyi iptal edin.");
            return;
        }
        if (sender is not System.Windows.Controls.Button { Tag: RouteDraft route })
            return;
        ClearArchiveCorrectionState();
        ResetRouteDateValidation();
        ClearRouteValidationVisuals();

        _editingRoute = route;
        SetRouteEditorMode(true);
        var contract = FindContract(route.TenderNumber, route.ContractNumber, route.ProgramType);
        var contractor = FindContractor(route.ContractorTaxOrIdentityNumber, route.ProgramType);

        _suppressSelectionEvents = true;
        try
        {
            RouteTenderListBox.SelectedItem = contract;
            RouteContractorListBox.ItemsSource = contractor is null ? Array.Empty<ContractorDraft>() : new[] { contractor };
            RouteContractorListBox.SelectedItem = contractor;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        RouteNumberTextBox.Text = route.RouteNumber;
        RouteNameTextBox.Text = route.Name;
        RouteDailyPriceTextBox.Text = FormatCalculationInput(route.DailyPrice);
        RouteContractAmountTextBox.Text = FormatCalculationInput(route.ContractAmount ?? 0m);
        RouteContractDatePicker.SelectedDate = route.ContractDate?.ToDateTime(TimeOnly.MinValue);
        RouteGuideDailyPriceTextBox.Text = FormatCalculationInput(route.GuideDailyPrice);
        ApplyRouteProgramFields(route.ProgramType);
        RouteSelectedTenderTextBlock.Text = contract is null ? "İhale bulunamadı" : $"İKN: {contract.TenderNumber}";
        RouteSelectedContractorTextBlock.Text = contractor is null ? "Yüklenici bulunamadı" : $"Yüklenici: {contractor.Name}";
        RouteStatusTextBlock.Text = $"Düzenleme modu: İKN {route.TenderNumber} / Hat {route.RouteNumber}. DÜZENLE ile kaydedin.";
    }

    private void CancelRouteEditButton_Click(object sender, RoutedEventArgs e)
    {
        var archiveCorrection = IsArchiveCorrectionFor(ArchiveCorrectionSection.Route);
        ClearArchiveCorrectionState();
        _editingRoute = null;
        ClearRouteEditorFields();
        SetRouteEditorMode(false);
        if (archiveCorrection)
        {
            NavigateToArchives();
            return;
        }
        RouteStatusTextBlock.Text = "Hat düzenlemesi iptal edildi; kayıt değiştirilmedi.";
    }

    private void ClearRouteButton_Click(object sender, RoutedEventArgs e)
    {
        ClearArchiveCorrectionState();
        _editingRoute = null;
        RouteNumberTextBox.IsReadOnly = false;
        ClearRouteEditorFields();
        // Keep the chosen tender/contractor filters so the saved route remains
        // visible in the saved-record list; only the entry fields are reset.
        UpdateRouteListView();
        UpdateRouteSummary();
        SetRouteEditorMode(false);
    }

    private void UpdateRouteSummary()
    {
        RefreshRoutePanelList();
    }

    private void EditContractButton_Click(object sender, RoutedEventArgs e)
    {
        ClearArchiveCorrectionState();
        if (sender is not Button { Tag: ContractDraftData contract })
            return;

        _editingContractTender = contract.TenderNumber;
        _editingContractNumber = contract.ContractNumber;
        _editingContractProgramType = contract.ProgramType;
        ApplyContractToForm(contract);
        UpdateContractSelectors();
        SetContractEditorMode(true);
        NavigationButton_Click(NavContract, new RoutedEventArgs(Button.ClickEvent, NavContract));
        ContractStatusTextBlock.Text = $"Düzenleme modu: İKN {contract.TenderNumber}. Değiştirip DÜZENLE'e basın.";
    }

    private void CancelContractEditButton_Click(object sender, RoutedEventArgs e)
    {
        var archiveCorrection = IsArchiveCorrectionFor(ArchiveCorrectionSection.Contract);
        var original = !archiveCorrection && !string.IsNullOrWhiteSpace(_editingContractTender)
            ? FindContract(_editingContractTender, _editingContractNumber, _editingContractProgramType)
            : null;

        ClearArchiveCorrectionState();
        _editingContractTender = null;
        _editingContractNumber = null;
        _editingContractProgramType = null;

        if (archiveCorrection)
        {
            ClearContractFormOnly();
            NavigateToArchives();
            return;
        }

        if (original is not null) ApplyContractToForm(original);
        else ClearContractFormOnly();
        ContractStatusTextBlock.Text = "Sözleşme düzenlemesi iptal edildi; kayıt değiştirilmedi.";
    }

    private void DeleteContractButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ContractDraftData contract })
            return;

        var routeCount = _routes.Count(x => IsRouteForContract(x, contract));
        var historyCount = _progressHistory.Count(x => IsHistoryForContract(x, contract));

        // Do not orphan legacy data that couldn't be assigned safely during migration.
        // If a tender has multiple contract parts and a legacy row lacks ContractNumber,
        // the UI cannot determine which part owns it, so deletion must be blocked.
        var ambiguousRouteCount = _routes.Count(x =>
            string.IsNullOrWhiteSpace(x.ContractNumber) &&
            string.Equals(x.TenderNumber.Trim(), contract.TenderNumber.Trim(), StringComparison.OrdinalIgnoreCase));
        var ambiguousHistoryCount = _progressHistory.Count(x =>
            string.IsNullOrWhiteSpace(x.ContractNumber) &&
            string.Equals(x.TenderNumber?.Trim(), contract.TenderNumber.Trim(), StringComparison.OrdinalIgnoreCase));

        if (routeCount > 0 || historyCount > 0 || ambiguousRouteCount > 0 || ambiguousHistoryCount > 0)
        {
            var ambiguous = ambiguousRouteCount + ambiguousHistoryCount;
            var ambiguityNote = ambiguous > 0
                ? $" Ayrıca {ambiguous} eski hat/hakediş kaydının sözleşme bağı belirsiz; önce bu kayıtların bağı netleştirilmelidir."
                : string.Empty;
            MessageBox.Show(
                $"İKN '{contract.TenderNumber}' / '{contract.WorkName}' kaydı {routeCount} bağlı hat ve {historyCount} bağlı hakedişle ilişkili. Önce ilişkili kayıtları düzeltin; kayıt silinmedi.{ambiguityNote}",
                "OEHS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"İKN '{contract.TenderNumber}' / '{contract.WorkName}' kaydı silinsin mi?",
            "Sözleşme Kaydını Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        var index = _contracts.IndexOf(contract);
        if (index < 0) return;
        _contracts.RemoveAt(index);
        if (!SavePersistedData())
        {
            _contracts.Insert(index, contract);
            RefreshViewsAfterPersistenceRollback();
            ContractStatusTextBlock.Text =
                "Sözleşme silinemedi: disk kaydı başarısız oldu; sözleşme tekrar listeye alındı.";
            return;
        }
        UpdateContractSelectors();
        UpdateHomeSummaryView();
        UpdateContractSummaryView();
    }

    private void SaveContractButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveEditing)
        {
            if (IsArchiveCorrectionFor(ArchiveCorrectionSection.Contract))
            {
                SaveArchivedContractCorrection();
            }
            else
            {
                ShowAlreadyRegistered("Arşiv düzeltmesi devam ediyor. Önce ilgili düzenleme panelindeki DÜZENLEME İPTAL işlemini tamamlayın.");
            }
            return;
        }

        ClearContractValidationVisuals();

        // A new contract must be explicitly assigned to a transport program.
        // Do not let the legacy fallback in GetSelectedTransportationProgramType()
        // silently classify an unselected program as Special Education.
        if (_editingContractTender is null &&
            !TryGetSelectedTransportationProgramType(out _))
        {
            ShowContractValidation("Önce TAŞIMA PROGRAMI alanından Özel Eğitim Taşıma veya Temel Eğitim/Ortaöğretim Taşıma programını seçin.");
            return;
        }

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(TenderNumberTextBox.Text))
        {
            errors.Add("İhale / Dosya No");
            MarkInvalid(TenderNumberTextBox);
        }
        if (!TryReadLegacyContractAmount(ContractAmountTextBox.Text, out var contractAmount))
        {
            errors.Add("Eski sözleşme bedeli");
            MarkInvalid(ContractAmountTextBox);
        }
        var contractWorkDays = 0;
        if (!string.IsNullOrWhiteSpace(ContractWorkDaysTextBox.Text) &&
            (!int.TryParse(ContractWorkDaysTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out contractWorkDays) || contractWorkDays < 0))
        {
            errors.Add("Sözleşme İş Günü");
            MarkInvalid(ContractWorkDaysTextBox);
        }
        if (!TryReadLegacyContractAmount(ContractTransportDailyPriceTextBox.Text, out var transportDailyPrice))
        {
            errors.Add("Eski taşıma günlük fiyatı");
            MarkInvalid(ContractTransportDailyPriceTextBox);
        }
        if (!HasValidTenderDateText(StartDatePicker)) { errors.Add("Başlangıç tarihi metni"); MarkInvalid(StartDatePicker); }
        if (!HasValidTenderDateText(EndDatePicker)) { errors.Add("Bitiş tarihi metni"); MarkInvalid(EndDatePicker); }
        if (!HasValidTenderDateText(TenderDatePicker)) { errors.Add("İhale tarihi metni"); MarkInvalid(TenderDatePicker); }
        var startDate = StartDatePicker.SelectedDate;
        var endDate = EndDatePicker.SelectedDate;
        if (!startDate.HasValue)
        {
            errors.Add("Başlangıç Tarihi");
            MarkInvalid(StartDatePicker);
        }
        if (!endDate.HasValue)
        {
            errors.Add("Bitiş Tarihi");
            MarkInvalid(EndDatePicker);
        }
        if (startDate.HasValue && endDate.HasValue && endDate.Value.Date < startDate.Value.Date)
        {
            errors.Add("Başlangıç / Bitiş Tarihi");
            MarkInvalid(StartDatePicker);
            MarkInvalid(EndDatePicker);
        }

        if (errors.Count > 0)
        {
            ShowContractValidation("Lütfen şu alanları kontrol edin: " + string.Join(", ", errors) + ".");
            return;
        }

        var sourceTenderForEdit = _editingContractTender ?? TenderNumberTextBox.Text.Trim();
        var sourceContractNumberForEdit = _editingContractNumber ?? string.Empty;
        var oldContract = _editingContractTender is null
            ? null
            : FindContract(sourceTenderForEdit, sourceContractNumberForEdit, _editingContractProgramType ?? GetSelectedTransportationProgramType());

        if (_editingContractTender is not null && oldContract is null)
        {
            ShowAlreadyRegistered("Düzenlenen sözleşme artık bulunamıyor. Değişiklik uygulanmadı.");
            return;
        }

        var internalContractNumber = oldContract?.ContractNumber ?? "INTERNAL-" + Guid.NewGuid().ToString("N");
        ContractNumberTextBox.Text = internalContractNumber;

        var proposedContract = new ContractDraftData(
            TenderNumberTextBox.Text.Trim(),
            internalContractNumber,
            // Derived titles belong to presentation/output, not stored contracts.
            oldContract?.WorkName ?? string.Empty,
            // A new contract/part must not silently inherit a sibling contract's contractor.
            // Contractor identity is associated with the route and is entered/selected there.
            oldContract?.ContractorName ?? string.Empty,
            contractAmount,
            DateOnly.FromDateTime(startDate!.Value),
            DateOnly.FromDateTime(endDate!.Value),
            oldContract?.VatExempt ?? false,
            TenderDatePicker.SelectedDate.HasValue ? DateOnly.FromDateTime(TenderDatePicker.SelectedDate.Value) : null,
            ContractDatePicker.SelectedDate.HasValue ? DateOnly.FromDateTime(ContractDatePicker.SelectedDate.Value) : null,
            oldContract?.ContractorTaxOrIdentityNumber ?? string.Empty,
            oldContract?.BankAccountNumber ?? string.Empty,
            contractWorkDays,
            transportDailyPrice,
            oldContract?.ProgramType ??
                (TryGetSelectedTransportationProgramType(out var selectedProgramType)
                    ? selectedProgramType
                    : throw new InvalidOperationException("Yeni sözleşme için taşıma programı seçilmelidir.")));

        var duplicateTarget = _contracts.FirstOrDefault(x =>
            string.Equals(
                OehsIdentityService.NormalizeIdentity(x.TenderNumber),
                OehsIdentityService.NormalizeIdentity(proposedContract.TenderNumber),
                StringComparison.Ordinal) &&
            !ReferenceEquals(x, oldContract));

        if (duplicateTarget is not null && !ReferenceEquals(duplicateTarget, oldContract))
        {
            ShowAlreadyRegistered(
                $"İKN '{proposedContract.TenderNumber}' zaten kayıtlı. Aynı ihale için yeni kayıt açmayın; farklı yüklenicileri Hatlar bölümünde bu İKN'ye bağlayın.");
            return;
        }

        var contractsBeforeSave = _contracts.ToArray();
        var routesBeforeSave = _routes.ToArray();
        var hybridDraftsBeforeSave = SavedHybridPaymentDrafts.ToArray();
        var hybridKeysBeforeSave = _multiRouteDraftCache.ToArray();
        var historyBeforeSave = _progressHistory.ToArray();
        var editingContractTenderBeforeSave = _editingContractTender;
        var editingContractNumberBeforeSave = _editingContractNumber;

        if (oldContract is null)
        {
            _contracts.Add(proposedContract);
        }
        else
        {
            var affectedPaymentPeriods = _progressHistory
                .Where(x => IsHistoryForContract(x, oldContract))
                .Where(x => x.PeriodDate.HasValue)
                .Select(x => x.PeriodDate!.Value)
                .ToArray();

            if (affectedPaymentPeriods.Any(period =>
                    period < proposedContract.StartDate || period > proposedContract.EndDate))
            {
                ShowContractValidation(
                    "Sözleşme tarih aralığı bu sözleşmeye bağlı mevcut hakediş dönemlerini kapsamalıdır. " +
                    "Dönem aralığı dışına çıkan en az bir hakediş var.");
                MarkInvalid(StartDatePicker);
                MarkInvalid(EndDatePicker);
                return;
            }

            var routeData = _routes.Select(x => new RouteData(
                x.RouteNumber,
                x.Name,
                x.DailyPrice,
                x.GuideDailyPrice,
                x.TransportWorkDays,
                x.TenderNumber,
                x.ContractorTaxOrIdentityNumber,
                x.ContractNumber, x.ProgramType, x.ContractAmount, x.ContractDate)).ToArray();

            var proposedHistories = ProgressPaymentPropagationService.RecalculateForContractChange(
                _progressHistory.ToArray(), routeData, oldContract, proposedContract, _calculationConstants);
            var proposedTargetPayment = _historyCorrectionTargetId is Guid targetId
                ? proposedHistories.FirstOrDefault(x => IsOperationPayment(x, targetId))
                : null;
            if (!ConfirmLiveHistoryCorrection("Sözleşme bilgileri", proposedTargetPayment))
                return;

            var propagatedRoutes = TenderRoutePropagationService.UpdateContractIdentity(
                routeData, oldContract, proposedContract);
            var affectedRouteIndices = _routes
                .Select((route, routeIndex) => (route, routeIndex))
                .Where(x => IsRouteForContract(x.route, oldContract))
                .ToArray();

            var oldIndex = _contracts.IndexOf(oldContract);
            if (oldIndex < 0)
            {
                ShowAlreadyRegistered("Düzenlenen sözleşme listede bulunamadı. Değişiklik uygulanmadı.");
                return;
            }

            _contracts[oldIndex] = proposedContract;
            foreach (var entry in affectedRouteIndices)
            {
                var updatedRoute = propagatedRoutes[entry.routeIndex];
                var nextRoute = entry.route with
                {
                    TenderNumber = updatedRoute.TenderNumber,
                    ContractNumber = updatedRoute.ContractNumber,
                    DailyPrice = entry.route.DailyPrice,
                    GuideDailyPrice = updatedRoute.GuideDailyPrice,
                    ProgramType = updatedRoute.ProgramType
                };
                RekeyHybridDrafts(entry.route,nextRoute);
                _routes[entry.routeIndex] = nextRoute;
            }

            _progressHistory.Clear();
            foreach (var item in proposedHistories)
                _progressHistory.Add(item);
        }

        if (!SavePersistedData())
        {
            RestoreHybridDraftIdentitySnapshot(hybridDraftsBeforeSave,hybridKeysBeforeSave,routesBeforeSave);
            RestoreLiveDataCollections(
                contractsBeforeSave, _contractors.ToArray(), routesBeforeSave, historyBeforeSave);
            _editingContractTender = editingContractTenderBeforeSave;
            _editingContractNumber = editingContractNumberBeforeSave;
            SetContractEditorMode(!string.IsNullOrWhiteSpace(editingContractTenderBeforeSave));
            RefreshViewsAfterPersistenceRollback();
            ContractStatusTextBlock.Text =
                "Sözleşme kaydı diske yazılamadı. Bellekteki değişiklik geri alındı; form bilgileri korundu.";
            return;
        }

        _editingContractTender = null;
        _editingContractNumber = null;
        _editingContractProgramType = null;
        SetContractEditorMode(false);
        UpdateContractSelectors();
        UpdateRouteListView();
        UpdateRouteSummary();
        UpdateContractSummaryView();
        UpdateHomeSummaryView();
        UpdateAcceptanceView();
        if (_historyCorrectionSection == HistoryCorrectionSection.Contract)
            RefreshViewsAfterHistoryCorrection();
        ClearContractFormOnly();
        ClearContractValidationVisuals();

        ContractStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 104, 216));
        ContractStatusTextBlock.Text = $"İhale kaydedildi: {proposedContract.TenderNumber}.";
    }

    private void LoadPersistedData()
    {
        try
        {
            var data = _dataStore.Load();
            _savedHybridPaymentDrafts = (data.HybridPaymentDrafts ?? Array.Empty<HybridPaymentDraftData>()).ToList();
            var loadedConstants = data.CalculationConstants ?? ProgressPaymentCalculationConstants.Defaults;
            if (!loadedConstants.TryValidate(out var constantsError))
            {
                throw new OehsApplicationDataStoreException(
                    "Kaydedilmiş hesaplama sabitleri geçersiz. Güvenlik nedeniyle kayıt ve hesaplama durduruldu: " + constantsError,
                    new InvalidDataException(constantsError));
            }

            _calculationConstants = loadedConstants;
            _calculationConstantsHistory =
                (data.CalculationConstantsHistory ?? Array.Empty<ProgressPaymentCalculationConstantsChange>()).ToList();

            _contracts.Clear();
            var persistedContracts = data.Contracts ?? Array.Empty<ContractDraftData>();
            if (persistedContracts.Count > 0)
            {
                foreach (var contract in persistedContracts) _contracts.Add(contract);
            }
            else if (data.Contract is not null)
            {
                _contracts.Add(data.Contract);
            }

            _contractors.Clear();
            foreach (var contractor in data.Contractors ?? Array.Empty<Contractor>())
            {
                // The program-scoped store performs legacy migration once, then
                // returns each contractor in exactly one program partition.
                _contractors.Add(new ContractorDraft(
                    contractor.Name,
                    OehsIdentityService.NormalizeIdentity(contractor.TaxOrIdentityNumber),
                    contractor.Address, contractor.Phone, contractor.Email,
                    contractor.BankAccountNumber, contractor.ContactPerson, contractor.ProgramType,
                    contractor.VatExempt ?? HasLegacyContractVatExemption(contractor.TaxOrIdentityNumber, contractor.ProgramType)));
            }

            _routes.Clear();
            foreach (var route in data.Routes ?? Array.Empty<RouteData>())
            {
                var tender = string.IsNullOrWhiteSpace(route.TenderNumber)
                    ? _contracts.FirstOrDefault()?.TenderNumber ?? string.Empty
                    : route.TenderNumber;
                var tenderContracts = _contracts.Where(x =>
                    x.ProgramType == route.ProgramType &&
                    string.Equals(x.TenderNumber, tender, StringComparison.OrdinalIgnoreCase)).ToArray();

                var contractorTax = OehsIdentityService.NormalizeIdentity(route.ContractorTaxOrIdentityNumber);
                var routeContract = !string.IsNullOrWhiteSpace(route.ContractNumber)
                    ? FindContract(tender, route.ContractNumber, route.ProgramType)
                    : tenderContracts.Where(x => x.ProgramType == route.ProgramType).ToArray() is var programContracts && programContracts.Length == 1
                        ? programContracts[0]
                        : null;

                if (routeContract is null && string.IsNullOrWhiteSpace(route.ContractNumber) && !string.IsNullOrWhiteSpace(contractorTax))
                {
                    var contractorMatches = tenderContracts.Where(x =>
                        string.Equals(
                            OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
                            contractorTax,
                            StringComparison.Ordinal)).ToArray();
                    if (contractorMatches.Length == 1)
                        routeContract = contractorMatches[0];
                }

                if (string.IsNullOrWhiteSpace(contractorTax))
                    contractorTax = OehsIdentityService.NormalizeIdentity(routeContract?.ContractorTaxOrIdentityNumber ?? string.Empty);

                var contractorName = FindContractor(contractorTax, routeContract?.ProgramType ?? route.ProgramType)?.Name ?? routeContract?.ContractorName ?? string.Empty;
                var contractNumber = !string.IsNullOrWhiteSpace(route.ContractNumber)
                    ? route.ContractNumber
                    : routeContract?.ContractNumber ?? string.Empty;

                _routes.Add(new RouteDraft(
                    route.RouteNumber, route.Name, route.DailyPrice,
                    route.GuideDailyPrice, route.TransportWorkDays,
                    tender, contractorTax, contractorName, contractNumber,
                    routeContract?.ProgramType ?? route.ProgramType,
                    route.ContractAmount ?? routeContract?.ContractAmount,
                    route.ContractDate ?? routeContract?.ContractDate));
            }

            _progressHistory.Clear();
            foreach (var history in data.ProgressPayments ?? Array.Empty<ProgressPaymentHistoryData>())
            {
                var contract = FindContract(history.TenderNumber, history.ContractNumber, history.Input.ProgramType);
                if (contract is null && string.IsNullOrWhiteSpace(history.ContractNumber))
                {
                    var tenderContracts = _contracts.Where(x =>
                        string.Equals(x.TenderNumber, history.TenderNumber, StringComparison.OrdinalIgnoreCase)).ToArray();
                    if (tenderContracts.Length == 1)
                        contract = tenderContracts[0];
                    else if (!string.IsNullOrWhiteSpace(history.ContractorTaxOrIdentityNumber))
                    {
                        var identity = OehsIdentityService.NormalizeIdentity(history.ContractorTaxOrIdentityNumber);
                        var contractorMatches = tenderContracts.Where(x =>
                            string.Equals(OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber), identity, StringComparison.Ordinal))
                            .ToArray();
                        if (contractorMatches.Length == 1)
                            contract = contractorMatches[0];
                    }
                }

                var contractorTax = OehsIdentityService.NormalizeIdentity(
                    history.ContractorTaxOrIdentityNumber ?? contract?.ContractorTaxOrIdentityNumber ?? string.Empty);
                var contractor = FindContractor(contractorTax, history.Input.ProgramType);
                var contractNumber = !string.IsNullOrWhiteSpace(history.ContractNumber)
                    ? history.ContractNumber
                    : contract?.ContractNumber ?? string.Empty;

                _progressHistory.Add(history with
                {
                    ContractNumber = contractNumber,
                    ContractorTaxOrIdentityNumber = string.IsNullOrWhiteSpace(contractorTax)
                        ? history.ContractorTaxOrIdentityNumber
                        : contractorTax,
                    ContractorName = contractor?.Name ?? history.ContractorName ?? contract?.ContractorName,
                    BankAccountNumber = contractor?.BankAccountNumber ?? history.BankAccountNumber ?? contract?.BankAccountNumber
                });
            }

            ClearContractFormOnly();
            _dataLoadFailed = false;
            SetHeaderSaveStatus("Veri yüklendi", isError: false);
            UpdateContractSelectors();
            UpdateRouteListView();
            UpdateContractSummaryView();
            UpdateHomeSummaryView();
        }
        catch (OehsApplicationDataStoreException ex)
        {
            _dataLoadFailed = true;
            SetHeaderSaveStatus("Veri yükleme hatası", isError: true);
            _contracts.Clear();
            _contractors.Clear();
            _routes.Clear();
            _progressHistory.Clear();

            MessageBox.Show(
                $"{ex.Message}\n\nOEHS güvenlik nedeniyle mevcut bozuk/veri dosyasının üzerine yazmayacak.",
                "OEHS — Veri Güvenliği", MessageBoxButton.OK, MessageBoxImage.Error);
            UpdateContractSelectors();
            UpdateRouteListView();
            UpdateContractSummaryView();
            UpdateHomeSummaryView();
        }
    }

    private void SetHeaderSaveStatus(string status, bool isError)
    {
        if (HeaderSaveStatusTextBlock is null || HeaderSaveStatusDot is null)
            return;

        var foreground = isError
            ? System.Windows.Media.Color.FromRgb(255, 190, 190)
            : System.Windows.Media.Color.FromRgb(221, 246, 231);
        var indicator = isError
            ? System.Windows.Media.Color.FromRgb(230, 65, 78)
            : System.Windows.Media.Color.FromRgb(67, 199, 122);

        HeaderSaveStatusTextBlock.Text = status;
        HeaderSaveStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(foreground);
        HeaderSaveStatusDot.Fill = new System.Windows.Media.SolidColorBrush(indicator);
    }

    private bool SavePersistedData(bool showErrorDialog = true)
    {
        if (_dataLoadFailed)
        {
            SetHeaderSaveStatus("Kaydetme durduruldu", isError: true);
            if (showErrorDialog) MessageBox.Show(
                "OEHS veri dosyası güvenli olarak yüklenemedi. Veri kaybını önlemek için kaydetme durduruldu.",
                "OEHS — Kaydetme Durduruldu", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        var savedDraftsBeforeSave = SavedHybridPaymentDrafts.ToArray();
        ReconcileSavedHybridSources();
        var activeContract = GetSelectedContract() ?? _contracts.FirstOrDefault();
        var data = new OehsApplicationData(
            activeContract,
            _routes.Select(x => new RouteData(
                x.RouteNumber, x.Name, x.DailyPrice, x.GuideDailyPrice,
                x.TransportWorkDays, x.TenderNumber, x.ContractorTaxOrIdentityNumber, x.ContractNumber, x.ProgramType, x.ContractAmount, x.ContractDate)).ToArray(),
            _contractors.Select(x => new Contractor(
                x.Name, x.TaxOrIdentityNumber, x.Address, x.Phone, x.Email,
                x.BankAccountNumber, x.ContactPerson, x.ProgramType, x.VatExempt)).ToArray(),
            _progressHistory.ToArray(),
            _contracts.ToArray(),
            _calculationConstants,
            _calculationConstantsHistory.ToArray(),
            SavedHybridPaymentDrafts.ToArray());

        try
        {
            _dataStore.Save(data);
            SetHeaderSaveStatus("Kaydedildi", isError: false);
            return true;
        }
        catch (Exception ex) when (ex is OehsApplicationDataStoreException or IOException or UnauthorizedAccessException)
        {
            _savedHybridPaymentDrafts = savedDraftsBeforeSave.ToList();
            SetHeaderSaveStatus("Kaydetme hatası", isError: true);
            if (showErrorDialog) MessageBox.Show(
                $"{ex.Message}\n\nMevcut veri dosyası korunmuştur. OEHS değişikliklerinizi kaydetmedi.",
                "OEHS — Veri Kaydetme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void RestoreLiveDataCollections(
        IReadOnlyList<ContractDraftData> contracts,
        IReadOnlyList<ContractorDraft> contractors,
        IReadOnlyList<RouteDraft> routes,
        IReadOnlyList<ProgressPaymentHistoryData> history)
    {
        _contracts.Clear();
        foreach (var item in contracts) _contracts.Add(item);

        _contractors.Clear();
        foreach (var item in contractors) _contractors.Add(item);

        _routes.Clear();
        foreach (var item in routes) _routes.Add(item);

        _progressHistory.Clear();
        foreach (var item in history) _progressHistory.Add(item);
    }

    private void RefreshViewsAfterPersistenceRollback()
    {
        UpdateContractSelectors();
        UpdateContractorSummary();
        UpdateRouteSummary();
        UpdateRouteListView();
        UpdateContractSummaryView();
        UpdateHistoryView();
        UpdateSummaryView();
        UpdateReportView();
        UpdateHomeSummaryView();
        UpdateAcceptanceView();
    }

    private void ClearContractButton_Click(object sender, RoutedEventArgs e)
    {
        ClearArchiveCorrectionState();
        _editingContractTender = null;
        _editingContractNumber = null;
        _editingContractProgramType = null;
        ClearContractFormOnly();
        UpdateContractSummaryView();
        ContractStatusTextBlock.Text = "Yeni ihale/sözleşme kaydı için alanlar temizlendi.";
    }

    private static string OehsDesktopRoot => OehsOutputPathService.DesktopRoot;

    private static string SafeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return string.IsNullOrWhiteSpace(value) ? "OEHS" : value;
    }

    private static readonly JsonSerializerOptions JsonWriteOptions = new() { WriteIndented = true };

    private static string BuildArchiveKey(ProgressPaymentHistoryData item)
    {
        var tender = SafeFileName(string.IsNullOrWhiteSpace(item.TenderNumber) ? "Ihale" : item.TenderNumber);
        var contractor = OehsFileNameService.BuildContractorFilePart(
            item.ContractorName,
            item.ContractorTaxOrIdentityNumber);
        var route = SafeFileName(string.IsNullOrWhiteSpace(item.RouteNumber) ? "Hat" : item.RouteNumber);
        var progress = item.ProgressPaymentNumber?.ToString(CultureInfo.InvariantCulture) ?? "Yeni";
        var period = item.PeriodDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Donemsiz";
        return $"{tender}_Yuklenici-{contractor}_{route}_Hakedis-{progress}_{period}_{item.Id:N}";
    }

    private static string SaveRouteDocumentsArchiveMetadata(
        ProgressPaymentHistoryData latest,
        IReadOnlyList<string> pdfPaths)
    {
        ArgumentNullException.ThrowIfNull(latest);
        ArgumentNullException.ThrowIfNull(pdfPaths);
        if (pdfPaths.Count is not (1 or 4))
            throw new ArgumentException("Arşiv meta verisi için bir birleşik PDF veya dört eski PDF gerekir.", nameof(pdfPaths));

        var documentFolder = Path.GetDirectoryName(Path.GetFullPath(pdfPaths[0]))!;
        Directory.CreateDirectory(documentFolder);

        var fileNames = pdfPaths.Select(Path.GetFileName).ToArray();
        var metadataSuffix = latest.ProgressPaymentNumber.HasValue && latest.PeriodDate.HasValue
            ? $"_{latest.ProgressPaymentNumber.Value:00}_{latest.PeriodDate.Value:yyyy-MM-dd}"
            : "_Yeni";

        var metadataPath = Path.Combine(documentFolder, $"OEHS_PDF_Bilgi{metadataSuffix}.json");
        var metadata = JsonSerializer.Serialize(new
        {
            latest.Id,
            latest.ProgressPaymentNumber,
            latest.PeriodDate,
            latest.RouteNumber,
            latest.RouteName,
            latest.WorkName,
            latest.ContractorName,
            latest.BankAccountNumber,
            TransportDailyPrice = latest.Input.DailyPrice,
            PdfFileName = fileNames[0],
            PdfFileNames = fileNames,
            PdfTargetPaths = pdfPaths.ToArray(),
            PdfGenerator = "OEHS FixedDocument / PdfVectorWriter (DoğruTemin port)",
            Documents = OfficialDocumentNames,
            SavedAt = DateTime.Now
        }, JsonWriteOptions);

        var tempPath = metadataPath + ".tmp";
        File.WriteAllText(tempPath, metadata);
        File.Move(tempPath, metadataPath, overwrite: true);
        return documentFolder;
    }

    private void ShowPdfSaveInfoDialog(string targetFolder, string fileName, string targetPath)
    {
        var dialog = new Window
        {
            Title = "OEHS — PDF Kayıt Bilgisi",
            Owner = this,
            Width = 680,
            Height = fileName.Contains(Environment.NewLine, StringComparison.Ordinal) ? 470 : 390,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White
        };

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = "PDF BAŞARIYLA KAYDEDİLDİ",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = System.Windows.Media.Brushes.DarkSlateGray
        });
        panel.Children.Add(new TextBlock
        {
            Text = fileName.Contains(Environment.NewLine, StringComparison.Ordinal)
                ? "Dört resmî belge tek PDF dosyasında oluşturuldu. Yazıcı kullanılmadı; dosyalar vektör tabanlı belge motoruyla OEHS kayıt klasörüne kaydedildi."
                : "Dört belge belirtilen sırayla tek PDF dosyasında oluşturuldu. Bu işlemde yazıcı kullanılmadı; dosya doğrudan OEHS kayıt klasörüne kaydedildi.",
            FontSize = 13.5,
            Foreground = System.Windows.Media.Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 16)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "KAYIT KLASÖRÜ",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = System.Windows.Media.Brushes.SteelBlue
        });
        panel.Children.Add(new TextBlock
        {
            Text = targetFolder,
            FontSize = 13.5,
            Foreground = System.Windows.Media.Brushes.DarkSlateGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 12)
        });
        panel.Children.Add(new TextBlock
        {
            Text = fileName.Contains(Environment.NewLine, StringComparison.Ordinal) ? "OLUŞTURULAN PDF DOSYALARI" : "ÖNERİLEN DOSYA ADI",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = System.Windows.Media.Brushes.SteelBlue
        });
        panel.Children.Add(new TextBlock
        {
            Text = fileName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = System.Windows.Media.Brushes.DarkSlateGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 18)
        });

        panel.Children.Add(new TextBlock
        {
            Text = fileName.Contains(Environment.NewLine, StringComparison.Ordinal)
                ? $"İlk PDF dosyası: {targetPath}"
                : $"Oluşturulan dosya: {targetPath}",
            FontSize = 11.5,
            Foreground = System.Windows.Media.Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var folderButton = new Button
        {
            Content = "KLASÖRÜ AÇ",
            Width = 150,
            Height = 42,
            Style = (Style)FindResource("SecondaryButton"),
            Margin = new Thickness(0, 0, 10, 0)
        };
        folderButton.Click += (_, _) =>
        {
            Directory.CreateDirectory(targetFolder);
            Process.Start(new ProcessStartInfo { FileName = targetFolder, UseShellExecute = true });
            dialog.DialogResult = true;
            dialog.Close();
        };

        var okButton = new Button
        {
            Content = "TAMAM",
            Width = 120,
            Height = 42,
            Style = (Style)FindResource("ModernButton")
        };
        okButton.Click += (_, _) =>
        {
            dialog.DialogResult = true;
            dialog.Close();
        };

        buttons.Children.Add(folderButton);
        buttons.Children.Add(okButton);
        panel.Children.Add(buttons);

        dialog.Content = panel;
        dialog.ShowDialog();
    }

    private static int ExtractArchiveRevision(string path)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            Path.GetFileNameWithoutExtension(path),
            @"_v(?<revision>\d+)$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success &&
               int.TryParse(
                   match.Groups["revision"].Value,
                   System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var revision)
            ? revision
            : 1;
    }

    private void RecalculateHistoryItem(int index, ProgressPaymentInput input, ProgressPaymentHistoryData metadataSource)
    {
        var result = ProgressPaymentCalculator.Calculate(input, _calculationConstants);
        _progressHistory[index] = metadataSource with
        {
            Input = input,
            Calculation = result
        };
    }

    private void RecalculateHistoriesForTenderChange(ContractDraftData oldContract, ContractDraftData updatedContract)
    {
        var propagated = ProgressPaymentPropagationService.RecalculateForContractChange(
            _progressHistory.ToArray(),
            _routes.Select(x => new RouteData(
                x.RouteNumber,
                x.Name,
                x.DailyPrice,
                x.GuideDailyPrice,
                x.TransportWorkDays,
                x.TenderNumber,
                x.ContractorTaxOrIdentityNumber,
                x.ContractNumber, x.ProgramType, x.ContractAmount, x.ContractDate)).ToArray(),
            oldContract,
            updatedContract,
            _calculationConstants);

        _progressHistory.Clear();
        foreach (var item in propagated)
            _progressHistory.Add(item);
    }

    private void UpdateHistoryForRouteEdit(RouteDraft oldRoute, RouteDraft updatedRoute)
    {
        var propagated = BuildRoutePropagation(oldRoute, updatedRoute);
        _progressHistory.Clear();

        foreach (var item in propagated)
            _progressHistory.Add(item);
    }

    private void SaveMultiRouteProgressPayment(
        ContractDraftData contract,
        IReadOnlyList<RouteDraft> routes,
        IReadOnlyList<MultiRoutePaymentDraftRow> entries,
        DateOnly? periodDate)
    {
        if (routes.Count == 0)
            return;

        if (!periodDate.HasValue)
        {
            var message = "Çoklu hat hesabı için sözleşme ayı seçilmelidir.";
            ProgressValidationBanner.Visibility = Visibility.Visible;
            ProgressValidationMessageTextBlock.Text = message;
            CalculationStatusTextBlock.Text = message;
            ProgressCalculationSummaryTextBlock.Text = "KONTROL GEREKLİ";
            return;
        }

        if (!TryCommitHybridGrid()) return;

        if (entries.Count != routes.Count)
        {
            ShowMultiRouteValidation("Seçili hatların her biri için ayrı hesap satırı oluşturulamadı. Hat seçimlerini yenileyip tekrar deneyin.");
            return;
        }

        var selectedPeriod = ProgressPaymentMonthService.FindForPeriod(
            ProgressPaymentMonthService.BuildOptions(contract.StartDate, contract.EndDate),
            periodDate.Value);
        if (selectedPeriod is null)
        {
            ShowMultiRouteValidation("Seçilen ay sözleşme tarih aralığında değil; hakediş kaydı oluşturulmadı.");
            return;
        }

        var contractors = _contractors.Select(x => new Contractor(
            x.Name, x.TaxOrIdentityNumber, x.Address, x.Phone, x.Email,
            x.BankAccountNumber, x.ContactPerson, x.ProgramType, x.VatExempt)).ToArray();
        var data = new OehsApplicationData(
            contract,
            _routes.Select(x => new RouteData(
                x.RouteNumber, x.Name, x.DailyPrice, x.GuideDailyPrice,
                x.TransportWorkDays, x.TenderNumber, x.ContractorTaxOrIdentityNumber,
                x.ContractNumber, x.ProgramType, x.ContractAmount, x.ContractDate)).ToArray(),
            contractors,
            _progressHistory.ToArray(),
            _contracts.ToArray());

        var createdAt = DateTime.Now;
        var batchId = Guid.NewGuid();
        var sortedRouteNumbers = routes
            .Select(x => x.RouteNumber.Trim())
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        // A tender can contain routes belonging to different firms. The displayed
        // batch name must describe the selected routes' actual contractor, not the
        // tender's optional/default contractor label.
        var contractorNameForBatch = routes
            .Select(route => FindContractor(route.ContractorTaxOrIdentityNumber, route.ProgramType)?.Name)
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            ?? routes.Select(route => route.ContractorName)
                .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            ?? contract.ContractorName
            ?? "Yüklenici";
        var batchDisplayName = MonthlyProgressPaymentIdentityService.BuildDisplayName(
            contract.ProgramType,
            contract.TenderNumber,
            contract.ContractNumber,
            contractorNameForBatch,
            sortedRouteNumbers,
            periodDate.Value);
        var batchRouteNumbers = string.Join("+", sortedRouteNumbers);
        var entriesByKey = entries.ToDictionary(x => x.RouteKey, StringComparer.Ordinal);
        var batch = new List<ProgressPaymentHistoryData>();
        var errors = new List<string>();
        var hasGuide = HasGuideStaff(contract.ProgramType);

        foreach (var route in routes)
        {
            if (route.ProgramType != contract.ProgramType || !IsRouteForContract(route, contract))
            {
                errors.Add($"Hat {route.RouteNumber}: seçilen sözleşmenin programına bağlı değil.");
                continue;
            }

            var routeKey = OehsIdentityService.BuildRouteKey(
                route.TenderNumber,
                route.RouteNumber,
                route.ContractorTaxOrIdentityNumber,
                route.ContractNumber,
                route.ProgramType);
            if (!entriesByKey.TryGetValue(routeKey, out var row))
            {
                errors.Add($"Hat {route.RouteNumber}: hat bazlı giriş satırı bulunamadı.");
                continue;
            }

            if (row.WorkDays < 0 || row.DailyPrice < 0m || row.DiscountAmount < 0m ||
                row.TaxDebt < 0m || row.SocialSecurityDebt < 0m ||
                row.MinimumWageSupport < 0m || row.InspectionPenalty < 0m ||
                (hasGuide && (row.GuideWorkDays < 0 || row.GuideDailyPrice < 0m || row.GuideDiscountAmount < 0m)))
            {
                errors.Add($"Hat {route.RouteNumber}: gün, günlük fiyat, iskonto ve kesinti alanları negatif olamaz. Fiyat farkı işaretli olabilir.");
                continue;
            }

            var calendarCapacityError = ProgressPaymentMonthService.GetCalendarCapacityError(
                selectedPeriod, row.WorkDays, hasGuide ? row.GuideWorkDays : 0m);
            if (calendarCapacityError is not null)
            {
                errors.Add($"Hat {route.RouteNumber}: {calendarCapacityError}");
                continue;
            }

            var identity = OehsIdentityService.NormalizeIdentity(route.ContractorTaxOrIdentityNumber);
            var contractor = FindContractor(identity, route.ProgramType);
            var input = new ProgressPaymentInput(
                row.WorkDays,
                row.DailyPrice,
                row.DiscountAmount,
                row.PriceDifference,
                route.ContractAmount ?? contract.ContractAmount,
                contractor?.VatExempt ?? contract.VatExempt,
                row.TaxDebt,
                row.SocialSecurityDebt,
                row.MinimumWageSupport,
                row.InspectionPenalty,
                hasGuide ? row.GuideWorkDays : 0,
                hasGuide ? row.GuideDailyPrice : 0m,
                hasGuide ? row.GuideDiscountAmount : 0m,
                hasGuide ? row.GuidePriceDifference : 0m,
                contract.ProgramType, row.TransportPeriods, hasGuide ? row.GuidePeriods : null, row.HasPeriodChanges);

            input = AttachEditingPeriods(input);
            var calculation = ProgressPaymentCalculator.Calculate(input, _calculationConstants);
            var routeProgressNumber = MonthlyProgressPaymentIdentityService.SuggestNextRouteNumber(
                _progressHistory,
                contract.TenderNumber,
                contract.ContractNumber,
                identity,
                route.RouteNumber,
                contract.ProgramType,
                periodDate.Value);
            var item = new ProgressPaymentHistoryData(
                Guid.NewGuid(), createdAt, input, calculation,
                routeProgressNumber, periodDate,
                route.RouteNumber, route.Name,
                contract.WorkName, contractor?.Name ?? route.ContractorName,
                contract.TenderNumber, contract.ContractNumber,
                contract.TenderDate, route.ContractDate ?? contract.ContractDate,
                contract.StartDate, contract.EndDate,
                contractor?.BankAccountNumber ?? string.Empty,
                identity,
                BatchId: batchId,
                BatchDisplayName: batchDisplayName,
                BatchRouteNumbers: batchRouteNumbers);

            var validationErrors = ProgressPaymentValidationService.ValidateCreation(
                data, contract,
                new RouteData(
                    route.RouteNumber, route.Name, route.DailyPrice,
                    route.GuideDailyPrice, route.TransportWorkDays,
                    route.TenderNumber, identity, route.ContractNumber, route.ProgramType, route.ContractAmount, route.ContractDate),
                item, editingExisting: false);
            errors.AddRange(validationErrors.Select(error => $"Hat {route.RouteNumber}: {error}"));
            batch.Add(item);
        }

        if (errors.Count > 0 || batch.Count != routes.Count)
        {
            ShowMultiRouteValidation(string.Join(Environment.NewLine, errors.Distinct()));
            return;
        }

        foreach (var item in batch)
            _progressHistory.Add(item);
        if (!SavePersistedData())
        {
            foreach (var item in batch)
                _progressHistory.Remove(item);
            UpdateHistoryView();
            UpdateSummaryView();
            UpdateReportView();
            UpdateHomeSummaryView();
            UpdateAcceptanceView();
            CalculationStatusTextBlock.Text = "Çoklu hat kaydı diske yazılamadı; tüm yeni hat kayıtları geri alındı.";
            ProgressCalculationSummaryTextBlock.Text = "KAYIT BAŞARISIZ";
            return;
        }

        var total = batch.Select(x => x.Calculation).ToArray();
        decimal Sum(Func<ProgressPaymentCalculation, decimal> selector) =>
            total.Sum(selector);
        var deductions = new DeductionBreakdown(
            Sum(x => x.Deductions.Withholding),
            Sum(x => x.Deductions.StampTax),
            Sum(x => x.Deductions.TaxDebt),
            Sum(x => x.Deductions.InspectionPenalty),
            Sum(x => x.Deductions.SocialSecurityDebt),
            Sum(x => x.Deductions.MinimumWageSupport),
            Sum(x => x.Deductions.AdditionalGuarantee));

        ServiceAmountResultTextBlock.Text = FormatMoney(Sum(x => x.ServiceAmount));
        GuideServiceAmountResultTextBlock.Text = FormatMoney(Sum(x => x.GuideServiceAmount));
        TransportPriceDifferenceResultTextBlock.Text = FormatMoney(Sum(x => x.PriceDifferenceExcludingVat));
        GuidePriceDifferenceResultTextBlock.Text = FormatMoney(Sum(x => x.GuidePriceDifferenceExcludingVat));
        CombinedPriceDifferenceResultTextBlock.Text = FormatMoney(Sum(x => x.CombinedPriceDifferenceExcludingVat));
        TotalAmountResultTextBlock.Text = FormatMoney(Sum(x => x.TotalAmount));
        VatResultTextBlock.Text = FormatMoney(Sum(x => x.Vat));
        DeductionsResultTextBlock.Text = FormatMoney(Sum(x => x.TotalDeductions));
        GrossResultTextBlock.Text = FormatMoney(Sum(x => x.GrossAmount));
        NetResultTextBlock.Text = FormatMoney(Sum(x => x.NetPayable));
        NetControlResultTextBlock.Text = FormatMoney(Sum(x => x.NetPayable));
        WithholdingResultTextBlock.Text = FormatMoney(deductions.Withholding);
        StampTaxResultTextBlock.Text = FormatMoney(deductions.StampTax);
        TaxDebtResultTextBlock.Text = FormatMoney(deductions.TaxDebt);
        SocialSecurityDebtResultTextBlock.Text = FormatMoney(deductions.SocialSecurityDebt);
        MinimumWageSupportResultTextBlock.Text = FormatMoney(deductions.MinimumWageSupport);
        InspectionPenaltyResultTextBlock.Text = FormatMoney(deductions.InspectionPenalty);
        AdditionalGuaranteeResultTextBlock.Text = FormatMoney(deductions.AdditionalGuarantee);
        ManualAdditionalGuaranteeTextBox.Text = FormatMoney(deductions.AdditionalGuarantee);
        ProgressWorkDaysSummaryTextBlock.Text = batch.Sum(x => x.Input.WorkDays).ToString(CultureInfo.CurrentCulture);
        ProgressWorkDaysCardTextBlock.Text = ProgressWorkDaysSummaryTextBlock.Text + " gün";
        ProgressNetSummaryTextBlock.Text = FormatMoney(Sum(x => x.NetPayable));
        ProgressCalculationSummaryTextBlock.Text = "TAMAM";

        ResetNewProgressDraftAfterCalculation(batch[^1]);
        UpdateHistoryView();
        UpdateSummaryView();
        UpdateReportView();
        UpdateHomeSummaryView();
        UpdateAcceptanceView();
        UpdateArchiveView();

        var routeLabels = string.Join(", ", routes.Select(x => x.RouteNumber));
        var aggregateMessage = $"{batchDisplayName} hesaplandı. {routes.Count} hat ({routeLabels}) için her satır ayrı hesaplandı ve kaydedildi. Birleşik net tutar: {FormatMoney(Sum(x => x.NetPayable))}.";
        ProgressValidationBanner.Visibility = Visibility.Collapsed;
        ProgressValidationMessageTextBlock.Text = aggregateMessage;
        CalculationStatusTextBlock.Foreground =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 104, 216));
        CalculationStatusTextBlock.FontWeight = FontWeights.SemiBold;
        CalculationStatusTextBlock.Text = aggregateMessage;
        ArchiveCurrentProgressButton.Visibility = Visibility.Visible;
        CorrectCurrentProgressButton.Visibility = Visibility.Visible;
    }

    private void ShowMultiRouteValidation(string message)
    {
        ProgressValidationBanner.Visibility = Visibility.Visible;
        ProgressValidationMessageTextBlock.Text = message;
        ProgressCalculationSummaryTextBlock.Text = "KONTROL GEREKLİ";
        CalculationStatusTextBlock.Foreground =
            new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(198, 40, 40));
        CalculationStatusTextBlock.FontWeight = FontWeights.Bold;
        CalculationStatusTextBlock.Text = message;
        ArchiveCurrentProgressButton.Visibility = Visibility.Collapsed;
        CorrectCurrentProgressButton.Visibility = Visibility.Collapsed;
    }

    private void CalculateProgressButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveEditing && !IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment))
        {
            ShowAlreadyRegistered("Arşiv düzeltmesi devam ediyor. Önce mevcut düzenlemeyi kaydedin veya DÜZENLEME İPTAL işlemini tamamlayın.");
            return;
        }

        var wasArchiveCorrection = IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment);

        ClearProgressValidationVisuals();
        ProgressValidationBanner.Visibility = Visibility.Collapsed;
        ProgressValidationMessageTextBlock.Text = string.Empty;

        var selectedContract = IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment)
            ? _editingArchiveSnapshot!.Contract
            : ProgressTenderListBox.SelectedItem as ContractDraftData;
        if (selectedContract is null)
        {
            ShowAlreadyRegistered("Önce İhale Kayıt Numarası (İKN) seçin.");
            return;
        }
        if (!IsArchiveEditing && !_editingHistoryId.HasValue)
        {
            if (ProgressRouteComboBox.SelectedItems.Count == 0)
            {
                ShowMultiRouteValidation("Önce hakediş oluşturulacak en az bir hat seçin.");
                return;
            }
            TryCalculateHybridSelection(selectedContract);
            return;
        }
        var hasGuide = HasGuideStaff(selectedContract.ProgramType);

        var invalidFields = new List<(Control Control, string Name)>();

        if (!TryParseWorkDays(WorkDaysTextBox.Text, out var workDays) || workDays < 0)
            invalidFields.Add((WorkDaysTextBox, "Döneme Esas Taşıma Hakediş Gün Sayısı (Döneme Esas THGS)"));

        decimal guideWorkDays = 0m;
        var guideDailyPrice = 0m;
        var guideDiscount = 0m;
        var guidePriceDifference = 0m;
        var minimumWageSupport = 0m;
        if (hasGuide)
        {
            if (!TryParseWorkDays(GuideWorkDaysTextBox.Text, out guideWorkDays) || guideWorkDays < 0)
                invalidFields.Add((GuideWorkDaysTextBox, "Rehber İş Günü (bu ay)"));
            if (!TryParseRequiredMoney(GuideDailyPriceTextBox.Text, out guideDailyPrice) || guideDailyPrice < 0)
                invalidFields.Add((GuideDailyPriceTextBox, "Rehber Personel Günlük Fiyat"));
            if (!TryParseDecimalOrZero(GuideDiscountTextBox.Text, out guideDiscount) || guideDiscount < 0)
                invalidFields.Add((GuideDiscountTextBox, "Rehber İskonto"));
            if (!TryParseDecimalOrZero(GuidePriceDifferenceTextBox.Text, out guidePriceDifference))
                invalidFields.Add((GuidePriceDifferenceTextBox, "Rehber Fiyat Farkı"));
        }

        // Asgari ücret desteği her iki kaynak Excel'de de kesinti alanıdır.
        // Her iki programda tutar doğrudan kullanıcı girişinden alınır.
        if (!TryParseDecimalOrZero(MinimumWageSupportTextBox.Text, out minimumWageSupport) ||
            minimumWageSupport < 0)
        {
            invalidFields.Add((MinimumWageSupportTextBox, "Asgari Ücret Desteği"));
        }

        if (!TryParseRequiredMoney(DailyPriceTextBox.Text, out var dailyPrice) || dailyPrice < 0)
            invalidFields.Add((DailyPriceTextBox, "Taşıma Günlük Fiyatı"));
        if (!TryParseDecimalOrZero(DiscountTextBox.Text, out var discount) || discount < 0)
            invalidFields.Add((DiscountTextBox, "Taşıma İskonto"));
        if (!TryParseDecimalOrZero(PriceDifferenceTextBox.Text, out var priceDifference))
            invalidFields.Add((PriceDifferenceTextBox, "Taşıma Fiyat Farkı"));
        if (!TryParseDecimalOrZero(TaxDebtTextBox.Text, out var taxDebt) || taxDebt < 0)
            invalidFields.Add((TaxDebtTextBox, "Vergi Borcu"));
        if (!TryParseDecimalOrZero(SocialSecurityDebtTextBox.Text, out var socialSecurityDebt) || socialSecurityDebt < 0)
            invalidFields.Add((SocialSecurityDebtTextBox, "SGK Borcu"));
        if (!TryParseDecimalOrZero(InspectionPenaltyTextBox.Text, out var inspectionPenalty) || inspectionPenalty < 0)
            invalidFields.Add((InspectionPenaltyTextBox, "Denetim Cezası"));

        if (invalidFields.Count > 0)
        {
            foreach (var (control, _) in invalidFields)
                MarkProgressInvalid(control);

            var message = "Lütfen kırmızı işaretli alanları tamamlayın/düzeltin: " +
                          string.Join(", ", invalidFields.Select(x => x.Name)) +
                          ". Hesap tutarlarını en fazla dört ondalık basamakla girin (ör. 12.536,4801).";
            ProgressValidationMessageTextBlock.Text = message;
            ProgressValidationBanner.Visibility = Visibility.Visible;
            ProgressCalculationSummaryTextBlock.Text = "KONTROL GEREKLİ";
            CalculationStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(198, 40, 40));
            CalculationStatusTextBlock.FontWeight = FontWeights.Bold;
            CalculationStatusTextBlock.Text = message;
            return;
        }

        try
        {
            RouteDraft? selectedRoute;
            if (IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment))
            {
                var archivedRoute = _editingArchiveSnapshot!.Route;
                selectedRoute = new RouteDraft(
                    archivedRoute.RouteNumber,
                    archivedRoute.Name,
                    archivedRoute.DailyPrice,
                    archivedRoute.GuideDailyPrice,
                    archivedRoute.TransportWorkDays,
                    archivedRoute.TenderNumber,
                    archivedRoute.ContractorTaxOrIdentityNumber,
                    _editingArchiveSnapshot.Contractor.Name,
                    archivedRoute.ContractNumber,
                    _editingArchiveSnapshot.Contract.ProgramType,
                    archivedRoute.ContractAmount ?? _editingArchiveSnapshot.ProgressPayment.Input.ContractAmount,
                    archivedRoute.ContractDate ?? _editingArchiveSnapshot.ProgressPayment.ContractDate);
            }
            else
            {
                // New calculations require an explicit route choice; never silently use the first route.
                selectedRoute = ProgressRouteComboBox.SelectedItem as RouteDraft;
            }

            if (selectedRoute is null)
            {
                ShowAlreadyRegistered($"'{selectedContract.TenderNumber}' ihalesine bağlı bir hat seçin.");
                return;
            }

            var input = new ProgressPaymentInput(
                workDays,
                dailyPrice,
                discount,
                priceDifference,
                selectedRoute.ContractAmount ?? selectedContract.ContractAmount,
                _editingArchiveSnapshot?.ProgressPayment.Input.IsVatExempt
                    ?? FindContractor(selectedRoute.ContractorTaxOrIdentityNumber, selectedRoute.ProgramType)?.VatExempt
                    ?? selectedContract.VatExempt,
                taxDebt,
                socialSecurityDebt,
                minimumWageSupport,
                inspectionPenalty,
                guideWorkDays,
                guideDailyPrice,
                guideDiscount,
                guidePriceDifference,
                selectedContract.ProgramType);
            input = AttachEditingPeriods(input);

            int? progressNumber = null;
            if (int.TryParse(
                    ProgressNumberTextBox.Text,
                    NumberStyles.Integer,
                    CultureInfo.CurrentCulture,
                    out var parsedProgressNumber) &&
                parsedProgressNumber > 0)
            {
                progressNumber = parsedProgressNumber;
            }

            var periodDate = (ProgressPeriodMonthComboBox.SelectedItem as ProgressPaymentMonthOption)?.PeriodDate;

            var selectedRoutes = ProgressRouteComboBox.SelectedItems.OfType<RouteDraft>().ToArray();
            if (!IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment) &&
                !_editingHistoryId.HasValue &&
                selectedRoutes.Length > 1)
            {
                MultiRoutePaymentGrid.CommitEdit(DataGridEditingUnit.Cell, true);
                MultiRoutePaymentGrid.CommitEdit(DataGridEditingUnit.Row, true);
                // Commit grid edits, refresh the cache, then cross an explicit
                // selected-route boundary before any calculation or PDF generation.
                foreach (var visibleRow in _multiRoutePaymentRows)
                    _multiRouteDraftCache[visibleRow.RouteKey] = visibleRow;

                try
                {
                    var selectedRouteKeys = selectedRoutes.Select(route =>
                        OehsIdentityService.BuildRouteKey(
                            route.TenderNumber,
                            route.RouteNumber,
                            route.ContractorTaxOrIdentityNumber,
                            route.ContractNumber,
                            route.ProgramType));
                    var selectedEntries = SelectedRouteDrafts.SelectExact(
                        selectedRouteKeys, _multiRouteDraftCache);
                    SaveMultiRouteProgressPayment(
                        selectedContract, selectedRoutes, selectedEntries, periodDate);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    ShowMultiRouteValidation(
                        $"Seçili hat taslakları doğrulanamadı; hakediş üretilmedi. {ex.Message}");
                }
                return;
            }

            if (IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment))
            {
                if (!progressNumber.HasValue || progressNumber <= 0)
                    invalidFields.Add((ProgressNumberTextBox, "Hakediş No"));

                if (!periodDate.HasValue)
                {
                    invalidFields.Add((ProgressPeriodMonthComboBox, "Dönem Tarihi"));
                }
                else if (periodDate.Value < _editingArchiveSnapshot!.Contract.StartDate ||
                         periodDate.Value > _editingArchiveSnapshot.Contract.EndDate)
                {
                    invalidFields.Add((ProgressPeriodMonthComboBox, "Dönem Tarihi"));
                }
            }

            if (invalidFields.Count > 0)
            {
                foreach (var (control, _) in invalidFields)
                    MarkProgressInvalid(control);

                var correctionValidationMessage =
                    "Lütfen kırmızı işaretli alanları tamamlayın/düzeltin: " +
                    string.Join(", ", invalidFields.Select(x => x.Name)) + ".";

                ProgressValidationMessageTextBlock.Text = correctionValidationMessage;
                ProgressValidationBanner.Visibility = Visibility.Visible;
                ProgressCalculationSummaryTextBlock.Text = "KONTROL GEREKLİ";
                CalculationStatusTextBlock.Foreground =
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(198, 40, 40));
                CalculationStatusTextBlock.FontWeight = FontWeights.Bold;
                CalculationStatusTextBlock.Text = correctionValidationMessage;
                return;
            }

            var normalizedContractorIdentity = OehsIdentityService.NormalizeIdentity(
                selectedRoute.ContractorTaxOrIdentityNumber);

            string contractorName;
            string contractorBankAccount;

            if (IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment))
            {
                var archivedContractor = _editingArchiveSnapshot!.Contractor;
                contractorName = archivedContractor.Name;
                contractorBankAccount = archivedContractor.BankAccountNumber;
            }
            else
            {
                var liveContractor = FindContractor(normalizedContractorIdentity, selectedContract.ProgramType);
                contractorName = liveContractor?.Name ?? selectedRoute.ContractorName;
                contractorBankAccount = liveContractor?.BankAccountNumber ?? string.Empty;
            }

            var existingPaymentForMetadata = _editingHistoryId.HasValue
                ? _progressHistory.FirstOrDefault(x => IsOperationPayment(x, _editingHistoryId.Value))
                : null;
            var metadataSource = IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment)
                ? _editingArchiveSnapshot!.ProgressPayment
                : existingPaymentForMetadata;
            var selectedRouteNumbersForMetadata = ProgressRouteComboBox.SelectedItems
                .OfType<RouteDraft>()
                .Select(x => x.RouteNumber.Trim())
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (selectedRouteNumbersForMetadata.Length == 0)
                selectedRouteNumbersForMetadata = new[] { selectedRoute.RouteNumber.Trim() };
            var batchIdForMetadata = metadataSource?.BatchId ?? metadataSource?.Id ?? Guid.NewGuid();
            var batchRouteNumbersForMetadata = metadataSource?.BatchRouteNumbers
                ?? string.Join("+", selectedRouteNumbersForMetadata);
            var batchDisplayNameForMetadata = metadataSource?.BatchDisplayName
                ?? MonthlyProgressPaymentIdentityService.BuildDisplayName(
                    selectedContract.ProgramType,
                    selectedContract.TenderNumber,
                    selectedContract.ContractNumber,
                    contractorName,
                    selectedRouteNumbersForMetadata,
                    periodDate ?? DateOnly.FromDateTime(DateTime.Today));

            // Ön kontrol, hesaplama motorundan önce çalışır.
            var historyItem = new ProgressPaymentHistoryData(
                _editingHistoryId ?? Guid.NewGuid(),
                _editingHistoryId.HasValue
                    ? _progressHistory.First(x => IsOperationPayment(x, _editingHistoryId.Value)).CreatedAt
                    : DateTime.Now,
                input,
                new ProgressPaymentCalculation(
                    0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m,
                    new DeductionBreakdown(0m, 0m, 0m, 0m, 0m, 0m, 0m)),
                progressNumber,
                periodDate,
                selectedRoute.RouteNumber,
                selectedRoute.Name,
                selectedContract.WorkName,
                contractorName,
                selectedRoute.TenderNumber,
                selectedContract.ContractNumber,
                selectedContract.TenderDate,
                selectedRoute.ContractDate ?? selectedContract.ContractDate,
                selectedContract.StartDate,
                selectedContract.EndDate,
                contractorBankAccount,
                normalizedContractorIdentity,
                BatchId: batchIdForMetadata,
                BatchDisplayName: batchDisplayNameForMetadata,
                BatchRouteNumbers: batchRouteNumbersForMetadata);

            var dataForValidation = new OehsApplicationData(
                selectedContract,
                _routes.Select(x => new RouteData(
                    x.RouteNumber,
                    x.Name,
                    x.DailyPrice,
                    x.GuideDailyPrice,
                    x.TransportWorkDays,
                    x.TenderNumber,
                    x.ContractorTaxOrIdentityNumber,
                    x.ContractNumber, x.ProgramType)).ToArray(),
                _contractors.Select(x => new Contractor(
                    x.Name,
                    OehsIdentityService.NormalizeIdentity(x.TaxOrIdentityNumber),
                    x.Address,
                    x.Phone,
                    x.Email,
                    x.BankAccountNumber,
                    x.ContactPerson,
                    x.ProgramType,
                    x.VatExempt)).ToArray(),
                _progressHistory.ToArray(),
                _contracts.ToArray());

            var validationErrors = IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment)
                ? Array.Empty<string>()
                : ProgressPaymentValidationService.ValidateCreation(
                    dataForValidation,
                    selectedContract,
                    new RouteData(
                        selectedRoute.RouteNumber,
                        selectedRoute.Name,
                        selectedRoute.DailyPrice,
                        selectedRoute.GuideDailyPrice,
                        selectedRoute.TransportWorkDays,
                        selectedRoute.TenderNumber,
                        normalizedContractorIdentity,
                        selectedContract.ContractNumber, selectedRoute.ProgramType, selectedRoute.ContractAmount, selectedRoute.ContractDate),
                    historyItem,
                    _editingHistoryId.HasValue);

            if (validationErrors.Count > 0)
            {
                ProgressCalculationSummaryTextBlock.Text = "KONTROL GEREKLİ";
                ProgressValidationMessageTextBlock.Text = string.Join(Environment.NewLine, validationErrors);
                ProgressValidationBanner.Visibility = Visibility.Visible;
                CalculationStatusTextBlock.Foreground =
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(198, 40, 40));
                CalculationStatusTextBlock.FontWeight = FontWeights.Bold;
                CalculationStatusTextBlock.Text = string.Join(" ", validationErrors);
                ArchiveCurrentProgressButton.Visibility = Visibility.Collapsed;
                CorrectCurrentProgressButton.Visibility = Visibility.Collapsed;
                return;
            }

            ProgressValidationBanner.Visibility = Visibility.Collapsed;
            ProgressValidationMessageTextBlock.Text =
                $"Ön kontrol tamam: İhale/Dosya {selectedContract.TenderNumber} • Hat {selectedRoute.RouteNumber} • " +
                $"Yüklenici {contractorName} • Hakediş {progressNumber} • Dönem {periodDate:dd.MM.yyyy}.";
            CalculationStatusTextBlock.Foreground =
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 104, 216));
            CalculationStatusTextBlock.FontWeight = FontWeights.SemiBold;
            CalculationStatusTextBlock.Text = ProgressValidationMessageTextBlock.Text;

            var result = ProgressPaymentCalculator.Calculate(input, _calculationConstants);

            historyItem = historyItem with { Calculation = result };

            ServiceAmountResultTextBlock.Text = FormatMoney(result.ServiceAmount);
            GuideServiceAmountResultTextBlock.Text = FormatMoney(result.GuideServiceAmount);
            TransportPriceDifferenceResultTextBlock.Text = FormatMoney(result.PriceDifferenceExcludingVat);
            GuidePriceDifferenceResultTextBlock.Text = FormatMoney(result.GuidePriceDifferenceExcludingVat);
            CombinedPriceDifferenceResultTextBlock.Text = FormatMoney(result.CombinedPriceDifferenceExcludingVat);
            TotalAmountResultTextBlock.Text = FormatMoney(result.TotalAmount);
            VatResultTextBlock.Text = FormatMoney(result.Vat);
            DeductionsResultTextBlock.Text = FormatMoney(result.TotalDeductions);
            GrossResultTextBlock.Text = FormatMoney(result.GrossAmount);
            NetResultTextBlock.Text = FormatMoney(result.NetPayable);
            NetControlResultTextBlock.Text = FormatMoney(result.NetPayable);
            WithholdingResultTextBlock.Text = FormatMoney(result.Withholding);
            StampTaxResultTextBlock.Text = FormatMoney(result.StampTax);
            TaxDebtResultTextBlock.Text = FormatMoney(result.Deductions.TaxDebt);
            SocialSecurityDebtResultTextBlock.Text = FormatMoney(result.Deductions.SocialSecurityDebt);
            MinimumWageSupportResultTextBlock.Text = FormatMoney(result.Deductions.MinimumWageSupport);
            InspectionPenaltyResultTextBlock.Text = FormatMoney(result.Deductions.InspectionPenalty);
            AdditionalGuaranteeResultTextBlock.Text = FormatMoney(result.AdditionalGuarantee);
            ManualAdditionalGuaranteeTextBox.Text = FormatMoney(result.AdditionalGuarantee);
            ProgressWorkDaysSummaryTextBlock.Text = workDays.ToString(CultureInfo.CurrentCulture);
            ProgressWorkDaysCardTextBlock.Text = ProgressWorkDaysSummaryTextBlock.Text + " gün";
        ProgressWorkDaysCardTextBlock.Text = workDays.ToString(CultureInfo.CurrentCulture) + " gün";
            ProgressNetSummaryTextBlock.Text = FormatMoney(result.NetPayable);
            ProgressCalculationSummaryTextBlock.Text = "TAMAM";

            if (IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment))
            {
                var archive = _editingArchiveSnapshot!;
                var updatedPayment = archive.ProgressPayment with
                {
                    Input = historyItem.Input,
                    Calculation = historyItem.Calculation,
                    ProgressPaymentNumber = historyItem.ProgressPaymentNumber,
                    PeriodDate = historyItem.PeriodDate,
                    RouteNumber = historyItem.RouteNumber,
                    RouteName = historyItem.RouteName
                };

                SaveArchiveCorrectionSnapshot(
                    archive with
                    {
                        ProgressPayment = updatedPayment,
                        PdfFileName = BuildArchivePdfFileName(updatedPayment)
                    },
                    "Hakediş girdileri");
                return;
            }

            if (_editingHistoryId.HasValue &&
                !ConfirmLiveHistoryCorrection("Hakediş girdileri", historyItem))
            {
                SetProgressEditorMode(true);
                return;
            }

            var editingHistoryId = _editingHistoryId;
            var editIndex = -1;
            ProgressPaymentHistoryData? previousHistoryItem = null;

            if (editingHistoryId.HasValue)
            {
                var existingHistoryForEdit = _progressHistory.FirstOrDefault(x => IsOperationPayment(x, editingHistoryId.Value));
                editIndex = existingHistoryForEdit is null ? -1 : _progressHistory.IndexOf(existingHistoryForEdit);
                if (editIndex < 0)
                {
                    ShowAlreadyRegistered("Düzenlenecek hakediş kaydı artık bulunamıyor; değişiklik kaydedilmedi.");
                    return;
                }

                previousHistoryItem = _progressHistory[editIndex];
                _progressHistory[editIndex] = historyItem;
                _editingHistoryId = null;
            }
            else
            {
                _progressHistory.Add(historyItem);
            }

            if (!SavePersistedData())
            {
                if (editingHistoryId.HasValue && editIndex >= 0 && previousHistoryItem is not null)
                {
                    _progressHistory[editIndex] = previousHistoryItem;
                    _editingHistoryId = editingHistoryId;
                    SetProgressEditorMode(true);
                }
                else
                {
                    var failedNewItem = _progressHistory.FirstOrDefault(x => x.Id == historyItem.Id);
                    if (failedNewItem is not null)
                        _progressHistory.Remove(failedNewItem);
                    SetProgressEditorMode(false);
                }

                UpdateHistoryView();
                UpdateSummaryView();
                UpdateReportView();
                UpdateHomeSummaryView();
                UpdateAcceptanceView();
                CalculationStatusTextBlock.Text =
                    "Veri diske kaydedilemedi. Bellekteki değişiklik geri alındı; başarıyla kaydedilmiş sayılmıyor.";
                ProgressCalculationSummaryTextBlock.Text = "KAYIT BAŞARISIZ";
                return;
            }

            SetProgressEditorMode(false);
            _lastCalculatedHistoryId = historyItem.Id;
            UpdateHistoryView();
            UpdateSummaryView();
            UpdateReportView();
            UpdateHomeSummaryView();
            UpdateAcceptanceView();
            if (_historyCorrectionSection == HistoryCorrectionSection.ProgressPayment)
                RefreshViewsAfterHistoryCorrection();

            if (!editingHistoryId.HasValue)
            {
                ResetNewProgressDraftAfterCalculation(historyItem);
            }

            CalculationStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 104, 216));
            CalculationStatusTextBlock.FontWeight = FontWeights.SemiBold;
            CalculationStatusTextBlock.Text = wasArchiveCorrection
                ? "Düzeltme hesaplandı. Önceki arşiv revision'ı korunuyor; yeni revision için ARŞİVLE düğmesine basın."
                : "Hesaplama tamamlandı. Arşivlemek veya düzeltmek için aşağıdaki işlemleri kullanın.";
            ArchiveCurrentProgressButton.Visibility = Visibility.Visible;
            CorrectCurrentProgressButton.Visibility = Visibility.Visible;
            UpdateArchiveView();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            ProgressCalculationSummaryTextBlock.Text = "KONTROL";
            CalculationStatusTextBlock.Text = ex.Message;
        }
    }

    private void TemporaryCalculationTraceButton_Click(object sender, RoutedEventArgs e)
    {
        NavigationButton_Click(new System.Windows.Controls.Button { Tag = "Trace" }, new RoutedEventArgs());
    }

    private void ConfigureLiveCalculationTrace()
    {
        foreach (var textBox in new TextBox[]
        {
            WorkDaysTextBox, DailyPriceTextBox, GuideWorkDaysTextBox, GuideDailyPriceTextBox,
            DiscountTextBox, GuideDiscountTextBox, PriceDifferenceTextBox, GuidePriceDifferenceTextBox,
            TaxDebtTextBox, SocialSecurityDebtTextBox, MinimumWageSupportTextBox, InspectionPenaltyTextBox,
            ContractAmountTextBox, ProgressNumberTextBox
        })
            textBox.TextChanged += (_, _) => RefreshCalculationTraceLive();

        ProgressTenderListBox.SelectionChanged += (_, _) => RefreshCalculationTraceLive();
        ProgressRouteComboBox.SelectionChanged += (_, _) => RefreshCalculationTraceLive();
        ProgressPeriodMonthComboBox.SelectionChanged += (_, _) => RefreshCalculationTraceLive();
        ContractorVatExemptCheckBox.Checked += (_, _) => RefreshCalculationTraceLive();
        ContractorVatExemptCheckBox.Unchecked += (_, _) => RefreshCalculationTraceLive();
        MinimumWageSupportEnabledCheckBox.Checked += (_, _) => RefreshCalculationTraceLive();
        MinimumWageSupportEnabledCheckBox.Unchecked += (_, _) => RefreshCalculationTraceLive();
        MultiRoutePaymentGrid.CellEditEnding += (_, _) =>
            Dispatcher.BeginInvoke(new Action(RefreshCalculationTraceLive));
        MultiRoutePaymentGrid.RowEditEnding += (_, _) =>
            Dispatcher.BeginInvoke(new Action(RefreshCalculationTraceLive));

        RefreshCalculationTraceLive();
    }

    private void RefreshCalculationTraceLive()
    {
        if (CalculationTraceLiveTextBox is null)
            return;

        if (!IsArchiveEditing && !_editingHistoryId.HasValue && _multiRoutePaymentRows.Count > 0 &&
            MultiRouteEntryBorder.Visibility == Visibility.Visible)
        {
            RefreshHybridCalculationTrace();
            return;
        }

        var culture = CultureInfo.CurrentCulture;
        string N(decimal value) => value.ToString("N4", culture);
        string M(decimal value) => value.ToString("N2", culture);
        string Raw(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? "(boş)" : box.Text.Trim();
        var issues = new List<string>();

        var selectedContract = IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment) &&
                               _editingArchiveSnapshot is not null
            ? _editingArchiveSnapshot.Contract
            : ProgressTenderListBox.SelectedItem as ContractDraftData;
        var selectedProgram = selectedContract?.ProgramType;
        var hasGuide = selectedContract is not null && HasGuideStaff(selectedContract.ProgramType);
        var selectedTraceRoute = ProgressRouteComboBox.SelectedItem as RouteDraft;
        var selectedRouteName = selectedTraceRoute?.Name ?? "Seçilmedi";
        var selectedRouteAmount = selectedTraceRoute?.ContractAmount ?? _editingArchiveSnapshot?.ProgressPayment.Input.ContractAmount ?? selectedContract?.ContractAmount ?? 0m;

        if (selectedContract is null)
            issues.Add("Önce ihale/program seçin.");

        if (!TryParseWorkDays(WorkDaysTextBox.Text, out var g) || g < 0m)
        {
            g = 0m;
            issues.Add("Taşıma iş günü eksik veya geçersiz.");
        }

        if (!TryParseRequiredMoney(DailyPriceTextBox.Text, out var h) || h < 0m)
        {
            h = 0m;
            issues.Add("Taşıma günlük fiyatı eksik veya geçersiz.");
        }

        if (!TryParseDecimalOrZero(PriceDifferenceTextBox.Text, out var i))
        {
            i = 0m;
            issues.Add("Taşıma fiyat farkı geçersiz.");
        }

        if (!TryParseDecimalOrZero(DiscountTextBox.Text, out var j) || j < 0m)
        {
            j = 0m;
            issues.Add("Taşıma iskontosu geçersiz.");
        }

        decimal p = 0m, q = 0m, r = 0m, s = 0m;
        if (hasGuide)
        {
            if (!TryParseWorkDays(GuideWorkDaysTextBox.Text, out p) || p < 0m)
            {
                p = 0m;
                issues.Add("Rehber iş günü eksik veya geçersiz.");
            }
            if (!TryParseRequiredMoney(GuideDailyPriceTextBox.Text, out q) || q < 0m)
            {
                q = 0m;
                issues.Add("Rehber günlük fiyatı eksik veya geçersiz.");
            }
            if (!TryParseDecimalOrZero(GuidePriceDifferenceTextBox.Text, out r))
            {
                r = 0m;
                issues.Add("Rehber fiyat farkı geçersiz.");
            }
            if (!TryParseDecimalOrZero(GuideDiscountTextBox.Text, out s) || s < 0m)
            {
                s = 0m;
                issues.Add("Rehber iskontosu geçersiz.");
            }
        }

        decimal taxDebt = 0m, socialSecurityDebt = 0m, minimumWageSupport = 0m, inspectionPenalty = 0m;
        if (!TryParseDecimalOrZero(TaxDebtTextBox.Text, out taxDebt) || taxDebt < 0m)
        {
            taxDebt = 0m;
            issues.Add("Vergi borcu geçersiz.");
        }
        if (!TryParseDecimalOrZero(SocialSecurityDebtTextBox.Text, out socialSecurityDebt) || socialSecurityDebt < 0m)
        {
            socialSecurityDebt = 0m;
            issues.Add("SGK borcu geçersiz.");
        }
        if (!TryParseDecimalOrZero(MinimumWageSupportTextBox.Text, out minimumWageSupport) || minimumWageSupport < 0m)
        {
            minimumWageSupport = 0m;
            issues.Add("Asgari ücret desteği geçersiz.");
        }
        if (!TryParseDecimalOrZero(InspectionPenaltyTextBox.Text, out inspectionPenalty) || inspectionPenalty < 0m)
        {
            inspectionPenalty = 0m;
            issues.Add("Denetim cezası geçersiz.");
        }


        var transportService = g * h;
        var transportNet = transportService + i - j;
        var guideService = p * q;
        var guideNet = guideService + r - s;
        var guaranteeRate = _calculationConstants.AdditionalGuaranteeRate;
        var specialGuarantee = i * guaranteeRate + r * guaranteeRate;
        var guaranteeDisplay = selectedProgram == TransportationProgramType.BasicOrSecondary
            ? "I × " + (guaranteeRate * 100m).ToString("0.##", culture) + "% = " + M(i * guaranteeRate) + " TL"
            : selectedProgram == TransportationProgramType.SpecialEducation
                ? "(I × " + (guaranteeRate * 100m).ToString("0.##", culture) + "%) + (R × "
                  + (guaranteeRate * 100m).ToString("0.##", culture) + "%) = "
                  + M(specialGuarantee) + " TL"
                : "Program seçilince hesaplanır.";

        var lines = new List<string>
        {
            "TEHYS — CANLI HESAP DÖKÜMÜ",
            "Son güncelleme: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", culture),
            "Program: " + (selectedProgram.HasValue ? ProgramLabel(selectedProgram) : "Seçilmedi"),
            "İhale: " + (selectedContract?.TenderNumber ?? "Seçilmedi"),
            "Hat: " + selectedRouteName,
            "Dönem: " + ((ProgressPeriodMonthComboBox.SelectedItem as ProgressPaymentMonthOption)?.PeriodDate.ToString("MMMM yyyy", culture) ?? "Seçilmedi"),
            "KDV muafiyeti: " + (IsVatExemptForCurrentFirm(selectedContract) ? "Evet" : "Hayır"),
            "Girdi durumu: " + (issues.Count == 0 ? "Ön hesaplamaya uygun" : "HESAPLAMA BEKLEMEDE — eski sonuç gösterilmiyor"),
            ""
        };

        lines.AddRange(new[]
        {
            "1) GİRDİLER (ham metin korunur)",
            "G — Taşıma iş günü: " + Raw(WorkDaysTextBox),
            "H — Taşıma günlük fiyat: " + Raw(DailyPriceTextBox) + " TL",
            "I — Taşıma fiyat farkı: " + Raw(PriceDifferenceTextBox) + " TL",
            "J — Taşıma iskontosu: " + Raw(DiscountTextBox) + " TL",
            "P — Rehber iş günü: " + (hasGuide ? Raw(GuideWorkDaysTextBox) : "Bu programda kullanılmıyor"),
            "Q — Rehber günlük fiyat: " + (hasGuide ? Raw(GuideDailyPriceTextBox) + " TL" : "Bu programda kullanılmıyor"),
            "R — Rehber fiyat farkı: " + (hasGuide ? Raw(GuidePriceDifferenceTextBox) + " TL" : "Bu programda kullanılmıyor"),
            "S — Rehber iskontosu: " + (hasGuide ? Raw(GuideDiscountTextBox) + " TL" : "Bu programda kullanılmıyor"),
            "Vergi borcu: " + Raw(TaxDebtTextBox) + " TL",
            "SGK borcu: " + Raw(SocialSecurityDebtTextBox) + " TL",
            "Asgari ücret desteği (etkin): " + M(minimumWageSupport) + " TL",
            "Denetim cezası: " + Raw(InspectionPenaltyTextBox) + " TL",
            "Sözleşme bedeli: " + M(selectedRouteAmount) + " TL",
            "Ek kesin teminat: " + guaranteeDisplay,
            "",
            "2) EXCEL KAYNAK EŞLEŞMESİ (sayfa, hücre, sütun başlığı ve birebir formül)",
            selectedProgram == TransportationProgramType.BasicOrSecondary
                ? "Kaynak: Hesaplama Bilgileri!T4 | Başlık: Ek Kesin Teminat | Excel formülü: =I4*6/100 | OEHS: I (Taşıma fiyat farkı) × %6"
                : selectedProgram == TransportationProgramType.SpecialEducation
                    ? "Kaynak: Hesaplama!AC4 | Başlık: Ek Kesin Teminat | Excel formülü: =(I4*6/100)+(R4*6/100) | OEHS: (I taşıma fiyat farkı × %6) + (R rehber fiyat farkı × %6)"
                    : "Kaynak hücre/formül eşlemesi için önce program seçin.",
            selectedProgram == TransportationProgramType.SpecialEducation
                ? "Ek kaynak: Hesaplama!K4 | Excel formülü: =(G4*H4)+I4-J4"
                : selectedProgram == TransportationProgramType.BasicOrSecondary
                    ? "Temel Eğitim taşıma neti için hücre/formül eşlemesi kaynak çalışma kitabıyla birebir doğrulanmayı bekliyor; doğrulanmamış K4 eşlemesi gösterilmez."
                    : "Taşıma net tutarı kaynak hücresi: program seçimi bekleniyor.",
            selectedProgram == TransportationProgramType.SpecialEducation
                ? "Ek kaynak: Hesaplama!T4 | Başlık: Rehber net tutarı | Excel formülü: =(P4*Q4)+R4-S4"
                : "Rehber satırı bu programda kaynak formülüne göre kullanılmaz.",
            selectedProgram == TransportationProgramType.BasicOrSecondary
                ? "Ek kaynak: Hesaplama Bilgileri!U4 | Başlık: Damga Vergisi | Excel formülü: =K4*9.48/1000 (rapor sayfasındaki farklı matrah davranışıyla karıştırmayın)"
                : selectedProgram == TransportationProgramType.SpecialEducation
                    ? "Ek kaynak: Hesaplama!AD4 | Başlık: Damga Vergisi | Excel formülü: =(K4*9.48/1000)+(T4*9.48/1000)"
                    : "Damga vergisi kaynak hücresi: program seçimi bekleniyor.",
            "Not: Kaynak başlıkları/formülleri XLSM statik incelemesinden alınmıştır; Excel masaüstü/VBA yeniden hesaplaması yerine geçmez.",
            "",
            "3) OEHS CANLI FORMÜL VE ARA DEĞERLER",
            "K — Taşıma neti = G × H + I − J = " + (issues.Count == 0
                ? N(g) + " × " + N(h) + " + " + N(i) + " − " + N(j) + " = " + M(transportNet) + " TL"
                : "Hesap bekliyor (eksik/geçersiz giriş)"),
            hasGuide
                ? "T — Rehber neti = P × Q + R − S = " + (issues.Count == 0
                    ? N(p) + " × " + N(q) + " + " + N(r) + " − " + N(s) + " = " + M(guideNet) + " TL"
                    : "Hesap bekliyor (eksik/geçersiz giriş)")
                : selectedProgram == TransportationProgramType.BasicOrSecondary
                    ? "T — Rehber neti = Bu programda rehber kalemi yok; 0,00 TL"
                    : "T — Rehber neti = Program seçimi bekleniyor",
            "AC — Ek kesin teminat = " + (issues.Count == 0 ? guaranteeDisplay : "Hesap bekliyor (eksik/geçersiz giriş)"),
            "AD — Damga vergisi matrahı: " + (selectedProgram == TransportationProgramType.BasicOrSecondary
                ? "Kaynak Excel U sütunu formülü; kaynak hücreler esas alınır"
                : "K × damga vergisi oranı + T × damga vergisi oranı"),
            ""
        });

        if (issues.Count > 0)
        {
            lines.Add("4) EKSİK / GEÇERSİZ GİRDİLER");
            lines.AddRange(issues.Select(x => "• " + x));
            lines.Add("");
            lines.Add("Vergi, tevkifat, damga vergisi, toplam kesinti ve net ödeme için sonuç üretilmedi. Eksik alanlar tamamlandığında bu iz kendiliğinden yenilenir.");
        }
        else if (selectedContract is not null)
        {
            try
            {
                var input = new ProgressPaymentInput(
                    g, h, j, i, selectedRouteAmount, IsVatExemptForCurrentFirm(selectedContract),
                    taxDebt, socialSecurityDebt, minimumWageSupport, inspectionPenalty,
                    p, q, s, r, selectedContract.ProgramType);
                input = AttachEditingPeriods(input);
                var result = ProgressPaymentCalculator.Calculate(input, _calculationConstants);
                var constants = result.AppliedConstants ?? _calculationConstants;
                var vatLabel = IsVatExemptForCurrentFirm(selectedContract) ? "KDV muaf" : "KDV dahil";
                var effectiveTransportVat = constants.TransportVatRate;
                var effectiveGuideVat = constants.GuideVatRate;
                var effectiveTransportWithholding = constants.TransportWithholdingShare;
                var effectiveGuideWithholding = constants.GuideWithholdingShare;

                lines.AddRange(new[]
                {
                    "Taşıma hizmet tutarı = G × H = " + M(result.ServiceAmount) + " TL",
                    "Taşıma fiyat farkı neti = I − J = " + M(result.PriceDifferenceExcludingVat) + " TL",
                    "K — Taşıma neti = hizmet tutarı + fiyat farkı neti = " + M(result.TransportNetAmount) + " TL",
                    "Taşıma KDV (L) = IF(KDV muaf, 0, K × " + (effectiveTransportVat * 100m).ToString("0.##", culture) + "%) = " + M(result.TransportVat) + " TL",
                    "Taşıma tevkifat (M) = IF(KDV muaf, 0, L × " + (effectiveTransportWithholding * 100m).ToString("0.##", culture) + "%) = " + M(result.TransportWithholding) + " TL",
                    "Taşıma fatura ara toplamı (N) = K + M = " + M(result.TransportNetAmount + result.TransportWithholding) + " TL",
                    "Taşıma brüt (O) = K + L = " + M(result.TransportGrossAmount) + " TL",
                    "Rehber hizmet tutarı = P × Q = " + M(result.GuideServiceAmount) + " TL",
                    "Rehber fiyat farkı neti = R − S = " + M(result.GuidePriceDifferenceExcludingVat) + " TL",
                    "T — Rehber neti = hizmet tutarı + fiyat farkı neti = " + M(result.GuideNetAmount) + " TL",
                    "Rehber KDV (U) = IF(KDV muaf, 0, T × " + (effectiveGuideVat * 100m).ToString("0.##", culture) + "%) = " + M(result.GuideVat) + " TL",
                    "Rehber tevkifat (V) = IF(KDV muaf, 0, U × " + (effectiveGuideWithholding * 100m).ToString("0.##", culture) + "%) = " + M(result.GuideWithholding) + " TL",
                    "Rehber fatura ara toplamı (W) = T + V = " + M(result.GuideNetAmount + result.GuideWithholding) + " TL",
                    "Rehber brüt (X) = T + U = " + M(result.GuideGrossAmount) + " TL",
                    "Toplam iş tutarı = K + T = " + M(result.TotalAmount) + " TL",
                    "Toplam KDV = L + U = " + M(result.Vat) + " TL",
                    "Toplam tevkifat = M + V = " + M(result.Withholding) + " TL",
                    "Fatura ara toplamı = N + W = " + M(result.InvoiceAmountAfterWithholding) + " TL",
                    "AD — Damga vergisi = " + (selectedProgram == TransportationProgramType.BasicOrSecondary
                        ? "Kaynak Excel formülü = " + M(result.StampTax) + " TL"
                        : "(K × oran) + (T × oran) = " + M(result.StampTax) + " TL"),
                    "AE — Toplam kesintiler = tevkifat + vergi borcu + denetim cezası + SGK + asgari ücret desteği + AC + AD = " + M(result.TotalDeductions) + " TL",
                    "AF — Brüt toplam = O + X = " + M(result.GrossAmount) + " TL",
                    "AG — NET ÖDEME = AF − AE = " + M(result.GrossAmount) + " − " + M(result.TotalDeductions) + " = " + M(result.NetPayable) + " TL",
                    "",
                    "4) AYRINTILI KESİNTİ DÖKÜMÜ",
                    "Taşıma tevkifatı: " + M(result.TransportWithholding) + " TL",
                    "Rehber tevkifatı: " + M(result.GuideWithholding) + " TL",
                    "Vergi borcu: " + M(result.Deductions.TaxDebt) + " TL",
                    "Denetim cezası: " + M(result.Deductions.InspectionPenalty) + " TL",
                    "SGK borcu: " + M(result.Deductions.SocialSecurityDebt) + " TL",
                    "Asgari ücret desteği: " + M(result.Deductions.MinimumWageSupport) + " TL",
                    "Ek kesin teminat: " + M(result.Deductions.AdditionalGuarantee) + " TL",
                    "Damga vergisi: " + M(result.Deductions.StampTax) + " TL",
                    "KDV durumu: " + vatLabel,
                    "",
                    "Not: Tüm değerler aynı hesaplayıcıdan anlık üretilir; Excel/VBA masaüstü ve dört PDF ile birebir kabul testi ayrıca yapılmalıdır."
                });
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                lines.Add("HESAPLAMA HATASI: " + ex.Message);
                lines.Add("Bu durumda sonuçlar geçerli kabul edilmez.");
            }
        }

        if (_multiRoutePaymentRows.Count > 0 && selectedContract is not null)
        {
            lines.Add("");
            lines.Add("5) ÇOKLU HAT — HER SATIRIN CANLI HESABI");
            foreach (var row in _multiRoutePaymentRows)
            {
                try
                {
                    var rowInput = new ProgressPaymentInput(
                        row.WorkDays, row.DailyPrice, row.DiscountAmount, row.PriceDifference,
                        _routes.FirstOrDefault(route => HybridRouteKey(route) == row.RouteKey)?.ContractAmount ?? selectedContract.ContractAmount, IsVatExemptForCurrentFirm(selectedContract),
                        row.TaxDebt, row.SocialSecurityDebt,
                        selectedProgram == TransportationProgramType.BasicOrSecondary && !row.MinimumWageSupportEnabled
                            ? 0m : row.MinimumWageSupport,
                        row.InspectionPenalty,
                        hasGuide ? row.GuideWorkDays : 0m,
                        hasGuide ? row.GuideDailyPrice : 0m,
                        hasGuide ? row.GuideDiscountAmount : 0m,
                        hasGuide ? row.GuidePriceDifference : 0m,
                        selectedContract.ProgramType);
                    var rowResult = ProgressPaymentCalculator.Calculate(rowInput, _calculationConstants);
                    lines.Add("Hat " + row.RouteNumber + " — " + row.RouteName);
                    lines.Add("  K = G×H+I−J = " + M(rowResult.TransportNetAmount) + " TL");
                    lines.Add("  L — Taşıma KDV = " + M(rowResult.TransportVat) + " TL");
                    lines.Add("  M — Taşıma tevkifat = " + M(rowResult.TransportWithholding) + " TL");
                    lines.Add("  N — Taşıma fatura ara toplamı = " + M(rowResult.TransportNetAmount + rowResult.TransportWithholding) + " TL");
                    lines.Add("  O — Taşıma brüt = " + M(rowResult.TransportGrossAmount) + " TL");
                    lines.Add("  T = P×Q+R−S = " + M(rowResult.GuideNetAmount) + " TL");
                    lines.Add("  U — Rehber KDV = " + M(rowResult.GuideVat) + " TL");
                    lines.Add("  V — Rehber tevkifat = " + M(rowResult.GuideWithholding) + " TL");
                    lines.Add("  W — Rehber fatura ara toplamı = " + M(rowResult.GuideNetAmount + rowResult.GuideWithholding) + " TL");
                    lines.Add("  X — Rehber brüt = " + M(rowResult.GuideGrossAmount) + " TL");
                    lines.Add("  AC — Ek kesin teminat = " + M(rowResult.AdditionalGuarantee) + " TL");
                    lines.Add("  AD — Damga vergisi = " + M(rowResult.StampTax) + " TL");
                    lines.Add("  AE — Toplam kesinti = " + M(rowResult.TotalDeductions) + " TL");
                    lines.Add("  AF — Brüt = " + M(rowResult.GrossAmount) + " TL");
                    lines.Add("  AG — Net ödeme = " + M(rowResult.NetPayable) + " TL");
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    lines.Add("Hat " + row.RouteNumber + " — " + row.RouteName + ": HESAP HATASI — " + ex.Message);
                }
            }
        }

        CalculationTraceLiveTextBox.Text = string.Join(Environment.NewLine, lines);
    }

    private ProgressPaymentHistoryData? GetLastCalculatedProgress()
    {
        if (_lastCalculatedHistoryId is Guid id)
            return _progressHistory.FirstOrDefault(x => x.Id == id);

        return GetSelectedProgressLatestPayment();
    }

    private void ArchiveCurrentProgressButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment) &&
            _editingArchiveSnapshot is not null &&
            !string.IsNullOrWhiteSpace(_editingArchiveSourcePath))
        {
            SaveArchiveCorrectionSnapshot(_editingArchiveSnapshot, "Arşiv");
            return;
        }

        var item = GetLastCalculatedProgress();
        if (item is null)
        {
            MessageBox.Show("Arşivlenecek tamamlanmış bir hakediş bulunamadı. Önce HAKEDİŞ HESAPLA/KAYDET düğmesine basın.", "OEHS",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Multi-route calculations share an exact CreatedAt, contract, period and
        // payment number. Archive every route snapshot in one click, independently.
        var batch = _progressHistory
            .Where(x => item.BatchId.HasValue
                ? x.BatchId == item.BatchId
                : x.CreatedAt == item.CreatedAt &&
                  string.Equals(x.TenderNumber, item.TenderNumber, StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(x.ContractNumber, item.ContractNumber, StringComparison.OrdinalIgnoreCase) &&
                  x.ProgressPaymentNumber == item.ProgressPaymentNumber &&
                  x.PeriodDate == item.PeriodDate &&
                  x.Input.ProgramType == item.Input.ProgramType)
            .OrderBy(x => x.RouteNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (batch.Length == 0)
            batch = new[] { item };

        var archiveResult = ProgressPaymentArchiveBatchService.Execute(
            batch,
            payment =>
            {
                if (TryArchiveProgressItem(payment, out var path, out _))
                    return (Success: true, Path: (string?)path, Error: (string?)null);

                return (Success: false, Path: (string?)null,
                    Error: $"Hat {payment.RouteNumber}: arşiv kaydı oluşturulamadı.");
            },
            path => File.Delete(path));

        if (!archiveResult.Success)
        {
            ArchiveStatusTextBlock.Text = archiveResult.RollbackFailures.Count == 0
                ? "ARŞİV İŞLEMİ GERİ ALINDI"
                : "KISMİ ARŞİV HATASI";

            if (archiveResult.RollbackFailures.Count > 0)
            {
                var rollbackDetails = string.Join(Environment.NewLine, archiveResult.RollbackFailures);
                var rollbackMessage = "Çoklu hat arşivleme tamamlanamadı. Bazı yeni dosyalar geri alınamadı; lütfen arşiv klasörünü kontrol edin."
                    + Environment.NewLine + rollbackDetails;
                CalculationStatusTextBlock.Text = rollbackMessage;
                MessageBox.Show(rollbackMessage, "OEHS — Arşiv Geri Alma Uyarısı",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                CalculationStatusTextBlock.Text =
                    "Çoklu hat arşivleme tamamlanamadı. Bu işlemde oluşturulan önceki arşiv dosyaları geri alındı.";
            }

            UpdateArchiveView();
            return;
        }

        var archivedPaths = archiveResult.WrittenPaths.ToList();

        _lastCalculatedHistoryId = null;
        UpdateArchiveView();
        NavigateToArchives();

        var routeSummary = string.Join(", ", batch.Select(x => x.RouteNumber).Where(x => !string.IsNullOrWhiteSpace(x)));
        var batchTitle = item.BatchDisplayName ?? $"Hakediş No {item.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? "-"}";
        var message = batch.Length == 1
            ? $"{batchTitle} arşive eklendi.\n\n{Path.GetFileName(archivedPaths[0])}"
            : $"{batch.Length} hatlık aylık kayıt arşivlendi.\n{batchTitle}\nHatlar: {routeSummary}\n\nHat ayrıntıları arşivde korunmuştur. Resmî çıktılar seçili hatların toplamıyla tek belge takımı olarak hazırlanır.";
        ArchiveStatusTextBlock.Text = "ARŞİV OLUŞTURULDU";
        ClearProgressEntryFields();
        SetProgressEditorMode(false);
        CalculationStatusTextBlock.Text = message;
        MessageBox.Show(message, "OEHS — Arşiv Bilgisi", MessageBoxButton.OK, MessageBoxImage.Information);
        _editingArchiveSourcePath = null;
    }

    private bool TryArchiveProgressItem(
        ProgressPaymentHistoryData item,
        out string archivePath,
        out int revision)
    {
        archivePath = string.Empty;
        revision = 0;

        var contract = FindContract(item.TenderNumber, item.ContractNumber, item.Input.ProgramType);
        var route = _routes.FirstOrDefault(x =>
            string.Equals(
                OehsIdentityService.BuildRouteKey(
                    x.TenderNumber, x.RouteNumber, x.ContractorTaxOrIdentityNumber, x.ContractNumber, x.ProgramType),
                OehsIdentityService.BuildRouteKey(
                    item.TenderNumber ?? string.Empty, item.RouteNumber ?? string.Empty,
                    item.ContractorTaxOrIdentityNumber ?? string.Empty, item.ContractNumber ?? string.Empty,
                    item.Input.ProgramType),
                StringComparison.Ordinal));

        var contractor = route is null
            ? FindContractor(item.ContractorTaxOrIdentityNumber ?? string.Empty, item.Input.ProgramType)
            : FindContractor(route.ContractorTaxOrIdentityNumber, route.ProgramType);
        if (contract is null || route is null || contractor is null)
        {
            MessageBox.Show($"Hat {item.RouteNumber}: arşiv için ihale, hat ve yüklenici snapshot'ı çözümlenemedi. Kaydı kontrol edin.",
                "OEHS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var routeData = new RouteData(
            route.RouteNumber, route.Name, route.DailyPrice, route.GuideDailyPrice, route.TransportWorkDays, route.TenderNumber,
            route.ContractorTaxOrIdentityNumber, route.ContractNumber, route.ProgramType, route.ContractAmount, route.ContractDate);

        var prior = _progressHistory
            .Where(x => x.Input.ProgramType == item.Input.ProgramType)
            .Where(x => string.Equals(
                OehsIdentityService.BuildRouteKey(
                    x.TenderNumber ?? string.Empty, x.RouteNumber ?? string.Empty,
                    x.ContractorTaxOrIdentityNumber ?? string.Empty, x.ContractNumber ?? string.Empty,
                    x.Input.ProgramType),
                OehsIdentityService.BuildRouteKey(
                    route.TenderNumber, route.RouteNumber, route.ContractorTaxOrIdentityNumber,
                    route.ContractNumber, route.ProgramType),
                StringComparison.Ordinal))
            .Where(x => x.Id != item.Id)
            .Where(x => x.PeriodDate.HasValue && (!item.PeriodDate.HasValue || x.PeriodDate.Value < item.PeriodDate.Value))
            .OrderBy(x => x.PeriodDate)
            .ThenBy(x => x.CreatedAt)
            .ToArray();

        var archive = new ProgressPaymentArchive(
            "1.0", 1, DateTime.UtcNow, "ActiveRecord", contract,
            new Contractor(
                contractor.Name,
                OehsIdentityService.NormalizeIdentity(contractor.TaxOrIdentityNumber),
                contractor.Address, contractor.Phone, contractor.Email,
                contractor.BankAccountNumber, contractor.ContactPerson, contractor.ProgramType, contractor.VatExempt),
            routeData, item, prior,
            OehsFileNameService.BuildProgressPdfFileName(
                item.TenderNumber ?? string.Empty, item.ContractNumber ?? string.Empty,
                route.RouteNumber, item.ProgressPaymentNumber ?? 1,
                item.PeriodDate ?? DateOnly.FromDateTime(DateTime.Today)),
            ProgressPaymentReportService.RequiredDocumentNames);

        try
        {
            archivePath = _archiveStore.Save(archive);
            revision = ExtractArchiveRevision(archivePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProgressPaymentArchiveException)
        {
            ArchiveStatusTextBlock.Text = "ARŞİV HATASI";
            CalculationStatusTextBlock.Text = $"Hat {item.RouteNumber}: {ex.Message}";
            MessageBox.Show(CalculationStatusTextBlock.Text, "OEHS — Arşiv Hatası",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void CorrectCurrentProgressButton_Click(object sender, RoutedEventArgs e)
    {
        var item = GetLastCalculatedProgress();
        if (item is null)
        {
            MessageBox.Show("Düzeltilecek hakediş bulunamadı.", "OEHS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        BeginHistoryOrArchiveCorrection(item);
    }

    private void EditProgressWithTarget(ProgressPaymentHistoryData item)
    {
        var target = AskHistoryEditTarget(item);
        if (!target.HasValue)
        {
            ClearArchiveCorrectionState();
            return;
        }

        var selection = ProgressCorrectionTargetService.Select(target.Value, item);
        switch (selection.Target)
        {
            case ProgressCorrectionTarget.Tender:
                BeginContractHistoryEdit(item);
                break;
            case ProgressCorrectionTarget.Contractor:
                BeginContractorHistoryEdit(item);
                break;
            case ProgressCorrectionTarget.Route:
                BeginRouteHistoryEdit(item);
                break;
            case ProgressCorrectionTarget.ProgressPayment:
                BeginProgressHistoryEdit(item);
                break;
            case ProgressCorrectionTarget.Cancel:
                ClearArchiveCorrectionState();
                break;
        }
    }

    private void UpdateArchiveView()
    {
        var selectedPath = (ArchiveItemsControl.SelectedItem as ArchiveRow)?.FullPath;
        _archiveRows.Clear();

        foreach (var row in GroupArchiveRows(FilterArchives()))
        {
            _archiveRows.Add(new ArchiveRow(
                row.FullPath,
                row.DisplayTitle,
                row.DetailText,
                row.NetPayableText,
                row.RevisionText));
        }

        ArchiveCountTextBlock.Text = _archiveRows.Count.ToString(CultureInfo.CurrentCulture);

        ArchiveItemsControl.SelectedItem = _archiveRows.FirstOrDefault(row => row.FullPath == selectedPath);
        ArchiveStatusTextBlock.Text = "İşlem yapmak için bir arşiv satırı seçin. Önceki revizyonlar korunur.";
        ArchiveEmptyStateBorder.Visibility = _archiveRows.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void LoadArchiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ArchiveRow row })
            return;

        var chosen=ChooseArchiveMember(row);
        if(chosen is null)return;
        row=chosen;

        if (sender is Button loadButton &&
            string.Equals(loadButton.Content?.ToString(), "ÇAĞIR", StringComparison.OrdinalIgnoreCase))
        {
            ClearArchiveCorrectionState();
        }

        ProgressPaymentArchive archive;
        try
        {
            archive = ProgressPaymentArchiveStore.Load(row.FullPath);
        }
        catch (ProgressPaymentArchiveException ex)
        {
            MessageBox.Show(ex.Message, "OEHS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var contractsBeforeRestore = _contracts.ToArray();
            var contractorsBeforeRestore = _contractors.ToArray();
            var routesBeforeRestore = _routes.ToArray();
            var historyBeforeRestore = _progressHistory.ToArray();

            var currentData = new OehsApplicationData(
                _contracts.FirstOrDefault(),
                _routes.Select(x => new RouteData(
                    x.RouteNumber,
                    x.Name,
                    x.DailyPrice,
                    x.GuideDailyPrice,
                    x.TransportWorkDays,
                    x.TenderNumber,
                    x.ContractorTaxOrIdentityNumber,
                    x.ContractNumber, x.ProgramType, x.ContractAmount, x.ContractDate)).ToArray(),
                _contractors.Select(x => new Contractor(
                    x.Name,
                    OehsIdentityService.NormalizeIdentity(x.TaxOrIdentityNumber),
                    x.Address,
                    x.Phone,
                    x.Email,
                    x.BankAccountNumber,
                    x.ContactPerson,
                    x.ProgramType,
                    x.VatExempt)).ToArray(),
                _progressHistory.ToArray(),
                _contracts.ToArray());

            var restored = ProgressPaymentArchiveRestoreService.Restore(currentData, archive);

            _contracts.Clear();
            foreach (var contract in restored.Data.Contracts ?? Array.Empty<ContractDraftData>())
                _contracts.Add(contract);

            _contractors.Clear();
            foreach (var contractor in restored.Data.Contractors ?? Array.Empty<Contractor>())
                _contractors.Add(new ContractorDraft(
                    contractor.Name,
                    OehsIdentityService.NormalizeIdentity(contractor.TaxOrIdentityNumber),
                    contractor.Address,
                    contractor.Phone,
                    contractor.Email,
                    contractor.BankAccountNumber,
                    contractor.ContactPerson,
                    contractor.ProgramType,
                    contractor.VatExempt ?? HasLegacyContractVatExemption(contractor.TaxOrIdentityNumber, contractor.ProgramType)));

            _routes.Clear();
            foreach (var route in restored.Data.Routes ?? Array.Empty<RouteData>())
            {
                var contractor = FindContractor(route.ContractorTaxOrIdentityNumber, route.ProgramType);
                _routes.Add(new RouteDraft(
                    route.RouteNumber,
                    route.Name,
                    route.DailyPrice,
                    route.GuideDailyPrice,
                    route.TransportWorkDays,
                    route.TenderNumber,
                    OehsIdentityService.NormalizeIdentity(route.ContractorTaxOrIdentityNumber),
                    contractor?.Name ?? archive.Contractor.Name,
                    route.ContractNumber,
                    route.ProgramType,
                    route.ContractAmount ?? FindContract(route.TenderNumber, route.ContractNumber, route.ProgramType)?.ContractAmount,
                    route.ContractDate ?? FindContract(route.TenderNumber, route.ContractNumber, route.ProgramType)?.ContractDate));
            }

            _progressHistory.Clear();
            foreach (var item in restored.Data.ProgressPayments ?? Array.Empty<ProgressPaymentHistoryData>())
                _progressHistory.Add(item);

            if (!SavePersistedData())
            {
                RestoreLiveDataCollections(
                    contractsBeforeRestore,
                    contractorsBeforeRestore,
                    routesBeforeRestore,
                    historyBeforeRestore);
                RefreshViewsAfterPersistenceRollback();
                MessageBox.Show(
                    "Arşiv dosyası okundu ancak aktif OEHS verisi diske kaydedilemedi. Birleştirme geri alındı; mevcut aktif kayıtlar korundu.",
                    "OEHS — Arşiv Geri Yükleme Tamamlanamadı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Refresh master lists before selecting the editor context. Refreshing
            // afterwards clears its contractor, route and period selections.
            UpdateContractSelectors();
            UpdateHistoryView();
            UpdateArchiveView();
            UpdateHomeSummaryView();
            UpdateAcceptanceView();
            BeginProgressHistoryEdit(archive.ProgressPayment);
            _lastCalculatedHistoryId = archive.ProgressPayment.Id;

            CalculationStatusTextBlock.Text =
                restored.AddedAnything
                    ? $"Arşiv çağrıldı ve aktif veriyle birleştirildi. {restored.Message} Kaydetmeden önce ön kontrol yapılacaktır."
                    : $"Arşiv çağrıldı. {restored.Message} Kaydetmeden önce ön kontrol yapılacaktır.";
        }
        catch (Exception ex) when (ex is ProgressPaymentArchiveException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Arşiv geri yüklenemedi.\n\n{ex.Message}", "OEHS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void EditArchiveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ArchiveRow row } && ChooseArchiveMember(row) is {} chosen)
            BeginCorrectionFromArchive(chosen);
    }

    private void BeginCorrectionFromArchive(ArchiveRow row)
    {
        try
        {
            var archive = ProgressPaymentArchiveStore.Load(row.FullPath);
            ClearLiveHistoryCorrectionContext();
            _editingArchiveSourcePath = row.FullPath;
            _periodEditingId = null; _periodEditingInput = null;
            _editingArchiveSnapshot = archive;
            _archiveCorrectionSection = ArchiveCorrectionSection.None;

            EditProgressWithTarget(archive.ProgressPayment);
        }
        catch (ProgressPaymentArchiveException ex)
        {
            ClearArchiveCorrectionState();
            MessageBox.Show(ex.Message, "OEHS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenArchiveFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ArchiveRow row })
            return;

        var folder = Path.GetDirectoryName(row.FullPath);
        OpenRecordsFolder(folder, archive: true);
    }

    private void UpdateHistoryView()
    {
        var selected = HistoryItemsControl.SelectedItem as HistoryRow;
        _historyRows.Clear();

        var orderedHistory = FilterHistory();

        foreach (var group in orderedHistory.GroupBy(ProgressPaymentBatchService.GroupKey))
        {
            var members=group.OrderBy(x=>x.RouteNumber,StringComparer.OrdinalIgnoreCase).ToArray();
            var item = members[0];
            var recordedAt = item.CreatedAt.Kind == DateTimeKind.Utc ? item.CreatedAt.ToLocalTime() : item.CreatedAt;
            var batchTitle = item.BatchDisplayName;
            var displayNumber = !string.IsNullOrWhiteSpace(batchTitle)
                ? $"{batchTitle} • Hat {item.RouteNumber ?? "-"} • Hakediş No {item.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? "-"}"
                : item.ProgressPaymentNumber.HasValue
                    ? $"Hakediş No {item.ProgressPaymentNumber.Value} • İhale/Dosya: {item.TenderNumber ?? "-"} • Hat: {item.RouteNumber ?? "-"}"
                    : $"Hesaplama • İhale/Dosya: {item.TenderNumber ?? "-"} • Hat: {item.RouteNumber ?? "-"}";
            if(members.Length>1)displayNumber=GroupRecordTitle(members);
            var periodText = item.PeriodDate.HasValue
                ? $"Dönem: {CultureInfo.GetCultureInfo("tr-TR").TextInfo.ToTitleCase(item.PeriodDate.Value.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR")))} • Yüklenici: {item.ContractorName ?? "-"} • Kayıt: {recordedAt:dd.MM.yyyy HH:mm}"
                : $"Yüklenici: {item.ContractorName ?? "-"} • Kayıt: {recordedAt:dd.MM.yyyy HH:mm}";

            _historyRows.Add(new HistoryRow(
                item.Id,
                displayNumber,
                periodText,
                FormatMoney(members.Sum(p=>p.Calculation.TotalAmount)),
                FormatMoney(members.Sum(p=>p.Calculation.NetPayable)), item.Input.ProgramType));
        }

        HistoryCountTextBlock.Text = _historyRows.Count.ToString(CultureInfo.CurrentCulture);
        HistoryNetTotalTextBlock.Text = FormatMoney(orderedHistory.Sum(x => x.Calculation.NetPayable));
        HistoryItemsControl.SelectedItem = _historyRows.FirstOrDefault(row => selected is not null && row.Id == selected.Id && row.ProgramType == selected.ProgramType);
        HistoryOperationStatusTextBlock.Text = string.Empty;
        HistoryOperationStatusTextBlock.Visibility = Visibility.Collapsed;
        HistoryEmptyStateBorder.Visibility = orderedHistory.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private ProgressPaymentHistoryData? GetHistoryItemFromRow(object? tag)
    {
        return tag is HistoryRow row
            ? _progressHistory.FirstOrDefault(x => x.Id == row.Id && x.Input.ProgramType == row.ProgramType)
            : null;
    }

    private static string GetHistoryArchiveFolder(ProgressPaymentHistoryData item) =>
        OutputDirectoryForPayment(item);

    private void OpenHistoryFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var item = GetHistoryItemFromRow((sender as Button)?.Tag);
        if (item is null)
            return;

        OpenRecordsFolder(GetHistoryArchiveFolder(item), archive: false);
    }

    private void EditHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        ClearArchiveCorrectionState();
        if (sender is Button { Tag: HistoryRow row })
        {
            var item = ChooseHistoryMember(row);
            if (item is not null)
                BeginHistoryOrArchiveCorrection(item);
        }
    }

    private void BeginHistoryOrArchiveCorrection(ProgressPaymentHistoryData item)
    {
        _historyOperationProgram = item.Input.ProgramType;
        ClearLiveHistoryCorrectionContext();
        ArchiveRow? matchingArchiveRow = null;
        var matchingRevision = 0;

        try
        {
            foreach (var file in _archiveStore.List())
            {
                ProgressPaymentArchive archive;
                try
                {
                    archive = ProgressPaymentArchiveStore.Load(file.FullName);
                }
                catch (ProgressPaymentArchiveException)
                {
                    // Invalid archives remain visible in the archive panel, but cannot be edited as a snapshot.
                    continue;
                }

                if (archive.ProgressPayment.Id != item.Id || archive.ProgressPayment.Input.ProgramType != item.Input.ProgramType || archive.Revision <= matchingRevision)
                    continue;

                matchingRevision = archive.Revision;
                var payment = archive.ProgressPayment;
                matchingArchiveRow = new ArchiveRow(
                    file.FullName,
                    $"İhale/Dosya: {payment.TenderNumber ?? "-"} • Hat {payment.RouteNumber ?? "-"} • Hakediş {payment.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? "-"}",
                    $"Dönem: {payment.PeriodDate?.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture) ?? "-"} • Yüklenici: {archive.Contractor.Name}",
                    FormatMoney(payment.Calculation.NetPayable),
                    $"v{archive.Revision:00}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProgressPaymentArchiveException)
        {
            MessageBox.Show(
                $"Hakedişin arşiv kaydı aranamadı; herhangi bir kayıt değiştirilmedi.\n\n{ex.Message}",
                "OEHS — Arşiv Arama Hatası",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (matchingArchiveRow is not null)
        {
            BeginCorrectionFromArchive(matchingArchiveRow);
            return;
        }

        // If this history row has never been archived, keep the regular live-record editing flow.
        EditProgressWithTarget(item);
    }

    private ProgressCorrectionTarget? AskHistoryEditTarget(ProgressPaymentHistoryData item)
    {
        ProgressCorrectionTarget? selected = null;
        var dialog = new Window
        {
            Title = !string.IsNullOrWhiteSpace(_editingArchiveSourcePath) ? "Arşiv Kaydı Düzelt" : "Hakediş Düzelt",
            Owner = this,
            Width = 500,
            Height = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White
        };

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = !string.IsNullOrWhiteSpace(_editingArchiveSourcePath)
                ? "Arşiv kaydında hangi bölümü düzeltmek istiyorsunuz?"
                : "Hangi bölümde düzeltme yapmak istiyorsunuz?",
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = System.Windows.Media.Brushes.DarkSlateGray,
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"İhale/Dosya: {item.TenderNumber ?? "-"} • Hat: {item.RouteNumber ?? "-"} • Hakediş: {item.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? "-"}",
            FontSize = 12.5,
            Foreground = System.Windows.Media.Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        foreach (var option in new[]
        {
            ("İHALE BİLGİLERİ", ProgressCorrectionTarget.Tender),
            ("YÜKLENİCİ FİRMA", ProgressCorrectionTarget.Contractor),
            ("HAT BİLGİLERİ", ProgressCorrectionTarget.Route),
            ("HAKEDİŞ GİRDİLERİ", ProgressCorrectionTarget.ProgressPayment),
            ("VAZGEÇ", ProgressCorrectionTarget.Cancel)
        })
        {
            var button = new Button
            {
                Content = option.Item1,
                Width = 420,
                Height = 42,
                Margin = new Thickness(0, 4, 0, 4),
                Style = (Style)FindResource(option.Item2 == ProgressCorrectionTarget.Cancel ? "SecondaryButton" : "ModernButton")
            };
            button.Click += (_, _) =>
            {
                selected = option.Item2;
                dialog.DialogResult = true;
                dialog.Close();
            };
            panel.Children.Add(button);
        }

        dialog.Content = panel;
        dialog.ShowDialog();
        return selected;
    }

    private void BeginContractHistoryEdit(ProgressPaymentHistoryData item)
    {
        if (IsArchiveEditing)
        {
            _suppressArchiveDirtyTracking = true;
            _archiveCorrectionSection = ArchiveCorrectionSection.Contract;
            var archivedContract = _editingArchiveSnapshot!.Contract;
            _editingContractTender = null;
            ApplyContractToForm(archivedContract);
            NavigationButton_Click(NavContract, new RoutedEventArgs(Button.ClickEvent, NavContract));
            // Panel geçişinden sonra editör modunu son kez uygula.
            SetContractEditorMode(true);
            ContractStatusTextBlock.Text =
                "ARŞİV DÜZELTME MODU: İhale bilgilerini güncelleyin. DÜZENLE mevcut arşiv revision'ını aynı kayıt üzerinde güncelleyecektir.";
            _suppressArchiveDirtyTracking = false;
            return;
        }

        var contract = FindContract(item.TenderNumber, item.ContractNumber, item.Input.ProgramType);
        if (contract is null)
        {
            ShowAlreadyRegistered("Bu hakedişin bağlı olduğu ihale kaydı bulunamadı.");
            return;
        }

        _editingContractTender = contract.TenderNumber;
        ApplyContractToForm(contract);
        SetContractEditorMode(true);
        NavigationButton_Click(NavContract, new RoutedEventArgs(Button.ClickEvent, NavContract));
        ContractStatusTextBlock.Text = "Hakedişten gelen düzenleme: yalnızca İhale Bilgileri alanlarını düzeltin ve DÜZENLE'e basın.";
        SetLiveHistoryCorrectionContext(item.Id, HistoryCorrectionSection.Contract);
    }

    private void BeginContractorHistoryEdit(ProgressPaymentHistoryData item)
    {
        if (IsArchiveEditing)
        {
            _suppressArchiveDirtyTracking = true;
            _archiveCorrectionSection = ArchiveCorrectionSection.Contractor;
            var archivedContractor = _editingArchiveSnapshot!.Contractor;
            _editingContractorKey = null;
            ContractorNameTextBox.Text = archivedContractor.Name;
            ContractorTaxTextBox.Text = archivedContractor.TaxOrIdentityNumber;
            ContractorBankTextBox.Text = archivedContractor.BankAccountNumber;
            ContractorVatExemptCheckBox.IsChecked = archivedContractor.VatExempt ?? _editingArchiveSnapshot.ProgressPayment.Input.IsVatExempt;
            ClearContractorValidation();
            NavigationButton_Click(NavContractors, new RoutedEventArgs(Button.ClickEvent, NavContractors));
            // Panel geçişinden sonra editör modunu son kez uygula.
            SetContractorEditorMode(true);
            ContractorStatusTextBlock.Text =
                "ARŞİV DÜZELTME MODU: firma bilgilerini güncelleyin. Değişiklikleri kaydettiğinizde bu arşivin yeni revizyonu oluşturulur.";
            _suppressArchiveDirtyTracking = false;
            return;
        }

        var contractor = FindContractor(item.ContractorTaxOrIdentityNumber ?? string.Empty, item.Input.ProgramType);

        if (contractor is null)
        {
            ShowAlreadyRegistered("Bu hakedişin bağlı olduğu yüklenici firma bulunamadı.");
            return;
        }

        _editingContractorKey = BuildContractorKey(contractor);
        SetContractorEditorMode(true);
        ContractorNameTextBox.Text = contractor.Name;
        ContractorTaxTextBox.Text = contractor.TaxOrIdentityNumber;
        ContractorBankTextBox.Text = contractor.BankAccountNumber;
        ContractorVatExemptCheckBox.IsChecked = contractor.VatExempt;
        ClearContractorValidation();
        NavigationButton_Click(NavContractors, new RoutedEventArgs(Button.ClickEvent, NavContractors));
        ContractorStatusTextBlock.Text = "Hakedişten gelen düzenleme: Firma bilgilerini değiştirip DEĞİŞİKLİKLERİ KAYDET düğmesini kullanın.";
        SetLiveHistoryCorrectionContext(item.Id, HistoryCorrectionSection.Contractor);
    }

    private void BeginRouteHistoryEdit(ProgressPaymentHistoryData item)
    {
        ResetRouteDateValidation();
        if (IsArchiveEditing)
        {
            _suppressArchiveDirtyTracking = true;
            _archiveCorrectionSection = ArchiveCorrectionSection.Route;
            var archive = _editingArchiveSnapshot!;
            RouteTenderListBox.SelectedItem = null;
            RouteContractorListBox.ItemsSource = Array.Empty<ContractorDraft>();
            RouteContractorListBox.SelectedItem = null;

            RouteNumberTextBox.Text = archive.Route.RouteNumber;
            RouteNameTextBox.Text = archive.Route.Name;
            RouteDailyPriceTextBox.Text = FormatCalculationInput(archive.Route.DailyPrice);
            RouteContractAmountTextBox.Text = FormatCalculationInput(archive.Route.ContractAmount ?? archive.ProgressPayment.Input.ContractAmount);
            RouteContractDatePicker.SelectedDate = (archive.Route.ContractDate ?? archive.ProgressPayment.ContractDate ?? archive.Contract.ContractDate)?.ToDateTime(TimeOnly.MinValue);
            RouteGuideDailyPriceTextBox.Text = FormatCalculationInput(archive.Route.GuideDailyPrice);
            ApplyRouteProgramFields(archive.Contract.ProgramType);
            RouteNumberTextBox.IsReadOnly = false;
            RouteSelectedTenderTextBlock.Text = $"Arşiv ihale: {archive.Route.TenderNumber}";
            RouteSelectedContractorTextBlock.Text = $"Arşiv yüklenici: {archive.Contractor.Name}";

            _editingRoute = null;
            NavigationButton_Click(NavLines, new RoutedEventArgs(Button.ClickEvent, NavLines));
            // Panel açıldıktan sonra editör modunu son kez uygula. Böylece panel geçişi
            // veya seçim olayları DÜZENLE / DÜZENLEME İPTAL durumunu bozamaz.
            SetRouteEditorMode(true, archiveCorrection: true);
            RouteStatusTextBlock.Text =
                "ARŞİV DÜZELTME MODU: Hat No dahil ilgili alanları değiştirebilirsiniz. Değişiklikleri kaydettiğinizde yeni arşiv revizyonu oluşturulur; yeni hat oluşturulmaz.";
            _suppressArchiveDirtyTracking = false;
            return;
        }

        var historyRouteKey = OehsIdentityService.BuildRouteKey(
            item.TenderNumber ?? string.Empty,
            item.RouteNumber ?? string.Empty,
            item.ContractorTaxOrIdentityNumber ?? string.Empty,
            item.ContractNumber ?? string.Empty,
            item.Input.ProgramType);

        var route = _routes.FirstOrDefault(x =>
            string.Equals(
                OehsIdentityService.BuildRouteKey(
                    x.TenderNumber,
                    x.RouteNumber,
                    x.ContractorTaxOrIdentityNumber,
                    x.ContractNumber,
                    x.ProgramType),
                historyRouteKey,
                StringComparison.Ordinal));

        if (route is null)
        {
            ShowAlreadyRegistered("Bu hakedişin bağlı olduğu hat bulunamadı.");
            return;
        }

        _editingRoute = route;

        var contract = FindContract(route.TenderNumber, route.ContractNumber, route.ProgramType);
        var contractor = FindContractor(route.ContractorTaxOrIdentityNumber, route.ProgramType);

        _suppressSelectionEvents = true;
        try
        {
            RouteTenderListBox.SelectedItem = contract;
            RouteContractorListBox.ItemsSource = contractor is null
                ? Array.Empty<ContractorDraft>()
                : new[] { contractor };
            RouteContractorListBox.SelectedItem = contractor;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        RouteNumberTextBox.Text = route.RouteNumber;
        RouteNumberTextBox.IsReadOnly = false;
        RouteNameTextBox.Text = route.Name;
        RouteDailyPriceTextBox.Text = FormatCalculationInput(route.DailyPrice);
        RouteContractAmountTextBox.Text = FormatCalculationInput(route.ContractAmount ?? item.Input.ContractAmount);
        RouteContractDatePicker.SelectedDate = (route.ContractDate ?? item.ContractDate)?.ToDateTime(TimeOnly.MinValue);
        RouteGuideDailyPriceTextBox.Text = FormatCalculationInput(route.GuideDailyPrice);
        SetRouteEditorMode(true);
        ApplyRouteProgramFields(route.ProgramType);
        NavigationButton_Click(NavLines, new RoutedEventArgs(Button.ClickEvent, NavLines));
        RouteStatusTextBlock.Text = "Hakedişten gelen düzenleme: Hat bilgilerini değiştirin ve DÜZENLE'e basın.";
        SetLiveHistoryCorrectionContext(item.Id, HistoryCorrectionSection.Route);
    }

    private void BeginProgressHistoryEdit(ProgressPaymentHistoryData item)
    {
        if (IsArchiveEditing)
        {
            _suppressArchiveDirtyTracking = true;
            _archiveCorrectionSection = ArchiveCorrectionSection.ProgressPayment;
            var archive = _editingArchiveSnapshot!;
            var payment = archive.ProgressPayment;
            _editingHistoryId = null;
            LoadArchiveProgressEditorContext(ArchiveProgressEditorContext.Create(archive));
            ProgressContractorTextBlock.Text = archive.Contractor.Name;
            ProgressRouteRelationTextBlock.Text =
                $"Arşiv ihale: {payment.TenderNumber} • Yüklenici: {archive.Contractor.Name} • Hat: {payment.RouteNumber} — {payment.RouteName}";
            WorkDaysTextBox.Text = payment.Input.WorkDays.ToString(CultureInfo.CurrentCulture);
            DailyPriceTextBox.Text = FormatCalculationInput(payment.Input.DailyPrice);
            GuideWorkDaysTextBox.Text = payment.Input.GuideWorkDays.ToString(CultureInfo.CurrentCulture);
            GuideDailyPriceTextBox.Text = FormatCalculationInput(payment.Input.GuideDailyPrice);
            DiscountTextBox.Text = FormatCalculationInput(payment.Input.DiscountAmount);
            GuideDiscountTextBox.Text = FormatCalculationInput(payment.Input.GuideDiscountAmount);
            PriceDifferenceTextBox.Text = FormatCalculationInput(payment.Input.PriceDifference);
            GuidePriceDifferenceTextBox.Text = FormatCalculationInput(payment.Input.GuidePriceDifference);
            TaxDebtTextBox.Text = FormatCalculationInput(payment.Input.TaxDebt);
            SocialSecurityDebtTextBox.Text = FormatCalculationInput(payment.Input.SocialSecurityDebt);
            MinimumWageSupportTextBox.Text = FormatCalculationInput(payment.Input.MinimumWageSupport);
            RestoreMinimumWageSupportInputState(payment.Input.ProgramType, payment.Input.MinimumWageSupport);
            InspectionPenaltyTextBox.Text = FormatCalculationInput(payment.Input.InspectionPenalty);
            ProgressNumberTextBox.Text = payment.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            SelectProgressPeriod(payment.PeriodDate);

            NavigationButton_Click(NavProgress, new RoutedEventArgs(Button.ClickEvent, NavProgress));
            // Panel geçişinden sonra editör modunu son kez uygula.
            SetProgressEditorMode(true, archiveCorrection: true);
            CalculationStatusTextBlock.Text =
                "ARŞİV DÜZELTME MODU: Hakediş girdilerini güncelleyin. DÜZENLE mevcut arşiv revision'ını aynı kayıt üzerinde güncelleyecektir.";
            _suppressArchiveDirtyTracking = false;
            return;
        }

        var contract = FindContract(item.TenderNumber, item.ContractNumber, item.Input.ProgramType);
        if (contract is not null)
        {
            _suppressSelectionEvents = true;
            try
            {
                ProgressTenderListBox.SelectedItem = contract;
                ProgressContractorComboBox.ItemsSource = _contractors
                    .Where(x => x.ProgramType == item.Input.ProgramType).ToArray();
                ProgressContractorComboBox.SelectedItem = FindContractor(
                    item.ContractorTaxOrIdentityNumber ?? string.Empty, item.Input.ProgramType);
            }
            finally
            {
                _suppressSelectionEvents = false;
            }
            UpdateProgressContractorAndRoutes(contract);
        }

        _periodEditingId = null; _periodEditingInput = null;
        _editingHistoryId = item.Id;
        _historyOperationProgram = item.Input.ProgramType;
        var historyRouteKey = OehsIdentityService.BuildRouteKey(
            item.TenderNumber ?? string.Empty,
            item.RouteNumber ?? string.Empty,
            item.ContractorTaxOrIdentityNumber ?? string.Empty,
            item.ContractNumber ?? string.Empty,
            item.Input.ProgramType);

        ProgressRouteComboBox.SelectedItem = _routes.FirstOrDefault(x =>
            string.Equals(
                OehsIdentityService.BuildRouteKey(
                    x.TenderNumber,
                    x.RouteNumber,
                    x.ContractorTaxOrIdentityNumber,
                    x.ContractNumber,
                    x.ProgramType),
                historyRouteKey,
                StringComparison.Ordinal));

        WorkDaysTextBox.Text = item.Input.WorkDays.ToString(CultureInfo.CurrentCulture);
        DailyPriceTextBox.Text = FormatCalculationInput(item.Input.DailyPrice);
        GuideWorkDaysTextBox.Text = item.Input.GuideWorkDays.ToString(CultureInfo.CurrentCulture);
        GuideDailyPriceTextBox.Text = FormatCalculationInput(item.Input.GuideDailyPrice);
        DiscountTextBox.Text = FormatCalculationInput(item.Input.DiscountAmount);
        GuideDiscountTextBox.Text = FormatCalculationInput(item.Input.GuideDiscountAmount);
        PriceDifferenceTextBox.Text = FormatCalculationInput(item.Input.PriceDifference);
        GuidePriceDifferenceTextBox.Text = FormatCalculationInput(item.Input.GuidePriceDifference);
        TaxDebtTextBox.Text = FormatCalculationInput(item.Input.TaxDebt);
        SocialSecurityDebtTextBox.Text = FormatCalculationInput(item.Input.SocialSecurityDebt);
        MinimumWageSupportTextBox.Text = FormatCalculationInput(item.Input.MinimumWageSupport);
        RestoreMinimumWageSupportInputState(item.Input.ProgramType, item.Input.MinimumWageSupport);
        InspectionPenaltyTextBox.Text = FormatCalculationInput(item.Input.InspectionPenalty);
        ProgressNumberTextBox.Text = item.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        SelectProgressPeriod(item.PeriodDate);

        SetProgressEditorMode(true);
        NavigationButton_Click(NavProgress, new RoutedEventArgs(Button.ClickEvent, NavProgress));
        CalculationStatusTextBlock.Text = "Düzenleme modu: hakediş girdilerini değiştirin ve DÜZENLE'e basın.";
        SetLiveHistoryCorrectionContext(item.Id, HistoryCorrectionSection.ProgressPayment);
    }

    private void CancelProgressEditButton_Click(object sender, RoutedEventArgs e)
    {
        var archiveCorrection = IsArchiveCorrectionFor(ArchiveCorrectionSection.ProgressPayment);
        var original = !archiveCorrection && _editingHistoryId is Guid id
            ? _progressHistory.FirstOrDefault(x => IsOperationPayment(x, id))
            : null;

        ClearArchiveCorrectionState();
        _editingHistoryId = null;

        if (archiveCorrection)
        {
            ProgressValidationBanner.Visibility = Visibility.Collapsed;
            NavigateToArchives();
            return;
        }

        if (original is not null)
        {
            WorkDaysTextBox.Text = original.Input.WorkDays.ToString(CultureInfo.CurrentCulture);
            DailyPriceTextBox.Text = FormatCalculationInput(original.Input.DailyPrice);
            GuideWorkDaysTextBox.Text = original.Input.GuideWorkDays.ToString(CultureInfo.CurrentCulture);
            GuideDailyPriceTextBox.Text = FormatCalculationInput(original.Input.GuideDailyPrice);
            DiscountTextBox.Text = FormatCalculationInput(original.Input.DiscountAmount);
            GuideDiscountTextBox.Text = FormatCalculationInput(original.Input.GuideDiscountAmount);
            PriceDifferenceTextBox.Text = FormatCalculationInput(original.Input.PriceDifference);
            GuidePriceDifferenceTextBox.Text = FormatCalculationInput(original.Input.GuidePriceDifference);
            TaxDebtTextBox.Text = FormatCalculationInput(original.Input.TaxDebt);
            SocialSecurityDebtTextBox.Text = FormatCalculationInput(original.Input.SocialSecurityDebt);
            MinimumWageSupportTextBox.Text = FormatCalculationInput(original.Input.MinimumWageSupport);
            RestoreMinimumWageSupportInputState(original.Input.ProgramType, original.Input.MinimumWageSupport);
            InspectionPenaltyTextBox.Text = FormatCalculationInput(original.Input.InspectionPenalty);
            ProgressNumberTextBox.Text = original.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            SelectProgressPeriod(original.PeriodDate);
        }

        SetProgressEditorMode(false);
        CalculationStatusTextBlock.Text = "Hakediş düzenlemesi iptal edildi; kayıt değiştirilmedi.";
    }

    private void DeleteHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HistoryRow row }) return;
        var item = ChooseHistoryMember(row);
        if (item is null) return;

        var title = item.ProgressPaymentNumber.HasValue
            ? $"Hakediş No {item.ProgressPaymentNumber.Value}"
            : "Hakediş Hesaplama";
        var result = MessageBox.Show(
            $"{title} kaydı silinsin mi? Bu işlem geri alınamaz.",
            "Hakediş Kaydını Sil", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var index = _progressHistory.IndexOf(item);
        var previousEditingHistoryId = _editingHistoryId;
        if (index < 0) return;

        _progressHistory.RemoveAt(index);
        if (_editingHistoryId == item.Id) _editingHistoryId = null;

        if (!SavePersistedData())
        {
            _progressHistory.Insert(index, item);
            _editingHistoryId = previousEditingHistoryId;
            RefreshViewsAfterPersistenceRollback();
            CalculationStatusTextBlock.Text =
                "Hakediş silinmedi: disk kaydı başarısız oldu; kayıt tekrar listeye alındı.";
            return;
        }

        UpdateHistoryView();
        UpdateSummaryView();
        UpdateReportView();
        UpdateHomeSummaryView();
        UpdateAcceptanceView();
        CalculationStatusTextBlock.Text = $"{title} kaydı silindi.";
    }

    private void UpdateSummaryView()
    {
        _summaryRows.Clear();

        var ordered = _progressHistory
            .OrderBy(x => x.PeriodDate ?? DateOnly.MaxValue)
            .ThenBy(x => x.ProgressPaymentNumber ?? int.MaxValue)
            .ThenBy(x => x.CreatedAt)
            .ToArray();

        foreach (var item in ordered)
        {
            var title = !string.IsNullOrWhiteSpace(item.BatchDisplayName)
                ? $"{item.BatchDisplayName} • Hat {item.RouteNumber ?? "-"} • Hakediş No {item.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? "-"}"
                : item.ProgressPaymentNumber.HasValue
                    ? $"Hakediş No {item.ProgressPaymentNumber.Value}"
                    : "Hakediş Hesaplama";

            var period = item.PeriodDate.HasValue
                ? $"Dönem: {CultureInfo.GetCultureInfo("tr-TR").TextInfo.ToTitleCase(item.PeriodDate.Value.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR")))}"
                : $"Kayıt: {item.CreatedAt:dd.MM.yyyy HH:mm}";

            _summaryRows.Add(new SummaryRow(
                title,
                period,
                FormatMoney(item.Calculation.GrossAmount),
                FormatMoney(item.Calculation.NetPayable)));
        }

        var latest = _progressHistory
            .Where(x => x.PeriodDate.HasValue)
            .OrderByDescending(x => x.PeriodDate)
            .FirstOrDefault();

        SummaryLatestPeriodTextBlock.Text = latest?.PeriodDate?.ToString("MM.yyyy", CultureInfo.CurrentCulture) ?? "—";
        SummaryCountTextBlock.Text = $"{_progressHistory.Count.ToString(CultureInfo.CurrentCulture)} hakediş";
        SummaryGrossTotalTextBlock.Text = FormatMoney(_progressHistory.Sum(x => x.Calculation.GrossAmount));
        SummaryNetTotalTextBlock.Text = FormatMoney(_progressHistory.Sum(x => x.Calculation.NetPayable));
        SummaryWithholdingTextBlock.Text = FormatMoney(_progressHistory.Sum(x => x.Calculation.Deductions.Withholding));
        SummaryStampTextBlock.Text = FormatMoney(_progressHistory.Sum(x => x.Calculation.Deductions.StampTax));
        SummaryOtherDeductionsTextBlock.Text = FormatMoney(_progressHistory.Sum(x =>
            x.Calculation.Deductions.TaxDebt
            + x.Calculation.Deductions.SocialSecurityDebt
            + x.Calculation.Deductions.MinimumWageSupport
            + x.Calculation.Deductions.InspectionPenalty
            + x.Calculation.Deductions.AdditionalGuarantee));

        SummaryEmptyStateBorder.Visibility = _summaryRows.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateReportView()
    {
        RefreshOutputRecords();
    }

    private void ProgressRouteComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _suppressSelectionEvents)
            return;

        // Keep every selected route's own monthly draft when the selection changes.
        UpdateMultiRouteDraftRows();
        UpdateProgressRouteDefaults();
    }

    private void UpdateMultiRouteDraftRows()
    {
        if (!IsInitialized) return;
        UpdateHybridRouteDraftRows();
    }

    private void UpdateHybridRouteDraftRows()
    {
        if (!TryCommitHybridGrid()) return;
        var selectedRoutes = ProgressRouteComboBox.SelectedItems.OfType<RouteDraft>().ToArray();
        var showMultiRouteGrid = selectedRoutes.Length > 0 &&
                                 !_editingHistoryId.HasValue &&
                                 !IsArchiveEditing;
        if (!showMultiRouteGrid)
        {
            foreach (var row in _multiRoutePaymentRows)
                _multiRouteDraftCache[row.RouteKey] = row;
            _multiRoutePaymentRows.Clear();
            MultiRouteEntryBorder.Visibility = Visibility.Collapsed;
            RefreshHybridUpdateButton();
            SetGlobalMonetaryFieldsVisible(true);
            return;
        }

        // Cache all drafts for this unsaved payment form, not just currently
        // selected routes. The grid/output path below still uses selected routes only.
        foreach (var visibleRow in _multiRoutePaymentRows)
            _multiRouteDraftCache[visibleRow.RouteKey] = visibleRow;
        var existingRows = _multiRouteDraftCache;
        var nextRows = new List<MultiRoutePaymentDraftRow>();
        foreach (var route in selectedRoutes.OrderBy(x => x.RouteNumber, StringComparer.OrdinalIgnoreCase))
        {
            var routeKey = OehsIdentityService.BuildRouteKey(
                route.TenderNumber,
                route.RouteNumber,
                route.ContractorTaxOrIdentityNumber,
                route.ContractNumber,
                route.ProgramType);
            if (existingRows.TryGetValue(routeKey, out var existing))
            {
                ConfigureHybridRow(existing, route);
                nextRows.Add(existing);
                continue;
            }

            var hasGuide = HasGuideStaff(route.ProgramType);
            var row = new MultiRoutePaymentDraftRow
            {
                RouteKey = routeKey,
                RouteNumber = route.RouteNumber,
                RouteName = route.Name,
                MinimumWageSupportEnabled =
                    route.ProgramType != TransportationProgramType.BasicOrSecondary,
                // Work days are entered only in this period-specific hybrid row.
                WorkDays = 0m,
                DailyPrice = route.DailyPrice,
                GuideWorkDays = 0,
                GuideDailyPrice = hasGuide ? route.GuideDailyPrice : 0m
            };

            SuggestPeriodDailyPrices(row, route);
            RestoreSavedHybridInput(row);
            ConfigureHybridRow(row, route);
            row.AcceptChanges();
            _multiRouteDraftCache[routeKey] = row;
            nextRows.Add(row);
        }

        _multiRoutePaymentRows.Clear();
        foreach (var row in nextRows)
            _multiRoutePaymentRows.Add(row);

        MultiRouteEntryBorder.Visibility = Visibility.Visible;
        SetGlobalMonetaryFieldsVisible(false);
        ApplyHybridColumnView();
        RefreshHybridTotals();
        RefreshHybridUpdateButton();
        SyncHybridSelectedRowFields();
    }

    private void SetGlobalMonetaryFieldsVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        var selectedProgram = (ProgressTenderListBox.SelectedItem as ContractDraftData)?.ProgramType;
        var showGuide = visible && selectedProgram.HasValue && HasGuideStaff(selectedProgram.Value);
        SinglePaymentFieldsGrid.Visibility = visible && (_editingHistoryId.HasValue || IsArchiveEditing)
            ? Visibility.Visible : Visibility.Collapsed;
        WorkDaysLabel.Visibility = visibility;
        WorkDaysTextBox.Visibility = visibility;
        DailyPriceLabel.Visibility = visibility;
        DailyPriceTextBox.Visibility = visibility;
        ProgressGuideWorkDaysLabel.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        GuideWorkDaysTextBox.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        ProgressGuideDailyPriceLabel.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        GuideDailyPriceTextBox.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        DiscountLabel.Visibility = visibility;
        DiscountTextBox.Visibility = visibility;
        PriceDifferenceLabel.Visibility = visibility;
        PriceDifferenceTextBox.Visibility = visibility;
        GuideDiscountLabel.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        GuideDiscountTextBox.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        GuidePriceDifferenceLabel.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        GuidePriceDifferenceTextBox.Visibility = showGuide ? Visibility.Visible : Visibility.Collapsed;
        TaxDebtLabel.Visibility = visibility;
        TaxDebtTextBox.Visibility = visibility;
        SocialSecurityDebtLabel.Visibility = visibility;
        SocialSecurityDebtTextBox.Visibility = visibility;
        MinimumWageSupportLabel.Visibility = visibility;
        MinimumWageSupportTextBox.Visibility = visibility;
        MinimumWageSupportEnabledCheckBox.Visibility =
            visible && selectedProgram == TransportationProgramType.BasicOrSecondary
                ? Visibility.Visible
                : Visibility.Collapsed;

        var supportToggleColumn = MultiRoutePaymentGrid.Columns.FirstOrDefault(column =>
            string.Equals(column.Header?.ToString(), "Asgari Ücret Desteği Kullan", StringComparison.Ordinal));
        if (supportToggleColumn is not null)
        {
            supportToggleColumn.Visibility =
                !visible && selectedProgram == TransportationProgramType.BasicOrSecondary
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        InspectionPenaltyLabel.Visibility = visibility;
        InspectionPenaltyTextBox.Visibility = visibility;
    }

    private void UpdateProgressRouteDefaults()
    {
        var selectedProgram = (ProgressTenderListBox.SelectedItem as ContractDraftData)?.ProgramType
            ?? TransportationProgramType.SpecialEducation;
        ManualAdditionalGuaranteeTextBox.IsEnabled = true;
        ManualAdditionalGuaranteeTextBox.Text = FormatMoney(0m);
        ManualAdditionalGuaranteeLabel.Text = "Ek Kesin Teminat (Hesaplanan)";
        GuaranteeRuleHintTextBlock.Text = selectedProgram == TransportationProgramType.BasicOrSecondary
            ? "Temel Eğitim/Ortaöğretim Taşıma: kaynak Excel formülü = I × %6"
            : "Özel Eğitim Taşıma: kaynak Excel formülü = (I × %6) + (R × %6)";
        var selectedRoutes = ProgressRouteComboBox.SelectedItems.OfType<RouteDraft>().ToArray();
        if (selectedRoutes.Length > 1)
        {
            var contract = ProgressTenderListBox.SelectedItem as ContractDraftData;
            var contractorNames = selectedRoutes
                .Select(x => x.ContractorName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            ProgressContractorTextBlock.Text = contractorNames.Length == 1
                ? contractorNames[0]
                : contract?.ContractorName ?? "Yükleniciler";
            ProgressRouteRelationTextBlock.Text =
                $"İKN: {contract?.TenderNumber ?? selectedRoutes[0].TenderNumber} • {selectedRoutes.Length} hat seçildi: " +
                string.Join(", ", selectedRoutes.Select(x => x.RouteNumber).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            UpdateAutomaticProgressNumber();
            return;
        }

        if (selectedRoutes.Length == 0 || ProgressRouteComboBox.SelectedItem is not RouteDraft route)
        {
            ProgressContractorTextBlock.Text = "Seçilmedi";
            ProgressRouteRelationTextBlock.Text = "Hat seçiniz.";
            _lastProgressDefaultsRouteKey = null;
            ProgressNumberTextBox.Clear();
            return;
        }

        ProgressContractorTextBlock.Text = string.IsNullOrWhiteSpace(route.ContractorName)
            ? "Firma seçilmemiş"
            : route.ContractorName;
        ProgressRouteRelationTextBlock.Text = $"İhale: {route.TenderNumber} • Yüklenici: {route.ContractorName} • Hat: {route.RouteNumber} — {route.Name}";

        var routeKey = OehsIdentityService.BuildRouteKey(
            route.TenderNumber, route.RouteNumber, route.ContractorTaxOrIdentityNumber,
            route.ContractNumber, route.ProgramType);
        var routeChanged = !string.Equals(_lastProgressDefaultsRouteKey, routeKey, StringComparison.Ordinal);
        if (routeChanged && !_editingHistoryId.HasValue && !IsArchiveEditing)
        {
            // A route change must never carry the previous route's monthly
            // discounts/debts/price differences into a new route.
            DiscountTextBox.Text = "0";
            PriceDifferenceTextBox.Text = "0";
            GuideDiscountTextBox.Text = "0";
            GuidePriceDifferenceTextBox.Text = "0";
            TaxDebtTextBox.Text = "0";
            SocialSecurityDebtTextBox.Text = "0";
            MinimumWageSupportTextBox.Text = "0";
            InspectionPenaltyTextBox.Text = "0";
        }

        DailyPriceTextBox.Text = FormatCalculationInput(route.DailyPrice);
        GuideDailyPriceTextBox.Text = FormatCalculationInput(
            HasGuideStaff(route.ProgramType) ? route.GuideDailyPrice : 0m);

        SyncHybridSelectedRowFields();
        _lastProgressDefaultsRouteKey = routeKey;

        if (!_editingHistoryId.HasValue && !IsArchiveEditing)
            UpdateAutomaticProgressNumber();
    }

    private void OutputTenderListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionEvents)
            return;

        UpdateOutputRoutesForContract(OutputTenderListBox.SelectedItem as ContractDraftData);
        UpdateReportView();
        UpdateAcceptanceView();
    }

    private void UpdateOutputRoutesForContract(ContractDraftData? contract)
    {
        var routes = contract is null
            ? Array.Empty<RouteDraft>()
            : _routes.Where(x => IsRouteForContract(x, contract))
                .GroupBy(x=>string.IsNullOrWhiteSpace(x.ContractorTaxOrIdentityNumber)?HybridRouteKey(x):OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber))
                .Select(g=>g.First()).ToArray();

        var previous = ReportRouteComboBox.SelectedItem as RouteDraft;
        ReportRouteComboBox.ItemsSource = routes;
        if (previous is not null)
            ReportRouteComboBox.SelectedItem = routes.FirstOrDefault(x =>
                string.Equals(x.ContractNumber, previous.ContractNumber, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
                    OehsIdentityService.NormalizeIdentity(previous.ContractorTaxOrIdentityNumber),
                    StringComparison.Ordinal));
        if (ReportRouteComboBox.SelectedItem is null && routes.Length > 0)
            ReportRouteComboBox.SelectedIndex = 0;
    }

    private void ReportRouteComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized)
            return;

        UpdateReportView();
        UpdateAcceptanceView();
    }

    private string CurrentContractorName =>
        (ReportRouteComboBox.SelectedItem as RouteDraft)?.ContractorName
        ?? (ProgressRouteComboBox.SelectedItem as RouteDraft)?.ContractorName
        ?? string.Empty;

    private ProgressPaymentHistoryData? GetSelectedProgressLatestPayment()
    {
        var contract = ProgressTenderListBox.SelectedItem as ContractDraftData;
        var route = ProgressRouteComboBox.SelectedItem as RouteDraft;
        if (contract is null || route is null)
            return null;

        return _progressHistory
            .Where(x =>
                string.Equals(x.TenderNumber, contract.TenderNumber, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ContractNumber, contract.ContractNumber, StringComparison.OrdinalIgnoreCase) &&
                x.Input.ProgramType == route.ProgramType &&
                string.Equals(x.RouteNumber, route.RouteNumber, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                        OehsIdentityService.NormalizeIdentity(x.ContractorTaxOrIdentityNumber),
                        OehsIdentityService.NormalizeIdentity(route.ContractorTaxOrIdentityNumber),
                        StringComparison.Ordinal))
            .OrderByDescending(x => x.PeriodDate ?? DateOnly.MinValue)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefault();
    }

    private ProgressPaymentHistoryData? GetSelectedRouteLatestPayment() => GetSelectedRoutePayments().LastOrDefault();

    private List<ProgressPaymentHistoryData> GetSelectedRoutePayments()
    {
        if (!TryGetOutputRoute(out var route)) return new List<ProgressPaymentHistoryData>();
        return _progressHistory.Where(payment => IsOutputPaymentForRoute(payment, route!))
            .OrderBy(payment => payment.PeriodDate ?? DateOnly.MinValue).ThenBy(payment => payment.CreatedAt).ToList();
    }



    private static TableCell CreateCell(string text, bool bold = false, bool right = false, bool center = false, bool shaded = false)
    {
        return new TableCell(new Paragraph(new Run(text ?? "-")))
        {
            Padding = new Thickness(5),
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextAlignment = right ? TextAlignment.Right : center ? TextAlignment.Center : TextAlignment.Left,
            Background = shaded ? System.Windows.Media.Brushes.Gainsboro : System.Windows.Media.Brushes.White,
            BorderBrush = System.Windows.Media.Brushes.Black,
            BorderThickness = new Thickness(0.5)
        };
    }

    private static Table CreateDocumentTable(params (string Label, string Value)[] rows)
    {
        var table = new Table { CellSpacing = 0, BorderBrush = System.Windows.Media.Brushes.Black, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10) };
        AddFittedColumns(table, 345d, 345d);
        var group = new TableRowGroup();
        foreach (var (label, value) in rows)
        {
            var row = new TableRow();
            row.Cells.Add(CreateCell(label, true, false, false, true));
            row.Cells.Add(CreateCell(value));
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        return table;
    }

    private static Table CreateThreeColumnTable(
        (string A, string B, string C)[] rows,
        string[] headers,
        bool includeHeader = true)
    {
        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = System.Windows.Media.Brushes.Black,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10)
        };
        AddFittedColumns(table, ThreeColumnWidths);

        var group = new TableRowGroup();

        if (includeHeader)
        {
            var header = new TableRow();
            for (var i = 0; i < headers.Length; i++)
                header.Cells.Add(CreateCell(headers[i], true, false, true, true));
            group.Rows.Add(header);
        }

        foreach (var item in rows)
        {
            var row = new TableRow();
            row.Cells.Add(CreateCell(item.A, false, false, true));
            row.Cells.Add(CreateCell(item.B));
            row.Cells.Add(CreateCell(item.C, false, true));
            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        return table;
    }

    private static Table CreateDeductionTable((string A, string B, string C)[] rows)
    {
        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = System.Windows.Media.Brushes.Black,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10)
        };
        AddFittedColumns(table, 56d, 36d, 426d, 172d);

        var group = new TableRowGroup();
        const int deductionItemCount = 12;

        var verticalCell = new TableCell
        {
            RowSpan = deductionItemCount,
            Padding = new Thickness(0),
            BorderBrush = System.Windows.Media.Brushes.Black,
            BorderThickness = new Thickness(0.5)
        };

        var verticalText = new TextBlock
        {
            Text = "KESİNTİLER VE MAHSUPLAR",
            FontSize = 9,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LayoutTransform = new System.Windows.Media.RotateTransform(-90),
            Width = 190,
            Height = 34
        };
        verticalCell.Blocks.Add(new BlockUIContainer(verticalText));

        for (var i = 0; i < rows.Length; i++)
        {
            var item = rows[i];
            var row = new TableRow();

            if (i == 0)
                row.Cells.Add(verticalCell);

            var shaded = item.A is "H" or "I";

            if (shaded)
            {
                row.Cells.Add(CreateCell(item.A, true, false, true, true));

                var summaryDescriptionCell = CreateCell(item.B, true, false, false, true);
                summaryDescriptionCell.ColumnSpan = 2;
                row.Cells.Add(summaryDescriptionCell);

                row.Cells.Add(CreateCell(item.C, true, true, false, true));
            }
            else
            {
                row.Cells.Add(CreateCell(item.A, false, false, true, false));
                row.Cells.Add(CreateCell(item.B, false, false, false, false));
                row.Cells.Add(CreateCell(item.C, false, true, false, false));
            }

            if (shaded)
            {
                foreach (var cell in row.Cells)
                    cell.Background = System.Windows.Media.Brushes.Gainsboro;
            }

            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        return table;
    }

    private static Table CreateReportHeaderTable(RouteDraft route, ProgressPaymentHistoryData latest)
    {
        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = System.Windows.Media.Brushes.Black,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8)
        };
        AddFittedColumns(table, 480d, 210d);

        var group = new TableRowGroup();
        var left = CreateCell(route.Name, true);
        left.RowSpan = 2;
        left.Padding = new Thickness(7);
        var row1 = new TableRow();
        row1.Cells.Add(left);
        row1.Cells.Add(CreateCell($"Hakediş No : {latest.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? "-"}", true, false, true));
        group.Rows.Add(row1);

        var row2 = new TableRow();
        row2.Cells.Add(CreateCell($"Hat No : {route.RouteNumber}", true, false, true));
        group.Rows.Add(row2);

        table.RowGroups.Add(group);
        return table;
    }

    private static Table CreateIcmalHeaderTable(RouteDraft route, ProgressPaymentHistoryData latest)
    {
        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = System.Windows.Media.Brushes.Black,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 8)
        };
        AddFittedColumns(table, 500d, 218.11d);

        var group = new TableRowGroup();
        var left = CreateCell(route.Name, true);
        left.RowSpan = 3;
        left.Padding = new Thickness(7);

        var row1 = new TableRow();
        row1.Cells.Add(left);
        row1.Cells.Add(CreateCell("Sayfa No : 1", true, false, true));
        group.Rows.Add(row1);

        var row2 = new TableRow();
        row2.Cells.Add(CreateCell($"Hakediş No : {latest.ProgressPaymentNumber?.ToString(CultureInfo.CurrentCulture) ?? "-"}", true, false, true));
        group.Rows.Add(row2);

        var row3 = new TableRow();
        row3.Cells.Add(CreateCell($"Hat No : {route.RouteNumber}", true, false, true));
        group.Rows.Add(row3);

        table.RowGroups.Add(group);
        return table;
    }

    private static Table CreateExtensionTable()
    {
        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = System.Windows.Media.Brushes.Black,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10)
        };
        AddFittedColumns(table, FourColumnWidths);

        var group = new TableRowGroup();
        var title = CreateCell("Süre Uzatım Kararlarının", true, false, true, true);
        title.ColumnSpan = 4;
        group.Rows.Add(new TableRow { Cells = { title } });

        var header = new TableRow();
        foreach (var h in ExtensionHeaders)
            header.Cells.Add(CreateCell(h, true, false, true, true));
        group.Rows.Add(header);

        for (var i = 0; i < 4; i++)
        {
            var row = new TableRow();
            row.Cells.Add(CreateCell("................", false, false, true));
            row.Cells.Add(CreateCell("................", false, false, true));
            row.Cells.Add(CreateCell("................", false, false, true));
            row.Cells.Add(CreateCell("................", false, false, true));
            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        return table;
    }

    private static Table CreateThreeValueTable(params (string A, string B, string C)[] rows)
    {
        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = System.Windows.Media.Brushes.Black,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10)
        };

        AddFittedColumns(table, ThreeValueWidths);

        var group = new TableRowGroup();
        foreach (var item in rows)
        {
            var row = new TableRow();
            row.Cells.Add(CreateCell(item.A, true, false, true));
            row.Cells.Add(CreateCell(item.B, true));
            row.Cells.Add(CreateCell(item.C, true, true));
            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        return table;
    }

    private static Table CreateFourColumnTable(IEnumerable<(string A, string B, string C, string D)> rows, string[] headers)
    {
        var table = new Table { CellSpacing = 0, BorderBrush = System.Windows.Media.Brushes.Black, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10) };
        AddFittedColumns(table, FourColumnWidths);

        var group = new TableRowGroup();
        var header = new TableRow();
        foreach (var h in headers)
            header.Cells.Add(CreateCell(h, true, false, true, true));
        group.Rows.Add(header);

        foreach (var item in rows)
        {
            var row = new TableRow();
            row.Cells.Add(CreateCell(item.A, false, false, true));
            row.Cells.Add(CreateCell(item.B));
            row.Cells.Add(CreateCell(item.C, false, true));
            row.Cells.Add(CreateCell(item.D, false, true));
            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        return table;
    }

    private static Table CreateFiveColumnTable(IEnumerable<(string A, string B, string C, string D, string E)> rows, string[] headers)
    {
        var table = new Table
        {
            CellSpacing = 0,
            BorderBrush = System.Windows.Media.Brushes.Black,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10)
        };

        AddFittedColumns(table, FiveColumnWidths);

        var group = new TableRowGroup();
        var header = new TableRow();
        foreach (var h in headers)
            header.Cells.Add(CreateCell(h, true, false, true, true));
        group.Rows.Add(header);

        foreach (var item in rows)
        {
            var row = new TableRow();
            row.Cells.Add(CreateCell(item.A, false, false, true));
            row.Cells.Add(CreateCell(item.B));
            row.Cells.Add(CreateCell(item.C, false, true));
            row.Cells.Add(CreateCell(item.D, false, true));
            row.Cells.Add(CreateCell(item.E, false, true));
            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        return table;
    }

    private static Table CreateSignatureTable(
        string leftTitle, string leftName, string leftRole,
        string rightTitle, string rightName, string rightRole)
    {
        var table = new Table
        {
            CellSpacing = 0,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 18, 0, 8)
        };

        AddFittedColumns(table, 359.055d, 359.055d);

        var group = new TableRowGroup();
        var row = new TableRow();

        row.Cells.Add(CreateSignatureCell(leftTitle, leftName, leftRole));
        row.Cells.Add(CreateSignatureCell(rightTitle, rightName, rightRole));

        group.Rows.Add(row);
        table.RowGroups.Add(group);
        return table;
    }

    private static TableCell CreateSignatureCell(string title, string name, string role)
    {
        var cell = new TableCell
        {
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 2, 12, 2)
        };

        cell.Blocks.Add(new Paragraph(new Run(title))
        {
            TextAlignment = TextAlignment.Center,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 14)
        });

        cell.Blocks.Add(new Paragraph(new Run(name))
        {
            TextAlignment = TextAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });

        cell.Blocks.Add(new Paragraph(new Run(role))
        {
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0)
        });

        return cell;
    }

    private static Paragraph CreateDocumentExampleFooter(string text)
    {
        return new Paragraph(new Run(text))
        {
            FontSize = 7.5,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };
    }

    private static FlowDocument CreateBaseDocument(string title, string subtitle)
    {
        const double a4Width = PdfLayoutService.A4Width;
        const double a4Height = PdfLayoutService.A4Height;
        const double horizontalMargin = (a4Width - DocumentContentWidth) / 2d;
        const double verticalMargin = 40d;
        const double contentWidth = DocumentContentWidth;
        // A4 sabit: 210 x 297 mm. Yazıcı sürücüsü için yatayda güvenli içerik alanı,
        // dikeyde gereksiz sıkıştırmayı azaltan makul kenar boşlukları.

        var document = new FlowDocument
        {
            PageWidth = a4Width,
            PageHeight = a4Height,
            PagePadding = new Thickness(horizontalMargin, verticalMargin, horizontalMargin, verticalMargin),
            FontFamily = new System.Windows.Media.FontFamily("Arial"),
            FontSize = 9,
            ColumnWidth = contentWidth,
            IsOptimalParagraphEnabled = true
        };

        var header = new Paragraph
        {
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        };
        header.Inlines.Add(new Run(title) { FontSize = 15, FontWeight = FontWeights.Bold });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            header.Inlines.Add(new LineBreak());
            header.Inlines.Add(new Run(subtitle) { FontSize = 9.5, FontWeight = FontWeights.Bold });
        }

        document.Blocks.Add(header);
        return document;
    }

    private static Paragraph SectionHeading(string text)
    {
        return new Paragraph(new Run(text))
        {
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 5, 0, 4)
        };
    }

    private static void AddFittedColumns(Table table, params double[] widths)
    {
        foreach (var width in PdfLayoutService.FitColumns(widths))
            table.Columns.Add(new TableColumn { Width = new GridLength(width) });
    }


    private static bool IsBeforePayment(ProgressPaymentHistoryData item, ProgressPaymentHistoryData current)
    {
        if (current.PeriodDate.HasValue && item.PeriodDate.HasValue)
            return item.PeriodDate.Value < current.PeriodDate.Value;

        return item.CreatedAt < current.CreatedAt;
    }

    private OehsOfficialDocumentContext? BuildSelectedOfficialDocumentContext()
    {
        if (_homeOutputPayment is null && !EnsureRouteForOutput()) return null;
        var latest = _homeOutputPayment ?? GetSelectedOutputPayment();
        if (latest is null || GetOutputBatchPayments(latest).Any(payment=>!EnsurePaymentPreflight(payment))) return null;
        return BuildOfficialDocumentContextForPayment(latest);
    }

    private OehsOfficialDocumentContext? BuildOfficialDocumentContextForPayment(ProgressPaymentHistoryData latest)
    {
        var members = GetOutputBatchPayments(latest);
        var contexts = members.Select(BuildSingleRouteDocumentContext).ToArray();
        if (contexts.Any(context => context is null)) return null;
        return OehsCombinedDocumentContext.Combine(contexts.Select(context => context!).ToArray());
    }

    private OehsOfficialDocumentContext? BuildSingleRouteDocumentContext(ProgressPaymentHistoryData latest)
    {
        ArgumentNullException.ThrowIfNull(latest);

        var routeKey = OehsIdentityService.BuildRouteKey(
            latest.TenderNumber ?? string.Empty,
            latest.RouteNumber ?? string.Empty,
            latest.ContractorTaxOrIdentityNumber ?? string.Empty,
            latest.ContractNumber ?? string.Empty,
            latest.Input.ProgramType);
        var route = _routes.FirstOrDefault(candidate => string.Equals(
            OehsIdentityService.BuildRouteKey(
                candidate.TenderNumber,
                candidate.RouteNumber,
                candidate.ContractorTaxOrIdentityNumber,
                candidate.ContractNumber,
                candidate.ProgramType),
            routeKey,
            StringComparison.Ordinal));
        if (route is null)
            return null;

        var payments = _progressHistory
            .Where(x => string.Equals(
                OehsIdentityService.BuildRouteKey(
                    x.TenderNumber ?? string.Empty,
                    x.RouteNumber ?? string.Empty,
                    x.ContractorTaxOrIdentityNumber ?? string.Empty,
                    x.ContractNumber ?? string.Empty,
                    x.Input.ProgramType),
                routeKey,
                StringComparison.Ordinal))
            .OrderBy(x => x.PeriodDate ?? DateOnly.MinValue)
            .ThenBy(x => x.CreatedAt)
            .ToList();
        if (!payments.Any(x => x.Id == latest.Id))
            payments.Add(latest);
        payments = ProgressPaymentReportService.OrderByPaymentPeriod(payments).ToList();

        var reportTotals = ProgressPaymentReportService.Calculate(latest, payments);
        var reportNetPayable = ProgressPaymentReportService.CalculateReportNetPayable(latest, reportTotals);
        var previousDeduction = ProgressPaymentIcmalService.FindPreviousDeduction(payments, latest);
        var signatories = OehsOfficialDocumentSettingsStore.Load();

        var contract = FindContract(
            latest.TenderNumber ?? route.TenderNumber,
            latest.ContractNumber ?? route.ContractNumber,
            latest.Input.ProgramType);

        var contractStartDate = latest.ContractStartDate ?? contract?.StartDate;
        var contractEndDate = latest.ContractEndDate ?? contract?.EndDate;
        var tenderDate = latest.TenderDate ?? contract?.TenderDate;
        var contractDate = latest.ContractDate ?? contract?.ContractDate;

        // Do not label a calendar-day subtraction as contract workdays.
        // If the authoritative workday count is absent, official forms show "-".
        var duration = contract?.ContractWorkDays ?? 0;
        var contractorName = string.IsNullOrWhiteSpace(latest.ContractorName)
            ? (string.IsNullOrWhiteSpace(contract?.ContractorName) ? "-" : contract.ContractorName.Trim())
            : latest.ContractorName.Trim();

        return new OehsOfficialDocumentContext(
            WorkName: OfficialTemplateProfile.DeriveWorkName(
                string.IsNullOrWhiteSpace(latest.RouteName) ? route.Name : latest.RouteName,
                string.IsNullOrWhiteSpace(latest.WorkName) ? contract?.WorkName : latest.WorkName),
            TenderNumber: latest.TenderNumber ?? route.TenderNumber,
            RouteNumber: latest.RouteNumber ?? route.RouteNumber,
            ContractorName: contractorName,
            ContractNumber: latest.ContractNumber ?? contract?.ContractNumber ?? string.Empty,
            ContractAmount: latest.Input.ContractAmount,
            TenderDate: tenderDate,
            ContractDate: contractDate,
            ContractStartDate: contractStartDate,
            ContractEndDate: contractEndDate,
            ContractWorkDays: duration,
            PeriodDate: latest.PeriodDate,
            ProgressPaymentNumber: latest.ProgressPaymentNumber,
            Calculation: latest.Calculation,
            Payments: payments,
            ReportTotals: reportTotals,
            ReportNetPayable: reportNetPayable,
            PreviousDeduction: previousDeduction,
            GeneratedDate: DateOnly.FromDateTime(DateTime.Today),
            PreparerName: signatories.PreparerName,
            PreparerRole: signatories.PreparerRole,
            ControllerName: signatories.ControllerName,
            ControllerRole: signatories.ControllerRole,
            ApproverName: signatories.ApproverName,
            ApproverRole: signatories.ApproverRole,
            ProgramType: latest.Input.ProgramType,
            RouteName: string.IsNullOrWhiteSpace(latest.RouteName) ? route.Name : latest.RouteName.Trim());
    }

    private FixedDocument? BuildSelectedRouteOutputDocument()
    {
        var context = BuildSelectedOfficialDocumentContext();
        return context is null ? null : OehsOfficialDocumentFactory.BuildCombined(context);
    }

    private void PreviewAllRouteDocumentsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var document = BuildSelectedRouteOutputDocument();
            if (document is null)
                return;

            new DocumentPreviewWindow(document) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Belge önizlemesi açılamadı.\n\n{ex.Message}",
                "OEHS — Belge Önizleme",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void PrintAllRouteDocumentsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var document = BuildSelectedRouteOutputDocument();
            if (document is null)
                return;

            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true)
                return;

            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            paginator.ComputePageCount();
            dialog.PrintDocument(paginator, "OEHS Hakediş Belgeleri");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Yazdırma işlemi başlatılamadı.\n\n{ex.Message}",
                "OEHS — Yazdırma",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private bool EnsureRouteForOutput()
    {
        if (GetSelectedOutputPayment() is not null) return true;
        UpdateReportView();
        ShowOutputStatus("İhale, yüklenici ve hakediş seçimini kontrol edin. Kayıt güncellendiyse bilgileri tekrar doğrulayıp işlemi yineleyin.", warning: true);
        return false;
    }

    private void CreateSaveAllRouteDocumentsButton_Click(object sender, RoutedEventArgs e)
    {
        string? stagingFolder = null;
        var preserveStagingFolderForRecovery = false;
        try
        {
            var context = BuildSelectedOfficialDocumentContext();
            if (context is null) return;
            var latest = _homeOutputPayment ?? GetSelectedOutputPayment();
            if (latest is null) return;
            var targetFolder = OehsOutputPathService.BuildOfficialPdfDirectory(OehsRuntimePaths.DesktopRoot, latest);
            Directory.CreateDirectory(targetFolder);

            var outputs = new[] { OehsOfficialDocumentFactory.BuildCombined(context) };
            if (outputs[0].Pages.Count != 4)
                throw new InvalidOperationException("Birleşik PDF için dört resmî belge sayfası gerekir.");

            var targetPaths = new[] { OehsOutputPathService.BuildPaymentDocumentsPdfFilePath(OehsRuntimePaths.DesktopRoot, latest) };

            var existingPaths = targetPaths.Where(File.Exists).ToArray();
            if (existingPaths.Length > 0)
            {
                var existingNames = string.Join(
                    Environment.NewLine,
                    existingPaths.Select(Path.GetFileName));

                var choice = MessageBox.Show(
                    this,
                    "Bu hakediş için aynı isimde PDF dosyaları zaten var. Onay verirsen yalnızca listelenen mevcut dosyalar yenileriyle değiştirilecek. Devam etmeden önce var olan dosyaların yedeği geçici olarak alınacak.\n\n" +
                    existingNames +
                    "\n\nMevcut PDF dosyalarının üzerine yazılsın mı?",
                    "OEHS — Mevcut PDF Dosyaları",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (choice != MessageBoxResult.Yes)
                    return;
            }

            // Stage the combined four-page PDF before replacing the final file.
            // Retain the original for rollback if publishing the new file fails.
            stagingFolder = Path.Combine(Path.GetTempPath(), "oehs-pdf-" + Guid.NewGuid().ToString("N"));
            var stagedPaths = outputs
                .Select((output, index) => Path.Combine(stagingFolder, $"{index}.pdf"))
                .ToArray();

            for (var index = 0; index < outputs.Length; index++)
            {
                PdfVectorWriter.Save(outputs[index], stagedPaths[index]);
                if (!File.Exists(stagedPaths[index]) || new FileInfo(stagedPaths[index]).Length < 500)
                    throw new IOException($"PDF dosyası doğrulanamadı: {Path.GetFileName(stagedPaths[index])}");
            }

            var backupFolder = Path.Combine(stagingFolder, "originals");
            Directory.CreateDirectory(backupFolder);
            var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var targetPath in existingPaths)
            {
                var backupPath = Path.Combine(backupFolder, $"{originals.Count}.pdf");
                File.Copy(targetPath, backupPath, overwrite: false);
                originals[targetPath] = backupPath;
            }

            var replacedPaths = new List<string>();
            try
            {
                for (var index = 0; index < targetPaths.Length; index++)
                {
                    File.Move(stagedPaths[index], targetPaths[index], overwrite: true);
                    replacedPaths.Add(targetPaths[index]);
                }
            }
            catch
            {
                // Best-effort rollback if an individual target could not be replaced.
                var rollbackFailed = false;
                foreach (var targetPath in replacedPaths.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (originals.TryGetValue(targetPath, out var backupPath) && File.Exists(backupPath))
                            File.Copy(backupPath, targetPath, overwrite: true);
                        else if (File.Exists(targetPath))
                            File.Delete(targetPath);
                    }
                    catch
                    {
                        rollbackFailed = true;
                    }
                }

                if (rollbackFailed)
                    preserveStagingFolderForRecovery = true;

                throw;
            }

            var metadataSaved = true;
            string? metadataError = null;
            try
            {
                foreach(var member in GetOutputBatchPayments(latest))
                {
                    SaveRouteDocumentsArchiveMetadata(member, targetPaths);
                    OutputEvidenceStore.Save(OehsRuntimePaths.DesktopRoot, member.Id, BuildOutputFingerprint(context), targetPaths, member.Input.ProgramType);
                }
                RefreshHomeOperations();
            }
            catch (Exception ex)
            {
                metadataSaved = false;
                metadataError = ex.Message;
            }

            CalculationStatusTextBlock.Text =
                $"Dört sayfalı tek PDF oluşturuldu: {Path.GetFileName(targetPaths[0])}.";

            if (!metadataSaved && _homeOutputPayment is not null)
            {
                MessageBox.Show(
                    this,
                    "Dört sayfalı PDF oluşturuldu; ancak arşiv bilgi dosyası güncellenemedi. PDF kayıt klasöründedir. Arşiv bilgi dosyası hatası:\n\n" + metadataError,
                    "OEHS — PDF Kaydedildi, Arşiv Bilgisi Uyarısı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            var savedNames = string.Join(Environment.NewLine, targetPaths.Select(Path.GetFileName));
            if (_homeOutputPayment is not null)
                ShowPdfSaveInfoDialog(targetFolder, savedNames, targetPaths[0]);
            else
            {
                RefreshOutputSelection(clearOperationStatus: false);
                ShowOutputStatus(metadataSaved ? "Dört sayfalı tek PDF kaydedildi. PDF klasörünü aç düğmesiyle dosyalara ulaşabilirsiniz."
                    : $"Dört sayfalı tek PDF kaydedildi; arşiv bilgi kaydı tamamlanamadı: {metadataError}", warning: !metadataSaved);
            }
        }
        catch (Exception ex)
        {
            var recoveryNote = preserveStagingFolderForRecovery && !string.IsNullOrWhiteSpace(stagingFolder)
                ? $"\n\nGeri alma işlemi tam tamamlanamadı. Kurtarma kopyaları şu klasörde korundu:\n{stagingFolder}"
                : string.Empty;
            if (_homeOutputPayment is null)
                ShowOutputStatus($"PDF kaydedilemedi: {ex.Message}{recoveryNote}", warning: true);
            else
                MessageBox.Show($"Resmî PDF belgeleri oluşturulurken hata oluştu.\n\n{ex.Message}{recoveryNote}",
                    "OEHS PDF Çıktı Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            if (!preserveStagingFolderForRecovery &&
                !string.IsNullOrWhiteSpace(stagingFolder) &&
                Directory.Exists(stagingFolder))
            {
                try
                {
                    Directory.Delete(stagingFolder, recursive: true);
                }
                catch
                {
                    // Any leftover staging/originals files are safer than masking the
                    // actual save result. The folder name makes them easy to identify.
                }
            }
        }
    }

private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaximizeRestoreButton();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            MaximizeRestore_Click(sender, e);
            return;
        }

        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void Window_StateChanged(object? sender, EventArgs e) => UpdateMaximizeRestoreButton();

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var navigationWidth = WindowLayoutMetrics.GetNavigationWidth(e.NewSize.Width);
        NavigationColumn.Width = new GridLength(navigationWidth);
        HeaderNavigationColumn.Width = new GridLength(navigationWidth);
    }

    private void ConfigureZeroValueInputs()
    {
        foreach (var textBox in new[]
        {
            WorkDaysTextBox,
            DailyPriceTextBox,
            GuideWorkDaysTextBox,
            GuideDailyPriceTextBox,
            GuideDiscountTextBox,
            GuidePriceDifferenceTextBox,
            TaxDebtTextBox,
            SocialSecurityDebtTextBox,
            MinimumWageSupportTextBox,
            InspectionPenaltyTextBox,
            DiscountTextBox,
            PriceDifferenceTextBox,

            RouteGuideDailyPriceTextBox
        })
        {
            textBox.GotFocus += ZeroValueTextBox_GotFocus;
        }
    }

    private static void ZeroValueTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && (textBox.Text.Trim() == "0" || textBox.Text.Trim() == "0,00" || textBox.Text.Trim() == "0.00"))
        {
            textBox.Clear();
            textBox.Select(0, 0);
        }
    }

    private void ResetNewProgressDraftAfterCalculation(ProgressPaymentHistoryData lastCalculated)
    {
        ClearProgressEntryFields();
        // Completed-record actions belong to the persisted payment, not the next draft's selectors.
        _lastCalculatedHistoryId = lastCalculated.Id;
        SetProgressEditorMode(false);
    }

    private void ClearProgressEntryFields(bool clearTenderSelection = true)
    {
        _periodEditingId = null; _periodEditingInput = null;
        foreach (var textBox in new[]
        {
            WorkDaysTextBox, DailyPriceTextBox, GuideWorkDaysTextBox, GuideDailyPriceTextBox,
            DiscountTextBox, GuideDiscountTextBox, PriceDifferenceTextBox, GuidePriceDifferenceTextBox,
            TaxDebtTextBox, SocialSecurityDebtTextBox, MinimumWageSupportTextBox, InspectionPenaltyTextBox
        })
        {
            textBox.Clear();
        }

        ProgressNumberTextBox.Clear();
        _lastSelectedPaymentMonthKey = null;
        _multiRoutePaymentRows.Clear();
        _multiRouteDraftCache.Clear();
        MultiRouteEntryBorder.Visibility = Visibility.Collapsed;
        SetGlobalMonetaryFieldsVisible(true);
        _lastProgressDefaultsRouteKey = null;
        _suppressSelectionEvents = true;
        try
        {
            ProgressPeriodMonthComboBox.SelectedItem = null;
            if (clearTenderSelection)
            {
                ProgressTenderListBox.SelectedItem = null;
                ProgressContractorComboBox.ItemsSource = Array.Empty<ContractorDraft>();
                ProgressContractorComboBox.SelectedItem = null;
                ProgressPeriodMonthComboBox.ItemsSource = Array.Empty<ProgressPaymentMonthOption>();
                ProgressRouteComboBox.ItemsSource = Array.Empty<RouteDraft>();
                ProgressContractorTextBlock.Text = "Seçilmedi";
                ProgressRouteRelationTextBlock.Text = "İhale ve hat seçiniz.";
            }

            ProgressRouteComboBox.SelectedItems.Clear();
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        ClearProgressValidationVisuals();
        ProgressValidationBanner.Visibility = Visibility.Collapsed;
        ProgressValidationMessageTextBlock.Text = string.Empty;
        ServiceAmountResultTextBlock.Text = FormatMoney(0m);
        GuideServiceAmountResultTextBlock.Text = FormatMoney(0m);
        TransportPriceDifferenceResultTextBlock.Text = FormatMoney(0m);
        GuidePriceDifferenceResultTextBlock.Text = FormatMoney(0m);
        CombinedPriceDifferenceResultTextBlock.Text = FormatMoney(0m);
        TotalAmountResultTextBlock.Text = FormatMoney(0m);
        VatResultTextBlock.Text = FormatMoney(0m);
        DeductionsResultTextBlock.Text = FormatMoney(0m);
        GrossResultTextBlock.Text = FormatMoney(0m);
        NetResultTextBlock.Text = FormatMoney(0m);
        NetControlResultTextBlock.Text = FormatMoney(0m);
        WithholdingResultTextBlock.Text = FormatMoney(0m);
        StampTaxResultTextBlock.Text = FormatMoney(0m);
        TaxDebtResultTextBlock.Text = FormatMoney(0m);
        SocialSecurityDebtResultTextBlock.Text = FormatMoney(0m);
        MinimumWageSupportResultTextBlock.Text = FormatMoney(0m);
        InspectionPenaltyResultTextBlock.Text = FormatMoney(0m);
        AdditionalGuaranteeResultTextBlock.Text = FormatMoney(0m);
        ManualAdditionalGuaranteeTextBox.Text = FormatMoney(0m);
        ProgressWorkDaysSummaryTextBlock.Text = "0";
        ProgressWorkDaysCardTextBlock.Text = "0 gün";
        ProgressNetSummaryTextBlock.Text = FormatMoney(0m);
        ProgressCalculationSummaryTextBlock.Text = "HAZIR";
    }

    private void ClearProgressValidationVisuals()
    {
        foreach (var control in new Control[]
        {
            DailyPriceTextBox, GuideDailyPriceTextBox, DiscountTextBox, GuideDiscountTextBox,
            PriceDifferenceTextBox, GuidePriceDifferenceTextBox, ContractAmountTextBox,
            TaxDebtTextBox, SocialSecurityDebtTextBox, MinimumWageSupportTextBox, InspectionPenaltyTextBox
        })
            ClearInvalid(control);
    }

    private static void MarkProgressInvalid(Control control)
    {
        MarkInvalid(control);
    }

    private void ConfigureContractValidationInputs()
    {
        foreach (var textBox in new[]
        {
            TenderNumberTextBox,
            ContractNumberTextBox,

            ContractAmountTextBox,
            ContractWorkDaysTextBox
        })
        {
            textBox.TextChanged += ContractValidationInput_TextChanged;
        }

        StartDatePicker.SelectedDateChanged += ContractValidationDateChanged;
        EndDatePicker.SelectedDateChanged += ContractValidationDateChanged;
    }

    private void ContractValidationInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is Control control)
            ClearInvalid(control);
    }

    private void ContractTransportDailyPriceTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (TurkishMoneyInput.TryParse(ContractTransportDailyPriceTextBox.Text, out var price) && price >= 0)
            ClearInvalid(ContractTransportDailyPriceTextBox);
    }

    private static string NormalizePhoneNumber(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0 || digits.All(digit => digit == '0'))
            return string.Empty;

        // Accept Turkish international prefixes as well as the domestic format.
        if (digits.StartsWith("0090", StringComparison.Ordinal) && digits.Length >= 14)
            digits = digits[4..];
        else if (digits.StartsWith("90", StringComparison.Ordinal) && digits.Length >= 12)
            digits = digits[2..];

        // Stored/document values use one domestic prefix; the editor omits it.
        digits = digits.TrimStart('0');
        return digits.Length == 0 ? string.Empty : "0" + digits;
    }

    private void ContractValidationDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is Control control)
            ClearInvalid(control);
    }

    private static void MarkInvalid(Control control)
    {
        control.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 235, 235));
        control.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 53, 69));
        control.BorderThickness = new Thickness(2);
        control.ToolTip = "Bu alanı doldurun veya düzeltin.";
    }

    private static void ClearInvalid(Control control)
    {
        control.ClearValue(Control.BackgroundProperty);
        control.ClearValue(Control.BorderBrushProperty);
        control.ClearValue(Control.BorderThicknessProperty);
        control.ClearValue(Control.ToolTipProperty);
    }

    private void ClearContractValidationVisuals()
    {
        foreach (var control in new Control[]
        {
            TenderNumberTextBox,
            ContractNumberTextBox,

            ContractAmountTextBox,
            ContractWorkDaysTextBox,
            ContractTransportDailyPriceTextBox,
            StartDatePicker,
            EndDatePicker
        })
        {
            ClearInvalid(control);
        }

        ContractValidationBanner.Visibility = Visibility.Collapsed;
        ContractValidationMessageTextBlock.Text = string.Empty;
    }

    private void ShowContractValidation(string message)
    {
        ContractValidationMessageTextBlock.Text = message;
        ContractValidationBanner.Visibility = Visibility.Visible;
        ContractStatusTextBlock.Text = "Kaydetme durduruldu. Kırmızı işaretli alanları tamamlayın.";
        ContractStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(198, 40, 40));
    }

    private static bool TryParseDecimal(string? text, out decimal value)
    {
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value) ||
               decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    // Monetary editors use the same Turkish separators regardless of machine culture;
    // historical precision beyond kuruş is preserved when a record is reopened.
    private static string FormatCalculationInput(decimal value) =>
        TurkishMoneyInput.Format(value);

    private static decimal RoundMoney(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string FormatMoney(decimal value) => RoundMoney(value).ToString("N2", CultureInfo.GetCultureInfo("tr-TR")) + " ₺";
    private static string BuildPdfPrintTitle(RouteDraft route, ProgressPaymentHistoryData latest)
    {
        if (!latest.ProgressPaymentNumber.HasValue || !latest.PeriodDate.HasValue)
            return "OEHS_Hakediş";

        return Path.GetFileNameWithoutExtension(
            OehsFileNameService.BuildProgressPdfFileName(
                latest.TenderNumber ?? string.Empty,
                latest.ContractNumber ?? string.Empty,
                route.RouteNumber,
                latest.ProgressPaymentNumber.Value,
                latest.PeriodDate.Value));
    }


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
