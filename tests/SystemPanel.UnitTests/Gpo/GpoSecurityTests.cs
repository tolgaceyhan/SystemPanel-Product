using System.IO.Compression;
using System.Management.Automation.Language;
using SystemPanel.Services;
using SystemPanel.Services.Gpo;

namespace SystemPanel.UnitTests.Gpo;

public class GpoSecurityTests
{
    [Theory]
    [InlineData("$(Remove-Item C:\\ -Recurse)")]
    [InlineData("x\"; Remove-GPO -All; \"")]
    [InlineData("a`b")]
    [InlineData("name with spaces")]
    public void Values_are_passed_as_separate_literal_arguments(string value)
    {
        // Değerlerde " olanlar reddedilir; diğerleri tek bir argüman olarak kalır (komut dizesine gömülmez).
        if (value.Contains('"'))
        {
            Assert.Throws<ArgumentException>(() => PowerShellScriptRunner.BuildArguments("s.ps1", new Dictionary<string, string> { ["Name"] = value }));
            return;
        }

        var args = PowerShellScriptRunner.BuildArguments("C:\\app\\scripts\\LinkGpo.ps1", new Dictionary<string, string> { ["Name"] = value });

        Assert.Equal("-File", args[5]);
        Assert.Equal("C:\\app\\scripts\\LinkGpo.ps1", args[6]);
        Assert.Equal("-Name", args[7]);
        Assert.Equal(value, args[8]);
        Assert.DoesNotContain("-Command", args);
    }

    [Theory]
    [InlineData("-Confirm:$false")]
    [InlineData("line\nbreak")]
    [InlineData("nul\0")]
    [InlineData("")]
    public void Dangerous_values_are_rejected(string value)
    {
        Assert.Throws<ArgumentException>(() => PowerShellScriptRunner.ValidateValue("Name", value));
    }

    [Theory]
    [InlineData("Name; rm")]
    [InlineData("-Name")]
    [InlineData("")]
    public void Parameter_names_are_allowlisted(string name)
    {
        Assert.Throws<ArgumentException>(() => PowerShellScriptRunner.BuildArguments("s.ps1", new Dictionary<string, string> { [name] = "v" }));
    }

    [Theory]
    [InlineData("31B2F340-016D-11D2-945F-00C04FB984F9", "31b2f340-016d-11d2-945f-00c04fb984f9")]
    [InlineData("{31B2F340-016D-11D2-945F-00C04FB984F9}", "31b2f340-016d-11d2-945f-00c04fb984f9")]
    public void Guids_are_normalized(string input, string expected)
    {
        Assert.True(GpoInput.TryNormalizeGuid(input, out var guid));
        Assert.Equal(expected, guid);
    }

    [Theory]
    [InlineData("$(whoami)")]
    [InlineData("Default Domain Policy")]
    [InlineData(null)]
    public void Non_guid_ids_are_rejected(string? input)
    {
        Assert.False(GpoInput.TryNormalizeGuid(input, out _));
    }

    [Theory]
    [InlineData("OU=ANKARA,DC=corp,DC=example", true)]
    [InlineData("DC=corp,DC=example", true)]
    [InlineData("CN=Users,DC=corp,DC=example", false)]               // GPO bir CN'e bağlanamaz
    [InlineData("OU=X,DC=other,DC=example", false)]                  // domain dışı
    [InlineData("OU=\"quoted\",DC=corp,DC=example", false)]
    [InlineData("OU=$(evil),DC=corp,DC=example", true)]              // DN değeri; -File ile literal geçer
    [InlineData("not a dn", false)]
    public void Ou_dn_must_be_an_ou_inside_the_domain(string input, bool expected)
    {
        Assert.Equal(expected, GpoInput.TryNormalizeOuDn(input, "DC=corp,DC=example", out _));
    }

    [Theory]
    [InlineData("Default Domain Policy", true)]
    [InlineData("USB Engelleme (Ankara)", true)]
    [InlineData("-Remove", false)]
    [InlineData("a\"b", false)]
    [InlineData("", false)]
    public void Display_names_are_allowlisted(string name, bool expected)
    {
        Assert.Equal(expected, GpoInput.IsValidDisplayName(name));
    }

    public static IEnumerable<object[]> Scripts()
    {
        var dir = FindScriptsDirectory();
        return Directory.GetFiles(dir, "*.ps1").Select(f => new object[] { Path.GetFileName(f) });
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void Every_gpo_script_parses_without_errors(string fileName)
    {
        var path = Path.Combine(FindScriptsDirectory(), fileName);
        Parser.ParseFile(path, out _, out var errors);
        Assert.True(errors.Length == 0, string.Join("; ", errors.Select(e => $"{e.Extent.StartLineNumber}: {e.Message}")));
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void Gpo_scripts_do_not_use_invoke_expression(string fileName)
    {
        var text = File.ReadAllText(Path.Combine(FindScriptsDirectory(), fileName));
        Assert.DoesNotContain("Invoke-Expression", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" iex ", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Zip_slip_entries_are_rejected()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = zip.CreateEntry("../../evil.ps1");
            using var w = new StreamWriter(e.Open());
            w.Write("x");
        }
        ms.Position = 0;

        var dest = Path.Combine(Path.GetTempPath(), "sp-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dest);
        try
        {
            using var read = new ZipArchive(ms, ZipArchiveMode.Read);
            Assert.Throws<InvalidDataException>(() => GpoService.SafeExtract(read, dest));
        }
        finally
        {
            Directory.Delete(dest, true);
        }
    }

    [Fact]
    public void Script_error_json_is_surfaced()
    {
        var msg = GpoService.ExtractError(new PowerShellScriptResult(1, "{\"success\":false,\"error\":\"GPO bulunamadı\"}", "", false));
        Assert.Equal("GPO bulunamadı", msg);
    }

    private static string FindScriptsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SystemPanel.sln")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "SystemPanel", "scripts");
    }
}
