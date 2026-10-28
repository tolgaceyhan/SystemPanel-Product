using System.Text;
using SystemPanel.Services.Export;

namespace SystemPanel.UnitTests.Export;

public sealed class SimplePdfWriterTests
{
    [Fact]
    public void Write_ProducesMultiPagePdf_WithTurkishText()
    {
        var lines = Enumerable.Range(1, 200).Select(i => $"[{i}] Info - Kullanıcı şifresi sıfırlandı: ĞÜŞİÖÇ ğüşıöç " + new string('x', i % 50));

        var bytes = SimplePdfWriter.Write("Log Listesi", lines);

        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Contains("/Count ", Encoding.ASCII.GetString(bytes));
    }
}
