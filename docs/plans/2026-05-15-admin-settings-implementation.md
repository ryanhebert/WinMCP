# Admin Settings Pages — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the dashboard's editable Settings page (`/settings`) with three tabs — Identity providers, Admin auth, MCP default auth — backed by REST endpoints that mutate `config.json` and require a service restart to take effect.

**Architecture:** REST JSON API at `/api/settings/*` consumed by a single client-side HTML page at `/settings`. Saves trigger an in-process `RestartPending` flag visible globally across the dashboard. A `POST /api/settings/restart` endpoint spawns a detached helper batch (same idiom as `UpgradeOrchestrator`) that stops and starts the service. Server-side validation in a pure `SettingsValidator` class has unit-test coverage in a new `tests/Platform.Tests/` xUnit project.

**Tech Stack:** .NET 8, ASP.NET Core, ModelContextProtocol.AspNetCore, xUnit (new), Serilog, existing `ConfigLoader` for atomic save.

**Spec:** `docs/specs/2026-05-15-admin-settings-design.md` (commit `0638a4e`).

---

## File map

**Create:**
- `tests/Platform.Tests/Platform.Tests.csproj`
- `tests/Platform.Tests/SettingsValidatorTests.cs`
- `src/Platform/Config/SettingsValidator.cs`
- `src/Platform/Hosting/RestartCoordinator.cs`
- `src/Platform/Hosting/SettingsApi.cs`
- `src/Platform/Web/SettingsPage.cs`

**Modify:**
- `src/Platform/Hosting/RouteRegistration.cs` — wire new routes, add `/settings` + `/api/settings` to the no-store cache list
- `src/Platform/Web/IndexPage.cs` — add the global restart-pending banner
- `WinMCP.sln` — add the test project
- `docs/BACKLOG.md` — add live-reload-of-validators entry
- `CHANGELOG.md` — `[Unreleased]` entry

---

### Task 1: Scaffold tests/Platform.Tests project

**Files:**
- Create: `tests/Platform.Tests/Platform.Tests.csproj`
- Create: `tests/Platform.Tests/SmokeTest.cs`
- Modify: `WinMCP.sln`

- [ ] **Step 1: Write the project file**

`tests/Platform.Tests/Platform.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <RootNamespace>WinMcp.Platform.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Platform\WinMcp.Platform.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Write a smoke test**

`tests/Platform.Tests/SmokeTest.cs`:

```csharp
namespace WinMcp.Platform.Tests;

public class SmokeTest
{
    [Fact]
    public void RuntimeWorks() => Assert.Equal(4, 2 + 2);
}
```

- [ ] **Step 3: Add to solution**

Run:

```bash
cd /home/ryan/ai/WinMCP && /home/ryan/.dotnet/dotnet sln add tests/Platform.Tests/Platform.Tests.csproj
```

- [ ] **Step 4: Run the test**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet test tests/Platform.Tests/Platform.Tests.csproj --nologo
```

Expected: `Passed: 1`.

Note: the Platform.csproj references Windows-specific APIs and is annotated `[SupportedOSPlatform("windows")]` on the host-y types. Test project may emit `CA1416` warnings when transitively touching those types from Linux. They're informational and won't fail the build. Ignore.

- [ ] **Step 5: Commit**

```bash
git add tests/Platform.Tests/ WinMCP.sln
git -c user.name="Ryan Hebert" -c user.email="ryan.hebert@gmail.com" \
    commit -m "test: scaffold tests/Platform.Tests xUnit project"
```

---

### Task 2: SettingsValidator — name / issuer / audience validation (TDD)

**Files:**
- Test: `tests/Platform.Tests/SettingsValidatorTests.cs`
- Create: `src/Platform/Config/SettingsValidator.cs`

The validator exposes static `Validate*` methods returning a `ValidationResult` record. Each method represents one logical check.

- [ ] **Step 1: Write failing tests for provider-shape validation**

Replace `tests/Platform.Tests/SettingsValidatorTests.cs` (delete the smoke test once these pass; tracked in Task 4):

```csharp
using WinMcp.Platform.Config;

namespace WinMcp.Platform.Tests;

public class SettingsValidator_ProviderShape
{
    [Theory]
    [InlineData("okta")]                                    // valid
    [InlineData("auth0-staging")]                           // valid with hyphen
    [InlineData("a")]                                       // min length
    public void Name_Valid(string name) =>
        Assert.True(SettingsValidator.ValidateProviderShape(name, "https://issuer.example.com", "winmcp").Ok);

    [Theory]
    [InlineData("")]                                        // empty
    [InlineData("Okta")]                                    // uppercase
    [InlineData("okta_idp")]                                // underscore
    [InlineData("1okta")]                                   // leading digit
    [InlineData("-okta")]                                   // leading hyphen
    [InlineData("okta!")]                                   // special char
    public void Name_Invalid(string name)
    {
        var r = SettingsValidator.ValidateProviderShape(name, "https://issuer.example.com", "winmcp");
        Assert.False(r.Ok);
        Assert.Equal("name", r.Field);
    }

    [Theory]
    [InlineData("https://issuer.example.com")]
    [InlineData("https://issuer.example.com/realms/main")]
    [InlineData("http://localhost:8080")]                   // http allowed for dev
    public void Issuer_Valid(string issuer) =>
        Assert.True(SettingsValidator.ValidateProviderShape("okta", issuer, "winmcp").Ok);

    [Theory]
    [InlineData("")]
    [InlineData("issuer.example.com")]                      // no scheme
    [InlineData("ftp://issuer.example.com")]                // wrong scheme
    [InlineData("https://issuer.example.com?q=1")]          // query string
    [InlineData("https://issuer.example.com/../etc/passwd")] // traversal
    public void Issuer_Invalid(string issuer)
    {
        var r = SettingsValidator.ValidateProviderShape("okta", issuer, "winmcp");
        Assert.False(r.Ok);
        Assert.Equal("issuer", r.Field);
    }

    [Theory]
    [InlineData("")]
    [InlineData("two tokens")]
    public void Audience_Invalid(string audience)
    {
        var r = SettingsValidator.ValidateProviderShape("okta", "https://issuer.example.com", audience);
        Assert.False(r.Ok);
        Assert.Equal("audience", r.Field);
    }

    [Fact]
    public void Audience_DefaultsAllowed() =>
        Assert.True(SettingsValidator.ValidateProviderShape("okta", "https://issuer.example.com", "winmcp").Ok);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet test tests/Platform.Tests/Platform.Tests.csproj --nologo
```

Expected: compile error — `SettingsValidator` doesn't exist yet.

- [ ] **Step 3: Implement the validator**

Create `src/Platform/Config/SettingsValidator.cs`:

```csharp
using System.Text.RegularExpressions;

namespace WinMcp.Platform.Config;

/// <summary>
/// Pure shape + cross-reference validation for incoming settings payloads.
/// Mirrors the strictness of <see cref="Modules.ModuleManifestParser"/>:
/// reject on any malformed input; never silently save.
/// </summary>
/// <remarks>
/// All public methods are pure functions returning <see cref="ValidationResult"/>.
/// They never write, log, or otherwise mutate state — that's the API handler's job.
/// </remarks>
public static class SettingsValidator
{
    private static readonly Regex ProviderNameRegex = new(@"^[a-z][a-z0-9-]{0,62}$", RegexOptions.Compiled);

    public static ValidationResult ValidateProviderShape(string name, string issuer, string audience)
    {
        if (!ProviderNameRegex.IsMatch(name))
        {
            return ValidationResult.Fail("name", "must match ^[a-z][a-z0-9-]{0,62}$");
        }
        var issuerResult = ValidateIssuerUrl(issuer);
        if (!issuerResult.Ok) return issuerResult;
        if (string.IsNullOrWhiteSpace(audience) || audience.Contains(' '))
        {
            return ValidationResult.Fail("audience", "must be a single non-empty token");
        }
        return ValidationResult.Pass();
    }

    private static ValidationResult ValidateIssuerUrl(string issuer)
    {
        if (string.IsNullOrWhiteSpace(issuer))
        {
            return ValidationResult.Fail("issuer", "must not be empty");
        }
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri))
        {
            return ValidationResult.Fail("issuer", "must be an absolute URL");
        }
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return ValidationResult.Fail("issuer", "must use http or https");
        }
        if (!string.IsNullOrEmpty(uri.Query))
        {
            return ValidationResult.Fail("issuer", "must not include a query string");
        }
        // Reject "..\.." or "../.." traversal in any path segment.
        if (uri.AbsolutePath.Split('/').Any(s => s == ".." || s == "."))
        {
            return ValidationResult.Fail("issuer", "must not contain path traversal");
        }
        return ValidationResult.Pass();
    }
}

public sealed record ValidationResult(bool Ok, string? Field, string? Message)
{
    public static ValidationResult Pass() => new(true, null, null);
    public static ValidationResult Fail(string field, string message) => new(false, field, message);
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet test tests/Platform.Tests/Platform.Tests.csproj --nologo
```

Expected: all tests pass (16+ cases).

- [ ] **Step 5: Commit**

```bash
git add src/Platform/Config/SettingsValidator.cs tests/Platform.Tests/SettingsValidatorTests.cs
git -c user.name="Ryan Hebert" -c user.email="ryan.hebert@gmail.com" \
    commit -m "feat(config): SettingsValidator — provider shape validation"
```

---

### Task 3: SettingsValidator — auth domain validation (TDD)

**Files:**
- Modify: `tests/Platform.Tests/SettingsValidatorTests.cs` (append)
- Modify: `src/Platform/Config/SettingsValidator.cs` (append methods)

Adds `ValidateAuthDomainShape` covering admin + MCP. Server-side counterpart to the client's form validation.

- [ ] **Step 1: Append failing tests**

Append to `tests/Platform.Tests/SettingsValidatorTests.cs`:

```csharp
public class SettingsValidator_AuthDomain
{
    private static readonly HashSet<string> NoProviders = new();
    private static readonly HashSet<string> WithOkta = new() { "okta" };

    [Fact]
    public void Mode_None_OK() =>
        Assert.True(SettingsValidator.ValidateAuthDomainShape(
            "none", providerRef: null, requiredScopes: Array.Empty<string>(),
            requiredClaims: new Dictionary<string, IReadOnlyList<string>>(),
            knownProviders: NoProviders).Ok);

    [Fact]
    public void Mode_Demo_OK() =>
        Assert.True(SettingsValidator.ValidateAuthDomainShape(
            "demo", null, Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>>(),
            NoProviders).Ok);

    [Fact]
    public void Mode_Oidc_RequiresProviderRef()
    {
        var r = SettingsValidator.ValidateAuthDomainShape(
            "oidc", providerRef: null, Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>>(), WithOkta);
        Assert.False(r.Ok);
        Assert.Equal("providerRef", r.Field);
    }

    [Fact]
    public void Mode_Oidc_ProviderRefMustExist()
    {
        var r = SettingsValidator.ValidateAuthDomainShape(
            "oidc", providerRef: "ghost", Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>>(), WithOkta);
        Assert.False(r.Ok);
        Assert.Equal("providerRef", r.Field);
        Assert.Contains("not configured", r.Message ?? "");
    }

    [Fact]
    public void Mode_Oidc_HappyPath() =>
        Assert.True(SettingsValidator.ValidateAuthDomainShape(
            "oidc", "okta",
            new[] { "openid", "email" },
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["groups"] = new[] { "admins" },
            },
            WithOkta).Ok);

    [Theory]
    [InlineData("garbage")]
    [InlineData("OIDC")]
    [InlineData("")]
    public void Mode_Invalid(string mode)
    {
        var r = SettingsValidator.ValidateAuthDomainShape(
            mode, null, Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>>(),
            NoProviders);
        Assert.False(r.Ok);
        Assert.Equal("mode", r.Field);
    }

    [Fact]
    public void RequiredScopes_EmptyStringRejected()
    {
        var r = SettingsValidator.ValidateAuthDomainShape(
            "oidc", "okta", new[] { "openid", "" },
            new Dictionary<string, IReadOnlyList<string>>(),
            WithOkta);
        Assert.False(r.Ok);
        Assert.Equal("requiredScopes", r.Field);
    }

    [Fact]
    public void RequiredClaims_EmptyKeyRejected()
    {
        var r = SettingsValidator.ValidateAuthDomainShape(
            "oidc", "okta", Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>> { [""] = new[] { "admins" } },
            WithOkta);
        Assert.False(r.Ok);
        Assert.Equal("requiredClaims", r.Field);
    }

    [Fact]
    public void RequiredClaims_EmptyValueArrayRejected()
    {
        var r = SettingsValidator.ValidateAuthDomainShape(
            "oidc", "okta", Array.Empty<string>(),
            new Dictionary<string, IReadOnlyList<string>> { ["groups"] = Array.Empty<string>() },
            WithOkta);
        Assert.False(r.Ok);
        Assert.Equal("requiredClaims", r.Field);
    }
}
```

- [ ] **Step 2: Run tests; expect compile error**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet test tests/Platform.Tests/Platform.Tests.csproj --nologo
```

Expected: compile error (method doesn't exist).

- [ ] **Step 3: Append to SettingsValidator**

Add to `src/Platform/Config/SettingsValidator.cs` inside the class:

```csharp
    private static readonly HashSet<string> ValidModes = new(StringComparer.Ordinal) { "none", "demo", "oidc" };

    public static ValidationResult ValidateAuthDomainShape(
        string mode,
        string? providerRef,
        IReadOnlyList<string> requiredScopes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> requiredClaims,
        IReadOnlySet<string> knownProviders)
    {
        if (!ValidModes.Contains(mode))
        {
            return ValidationResult.Fail("mode", $"must be one of 'none', 'demo', 'oidc' (got '{mode}')");
        }

        if (mode == "oidc")
        {
            if (string.IsNullOrWhiteSpace(providerRef))
            {
                return ValidationResult.Fail("providerRef", "must reference a configured identity provider");
            }
            if (!knownProviders.Contains(providerRef))
            {
                return ValidationResult.Fail("providerRef", $"provider '{providerRef}' is not configured");
            }
        }

        foreach (var scope in requiredScopes)
        {
            if (string.IsNullOrWhiteSpace(scope))
            {
                return ValidationResult.Fail("requiredScopes", "scope entries must not be empty");
            }
        }

        foreach (var (key, values) in requiredClaims)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return ValidationResult.Fail("requiredClaims", "claim names must not be empty");
            }
            if (values.Count == 0)
            {
                return ValidationResult.Fail("requiredClaims", $"claim '{key}' must have at least one value");
            }
            foreach (var v in values)
            {
                if (string.IsNullOrWhiteSpace(v))
                {
                    return ValidationResult.Fail("requiredClaims", $"claim '{key}' has an empty value");
                }
            }
        }

        return ValidationResult.Pass();
    }
```

- [ ] **Step 4: Run tests, expect pass**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet test tests/Platform.Tests/Platform.Tests.csproj --nologo
```

Expected: all tests pass (~25 cases total now).

- [ ] **Step 5: Commit**

```bash
git add src/Platform/Config/SettingsValidator.cs tests/Platform.Tests/SettingsValidatorTests.cs
git -c user.name="Ryan Hebert" -c user.email="ryan.hebert@gmail.com" \
    commit -m "feat(config): SettingsValidator — auth domain validation"
```

---

### Task 4: SettingsValidator — orphan rejection on provider delete (TDD)

**Files:**
- Modify: `tests/Platform.Tests/SettingsValidatorTests.cs` (append + delete smoke test)
- Modify: `src/Platform/Config/SettingsValidator.cs` (append)
- Delete: `tests/Platform.Tests/SmokeTest.cs`

- [ ] **Step 1: Append orphan-check tests**

Append to `tests/Platform.Tests/SettingsValidatorTests.cs`:

```csharp
using WinMcp.Platform.Auth;
using WinMcp.Platform.Config;

public class SettingsValidator_OrphanCheck
{
    private static PlatformConfig MakeConfig(
        AuthMode adminMode = AuthMode.None,
        string? adminProvider = null,
        AuthMode mcpMode = AuthMode.None,
        string? mcpProvider = null)
    {
        var c = new PlatformConfig();
        c.Admin.Auth = new AuthDomainConfig { Mode = adminMode, ProviderRef = adminProvider };
        c.Mcp.DefaultAuth = new AuthDomainConfig { Mode = mcpMode, ProviderRef = mcpProvider };
        return c;
    }

    [Fact]
    public void Delete_Unreferenced_OK()
    {
        var c = MakeConfig();
        var r = SettingsValidator.ValidateProviderDeletable("okta", c);
        Assert.True(r.Ok);
    }

    [Fact]
    public void Delete_AdminReferenced_Rejected()
    {
        var c = MakeConfig(adminMode: AuthMode.Oidc, adminProvider: "okta");
        var r = SettingsValidator.ValidateProviderDeletable("okta", c);
        Assert.False(r.Ok);
        Assert.Equal("name", r.Field);
        Assert.Contains("admin", r.Message ?? "");
    }

    [Fact]
    public void Delete_McpReferenced_Rejected()
    {
        var c = MakeConfig(mcpMode: AuthMode.Oidc, mcpProvider: "okta");
        var r = SettingsValidator.ValidateProviderDeletable("okta", c);
        Assert.False(r.Ok);
        Assert.Equal("name", r.Field);
        Assert.Contains("mcp", r.Message ?? "");
    }

    [Fact]
    public void Delete_BothReferenced_ReportsBoth()
    {
        var c = MakeConfig(
            adminMode: AuthMode.Oidc, adminProvider: "okta",
            mcpMode: AuthMode.Oidc, mcpProvider: "okta");
        var r = SettingsValidator.ValidateProviderDeletable("okta", c);
        Assert.False(r.Ok);
        Assert.Contains("admin", r.Message ?? "");
        Assert.Contains("mcp", r.Message ?? "");
    }

    [Fact]
    public void Delete_DifferentProviderReferenced_OK()
    {
        var c = MakeConfig(adminMode: AuthMode.Oidc, adminProvider: "auth0");
        var r = SettingsValidator.ValidateProviderDeletable("okta", c);
        Assert.True(r.Ok);
    }
}
```

- [ ] **Step 2: Delete the smoke test now that real coverage exists**

```bash
rm tests/Platform.Tests/SmokeTest.cs
```

- [ ] **Step 3: Run tests; expect failures**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet test tests/Platform.Tests/Platform.Tests.csproj --nologo
```

Expected: compile error (`ValidateProviderDeletable` doesn't exist).

- [ ] **Step 4: Implement orphan check**

Append to `src/Platform/Config/SettingsValidator.cs`:

```csharp
    /// <summary>
    /// Rejects deletion of an OIDC provider that the admin or MCP auth
    /// domain still references. Returns the referencing domains in the
    /// error message so the operator knows which setting to change first.
    /// </summary>
    public static ValidationResult ValidateProviderDeletable(string name, PlatformConfig config)
    {
        var inUseBy = new List<string>();
        if (config.Admin.Auth.Mode == Auth.AuthMode.Oidc &&
            string.Equals(config.Admin.Auth.ProviderRef, name, StringComparison.Ordinal))
        {
            inUseBy.Add("admin");
        }
        if (config.Mcp.DefaultAuth.Mode == Auth.AuthMode.Oidc &&
            string.Equals(config.Mcp.DefaultAuth.ProviderRef, name, StringComparison.Ordinal))
        {
            inUseBy.Add("mcp");
        }
        if (inUseBy.Count > 0)
        {
            return ValidationResult.Fail(
                "name",
                $"provider '{name}' is in use by: {string.Join(", ", inUseBy)}. Change those settings before deleting.");
        }
        return ValidationResult.Pass();
    }
```

- [ ] **Step 5: Run + commit**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet test tests/Platform.Tests/Platform.Tests.csproj --nologo
```

Expected: all pass (~30 cases).

```bash
git add src/Platform/Config/SettingsValidator.cs tests/Platform.Tests/SettingsValidatorTests.cs
git rm tests/Platform.Tests/SmokeTest.cs
git -c user.name="Ryan Hebert" -c user.email="ryan.hebert@gmail.com" \
    commit -m "feat(config): SettingsValidator — orphan rejection on provider delete"
```

---

### Task 5: RestartCoordinator

**Files:**
- Create: `src/Platform/Hosting/RestartCoordinator.cs`

Holds the in-process `RestartPending` flag and spawns the helper batch on demand. No unit tests — the batch script is verified via the Windows smoke checklist.

- [ ] **Step 1: Implement RestartCoordinator**

Create `src/Platform/Hosting/RestartCoordinator.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.Versioning;

namespace WinMcp.Platform.Hosting;

/// <summary>
/// In-process restart-pending flag and helper-batch spawner. The flag is
/// set by every successful settings save; the dashboard polls it and shows
/// a global banner. <see cref="TriggerRestart"/> spawns a detached
/// <c>.cmd</c> that waits 2s, then sc-stops + sc-starts the service.
/// </summary>
/// <remarks>
/// Same helper-batch pattern as <see cref="UpgradeOrchestrator"/>: the
/// running .NET process can't restart itself synchronously, so we hand
/// the work to cmd.exe and exit when SCM tells us to.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class RestartCoordinator
{
    private static volatile bool _pending;
    private static DateTime? _since;
    private static readonly object _gate = new();

    public static bool IsPending => _pending;
    public static DateTime? PendingSince => _since;

    public static void MarkPending()
    {
        lock (_gate)
        {
            if (!_pending)
            {
                _pending = true;
                _since = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Writes the helper batch + spawns it detached. Returns immediately.
    /// The helper sleeps 2s (long enough for this process to flush its
    /// 202 response), then sc-stops and sc-starts the service.
    /// </summary>
    public static void TriggerRestart(ILogger logger)
    {
        var helperPath = Path.Combine(PlatformPaths.InstallDir, "restart-helper.cmd");
        var helperContent =
            "@echo off\r\n" +
            "timeout /t 2 /nobreak >nul\r\n" +
            $"sc stop {PlatformPaths.ServiceName} >nul 2>&1\r\n" +
            ":wait_exit\r\n" +
            "tasklist /fi \"imagename eq WinMCP.exe\" 2>nul | find /i \"WinMCP.exe\" >nul\r\n" +
            "if errorlevel 1 goto :start\r\n" +
            "timeout /t 1 /nobreak >nul\r\n" +
            "goto :wait_exit\r\n" +
            ":start\r\n" +
            $"sc start {PlatformPaths.ServiceName} >nul 2>&1\r\n" +
            "exit /b 0\r\n";
        File.WriteAllText(helperPath, helperContent);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{helperPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        using var p = Process.Start(psi);
        logger.LogWarning(
            "Restart helper spawned (PID {Pid}). Service stop will follow in ~2s.",
            p?.Id);
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet build src/Platform/WinMcp.Platform.csproj -c Release -nologo 2>&1 | tail -6
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/Platform/Hosting/RestartCoordinator.cs
git -c user.name="Ryan Hebert" -c user.email="ryan.hebert@gmail.com" \
    commit -m "feat(hosting): RestartCoordinator — pending flag + helper-batch spawn"
```

---

### Task 6: SettingsApi — DTOs + GET /api/settings + restart-pending endpoint

**Files:**
- Create: `src/Platform/Hosting/SettingsApi.cs`

The DTOs are internal records in the same file. `GET /api/settings` returns a snapshot of the editable surface; `GET /api/settings/restart-pending` is a tiny endpoint the dashboard polls for the global banner.

- [ ] **Step 1: Create SettingsApi with DTOs + the two GETs**

Create `src/Platform/Hosting/SettingsApi.cs`:

```csharp
using System.Runtime.Versioning;
using WinMcp.Platform.Auth;
using WinMcp.Platform.Config;

namespace WinMcp.Platform.Hosting;

/// <summary>
/// REST handlers for <c>/api/settings/*</c>. Sibling of
/// <see cref="UpgradeOrchestrator"/>: a static class with handler methods
/// that mutate <c>config.json</c> + flip <see cref="RestartCoordinator"/>'s
/// pending flag. The endpoints are gated by the existing admin-auth
/// middleware registered in <see cref="RouteRegistration"/>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SettingsApi
{
    public static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/api/settings", (Delegate)HandleGetSnapshot);
        app.MapGet("/api/settings/restart-pending", (Delegate)HandleGetRestartPending);
    }

    // ----- DTOs -----

    internal sealed record SettingsSnapshotDto(
        AuthDomainDto Admin,
        AuthDomainDto Mcp,
        IReadOnlyList<OidcProviderDto> OidcProviders);

    internal sealed record AuthDomainDto(
        string Mode,                 // "none" | "demo" | "oidc"
        string? ProviderRef,
        IReadOnlyList<string> RequiredScopes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredClaims);

    internal sealed record OidcProviderDto(
        string Name,
        string Issuer,
        string? Audience,
        string? JwksUrl,
        string? AuthorizationEndpoint,
        string? TokenEndpoint,
        string? DiscoveredAtIso,
        IReadOnlyList<string> InUseBy);  // ["admin"], ["mcp"], or both

    internal sealed record RestartPendingDto(bool Pending, string? Since);

    // ----- Handlers -----

    private static IResult HandleGetSnapshot(HttpContext ctx)
    {
        var config = ctx.RequestServices.GetRequiredService<PlatformConfig>();
        return Results.Json(ToSnapshot(config));
    }

    private static IResult HandleGetRestartPending() =>
        Results.Json(new RestartPendingDto(
            Pending: RestartCoordinator.IsPending,
            Since: RestartCoordinator.PendingSince?.ToString("O")));

    // ----- Helpers (also used by later handler tasks) -----

    internal static SettingsSnapshotDto ToSnapshot(PlatformConfig c) =>
        new(
            Admin: ToAuthDto(c.Admin.Auth),
            Mcp: ToAuthDto(c.Mcp.DefaultAuth),
            OidcProviders: c.OidcProviders.Select(kvp => ToProviderDto(kvp.Key, kvp.Value, c)).ToList());

    internal static AuthDomainDto ToAuthDto(AuthDomainConfig a) =>
        new(
            Mode: a.Mode.ToString().ToLowerInvariant(),
            ProviderRef: a.ProviderRef,
            RequiredScopes: a.RequiredScopes,
            RequiredClaims: a.RequiredClaims);

    internal static OidcProviderDto ToProviderDto(string name, OidcProviderConfig p, PlatformConfig c)
    {
        var inUseBy = new List<string>();
        if (c.Admin.Auth.Mode == AuthMode.Oidc && c.Admin.Auth.ProviderRef == name) inUseBy.Add("admin");
        if (c.Mcp.DefaultAuth.Mode == AuthMode.Oidc && c.Mcp.DefaultAuth.ProviderRef == name) inUseBy.Add("mcp");
        return new OidcProviderDto(
            Name: name,
            Issuer: p.Issuer,
            Audience: p.Audience,
            JwksUrl: p.JwksUrl,
            AuthorizationEndpoint: p.AuthorizationEndpoint,
            TokenEndpoint: p.TokenEndpoint,
            DiscoveredAtIso: p.DiscoveredAtIso,
            InUseBy: inUseBy);
    }
}
```

- [ ] **Step 2: Register PlatformConfig in DI**

The handler resolves `PlatformConfig` from DI but ServiceHost doesn't currently register it. Add to `src/Platform/Hosting/ServiceHost.cs` after the `requestLog` / `tokenStore` lines (around line 100):

```csharp
        // Make the live config available to the settings API. SettingsApi
        // mutates it in-place + persists via ConfigLoader.Save; the live
        // instance keeps existing consumers (RouteRegistration, etc.)
        // pointed at the same object.
        builder.Services.AddSingleton(config);
```

- [ ] **Step 3: Build**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet build src/Platform/WinMcp.Platform.csproj -c Release -nologo 2>&1 | tail -6
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/Platform/Hosting/SettingsApi.cs src/Platform/Hosting/ServiceHost.cs
git -c user.name="Ryan Hebert" -c user.email="ryan.hebert@gmail.com" \
    commit -m "feat(hosting): SettingsApi — DTOs + GET snapshot + restart-pending"
```

---

### Task 7: SettingsApi — OIDC provider CRUD endpoints

**Files:**
- Modify: `src/Platform/Hosting/SettingsApi.cs`

Adds POST / PUT / DELETE on `/api/settings/oidc-providers` + rediscover sub-route. POST runs `OidcDiscovery` synchronously; failure = 422, no persistence. DELETE consults `SettingsValidator.ValidateProviderDeletable`.

- [ ] **Step 1: Add the four endpoints to MapEndpoints**

Replace the existing `MapEndpoints` body in `SettingsApi.cs`:

```csharp
    public static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/api/settings", (Delegate)HandleGetSnapshot);
        app.MapGet("/api/settings/restart-pending", (Delegate)HandleGetRestartPending);
        app.MapPost("/api/settings/oidc-providers", (Delegate)HandleAddProvider);
        app.MapPut("/api/settings/oidc-providers/{name}", (Delegate)HandleUpdateProvider);
        app.MapDelete("/api/settings/oidc-providers/{name}", (Delegate)HandleDeleteProvider);
        app.MapPost("/api/settings/oidc-providers/{name}/rediscover", (Delegate)HandleRediscover);
    }
```

- [ ] **Step 2: Add the AddProvider DTO + handler**

Append to `SettingsApi.cs` (inside the class):

```csharp
    internal sealed record AddProviderRequest(string Name, string Issuer, string? Audience);

    private static async Task<IResult> HandleAddProvider(HttpContext ctx)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("WinMcp.Settings");
        var config = ctx.RequestServices.GetRequiredService<PlatformConfig>();

        AddProviderRequest? body;
        try
        {
            body = await ctx.Request.ReadFromJsonAsync<AddProviderRequest>();
        }
        catch
        {
            return Results.Json(new { error = "invalid_request", message = "malformed JSON body" }, statusCode: 400);
        }
        if (body is null)
        {
            return Results.Json(new { error = "invalid_request", message = "body is required" }, statusCode: 400);
        }

        var audience = string.IsNullOrWhiteSpace(body.Audience) ? "winmcp" : body.Audience;
        var validation = SettingsValidator.ValidateProviderShape(body.Name, body.Issuer, audience);
        if (!validation.Ok)
        {
            return Results.Json(new { error = "invalid_request", field = validation.Field, message = validation.Message }, statusCode: 400);
        }

        if (config.OidcProviders.ContainsKey(body.Name))
        {
            return Results.Json(new { error = "already_exists", message = $"provider '{body.Name}' already exists" }, statusCode: 409);
        }

        // Discover synchronously — this is the right tradeoff per the design doc.
        var http = ctx.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
        var disco = new Auth.Oidc.OidcDiscovery(http,
            ctx.RequestServices.GetRequiredService<ILogger<Auth.Oidc.OidcDiscovery>>());
        var probe = new Auth.Oidc.OidcProvider { Name = body.Name, Issuer = body.Issuer, Audience = audience };
        try
        {
            await disco.DiscoverAsync(probe);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OIDC discovery failed for proposed provider {Name} issuer={Issuer}", body.Name, body.Issuer);
            return Results.Json(new { error = "discovery_failed", message = ex.Message }, statusCode: 422);
        }

        var entry = new OidcProviderConfig
        {
            Issuer = body.Issuer,
            Audience = audience,
            JwksUrl = probe.JwksUrl,
            AuthorizationEndpoint = probe.AuthorizationEndpoint,
            TokenEndpoint = probe.TokenEndpoint,
            DiscoveredAtIso = DateTime.UtcNow.ToString("O"),
        };
        config.OidcProviders[body.Name] = entry;
        ConfigLoader.Save(config, PlatformPaths.ConfigPath);

        // Register live so subsequent admin/mcp save calls' providerRef
        // resolution sees the new provider without requiring a restart.
        // (The validator itself will be active only after restart.)
        ctx.RequestServices.GetRequiredService<Auth.Oidc.OidcProviderRegistry>().Upsert(probe);

        RestartCoordinator.MarkPending();
        logger.LogWarning("Added OIDC provider name={Name} issuer={Issuer}", body.Name, body.Issuer);
        return Results.Json(ToProviderDto(body.Name, entry, config), statusCode: 201);
    }
```

- [ ] **Step 3: Add Upsert to OidcProviderRegistry**

The handler calls `OidcProviderRegistry.Upsert(...)`. Add the method to `src/Platform/Auth/Oidc/OidcProviderRegistry.cs`:

```csharp
    public void Upsert(OidcProvider provider)
    {
        // Replace if present; add otherwise. Used by the settings API when
        // a new provider is added live so subsequent providerRef validation
        // sees it.
        for (var i = 0; i < _providers.Count; i++)
        {
            if (string.Equals(_providers[i].Name, provider.Name, StringComparison.Ordinal))
            {
                _providers[i] = provider;
                return;
            }
        }
        _providers.Add(provider);
    }
```

If `_providers` is currently a `IReadOnlyList`, change it to `List<OidcProvider>`. Verify the field declaration matches before adding the method.

- [ ] **Step 4: Add the Update / Delete / Rediscover handlers**

Append to `SettingsApi.cs`:

```csharp
    internal sealed record UpdateProviderRequest(string Issuer, string? Audience);

    private static async Task<IResult> HandleUpdateProvider(HttpContext ctx, string name)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("WinMcp.Settings");
        var config = ctx.RequestServices.GetRequiredService<PlatformConfig>();

        if (!config.OidcProviders.TryGetValue(name, out var existing))
        {
            return Results.NotFound(new { error = "not_found", message = $"provider '{name}' does not exist" });
        }

        UpdateProviderRequest? body;
        try { body = await ctx.Request.ReadFromJsonAsync<UpdateProviderRequest>(); }
        catch { return Results.Json(new { error = "invalid_request", message = "malformed JSON body" }, statusCode: 400); }
        if (body is null) return Results.Json(new { error = "invalid_request", message = "body is required" }, statusCode: 400);

        var audience = string.IsNullOrWhiteSpace(body.Audience) ? "winmcp" : body.Audience;
        var validation = SettingsValidator.ValidateProviderShape(name, body.Issuer, audience);
        if (!validation.Ok)
        {
            return Results.Json(new { error = "invalid_request", field = validation.Field, message = validation.Message }, statusCode: 400);
        }

        // Issuer change → must re-discover. Audience-only change → keep existing endpoints.
        var issuerChanged = !string.Equals(body.Issuer, existing.Issuer, StringComparison.Ordinal);
        var entry = new OidcProviderConfig
        {
            Issuer = body.Issuer,
            Audience = audience,
            JwksUrl = issuerChanged ? null : existing.JwksUrl,
            AuthorizationEndpoint = issuerChanged ? null : existing.AuthorizationEndpoint,
            TokenEndpoint = issuerChanged ? null : existing.TokenEndpoint,
            DiscoveredAtIso = issuerChanged ? null : existing.DiscoveredAtIso,
        };

        if (issuerChanged)
        {
            var http = ctx.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
            var disco = new Auth.Oidc.OidcDiscovery(http,
                ctx.RequestServices.GetRequiredService<ILogger<Auth.Oidc.OidcDiscovery>>());
            var probe = new Auth.Oidc.OidcProvider { Name = name, Issuer = body.Issuer, Audience = audience };
            try { await disco.DiscoverAsync(probe); }
            catch (Exception ex)
            {
                return Results.Json(new { error = "discovery_failed", message = ex.Message }, statusCode: 422);
            }
            entry.JwksUrl = probe.JwksUrl;
            entry.AuthorizationEndpoint = probe.AuthorizationEndpoint;
            entry.TokenEndpoint = probe.TokenEndpoint;
            entry.DiscoveredAtIso = DateTime.UtcNow.ToString("O");
            ctx.RequestServices.GetRequiredService<Auth.Oidc.OidcProviderRegistry>().Upsert(probe);
        }

        config.OidcProviders[name] = entry;
        ConfigLoader.Save(config, PlatformPaths.ConfigPath);
        RestartCoordinator.MarkPending();
        logger.LogWarning("Updated OIDC provider name={Name}", name);
        return Results.Json(ToProviderDto(name, entry, config));
    }

    private static IResult HandleDeleteProvider(HttpContext ctx, string name)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("WinMcp.Settings");
        var config = ctx.RequestServices.GetRequiredService<PlatformConfig>();

        if (!config.OidcProviders.ContainsKey(name))
        {
            return Results.NotFound(new { error = "not_found", message = $"provider '{name}' does not exist" });
        }

        var validation = SettingsValidator.ValidateProviderDeletable(name, config);
        if (!validation.Ok)
        {
            var inUseBy = new List<string>();
            if (config.Admin.Auth.Mode == AuthMode.Oidc && config.Admin.Auth.ProviderRef == name) inUseBy.Add("admin");
            if (config.Mcp.DefaultAuth.Mode == AuthMode.Oidc && config.Mcp.DefaultAuth.ProviderRef == name) inUseBy.Add("mcp");
            return Results.Json(new { error = "in_use", message = validation.Message, inUseBy }, statusCode: 409);
        }

        config.OidcProviders.Remove(name);
        ConfigLoader.Save(config, PlatformPaths.ConfigPath);
        RestartCoordinator.MarkPending();
        logger.LogWarning("Deleted OIDC provider name={Name}", name);
        return Results.NoContent();
    }

    private static async Task<IResult> HandleRediscover(HttpContext ctx, string name)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("WinMcp.Settings");
        var config = ctx.RequestServices.GetRequiredService<PlatformConfig>();

        if (!config.OidcProviders.TryGetValue(name, out var existing))
        {
            return Results.NotFound(new { error = "not_found", message = $"provider '{name}' does not exist" });
        }

        var http = ctx.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient();
        var disco = new Auth.Oidc.OidcDiscovery(http,
            ctx.RequestServices.GetRequiredService<ILogger<Auth.Oidc.OidcDiscovery>>());
        var probe = new Auth.Oidc.OidcProvider
        {
            Name = name,
            Issuer = existing.Issuer,
            Audience = existing.Audience ?? "winmcp",
        };
        try { await disco.DiscoverAsync(probe); }
        catch (Exception ex)
        {
            return Results.Json(new { error = "discovery_failed", message = ex.Message }, statusCode: 422);
        }

        existing.JwksUrl = probe.JwksUrl;
        existing.AuthorizationEndpoint = probe.AuthorizationEndpoint;
        existing.TokenEndpoint = probe.TokenEndpoint;
        existing.DiscoveredAtIso = DateTime.UtcNow.ToString("O");
        ConfigLoader.Save(config, PlatformPaths.ConfigPath);
        ctx.RequestServices.GetRequiredService<Auth.Oidc.OidcProviderRegistry>().Upsert(probe);
        // Re-discovery doesn't flip the restart-pending flag; no auth-mode
        // change occurred, just refreshed endpoint URLs.
        logger.LogInformation("Re-discovered OIDC provider name={Name}", name);
        return Results.Json(ToProviderDto(name, existing, config));
    }
```

- [ ] **Step 5: Build + commit**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet build src/Platform/WinMcp.Platform.csproj -c Release -nologo 2>&1 | tail -6
```

Expected: 0 errors. If `OidcProviderRegistry._providers` was readonly, the build will fail until that field is changed.

```bash
git add src/Platform/Hosting/SettingsApi.cs src/Platform/Auth/Oidc/OidcProviderRegistry.cs
git -c user.name="Ryan Hebert" -c user.email="ryan.hebert@gmail.com" \
    commit -m "feat(hosting): SettingsApi — OIDC provider CRUD + rediscover"
```

---

### Task 8: SettingsApi — admin + MCP auth PUT endpoints

**Files:**
- Modify: `src/Platform/Hosting/SettingsApi.cs`

Two PUT endpoints with shared payload + validation. Server-side enforces the lockout-safety contract by accepting `oidc` for admin auth but logging a WARN — the UI's confirm dialog is the operator-facing gate.

- [ ] **Step 1: Add the route registrations**

Append to `MapEndpoints` in `SettingsApi.cs`:

```csharp
        app.MapPut("/api/settings/admin-auth", (Delegate)HandleUpdateAdminAuth);
        app.MapPut("/api/settings/mcp-auth", (Delegate)HandleUpdateMcpAuth);
```

- [ ] **Step 2: Add the shared request DTO + handlers**

Append to `SettingsApi.cs`:

```csharp
    internal sealed record AuthDomainRequest(
        string Mode,
        string? ProviderRef,
        IReadOnlyList<string>? RequiredScopes,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? RequiredClaims);

    private static async Task<IResult> HandleUpdateAdminAuth(HttpContext ctx) =>
        await HandleUpdateAuthDomain(ctx, isAdmin: true);

    private static async Task<IResult> HandleUpdateMcpAuth(HttpContext ctx) =>
        await HandleUpdateAuthDomain(ctx, isAdmin: false);

    private static async Task<IResult> HandleUpdateAuthDomain(HttpContext ctx, bool isAdmin)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("WinMcp.Settings");
        var config = ctx.RequestServices.GetRequiredService<PlatformConfig>();

        AuthDomainRequest? body;
        try { body = await ctx.Request.ReadFromJsonAsync<AuthDomainRequest>(); }
        catch { return Results.Json(new { error = "invalid_request", message = "malformed JSON body" }, statusCode: 400); }
        if (body is null) return Results.Json(new { error = "invalid_request", message = "body is required" }, statusCode: 400);

        var scopes = body.RequiredScopes ?? Array.Empty<string>();
        var claims = body.RequiredClaims ?? new Dictionary<string, IReadOnlyList<string>>();
        var knownProviders = new HashSet<string>(config.OidcProviders.Keys, StringComparer.Ordinal);

        var validation = SettingsValidator.ValidateAuthDomainShape(
            body.Mode, body.ProviderRef, scopes, claims, knownProviders);
        if (!validation.Ok)
        {
            return Results.Json(new { error = "invalid_request", field = validation.Field, message = validation.Message }, statusCode: 400);
        }

        var parsed = body.Mode.ToLowerInvariant() switch
        {
            "none" => AuthMode.None,
            "demo" => AuthMode.Demo,
            "oidc" => AuthMode.Oidc,
            _ => throw new InvalidOperationException("validator should have caught this"),
        };

        // Lockout-safety note: we accept admin → oidc even though the OIDC
        // validator returns 503 in v1.0. The UI shows a confirmation gate
        // before the request reaches us. Logging WARN gives operators a
        // breadcrumb if they wonder later why the dashboard is bricked.
        if (isAdmin && parsed == AuthMode.Oidc)
        {
            logger.LogWarning(
                "Admin auth set to OIDC. OIDC validator returns 503 until v1.1; the dashboard will be unreachable until that ships or until config.json is edited on the host.");
        }

        var newDomain = new AuthDomainConfig
        {
            Mode = parsed,
            ProviderRef = body.ProviderRef,
            RequiredScopes = scopes,
            RequiredClaims = claims,
            DemoCredentials = isAdmin ? null : config.Mcp.DefaultAuth.DemoCredentials,
        };

        if (isAdmin)
        {
            config.Admin.Auth = newDomain;
            logger.LogWarning("Admin auth updated: mode={Mode} providerRef={Ref}", parsed, body.ProviderRef ?? "(none)");
        }
        else
        {
            config.Mcp.DefaultAuth = newDomain;
            logger.LogWarning("MCP default auth updated: mode={Mode} providerRef={Ref}", parsed, body.ProviderRef ?? "(none)");
        }

        ConfigLoader.Save(config, PlatformPaths.ConfigPath);
        RestartCoordinator.MarkPending();
        return Results.Json(ToSnapshot(config));
    }
```

- [ ] **Step 3: Build + commit**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet build src/Platform/WinMcp.Platform.csproj -c Release -nologo 2>&1 | tail -6
```

Expected: 0 errors.

```bash
git add src/Platform/Hosting/SettingsApi.cs
git -c user.name="Ryan Hebert" -c user.email="ryan.hebert@gmail.com" \
    commit -m "feat(hosting): SettingsApi — admin + MCP auth PUT endpoints"
```

---

### Task 9: SettingsApi — restart endpoint

**Files:**
- Modify: `src/Platform/Hosting/SettingsApi.cs`

`POST /api/settings/restart` invokes `RestartCoordinator.TriggerRestart` and returns 202. The handler does NOT clear `RestartPending` — the flag dies with the process; the dashboard auto-reloads once `/info` responds.

- [ ] **Step 1: Add the route + handler**

Append to `MapEndpoints`:

```csharp
        app.MapPost("/api/settings/restart", (Delegate)HandleRestart);
```

Append to `SettingsApi.cs`:

```csharp
    private static IResult HandleRestart(HttpContext ctx)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("WinMcp.Settings");
        RestartCoordinator.TriggerRestart(logger);
        return Results.Json(new
        {
            status = "initiated",
            note = "Service will stop in ~2s and start back up. Poll /info to detect when it's online.",
        }, statusCode: 202);
    }
```

- [ ] **Step 2: Build + commit**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet build src/Platform/WinMcp.Platform.csproj -c Release -nologo 2>&1 | tail -6
```

Expected: 0 errors.

```bash
git add src/Platform/Hosting/SettingsApi.cs
git -c user.name="Ryan Hebert" -c user.email="ryan.hebert@gmail.com" \
    commit -m "feat(hosting): SettingsApi — POST /api/settings/restart"
```

---

### Task 10: SettingsPage HTML

**Files:**
- Create: `src/Platform/Web/SettingsPage.cs`

A single self-contained HTML doc — same C# raw-string-literal idiom as `IndexPage.cs` and `LogsPage.cs`. Three client-side tabs, REST calls via fetch, optimistic-pessimistic posture (wait for server, show inline errors).

This is a large file — the structure here lays out everything needed; the implementer follows the existing dashboard templates for visual style. Match the `--bg`, `--bg-card`, `--accent`, `--accent-2` colour palette and card / badge / button styling already used by `IndexPage.cs`. Reuse the `.copy-btn` / `.card` / `.btn` classes.

- [ ] **Step 1: Create the file scaffold**

Create `src/Platform/Web/SettingsPage.cs`:

```csharp
using System.Text.Json;

namespace WinMcp.Platform.Web;

/// <summary>
/// Editable settings page at <c>/settings</c>. Three client-side tabs
/// (Identity providers / Admin auth / MCP default auth) backed by the
/// REST endpoints under <c>/api/settings/*</c>. Restart-required apply
/// semantics — successful saves flip an in-process flag that the
/// dashboard renders as a global "Restart pending" banner.
/// </summary>
internal static class SettingsPage
{
    public static string Render(SettingsPageModel m)
    {
        var initialJson = JsonSerializer.Serialize(m.Snapshot, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>WinMCP — Settings</title>
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<style>
  /* Share the dashboard palette + base styles. Reuse: .card, .btn,
     .badge variants, .copy-btn, .pill. */
  :root {
    --bg: #0b1020; --bg-card: #131a30; --bg-card-2: #1a2240;
    --fg: #e6ecff; --fg-muted: #8a93b3;
    --accent: #7c5cff; --accent-2: #4ad6ff;
    --ok: #34d399; --warn: #fbbf24; --err: #f87171;
    --border: rgba(255,255,255,0.08); --code-bg: #0f1428;
  }
  * { box-sizing: border-box; }
  html, body { margin: 0; padding: 0; }
  body {
    font: 15px/1.55 -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, sans-serif;
    color: var(--fg);
    background:
      radial-gradient(60vh 60vh at 15% 0%, rgba(124,92,255,0.18), transparent 70%),
      radial-gradient(60vh 60vh at 95% 10%, rgba(74,214,255,0.12), transparent 70%),
      var(--bg);
    min-height: 100vh;
  }
  .wrap { max-width: 960px; margin: 0 auto; padding: 48px 24px 64px; }
  .crumbs { color: var(--fg-muted); font-size: 12px; margin-bottom: 8px; }
  .crumbs a { color: var(--accent-2); text-decoration: none; }
  header { display: flex; align-items: center; gap: 16px; margin-bottom: 24px; }
  h1 { margin: 0; font-size: 26px; }
  /* Tabs */
  .tabs { display: flex; gap: 4px; border-bottom: 1px solid var(--border); margin-bottom: 20px; }
  .tab {
    background: transparent; color: var(--fg-muted);
    border: none; cursor: pointer;
    padding: 10px 18px; font: inherit; font-size: 13px;
    border-bottom: 2px solid transparent;
  }
  .tab.on { color: var(--fg); border-bottom-color: var(--accent); }
  .tab-panel { display: none; }
  .tab-panel.on { display: block; }
  /* Forms */
  .field { margin: 12px 0; display: grid; grid-template-columns: 160px 1fr; gap: 10px; align-items: center; }
  .field label { color: var(--fg-muted); font-size: 13px; }
  .field input[type="text"], .field input[type="url"], .field select {
    background: var(--code-bg); color: var(--fg);
    border: 1px solid var(--border); border-radius: 6px;
    padding: 6px 10px; font: inherit; font-size: 13px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
  }
  .field input:focus, .field select:focus { outline: none; border-color: var(--accent); }
  .field-error { grid-column: 2; color: var(--err); font-size: 12px; }
  .helper { grid-column: 2; color: var(--fg-muted); font-size: 12px; }
  /* Provider card */
  .provider {
    background: var(--code-bg); border: 1px solid var(--border);
    border-radius: 10px; padding: 14px; margin: 10px 0;
  }
  .provider-row { display: flex; gap: 12px; align-items: baseline; flex-wrap: wrap; }
  .provider-name { font-weight: 600; font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace; }
  .provider-issuer { color: var(--fg-muted); font-size: 12px; word-break: break-all; }
  .provider-status { font-size: 11px; }
  .provider-status.ok { color: var(--ok); }
  .provider-status.err { color: var(--err); }
  .provider-actions { margin-left: auto; display: flex; gap: 6px; }
  /* Restart banner (also rendered on the main dashboard — keep selectors identical) */
  .restart-banner {
    display: none;
    background: linear-gradient(90deg, rgba(251,191,36,0.18) 0%, rgba(124,92,255,0.12) 100%);
    border: 1px solid rgba(251,191,36,0.4);
    border-radius: 10px;
    padding: 10px 14px; margin-bottom: 18px;
    font-size: 13px;
    display: flex; align-items: center; gap: 12px;
  }
  .restart-banner.show { display: flex !important; }
  .restart-banner .dot {
    width: 8px; height: 8px; border-radius: 50%;
    background: var(--warn); box-shadow: 0 0 8px rgba(251,191,36,0.6);
  }
  /* Banners */
  .lockout-warn {
    background: rgba(248,113,113,0.08); border: 1px solid rgba(248,113,113,0.35);
    color: var(--err); padding: 10px 14px; border-radius: 8px;
    font-size: 13px; margin: 12px 0;
  }
  .none-warn {
    background: rgba(251,191,36,0.06); border: 1px solid rgba(251,191,36,0.25);
    color: var(--warn); padding: 8px 12px; border-radius: 8px;
    font-size: 12px; margin: 8px 0;
  }
  /* Empty state */
  .empty {
    color: var(--fg-muted); font-size: 13px; text-align: center;
    padding: 32px; border: 1px dashed var(--border); border-radius: 10px;
  }
</style>
</head>
<body>
  <div class="wrap">
    <div class="crumbs"><a href="/">← Dashboard</a></div>

    <div class="restart-banner" id="restart-banner">
      <span class="dot"></span>
      <span><strong>Restart pending</strong> — your settings changes won't take effect until WinMCP restarts.</span>
      <span style="margin-left:auto"><button class="btn" id="restart-now-btn">Restart now</button></span>
    </div>

    <header>
      <h1>Settings</h1>
    </header>

    <div class="tabs" role="tablist">
      <button class="tab on" data-tab="providers">Identity providers</button>
      <button class="tab" data-tab="admin">Admin auth</button>
      <button class="tab" data-tab="mcp">MCP default auth</button>
    </div>

    <div class="tab-panel on" id="tab-providers">
      <!-- Provider list + add form. JS populates from /api/settings. -->
      <div id="providers-list"></div>
      <button class="btn" id="add-provider-btn">+ Add provider</button>
      <form id="add-provider-form" style="display:none; margin-top:12px;">
        <div class="field">
          <label for="np-name">Name</label>
          <input type="text" id="np-name" placeholder="okta" required>
        </div>
        <div class="field">
          <label for="np-issuer">Issuer URL</label>
          <input type="url" id="np-issuer" placeholder="https://issuer.example.com" required>
        </div>
        <div class="field">
          <label for="np-audience">Audience</label>
          <input type="text" id="np-audience" value="winmcp">
        </div>
        <div class="field"><span></span><span class="field-error" id="np-error" style="display:none"></span></div>
        <div class="field">
          <span></span>
          <span>
            <button class="btn" type="submit" id="np-save">Discover + save</button>
            <button class="btn" type="button" id="np-cancel">Cancel</button>
          </span>
        </div>
      </form>
    </div>

    <div class="tab-panel" id="tab-admin">
      <!-- Mode picker + provider dropdown. Render via JS. -->
      <div id="admin-form-container"></div>
    </div>

    <div class="tab-panel" id="tab-mcp">
      <div id="mcp-form-container"></div>
    </div>
  </div>

<script>
  const INITIAL = {{initialJson}};
  let state = INITIAL;

  // ---- Tabs ----
  document.querySelectorAll('.tab').forEach(t => t.addEventListener('click', () => {
    document.querySelectorAll('.tab').forEach(x => x.classList.toggle('on', x === t));
    document.querySelectorAll('.tab-panel').forEach(p =>
      p.classList.toggle('on', p.id === 'tab-' + t.dataset.tab));
  }));

  // ---- Helpers ----
  function esc(s) {
    return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  }
  async function api(method, path, body) {
    const res = await fetch(path, {
      method, cache: 'no-store',
      headers: body ? { 'Content-Type': 'application/json' } : {},
      body: body ? JSON.stringify(body) : undefined,
    });
    const text = await res.text();
    const json = text ? JSON.parse(text) : null;
    return { ok: res.ok, status: res.status, json };
  }

  // ---- Restart banner ----
  async function pollRestartPending() {
    try {
      const r = await api('GET', '/api/settings/restart-pending');
      if (r.ok && r.json.pending) {
        document.getElementById('restart-banner').classList.add('show');
      }
    } catch (_) { /* ignore */ }
  }
  document.getElementById('restart-now-btn').addEventListener('click', async () => {
    if (!confirm('Restart WinMCP now? Connected MCP clients will see a brief outage (~10s).')) return;
    await api('POST', '/api/settings/restart');
    waitForRestartComplete();
  });
  async function waitForRestartComplete() {
    document.getElementById('restart-banner').innerHTML =
      '<span class="dot"></span><span>Restarting… waiting for the service to come back online.</span>';
    const start = Date.now();
    while (Date.now() - start < 240000) {
      await new Promise(r => setTimeout(r, 1000));
      try {
        const r = await fetch('/info', { cache: 'no-store' });
        if (r.ok) { location.reload(); return; }
      } catch (_) { /* still down */ }
    }
    document.getElementById('restart-banner').innerHTML =
      '<span class="dot" style="background:var(--err)"></span><span>Restart timed out. Check /logs.</span>';
  }
  pollRestartPending();
  setInterval(pollRestartPending, 10000);

  // ---- Identity providers ----
  function renderProviders() {
    const list = document.getElementById('providers-list');
    if (!state.oidcProviders || state.oidcProviders.length === 0) {
      list.innerHTML = '<div class="empty">No identity providers configured. Add one to use OIDC mode for admin or MCP auth.</div>';
      return;
    }
    list.innerHTML = state.oidcProviders.map(p => `
      <div class="provider" data-name="${esc(p.name)}">
        <div class="provider-row">
          <span class="provider-name">${esc(p.name)}</span>
          <span class="provider-status ${p.discoveredAtIso ? 'ok' : 'err'}">
            ${p.discoveredAtIso ? '✓ Discovered ' + esc(p.discoveredAtIso.slice(0, 19)) : '✗ Not yet discovered'}
          </span>
          <div class="provider-actions">
            <button class="btn" data-action="rediscover" data-name="${esc(p.name)}">Re-discover</button>
            <button class="btn" data-action="delete" data-name="${esc(p.name)}">Delete</button>
          </div>
        </div>
        <div class="provider-issuer">${esc(p.issuer)} &middot; audience: ${esc(p.audience || 'winmcp')}</div>
        ${p.inUseBy && p.inUseBy.length ? '<div class="provider-issuer">In use by: ' + p.inUseBy.map(esc).join(', ') + '</div>' : ''}
      </div>
    `).join('');
    list.querySelectorAll('button[data-action]').forEach(b => {
      b.addEventListener('click', () => providerAction(b.dataset.action, b.dataset.name));
    });
  }
  async function providerAction(action, name) {
    if (action === 'rediscover') {
      const r = await api('POST', `/api/settings/oidc-providers/${encodeURIComponent(name)}/rediscover`);
      if (!r.ok) { alert(`Re-discover failed: ${r.json?.message ?? r.status}`); return; }
      // Refresh state.
      const snap = await api('GET', '/api/settings');
      state = snap.json; renderProviders(); renderAdminForm(); renderMcpForm();
    }
    if (action === 'delete') {
      if (!confirm(`Delete provider ${name}?`)) return;
      const r = await api('DELETE', `/api/settings/oidc-providers/${encodeURIComponent(name)}`);
      if (!r.ok) { alert(`Delete failed: ${r.json?.message ?? r.status}`); return; }
      const snap = await api('GET', '/api/settings');
      state = snap.json; renderProviders(); renderAdminForm(); renderMcpForm();
      pollRestartPending();
    }
  }
  document.getElementById('add-provider-btn').addEventListener('click', () => {
    document.getElementById('add-provider-form').style.display = '';
    document.getElementById('add-provider-btn').style.display = 'none';
  });
  document.getElementById('np-cancel').addEventListener('click', () => {
    document.getElementById('add-provider-form').style.display = 'none';
    document.getElementById('add-provider-btn').style.display = '';
    document.getElementById('np-error').style.display = 'none';
  });
  document.getElementById('add-provider-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const body = {
      name: document.getElementById('np-name').value.trim(),
      issuer: document.getElementById('np-issuer').value.trim(),
      audience: document.getElementById('np-audience').value.trim() || 'winmcp',
    };
    const errEl = document.getElementById('np-error');
    errEl.style.display = 'none';
    document.getElementById('np-save').disabled = true;
    document.getElementById('np-save').textContent = 'Discovering…';
    const r = await api('POST', '/api/settings/oidc-providers', body);
    document.getElementById('np-save').disabled = false;
    document.getElementById('np-save').textContent = 'Discover + save';
    if (!r.ok) {
      errEl.textContent = r.json?.message ?? `HTTP ${r.status}`;
      errEl.style.display = '';
      return;
    }
    document.getElementById('add-provider-form').reset();
    document.getElementById('np-audience').value = 'winmcp';
    document.getElementById('add-provider-form').style.display = 'none';
    document.getElementById('add-provider-btn').style.display = '';
    const snap = await api('GET', '/api/settings');
    state = snap.json; renderProviders(); renderAdminForm(); renderMcpForm();
    pollRestartPending();
  });

  // ---- Admin auth tab ----
  function renderAdminForm() { renderAuthForm('admin', 'admin-form-container'); }
  function renderMcpForm()   { renderAuthForm('mcp',   'mcp-form-container'); }

  function renderAuthForm(scope, containerId) {
    const cur = scope === 'admin' ? state.admin : state.mcp;
    const providerOptions = (state.oidcProviders || []).map(p =>
      `<option value="${esc(p.name)}" ${cur.providerRef === p.name ? 'selected' : ''}>${esc(p.name)}</option>`).join('');
    const lockoutWarn = scope === 'admin'
      ? `<div class="lockout-warn" id="${scope}-lockout-warn" style="display:none">
           <strong>Lockout risk</strong>: real OIDC validation lands in v1.1. Switching admin auth to OIDC will
           return 503 on every dashboard request — locking you out. Select this only on a staging install
           where you can recover by editing config.json on the host.
         </div>`
      : '';
    const noneWarn = scope === 'admin'
      ? `<div class="none-warn" id="${scope}-none-warn">
           Dashboard is unauthenticated — anyone on the network can edit these settings.
         </div>`
      : '';
    const demoDisabled = scope === 'admin' ? 'disabled' : '';
    const demoHelper = scope === 'admin'
      ? '<span class="helper">Demo mode is intended for the MCP auth domain only — admin endpoints don\'t issue tokens.</span>'
      : '';
    document.getElementById(containerId).innerHTML = `
      ${lockoutWarn}
      ${noneWarn}
      <form id="${scope}-form">
        <div class="field">
          <label>Mode</label>
          <span>
            <label><input type="radio" name="${scope}-mode" value="none" ${cur.mode === 'none' ? 'checked' : ''}> none</label>
            &nbsp;
            <label><input type="radio" name="${scope}-mode" value="demo" ${cur.mode === 'demo' ? 'checked' : ''} ${demoDisabled}> demo</label>
            &nbsp;
            <label><input type="radio" name="${scope}-mode" value="oidc" ${cur.mode === 'oidc' ? 'checked' : ''}> oidc</label>
          </span>
        </div>
        ${demoHelper}
        <div class="field" id="${scope}-provider-row" style="${cur.mode === 'oidc' ? '' : 'display:none'}">
          <label>Identity provider</label>
          <select name="providerRef" required>
            <option value="">— select —</option>
            ${providerOptions}
          </select>
        </div>
        ${scope === 'mcp' ? `
        <div class="field" id="mcp-scopes-row" style="${cur.mode === 'oidc' ? '' : 'display:none'}">
          <label>Required scopes</label>
          <input type="text" name="requiredScopes" value="${esc((cur.requiredScopes || []).join(' '))}" placeholder="openid email">
        </div>
        ` : ''}
        <div class="field"><span></span><span class="field-error" id="${scope}-error" style="display:none"></span></div>
        <div class="field">
          <span></span>
          <button class="btn" type="submit" id="${scope}-save">Save</button>
        </div>
      </form>
    `;
    // Mode change → toggle provider/scopes rows + lockout warning.
    document.getElementById(`${scope}-form`).addEventListener('change', () => {
      const mode = document.querySelector(`input[name="${scope}-mode"]:checked`)?.value;
      const providerRow = document.getElementById(`${scope}-provider-row`);
      if (providerRow) providerRow.style.display = mode === 'oidc' ? '' : 'none';
      const scopesRow = document.getElementById('mcp-scopes-row');
      if (scopesRow && scope === 'mcp') scopesRow.style.display = mode === 'oidc' ? '' : 'none';
      const warn = document.getElementById(`${scope}-lockout-warn`);
      if (warn) warn.style.display = (scope === 'admin' && mode === 'oidc') ? '' : 'none';
      const noneWarnEl = document.getElementById(`${scope}-none-warn`);
      if (noneWarnEl) noneWarnEl.style.display = mode === 'none' ? '' : 'none';
    });
    document.getElementById(`${scope}-form`).addEventListener('submit', async (e) => {
      e.preventDefault();
      const mode = document.querySelector(`input[name="${scope}-mode"]:checked`)?.value;
      const body = {
        mode,
        providerRef: mode === 'oidc' ? document.querySelector(`#${scope}-provider-row select`)?.value : null,
        requiredScopes: [],
        requiredClaims: {},
      };
      if (scope === 'mcp' && mode === 'oidc') {
        const raw = document.querySelector('#mcp-scopes-row input')?.value || '';
        body.requiredScopes = raw.split(/\s+/).filter(s => s.length > 0);
      }
      // Lockout-safety confirm for admin → oidc.
      if (scope === 'admin' && mode === 'oidc') {
        const ok = confirm(
          'Switching admin auth to OIDC will return 503 on every dashboard request ' +
          'until WinMCP v1.1 ships the OIDC validator. You will be locked out and will ' +
          'need to edit C:\\\\Program Files\\\\WinMCP\\\\config.json on the host to recover. Continue?');
        if (!ok) return;
      }
      const errEl = document.getElementById(`${scope}-error`);
      errEl.style.display = 'none';
      document.getElementById(`${scope}-save`).disabled = true;
      const r = await api('PUT', `/api/settings/${scope === 'admin' ? 'admin-auth' : 'mcp-auth'}`, body);
      document.getElementById(`${scope}-save`).disabled = false;
      if (!r.ok) {
        errEl.textContent = r.json?.message ?? `HTTP ${r.status}`;
        errEl.style.display = '';
        return;
      }
      state = r.json;
      renderProviders(); renderAdminForm(); renderMcpForm();
      pollRestartPending();
    });
  }

  renderProviders();
  renderAdminForm();
  renderMcpForm();
</script>
</body>
</html>
""";
    }
}

internal sealed record SettingsPageModel(SettingsApi.SettingsSnapshotDto Snapshot);
```

- [ ] **Step 2: Build to verify**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet build src/Platform/WinMcp.Platform.csproj -c Release -nologo 2>&1 | tail -6
```

Expected: 0 errors. If `SettingsApi.SettingsSnapshotDto` accessibility complains, change its declaration in `SettingsApi.cs` from `internal sealed record` to `public sealed record` (the page model needs to see the type).

- [ ] **Step 3: Commit**

```bash
git add src/Platform/Web/SettingsPage.cs src/Platform/Hosting/SettingsApi.cs
git -c user.name="Ryan Hebert" -c user.email="ryan.hebert@gmail.com" \
    commit -m "feat(web): SettingsPage — three tabs + restart banner + REST calls"
```

---

### Task 11: IndexPage — global restart-pending banner

**Files:**
- Modify: `src/Platform/Web/IndexPage.cs`

The same restart-banner UX from the Settings page lives on the main dashboard, polling the same endpoint. Operators don't lose the cue when navigating away from `/settings`.

- [ ] **Step 1: Add the CSS for the banner**

Find the existing CSS block in `IndexPage.cs`. After the `.update-banner` rules (search for `.update-banner .progress-bar.indeterminate`), append:

```css
  /* Restart-pending banner — driven by /api/settings/restart-pending. */
  .restart-banner {
    display: none;
    background: linear-gradient(90deg, rgba(251,191,36,0.18) 0%, rgba(124,92,255,0.12) 100%);
    border: 1px solid rgba(251,191,36,0.4);
    border-radius: 10px;
    padding: 10px 14px; margin: 0 0 18px;
    font-size: 13px;
    align-items: center; gap: 12px;
  }
  .restart-banner.show { display: flex; }
  .restart-banner .dot {
    width: 8px; height: 8px; border-radius: 50%;
    background: var(--warn); box-shadow: 0 0 8px rgba(251,191,36,0.6);
  }
```

- [ ] **Step 2: Add the banner HTML**

Find the body section just before `<div class="update-banner" id="update-banner">` and insert above it:

```html
    <div class="restart-banner" id="restart-banner">
      <span class="dot"></span>
      <span><strong>Restart pending</strong> — settings changes won't take effect until WinMCP restarts.</span>
      <span style="margin-left:auto">
        <a href="/settings" class="btn" style="border:1px solid var(--border); border-radius:6px; padding:5px 12px; font-size:12px; text-decoration:none; color:var(--accent);">Open settings</a>
      </span>
    </div>
```

- [ ] **Step 3: Add the polling JS**

At the end of the existing `<script>` block (just before `</script>`) append:

```javascript
  (function() {
    async function pollRestartPending() {
      try {
        const r = await fetch('/api/settings/restart-pending', { cache: 'no-store' });
        if (!r.ok) return;
        const j = await r.json();
        document.getElementById('restart-banner').classList.toggle('show', !!j.pending);
      } catch (_) { /* swallow */ }
    }
    pollRestartPending();
    setInterval(pollRestartPending, 10000);
  })();
```

- [ ] **Step 4: Build + commit**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet build src/Platform/WinMcp.Platform.csproj -c Release -nologo 2>&1 | tail -6
```

```bash
git add src/Platform/Web/IndexPage.cs
git -c user.name="Ryan Hebert" -c user.email="ryan.hebert@gmail.com" \
    commit -m "feat(web): IndexPage — global restart-pending banner"
```

---

### Task 12: RouteRegistration wiring + /settings page route

**Files:**
- Modify: `src/Platform/Hosting/RouteRegistration.cs`

Mounts the settings page + API endpoints. Adds the new paths to the cache-control no-store list.

- [ ] **Step 1: Add the settings page route**

Find the section in `RouteRegistration.cs` that maps `/info`, `/health`, etc. (search for `MapInfo(app,`). After `UpgradeOrchestrator.MapEndpoints(app);` add:

```csharp
        MapSettingsPage(app);
        SettingsApi.MapEndpoints(app);
```

Then append a `MapSettingsPage` method to the class:

```csharp
    private static void MapSettingsPage(WebApplication app)
    {
        app.MapGet("/settings", (HttpContext ctx) =>
        {
            var config = ctx.RequestServices.GetRequiredService<PlatformConfig>();
            var snapshot = SettingsApi.ToSnapshot(config);
            var html = SettingsPage.Render(new Web.SettingsPageModel(snapshot));
            return Results.Content(html, "text/html; charset=utf-8");
        });
    }
```

If `SettingsApi.ToSnapshot` is internal-accessible (same assembly) the call works as-is. Otherwise mark it `public static`.

- [ ] **Step 2: Add /settings + /api/settings to the no-store cache list**

Find the existing cache-control middleware (search for `Cache-Control: no-store`):

```csharp
            if (p == "/" ||
                p.StartsWith("/info", StringComparison.Ordinal) ||
                p.StartsWith("/health", StringComparison.Ordinal) ||
                p.StartsWith("/requests", StringComparison.Ordinal) ||
                p.StartsWith("/logs", StringComparison.Ordinal) ||
                p.StartsWith("/upgrade", StringComparison.Ordinal))
```

Add two more conditions:

```csharp
            if (p == "/" ||
                p.StartsWith("/info", StringComparison.Ordinal) ||
                p.StartsWith("/health", StringComparison.Ordinal) ||
                p.StartsWith("/requests", StringComparison.Ordinal) ||
                p.StartsWith("/logs", StringComparison.Ordinal) ||
                p.StartsWith("/upgrade", StringComparison.Ordinal) ||
                p.StartsWith("/settings", StringComparison.Ordinal) ||
                p.StartsWith("/api/settings", StringComparison.Ordinal))
```

- [ ] **Step 3: Add the new endpoints to the README + IndexPage endpoints list**

Find the Endpoints card in `src/Platform/Web/IndexPage.cs` (in `RenderEndpointsCard`). Add to the list:

```csharp
            "<a class=\"endpoint\" href=\"/settings\"><span class=\"method\">GET</span>/settings<span class=\"desc\">Editable identity providers + admin auth + MCP default auth</span></a>",
```

Add the same line to the README's Endpoints table (Platform endpoints section), right above the `/upgrade` row:

```
| `/settings` | GET | Editable settings page (identity providers, admin auth, MCP default auth) |
| `/api/settings/*` | GET / PUT / POST / DELETE | REST endpoints backing the settings page |
```

- [ ] **Step 4: Full build + tests**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet build /home/ryan/ai/WinMCP/WinMCP.sln -c Release -nologo 2>&1 | tail -8
PATH=/home/ryan/.dotnet:$PATH dotnet test tests/Platform.Tests/Platform.Tests.csproj --nologo 2>&1 | tail -6
```

Expected: 0 build errors. All unit tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Platform/Hosting/RouteRegistration.cs src/Platform/Web/IndexPage.cs README.md
git -c user.name="Ryan Hebert" -c user.email="ryan.hebert@gmail.com" \
    commit -m "feat(hosting): wire /settings page + /api/settings endpoints into RouteRegistration"
```

---

### Task 13: CHANGELOG + BACKLOG + push

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `docs/BACKLOG.md`

- [ ] **Step 1: CHANGELOG entry**

Replace the `## [Unreleased]` block in `CHANGELOG.md` (currently `_(no entries yet)_`) with:

```markdown
## [Unreleased]

### Added
- Editable Settings page at `/settings` with three tabs:
  - **Identity providers** — CRUD with synchronous OIDC discovery on save.
    Re-discover button per provider for endpoint-rotation cases.
  - **Admin auth** — mode picker (`none` / `demo` / `oidc`). `demo` is
    disabled with helper text; `oidc` shows the v1.0 lockout warning and
    requires explicit confirmation.
  - **MCP default auth** — mode picker + provider dropdown + required
    scopes.
- REST API at `/api/settings/*` backing the page:
  `GET /api/settings`, `GET /api/settings/restart-pending`,
  `POST/PUT/DELETE/.../rediscover` on `oidc-providers`, `PUT` on
  `admin-auth` and `mcp-auth`, `POST /api/settings/restart`.
- `RestartCoordinator` — in-process pending-restart flag + helper-batch
  spawner. Every successful save sets the flag; the dashboard renders a
  global banner across `/` and `/settings`; clicking Restart now spawns
  a detached `.cmd` that stops + starts the service.
- Strict server-side validation in `SettingsValidator` with xUnit coverage
  in a new `tests/Platform.Tests/` project (~30 cases): provider name /
  issuer / audience shapes; auth-domain shape + provider-ref resolution;
  orphan rejection on provider delete.
- Lockout-safety machinery for admin auth → `oidc`: server logs WARN +
  client requires confirmation, since v1.0's OIDC validator returns 503.

### Notes
- Per-module auth override editing remains config.json-only; deferred to
  the next slice.
- Live-reload (no restart required) added to BACKLOG.
```

- [ ] **Step 2: BACKLOG entry**

Append a new section to `docs/BACKLOG.md`'s Auth section:

```markdown
### Live-reload of auth validators
Today (post-Settings-page) saved auth changes set an in-process
restart-pending flag and require a service restart to take effect.
Live-reload would rebuild `AuthValidatorFactory`'s cached validators
in-place when config changes — no restart, no impact on other
modules. Requires plumbing dynamic config lookup through the
middleware closures wired up in P4c (`UseMiddleware<AuthMiddleware>(validator)`
captures the validator instance at startup). Possible options: a config-version
counter checked per request, swap-on-write of the singleton factory,
or a small per-request resolver delegate. Deserves its own design.
```

- [ ] **Step 3: Final build + tests**

```bash
PATH=/home/ryan/.dotnet:$PATH dotnet build /home/ryan/ai/WinMCP/WinMCP.sln -c Release -nologo 2>&1 | tail -6
PATH=/home/ryan/.dotnet:$PATH dotnet test tests/Platform.Tests/Platform.Tests.csproj --nologo 2>&1 | tail -6
```

- [ ] **Step 4: Commit + push**

```bash
git add CHANGELOG.md docs/BACKLOG.md
git -c user.name="Ryan Hebert" -c user.email="ryan.hebert@gmail.com" \
    commit -m "docs: CHANGELOG entry + live-reload BACKLOG entry for admin Settings"
git push origin main
```

---

## Manual smoke checklist (post-merge, Windows test box only)

These can't run on the Linux dev host; record execution on the Windows VM that the existing release smoke uses.

1. Fresh install via `WinMCP.exe` → navigate to `http://localhost:52080/settings`. Page loads, three tabs render, all empty states correct.
2. Add provider with a reachable issuer (e.g. `https://accounts.google.com`) → 201, provider appears with green "Discovered" timestamp, restart banner appears globally on `/` and `/settings`.
3. Add provider with `https://example.invalid` (unreachable) → 422 inline error, provider does NOT appear, no restart pending.
4. Click Re-discover on the working provider → status timestamp updates; restart banner does NOT change.
5. Switch admin auth to `oidc` → see strong inline warning. Save → confirm dialog appears; cancel → no change.
6. Save → restart-pending banner persists across `/` and `/settings`.
7. Click Restart now on either page → banner shows "Restarting…", page auto-reloads when `/info` responds (~10s).
8. After reload, attempt to navigate to `/` with admin auth = `oidc` → 503 (expected lockout). Recover by editing `C:\Program Files\WinMCP\config.json` on the host, set admin.auth.mode back to `none`, `sc stop WinMcp && sc start WinMcp`.
9. Try to delete a provider currently referenced by mcp auth → 409 with `inUseBy: ["mcp"]`. Change mcp auth to `none`, delete again → 204.

---

## Self-review

**Spec coverage:**

- Goals 1-4: ✓ (Tasks 6-10 build the three tabs + REST + restart workflow; Task 8 enforces lockout-safety server-side; Tasks 2-4 cover strict validation).
- Non-goals: respected. Per-module override / live-reload / port editing all explicitly out.
- URL surface (9 endpoints): ✓ all mapped across Tasks 6, 7, 8, 9.
- Page layout (3 tabs, restart banner, empty states): ✓ Task 10.
- Restart workflow: ✓ Tasks 5 (coordinator), 9 (endpoint), 10/11 (UI).
- Validation: ✓ Tasks 2-4 (xUnit-covered).
- OIDC discovery sync-on-save: ✓ Task 7.
- Code shape: matches spec exactly. SettingsApi.cs + RestartCoordinator.cs in Hosting/, SettingsValidator.cs in Config/, SettingsPage.cs in Web/.
- Error handling: covered case-by-case in Task 7/8 (422 discovery, 409 orphan, 400 invalid, 500 not specifically tested but ConfigLoader.Save errors propagate naturally).
- Testing: ✓ Task 1-4 set up tests/Platform.Tests/ with SettingsValidator unit tests. Manual smoke checklist preserved.
- Lockout-safety: ✓ client confirm dialog (Task 10), server-side WARN log (Task 8). Strong inline warning rendered in the form (Task 10).

**Placeholder scan:** no TBD/TODO/fill-in-later remaining in steps. All code blocks are concrete.

**Type consistency:** `SettingsSnapshotDto` (Task 6), `AuthDomainDto` (Task 6), `OidcProviderDto` (Task 6) are referenced by `SettingsPageModel` (Task 10) and `MapSettingsPage` (Task 12) with matching names. `ValidationResult.Fail("name", ...)` field name matches what the orphan tests in Task 4 assert. `RestartCoordinator.MarkPending()` / `IsPending` / `PendingSince` / `TriggerRestart` all consistent across Tasks 5, 6, 7, 8, 9.

**Ambiguity:** the `ToSnapshot` accessibility note in Task 12 explicitly flags the possible internal/public adjustment. The `_providers` field-vs-readonly note in Task 7 covers the OidcProviderRegistry edit.
