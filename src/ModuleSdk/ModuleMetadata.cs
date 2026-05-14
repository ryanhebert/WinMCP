namespace WinMcp.ModuleSdk;

/// <summary>
/// Identity surfaced by an <see cref="IMcpModule"/> implementation to the
/// platform at load time. Must match the corresponding fields in this
/// module's <c>module.json</c> manifest exactly; mismatch fails the load.
/// </summary>
public sealed record ModuleMetadata
{
    /// <summary>
    /// Module identifier. Lowercase, alphanumeric + hyphens; matches the regex
    /// <c>^[a-z][a-z0-9-]{0,62}$</c>. Must match the module's directory name
    /// in <c>&lt;InstallDir&gt;\modules\</c> and the <c>name</c> field of its
    /// <c>module.json</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// SemVer version string (e.g., <c>"1.0.0"</c> or <c>"2.3.1-rc1"</c>).
    /// Must match the <c>version</c> field of the module's manifest.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Human-readable name shown on the platform dashboard (e.g., <c>"Math"</c>).
    /// Free-form; should be short.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// One-paragraph description of what the module exposes. Shown on the
    /// dashboard and in <c>/info</c>. Optional — null or empty is allowed.
    /// </summary>
    public string? Description { get; init; }
}
