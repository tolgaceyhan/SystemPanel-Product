using System.Text.RegularExpressions;

namespace SystemPanel.UnitTests.Infrastructure;

/// <summary>
/// Ürün kapalı ağda çalışır: görünümler ve statik dosyalar internetten (CDN) script, stil veya yazı tipi yüklememeli.
/// </summary>
public sealed class OfflineAssetsTests
{
    private static readonly Regex ExternalAsset = new(
        "(src|href)\\s*=\\s*[\"']\\s*(https?:)?//",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CssExternalImport = new(
        "(@import|url\\()\\s*[\"']?\\s*(https?:)?//",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string WebRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SystemPanel.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "SystemPanel");
    }

    [Fact]
    public void Views_DoNotLoadExternalAssets()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(WebRoot(), "Views"), "*.cshtml", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => ExternalAsset.IsMatch(x.line) && !x.line.Contains("<a ", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();

        Assert.True(offenders.Count == 0, "Harici kaynak yükleyen görünümler:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void SiteStylesheets_DoNotImportExternalResources()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(WebRoot(), "wwwroot", "css"), "*.css", SearchOption.AllDirectories)
            .Where(f => CssExternalImport.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }
}
