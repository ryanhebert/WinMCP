# WinMcp.ModuleSdk

SDK for building modules that run inside the [WinMCP](https://github.com/ryanhebert/WinMCP) platform.

A module is a .NET class library that:
1. References this NuGet package
2. Implements `IMcpModule` to register MCP tools, prompts, and resources
3. Ships alongside a `module.json` manifest in a release artifact

The WinMCP platform loads the module at startup, mounts its MCP server at `/<module-name>/mcp`, and handles HTTPS, authentication, observability, and lifecycle.

## Quick start

```bash
dotnet new classlib -n MyModule -f net8.0
cd MyModule
dotnet add package WinMcp.ModuleSdk
dotnet add package ModelContextProtocol
```

```csharp
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using WinMcp.ModuleSdk;
using System.ComponentModel;

namespace MyModule;

public sealed class MyMcpModule : IMcpModule
{
    public ModuleMetadata Metadata { get; } = new()
    {
        Name = "mymodule",
        Version = "1.0.0",
        DisplayName = "My Module",
        Description = "An example WinMCP module"
    };

    public void Configure(ModuleConfigurationContext ctx)
    {
        ctx.Logger.LogInformation("Configuring");
        ctx.Mcp.WithToolsFromAssembly(typeof(MyTools).Assembly);
    }
}

[McpServerToolType]
public static class MyTools
{
    [McpServerTool, Description("Says hello.")]
    public static string Hello(string name) => $"Hello, {name}!";
}
```

## Manifest

Each module ships a `module.json` alongside its DLL. Minimum required fields:

```json
{
  "name": "mymodule",
  "version": "1.0.0",
  "displayName": "My Module",
  "assembly": "MyModule.dll",
  "entryType": "MyModule.MyMcpModule",
  "mountPath": "/mymodule",
  "minPlatformVersion": "1.0.0"
}
```

Full manifest reference: <https://github.com/ryanhebert/WinMCP-Modules/blob/main/docs/MANIFEST.md>

## Distribution

Cut a GitHub release containing the DLL + `module.json` zipped together (e.g., `MyModule-v1.0.0.zip`). Users install via the WinMCP dashboard's "Install module" UI by pasting the release URL.

You can host modules in your own repo. The official modules repo at <https://github.com/ryanhebert/WinMCP-Modules> only carries the curated set.

## Versioning

This SDK follows SemVer. Anything in pre-1.0 (e.g., `1.0.0-alpha.N`) may break before 1.0.0 stable; pin to an exact version during alpha. After 1.0.0 stable, breaking changes happen only at major version bumps.

## Links

- WinMCP platform: <https://github.com/ryanhebert/WinMCP>
- Official modules: <https://github.com/ryanhebert/WinMCP-Modules>
- Building a module guide: <https://github.com/ryanhebert/WinMCP-Modules/blob/main/docs/BUILDING-A-MODULE.md>
- Manifest schema: <https://github.com/ryanhebert/WinMCP-Modules/blob/main/docs/MANIFEST.md>

## License

Apache 2.0.
