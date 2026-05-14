using Microsoft.Extensions.Logging;
using WinMcp.ModuleSdk;

namespace WinMcp.Platform.Modules;

/// <summary>
/// Scans <c>&lt;InstallDir&gt;\modules\</c> at startup, loads each module's
/// manifest + assembly, instantiates its <see cref="IMcpModule"/>, and
/// returns the successful loads. Per-module failures are isolated — one bad
/// module logs an error and is skipped; other modules continue to load.
/// </summary>
public sealed class ModuleLoader
{
    private readonly ILogger<ModuleLoader> _logger;
    private readonly string _platformVersion;

    public ModuleLoader(ILogger<ModuleLoader> logger, string platformVersion)
    {
        _logger = logger;
        _platformVersion = platformVersion;
    }

    /// <summary>
    /// Returns the loaded modules in deterministic order (by name) so that
    /// route registration is stable across restarts.
    /// </summary>
    public IReadOnlyList<LoadedModule> ScanAndLoad(string modulesRootDir)
    {
        if (!Directory.Exists(modulesRootDir))
        {
            _logger.LogInformation(
                "Modules dir does not exist; no modules to load. dir={Dir}", modulesRootDir);
            return Array.Empty<LoadedModule>();
        }

        var result = new List<LoadedModule>();
        var moduleDirs = Directory.EnumerateDirectories(modulesRootDir)
            .OrderBy(p => p, StringComparer.Ordinal);

        foreach (var dir in moduleDirs)
        {
            try
            {
                var loaded = LoadOne(dir);
                if (loaded is not null) result.Add(loaded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Module load failed: dir={Dir}. Module skipped; other modules continue.",
                    dir);
            }
        }

        return result
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
    }

    private LoadedModule? LoadOne(string moduleDir)
    {
        var manifestPath = Path.Combine(moduleDir, "module.json");
        if (!File.Exists(manifestPath))
        {
            _logger.LogWarning(
                "Module dir has no module.json; skipping. dir={Dir}", moduleDir);
            return null;
        }

        var manifest = ModuleManifestParser.ParseFile(manifestPath);

        if (!IsPlatformVersionCompatible(manifest.MinPlatformVersion))
        {
            _logger.LogWarning(
                "Module {Name} v{Version} requires minPlatformVersion >= {Required}; running platform is {Platform}. Skipping.",
                manifest.Name, manifest.Version, manifest.MinPlatformVersion, _platformVersion);
            return null;
        }

        var assemblyPath = Path.Combine(moduleDir, manifest.Assembly);
        if (!File.Exists(assemblyPath))
        {
            _logger.LogError(
                "Module {Name} manifest points to {Assembly} but it does not exist in {Dir}. Skipping.",
                manifest.Name, manifest.Assembly, moduleDir);
            return null;
        }

        // Module-name == directory-name check. The mount path is derived from
        // the manifest's name; if the dir is misnamed, operators will scratch
        // their heads when /<dirname>/mcp 404s while /<manifest.name>/mcp works.
        var dirName = Path.GetFileName(moduleDir);
        if (!string.Equals(dirName, manifest.Name, StringComparison.Ordinal))
        {
            _logger.LogError(
                "Module {Name}: directory name '{DirName}' does not match manifest name '{ManifestName}'. Skipping.",
                manifest.Name, dirName, manifest.Name);
            return null;
        }

        var alc = new ModuleAssemblyContext(manifest.Name, assemblyPath);
        var asm = alc.LoadFromAssemblyPath(assemblyPath);

        var entryType = asm.GetType(manifest.EntryType, throwOnError: false);
        if (entryType is null)
        {
            _logger.LogError(
                "Module {Name}: entryType '{EntryType}' not found in {Assembly}. Skipping.",
                manifest.Name, manifest.EntryType, manifest.Assembly);
            return null;
        }
        if (!typeof(IMcpModule).IsAssignableFrom(entryType))
        {
            _logger.LogError(
                "Module {Name}: entryType '{EntryType}' does not implement IMcpModule. Skipping.",
                manifest.Name, manifest.EntryType);
            return null;
        }

        IMcpModule instance;
        try
        {
            instance = (IMcpModule)Activator.CreateInstance(entryType)!;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Module {Name}: failed to instantiate {EntryType}. Skipping.",
                manifest.Name, manifest.EntryType);
            return null;
        }

        if (!MetadataMatchesManifest(instance.Metadata, manifest))
        {
            _logger.LogError(
                "Module {Name}: IMcpModule.Metadata fields do not match manifest. " +
                "code:(name={CodeName}, version={CodeVersion}, displayName={CodeDisplay}) " +
                "manifest:(name={MName}, version={MVersion}, displayName={MDisplay}). Skipping.",
                manifest.Name,
                instance.Metadata.Name, instance.Metadata.Version, instance.Metadata.DisplayName,
                manifest.Name, manifest.Version, manifest.DisplayName);
            return null;
        }

        _logger.LogInformation(
            "Loaded module {Name} v{Version} from {Dir} (maturity={Maturity}, mountPath={MountPath})",
            manifest.Name, manifest.Version, moduleDir, manifest.Maturity, manifest.MountPath);

        return new LoadedModule
        {
            Manifest = manifest,
            Context = alc,
            Instance = instance,
            InstallPath = moduleDir,
        };
    }

    private bool IsPlatformVersionCompatible(string minRequired)
    {
        // SemVer-aware: split on '-' to strip pre-release suffix, then
        // parse the major.minor.patch numbers. Pre-release tags on the
        // running platform (e.g., "1.0.0-alpha.1") count as the underlying
        // release for compatibility purposes.
        static (int major, int minor, int patch) Parse(string v)
        {
            var core = v.Split('-', 2)[0].Split('+', 2)[0];
            var parts = core.Split('.');
            return (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
        }

        var have = Parse(_platformVersion);
        var need = Parse(minRequired);

        if (have.major != need.major) return have.major > need.major;
        if (have.minor != need.minor) return have.minor > need.minor;
        return have.patch >= need.patch;
    }

    private static bool MetadataMatchesManifest(ModuleMetadata md, ModuleManifest m) =>
        string.Equals(md.Name, m.Name, StringComparison.Ordinal) &&
        string.Equals(md.Version, m.Version, StringComparison.Ordinal) &&
        string.Equals(md.DisplayName, m.DisplayName, StringComparison.Ordinal);
}
