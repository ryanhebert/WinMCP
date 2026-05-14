using WinMcp.ModuleSdk;

namespace WinMcp.Platform.Modules;

/// <summary>
/// Successfully loaded module: parsed manifest, loaded assembly context,
/// instantiated <see cref="IMcpModule"/>. Held by the platform for the
/// process lifetime; consumed by the service host when registering routes
/// and by the dashboard when rendering module cards.
/// </summary>
public sealed class LoadedModule
{
    public required ModuleManifest Manifest { get; init; }

    /// <summary>The module's per-instance <see cref="AssemblyLoadContext"/>.</summary>
    public required ModuleAssemblyContext Context { get; init; }

    /// <summary>The instantiated <see cref="IMcpModule"/>.</summary>
    public required IMcpModule Instance { get; init; }

    /// <summary>Absolute path of the module directory on disk.</summary>
    public required string InstallPath { get; init; }

    /// <summary>Convenience accessor; equivalent to <see cref="ModuleManifest.Name"/>.</summary>
    public string Name => Manifest.Name;
}
