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
        control.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(78, 32, 47));
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


}
