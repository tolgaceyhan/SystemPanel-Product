using SystemPanel.Services;

namespace SystemPanel.UnitTests.Gpo;

public class MiscValidationTests
{
    [Theory]
    [InlineData("http://grafana.local/d/abc", true)]
    [InlineData("https://systempanel/ServerMonitoring", true)]
    [InlineData("file:///C:/Windows/System32", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/relative/path", false)]
    [InlineData("", false)]
    public void Dashboard_panel_urls_must_be_http_or_https(string url, bool expected)
    {
        Assert.Equal(expected, DashboardPanelRules.IsAllowedUrl(url));
    }

    [Theory]
    [InlineData("A", true)]
    [InlineData("NOC-01", true)]
    [InlineData("noc 01", false)]
    [InlineData("<script>", false)]
    public void Screen_codes_are_allowlisted(string code, bool expected)
    {
        Assert.Equal(expected, DashboardPanelRules.IsValidScreenCode(code));
    }

    [Theory]
    [InlineData("CcmExec", "CcmExec")]
    [InlineData("x' OR Name LIKE '%", "x\\' OR Name LIKE \\'%")]
    [InlineData("a\\b", "a\\\\b")]
    public void Wql_string_literals_are_escaped(string input, string expected)
    {
        Assert.Equal(expected, SccmLogService.EscapeWql(input));
    }
}
