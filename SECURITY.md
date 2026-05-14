# Security Policy

## Reporting a vulnerability

Please report vulnerabilities privately, **not via public GitHub issues**.

Contact: open a [private security advisory on GitHub](https://github.com/ryanhebert/WinMCP/security/advisories/new), or email the project maintainer directly (see the GitHub profile at <https://github.com/ryanhebert>).

Include:
- WinMCP version (from `/info` if reachable)
- Affected component (platform, a specific module, the SDK)
- Reproduction steps or proof-of-concept
- Impact assessment (what an attacker can achieve)

## Response process

- Acknowledgement within 5 business days
- Triage and severity assessment within 10 business days
- Coordinated disclosure timeline agreed with the reporter; default 90 days from confirmation
- CVE assignment for confirmed vulnerabilities that warrant it
- Credit in the security advisory and changelog (unless the reporter prefers anonymity)

## Scope

In scope:
- The WinMCP platform binary
- The `WinMcp.ModuleSdk` NuGet package
- Default-shipped modules (currently: Math)

Out of scope:
- Third-party modules not maintained in `WinMCP-Modules`
- Operational misconfiguration (e.g., enabling `none` auth mode on a public network)
- Issues in upstream dependencies — report those to their respective projects

## Supported versions

The most recent two minor versions of the WinMCP platform receive security fixes. Older versions are not supported; users should upgrade via the in-UI upgrade flow or by downloading the latest release.
