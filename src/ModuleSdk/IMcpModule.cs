namespace WinMcp.ModuleSdk;

/// <summary>
/// Contract for a WinMCP module. Every loadable module ships a single class
/// implementing this interface; the platform's module loader discovers it via
/// the module's <c>module.json</c> manifest (<c>entryType</c> field), constructs
/// it via the parameterless constructor, verifies <see cref="Metadata"/>
/// matches the manifest, and calls <see cref="Configure"/> once at startup.
/// </summary>
/// <remarks>
/// <para>
/// Modules must have a public, parameterless constructor — the loader does not
/// resolve constructor dependencies via DI. Use
/// <see cref="ModuleConfigurationContext.Services"/> to register the module's
/// own services if it needs DI internally.
/// </para>
/// <para>
/// The <see cref="Configure"/> method must not block, must not throw under
/// normal startup conditions, and must complete in well under a second. Slow
/// initialization (e.g., reaching out to external services) should be deferred
/// to first-use rather than performed at configuration time.
/// </para>
/// </remarks>
public interface IMcpModule
{
    /// <summary>
    /// Identity of this module. Must match the corresponding fields in the
    /// module's <c>module.json</c> manifest. The platform compares both at
    /// load time and refuses to load on mismatch (typical cause: forgetting
    /// to bump <c>version</c> in code or manifest after a release).
    /// </summary>
    ModuleMetadata Metadata { get; }

    /// <summary>
    /// Called exactly once at platform startup. Register MCP tools, prompts,
    /// and resources via <see cref="ModuleConfigurationContext.Mcp"/>; register
    /// any module-scoped services via <see cref="ModuleConfigurationContext.Services"/>.
    /// </summary>
    /// <param name="ctx">Configuration context. See <see cref="ModuleConfigurationContext"/>.</param>
    void Configure(ModuleConfigurationContext ctx);
}
