# WinMCP Architecture

This document captures the design decisions that shape WinMCP v1.0. It's intended as a reference for contributors and as a record of why things are the way they are.

## What WinMCP is

A Windows Service that hosts multiple MCP (Model Context Protocol) servers as pluggable in-process modules. The platform owns the shared infrastructure — HTTPS, certificate management, authentication, dashboard, observability, install/upgrade. Each module owns its own MCP tools, prompts, and resources, exposed at its own URL prefix.

## What WinMCP is not (v1.0)

- Not a multi-process orchestrator. Modules load into the same .NET process. A bad module can affect the host. We accept that trade for simplicity in v1.0; the path to per-module process isolation is open for a future major version.
- Not polyglot. Modules are .NET assemblies that depend on the `WinMcp.ModuleSdk` NuGet package. Adding non-.NET module support would require the multi-process model.
- Not multi-tenant. One instance serves one organization.
- Not highly available. Single Windows Service per machine. Clustering is not in scope.

## Process model

Single process, in-process modules. At service startup, the `ModuleLoader` scans `<InstallDir>\modules\<name>\` for module manifests, loads each module's assembly into its own `AssemblyLoadContext` (isolating its dependencies), instantiates the `IMcpModule` implementation, and calls `Configure()` to register its MCP tools/prompts/resources. Each module's MCP transport is mounted at `/<module>/mcp` via the MCP SDK's `MapMcp` extension.

## URL layout

Platform endpoints live at the root. Module endpoints live under their module-named prefix. The platform never exposes its own `/mcp` — that path is reserved for module routing only.

| Path | Owner | Notes |
|---|---|---|
| `/`, `/info`, `/health`, `/logs`, `/logs/tail`, `/requests`, `/cert.cer`, `/cert.pem`, `/token`, `/upgrade`, `/.well-known/*` | Platform | Operator-facing |
| `/<module>/mcp` | Each module | Streamable HTTP MCP transport |
| `/<module>/info`, `/<module>/health` | Each module (optional, future) | Per-module metadata |

## Auth domains

Two independent auth domains with three modes each.

### Domain 1 — Admin auth

Protects every platform-managed surface: dashboard, `/info`, `/health`, `/logs`, `/upgrade`, etc. One config block in `config.json` under `admin.auth`.

Modes:
- `none` — no auth. Default for v1.0 fresh install.
- `oidc` — JWT validated against a configured OIDC provider (schema accepted in v1.0; implementation lands in v1.1).

### Domain 2 — MCP auth

Protects each module's `/<module>/mcp` endpoint. One default in `config.json` under `mcp.defaultAuth`; modules may override via their own manifest or via operator-set overrides in `config.json`.

Modes:
- `none` — no auth.
- `demo` — platform-issued static bearer + OAuth2 `client_credentials` flow. Credentials are public, visible on the dashboard. Intended for integrators testing MCP auth flows.
- `oidc` — JWT validated against a configured OIDC provider (schema accepted in v1.0; implementation lands in v1.1).

### Identity providers

OIDC providers are defined once under `oidcProviders` in `config.json` and referenced by name from both `admin.auth.providerRef` and `mcp.defaultAuth.providerRef`. Auto-discovery from `<issuer>/.well-known/openid-configuration` populates the rest.

### Per-module auth override

Each module's `module.json` may declare an `auth` field. At load time, the platform resolves the module's *effective* auth config as:
- `null` or missing → inherit `mcp.defaultAuth`
- explicit value → use the module's override

Operators can further override via `modules.<name>.authOverride` in `config.json`. Module manifest is the author's recommendation; platform config is the operator's final word.

Safety rail: in v1.1, modules declaring auth modes more permissive than the platform default will require an explicit `allowAuthDowngrade: true` flag on the per-module override.

## Module loading

```
<InstallDir>/modules/<name>/
  ├─ module.json          # manifest
  └─ <name>Module.dll     # assembly with IMcpModule implementation
```

Manifest fields shipping in v1.0: `name`, `version`, `displayName`, `description`, `assembly`, `entryType`, `mountPath`, `minPlatformVersion`, `maturity`, `publisher`, `homepage`, `license`, `tags`.

Manifest fields accepted but unenforced in v1.0 (reserved for v1.1+): `auth`, `requiredEntitlements`, `minMcpProtocolVersion`, `signature`.

The platform validates each manifest against a strict schema at load time. Unknown top-level keys are logged as warnings (forward-compat); known keys with wrong types fail the load with a clear error.

## Configuration

`config.json` lives in `<InstallDir>\config.json`. Atomic write (`.tmp` + rename) per `Config.Save`. Schema is operator-managed; module authors don't write to it.

The platform exposes its current config (with secrets masked) via `/info`. The dashboard's Settings pages (v1.1+) read and write `config.json` directly.

## Observability

- Serilog file logging to `<InstallDir>\logs\`, rolling daily, 30-day retention
- Windows Event Log source: `WinMcp`
- In-memory ring buffer of last 50 MCP requests, exposed via `/requests` (JSON) and `/` (dashboard table)
- Live log viewer at `/logs` with level filters, pause, search
- Plain-text log API at `/logs/tail` for external tools
- Per-session start/end lines, per-request method + duration + status (carryover from math-mcp v1.0.23)
- v1.1+: structured event fields, ActivitySource hooks for OpenTelemetry export

## Upgrade flow

In-UI upgrade carries over from math-mcp v1.0.24. Two scopes:
- **Platform upgrade**: dashboard's "Upgrade now" button downloads the latest WinMCP release, stages it, runs a helper batch that swaps the binary and restarts the service.
- **Module upgrade**: per-module "Upgrade" action downloads the module's latest release zip, drops it into `<InstallDir>\modules\<name>\`, restarts the service.

Module upgrades and platform upgrades are independent — module v1.2.0 works against platform v1.0.0 as long as the module's `minPlatformVersion` is satisfied.

## What's deferred to v1.1+

- Real OIDC validator (JWKS fetching, JWT signature verification)
- Admin auth `oidc` mode
- Dashboard Settings pages (Admin auth, MCP default auth, Identity providers)
- Per-module override editing via UI (v1.0 only supports manifest-declared and config-file overrides)
- Module install/uninstall via dashboard (v1.0 requires manual drop-in + service restart)
- Module maturity safety rails (refuse to load `demo`-maturity modules when MCP auth mode is `oidc`)
- Module signing (Authenticode + manifest signature field)
- OpenTelemetry export

See `docs/BACKLOG.md` for the full deferred list.
