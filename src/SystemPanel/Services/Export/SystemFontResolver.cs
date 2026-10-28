using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using PdfSharp.Fonts;

namespace SystemPanel.Services.Export
{
    /// <summary>
    /// PDFsharp için işletim sisteminin yazı tiplerini kullanır (yazı tipi dosyası pakete gömülmez,
    /// böylece yazı tipi lisansı sorunu doğmaz). Windows: Arial; Linux (test/CI): DejaVu Sans.
    /// </summary>
    public sealed class SystemFontResolver : IFontResolver
    {
        private const string Regular = "sp-regular";
        private const string Bold = "sp-bold";

        private static readonly ConcurrentDictionary<string, byte[]> Cache = new();

        private static readonly string[] RegularCandidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        };

        private static readonly string[] BoldCandidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialbd.ttf"),
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        };

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
            => new(isBold ? Bold : Regular);

        public byte[] GetFont(string faceName)
            => Cache.GetOrAdd(faceName, name =>
            {
                var candidates = name == Bold ? BoldCandidates.Concat(RegularCandidates) : RegularCandidates;
                var path = candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                    ?? throw new InvalidOperationException("PDF için uygun bir sistem yazı tipi bulunamadı.");
                return File.ReadAllBytes(path);
            });
    }
}
