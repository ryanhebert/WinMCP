# WinMCP

A multi-module MCP (Model Context Protocol) server platform for Windows. Runs as a Windows Service and hosts pluggable **modules** — each module exposes its own MCP server at `/<module>/mcp` with its own tools, prompts, and resources. The platform provides shared infrastructure: HTTPS, authentication, dashboard, observability, in-place upgrades, certificate management.

Distributed as a single self-contained `WinMCP.exe`. No .NET runtime install required on the target.

## Requirements

- Windows Server 2016+ or Windows 10 1607+
- Administrator privileges for install / uninstall
- Free TCP ports 52080 and 52443 (configurable post-install in `config.json`)

## Install

1. **Download the latest `WinMCP.exe`:** <https://github.com/ryanhebert/WinMCP/releases/latest/download/WinMCP.exe>

    Past versions and changelogs: <https://github.com/ryanhebert/WinMCP/releases>
2. Double-click. The `.exe` has the `requireAdministrator` manifest, so Windows shows the UAC prompt automatically. Accept it.

That's it. The installer:

- Stops and removes any existing `WinMcp` service (idempotent — safe to re-run for upgrades)
- Copies itself to `C:\Program Files\WinMCP\` and strips Mark-of-the-Web from the copy
- Generates a self-signed cert (CN/SAN = `localhost`, 1-year validity) at `certs\cert.pfx` on first install
- Writes default `config.json` (preserved on re-install)
- Adds Windows Firewall inbound TCP rules for the configured HTTP/HTTPS ports
- Registers and starts the `WinMcp` Windows Service (auto-start on boot, runs as virtual account `NT SERVICE\WinMcp`)
- Waits for the service to reach `RUNNING` and prints the endpoint URLs

The service binds to `0.0.0.0`, so it's reachable from the network. If port 80 is free at service startup, an additional listener is bound there and exposes `/token` only — useful for OAuth2 clients that can't deal with self-signed certs.

The platform ships with zero modules pre-installed. Add them as a separate step (next section).

## Adding modules

Modules are distributed independently as GitHub-released `.zip` artifacts. To install a module:

1. Stop the service: `sc stop WinMcp`
2. Download the module's release zip and extract it into `C:\Program Files\WinMCP\modules\<name>\`. The directory name must match the `name` field inside `module.json`.
3. Start the service: `sc start WinMcp`

The dashboard's Modules card will show the new module with its mount path, maturity, and tools/prompts/resources. The MCP transport is reachable at `/<name>/mcp`.

Official modules live in [WinMCP-Modules](https://github.com/ryanhebert/WinMCP-Modules). Third parties can build their own against the [WinMcp.ModuleSdk](https://www.nuget.org/packages/WinMcp.ModuleSdk) NuGet package and distribute via any GitHub repository — see the [module-author guide](https://github.com/ryanhebert/WinMCP-Modules/blob/main/docs/BUILDING-A-MODULE.md).

A module that declares an `updateSource` block in its `module.json` will also expose an in-UI **Upgrade ↑** button on the dashboard once installed; the platform handles download + verification + folder swap + service restart without further operator action.

## Endpoints

Platform endpoints (always present, served on every configured listener):

| Path | Method | Purpose |
|---|---|---|
| `/` | GET | Dashboard (HTML) — overview, modules, recent requests, demo credentials |
| `/info` | GET | Platform + module metadata (JSON) |
| `/health` | GET | Health probe |
| `/logs` | GET | Live log viewer (HTML) |
| `/logs/tail` | GET | Plain-text log tail (`?n=500`, `?date=YYYY-MM-DD`) |
| `/logs/dates` | GET | Available historical log dates (JSON) |
| `/requests` | GET | Recent MCP requests (JSON) |
| `/cert.cer` / `/cert.pem` | GET | TLS cert in DER / PEM |
| `/token` | POST | OAuth2 `client_credentials` → bearer (when MCP auth mode = `demo`) |
| `/.well-known/oauth-authorization-server` | GET | RFC 8414 server metadata (when demo auth is on) |
| `/.well-known/oauth-protected-resource/<module>/mcp` | GET | RFC 9728 per-resource metadata |
| `/settings` | GET | Editable settings page (identity providers, admin auth, MCP default auth) |
| `/api/settings/*` | GET / PUT / POST / DELETE | REST endpoints backing the settings page |
| `/upgrade` | POST | In-place platform upgrade (downloads new `WinMCP.exe`, swaps, restarts) |
| `/upgrade/status` | GET | Upgrade pipeline status (JSON) |
| `/upgrade/module/<name>` | POST | In-place module upgrade (downloads zip, swaps folder, restarts) |

Module endpoints (per loaded module):

| Path | Method | Purpose |
|---|---|---|
| `/<module>/mcp` | POST / GET / DELETE | Streamable HTTP MCP transport for that module |

All endpoints are served on both `http://<host>:52080` and `https://<host>:52443`. Remote clients connecting by IP or hostname will see a TLS hostname-mismatch warning (the cert is only valid for `localhost`); skip TLS verification in your MCP client or use the plain-HTTP listener.

Example Claude Desktop / Claude Code config for a `math` module:

```json
{
  "mcpServers": {
    "math": { "url": "http://<host>:52080/math/mcp" }
  }
}
```

## Authentication

Two independent auth domains, each with three modes (`none` / `demo` / `oidc`):

- **Admin auth** gates the platform-managed endpoints (`/`, `/info`, `/logs`, `/upgrade`, …). Defaults to `none`. OIDC schema is accepted in v1.0; real OIDC validation lands in v1.1.
- **MCP auth** gates each module's `/<module>/mcp` endpoint. Default is set under `mcp.defaultAuth` in `config.json` and applies to every module that doesn't override it. Per-module overrides live in either the module's `module.json` (`auth` block) or in `config.json` (`modules.<name>.authOverride`).

When MCP auth mode is `demo`, the platform issues a static bearer token + an OAuth2 `client_id` / `client_secret` pair. The credentials are **intentionally public** — they show up on the dashboard and in `/info` so integrators can copy them out for testing. The point of `demo` is to surface working test credentials and the `/token` flow against the same server, not to enforce real auth.

Enable demo mode at install time:

```cmd
WinMCP.exe --auth
```

Three values get printed once; re-running installer preserves them. To rotate:

```cmd
WinMCP.exe rotate-creds
```

To disable on a re-install:

```cmd
WinMCP.exe --auth off
```

## Configuration

Defaults are written to `C:\Program Files\WinMCP\config.json`:

```json
{
  "httpPort": 52080,
  "httpsPort": 52443,
  "logLevel": "Information",
  "admin": { "auth": { "mode": "none" } },
  "mcp":   { "defaultAuth": { "mode": "none" } },
  "oidcProviders": {},
  "modules": {},
  "upgrade": { "enabled": true }
}
```

Edit and restart the service to apply (`sc stop WinMcp && sc start WinMcp`). Re-running the installer preserves your edits.

Per-module operator overrides live under `modules.<name>`:

```json
"modules": {
  "math": {
    "enabled": true,
    "authOverride": { "mode": "demo" },
    "moduleConfig": { /* opaque per-module settings, passed to IMcpModule.Configure */ }
  }
}
```

## Logs

- Windows Event Log source: `WinMcp` (Application log)
- File log: `C:\Program Files\WinMCP\logs\winmcp-YYYYMMDD.log` (rolling daily, 30-day retention)
- Live web viewer at `http://<host>:52080/logs` — auto-refreshes every 3s; level filters; click any source or origin badge to drill down; auto-pauses on scroll-up; Enhanced (default) vs Raw view; Newest-first vs Oldest-first ordering; date picker for historical days

Each `/<module>/mcp` request emits one structured `WinMcp.Platform.RequestLogMiddleware` line capturing module, host, IP, method, status, and duration.

## Upgrades

**Platform**: the dashboard's **Upgrade now** button downloads the latest `WinMCP.exe` from GitHub Releases, PE-verifies it, writes a helper batch that stops the service / swaps the binary / starts the service back. Brief (~10–30s) outage on all modules.

**Modules**: each module declaring an `updateSource` in its manifest gets a per-module **Upgrade ↑** button on its dashboard row. The platform downloads the module's zip from the declared GitHub Releases, extracts to a staging dir, validates the staged `module.json` (name, version, minPlatformVersion), and swaps the folder during a service restart.

Both flows share an in-flight lock; only one upgrade can run at a time. Failure leaves staging artefacts in place + writes a marker file so operators can investigate, and starts the old binary back so the service never dies silent.

## Uninstall

```cmd
WinMCP.exe uninstall
```

Run from outside `C:\Program Files\WinMCP\` so the running `.exe` isn't itself in the directory being deleted. Requires admin; UAC will prompt.

Stops the service, removes the service registration, deletes the install directory (cert + config + logs + exe + modules).

## Other commands

| Command | Effect |
|---|---|
| `WinMCP.exe` | Install (silent; requires admin) |
| `WinMCP.exe --auth` | Install with demo auth (generates credentials) |
| `WinMCP.exe --auth off` | Reinstall with auth disabled |
| `WinMCP.exe --http-port N --https-port N` | Install with custom ports (writes to `config.json`) |
| `WinMCP.exe rotate-creds` | Regenerate demo credentials and restart (admin) |
| `WinMCP.exe uninstall` | Uninstall |
| `WinMCP.exe run` | Run in foreground (debugging — bypasses service host) |
| `WinMCP.exe --version` | Print version and exit |
| `WinMCP.exe --help` | Show usage |

## Service details

- Runs as virtual service account `NT SERVICE\WinMcp` (least-privilege; not LocalSystem)
- Auto-starts on boot
- Auto-restarts on crash (3 retries, 5 seconds apart, failure count resets after 60s healthy)
- Event Log source: `WinMcp` (Application log)
- Rolling daily file logs at `C:\Program Files\WinMCP\logs\` with 30-day retention

## Building from source

Requires .NET 8 SDK.

```sh
dotnet publish src/Platform/WinMcp.Platform.csproj -c Release -r win-x64 \
    --self-contained -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true
```

Output: `src/Platform/bin/Release/net8.0/win-x64/publish/WinMCP.exe`.

To publish the Module SDK NuGet package:

```sh
dotnet pack src/ModuleSdk/WinMcp.ModuleSdk.csproj -c Release
```

## Design and roadmap

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design (process model, URL layout, auth domains, module loading, upgrade flow) and [docs/BACKLOG.md](docs/BACKLOG.md) for v1.1+ work.

## Contributing

Contributions welcome. See [CONTRIBUTING.md](CONTRIBUTING.md). All contributors must sign the project [CLA](docs/CLA.md) before their first PR can be merged — automated by [cla-assistant.io](https://cla-assistant.io).

## License

Apache 2.0 — see [LICENSE](LICENSE).

## Security

Report vulnerabilities privately per [SECURITY.md](SECURITY.md). Do not file public issues.
