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

public class SettingsValidator_AuthDomain
{
    private static readonly HashSet<string> NoProviders = new();
    private static readonly HashSet<string> WithOkta = new() { "okta" };

    [Fact]
    public void Mode_None_OK() =>
        Assert.True(SettingsValidator.ValidateAuthDomainShape(
            "none", providerRef: null, requiredScopes: Array.Empty<string>(),
            requiredClaims: new Dictionary<string, IReadOnlyList<string>>(),
            knownProviders: NoProviders).Ok);

    [Fact]
    public void Mode_Demo_OK() =>
        Assert.True(SettingsValidator.ValidateAuthDomainShape(
            "demo", null, Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>>(),
            NoProviders).Ok);

    [Fact]
    public void Mode_Oidc_RequiresProviderRef()
    {
        var r = SettingsValidator.ValidateAuthDomainShape(
            "oidc", providerRef: null, Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>>(), WithOkta);
        Assert.False(r.Ok);
        Assert.Equal("providerRef", r.Field);
    }

    [Fact]
    public void Mode_Oidc_ProviderRefMustExist()
    {
        var r = SettingsValidator.ValidateAuthDomainShape(
            "oidc", providerRef: "ghost", Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>>(), WithOkta);
        Assert.False(r.Ok);
        Assert.Equal("providerRef", r.Field);
        Assert.Contains("not configured", r.Message ?? "");
    }

    [Fact]
    public void Mode_Oidc_HappyPath() =>
        Assert.True(SettingsValidator.ValidateAuthDomainShape(
            "oidc", "okta",
            new[] { "openid", "email" },
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["groups"] = new[] { "admins" },
            },
            WithOkta).Ok);

    [Theory]
    [InlineData("garbage")]
    [InlineData("OIDC")]
    [InlineData("")]
    public void Mode_Invalid(string mode)
    {
        var r = SettingsValidator.ValidateAuthDomainShape(
            mode, null, Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>>(),
            NoProviders);
        Assert.False(r.Ok);
        Assert.Equal("mode", r.Field);
    }

    [Fact]
    public void RequiredScopes_EmptyStringRejected()
    {
        var r = SettingsValidator.ValidateAuthDomainShape(
            "oidc", "okta", new[] { "openid", "" },
            new Dictionary<string, IReadOnlyList<string>>(),
            WithOkta);
        Assert.False(r.Ok);
        Assert.Equal("requiredScopes", r.Field);
    }

    [Fact]
    public void RequiredClaims_EmptyKeyRejected()
    {
        var r = SettingsValidator.ValidateAuthDomainShape(
            "oidc", "okta", Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>> { [""] = new[] { "admins" } },
            WithOkta);
        Assert.False(r.Ok);
        Assert.Equal("requiredClaims", r.Field);
    }

    [Fact]
    public void RequiredClaims_EmptyValueArrayRejected()
    {
        var r = SettingsValidator.ValidateAuthDomainShape(
            "oidc", "okta", Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>> { ["groups"] = Array.Empty<string>() },
            WithOkta);
        Assert.False(r.Ok);
        Assert.Equal("requiredClaims", r.Field);
    }

    [Fact]
    public void RequiredScopes_WhitespaceOnlyRejected()
    {
        var r = SettingsValidator.ValidateAuthDomainShape(
            "oidc", "okta", new[] { "openid", "   " },
            new Dictionary<string, IReadOnlyList<string>>(),
            WithOkta);
        Assert.False(r.Ok);
        Assert.Equal("requiredScopes", r.Field);
    }

    [Fact]
    public void RequiredClaims_WhitespaceValueRejected()
    {
        var r = SettingsValidator.ValidateAuthDomainShape(
            "oidc", "okta", Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>> { ["groups"] = new[] { "   " } },
            WithOkta);
        Assert.False(r.Ok);
        Assert.Equal("requiredClaims", r.Field);
    }
}
