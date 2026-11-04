using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using OzelEgitimHakedis.Core.Models;
using OzelEgitimHakedis.Core.Services;

namespace OzelEgitimHakedis.Wpf;

internal static partial class OehsOfficialDocumentFactory
{
    private static FixedPage BuildKabulTutanagi(OehsOfficialDocumentContext c)
    {
        var page = NewPage();
        var layout = OfficialPageLayouts.Acceptance;
        var x = layout.Left;
        var w = layout.Width;
        // The transparent outer rule is drawn after the cells so its full width remains visible.
        AddText(page, "HİZMET İŞLERİ KABUL TUTANAĞI", x, layout.TitleTop, w, 22, 15.36, true, TextAlignment.Center);

        var servicePeriod = ProgressPaymentAcceptancePeriodService.BuildServicePeriod(
            c.PeriodDate ?? c.GeneratedDate,
            c.ContractStartDate,
            c.ContractEndDate);
        var endDate = c.ContractEndDate;
        var isJune=c.PeriodDate?.Month==6;
        var completedDate=isJune?endDate:servicePeriod.End;
        var rows = new (string Label, string Value, double Height)[]
        {
            ("İşin adı", BuildWorkTitle(c, includeRouteNumber: false, includeStudentSuffix: true), 118),
            ("Yüklenicinin adı/ticari unvanı", c.ContractorName, 34.6),
            ("Sözleşme tarihi", FormatDate(c.ContractDate), 34.6),
            ("Sözleşme bedeli", FormatMoney(c.ContractAmount), 34.6),
            ("Sözleşmeye göre işin süresi (takvim günü)", FormatAcceptanceDuration(c), 34.6),
            ("Sözleşmeye göre işin bitirilmesi gereken tarih", FormatDate(endDate), 34.6),
            ("Varsa süre uzatımları", isJune && endDate.HasValue?$"Süre uzatımı verilmemiş olup, {FormatDate(endDate)} tarihinde bitmiştir":"", 34.6),
            ("Süre uzatımı dahil işin bitirilmesi gereken tarih", FormatDate(endDate), 34.6),
            ("İşin bitirildiği tarih", isJune?completedDate?.ToDateTime(TimeOnly.MinValue).ToString("dd.MM.yyyy dddd",Turkish) ?? "-":FormatLongDate(completedDate), 34.6)
        };

        var y = 60d;
        foreach (var row in rows)
        {
            AddCell(page, row.Label, x, y, 310, row.Height, 12.8, true, TextAlignment.Left);
            AddCell(page, row.Value, x + 310, y, w - 310, row.Height, 12.8, false, TextAlignment.Left);
            y += row.Height;
        }

        var paragraphOne =
            $"İlçe Milli Eğitim Müdürlüğü'nün {BuildWorkTitle(c, includeRouteNumber: true, includeStudentSuffix: false)} yüklenicisi {c.ContractorName} arasında imzalanan sözleşme kapsamında gerçekleştirilen iş için, {c.PreparerRole} {c.PreparerName} ve {c.ControllerRole} {c.ControllerName} tarafından inceleme yapılmış yüklenici, {c.ContractorName} tarafından {servicePeriod.Start:dd.MM.yyyy} / {servicePeriod.End:dd.MM.yyyy} tarihleri arasında yapılan işlerin,";
        var paragraphTwo =
            "Sözleşme ve eklerine uygun olduğu ve kabule engel olabilecek eksik kusur ve arızaların bulunmadığı görülmüştür.";
        var paragraphOneY = y + 16;
        var narrative = MakeText(paragraphOne, 14.08, false, TextAlignment.Justify, Black, new Thickness(0));
        var paragraphHeight = Math.Max(105, PdfVectorWriter.RequiredTextHeight(narrative, w - 4));
        // Grow the paragraph before reducing its font, with a fixed reserve for signatures.
        paragraphHeight = Math.Min(paragraphHeight, 748 - paragraphOneY - 62);
        AddText(page, paragraphOne, x + 2, paragraphOneY, w - 4, paragraphHeight, 14.08, false, TextAlignment.Justify);
        AddText(page, paragraphTwo, x + 2, paragraphOneY + paragraphHeight + 14, w - 4, 48, 14.08, false, TextAlignment.Justify);

        AddOuterFrame(page,x,layout.FrameTop,w,y-38);
        AddText(page, "Yüklenici", x + 12, 778, 280, 24, 12.8, true, TextAlignment.Center);
        AddText(page, c.ContractorName, x + 12, 804, 280, 48, 12.8, false, TextAlignment.Center);
        AddSignatures(page, 913, c, "",
            leftX: 100, leftWidth: 210, rightX: 440, rightWidth: 220,
            nameOffset: 35, roleOffset: 53, dateOffset: 88, directorOffset: 124, titleOffset: 142);
        return page;
    }

}
