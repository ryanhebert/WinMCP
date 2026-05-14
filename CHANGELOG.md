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

### Notes
- SDK is alpha; API surface may change before 1.0.0 stable. Module authors
  using the alpha should pin to the exact version.
- First tagged release of the platform will be `platform-v1.0.0`.
- First tagged release of the SDK will be `sdk-v1.0.0-alpha.1` (this build).
