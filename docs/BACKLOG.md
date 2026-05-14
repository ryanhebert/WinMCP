# WinMCP Backlog

Deferred work, organized by category. Items here are intentionally postponed — valid but not in the current release scope. Pull from here when planning new work.

When an item ships, delete it from this file and document it in `CHANGELOG.md`.

---

## Auth — v1.1 targets

### Real OIDC validator
`OidcValidator` in v1.0 returns 503 for any protected endpoint. v1.1 implements JWKS fetching with caching, JWT signature verification, audience/issuer/expiry validation, and claim mapping per the configured provider.

### Admin auth `oidc` mode
The schema already accepts it; the middleware needs to actually validate JWTs and gate the admin endpoints. Dashboard sees an "auth required" splash with a "Sign in with <provider>" button.

### Dashboard Settings pages
Editable UI for Admin auth, MCP default auth, and Identity providers. Today these are config-file-only. v1.1 adds:
- Settings → Identity providers (add via auto-discovery)
- Settings → Admin auth (mode + provider picker)
- Settings → MCP default auth (mode + provider + required scopes)

### Per-module auth override editing
Module Auth tab in the dashboard with `[Override]` / `[Reset to default]` buttons. Writes to `config.json`'s `modules.<name>.authOverride` field.

### Auth-mode safety rails
Refuse to load `demo`-maturity modules when MCP default auth is `oidc`, unless `mcp.allowDemoModulesInProduction: true`. Refuse to apply module overrides that downgrade auth (mode less strict than platform default) without `allowAuthDowngrade: true`.

---

## Modules — v1.1+ targets

### Install / uninstall via dashboard
Today: drop module dir into `<InstallDir>\modules\` and restart the service. v1.1 adds an "Install module" UI that takes a release URL or selects from `WinMCP-Modules/modules.json`, downloads the zip, validates the manifest, extracts to the modules dir, restarts the service.

### Module compatibility matrix
Enforce `minMcpProtocolVersion` and `maxMcpProtocolVersion`. Refuse to load modules whose declared MCP protocol range doesn't intersect the platform's supported range.

### Module signing
Authenticode signature on the module DLL, plus a `signature` field in the manifest (covering manifest + DLL hash). Dashboard shows a "verified publisher" badge when the signature matches a configured trusted-publisher key.

### Hot-swap module upgrade
Unload an `AssemblyLoadContext`, replace the DLL, reload — no service restart. Possible thanks to per-module ALC; deferred because correctness around in-flight requests + open SSE streams needs more design.

---

## Observability — v1.1+ targets

### OpenTelemetry instrumentation
`ActivitySource` for each MCP request, structured event fields for module name / method / duration / status / user identity (when oidc mode is enabled). OTLP exporter behind a config flag.

### Per-module logs / health
`/<module>/info` and `/<module>/health` endpoints surface per-module state without the operator having to hit the platform `/info` for everything.

### Audit log
For `oidc` mode: who invoked what tool, when, with what arguments. Tamper-evident (signed events). Distinct from the request log — survives ring-buffer rotation, retention configurable separately.

---

## CI / Build — v1.1+ targets

### Proxmox-orchestrated Windows runner
Self-hosted GitHub Actions runner inside a Windows Server 2022 VM on Proxmox. Workflow restores a clean snapshot per release, runs the full install / smoke-test / uninstall flow, captures logs, rolls back. Gates the release pipeline.

### Reproducible builds
Pinned dotnet SDK version (already done via `global.json`), pinned tool versions, sealed package restore (`packages.lock.json`). Verify a release built twice produces byte-identical binaries.

### Release signing
Sign the released `WinMCP.exe` with an Authenticode certificate. Windows SmartScreen no longer warns on download.

---

## Commercial / Entitlement — v2.0+ targets

### License key validation
A signed license file in `<InstallDir>` controls which features are enabled. `IEntitlementChecker` (already reserved in the SDK) returns true/false per feature; default OSS impl is `AllowAll`.

### Premium-only modules
Modules can declare `requiredEntitlements: ["enterprise"]` in their manifest. Platform refuses to load them when the entitlement check returns false.

### Enterprise dashboard features
RBAC editing UI, audit log viewer, license management page. Lives in a separate proprietary repo, plugged in via DI.

---

## Documentation — ongoing

### `WinMCP-Docs` repo + Docusaurus site
Replaces piecemeal markdown with a real docs site at `winmcp.io/docs` (or similar). Three top-level sections: Getting started / Build a module / Reference. Auto-generated SDK API reference from XML doc comments.

### Gateway mode (single-server view across all modules)
Optional platform mode that exposes ONE merged MCP server at `/mcp`,
aggregating tools/prompts/resources from every loaded module with their
names prefixed by the module name (e.g., `math.add`, `weather.forecast`).
The per-module `/<module>/mcp` endpoints continue to work in parallel.

Use case: MCP clients that want a single connection / single capability
list across the whole platform — no per-module URL bookkeeping. Trades
off per-module auth granularity (one auth config gates the merged surface)
for client simplicity.

Implementation sketch:
- New config flag `mcp.gatewayMode: { enabled: true, mountPath: "/mcp", toolNameSeparator: "." }`
- When enabled, mount an additional MCP server instance at the
  configured path, registering every module's assembly with a custom
  tool-name transformer that prefixes `<module><separator>`.
- The per-module endpoints stay as the source of truth for module-scoped
  auth; the gateway endpoint uses `mcp.defaultAuth` only.
- Document the naming-collision rule (no module may declare a tool
  whose unprefixed name conflicts with the separator).

Originally considered as the v1.0 implementation choice instead of the
per-module URL filtering shim; deferred because the URL design + auth
override semantics were the higher priority for v1.0. The filtering
shim shipping in v1.0 doesn't preclude this — gateway mode would be
an additive optional second view.

### Cross-platform Linux container
A Linux container distribution that runs WinMCP (or a subset) on docker hosts. Trades the Windows Service shape for a containerized entry point. Useful for CI/test scenarios where standing up a Windows VM is overkill.
