# Changelog

All notable changes to the WinMCP platform.

When a release ships, move "Unreleased" entries into a new `## [vX.Y.Z] — YYYY-MM-DD` section.

## [Unreleased]

_(no entries yet)_

## [v1.0.0] — 2026-05-15

First tagged platform release. Bundles auth, module loading, configuration, hosting, dashboard, logs viewer, well-known endpoints, and in-place platform + per-module upgrade.

### Added
- `WinMcp.ModuleSdk` 1.0.0-alpha.1 NuGet package
  - `IMcpModule` interface (module contract)
  - `ModuleMetadata` (identity record)
  - `ModuleConfigurationContext` (per-module configuration vehicle)
  - `IPlatformInfo` (read-only platform metadata available to modules)
  - `AuthMode` enum (None | Demo | Oidc)
  - `[McpModule]` attribute (reserved for future entry-type scanning)
- Auth subsystem: `IAuthValidator` + `NoneValidator` / `DemoValidator` / `OidcValidator`, `AuthMiddleware`, `AuthValidatorFactory`, `TokenStore`, `CredentialGenerator`, OIDC provider registry + discovery scaffold (real OIDC validation lands v1.1).
- Module subsystem: strict `module.json` parser (name regex, SemVer, reserved-path guards), per-module `AssemblyLoadContext` for dependency isolation, deterministic load order, manifest-validated entry-type resolution.
- Configuration: `PlatformConfig` schema (admin auth, mcp default auth, OIDC providers, per-module overrides, upgrade), atomic-write `config.json` loader, port-range validation.
- Hosting: `ServiceHost` composes auth + module loader + config + per-module routing with URL-scoped MCP filter; `Installer` (Install / Uninstall / RotateCreds verbs) idempotent on re-install; `ServiceControl` SCM wrappers with parallel stdout/stderr reads; `FirewallSetup` netsh add/remove; `ProcessSweep` for stray-process cleanup; `CertificateProvider` auto-renew-on-startup.
- Request log: in-memory ring buffer (last 50), module-aware; structured Serilog INFO line per `/<module>/mcp` request with method + status + duration; session-not-found labelling.
- Dashboard at `/` — multi-module overview with per-module rows (mount path, maturity + auth badges, tool/prompt/resource chips), recent-MCP-requests table with module column, listening/cert card, platform demo-credentials card, GitHub-backed update banner.
- Live log viewer at `/logs` with date picker, level + source filters, raw vs enhanced modes, click-to-filter on host/source, auto-pause on scroll. Backed by `/logs/tail` (tail-N lines via seek-from-EOF) and `/logs/dates`.
- `/favicon.svg` and `/favicon.ico` (inline SVG, no static-asset dep).
- `/.well-known/oauth-authorization-server` + `/.well-known/openid-configuration` served when any module uses demo auth (RFC 8414).
- `/.well-known/oauth-protected-resource/{module}/mcp` — per-resource metadata (RFC 9728 §3.1); `DemoValidator` emits a path-scoped `resource_metadata=` URL so multi-module installs let clients tell modules apart.
- Port-80 gating: when WinMCP binds :80, only `/token` answers there; every other request gets a 404 pointing at the configured HTTP/HTTPS ports.
- Cache-Control `no-store` on `/`, `/info`, `/health`, `/requests`, `/logs*`, `/upgrade*` so a tab left open across an upgrade doesn't show stale data.
- In-place platform upgrade: `POST /upgrade` streams a newer `WinMCP.exe` from GitHub Releases, PE-verifies it, writes a helper `.cmd` that stops the service, waits for the process to exit, swaps the binary (with retry to ride out antivirus locks), and asks SCM to start the service back. Returns 202 immediately; progress lives at `GET /upgrade/status` and on the dashboard's upgrade modal.
- Dashboard "Upgrade now" button + terminal-style progress modal.
- Startup scrubs stale upgrade artefacts (`WinMCP.exe.new`, `upgrade-helper.cmd`) and surfaces a WARN log if an `upgrade-failed.txt` marker is present.
- `updateSource` manifest field: per-module `{ type: "github-releases", repo: "owner/repo", asset: "name-{version}.zip" }`. Modules carrying this field gain an in-UI Upgrade button. Both `{version}` (with module-prefix-strip) and `{tag}` (verbatim) placeholders supported.
- In-place module upgrade: `POST /upgrade/module/{name}` resolves the latest release tag via the GitHub API, streams the asset zip, PK-verifies it, extracts into `<modules>/<name>.staging/`, validates the staged `module.json` (name match, minPlatformVersion compat, version differs), writes a helper `.cmd` that stops the service, removes the live module dir, moves staging into place (with retry), and starts the service back. No-op short-circuit when the staged version equals the running version. Shares the platform upgrade's in-flight lock + 5-minute watchdog.
- `/upgrade/status` carries `kind` (platform|module) and `module` fields; status state machine shared across both upgrade flows.
- Startup cleanup also scrubs `<name>.staging` dirs, `module-*.zip` staging zips, `module-upgrade-helper.cmd`, and surfaces any `module-upgrade-<name>-failed.txt` markers.
- `.github/workflows/release.yml` — tag-driven release: on `v*.*.*` push, verifies tag matches csproj `<Version>`, publishes self-contained single-file `win-x64`, uploads `WinMCP.exe` (latest alias) + `WinMCP-<tag>.exe` to a GitHub Release with notes drawn from `[Unreleased]`.

### Notes
- SDK ships as `1.0.0-alpha.1` for the initial release. API surface may change before SDK 1.0.0 stable; module authors using the alpha should pin to the exact version.
- Initial repo scaffolding (LICENSE, README, CONTRIBUTING, CoC, SECURITY, ARCHITECTURE, BACKLOG, CLA) and the `WinMCP.sln` solution file are not enumerated separately above.
