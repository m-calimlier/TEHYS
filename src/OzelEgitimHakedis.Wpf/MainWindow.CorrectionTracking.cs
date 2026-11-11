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

public partial class MainWindow
{
    private static void ShowAlreadyRegistered(string message)
    {
        NightMessageBox.Show(message, "TEHYS", MessageBoxButton.OK, MessageBoxImage.Information);
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
            calculation = PaymentScreenModel.Calculate(input, _calculationConstants);
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
            calculation = PaymentScreenModel.Calculate(input, _calculationConstants);
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

}
