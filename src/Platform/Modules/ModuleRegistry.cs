namespace WinMcp.Platform.Modules;

/// <summary>
/// Maps tool / prompt / resource names back to the module that contributed
/// them. Populated once during platform startup by reflecting each loaded
/// module's assembly. Consumed by <see cref="ModuleScopedMcpFilter"/> to
/// reject cross-module calls and to filter list responses.
/// </summary>
public sealed class ModuleRegistry
{
    private readonly Dictionary<string, string> _toolToModule;
    private readonly Dictionary<string, string> _promptToModule;
    private readonly Dictionary<string, string> _resourceToModule;
    private readonly Dictionary<string, ModuleScope> _moduleScopes;

    public IReadOnlyDictionary<string, ModuleScope> Scopes => _moduleScopes;

    public ModuleRegistry(IEnumerable<(string Module, ModuleToolDiscovery.DiscoveryResult Disco)> loaded)
    {
        _toolToModule = new(StringComparer.Ordinal);
        _promptToModule = new(StringComparer.Ordinal);
        _resourceToModule = new(StringComparer.Ordinal);
        _moduleScopes = new(StringComparer.Ordinal);

        foreach (var (module, disco) in loaded)
        {
            foreach (var t in disco.Tools) _toolToModule[t] = module;
            foreach (var p in disco.Prompts) _promptToModule[p] = module;
            foreach (var r in disco.Resources) _resourceToModule[r] = module;
            _moduleScopes[module] = new ModuleScope(
                new HashSet<string>(disco.Tools, StringComparer.Ordinal),
                new HashSet<string>(disco.Prompts, StringComparer.Ordinal),
                new HashSet<string>(disco.Resources, StringComparer.Ordinal));
        }
    }

    public bool ToolBelongsTo(string toolName, string module) =>
        _moduleScopes.TryGetValue(module, out var scope) && scope.Tools.Contains(toolName);

    public bool PromptBelongsTo(string promptName, string module) =>
        _moduleScopes.TryGetValue(module, out var scope) && scope.Prompts.Contains(promptName);

    public bool ResourceBelongsTo(string resourceUriOrName, string module) =>
        _moduleScopes.TryGetValue(module, out var scope) && scope.Resources.Contains(resourceUriOrName);
}

public sealed record ModuleScope(
    HashSet<string> Tools,
    HashSet<string> Prompts,
    HashSet<string> Resources);
