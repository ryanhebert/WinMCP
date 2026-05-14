# Contributing to WinMCP

Thanks for your interest in contributing. Please read this before opening a PR.

## CLA — required for all contributions

WinMCP requires every contributor to sign a one-time Contributor License Agreement before their first PR can be merged. The CLA grants the project maintainers the right to relicense your contributions in the future (e.g., to dual-license under a commercial license for future enterprise editions). It does *not* transfer copyright — you retain it.

The CLA process is automated. On your first PR, [@cla-assistant](https://cla-assistant.io) will comment with a sign-in link. Click it, authenticate with GitHub, agree to the terms. Subsequent PRs from the same GitHub account are remembered.

The CLA text lives at [docs/CLA.md](docs/CLA.md).

**Note**: the `WinMCP-Modules` repository does *not* require a CLA — module contributions are pure Apache 2.0 with no relicensing reservation. Only this platform repository requires the CLA.

## Code of Conduct

This project follows the [Contributor Covenant 2.1](CODE_OF_CONDUCT.md). Be kind.

## Development setup

Requires:
- .NET 8 SDK
- Windows (for runtime testing) or Linux/Mac (for cross-compiling to win-x64)

```bash
git clone https://github.com/ryanhebert/WinMCP.git
cd WinMCP
dotnet build
dotnet test
```

To run the platform locally in foreground mode (no service install):
```bash
dotnet run --project src/Platform -- run
```

## Branching and PRs

- `main` is the integration branch; protect against direct pushes
- Branch from `main` for any change; name branches `feature/<short-name>` or `fix/<short-name>`
- One logical change per PR
- Reference the issue number in the PR title or description if applicable
- Add a CHANGELOG entry under "Unreleased" — Fixed / Added / Changed / Removed
- Update relevant docs in the same PR (don't promise a follow-up)
- Keep the diff focused: no opportunistic refactoring of unrelated code

## Commit messages

Imperative, present tense; concise:
```
fix: cert auto-renew at startup (B3)
feat: tool-calls filter on dashboard (B22)
docs: clarify module auth override semantics
```

## Testing

- Unit tests with `dotnet test`
- Integration tests against the platform exe (run on Windows; CI smoke-tests via `windows-latest` runner)
- For changes that affect install/uninstall, manually verify on a fresh Windows VM and document the test in the PR description

## Style

Follow the existing code's conventions — `dotnet format` covers most. No additions of warnings.

## Reporting bugs

Open an issue. Include:
- WinMCP version (from `/info`)
- Windows version
- Reproduction steps
- Relevant log lines (from `/logs/tail`)

Security issues — see [SECURITY.md](SECURITY.md), do *not* file a public issue.
