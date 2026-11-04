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
    private void RecalculateHistoryItem(int index, ProgressPaymentInput input, ProgressPaymentHistoryData metadataSource)
    {
        var result = PaymentScreenModel.Calculate(input, _calculationConstants);
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
            var calculation = PaymentScreenModel.Calculate(input, _calculationConstants);
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

            var result = PaymentScreenModel.Calculate(input, _calculationConstants);

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

}
