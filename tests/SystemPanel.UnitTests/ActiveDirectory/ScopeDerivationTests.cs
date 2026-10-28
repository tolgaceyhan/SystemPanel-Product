using SystemPanel.Configuration;
using SystemPanel.Security.Scope;

namespace SystemPanel.UnitTests.ActiveDirectory;

public class ScopeDerivationTests
{
    private static readonly ScopeDerivationOptions Options = new()
    {
        AnchorOu = "IL",
        BranchOus = new() { "KULLANICILAR", "BILGISAYARLAR" },
    };

    [Theory]
    [InlineData("CN=Ali,OU=ANKARA,OU=KULLANICILAR,OU=IL,DC=corp,DC=example")] // il dalın altında
    [InlineData("CN=Ali,OU=KULLANICILAR,OU=ANKARA,OU=IL,DC=corp,DC=example")] // dal ilin altında
    [InlineData("CN=Ali,OU=ANKARA,OU=IL,DC=corp,DC=example")]
    public void Province_roots_are_real_ous_for_every_layout(string userDn)
    {
        var roots = ScopeDerivation.DeriveRoots(userDn, Options).Select(r => r.ToString()).ToList();

        Assert.Contains("OU=ANKARA,OU=IL,DC=corp,DC=example", roots);
        Assert.Contains("OU=ANKARA,OU=KULLANICILAR,OU=IL,DC=corp,DC=example", roots);
        Assert.Contains("OU=ANKARA,OU=BILGISAYARLAR,OU=IL,DC=corp,DC=example", roots);
        Assert.Equal("ANKARA", ScopeDerivation.ProvinceName(ScopeDerivation.DeriveRoots(userDn, Options)));
    }

    [Fact]
    public void Derived_scope_covers_users_and_computers_of_the_province_only()
    {
        var scope = OrganizationalScope.ForRoots(ScopeDerivation.DeriveRoots("CN=Ali,OU=ANKARA,OU=KULLANICILAR,OU=IL,DC=corp,DC=example", Options));

        Assert.True(scope.Contains("CN=Veli,OU=ANKARA,OU=KULLANICILAR,OU=IL,DC=corp,DC=example"));
        Assert.True(scope.Contains("CN=PC01,OU=ANKARA,OU=BILGISAYARLAR,OU=IL,DC=corp,DC=example"));
        Assert.False(scope.Contains("CN=Can,OU=IZMIR,OU=KULLANICILAR,OU=IL,DC=corp,DC=example"));
        Assert.False(scope.Contains("OU=KULLANICILAR,OU=IL,DC=corp,DC=example"));
    }

    [Theory]
    [InlineData("CN=Ali,OU=IL,DC=corp,DC=example")]                    // doğrudan anchor altında
    [InlineData("CN=Ali,OU=KULLANICILAR,OU=IL,DC=corp,DC=example")]    // yalnızca dal, il yok
    [InlineData("CN=Ali,OU=BT,DC=corp,DC=example")]                    // anchor yok
    [InlineData("CN=Ali,CN=Users,DC=corp,DC=example")]
    [InlineData("not-a-dn")]
    public void No_scope_when_province_cannot_be_determined(string userDn)
    {
        Assert.Empty(ScopeDerivation.DeriveRoots(userDn, Options));
    }

    [Fact]
    public void No_anchor_configured_means_no_scope()
    {
        Assert.Empty(ScopeDerivation.DeriveRoots("CN=Ali,OU=ANKARA,OU=IL,DC=corp", new ScopeDerivationOptions()));
    }
}
