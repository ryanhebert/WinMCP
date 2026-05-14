using WinMcp.ModuleSdk;

namespace WinMcp.Platform.Modules;

/// <summary>
/// Concrete <see cref="IPlatformInfo"/> implementation passed to each module
/// at configuration time. Snapshot — values are captured at module-load
/// time and do not refresh if platform config changes.
/// </summary>
public sealed class PlatformInfo : IPlatformInfo
{
    public required string PlatformVersion { get; init; }
    public required string McpProtocolVersion { get; init; }
    public required AuthMode AdminAuthMode { get; init; }
    public required AuthMode McpDefaultAuthMode { get; init; }
}
