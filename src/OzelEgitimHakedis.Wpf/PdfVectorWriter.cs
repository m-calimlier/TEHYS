using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using WpfBrush = System.Windows.Media.Brush;
using WpfImage = System.Windows.Controls.Image;
using WpfLine = System.Windows.Shapes.Line;
using WpfPath = System.Windows.Shapes.Path;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfEllipse = System.Windows.Shapes.Ellipse;

namespace OzelEgitimHakedis.Wpf;

/// <summary>PDF metni ayrılan alana eksiksiz yerleştirilemediğinde oluşur.</summary>
public sealed class PdfDocumentLayoutException : InvalidOperationException
{
    public PdfDocumentLayoutException(string message) : base(message) { }
}

/// <summary>
/// FixedDocument içeriğini tam sayfa ekran görüntüsüne çevirmeden PDF'e aktarır.
/// Metinler gerçek PDF metni, çizgi/kenarlıklar vektör; yalnız gerçek Image öğeleri bitmap kalır.
/// </summary>
public static class PdfVectorWriter
{
    private const double DipToPoint = 72.0 / 96.0;

    public static void Save(FixedDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Pages.Count == 0)
            throw new InvalidOperationException("PDF oluşturmak için sayfa bulunamadı.");

        var folder = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(folder))
            Directory.CreateDirectory(folder);

        using var pdf = new PdfDocument();
        pdf.Info.Title = "OEHS — Özel Eğitim Hakediş Sistemi";
        pdf.Info.Creator = "OEHS — Özel Eğitim Hakediş Sistemi";

        foreach (PageContent content in document.Pages)
        {
            if (content.Child is not FixedPage fixedPage)
                throw new InvalidOperationException("PDF sayfası yüklenemedi.");

            PreparePage(fixedPage);

            var page = pdf.AddPage();
            page.Width = XUnit.FromPoint(ToPoint(fixedPage.Width));
            page.Height = XUnit.FromPoint(ToPoint(fixedPage.Height));

            using var gfx = XGraphics.FromPdfPage(page);
            RenderElement(gfx, fixedPage, fixedPage);
        }

        pdf.Save(path);
    }

    private static void PreparePage(FixedPage page)
    {
        if (double.IsNaN(page.Width) || page.Width <= 0 || double.IsNaN(page.Height) || page.Height <= 0)
            throw new InvalidOperationException("PDF sayfa boyutu geçersiz.");

        page.Measure(new Size(page.Width, page.Height));
        page.Arrange(new Rect(new Point(0, 0), new Size(page.Width, page.Height)));
        page.UpdateLayout();
    }

    private static void RenderElement(XGraphics gfx, FixedPage page, DependencyObject current)
    {
        if (current is UIElement ui && ui.Visibility != Visibility.Visible)
            return;

        if (current is TextBlock textBlock)
        {
            DrawTextBlock(gfx, page, textBlock);
            return;
        }

        if (current is WpfImage image)
        {
            DrawImage(gfx, page, image);
            return;
        }

        if (current is Shape shape)
        {
            DrawShape(gfx, page, shape);
            return;
        }

        if (current is Border border)
            DrawBorder(gfx, page, border);
        else if (current is Panel panel)
            DrawPanelBackground(gfx, page, panel);

        var count = VisualTreeHelper.GetChildrenCount(current);
        for (var i = 0; i < count; i++)
            RenderElement(gfx, page, VisualTreeHelper.GetChild(current, i));
    }

    private static void DrawPanelBackground(XGraphics gfx, FixedPage page, Panel panel)
    {
        if (TryBrush(panel.Background, out var brush) && TryBounds(page, panel, out var rect))
            gfx.DrawRectangle(brush, rect);
    }

    private static void DrawBorder(XGraphics gfx, FixedPage page, Border border)
    {
        if (!TryBounds(page, border, out var rect))
            return;

        if (TryBrush(border.Background, out var background))
            gfx.DrawRectangle(background, rect);

        if (!TryColor(border.BorderBrush, out var color))
            return;

        var t = border.BorderThickness;
        DrawBorderLine(gfx, color, t.Left, rect.Left, rect.Top, rect.Left, rect.Bottom);
        DrawBorderLine(gfx, color, t.Top, rect.Left, rect.Top, rect.Right, rect.Top);
        DrawBorderLine(gfx, color, t.Right, rect.Right, rect.Top, rect.Right, rect.Bottom);
        DrawBorderLine(gfx, color, t.Bottom, rect.Left, rect.Bottom, rect.Right, rect.Bottom);
    }

    private static void DrawBorderLine(XGraphics gfx, XColor color, double thicknessDip, double x1, double y1, double x2, double y2)
    {
        if (thicknessDip <= 0)
            return;
        gfx.DrawLine(new XPen(color, Math.Max(0.1, ToPoint(thicknessDip))), x1, y1, x2, y2);
    }

    private static void DrawShape(XGraphics gfx, FixedPage page, Shape shape)
    {
        if (!TryBounds(page, shape, out var rect))
            return;

        var pen = TryColor(shape.Stroke, out var strokeColor)
            ? new XPen(strokeColor, Math.Max(0.1, ToPoint(shape.StrokeThickness)))
            : null;
        var hasFill = TryBrush(shape.Fill, out var fill);

        switch (shape)
        {
            case WpfLine line:
            {
                if (pen is null) return;
                var transform = line.TransformToAncestor(page);
                var p1 = transform.Transform(new Point(line.X1, line.Y1));
                var p2 = transform.Transform(new Point(line.X2, line.Y2));
                gfx.DrawLine(pen, ToPoint(p1.X), ToPoint(p1.Y), ToPoint(p2.X), ToPoint(p2.Y));
                break;
            }
            case WpfRectangle:
                if (hasFill && pen is not null) gfx.DrawRectangle(pen, fill, rect);
                else if (hasFill) gfx.DrawRectangle(fill, rect);
                else if (pen is not null) gfx.DrawRectangle(pen, rect);
                break;
            case WpfEllipse:
                if (hasFill && pen is not null) gfx.DrawEllipse(pen, fill, rect);
                else if (hasFill) gfx.DrawEllipse(fill, rect);
                else if (pen is not null) gfx.DrawEllipse(pen, rect);
                break;
            case WpfPath path:
                DrawFlattenedPath(gfx, page, path, pen, hasFill ? fill : null);
                break;
        }
    }

    private static void DrawFlattenedPath(XGraphics gfx, FixedPage page, WpfPath path, XPen? pen, XBrush? brush)
    {
        if (pen is null && brush is null)
            return;

        var geometry = path.RenderedGeometry?.GetFlattenedPathGeometry();
        if (geometry is null)
            return;

        var transform = path.TransformToAncestor(page);
        foreach (var figure in geometry.Figures)
        {
            var points = new List<XPoint>();
            var start = transform.Transform(figure.StartPoint);
            points.Add(new XPoint(ToPoint(start.X), ToPoint(start.Y)));

            foreach (var segment in figure.Segments)
            {
                if (segment is PolyLineSegment poly)
                {
                    foreach (var point in poly.Points)
                    {
                        var p = transform.Transform(point);
                        points.Add(new XPoint(ToPoint(p.X), ToPoint(p.Y)));
                    }
                }
                else if (segment is LineSegment line)
                {
                    var p = transform.Transform(line.Point);
                    points.Add(new XPoint(ToPoint(p.X), ToPoint(p.Y)));
                }
            }

            if (points.Count < 2)
                continue;

            if (brush is not null && figure.IsClosed)
                gfx.DrawPolygon(pen, brush, points.ToArray(), XFillMode.Winding);
            else if (pen is not null)
                gfx.DrawLines(pen, points.ToArray());
        }
    }

    private static void DrawTextBlock(XGraphics gfx, FixedPage page, TextBlock block)
    {
        var text = block.Text ?? string.Empty;
        if (text.Length == 0)
            return;

        // Preserve RenderTransform for labels such as the vertically oriented
        // "KESİNTİLER VE MAHSUPLAR" cell in the official Hakediş Raporu.
        if (block.RenderTransform is RotateTransform rotation && Math.Abs(rotation.Angle % 360) > 0.001)
        {
            // Use the declared layout slot rather than ActualWidth/ActualHeight.
            // FixedPage text blocks may not have completed WPF measure/arrange before
            // vector export; ActualWidth/ActualHeight can therefore be zero or stale.
            // RenderTransform rotates around this slot's center, which is also the
            // intended center of the merged vertical-heading cell.
            var left = FixedPage.GetLeft(block);
            var top = FixedPage.GetTop(block);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;
            var width = block.Width;
            var height = block.Height;
            if (double.IsNaN(width) || width <= 0) width = block.ActualWidth;
            if (double.IsNaN(height) || height <= 0) height = block.ActualHeight;
            var center = new Point(left + width / 2d, top + height / 2d);
            var localRect = new XRect(
                ToPoint(center.X - width / 2d),
                ToPoint(center.Y - height / 2d),
                ToPoint(width),
                ToPoint(height));

            var state = gfx.Save();
            try
            {
                gfx.RotateAtTransform(rotation.Angle, new XPoint(ToPoint(center.X), ToPoint(center.Y)));
                DrawTextBlockContent(gfx, block, text, localRect);
            }
            finally
            {
                gfx.Restore(state);
            }
            return;
        }

        if (!TryTextBounds(page, block, out var rect))
            throw TextOverflow(text, rect, "geçerli bir metin alanı yok");
        DrawTextBlockContent(gfx, block, text, rect);
    }

    private static bool TryTextBounds(FixedPage page, TextBlock block, out XRect rect)
    {
        rect = default;
        if (VisualTreeHelper.GetParent(block) is not Visual)
            return false;

        // TextBlock.RenderSize can exceed the arranged slot for NoWrap or
        // overflowing content. Conversely, a centered table cell's natural
        // text height is smaller than the usable area. Use the actual slot,
        // which already excludes the border and its padding, in both cases.
        var slot = LayoutInformation.GetLayoutSlot(block);
        var margin = block.Margin;
        var slotWidth = Math.Max(0, slot.Width - margin.Left - margin.Right);
        var slotHeight = Math.Max(0, slot.Height - margin.Top - margin.Bottom);
        var width = Math.Min(slotWidth, block.MaxWidth);
        var height = Math.Min(slotHeight, block.MaxHeight);
        if (!double.IsNaN(block.Width)) width = Math.Min(width, block.Width);
        else if (block.HorizontalAlignment != HorizontalAlignment.Stretch)
            width = Math.Min(width, block.ActualWidth);
        if (!double.IsNaN(block.Height)) height = Math.Min(height, block.Height);
        if (width <= 0 || height <= 0)
            return false;

        var left = slot.Left + margin.Left + (block.HorizontalAlignment switch
        {
            HorizontalAlignment.Right => slotWidth - width,
            HorizontalAlignment.Center or HorizontalAlignment.Stretch => (slotWidth - width) / 2d,
            _ => 0
        });
        var top = slot.Top + margin.Top + (block.VerticalAlignment switch
        {
            VerticalAlignment.Bottom => slotHeight - height,
            VerticalAlignment.Center or VerticalAlignment.Stretch => (slotHeight - height) / 2d,
            _ => 0
        });
        // Convert the allocated parent-space slot to block-local coordinates,
        // then use the block's complete transform chain. Starting at its parent
        // would omit the block's own RenderTransform (for example a translation).
        var layoutOffset = VisualTreeHelper.GetOffset(block);
        var bounds = block.TransformToAncestor(page).TransformBounds(
            new Rect(left - layoutOffset.X, top - layoutOffset.Y, width, height));
        rect = new XRect(ToPoint(bounds.X), ToPoint(bounds.Y), ToPoint(bounds.Width), ToPoint(bounds.Height));
        return true;
    }

    internal static double RequiredTextHeight(TextBlock block,double width)
    {
        using var pdf=new PdfDocument();
        var page=pdf.AddPage();
        using var gfx=XGraphics.FromPdfPage(page);
        var size=Math.Max(1,ToPoint(block.FontSize));
        var font=CreateFont(NormalizeFontFamily(block.FontFamily?.Source),size,FontStyleFor(block));
        var lines=WrapText(gfx,block.Text,font,Math.Max(1,ToPoint(width)));
        return lines.Count*Math.Max(size*1.18,font.GetHeight())/DipToPoint;
    }

    private static void DrawTextBlockContent(XGraphics gfx, TextBlock block, string text, XRect rect)
    {
        if (TryBrush(block.Background, out var background))
            gfx.DrawRectangle(background, rect);

        if (!TryColor(block.Foreground, out var color))
            color = XColors.Black;

        var family = NormalizeFontFamily(block.FontFamily?.Source);
        var style = FontStyleFor(block);
        var fontSize = Math.Max(1.0, ToPoint(block.FontSize));
        var font = CreateFont(family, fontSize, style);
        var brush = new XSolidBrush(color);
        var lineHeight = block.LineHeight > 0 && !double.IsNaN(block.LineHeight)
            ? ToPoint(block.LineHeight)
            : Math.Max(fontSize * 1.18, font.GetHeight());

        var lines = block.TextWrapping == TextWrapping.NoWrap
            ? text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n')
                .Select(line => new WrappedLine(line, IsLastLineOfParagraph: true))
                .ToList()
            : WrapText(gfx, text, font, Math.Max(1, rect.Width));

        var totalTextHeight = lines.Count * lineHeight;
        if (totalTextHeight > rect.Height + 0.5)
            throw TextOverflow(text, rect, FormattableString.Invariant($"gereken yükseklik {totalTextHeight:0.##} pt"));
        if (lines.Any(line => gfx.MeasureString(line.Text, font).Width > rect.Width + 0.5))
            throw TextOverflow(text, rect, "satır genişliği yetersiz");

        var y = rect.Top;
        if (block.VerticalAlignment == VerticalAlignment.Center)
            y += Math.Max(0, (rect.Height - totalTextHeight) / 2d);
        else if (block.VerticalAlignment == VerticalAlignment.Bottom)
            y = Math.Max(rect.Top, rect.Bottom - totalTextHeight);

        foreach (var line in lines)
        {
            DrawTextLine(
                gfx,
                line.Text,
                font,
                brush,
                new XRect(rect.Left, y, rect.Width, lineHeight),
                block.TextAlignment,
                line.IsLastLineOfParagraph);
            y += lineHeight;
        }
    }

    private static PdfDocumentLayoutException TextOverflow(string text, XRect rect, string reason)
    {
        var excerpt = text.Replace('\r', ' ').Replace('\n', ' ');
        if (excerpt.Length > 100)
            excerpt = excerpt[..100] + "...";
        return new PdfDocumentLayoutException(FormattableString.Invariant(
            $"PDF belge düzeni: \"{excerpt}\" metni ayrılan alana sığmıyor ({rect.Width:0.##} × {rect.Height:0.##} pt, konum {rect.X:0.##}, {rect.Y:0.##}; {reason}). Alanı genişletin veya metni kısaltın."));
    }

    private static XFont CreateFont(string family, double size, XFontStyleEx style)
    {
        try
        {
            return new XFont(family, size, style);
        }
        catch
        {
            return new XFont("Arial", size, style);
        }
    }

    private static XFontStyleEx FontStyleFor(TextBlock block)
    {
        var bold = block.FontWeight.ToOpenTypeWeight() >= 600;
        var italic = block.FontStyle == FontStyles.Italic || block.FontStyle == FontStyles.Oblique;
        if (bold && italic) return XFontStyleEx.BoldItalic;
        if (bold) return XFontStyleEx.Bold;
        if (italic) return XFontStyleEx.Italic;
        return XFontStyleEx.Regular;
    }

    private static string NormalizeFontFamily(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "Arial";
        var first = source.Split(',')[0].Trim();
        return first.Length == 0 ? "Arial" : first;
    }

    private readonly record struct WrappedLine(string Text, bool IsLastLineOfParagraph);

    private static List<WrappedLine> WrapText(XGraphics gfx, string text, XFont font, double width)
    {
        var result = new List<WrappedLine>();
        foreach (var paragraph in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            if (paragraph.Length == 0)
            {
                result.Add(new WrappedLine(string.Empty, IsLastLineOfParagraph: true));
                continue;
            }

            var paragraphLines = new List<string>();
            var words = paragraph.Split(' ', StringSplitOptions.None);
            var line = string.Empty;
            foreach (var word in words)
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (gfx.MeasureString(candidate, font).Width <= width)
                {
                    line = candidate;
                    continue;
                }

                if (line.Length > 0)
                    paragraphLines.Add(line);
                line = string.Empty;
                // Break an overlong word at text-element boundaries, retaining
                // every character without splitting surrogate pairs or accents.
                var elements = StringInfo.GetTextElementEnumerator(word);
                while (elements.MoveNext())
                {
                    var element = elements.GetTextElement();
                    if (line.Length > 0 && gfx.MeasureString(line + element, font).Width > width)
                    {
                        paragraphLines.Add(line);
                        line = string.Empty;
                    }
                    line += element;
                }
            }
            paragraphLines.Add(line);

            for (var index = 0; index < paragraphLines.Count; index++)
            {
                result.Add(new WrappedLine(
                    paragraphLines[index],
                    IsLastLineOfParagraph: index == paragraphLines.Count - 1));
            }
        }
        return result;
    }

    private static void DrawTextLine(
        XGraphics gfx,
        string line,
        XFont font,
        XBrush brush,
        XRect rect,
        TextAlignment alignment,
        bool isLastLineOfParagraph)
    {
        // Justify all wrapped lines except the last line of each paragraph.
        // Stretching the final line across the page causes extreme word spacing
        // for short sentences such as the acceptance statement.
        if (alignment == TextAlignment.Justify && !isLastLineOfParagraph)
        {
            DrawJustifiedLine(gfx, line, font, brush, rect);
            return;
        }

        var effectiveAlignment = alignment == TextAlignment.Justify
            ? TextAlignment.Left
            : alignment;
        var format = new XStringFormat
        {
            Alignment = effectiveAlignment switch
            {
                TextAlignment.Center => XStringAlignment.Center,
                TextAlignment.Right => XStringAlignment.Far,
                _ => XStringAlignment.Near
            },
            LineAlignment = XLineAlignment.Near
        };
        gfx.DrawString(line, font, brush, rect, format);
    }

    private static void DrawJustifiedLine(XGraphics gfx, string line, XFont font, XBrush brush, XRect rect)
    {
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2)
        {
            gfx.DrawString(line, font, brush, rect, XStringFormats.TopLeft);
            return;
        }

        var wordsWidth = words.Sum(word => gfx.MeasureString(word, font).Width);
        var gap = (rect.Width - wordsWidth) / (words.Length - 1);
        if (gap <= 0 || gap > font.Size * 2.5)
        {
            gfx.DrawString(line, font, brush, rect, XStringFormats.TopLeft);
            return;
        }

        var x = rect.Left;
        foreach (var word in words)
        {
            var width = gfx.MeasureString(word, font).Width;
            gfx.DrawString(word, font, brush, new XRect(x, rect.Top, width + 1, rect.Height), XStringFormats.TopLeft);
            x += width + gap;
        }
    }

    private static void DrawImage(XGraphics gfx, FixedPage page, WpfImage image)
    {
        if (image.Source is not BitmapSource bitmap || !TryBounds(page, image, out var rect))
            return;

        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        stream.Position = 0;
        using var xImage = XImage.FromStream(stream);
        gfx.DrawImage(xImage, rect);
    }

    private static bool TryBounds(FixedPage page, FrameworkElement element, out XRect rect)
    {
        rect = default;
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return false;

        try
        {
            var transform = element.TransformToAncestor(page);
            var bounds = transform.TransformBounds(new Rect(new Point(0, 0), element.RenderSize));
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return false;
            rect = new XRect(ToPoint(bounds.Left), ToPoint(bounds.Top), ToPoint(bounds.Width), ToPoint(bounds.Height));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryBrush(WpfBrush? brush, out XBrush xBrush)
    {
        if (TryColor(brush, out var color))
        {
            xBrush = new XSolidBrush(color);
            return true;
        }
        xBrush = XBrushes.Transparent;
        return false;
    }

    private static bool TryColor(WpfBrush? brush, out XColor color)
    {
        if (brush is SolidColorBrush solid)
        {
            var c = solid.Color;
            var alpha = (byte)Math.Clamp((int)Math.Round(c.A * solid.Opacity), 0, 255);
            color = XColor.FromArgb(alpha, c.R, c.G, c.B);
            return true;
        }
        color = XColors.Transparent;
        return false;
    }

    private static double ToPoint(double dip) => dip * DipToPoint;
}
