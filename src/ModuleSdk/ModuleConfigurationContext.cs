using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace WinMcp.ModuleSdk;

/// <summary>
/// Per-module context passed to <see cref="IMcpModule.Configure"/> at platform
/// startup. Bundles everything a module needs to register its capabilities
/// and (optionally) its supporting services.
/// </summary>
/// <remarks>
/// <para>
/// The context is constructed by the platform's module loader. Modules should
/// treat it as a one-shot configuration vehicle: read what's needed, register
/// services and tools, then return. Holding a reference to it past the
/// <c>Configure</c> call is not supported.
/// </para>
/// <para>
/// New properties may be added in minor SDK versions; modules should rely on
/// the property-access pattern (not constructor signatures) for forward
/// compatibility.
/// </para>
/// </remarks>
public sealed class ModuleConfigurationContext
{
    /// <summary>
    /// The MCP server builder for this module's <c>/mcp</c> endpoint. Use the
    /// standard ModelContextProtocol SDK extensions to register tools, prompts,
    /// and resources (typically <c>WithToolsFromAssembly</c>,
    /// <c>WithPromptsFromAssembly</c>, <c>WithResourcesFromAssembly</c>).
    /// </summary>
    public required IMcpServerBuilder Mcp { get; init; }

    /// <summary>
    /// DI service collection scoped to this module. Services registered here
    /// are available to the module's tools/prompts/resources but isolated
    /// from other modules. The platform's own services are not exposed.
    /// </summary>
    public required IServiceCollection Services { get; init; }

    /// <summary>
    /// Module-specific configuration slice, sourced from the platform's
    /// <c>config.json</c> under <c>modules.&lt;name&gt;.moduleConfig</c>.
    /// Use this for module-specific settings like API keys, base URLs, etc.
    /// The platform's own configuration is not exposed.
    /// </summary>
    public required IConfiguration ModuleConfig { get; init; }

    /// <summary>
    /// Pre-configured logger with the module name as its source context. All
    /// log lines from this logger are tagged so dashboard filters work
    /// correctly. Prefer this over creating loggers via
    /// <see cref="ILoggerFactory"/>.
    /// </summary>
    public required ILogger Logger { get; init; }

    /// <summary>
    /// Read-only metadata about the running platform — version, auth modes.
    /// </summary>
    public required IPlatformInfo Platform { get; init; }

    // Reserved for SDK v1.1+:
    //   public ISecretProvider Secrets { get; init; }
    //   public ITelemetry Telemetry { get; init; }
    //   public IEntitlementChecker Entitlements { get; init; }
}
