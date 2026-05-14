using System.Reflection;
using System.Runtime.Loader;

namespace WinMcp.Platform.Modules;

/// <summary>
/// Per-module <see cref="AssemblyLoadContext"/> that isolates a module's
/// transitive dependencies from the platform's own and from other modules.
/// </summary>
/// <remarks>
/// <para>
/// Two motivations:
/// <list type="number">
///   <item><description>
///     Modules may ship different transitive versions of common packages
///     (e.g., a different <c>System.Text.Json</c>). Loading each into its
///     own ALC prevents version conflicts.
///   </description></item>
///   <item><description>
///     Future hot-unload of a module on upgrade becomes feasible — unload
///     the ALC, replace the DLL, reload. Not implemented in v1.0 but the
///     groundwork is here.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// Types from the platform's own assemblies (notably <c>WinMcp.ModuleSdk</c>
/// and <c>ModelContextProtocol</c>) are deliberately resolved from the
/// platform's default context — that's what lets a module call
/// <see cref="WinMcp.ModuleSdk.IMcpModule"/> defined in the platform process.
/// </para>
/// </remarks>
public sealed class ModuleAssemblyContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _moduleName;

    public ModuleAssemblyContext(string moduleName, string assemblyPath)
        : base(name: $"WinMcpModule:{moduleName}", isCollectible: true)
    {
        _moduleName = moduleName;
        _resolver = new AssemblyDependencyResolver(assemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Shared types must come from the default (platform) context so
        // the module's IMcpModule impl is the same Type as what the
        // platform's ModuleLoader looks for. Without this, the cast at
        // load time would throw with "type isn't IMcpModule" even though
        // both sides reference the same package.
        if (IsPlatformSharedAssembly(assemblyName.Name))
        {
            return null; // defer to default ALC
        }

        var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
        return resolved is null ? null : LoadFromAssemblyPath(resolved);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }

    /// <summary>
    /// Assemblies a module should always resolve from the default platform
    /// context (i.e., the same instance the platform itself loaded). Keeps
    /// SDK contract types — IMcpModule, ModuleMetadata, etc. — singletons
    /// across module boundaries.
    /// </summary>
    private static bool IsPlatformSharedAssembly(string? name) => name is
        "WinMcp.ModuleSdk"
        or "ModelContextProtocol"
        or "ModelContextProtocol.Core"
        or "ModelContextProtocol.AspNetCore"
        or "Microsoft.Extensions.DependencyInjection.Abstractions"
        or "Microsoft.Extensions.Configuration.Abstractions"
        or "Microsoft.Extensions.Logging.Abstractions"
        or "Microsoft.Extensions.Hosting.Abstractions";

    public override string ToString() => $"ModuleAssemblyContext({_moduleName})";
}
