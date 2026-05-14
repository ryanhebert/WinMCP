using System.Collections.Concurrent;

namespace WinMcp.Platform.Auth;

/// <summary>
/// In-memory store of OAuth2-issued access tokens for the demo auth mode.
/// Token state is intentionally non-persistent — clients are expected to
/// re-fetch on service restart. Carries forward the v1.0.21 design from
/// math-mcp: periodic background sweep for expired entries; bounded size
/// with soonest-to-expire eviction on overflow.
/// </summary>
public sealed class TokenStore : IDisposable
{
    private const int MaxTokens = 10_000;
    private const int EvictBatch = 100;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, DateTime> _tokens = new();
    private readonly Timer _sweepTimer;

    public TokenStore()
    {
        _sweepTimer = new Timer(_ => SweepExpired(), null, SweepInterval, SweepInterval);
    }

    public string Issue(TimeSpan ttl)
    {
        var token = CredentialGenerator.NewSecret(32, CredentialGenerator.IssuedTokenPrefix);
        _tokens[token] = DateTime.UtcNow.Add(ttl);
        if (_tokens.Count > MaxTokens) EvictOldest();
        return token;
    }

    /// <summary>
    /// Hot path: no sweep. The background <see cref="Timer"/> keeps the
    /// dictionary trimmed; a transient expired entry is still rejected here
    /// via the explicit expiry comparison.
    /// </summary>
    public bool IsValid(string token) =>
        _tokens.TryGetValue(token, out var expiry) && expiry > DateTime.UtcNow;

    public int Count => _tokens.Count;

    private void SweepExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _tokens)
        {
            if (kv.Value <= now) _tokens.TryRemove(kv.Key, out _);
        }
    }

    private void EvictOldest()
    {
        var overflow = _tokens.Count - MaxTokens;
        if (overflow <= 0) return;

        var victims = _tokens.ToArray()
            .OrderBy(kv => kv.Value)
            .Take(overflow + EvictBatch)
            .Select(kv => kv.Key);
        foreach (var key in victims) _tokens.TryRemove(key, out _);
    }

    public void Dispose() => _sweepTimer.Dispose();
}
