# Changelog

All notable changes to the WinMCP platform.

When a release ships, move "Unreleased" entries into a new `## [vX.Y.Z] — YYYY-MM-DD` section.

## [Unreleased]

### Added
- Initial repo scaffolding (LICENSE, README, CONTRIBUTING, CoC, SECURITY)
- Architecture and backlog docs
- `WinMcp.ModuleSdk` 1.0.0-alpha.1 NuGet package
  - `IMcpModule` interface (module contract)
  - `ModuleMetadata` (identity record)
  - `ModuleConfigurationContext` (per-module configuration vehicle)
  - `IPlatformInfo` (read-only platform metadata available to modules)
  - `AuthMode` enum (None | Demo | Oidc)
  - `[McpModule]` attribute (reserved for future entry-type scanning)
- `WinMCP.sln` solution file
- Dashboard at `/` — multi-module overview with per-module rows
  (mount path, maturity + auth badges, tool/prompt/resource chips),
  recent-MCP-requests table with module column, listening/cert card,
  platform demo-credentials card, GitHub-backed update banner.
- Live log viewer at `/logs` with date picker, level + source filters,
  raw vs enhanced modes, click-to-filter on host/source, auto-pause on
  scroll. Backed by `/logs/tail` (tail-N lines via seek-from-EOF) and
  `/logs/dates`.
- `/favicon.svg` and `/favicon.ico` (inline SVG, no static-asset dep).
- `/.well-known/oauth-authorization-server` + `/.well-known/openid-configuration`
  served when any module uses demo auth (RFC 8414).
- `/.well-known/oauth-protected-resource/{module}/mcp` — per-resource
  metadata (RFC 9728 §3.1); `DemoValidator` now emits a path-scoped
  `resource_metadata=` URL so multi-module installs let clients tell
  modules apart.
- Port-80 gating: when WinMCP binds :80, only `/token` answers there;
  every other request gets a 404 pointing at the configured HTTP/HTTPS
  ports.
- Cache-Control `no-store` on `/`, `/info`, `/health`, `/requests`,
  `/logs*`, `/upgrade*` so a tab left open across an upgrade doesn't
  show stale data.
- In-place platform upgrade: `POST /upgrade` streams a newer
  `WinMCP.exe` from GitHub Releases, PE-verifies it, writes a helper
  `.cmd` that stops the service, waits for the process to exit, swaps
  the binary (with retry to ride out antivirus locks), and asks SCM to
  start the service back. Returns 202 immediately; progress lives at
  `GET /upgrade/status` and on the dashboard's upgrade modal.
- Dashboard "Upgrade now" button + terminal-style progress modal,
  driven by the same JS pipeline math-mcp shipped in v1.0.24.
- Startup scrubs stale upgrade artefacts (`WinMCP.exe.new`,
  `upgrade-helper.cmd`) and surfaces a WARN log if an
  `upgrade-failed.txt` marker is present so operators know to check.
- `updateSource` manifest field: per-module `{ type: "github-releases",
  repo: "owner/repo", asset: "name-{version}.zip" }`. Modules carrying
  this field gain an in-UI Upgrade button.
- In-place module upgrade: `POST /upgrade/module/{name}` resolves the
  latest release tag via the GitHub API, streams the asset zip,
  PK-verifies it, extracts into `<modules>/<name>.staging/`, validates
  the staged `module.json` (name match, minPlatformVersion compat,
  version differs), writes a helper `.cmd` that stops the service,
  removes the live module dir, moves staging into place (with retry),
  and starts the service back. No-op short-circuit when the staged
  version equals the running version. Shares the platform upgrade's
  in-flight lock + 5-minute watchdog.
- `/upgrade/status` extended with `kind` (platform|module) and `module`
  fields so the dashboard renders the right scope; status state machine
  is shared across platform and module upgrades.
- Startup cleanup also scrubs `<name>.staging` dirs, `module-*.zip`
  staging zips, `module-upgrade-helper.cmd`, and surfaces any
  `module-upgrade-<name>-failed.txt` markers.

### Notes
- SDK is alpha; API surface may change before 1.0.0 stable. Module authors
  using the alpha should pin to the exact version.
- First tagged release of the platform will be `platform-v1.0.0`.
- First tagged release of the SDK will be `sdk-v1.0.0-alpha.1` (this build).
