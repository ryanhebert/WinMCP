# WinMCP

A multi-module MCP (Model Context Protocol) server platform for Windows.

WinMCP runs as a Windows Service and hosts pluggable **modules** — each module exposes its own MCP server at `/<module>/mcp` with its own tools, prompts, and resources. The platform provides the shared infrastructure: HTTPS, authentication, dashboard, observability, install/upgrade flow, certificate management.

Modules are distributed independently as GitHub-released `.zip` artifacts; official modules live in [WinMCP-Modules](https://github.com/ryanhebert/WinMCP-Modules). Third parties can build their own modules against the [WinMcp.ModuleSdk](https://www.nuget.org/packages/WinMcp.ModuleSdk) NuGet package and distribute them via any GitHub repository.

## Status

Pre-1.0 — under active development. See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design and [docs/BACKLOG.md](docs/BACKLOG.md) for the roadmap.

## Requirements

- Windows Server 2016+ or Windows 10 1607+
- Administrator privileges for install/uninstall
- Free TCP ports 52080 and 52443 (configurable post-install)

## Install (once v1.0.0 ships)

1. Download the latest `WinMCP.exe` from the [releases page](https://github.com/ryanhebert/WinMCP/releases/latest).
2. Double-click. The `requireAdministrator` manifest triggers UAC; accept it.

The installer registers the `WinMcp` Windows Service, generates a self-signed cert, writes default config, opens firewall rules, and pre-installs the Math module so you can hit `/math/mcp` immediately.

## Endpoints

Platform endpoints (always present):

| Path | Method | Purpose |
|---|---|---|
| `/` | GET | Dashboard (HTML) |
| `/info` | GET | Platform + module metadata (JSON) |
| `/health` | GET | Health probe |
| `/logs` | GET | Live log viewer |
| `/logs/tail` | GET | Plain-text log tail |
| `/requests` | GET | Recent MCP requests (JSON) |
| `/cert.cer` / `/cert.pem` | GET | TLS cert download |
| `/token` | POST | OAuth2 client_credentials (when MCP auth mode = `demo`) |
| `/upgrade` | POST | In-place platform upgrade |

Module endpoints (per loaded module):

| Path | Method | Purpose |
|---|---|---|
| `/<module>/mcp` | POST / GET / DELETE | Streamable HTTP MCP transport |

## Building a module

See [WinMCP-Modules](https://github.com/ryanhebert/WinMCP-Modules) for the developer guide. Short version: depend on `WinMcp.ModuleSdk`, implement `IMcpModule`, ship a DLL + `module.json` in a GitHub release.

## Contributing

This project welcomes contributions. See [CONTRIBUTING.md](CONTRIBUTING.md). All contributors must sign the project [CLA](docs/CLA.md) before their first PR can be merged — automated by [cla-assistant.io](https://cla-assistant.io).

## License

Apache 2.0 — see [LICENSE](LICENSE).

## Security

Report vulnerabilities privately per [SECURITY.md](SECURITY.md). Do not file public issues.
