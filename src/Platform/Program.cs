using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting.WindowsServices;
using WinMcp.Platform.Hosting;

if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    Console.Error.WriteLine("WinMCP runs on Windows only.");
    return 1;
}

if (WindowsServiceHelpers.IsWindowsService())
{
    return ServiceHost.Run(asWindowsService: true);
}

var argList = args.ToList();

if (argList.Contains("--version") || argList.Contains("-v"))
{
    var version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";
    Console.WriteLine($"WinMCP {version}");
    return 0;
}

if (argList.Contains("--help") || argList.Contains("-h"))
{
    PrintUsage();
    return 0;
}

var verb = argList.Count > 0 && !argList[0].StartsWith("--", StringComparison.Ordinal)
    ? argList[0].ToLowerInvariant()
    : "";

return verb switch
{
    "uninstall" => Installer.Uninstall(),
    "run" => ServiceHost.Run(asWindowsService: false),
    "rotate-creds" => Installer.RotateCreds(),
    "" => Installer.Install(
        httpPort: TryGetIntFlag(argList, "--http-port"),
        httpsPort: TryGetIntFlag(argList, "--https-port"),
        authFlag: ParseAuthFlag(argList)),
    _ => UsageAndExit(),
};

static int UsageAndExit()
{
    PrintUsage();
    return 2;
}

static void PrintUsage()
{
    Console.WriteLine("WinMCP — multi-module MCP server platform for Windows");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  WinMCP.exe [--auth] [--http-port N] [--https-port N]");
    Console.WriteLine("                                                Install (silent, requires admin)");
    Console.WriteLine("  WinMCP.exe --auth off                         Reinstall with auth disabled");
    Console.WriteLine("  WinMCP.exe rotate-creds                       Regenerate demo credentials (admin)");
    Console.WriteLine("  WinMCP.exe uninstall                          Uninstall (requires admin)");
    Console.WriteLine("  WinMCP.exe run                                Run in foreground (debugging)");
    Console.WriteLine("  WinMCP.exe --version                          Print version and exit");
    Console.WriteLine("  WinMCP.exe --help                             Show this help");
    Console.WriteLine();
    Console.WriteLine("Auth modes:");
    Console.WriteLine("  --auth        Enable demo auth on /<module>/mcp endpoints");
    Console.WriteLine("                Credentials are auto-generated and printed once.");
    Console.WriteLine("                Real OIDC auth is configured via config.json after install.");
    Console.WriteLine("  --auth off    Explicitly disable auth on a reinstall.");
}

static AuthFlag ParseAuthFlag(List<string> args)
{
    var idx = args.IndexOf("--auth");
    if (idx < 0) return AuthFlag.NotSpecified;
    if (idx + 1 < args.Count && args[idx + 1].Equals("off", StringComparison.OrdinalIgnoreCase))
    {
        return AuthFlag.ForceDisabled;
    }
    return AuthFlag.EnableDemo;
}

static int? TryGetIntFlag(List<string> args, string name)
{
    var idx = args.IndexOf(name);
    if (idx < 0 || idx + 1 >= args.Count) return null;
    if (!int.TryParse(args[idx + 1], out var value))
    {
        Console.Error.WriteLine($"Invalid value for {name}: {args[idx + 1]}");
        Environment.Exit(2);
    }
    if (value < 1 || value > 65535)
    {
        Console.Error.WriteLine($"{name} must be in range 1..65535");
        Environment.Exit(2);
    }
    return value;
}
