namespace WinMcp.ModuleSdk;

/// <summary>
/// Optional marker attribute applied to a class implementing
/// <see cref="IMcpModule"/>. Lets the platform's module loader find the entry
/// type by scanning rather than requiring the <c>entryType</c> field in the
/// manifest to be populated.
/// </summary>
/// <remarks>
/// In v1.0 the manifest's <c>entryType</c> takes precedence; this attribute
/// is informational only. In a future SDK version, modules may omit
/// <c>entryType</c> from the manifest if the assembly contains exactly one
/// class marked with <see cref="McpModuleAttribute"/>. Recommended to apply
/// it now for forward compatibility.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class McpModuleAttribute : Attribute
{
}
