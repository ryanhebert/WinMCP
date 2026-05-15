namespace WinMcp.Platform.Auth.Oidc;

/// <summary>
/// Collection of configured OIDC providers, keyed by <see cref="OidcProvider.Name"/>.
/// Loaded once at startup from <c>config.json</c>'s <c>oidcProviders</c>
/// section, hydrated via <see cref="OidcDiscovery"/>, and held as a
/// singleton for the lifetime of the process.
/// </summary>
public sealed class OidcProviderRegistry
{
    private readonly Dictionary<string, OidcProvider> _providers;

    public OidcProviderRegistry(IEnumerable<OidcProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);
    }

    public bool TryGet(string name, out OidcProvider provider) =>
        _providers.TryGetValue(name, out provider!);

    public OidcProvider Get(string name) =>
        _providers.TryGetValue(name, out var p)
            ? p
            : throw new InvalidOperationException(
                $"OIDC provider '{name}' is referenced by an auth config but not defined in oidcProviders.");

    public IReadOnlyCollection<OidcProvider> All => _providers.Values;

    /// <summary>
    /// Adds or replaces a provider entry. Used by the settings API when
    /// a new provider is added live so subsequent providerRef validation
    /// sees it before the next restart.
    /// </summary>
    public void Upsert(OidcProvider provider)
    {
        _providers[provider.Name] = provider;
    }

    /// <summary>
    /// Removes a provider entry. Symmetric counterpart to <see cref="Upsert"/>;
    /// called by the settings API after a provider is deleted from config.
    /// </summary>
    public bool Remove(string name) => _providers.Remove(name);
}
