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


}
