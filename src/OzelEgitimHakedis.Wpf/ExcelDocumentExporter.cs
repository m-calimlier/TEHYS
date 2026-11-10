using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text;
using OzelEgitimHakedis.Core.Models;

namespace OzelEgitimHakedis.Wpf;

internal static class ExcelDocumentExporter
{
    public readonly record struct TableMoney(decimal Value);
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static void SaveTable(string filePath, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        if(headers.Count==0 || headers.Count>16384 || rows.Count>1048575 || rows.Any(r=>r.Count!=headers.Count))
            throw new ArgumentException("Excel tablosunun boyutu veya sütun sayısı geçersiz.");
        var sheet=new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\""+MainNs+"\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><cols><col min=\"1\" max=\""+headers.Count+"\" width=\"28\" customWidth=\"1\"/></cols><sheetData><row r=\"1\" ht=\"60\" customHeight=\"1\">");
        for(var c=0;c<headers.Count;c++) sheet.Append(Cell(TableColumn(c)+"1",headers[c],3));
        sheet.Append("</row>");
        for(var r=0;r<rows.Count;r++)
        {
            sheet.Append("<row r=\"").Append(r+2).Append("\">");
            for(var c=0;c<headers.Count;c++)
            {
                var reference=TableColumn(c)+(r+2).ToString(CultureInfo.InvariantCulture);
                var value=rows[r][c];
                if(value is TableMoney money)
                    sheet.Append("<c r=\"").Append(reference).Append("\" s=\"6\"><v>").Append(money.Value.ToString(CultureInfo.InvariantCulture)).Append("</v></c>");
                else if(value is bool flag) sheet.Append("<c r=\"").Append(reference).Append("\" t=\"b\"><v>").Append(flag?"1":"0").Append("</v></c>");
                else if(value is decimal or int or long or double or float)
                    sheet.Append("<c r=\"").Append(reference).Append("\"><v>").Append(Convert.ToString(value,CultureInfo.InvariantCulture)).Append("</v></c>");
                else sheet.Append(Cell(reference,value?.ToString()??"",4));
            }
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData><autoFilter ref=\"A1:").Append(TableColumn(headers.Count-1)).Append(rows.Count+1).Append("\"/></worksheet>");
        // Validate before replacing an existing file, including XML control characters in user text.
        System.Xml.Linq.XDocument.Parse(sheet.ToString());
        var fullPath=Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary=fullPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var file=File.Create(temporary))
            using(var zip=new ZipArchive(file,ZipArchiveMode.Create))
            {
                AddEntry(zip,"[Content_Types].xml",BuildContentTypes(1));
                AddEntry(zip,"_rels/.rels",BuildRootRelationships());
                AddEntry(zip,"xl/workbook.xml","<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\""+MainNs+"\" xmlns:r=\""+RelNs+"\"><sheets><sheet name=\"Seçili Hatlar\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                AddEntry(zip,"xl/_rels/workbook.xml.rels",BuildWorkbookRelationships(1));
                AddEntry(zip,"xl/styles.xml",BuildStyles());
                AddEntry(zip,"xl/worksheets/sheet1.xml",sheet.ToString());
            }
            File.Move(temporary,fullPath,true);
        }
        finally {if(File.Exists(temporary)) File.Delete(temporary);}
    }

    private static string TableColumn(int index)
    {
        var result="";
        for(var n=index+1;n>0;n=(n-1)/26) result=(char)('A'+(n-1)%26)+result;
        return result;
    }

    public static void Save(string filePath, string routeNumber, string routeName, string contractor, string workName, ProgressPaymentHistoryData latest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var sheets = new[]
        {
            BuildHakedisRaporu(routeNumber, routeName, contractor, workName, latest),
            BuildHizmetRaporu(routeNumber, routeName, contractor, workName, latest),
            BuildKabulTutanagi(routeNumber, routeName, contractor, workName, latest),
            BuildIcmal(routeNumber, routeName, contractor, workName, latest)
        };

        using var file = File.Create(filePath);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        AddEntry(zip, "[Content_Types].xml", BuildContentTypes(sheets.Length));
        AddEntry(zip, "_rels/.rels", BuildRootRelationships());
        AddEntry(zip, "xl/workbook.xml", BuildWorkbook(sheets));
        AddEntry(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationships(sheets.Length));
        AddEntry(zip, "xl/styles.xml", BuildStyles());

        for (var i = 0; i < sheets.Length; i++)
            AddEntry(zip, "xl/worksheets/sheet" + (i + 1) + ".xml", BuildWorksheet(sheets[i]));
    }

    private static SheetData BuildHakedisRaporu(string routeNumber, string routeName, string contractor, string workName, ProgressPaymentHistoryData item)
    {
        var c = item.Calculation;
        return new SheetData("Hakediş Raporu", new[]
        {
            Row($"{ProgramTypeLabel(item.Input.ProgramType)} • {workName}", workName, "Yüklenici", contractor),
            Row("Hat No", routeNumber, "Hat Adı", routeName),
            Row("İhale Kayıt Numarası (İKN)", item.TenderNumber ?? "-", "Yüklenici", contractor),
            Row("Sözleşme Bedeli", Money(item.Input.ContractAmount), "Banka Hesap No", item.BankAccountNumber ?? "-"),
            Row("İhale Tarihi", item.TenderDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "-", "Sözleşme Tarihi", item.ContractDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "-"),
            Row("İş Başlangıcı", item.ContractStartDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "-", "İş Bitişi", item.ContractEndDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "-"),
            Row("Hakediş No", item.ProgressPaymentNumber?.ToString(CultureInfo.InvariantCulture) ?? "-", "Dönem", item.PeriodDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "-"),
            Header("AÇIKLAMA", "TUTAR", "AÇIKLAMA", "TUTAR"),
            Row("Taşıma Hizmet Tutarı", Money(c.ServiceAmount), "Taşıma Fiyat Farkı", Money(c.PriceDifferenceExcludingVat)),
            Row("Taşıma Hakediş Tutarı", Money(c.TransportNetAmount), "Rehber Hizmet Tutarı", Money(c.GuideServiceAmount)),
            Row("Rehber Hakediş Tutarı", Money(c.GuideNetAmount), "Toplam Tutar", Money(c.TotalAmount)),
            Row("KDV", Money(c.Vat), "Tevkifat", Money(c.Withholding)),
            Row("Fatura Tutarı", Money(c.InvoiceAmountAfterWithholding), "Fiyat Farkı Teminatı", Money(c.AdditionalGuarantee)),
            Row("Damga Vergisi", Money(c.StampTax), "Toplam Kesinti", Money(c.TotalDeductions)),
            Row("Brüt Tutar", Money(c.GrossAmount), "Net Ödenecek", Money(c.NetPayable))
        });
    }

    private static SheetData BuildHizmetRaporu(string routeNumber, string routeName, string contractor, string workName, ProgressPaymentHistoryData item)
    {
        var c = item.Calculation;
        var d = c.Deductions;
        return new SheetData("Hizmet İşleri Hakediş", new[]
        {
            Row($"{ProgramTypeLabel(item.Input.ProgramType)} • {workName}", workName, "Yüklenici", contractor),
            Row("Hat No", routeNumber, "Hat Adı", routeName),
            Row("İhale Kayıt Numarası (İKN)", item.TenderNumber ?? "-", "Yüklenici", contractor),
            Row("Sözleşme Bedeli", Money(item.Input.ContractAmount), "Banka Hesap No", item.BankAccountNumber ?? "-"),
            Header("HİZMET / HESAP", "TUTAR", "KESİNTİ / MAHSUP", "TUTAR"),
            Row("Taşıma Hizmet", Money(c.ServiceAmount), "Vergi Borcu", Money(d.TaxDebt)),
            Row("Taşıma Fiyat Farkı", Money(c.PriceDifferenceExcludingVat), "SGK Borcu", Money(d.SocialSecurityDebt)),
            Row("Rehber Hizmet", Money(c.GuideServiceAmount), "Asgari Ücret Desteği", Money(d.MinimumWageSupport)),
            Row("Rehber Fiyat Farkı", Money(c.GuidePriceDifferenceExcludingVat), "Denetim Cezası", Money(d.InspectionPenalty)),
            Row("Toplam Tutar", Money(c.TotalAmount), "Fiyat Farkı Teminatı", Money(c.AdditionalGuarantee)),
            Row("KDV", Money(c.Vat), "Damga Vergisi", Money(c.StampTax)),
            Row("Tevkifat", Money(c.Withholding), "Toplam Kesinti", Money(c.TotalDeductions)),
            Row("Fatura Tutarı", Money(c.InvoiceAmountAfterWithholding), "Net Ödenecek", Money(c.NetPayable)),
            Row("Brüt Tutar", Money(c.GrossAmount), "KDV Durumu", item.Input.IsVatExempt ? "KDV İstisnası" : "KDV Uygulanır")
        });
    }

    private static SheetData BuildKabulTutanagi(string routeNumber, string routeName, string contractor, string workName, ProgressPaymentHistoryData item)
    {
        var input = item.Input;
        return new SheetData("Kabul Tutanağı", new[]
        {
            Row($"{ProgramTypeLabel(item.Input.ProgramType)} • {workName}", workName, "Yüklenici", contractor),
            Row("Hat No", routeNumber, "Hat Adı", routeName),
            Row("Sözleşme Bedeli", Money(item.Input.ContractAmount), "Banka Hesap No", item.BankAccountNumber ?? "-"),
            Row("İhale Kayıt Numarası (İKN)", item.TenderNumber ?? "-", "Sözleşme Tarihi", item.ContractDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "-"),
            Row("Sözleşme Tarihi", item.ContractDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "-", "Hakediş Tarihi", item.PeriodDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "-"),
            Header("BİLGİ", "DEĞER", "BİLGİ", "DEĞER"),
            Row("Döneme Esas Taşıma Hakediş Gün Sayısı (Döneme Esas THGS)", input.WorkDays.ToString(CultureInfo.InvariantCulture), "Taşıma Günlük Fiyatı", Money(input.DailyPrice)),
            Row("Rehber İş Günü (bu ay)", input.GuideWorkDays.ToString(CultureInfo.InvariantCulture), "Rehber Personel Günlük Fiyat", Money(input.GuideDailyPrice)),
            Row("Dönem Taşıma Hizmet", Money(item.Calculation.ServiceAmount), "Dönem Rehber Hizmet", Money(item.Calculation.GuideServiceAmount)),
            Row("Toplam Hakediş", Money(item.Calculation.TotalAmount), "Net Ödenecek", Money(item.Calculation.NetPayable)),
            Row("Düzenleyen", "", "Kontrol Eden", ""),
            Row("Onaylayan", "", "Tarih", DateTime.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture))
        });
    }

    private static SheetData BuildIcmal(string routeNumber, string routeName, string contractor, string workName, ProgressPaymentHistoryData item)
    {
        var c = item.Calculation;
        return new SheetData("Hakediş İcmali", new[]
        {
            Row($"{ProgramTypeLabel(item.Input.ProgramType)} • {workName}", workName, "Yüklenici", contractor),
            Row("Hat No", routeNumber, "Hat Adı", routeName),
            Row("İhale Kayıt Numarası (İKN)", item.TenderNumber ?? "-", "Yüklenici", contractor),
            Row("Sözleşme Bedeli", Money(item.Input.ContractAmount), "Banka Hesap No", item.BankAccountNumber ?? "-"),
            Row("Hakediş No", item.ProgressPaymentNumber?.ToString(CultureInfo.InvariantCulture) ?? "-", "Dönem", item.PeriodDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "-"),
            Header("KALEM", "TUTAR", "KALEM", "TUTAR"),
            Row("Hizmet Tutarı", Money(c.CombinedServiceAmount), "Fiyat Farkı", Money(c.CombinedPriceDifferenceExcludingVat)),
            Row("Toplam Hakediş", Money(c.TotalAmount), "KDV", Money(c.Vat)),
            Row("Tevkifat", Money(c.Withholding), "Damga Vergisi", Money(c.StampTax)),
            Row("Fiyat Farkı Teminatı", Money(c.AdditionalGuarantee), "Toplam Kesinti", Money(c.TotalDeductions)),
            Row("Brüt Tutar", Money(c.GrossAmount), "Net Ödenecek", Money(c.NetPayable))
        });
    }

    private static string ProgramTypeLabel(TransportationProgramType type) =>
        type == TransportationProgramType.BasicOrSecondary
            ? "Temel Eğitim/Ortaöğretim Taşıma"
            : "Özel Eğitim Taşıma";

    private static RowData Row(string a, string b, string c, string d) => new(false, a, b, c, d);
    private static RowData Header(string a, string b, string c, string d) => new(true, a, b, c, d);
    private static string Money(decimal value) => value.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("tr-TR")) + " ₺";

    private static void AddEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string BuildContentTypes(int count)
    {
        var sheets = string.Join("", Enumerable.Range(1, count).Select(i =>
            "<Override PartName=\"/xl/worksheets/sheet" + i + ".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" + sheets + "</Types>";
    }

    private static string BuildRootRelationships() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>";

    private static string BuildWorkbook(IReadOnlyList<SheetData> sheets)
    {
        var sheetXml = string.Join("", sheets.Select((s, i) =>
            "<sheet name=\"" + Xml(s.Name) + "\" sheetId=\"" + (i + 1) + "\" r:id=\"rId" + (i + 1) + "\"/>"));
        var names = string.Join("", sheets.Select((s, i) =>
            "<definedName name=\"_xlnm.Print_Area\" localSheetId=\"" + i + "\">'"
            + Xml(s.Name.Replace("'", "''", StringComparison.Ordinal)) + "'!$A$1:$D$" + s.Rows.Count + "</definedName>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"" + MainNs + "\" xmlns:r=\"" + RelNs + "\"><bookViews><workbookView xWindow=\"0\" yWindow=\"0\" windowWidth=\"24000\" windowHeight=\"14000\"/></bookViews><sheets>"
            + sheetXml + "</sheets><definedNames>" + names + "</definedNames></workbook>";
    }

    private static string BuildWorkbookRelationships(int count)
    {
        var rels = string.Join("", Enumerable.Range(1, count).Select(i =>
            "<Relationship Id=\"rId" + i + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet" + i + ".xml\"/>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"" + PackageRelNs + "\">" + rels + "<Relationship Id=\"styles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";
    }

    private static string BuildStyles() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"" + MainNs + "\"><fonts count=\"3\"><font><sz val=\"10\"/><name val=\"Arial\"/></font><font><b/><sz val=\"16\"/><name val=\"Arial\"/></font><font><b/><sz val=\"10\"/><name val=\"Arial\"/></font></fonts><fills count=\"4\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF0A2A49\"/><bgColor indexed=\"64\"/></patternFill></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE9F0F6\"/><bgColor indexed=\"64\"/></patternFill></fill></fills><borders count=\"2\"><border/><border><left style=\"thin\"/><right style=\"thin\"/><top style=\"thin\"/><bottom style=\"thin\"/></border></borders><cellXfs count=\"7\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\" wrapText=\"1\"/></xf><xf numFmtId=\"4\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf></cellXfs></styleSheet>";

    private static string BuildWorksheet(SheetData sheet)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"" + MainNs + "\"><sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr><sheetViews><sheetView workbookViewId=\"0\" showGridLines=\"0\"/></sheetViews><cols><col min=\"1\" max=\"1\" width=\"20\" customWidth=\"1\"/><col min=\"2\" max=\"2\" width=\"28\" customWidth=\"1\"/><col min=\"3\" max=\"3\" width=\"20\" customWidth=\"1\"/><col min=\"4\" max=\"4\" width=\"28\" customWidth=\"1\"/></cols><sheetData>");

        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            var excelRow = r + 1;
            sb.Append("<row r=\"" + excelRow + "\" ht=\"" + (r == 0 ? 28 : 21) + "\" customHeight=\"1\">");

            if (r == 0)
            {
                sb.Append(Cell("A" + excelRow, row.A, 1));
                sb.Append("</row>");
                continue;
            }

            sb.Append(Cell("A" + excelRow, row.A, row.IsHeader ? 2 : 3));
            sb.Append(Cell("B" + excelRow, row.B, row.IsHeader ? 2 : StyleFor(row.B)));
            sb.Append(Cell("C" + excelRow, row.C, row.IsHeader ? 2 : 3));
            sb.Append(Cell("D" + excelRow, row.D, row.IsHeader ? 2 : StyleFor(row.D)));
            sb.Append("</row>");
        }

        sb.Append("</sheetData><mergeCells count=\"1\"><mergeCell ref=\"A1:D1\"/></mergeCells><pageMargins left=\"0.3937\" right=\"0.3937\" top=\"0.3937\" bottom=\"0.3937\" header=\"0\" footer=\"0\"/><pageSetup paperSize=\"9\" orientation=\"portrait\" fitToWidth=\"1\" fitToHeight=\"0\" horizontalDpi=\"300\" verticalDpi=\"300\"/></worksheet>");
        return sb.ToString();
    }

    private static int StyleFor(string value) => value.Contains('₺', StringComparison.Ordinal) ? 5 : 4;

    private static string Cell(string reference, string value, int style)
    {
        // ST_Xstring interprets these sequences even in inline strings. Escape the
        // original underscore once so a literal route title stays literal in Excel.
        var literal = System.Text.RegularExpressions.Regex.Replace(value, @"_x[0-9A-Fa-f]{4}_", match => "_x005F_" + match.Value[1..]);
        var safe = Xml(literal).Replace("\r", "&#13;", StringComparison.Ordinal);
        return "<c r=\"" + reference + "\" s=\"" + style + "\" t=\"inlineStr\"><is><t xml:space=\"preserve\">" + safe + "</t></is></c>";
    }

    private static string Xml(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private sealed record RowData(bool IsHeader, string A, string B, string C, string D);
    private sealed record SheetData(string Name, IReadOnlyList<RowData> Rows);
}
