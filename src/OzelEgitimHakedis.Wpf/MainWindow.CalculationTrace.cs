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
                var result = PaymentScreenModel.Calculate(input, _calculationConstants);
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
                    var rowResult = PaymentScreenModel.Calculate(rowInput, _calculationConstants);
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

}
