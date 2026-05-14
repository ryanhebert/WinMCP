using System.Reflection;
using ModelContextProtocol.Server;

namespace WinMcp.Platform.Modules;

/// <summary>
/// Reflects over a module's assembly to enumerate the tool / prompt /
/// resource names it contributes. Used to build the
/// <see cref="ModuleRegistry"/> so the URL-scoped filter knows which
/// items belong to which module.
/// </summary>
/// <remarks>
/// Mirrors what the ModelContextProtocol SDK does internally — looks for
/// types marked with <c>[McpServerToolType]</c>, <c>[McpServerPromptType]</c>,
/// <c>[McpServerResourceType]</c>; on each, looks for methods marked with
/// the corresponding tool/prompt/resource attribute and computes the name.
/// </remarks>
public static class ModuleToolDiscovery
{
    public sealed record DiscoveryResult(
        IReadOnlyCollection<string> Tools,
        IReadOnlyCollection<string> Prompts,
        IReadOnlyCollection<string> Resources);

    public static DiscoveryResult Discover(Assembly asm)
    {
        var tools = new HashSet<string>(StringComparer.Ordinal);
        var prompts = new HashSet<string>(StringComparer.Ordinal);
        var resources = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in asm.GetTypes())
        {
            if (type.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            {
                foreach (var m in EnumerateMethods(type))
                {
                    var attr = m.GetCustomAttribute<McpServerToolAttribute>();
                    if (attr is null) continue;
                    tools.Add(ResolveName(attr.Name, m.Name));
                }
            }

            if (type.GetCustomAttribute<McpServerPromptTypeAttribute>() is not null)
            {
                foreach (var m in EnumerateMethods(type))
                {
                    var attr = m.GetCustomAttribute<McpServerPromptAttribute>();
                    if (attr is null) continue;
                    prompts.Add(ResolveName(attr.Name, m.Name));
                }
            }

            if (type.GetCustomAttribute<McpServerResourceTypeAttribute>() is not null)
            {
                foreach (var m in EnumerateMethods(type))
                {
                    var attr = m.GetCustomAttribute<McpServerResourceAttribute>();
                    if (attr is null) continue;
                    // Resources are accessed by URI template, but the SDK
                    // also exposes a "name" (informational). For our filter
                    // we key by URI template; capture both to be safe.
                    var uri = attr.UriTemplate;
                    if (!string.IsNullOrEmpty(uri)) resources.Add(uri);
                    resources.Add(ResolveName(attr.Name, m.Name));
                }
            }
        }

        return new DiscoveryResult(tools, prompts, resources);
    }

    private static IEnumerable<MethodInfo> EnumerateMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static string ResolveName(string? explicitName, string methodName) =>
        !string.IsNullOrEmpty(explicitName) ? explicitName : methodName;
}
