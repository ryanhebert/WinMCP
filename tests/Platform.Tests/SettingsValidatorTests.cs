using Xunit;
using WinMcp.Platform.Config;

namespace WinMcp.Platform.Tests;

public class SettingsValidator_ProviderShape
{
    [Theory]
    [InlineData("okta")]                                    // valid
    [InlineData("auth0-staging")]                           // valid with hyphen
    [InlineData("a")]                                       // min length
    public void Name_Valid(string name) =>
        Assert.True(SettingsValidator.ValidateProviderShape(name, "https://issuer.example.com", "winmcp").Ok);

    [Theory]
    [InlineData("")]                                        // empty
    [InlineData("Okta")]                                    // uppercase
    [InlineData("okta_idp")]                                // underscore
    [InlineData("1okta")]                                   // leading digit
    [InlineData("-okta")]                                   // leading hyphen
    [InlineData("okta!")]                                   // special char
    public void Name_Invalid(string name)
    {
        var r = SettingsValidator.ValidateProviderShape(name, "https://issuer.example.com", "winmcp");
        Assert.False(r.Ok);
        Assert.Equal("name", r.Field);
    }

    [Theory]
    [InlineData("https://issuer.example.com")]
    [InlineData("https://issuer.example.com/realms/main")]
    [InlineData("http://localhost:8080")]                   // http allowed for dev
    public void Issuer_Valid(string issuer) =>
        Assert.True(SettingsValidator.ValidateProviderShape("okta", issuer, "winmcp").Ok);

    [Theory]
    [InlineData("")]
    [InlineData("issuer.example.com")]                      // no scheme
    [InlineData("ftp://issuer.example.com")]                // wrong scheme
    [InlineData("https://issuer.example.com?q=1")]          // query string
    [InlineData("https://issuer.example.com/../etc/passwd")] // traversal
    [InlineData("https://issuer.example.com#fragment")]      // fragment
    public void Issuer_Invalid(string issuer)
    {
        var r = SettingsValidator.ValidateProviderShape("okta", issuer, "winmcp");
        Assert.False(r.Ok);
        Assert.Equal("issuer", r.Field);
    }

    [Theory]
    [InlineData("")]
    [InlineData("two tokens")]
    [InlineData("has\ttab")]
    [InlineData("has\nnewline")]
    public void Audience_Invalid(string audience)
    {
        var r = SettingsValidator.ValidateProviderShape("okta", "https://issuer.example.com", audience);
        Assert.False(r.Ok);
        Assert.Equal("audience", r.Field);
    }

    [Fact]
    public void Audience_DefaultsAllowed() =>
        Assert.True(SettingsValidator.ValidateProviderShape("okta", "https://issuer.example.com", "winmcp").Ok);
}
