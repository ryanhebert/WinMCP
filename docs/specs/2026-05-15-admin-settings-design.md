# Admin Settings pages — design

**Status:** spec, awaiting review
**Date:** 2026-05-15
**Targets:** WinMCP v1.1
**Related backlog entries:** "Dashboard Settings pages", "Admin auth `oidc` mode", "Real OIDC validator"

## Summary

Add an in-UI Settings surface to the dashboard so operators can edit identity-provider, admin-auth, and MCP-default-auth configuration without touching `config.json` by hand. Today the platform requires `sc stop WinMcp`, edit JSON, `sc start WinMcp`. This delivers the same effect through a `/settings` page backed by REST endpoints, with an in-UI restart workflow.

Per-module auth override editing — the fourth section the BACKLOG names — is **out of scope** for this slice and lands as a v1.2 follow-up.

## Goals

1. Editable UI for the three "global" auth settings: identity providers, admin auth, MCP default auth.
2. Server-side validation that mirrors the strictness `ModuleManifestParser` applies to module manifests — bad input is rejected, never silently saved.
3. A clear, blocking "Restart pending" workflow so operators understand changes don't take effect until the service restarts. Live-reload is on the roadmap but not in this slice.
4. Lockout-safe: operators cannot inadvertently brick the dashboard by selecting an auth mode that doesn't work in the running platform version.

## Non-goals

- Per-module auth override editing (deferred to v1.2 — separate "Module Auth" tab).
- Editing `httpPort` / `httpsPort` / `logLevel` via UI (rarely changed; remain config.json-only).
- Editing the per-module opaque `moduleConfig` blob (module-author concern, not operator).
- Live-reload (validators rebuilt without restart). Tracked as backlog.
- CSRF protection — irrelevant given bearer auth + no cookies.
- Provider renaming — to rename, delete + re-add.

## URL surface

| Path | Method | Purpose |
|---|---|---|
| `/settings` | GET | Settings page (HTML) |
| `/api/settings` | GET | Full snapshot of editable settings (JSON) |
| `/api/settings/admin-auth` | PUT | Update admin auth domain |
| `/api/settings/mcp-auth` | PUT | Update MCP default auth domain |
| `/api/settings/oidc-providers` | POST | Add a new OIDC provider (runs discovery synchronously) |
| `/api/settings/oidc-providers/{name}` | PUT | Update an OIDC provider's issuer / audience / required-scopes |
| `/api/settings/oidc-providers/{name}` | DELETE | Remove an OIDC provider (rejected if referenced) |
| `/api/settings/oidc-providers/{name}/rediscover` | POST | Re-run discovery against the configured issuer |
| `/api/settings/restart` | POST | Spawn the helper batch that stops + starts the service |
| `/api/settings/restart-pending` | GET | `{ pending: bool, since: iso }` — read by every dashboard page for the global banner |

All endpoints are gated by the existing admin-auth middleware (same posture as `/`, `/info`, `/upgrade`).

## Page layout

`/settings` is a single HTML doc with three client-side tabs. Same dark theme + card styling as the existing dashboard. Max-width matches the dashboard (960px). Desktop-shaped; no mobile-first work.

```
┌─────────────────────────────────────────────────────┐
│ WinMCP — Settings                       [Restart ●] │  ← global restart-pending banner
├─────────────────────────────────────────────────────┤
│ [ Identity providers ] [ Admin auth ] [ MCP auth ]  │
├─────────────────────────────────────────────────────┤
│                                                     │
│   <tab content>                                     │
│                                                     │
└─────────────────────────────────────────────────────┘
```

### Tab 1 — Identity providers

List view, one card per configured provider. Empty state when none configured:

> No identity providers configured. **[+ Add provider]** Add one to use OIDC mode for admin or MCP auth.

Each provider card shows:

- **Name** (immutable after creation)
- **Issuer URL**
- **Audience** (editable; pre-populates to `winmcp` on the Add form)
- **Discovery status:** ✓ Discovered 2026-05-15 10:23 UTC / ✗ Failed: <reason>
- **In use by:** Admin, MCP default (chips)
- Actions: **Re-discover**, **Edit**, **Delete**

"Add provider" form fields: name (required, kebab-case), issuer URL (required, http(s)), audience (defaults to `winmcp`). Submit invokes `OidcDiscovery` synchronously; success persists, failure shows the discovery error inline and persists nothing.

### Tab 2 — Admin auth

- **Mode**: radio group `none` / `demo` / `oidc`.
  - `demo` is disabled with helper text "Demo mode is intended for the MCP auth domain only — admin endpoints don't issue tokens."
  - `oidc` triggers the lockout-safety flow described in **Lockout-safety details**. Selecting it shows a yellow banner:
    > Real OIDC validation lands in v1.1. Until then, switching admin auth to OIDC will return 503 on every dashboard request — locking you out. Select this only on a staging install where you can recover by editing config.json on the host.
- **Provider** (only when mode = `oidc`): dropdown populated from Tab 1.
- When mode = `none`: yellow inline note "Dashboard is unauthenticated — anyone on the network can edit these settings."

### Tab 3 — MCP default auth

Same controls as Admin auth, plus:

- **Required scopes** (only when mode = `oidc`): tag-editor input.
- **Required claims** (only when mode = `oidc`): key/value table editor.
- When mode = `demo`: shows the current platform demo credentials (bearer token, client_id, client_secret, TTL) with copy buttons. Same surface the main dashboard already exposes.

Same lockout warning is NOT shown for MCP auth — it's not catastrophic if MCP auth is broken; modules just return 401. The warning is specific to admin.

## Restart workflow

Every successful save sets an in-process boolean `RestartPending = true`. A global banner — rendered by both `/settings` and the main dashboard — polls `/api/settings/restart-pending` on a 10-second interval and shows:

> ● **Restart pending** — your settings changes won't take effect until WinMCP restarts. **[Restart now]**

Clicking "Restart now" POSTs `/api/settings/restart`. The handler:

1. Writes a small `.cmd` helper to `<InstallDir>\restart-helper.cmd` (same pattern as `UpgradeOrchestrator`'s helper-batch):
   - `timeout /t 2 /nobreak`
   - `sc stop WinMcp`
   - wait for `WinMCP.exe` to exit
   - `sc start WinMcp`
2. Spawns it detached via `cmd /c`.
3. Returns 202.

The page swaps the banner to "Restarting…" and polls `/info` every 1 second. When `/info` returns 200, the page reloads. The 4-minute cap and "service is offline" messaging match the upgrade modal.

`RestartPending` clears at next process start. (The flag is in-memory; it's implicitly cleared by the restart itself.)

## Validation

Server-side validation lives in `Config/SettingsValidator.cs`. Mirrors `ModuleManifestParser`'s strictness — reject on any of:

- **OIDC provider name**: must match `^[a-z][a-z0-9-]{0,62}$`.
- **OIDC issuer URL**: must be http(s), no path traversal, no query string.
- **Audience**: non-empty, single token.
- **Required scopes**: array of non-empty strings.
- **Required claims**: object with string keys + string-or-array values.
- **Provider ref** (in admin/mcp auth): must resolve to an existing provider name.
- **Delete provider**: rejected with 409 + `{ inUseBy: ["admin", "mcp"] }` when referenced.

Client-side validation handles obvious shapes (URL well-formed) for snappier UX. Server is the source of truth.

## OIDC discovery flow

Synchronous on save. `POST /api/settings/oidc-providers` invokes the existing `OidcDiscovery.DiscoverAsync()` with the submitted issuer URL. Three outcomes:

| Outcome | HTTP | Response | Side effect |
|---|---|---|---|
| Discovery succeeds | 201 | `{ name, issuer, audience, jwksUrl, authorizationEndpoint, tokenEndpoint, discoveredAtIso }` | Provider persisted; OidcProviderRegistry updated in-memory; restart pending flag set |
| Discovery fails | 422 | `{ error: "discovery_failed", message }` | Nothing persisted |
| Invalid input | 400 | `{ error: "invalid_request", message }` | Nothing persisted |

Synchronous is the right tradeoff here: discovery is usually fast (1-2s), failure is loud and recoverable, and an async "pending discovery" state machine would complicate the UI without buying much. The platform already does sync discovery at startup; this matches.

**Re-discovery** (`POST /.../{name}/rediscover`) runs the same flow without re-validating the input shape. Useful when an issuer rotates JWKS or endpoint URLs.

## Code shape

```
src/Platform/
  Hosting/
    SettingsApi.cs           ← NEW: REST handlers (read/write/restart-pending/restart)
    RestartCoordinator.cs    ← NEW: pending-flag + helper-batch spawn
    RouteRegistration.cs     ← MOD: register the new routes
    UpgradeOrchestrator.cs   ← unchanged
  Config/
    SettingsValidator.cs     ← NEW: server-side validation
    ConfigLoader.cs          ← unchanged (atomic Save reused)
    PlatformConfig.cs        ← unchanged (already covers the editable surface)
  Web/
    SettingsPage.cs          ← NEW: HTML/CSS/JS doc with three tabs
    IndexPage.cs             ← MOD: add the global restart-pending banner
```

`SettingsApi.cs` is a sibling of `UpgradeOrchestrator.cs` — both are HTTP-handler classes that mutate config + spawn side-effect processes. Same idiom (static class with handler methods + a per-handler private static state struct).

`RestartCoordinator.cs` is its own file rather than nested inside `SettingsApi` so the upgrade orchestrator can also consume it if we later want both code paths to share the restart-pending UX.

## Error handling

| Failure | Behavior |
|---|---|
| ConfigLoader.Save() throws (disk full, permission) | 500 + `{ error: "config_write_failed", message }`. Pending flag not set. Operator sees the error inline. |
| OIDC discovery network failure | 422 + the error. No partial save. |
| Operator submits a delete that orphans admin/mcp auth | 409 + `{ inUseBy: [...] }`. Operator must change the dependent setting first. |
| Helper-batch spawn fails (cmd.exe missing??) | 500. The state machine on the client falls back to "Restart failed; check /logs". Operator can `sc stop && sc start` from a shell. |
| Concurrent saves (two operators editing at once) | Last write wins. ConfigLoader.Save is atomic at the file level; no in-process lock beyond what ConfigLoader provides. Acceptable for v1; operators rarely edit concurrently. |

## Testing

No automated tests for the platform exist today (per `tests/` being absent). This work introduces unit tests for `SettingsValidator` (pure function, easy to test) under a new `tests/Platform.Tests/` project. The HTTP handlers and HTML page are validated manually on a Windows test box pre-tag.

Smoke checklist:
1. Fresh install → `/settings` page loads, all tabs render, all empty states correct.
2. Add provider with reachable issuer → 201, provider appears in list with green discovery status.
3. Add provider with unreachable issuer → 422, provider does NOT appear in list, error shown inline.
4. Select admin auth → oidc → save → restart-pending banner appears globally. Reload main dashboard → banner persists.
5. Click "Restart now" → service restarts, page auto-reloads, banner clears.
6. Try to delete a provider currently referenced by mcp auth → 409 with `inUseBy: ["mcp"]`.
7. Set admin auth to oidc, confirm warning, save, click Restart → dashboard returns 503 (expected for v1.0). Recovery: edit config.json on host, restart.

## Lockout-safety details

The "set admin auth to oidc" path is the highest-risk action in this UI. Mitigations:

1. **Strong inline warning** before the operator can submit (described in **Tab 2 — Admin auth** above).
2. **Confirm modal**: clicking Save with admin auth changing to `oidc` triggers a `confirm()`:
   > "Switching admin auth to OIDC will return 503 on every dashboard request until WinMCP v1.1 ships the OIDC validator. You will be locked out and will need to edit C:\\Program Files\\WinMCP\\config.json on the host to recover. Continue?"
3. **No special help-me-out endpoint**: keeping the dashboard inaccessible-by-design when configured as inaccessible is correct. The recovery story is "edit config.json on the host" — and that's not new; it's the same recovery story today.

Once v1.1's real OIDC validator ships, the warning + confirmation are removed from the UI.

## Backlog additions to write alongside

- **Live-reload of validators**: rebuild `AuthValidatorFactory`'s cached validators + middleware closures when config changes, eliminating the restart cycle. Real architectural work — separate spec when it lands.
- **Per-module auth override editor**: the fourth section in BACKLOG's "Dashboard Settings pages" entry. Adds a "Module Auth" tab to either Settings or each module's dashboard row.
- **Admin auth `oidc` mode functional**: removing this spec's lockout-safety machinery once OIDC validation actually works.

These three are mentioned by name in the existing BACKLOG.md "Auth — v1.1 targets" section already; this design doesn't add new entries, just confirms scope cuts.
