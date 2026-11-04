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
    private static FixedPage BuildHizmetIsleriHakedisRaporu(OehsOfficialDocumentContext c)
    {
        var page = NewPage();
        var layout = OfficialPageLayouts.ServiceReport;
        var x = layout.Left;
        var w = layout.Width;

        // Frame encloses the form body; the signature block remains below it,
        // following the uploaded one-page reference PDF.
        AddText(page, "HİZMET İŞLERİ HAK EDİŞ RAPORU", x, layout.TitleTop, w, 25, 13.6, true, TextAlignment.Center);

        AddText(page, "Tarihi", x + 122, 81, 155, 19, 11.4, true, TextAlignment.Left);
        AddText(page, ":", x + 266, 81, 14, 19, 11.4, true, TextAlignment.Center);
        AddText(page, FormatDate(c.GeneratedDate), x + 303, 81, w - 303, 19, 12.5, false, TextAlignment.Left);
        AddText(page, "No.su", x + 122, 99, 155, 19, 11.4, true, TextAlignment.Left);
        AddText(page, ":", x + 266, 99, 14, 19, 11.4, true, TextAlignment.Center);
        AddText(page, c.ProgressPaymentNumber?.ToString(Turkish) ?? "-", x + 303, 99, w - 303, 19, 12.5, false, TextAlignment.Left);
        AddText(page, "Uygulama Yılı", x + 122, 117, 155, 19, 11.4, true, TextAlignment.Left);
        AddText(page, ":", x + 266, 117, 14, 19, 11.4, true, TextAlignment.Center);
        AddText(page, (c.PeriodDate?.Year ?? c.GeneratedDate.Year).ToString(Turkish), x + 303, 117, w - 303, 19, 12.5, false, TextAlignment.Left);

        AddText(page, "Yapılan Hizmetin Adı", x + 2, 139, 263, 79, 11.4, true, TextAlignment.Left);
        AddText(page, ":", x + 266, 139, 14, 79, 11.4, true, TextAlignment.Center);
        var serviceName = BuildWorkTitle(c, includeRouteNumber: false, includeStudentSuffix: true);
        // Older snapshots can carry a separate contract work description; retain it
        // alongside the route title, without repeating the same derived name.
        if (!string.IsNullOrWhiteSpace(c.WorkName)
            && !string.Equals(c.WorkName.Trim(), serviceName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(c.WorkName.Trim(), c.RouteName.Trim(), StringComparison.OrdinalIgnoreCase))
            serviceName += "\n" + c.WorkName.Trim();
        AddText(page, serviceName, x + 303, 139, w - 307, 79, 9.12, false, TextAlignment.Left);

        var info = new (string Label, string Value)[]
        {
            ("Yapılan Hizmetin Etüt / Proje No.su", "Proje Dışıdır."),
            ("Yüklenicinin Adı / Ticari Unvanı", c.ContractorName),
            ("Sözleşme Bedeli", FormatMoney(c.ContractAmount)),
            ("İhale Tarihi", FormatDate(c.TenderDate)),
            ("Sözleşme Tarihi ve No.su", FormatDate(c.ContractDate)),
            ("Sözleşmenin Sayıştayca Tescil Tarihi ve No'su", "Tescile Dahil Değil"),
            ("Tescilin Yükleniciye Tebliğ Tarihi", "Tescile Dahil Değil"),
            ("İşyeri Teslim Tarihi", FormatDate(c.ContractStartDate)),
            ("Sözleşmeye Göre İşin Süresi", FormatWorkDays(c)),
            ("Sözleşmeye Göre İş Bitim Tarihi", FormatDate(c.ContractEndDate)),
            ("Verilen Avanslar Toplamı", "Avans verilmeyecektir."),
            ("Mahsubu Yapılan Avansın Toplam Tutarı", "Yoktur.")
        };
        var infoY = 218d;
        foreach (var row in info)
        {
            var height = row.Label is "Yapılan Hizmetin Etüt / Proje No.su" or "Yüklenicinin Adı / Ticari Unvanı" ? 25d : row.Label is "Sözleşmenin Sayıştayca Tescil Tarihi ve No'su" or "Tescilin Yükleniciye Tebliğ Tarihi" ? 26d : 22d;
            AddText(page, row.Label, x + 2, infoY, 263, height, 11.4, true, TextAlignment.Left);
            AddText(page, ":", x + 266, infoY, 14, height, 11.4, true, TextAlignment.Center);
            AddText(page, row.Value, x + 303, infoY, w - 307, height, 12.5, false, TextAlignment.Left);
            infoY += height;
        }

        var y = 495d;
        var financialTop=y;
        var colWidths = new[] { 120d, 155d, 147d, w - 422d };
        var headings = new[]
        {
            "Sözleşme Bedeli",
            "Sözleşme Artış\nOnayının Tarihi No.su",
            "Ek Sözleşme\nBedeli",
            "Toplam Sözleşme\nBedeli"
        };
        var tx = x;
        for (var i = 0; i < colWidths.Length; i++)
        {
            AddText(page, headings[i], tx, y, colWidths[i], 42, 11.4, false, TextAlignment.Center);
            var underlineWidth = Math.Min(colWidths[i] - 24, headings[i].Split('\n').Max(line => line.Length) * 5.5);
            AddRule(page, tx + (colWidths[i] - underlineWidth) / 2, y + 39, underlineWidth, 0.65);
            tx += colWidths[i];
        }
        y += 42;
        for (var row = 0; row < 6; row++)
        {
            var values = row == 0
                ? new[] { FormatMoney(c.ContractAmount, false), "....................", "....................", FormatMoney(c.ContractAmount, false) }
                : new[] { "....................", "....................", "....................", "...................." };
            tx = x;
            for (var col = 0; col < colWidths.Length; col++)
            {
                AddText(page, values[col], tx, y, colWidths[col], 24, 11.4, false, TextAlignment.Center);
                tx += colWidths[col];
            }
            y += 24;
        }

        AddOuterFrame(page,x,financialTop,w,y-financialTop);
        var extensionsTop=y;
        AddText(page, "Süre Uzatım Kararlarının", x+5, y, w-10, 28, 11.4, false, TextAlignment.Left);
        AddRule(page,x+5,y+25,160,0.65);
        y += 28;
        var extensionWidths = new[] { 60d, 65d, 245d, w - 370d };
        var extensionHeads = new[] { "Tarih", "Sayısı", "Verilen Süre", "İş Bitim Tarihi" };
        tx = x;
        for (var i = 0; i < extensionWidths.Length; i++)
        {
            AddText(page, extensionHeads[i], tx, y, extensionWidths[i], 28, 11.4, false, TextAlignment.Center);
            var underlineWidth = extensionHeads[i].Length * 5.5;
            AddRule(page, tx + (extensionWidths[i] - underlineWidth) / 2, y + 25, underlineWidth, 0.65);
            tx += extensionWidths[i];
        }
        y += 28;
        for (var row = 0; row < 4; row++)
        {
            tx = x;
            for (var col = 0; col < extensionWidths.Length; col++)
            {
                AddText(page, ".................", tx, y, extensionWidths[col], 24, 11.4, false, TextAlignment.Center);
                tx += extensionWidths[col];
            }
            y += 24;
        }

        AddOuterFrame(page,x,extensionsTop,w,y-extensionsTop);
        AddOuterFrame(page,x,layout.FrameTop,w,y-38);
        AddSignatures(page, 875, c, "M.Y.H.B.Y. Örnek No: 4",
            leftX: 110, leftWidth: 210, rightX: 450, rightWidth: 220,
            nameOffset: 35, roleOffset: 52, dateOffset: 103, directorOffset: 138, titleOffset: 154, footerY: 1070, footerX: x);
        return page;
    }

}
