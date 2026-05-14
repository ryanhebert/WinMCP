using Microsoft.Extensions.Logging;
using WinMcp.ModuleSdk;
using WinMcp.Platform.Auth.Oidc;

namespace WinMcp.Platform.Auth;

/// <summary>
/// Builds an <see cref="IAuthValidator"/> for a given <see cref="AuthDomainConfig"/>.
/// One factory instance per process; called at startup to construct the admin
/// validator and one validator per loaded module.
/// </summary>
/// <remarks>
/// Validator selection is keyed by <see cref="AuthDomainConfig.Mode"/>:
/// <list type="bullet">
///   <item><description><see cref="AuthMode.None"/> → <see cref="NoneValidator"/></description></item>
///   <item><description><see cref="AuthMode.Demo"/> → <see cref="DemoValidator"/> bound to the supplied demo credentials + token store</description></item>
///   <item><description><see cref="AuthMode.Oidc"/> → <see cref="OidcValidator"/> bound to the named provider; stubbed in v1.0, real in v1.1</description></item>
/// </list>
/// Misconfiguration (e.g., <c>Mode=Oidc</c> with no <c>ProviderRef</c>) throws
/// at construction time so startup fails loudly rather than 500-ing per request.
/// </remarks>
public sealed class AuthValidatorFactory
{
    private readonly OidcProviderRegistry _providers;
    private readonly TokenStore _tokenStore;
    private readonly ILoggerFactory _loggerFactory;

    public AuthValidatorFactory(
        OidcProviderRegistry providers,
        TokenStore tokenStore,
        ILoggerFactory loggerFactory)
    {
        _providers = providers;
        _tokenStore = tokenStore;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Build the validator for an auth domain. Demo-mode credentials are
    /// drawn from <paramref name="config"/>.<see cref="AuthDomainConfig.DemoCredentials"/>;
    /// if null and mode is Demo, construction fails.
    /// </summary>
    public IAuthValidator Build(AuthDomainConfig config)
    {
        return config.Mode switch
        {
            AuthMode.None => new NoneValidator(),

            AuthMode.Demo => config.DemoCredentials is not null
                ? new DemoValidator(
                    config.DemoCredentials.BearerToken,
                    _tokenStore,
                    _loggerFactory.CreateLogger<DemoValidator>())
                : throw new InvalidOperationException(
                    "Auth domain configured Mode=Demo but DemoCredentials is null. " +
                    "Re-run with --auth to generate credentials, or set Mode=None."),

            AuthMode.Oidc => string.IsNullOrEmpty(config.ProviderRef)
                ? throw new InvalidOperationException(
                    "Auth domain configured Mode=Oidc but ProviderRef is null. " +
                    "Set ProviderRef to the name of an entry in oidcProviders.")
                : new OidcValidator(
                    _providers.Get(config.ProviderRef),
                    _loggerFactory.CreateLogger<OidcValidator>()),

            _ => throw new InvalidOperationException($"Unknown auth mode: {config.Mode}")
        };
    }
}
