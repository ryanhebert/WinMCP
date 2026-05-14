namespace WinMcp.ModuleSdk;

/// <summary>
/// Read-only platform metadata exposed to modules at configuration time.
/// Lets a module branch on platform version or auth mode without reaching
/// into platform internals.
/// </summary>
/// <remarks>
/// Available via <see cref="ModuleConfigurationContext.Platform"/>.
/// </remarks>
public interface IPlatformInfo
{
    /// <summary>
    /// SemVer version of the running WinMCP platform (e.g., <c>"1.0.0"</c>).
    /// Useful for modules that want to enable optional behavior only on
    /// newer platform versions.
    /// </summary>
    string PlatformVersion { get; }

    /// <summary>
    /// MCP protocol version the platform advertises by default
    /// (e.g., <c>"2025-06-18"</c>). Reflects the version pinned by the
    /// ModelContextProtocol SDK shipping with this platform release.
    /// </summary>
    string McpProtocolVersion { get; }

    /// <summary>
    /// Effective authentication mode for the platform's admin/operator
    /// surface. Modules don't generally need this — it's exposed for the
    /// rare case where a module's behavior should differ when admins
    /// can/cannot reach it (e.g., omitting test-only tools when the
    /// platform is in production posture).
    /// </summary>
    AuthMode AdminAuthMode { get; }

    /// <summary>
    /// Effective authentication mode applied to the MCP surface by default.
    /// Individual modules may have a stricter or more permissive override;
    /// see <see cref="ModuleConfigurationContext"/> for the resolved value
    /// applied to this module specifically.
    /// </summary>
    AuthMode McpDefaultAuthMode { get; }
}
