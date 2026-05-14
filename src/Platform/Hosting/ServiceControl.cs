using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WinMcp.Platform.Hosting;

/// <summary>
/// Helpers around <c>sc.exe</c> for service lifecycle and SDDL. Carries
/// forward math-mcp's v1.0.21 parallel stdout/stderr read pattern to avoid
/// the classic pipe-buffer-fills deadlock with verbose child output.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ServiceControl
{
    public static (int Exit, string Stdout, string Stderr) RunSc(params string[] args) =>
        Run("sc.exe", args);

    public static void SafeRunSc(params string[] args)
    {
        var (exit, stdout, stderr) = RunSc(args);
        if (exit != 0)
        {
            Console.Error.WriteLine($"sc.exe {string.Join(' ', args)} → exit {exit}");
            if (!string.IsNullOrWhiteSpace(stdout)) Console.Error.WriteLine(stdout);
            if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.WriteLine(stderr);
        }
    }

    /// <summary>Poll <c>sc query</c> until the service reaches the desired state or timeout.</summary>
    public static bool WaitForServiceState(string desiredState, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var (exit, stdout, _) = RunSc("query", PlatformPaths.ServiceName);
            if (exit == 0 && stdout.Contains(desiredState, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            Thread.Sleep(500);
        }
        return false;
    }

    /// <summary>Wait for a "marked for deletion" service to fully clear from SCM.</summary>
    public static bool WaitForServiceGone(int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var (exit, _, _) = RunSc("query", PlatformPaths.ServiceName);
            // sc query returns non-zero (1060) when the service does not exist.
            if (exit != 0) return true;
            Thread.Sleep(500);
        }
        return false;
    }

    /// <summary>
    /// Grant the service its own SERVICE_START/SERVICE_STOP rights so the
    /// in-UI upgrade helper can stop and start it. Carries forward math-mcp's
    /// v1.0.16 fix.
    /// </summary>
    public static void GrantServiceSelfControl()
    {
        var (sdExit, sdOut, _) = RunSc("sdshow", PlatformPaths.ServiceName);
        if (sdExit != 0) return;

        var dacl = sdOut.Trim();
        // Skip if our ACE is already present.
        var newAce = $"(A;;CCLCSWRPWPDTLOCRRC;;;{PlatformPaths.ServiceAccount.Replace(@"\", @"\\")})";
        if (dacl.Contains(newAce, StringComparison.Ordinal)) return;

        // Insert before the SACL marker (S:) if present, else append.
        var sIdx = dacl.IndexOf("S:", StringComparison.Ordinal);
        var updated = sIdx >= 0
            ? dacl.Insert(sIdx, newAce)
            : dacl + newAce;

        SafeRunSc("sdset", PlatformPaths.ServiceName, updated);
    }

    /// <summary>
    /// Parallel-read stdout/stderr from a child process. Carries forward
    /// math-mcp v1.0.21's deadlock fix; sequential ReadToEnd locks up if
    /// a child fills the stderr buffer while we're draining stdout.
    /// </summary>
    public static (int Exit, string Stdout, string Stderr) Run(string file, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        Task.WaitAll(stdoutTask, stderrTask);
        p.WaitForExit();
        return (p.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    public static bool IsAdmin()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return true;
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
