using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SystemPanel.Auditing;

namespace SystemPanel.UnitTests.Auditing;

public class AuditRedactorTests
{
    [Fact]
    public void Sensitive_fields_are_masked_recursively()
    {
        var json = AuditRedactor.ToRedactedJson(new
        {
            UserDn = "CN=a,OU=b",
            NewPassword = "S3cret!",
            Nested = new { LapsPassword = "x", ApiKey = "k", Fine = "ok" },
            Items = new object[] { new { Token = "t" }, new { Name = "n" } },
            AgentKey = "spa_abc",
        })!;

        Assert.DoesNotContain("S3cret!", json);
        Assert.DoesNotContain("\"x\"", json);
        Assert.DoesNotContain("spa_abc", json);
        Assert.DoesNotContain("\"t\"", json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("CN=a,OU=b", root.GetProperty("UserDn").GetString());
        Assert.Equal(AuditRedactor.Mask, root.GetProperty("NewPassword").GetString());
        Assert.Equal("ok", root.GetProperty("Nested").GetProperty("Fine").GetString());
        Assert.Equal("n", root.GetProperty("Items")[1].GetProperty("Name").GetString());
    }

    [Theory]
    [InlineData("password")]
    [InlineData("newPassword")]
    [InlineData("BindPassword")]
    [InlineData("pwd")]
    [InlineData("ms-Mcs-AdmPwd")]
    [InlineData("clientSecret")]
    [InlineData("accessToken")]
    [InlineData("AgentKey")]
    [InlineData("lapsPassword")]
    [InlineData("Authorization")]
    [InlineData("Cookie")]
    public void Known_sensitive_keys_are_detected(string key)
    {
        Assert.True(AuditRedactor.IsSensitiveKey(key));
    }

    [Theory]
    [InlineData("UserDn")]
    [InlineData("DisplayName")]
    [InlineData("Department")]
    [InlineData("TargetOu")]
    public void Ordinary_keys_are_kept(string key)
    {
        Assert.False(AuditRedactor.IsSensitiveKey(key));
    }

    [Fact]
    public void Null_value_produces_null()
    {
        Assert.Null(AuditRedactor.ToRedactedJson(null));
    }

    [Fact]
    public void Very_large_payloads_are_truncated()
    {
        var json = AuditRedactor.ToRedactedJson(new { Text = new string('a', 50_000) })!;
        Assert.True(json.Length <= AuditRedactor.MaxJsonLength + 20);
    }

    [Fact]
    public void Build_takes_actor_from_http_context_and_never_stores_secrets()
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "CORP\\ali"),
            new Claim(ClaimTypes.NameIdentifier, "id-1"),
        }, "test"));
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("10.0.0.5");

        var e = AuditService.Build(new AuditRecord(AuditActions.AdUserPasswordReset)
        {
            ResourceType = "AdUser",
            ResourceId = "CN=x,OU=y",
            Metadata = new { NewPassword = "Hunter2!", Unlock = true },
        }, http);

        Assert.Equal("CORP\\ali", e.ActorUserName);
        Assert.Equal("id-1", e.ActorUserId);
        Assert.Equal("User", e.ActorType);
        Assert.Equal("10.0.0.5", e.SourceIp);
        Assert.DoesNotContain("Hunter2!", e.MetadataJson);
        Assert.Contains("\"Unlock\":true", e.MetadataJson);
    }

    [Fact]
    public void Build_marks_anonymous_and_system_actors()
    {
        var anon = AuditService.Build(new AuditRecord(AuditActions.LoginFailed), new DefaultHttpContext());
        Assert.Equal("Anonymous", anon.ActorType);

        var system = AuditService.Build(new AuditRecord("retention.run"), null);
        Assert.Equal("System", system.ActorType);
    }
}
