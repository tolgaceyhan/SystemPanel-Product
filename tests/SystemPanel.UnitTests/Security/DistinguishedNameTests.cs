using SystemPanel.Security.Scope;

namespace SystemPanel.UnitTests.Security;

public class DistinguishedNameTests
{
    private const string Province = "OU=ANKARA,OU=IL,DC=corp,DC=example";

    [Theory]
    [InlineData("CN=Ali Veli,OU=KULLANICILAR,OU=ANKARA,OU=IL,DC=corp,DC=example")]
    [InlineData("OU=ANKARA,OU=IL,DC=corp,DC=example")]
    [InlineData("cn=ali,ou=ankara,ou=il,dc=CORP,dc=example")]
    [InlineData(" CN = Ali , OU=ANKARA , OU=IL,DC=corp,DC=example ")]
    public void Object_inside_province_is_within(string dn)
    {
        Assert.True(DistinguishedName.Parse(dn).IsWithin(DistinguishedName.Parse(Province)));
    }

    [Theory]
    [InlineData("CN=Ali,OU=IZMIR,OU=IL,DC=corp,DC=example")]
    [InlineData("OU=IL,DC=corp,DC=example")]
    [InlineData("CN=Domain Admins,CN=Users,DC=corp,DC=example")]
    [InlineData("OU=XANKARA,OU=IL,DC=corp,DC=example")]
    [InlineData("CN=Ali,OU=ANKARA,OU=IL,DC=other,DC=example")]
    public void Object_outside_province_is_not_within(string dn)
    {
        Assert.False(DistinguishedName.Parse(dn).IsWithin(DistinguishedName.Parse(Province)));
    }

    [Fact]
    public void Escaped_comma_cannot_spoof_a_parent_ou()
    {
        // Eski kod (EndsWith(",OU=ANKARA,...")) bu nesneyi ANKARA altında sanıyordu.
        // Gerçekte RDN "CN=evil,OU=ANKARA" tek bir değerdir ve nesne doğrudan OU=IL altındadır.
        var spoof = DistinguishedName.Parse(@"CN=evil\,OU=ANKARA,OU=IL,DC=corp,DC=example");

        Assert.False(spoof.IsWithin(DistinguishedName.Parse(Province)));
        Assert.Equal(4, spoof.Depth);
        Assert.Equal("evil,OU=ANKARA", spoof.Rdns[0].Value);
    }

    [Fact]
    public void Hex_escaped_comma_cannot_spoof_a_parent_ou()
    {
        var spoof = DistinguishedName.Parse(@"CN=evil\2COU=ANKARA,OU=IL,DC=corp,DC=example");

        Assert.False(spoof.IsWithin(DistinguishedName.Parse(Province)));
        Assert.Equal("evil,OU=ANKARA", spoof.Rdns[0].Value);
    }

    [Fact]
    public void Hex_escapes_decode_utf8()
    {
        // "Ş" = C5 9E
        var dn = DistinguishedName.Parse(@"OU=\C5\9Eube,DC=corp");
        Assert.Equal("Şube", dn.Rdns[0].Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a dn")]
    [InlineData("=ANKARA,DC=corp")]
    [InlineData("OU=,DC=corp")]
    [InlineData("OU=ANKARA,,DC=corp")]
    [InlineData(@"OU=ANKARA\")]
    [InlineData("O U=ANKARA")]
    public void Invalid_input_is_rejected(string? value)
    {
        Assert.False(DistinguishedName.TryParse(value, out _));
    }

    [Fact]
    public void Equality_is_case_insensitive_and_whitespace_tolerant()
    {
        var a = DistinguishedName.Parse("OU=Ankara,OU=IL,DC=corp,DC=example");
        var b = DistinguishedName.Parse("ou=ANKARA , ou=il,dc=CORP,dc=EXAMPLE");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void IsDescendantOf_excludes_self()
    {
        var p = DistinguishedName.Parse(Province);
        Assert.False(p.IsDescendantOf(p));
        Assert.True(DistinguishedName.Parse("CN=x," + Province).IsDescendantOf(p));
    }

    [Fact]
    public void ToString_round_trips_escaped_values()
    {
        var dn = DistinguishedName.Parse(@"CN=Yılmaz\, Ali,OU=ANKARA,DC=corp");
        var again = DistinguishedName.Parse(dn.ToString());

        Assert.Equal(dn, again);
        Assert.Equal("Yılmaz, Ali", again.Rdns[0].Value);
    }

    [Fact]
    public void Parent_walks_up_one_level()
    {
        var dn = DistinguishedName.Parse("CN=x,OU=ANKARA,DC=corp");
        Assert.Equal(DistinguishedName.Parse("OU=ANKARA,DC=corp"), dn.Parent);
        Assert.Null(DistinguishedName.Parse("DC=corp").Parent);
    }
}
