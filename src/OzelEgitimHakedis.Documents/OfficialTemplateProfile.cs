using System;
using OzelEgitimHakedis.Core.Models;

namespace OzelEgitimHakedis.Wpf;

public sealed class OfficialTemplateProfile
{
    private static readonly OfficialTemplateProfile Basic = new(
        "Hesaplama Bilgileri", "Hizmet İşleri Hakediş Raporu", null);
    private static readonly OfficialTemplateProfile Special = new(
        "Hesaplama", "Hizmetişleri Hakediş Raporu", "Rehber Personel Fiyat Farkı KDV Hariç");

    private OfficialTemplateProfile(string calculationSheetName, string serviceReportSheetName, string? guidePriceDifferenceLabel)
    {
        CalculationSheetName = calculationSheetName;
        ServiceReportSheetName = serviceReportSheetName;
        GuidePriceDifferenceLabel = guidePriceDifferenceLabel;
    }

    public string CalculationSheetName { get; }
    public string ServiceReportSheetName { get; }
    public string RouteNameLabel { get; } = "Hattın Adı";
    public string TransportPriceDifferenceLabel { get; } = "Taşıma Fiyat Farkı KDV Hariç";
    public string? GuidePriceDifferenceLabel { get; }
    public string NetPayableLabel { get; } = "Ele Geçen";

    public static OfficialTemplateProfile For(TransportationProgramType programType) => programType switch
    {
        TransportationProgramType.BasicOrSecondary => Basic,
        TransportationProgramType.SpecialEducation => Special,
        _ => throw new ArgumentOutOfRangeException(nameof(programType))
    };

    public static string DeriveWorkName(string? routeName, string? legacyWorkName = null) =>
        string.IsNullOrWhiteSpace(routeName)
            ? legacyWorkName?.Trim() ?? string.Empty
            : routeName.Trim() + " Hattı Öğrenci Taşıma İşi";
}
