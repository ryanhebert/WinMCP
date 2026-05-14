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
  `/logs*` so a tab left open across an upgrade doesn't show stale data.

### Notes
- SDK is alpha; API surface may change before 1.0.0 stable. Module authors
  using the alpha should pin to the exact version.
- First tagged release of the platform will be `platform-v1.0.0`.
- First tagged release of the SDK will be `sdk-v1.0.0-alpha.1` (this build).
