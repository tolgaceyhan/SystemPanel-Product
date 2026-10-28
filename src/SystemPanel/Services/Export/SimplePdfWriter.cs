using System;
using System.Collections.Generic;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace SystemPanel.Services.Export
{
    /// <summary>
    /// Satır listesini sayfalara bölerek A4 PDF üretir (PDFsharp, MIT). Türkçe karakterler için
    /// Unicode kodlama kullanılır; yazı tipi <see cref="SystemFontResolver"/> ile sistemden okunur.
    /// </summary>
    public static class SimplePdfWriter
    {
        private const double Margin = 40;
        private const double TitleSize = 14;
        private const double LineSize = 9;

        private static readonly object FontInit = new();

        private static void EnsureFontResolver()
        {
            lock (FontInit)
            {
                GlobalFontSettings.FontResolver ??= new SystemFontResolver();
            }
        }

        public static byte[] Write(string title, IEnumerable<string> lines, string fontFamily = "Arial")
        {
            EnsureFontResolver();
            using var document = new PdfDocument();
            document.Info.Title = title;

            var titleFont = new XFont(fontFamily, TitleSize, XFontStyleEx.Bold);
            var lineFont = new XFont(fontFamily, LineSize, XFontStyleEx.Regular);

            PdfPage page = null!;
            XGraphics gfx = null!;
            double y = 0, lineHeight = 0, maxWidth = 0;

            void NewPage()
            {
                gfx?.Dispose();
                page = document.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                gfx = XGraphics.FromPdfPage(page);
                lineHeight = lineFont.GetHeight() * 1.2;
                maxWidth = page.Width.Point - 2 * Margin;
                y = Margin;
            }

            NewPage();
            gfx.DrawString(title, titleFont, XBrushes.Black, new XPoint(Margin, y + TitleSize));
            y += TitleSize * 2;

            foreach (var line in lines)
            {
                foreach (var chunk in Wrap(gfx, line ?? string.Empty, lineFont, maxWidth))
                {
                    if (y + lineHeight > page.Height.Point - Margin)
                        NewPage();
                    gfx.DrawString(chunk, lineFont, XBrushes.Black, new XPoint(Margin, y + LineSize));
                    y += lineHeight;
                }
            }

            gfx.Dispose();
            using var stream = new MemoryStream();
            document.Save(stream, false);
            return stream.ToArray();
        }

        private static IEnumerable<string> Wrap(XGraphics gfx, string text, XFont font, double maxWidth)
        {
            if (text.Length == 0 || gfx.MeasureString(text, font).Width <= maxWidth)
            {
                yield return text;
                yield break;
            }

            var start = 0;
            while (start < text.Length)
            {
                var length = text.Length - start;
                while (length > 1 && gfx.MeasureString(text.Substring(start, length), font).Width > maxWidth)
                    length = Math.Max(1, length * 9 / 10);
                yield return text.Substring(start, length);
                start += length;
            }
        }
    }
}
