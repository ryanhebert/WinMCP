using System.Text.Json;

namespace WinMcp.Platform.Web;

/// <summary>
/// Root dashboard at <c>/</c>. Adapted from math-mcp's IndexPage: the
/// single-MCP "Tools/Prompts/Resources" card is replaced with a per-module
/// Modules card, and the in-UI upgrade flow (download + swap + restart
/// modal) is omitted — the upgrade orchestrator lands in a follow-up.
/// </summary>
internal static class IndexPage
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Render(IndexPageModel m)
    {
        var initialRequestsJson = JsonSerializer.Serialize(m.RecentRequests, CamelCase);
        var modulesJson = JsonSerializer.Serialize(m.Modules, CamelCase);
        var authCard = m.PlatformDemo is not null ? RenderAuthCard(m) : "";
        var modulesCard = RenderModulesCard(m);
        var endpointsCard = RenderEndpointsCard(m);
        var port80Note = m.Port80Active ? "active — /token serves here when demo auth is on" : "not bound";

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>WinMCP</title>
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<style>
  :root {
    --bg: #0b1020;
    --bg-card: #131a30;
    --bg-card-2: #1a2240;
    --fg: #e6ecff;
    --fg-muted: #8a93b3;
    --accent: #7c5cff;
    --accent-2: #4ad6ff;
    --ok: #34d399;
    --warn: #fbbf24;
    --err: #f87171;
    --border: rgba(255,255,255,0.08);
    --code-bg: #0f1428;
  }
  * { box-sizing: border-box; }
  html, body { margin: 0; padding: 0; }
  body {
    font: 15px/1.55 -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, sans-serif;
    color: var(--fg);
    background:
      radial-gradient(60vh 60vh at 15% 0%, rgba(124,92,255,0.18), transparent 70%),
      radial-gradient(60vh 60vh at 95% 10%, rgba(74,214,255,0.12), transparent 70%),
      var(--bg);
    min-height: 100vh;
  }
  .wrap { max-width: 960px; margin: 0 auto; padding: 48px 24px 64px; }
  header { display: flex; align-items: center; gap: 16px; margin-bottom: 24px; }
  .logo {
    width: 48px; height: 48px; flex: 0 0 48px;
    border-radius: 12px;
    background: linear-gradient(135deg, var(--accent) 0%, var(--accent-2) 100%);
    display: grid; place-items: center;
    font-weight: 700; font-size: 22px; color: #0b1020;
  }
  h1 { margin: 0; font-size: 26px; letter-spacing: -0.01em; }
  .subtitle { color: var(--fg-muted); font-size: 13px; margin-top: 2px; }
  .pill {
    display: inline-flex; align-items: center; gap: 6px;
    padding: 4px 10px; border-radius: 999px;
    background: rgba(52,211,153,0.12);
    color: var(--ok);
    font-size: 12px; font-weight: 600;
    border: 1px solid rgba(52,211,153,0.3);
  }
  .pill .dot {
    width: 8px; height: 8px; border-radius: 50%;
    background: var(--ok);
    box-shadow: 0 0 0 0 rgba(52,211,153,0.6);
    animation: pulse 2s infinite;
  }
  @keyframes pulse {
    0%   { box-shadow: 0 0 0 0 rgba(52,211,153,0.6); }
    70%  { box-shadow: 0 0 0 10px rgba(52,211,153,0); }
    100% { box-shadow: 0 0 0 0 rgba(52,211,153,0); }
  }
  .grid { display: grid; gap: 16px; grid-template-columns: 1fr 1fr; margin-top: 24px; }
  @media (max-width: 720px) { .grid { grid-template-columns: 1fr; } }
  .card {
    background: linear-gradient(180deg, var(--bg-card) 0%, var(--bg-card-2) 100%);
    border: 1px solid var(--border);
    border-radius: 14px;
    padding: 18px 20px;
  }
  .card h2 {
    margin: 0 0 12px; font-size: 13px;
    text-transform: uppercase; letter-spacing: 0.08em;
    color: var(--fg-muted); font-weight: 600;
  }
  .card.auth-card {
    border-color: rgba(251,191,36,0.35);
    background:
      linear-gradient(180deg, rgba(251,191,36,0.04) 0%, rgba(251,191,36,0.02) 100%),
      linear-gradient(180deg, var(--bg-card) 0%, var(--bg-card-2) 100%);
  }
  .card.auth-card h2 { color: var(--warn); display: flex; align-items: center; gap: 8px; }
  .card.auth-card h2::before {
    content: ""; width: 8px; height: 8px; border-radius: 50%;
    background: var(--warn); box-shadow: 0 0 8px rgba(251,191,36,0.6);
  }
  .auth-banner {
    font-size: 12px; color: var(--fg-muted);
    margin: -4px 0 14px;
    padding: 8px 10px;
    background: rgba(251,191,36,0.06);
    border: 1px solid rgba(251,191,36,0.18);
    border-radius: 8px;
  }
  .auth-banner strong { color: var(--warn); font-weight: 600; }
  dl { margin: 0; display: grid; grid-template-columns: max-content 1fr; gap: 8px 16px; }
  dt { color: var(--fg-muted); }
  dd { margin: 0; word-break: break-all; }
  code, .mono { font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace; }
  .endpoint {
    display: block;
    padding: 8px 12px;
    margin: 6px 0;
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 8px;
    color: var(--fg);
    text-decoration: none;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 13px;
    transition: border-color 0.15s ease;
  }
  .endpoint:hover { border-color: var(--accent); }
  .endpoint .method {
    display: inline-block; min-width: 44px;
    color: var(--accent-2); font-weight: 600; margin-right: 8px;
  }
  .endpoint .desc { color: var(--fg-muted); font-size: 12px; margin-left: 8px; }
  .tools { display: flex; flex-wrap: wrap; gap: 8px; }
  .tool {
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 8px;
    padding: 6px 12px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 13px;
  }
  .cred-group { margin-bottom: 14px; }
  .cred-group:last-child { margin-bottom: 0; }
  .cred-group-title {
    font-size: 11px; text-transform: uppercase; letter-spacing: 0.08em;
    color: var(--fg-muted); font-weight: 600;
    margin: 0 0 6px;
  }
  .cred {
    display: grid; grid-template-columns: max-content 1fr auto;
    align-items: center; gap: 10px;
    padding: 8px 10px; margin: 4px 0;
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 8px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 12.5px;
  }
  .cred-label { color: var(--fg-muted); }
  .cred-value { color: var(--fg); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .copy-btn {
    background: transparent;
    color: var(--accent-2);
    border: 1px solid var(--border);
    border-radius: 6px;
    padding: 4px 10px;
    font-family: inherit; font-size: 11px;
    cursor: pointer;
    transition: all 0.15s ease;
  }
  .copy-btn:hover { border-color: var(--accent-2); background: rgba(74,214,255,0.08); }
  .copy-btn.copied { color: var(--ok); border-color: var(--ok); background: rgba(52,211,153,0.1); }

  /* Modules table */
  .module-row {
    display: grid;
    grid-template-columns: 1fr auto;
    gap: 8px 12px;
    padding: 12px 14px;
    margin: 8px 0;
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 10px;
  }
  .module-row + .module-row { margin-top: 8px; }
  .module-row .name-line {
    display: flex; align-items: center; gap: 10px; flex-wrap: wrap;
    grid-column: 1 / -1;
  }
  .module-row .mname {
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-weight: 600; font-size: 14px; color: var(--fg);
  }
  .module-row .mver {
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 12px; color: var(--fg-muted);
  }
  .module-row .mdisplay { color: var(--fg-muted); font-size: 12.5px; }
  .badge {
    display: inline-flex; align-items: center;
    padding: 2px 8px; border-radius: 999px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 10.5px; font-weight: 600;
    text-transform: uppercase; letter-spacing: 0.06em;
    border: 1px solid var(--border);
    background: rgba(255,255,255,0.03);
    color: var(--fg-muted);
  }
  .badge.maturity-demo        { color: var(--warn); border-color: rgba(251,191,36,0.35); background: rgba(251,191,36,0.10); }
  .badge.maturity-experimental{ color: var(--accent-2); border-color: rgba(74,214,255,0.35); background: rgba(74,214,255,0.08); }
  .badge.maturity-production  { color: var(--ok); border-color: rgba(52,211,153,0.35); background: rgba(52,211,153,0.10); }
  .badge.auth-demo  { color: var(--warn); border-color: rgba(251,191,36,0.35); background: rgba(251,191,36,0.10); }
  .badge.auth-oidc  { color: var(--accent); border-color: rgba(124,92,255,0.45); background: rgba(124,92,255,0.10); }
  .badge.auth-none  { color: var(--fg-muted); }
  .module-meta {
    grid-column: 1 / -1;
    display: flex; gap: 10px 18px; flex-wrap: wrap;
    font-size: 12px; color: var(--fg-muted);
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
  }
  .module-meta strong { color: var(--fg); font-weight: 600; }
  .module-meta a { color: var(--accent-2); text-decoration: none; }
  .module-meta a:hover { text-decoration: underline; }
  .module-symbols {
    grid-column: 1 / -1;
    display: flex; gap: 6px; flex-wrap: wrap; margin-top: 4px;
  }
  .symbol {
    background: rgba(255,255,255,0.04);
    border: 1px solid var(--border);
    border-radius: 6px;
    padding: 3px 8px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 11.5px;
    color: var(--fg);
  }
  .symbol.kind-tool     { border-left: 2px solid var(--accent); }
  .symbol.kind-prompt   { border-left: 2px solid var(--accent-2); }
  .symbol.kind-resource { border-left: 2px solid var(--ok); }
  .symbols-empty { font-size: 11.5px; color: var(--fg-muted); font-style: italic; }
  .module-upgrade-btn {
    background: rgba(124,92,255,0.10);
    color: var(--accent);
    border: 1px solid rgba(124,92,255,0.5);
    border-radius: 6px;
    padding: 3px 10px;
    font-family: inherit; font-size: 11.5px; font-weight: 500;
    cursor: pointer;
    transition: background 0.15s ease;
  }
  .module-upgrade-btn:hover { background: rgba(124,92,255,0.18); }
  .module-upgrade-btn:disabled { opacity: 0.5; cursor: not-allowed; }

  /* Requests table */
  table.reqs {
    width: 100%; border-collapse: collapse;
    font-size: 12.5px;
  }
  table.reqs th {
    text-align: left;
    font-size: 11px; text-transform: uppercase; letter-spacing: 0.06em;
    color: var(--fg-muted); font-weight: 600;
    padding: 4px 10px; border-bottom: 1px solid var(--border);
  }
  table.reqs td {
    padding: 6px 10px;
    border-bottom: 1px solid rgba(255,255,255,0.04);
  }
  table.reqs tr:last-child td { border-bottom: none; }
  table.reqs tr:hover td { background: rgba(255,255,255,0.025); }
  table.reqs .ts { color: var(--fg-muted); width: 1%; white-space: nowrap; }
  table.reqs .args { color: var(--fg); }
  table.reqs .dur { color: var(--fg-muted); width: 1%; white-space: nowrap; }
  table.reqs .module { color: var(--accent); width: 1%; white-space: nowrap; }
  table.reqs .origin {
    display: inline-flex; align-items: center; gap: 6px;
    max-width: 220px;
  }
  table.reqs .origin .host {
    overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
    color: var(--fg);
  }
  table.reqs .origin .ip { color: var(--fg-muted); font-size: 11px; }
  .origin-dot {
    width: 8px; height: 8px; flex: 0 0 8px;
    border-radius: 50%;
    box-shadow: 0 0 4px rgba(0,0,0,0.4) inset;
  }
  .status {
    display: inline-block; min-width: 38px; text-align: center;
    padding: 2px 8px; border-radius: 6px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 11.5px; font-weight: 600;
  }
  .status.ok   { background: rgba(52,211,153,0.10); color: var(--ok);   border: 1px solid rgba(52,211,153,0.3); }
  .status.warn { background: rgba(251,191,36,0.10); color: var(--warn); border: 1px solid rgba(251,191,36,0.3); }
  .status.err  { background: rgba(248,113,113,0.10); color: var(--err); border: 1px solid rgba(248,113,113,0.3); }
  .reqs-footer {
    margin-top: 10px; font-size: 12px; color: var(--fg-muted); text-align: right;
  }
  .reqs-footer a { color: var(--accent-2); text-decoration: none; }
  .reqs-footer a:hover { text-decoration: underline; }
  .reqs-empty {
    color: var(--fg-muted); font-size: 12.5px; text-align: center;
    padding: 16px; font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
  }
  .reqs-head {
    display: flex; align-items: center; justify-content: space-between;
    margin: 0 0 12px;
  }
  .reqs-head h2 { margin: 0; }
  .reqs-filter { display: inline-flex; gap: 0; }
  .reqs-filter button {
    background: transparent; color: var(--fg-muted);
    border: 1px solid var(--border); padding: 3px 10px;
    font: inherit; font-size: 11px; letter-spacing: 0.04em;
    text-transform: uppercase; cursor: pointer;
  }
  .reqs-filter button:first-child { border-radius: 4px 0 0 4px; border-right: none; }
  .reqs-filter button:last-child  { border-radius: 0 4px 4px 0; }
  .reqs-filter button:hover { color: var(--fg); }
  .reqs-filter button.active {
    background: rgba(124,92,255,0.15); color: var(--accent);
    border-color: rgba(124,92,255,0.45);
  }
  footer { color: var(--fg-muted); font-size: 12px; margin-top: 32px; text-align: center; }
  footer a { color: var(--accent-2); text-decoration: none; }
  footer a:hover { text-decoration: underline; }
  .update-banner {
    display: none;
    align-items: center; justify-content: space-between; gap: 16px;
    padding: 12px 18px; margin: 0 0 18px;
    background: linear-gradient(90deg, rgba(124,92,255,0.18) 0%, rgba(74,214,255,0.12) 100%);
    border: 1px solid rgba(124,92,255,0.4);
    border-radius: 12px;
    font-size: 13px;
  }
  .update-banner.show { display: flex; }
  .update-banner .left { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
  .update-banner .spark {
    display: inline-grid; place-items: center;
    width: 22px; height: 22px; border-radius: 50%;
    background: linear-gradient(135deg, var(--accent) 0%, var(--accent-2) 100%);
    color: #0b1020; font-weight: 700;
  }
  .update-banner strong { color: var(--fg); }
  .update-banner .actions { display: flex; gap: 8px; flex-wrap: wrap; }
  .update-banner .actions a {
    color: var(--accent-2); text-decoration: none;
    border: 1px solid rgba(74,214,255,0.4);
    border-radius: 8px;
    padding: 5px 12px;
    font-size: 12px;
    transition: all 0.15s ease;
  }
  .update-banner .actions a:hover { background: rgba(74,214,255,0.08); }
  .update-banner .actions a.primary {
    color: var(--accent); border-color: rgba(124,92,255,0.5);
    background: rgba(124,92,255,0.10);
  }
  .update-banner .actions a.primary:hover { background: rgba(124,92,255,0.18); }
  .update-banner .dismiss {
    background: none; border: none; cursor: pointer;
    color: var(--fg-muted); font-size: 18px; line-height: 1;
    padding: 0 4px;
  }
  .update-banner .dismiss:hover { color: var(--fg); }
  .update-banner .progress {
    flex: 0 0 100%;
    height: 4px;
    background: rgba(255,255,255,0.06);
    border-radius: 2px;
    overflow: hidden;
    margin-top: 6px;
    display: none;
  }
  .update-banner .progress.show { display: block; }
  .update-banner .progress-bar {
    height: 100%; width: 0%;
    background: linear-gradient(90deg, var(--accent) 0%, var(--accent-2) 100%);
    transition: width 0.3s ease;
  }
  .update-banner .progress-bar.indeterminate {
    width: 30%;
    animation: indet 1.5s ease-in-out infinite;
  }
  @keyframes indet {
    0%   { margin-left: -30%; }
    100% { margin-left: 100%; }
  }

  /* === Upgrade modal === */
  .upgrade-modal-backdrop {
    position: fixed; inset: 0;
    background: rgba(0,0,0,0.6);
    backdrop-filter: blur(4px);
    z-index: 1000;
    display: none;
    align-items: center; justify-content: center;
  }
  .upgrade-modal-backdrop.show { display: flex; }
  .upgrade-modal {
    width: min(720px, 92vw);
    max-height: 80vh;
    background: linear-gradient(180deg, var(--bg-card) 0%, var(--bg-card-2) 100%);
    border: 1px solid var(--border);
    border-radius: 14px;
    box-shadow: 0 24px 64px rgba(0,0,0,0.6);
    display: flex; flex-direction: column;
    overflow: hidden;
  }
  .upgrade-modal-header {
    display: flex; align-items: center; justify-content: space-between;
    padding: 14px 18px;
    border-bottom: 1px solid var(--border);
  }
  .upgrade-modal-header .title { font-weight: 600; font-size: 14px; }
  .upgrade-modal-header .title .versions {
    color: var(--fg-muted); font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace; font-size: 12px;
    margin-left: 8px;
  }
  .upgrade-modal-header .close-x {
    background: none; border: none; cursor: pointer;
    color: var(--fg-muted); font-size: 22px; line-height: 1;
    padding: 0 6px;
  }
  .upgrade-modal-header .close-x:hover { color: var(--fg); }
  .upgrade-terminal {
    flex: 1;
    background: #07091a;
    padding: 14px 16px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 12.5px;
    line-height: 1.55;
    color: #c8d3ff;
    overflow-y: auto;
    border-bottom: 1px solid var(--border);
    min-height: 280px;
    max-height: 50vh;
  }
  .upgrade-terminal .ts { color: var(--fg-muted); margin-right: 12px; }
  .upgrade-terminal .ok { color: var(--ok); }
  .upgrade-terminal .err { color: var(--err); }
  .upgrade-terminal .warn { color: var(--warn); }
  .upgrade-terminal .dim { color: var(--fg-muted); }
  .upgrade-terminal .accent { color: var(--accent-2); }
  .upgrade-terminal .prompt { color: var(--accent); margin-right: 8px; }
  .upgrade-terminal .bar {
    display: inline-block; height: 8px; width: 200px;
    background: rgba(255,255,255,0.05);
    border-radius: 3px; vertical-align: middle;
    overflow: hidden; margin: 0 6px;
  }
  .upgrade-terminal .bar > span {
    display: block; height: 100%;
    background: linear-gradient(90deg, var(--accent) 0%, var(--accent-2) 100%);
    transition: width 0.2s ease;
  }
  .upgrade-modal-footer {
    display: flex; align-items: center; justify-content: space-between;
    padding: 12px 18px;
    background: rgba(0,0,0,0.2);
  }
  .upgrade-modal-footer .state {
    font-size: 12px; color: var(--fg-muted);
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
  }
  .upgrade-modal-footer .state.done { color: var(--ok); }
  .upgrade-modal-footer .state.failed { color: var(--err); }
  .upgrade-modal-footer .actions { display: flex; gap: 8px; }
  .upgrade-modal-footer button {
    background: rgba(124,92,255,0.10);
    color: var(--accent);
    border: 1px solid rgba(124,92,255,0.5);
    border-radius: 8px;
    padding: 6px 14px;
    font-family: inherit; font-size: 12.5px;
    cursor: pointer;
  }
  .upgrade-modal-footer button.muted {
    background: transparent; color: var(--fg-muted);
    border-color: var(--border);
  }
  .upgrade-modal-footer button:hover:not(:disabled) {
    background: rgba(124,92,255,0.18);
  }
  .upgrade-modal-footer button:disabled { opacity: 0.4; cursor: not-allowed; }
</style>
</head>
<body>
  <div class="wrap">
    <header>
      <div class="logo">W</div>
      <div style="flex:1">
        <h1>WinMCP</h1>
        <div class="subtitle">v{{m.Version}} on {{m.MachineName}} &middot; {{m.Modules.Count}} module{{(m.Modules.Count == 1 ? "" : "s")}} loaded</div>
      </div>
      <div class="pill"><span class="dot"></span> Running</div>
    </header>

    <div class="upgrade-modal-backdrop" id="upgrade-modal">
      <div class="upgrade-modal">
        <div class="upgrade-modal-header">
          <div class="title">Upgrade <span class="versions" id="um-versions"></span></div>
          <button class="close-x" id="um-close-x" title="Close">×</button>
        </div>
        <div class="upgrade-terminal" id="um-term"></div>
        <div class="upgrade-modal-footer">
          <span class="state" id="um-state">starting…</span>
          <div class="actions">
            <button id="um-action" disabled>Working…</button>
          </div>
        </div>
      </div>
    </div>

    <div class="update-banner" id="update-banner">
      <div class="left" id="ub-left">
        <span class="spark">↑</span>
        <span>Update available: <strong id="ub-version">—</strong></span>
        <span style="color:var(--fg-muted)">·</span>
        <span id="ub-status" style="color:var(--fg-muted)">downloads, swaps, and restarts in place</span>
      </div>
      <div class="actions" id="ub-actions">
        <button class="primary" id="ub-upgrade" style="background:rgba(124,92,255,0.10); color:var(--accent); border:1px solid rgba(124,92,255,0.5); border-radius:8px; padding:5px 12px; font-size:12px; cursor:pointer;">Upgrade now</button>
        <a id="ub-dl" href="#" target="_blank" rel="noopener">Download ↗</a>
        <a id="ub-notes" href="#" target="_blank" rel="noopener">Release notes ↗</a>
        <button class="dismiss" id="ub-dismiss" title="Dismiss for now">×</button>
      </div>
      <div class="progress" id="ub-progress"><div class="progress-bar" id="ub-progress-bar"></div></div>
    </div>

    <div class="grid">
      <div class="card">
        <h2>Overview</h2>
        <dl>
          <dt>Status</dt>     <dd>Running</dd>
          <dt>Uptime</dt>     <dd id="uptime">—</dd>
          <dt>Started</dt>    <dd class="mono" id="started">{{m.StartedAtIso}}</dd>
          <dt>Version</dt>    <dd class="mono">{{m.Version}}</dd>
          <dt>Machine</dt>    <dd class="mono">{{m.MachineName}}</dd>
          <dt>OS</dt>         <dd class="mono">{{m.Os}}</dd>
          <dt>Admin auth</dt> <dd class="mono">{{m.AdminAuthMode}}</dd>
          <dt>MCP default</dt><dd class="mono">{{m.McpDefaultAuthMode}}</dd>
        </dl>
      </div>

      <div class="card">
        <h2>Listening</h2>
        <dl>
          <dt>HTTP port</dt>   <dd class="mono">{{m.HttpPort}}</dd>
          <dt>HTTPS port</dt>  <dd class="mono">{{m.HttpsPort}}</dd>
          <dt>Port 80</dt>     <dd class="mono">{{port80Note}}</dd>
          <dt>Bind</dt>        <dd class="mono">0.0.0.0 (all interfaces)</dd>
          <dt>Cert valid</dt>  <dd class="mono">{{m.CertNotBefore}} → {{m.CertNotAfter}}</dd>
          <dt>Fingerprint</dt> <dd class="mono" style="font-size:11.5px">
            <span id="v-fp">SHA256:{{m.CertFingerprint}}</span>
            <button class="copy-btn" data-copy="v-fp" style="margin-left:6px">Copy</button>
          </dd>
          <dt>Download</dt>    <dd>
            <a href="/cert.cer" class="copy-btn" style="text-decoration:none">cert.cer (DER)</a>
            <a href="/cert.pem" class="copy-btn" style="text-decoration:none; margin-left:4px">cert.pem</a>
          </dd>
        </dl>
      </div>

      {{modulesCard}}

      {{authCard}}

      <div class="card" style="grid-column: 1 / -1">
        <div class="reqs-head">
          <h2>Recent MCP requests</h2>
          <span class="reqs-filter" role="group" aria-label="Filter requests">
            <button type="button" id="reqs-filter-all" data-filter="all">All</button>
            <button type="button" id="reqs-filter-tools" data-filter="tools">Tool calls</button>
          </span>
        </div>
        <table class="reqs">
          <thead>
            <tr>
              <th>Time</th>
              <th>Module</th>
              <th>Origin</th>
              <th>Method</th>
              <th>Args</th>
              <th>Status</th>
              <th>Duration</th>
            </tr>
          </thead>
          <tbody id="reqs-tbody"></tbody>
        </table>
        <div class="reqs-footer">
          <span id="reqs-summary">—</span> &middot; <a href="/logs">View full log →</a>
        </div>
      </div>

      {{endpointsCard}}
    </div>

    <footer>
      WinMCP v{{m.Version}} &middot;
      <a href="/info" target="_blank" rel="noopener">JSON</a> &middot;
      <a href="/logs" target="_blank" rel="noopener">Logs</a> &middot;
      <a href="/health" target="_blank" rel="noopener">Health</a> &middot;
      <a href="#" id="ub-check">Check for updates ↻</a> <span id="ub-check-result" style="color:var(--fg-muted)"></span> &middot;
      <a href="https://github.com/ryanhebert/WinMCP" target="_blank" rel="noopener">GitHub ↗</a> &middot;
      <a href="https://github.com/ryanhebert/WinMCP/releases" target="_blank" rel="noopener">Releases ↗</a>
    </footer>
  </div>

<script>
  (function() {
    const startedAt = new Date("{{m.StartedAtIso}}");
    const uptimeEl = document.getElementById('uptime');
    function fmt(seconds) {
      const d = Math.floor(seconds / 86400);
      const h = Math.floor((seconds % 86400) / 3600);
      const mm = Math.floor((seconds % 3600) / 60);
      const s = Math.floor(seconds % 60);
      const parts = [];
      if (d) parts.push(d + 'd');
      if (h || d) parts.push(h + 'h');
      if (mm || h || d) parts.push(mm + 'm');
      parts.push(s + 's');
      return parts.join(' ');
    }
    function tick() {
      const seconds = (Date.now() - startedAt.getTime()) / 1000;
      uptimeEl.textContent = fmt(seconds);
    }
    tick();
    setInterval(tick, 1000);
  })();

  async function copyToClipboard(text) {
    if (navigator.clipboard && window.isSecureContext) {
      try { await navigator.clipboard.writeText(text); return true; }
      catch (_) { /* fall through */ }
    }
    // Fallback for http:// on non-localhost where Clipboard API is unavailable.
    const ta = document.createElement('textarea');
    ta.value = text;
    ta.setAttribute('readonly', '');
    ta.style.position = 'fixed';
    ta.style.top = '0'; ta.style.left = '0';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    ta.setSelectionRange(0, text.length);
    let ok = false;
    try { ok = document.execCommand('copy'); } catch (_) { ok = false; }
    document.body.removeChild(ta);
    return ok;
  }

  document.querySelectorAll('.copy-btn[data-copy]').forEach(btn => {
    btn.addEventListener('click', async () => {
      const target = document.getElementById(btn.dataset.copy);
      if (!target) return;
      const text = target.innerText.trim();
      const orig = btn.textContent;
      const ok = await copyToClipboard(text);
      btn.textContent = ok ? 'Copied' : 'Failed';
      btn.classList.toggle('copied', ok);
      setTimeout(() => { btn.textContent = orig; btn.classList.remove('copied'); }, 1200);
    });
  });

  (function() {
    // ===== Check-for-updates banner =====
    // Queries GitHub's public API (CORS-enabled, anonymous, ~60/hr rate limit
    // per IP). Caches the answer for an hour. The in-UI upgrade workflow
    // will hang off this banner in a follow-up release; v1.0 lands with
    // links-only.
    const CURRENT = "{{m.Version}}";
    const CACHE_KEY  = 'winmcp.update.check.v1';
    const CACHE_TTL_MS = 60 * 60 * 1000;
    const DISMISS_KEY = 'winmcp.update.dismissed';

    const banner   = document.getElementById('update-banner');
    const ubVersion = document.getElementById('ub-version');
    const ubDl     = document.getElementById('ub-dl');
    const ubNotes  = document.getElementById('ub-notes');
    const ubDismiss = document.getElementById('ub-dismiss');
    const ubUpgrade = document.getElementById('ub-upgrade');
    const ubStatus = document.getElementById('ub-status');
    const ubProgress = document.getElementById('ub-progress');
    const ubProgressBar = document.getElementById('ub-progress-bar');
    const ubCheck = document.getElementById('ub-check');
    const ubCheckResult = document.getElementById('ub-check-result');
    const modal = document.getElementById('upgrade-modal');
    const umVersions = document.getElementById('um-versions');
    const umCloseX = document.getElementById('um-close-x');
    const umTerm = document.getElementById('um-term');
    const umState = document.getElementById('um-state');
    const umAction = document.getElementById('um-action');

    function cmpVersion(a, b) {
      const ap = a.split('.').map(n => parseInt(n, 10) || 0);
      const bp = b.split('.').map(n => parseInt(n, 10) || 0);
      const len = Math.max(ap.length, bp.length);
      for (let i = 0; i < len; i++) {
        const av = ap[i] || 0, bv = bp[i] || 0;
        if (av !== bv) return av < bv ? -1 : 1;
      }
      return 0;
    }

    async function checkUpdate() {
      let latest = null;
      try {
        const raw = localStorage.getItem(CACHE_KEY);
        if (raw) {
          const c = JSON.parse(raw);
          if (c && c.ts && (Date.now() - c.ts) < CACHE_TTL_MS) {
            latest = c.data;
          }
        }
        if (!latest) {
          const res = await fetch(
            'https://api.github.com/repos/ryanhebert/WinMCP/releases/latest',
            { headers: { Accept: 'application/vnd.github+json' } });
          if (!res.ok) return;
          const j = await res.json();
          // /releases/latest already excludes prereleases and drafts, but be
          // defensive — a prerelease asset wouldn't match our naming convention
          // so the download URL would 404.
          if (j.prerelease || j.draft) return;
          latest = { tag: j.tag_name, htmlUrl: j.html_url };
          localStorage.setItem(CACHE_KEY, JSON.stringify({ ts: Date.now(), data: latest }));
        }
      } catch (_) { return; }

      if (!latest || !latest.tag) return;
      const latestSemver = latest.tag.replace(/^v/, '');
      if (cmpVersion(latestSemver, CURRENT.split('+')[0]) <= 0) return;

      if (localStorage.getItem(DISMISS_KEY) === latest.tag) return;

      ubVersion.textContent = latest.tag;
      ubDl.href = `https://github.com/ryanhebert/WinMCP/releases/download/${latest.tag}/WinMCP-${latest.tag}.exe`;
      ubNotes.href = latest.htmlUrl;
      banner.classList.add('show');
    }

    ubDismiss.addEventListener('click', () => {
      localStorage.setItem(DISMISS_KEY, ubVersion.textContent);
      banner.classList.remove('show');
    });

    function fmtBytes(n) {
      if (n == null) return '—';
      if (n < 1024) return `${n} B`;
      if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
      return `${(n / 1024 / 1024).toFixed(1)} MB`;
    }

    function setProgress(percentOrIndet, visible) {
      ubProgress.classList.toggle('show', visible);
      if (percentOrIndet === 'indet') {
        ubProgressBar.classList.add('indeterminate');
        ubProgressBar.style.width = '';
      } else {
        ubProgressBar.classList.remove('indeterminate');
        ubProgressBar.style.width = `${Math.max(0, Math.min(100, percentOrIndet))}%`;
      }
    }

    function esc(s) {
      return String(s).replace(/[&<>"']/g, c =>
        ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    }

    // ===== Terminal-style modal output =====
    function nowHms() { return new Date().toTimeString().slice(0, 8); }
    function termLine(html, cls) {
      const div = document.createElement('div');
      div.innerHTML = `<span class="ts">${nowHms()}</span>${cls ? `<span class="${cls}">${html}</span>` : html}`;
      umTerm.appendChild(div);
      umTerm.scrollTop = umTerm.scrollHeight;
      return div;
    }
    function termHeader(text) {
      const div = document.createElement('div');
      div.innerHTML = `<span class="prompt">$</span><span class="accent">${esc(text)}</span>`;
      umTerm.appendChild(div);
      return div;
    }
    function termProgressLine() {
      const div = document.createElement('div');
      umTerm.appendChild(div);
      return div;
    }
    function termProgressUpdate(line, downloaded, total) {
      const pct = total ? Math.min(100, Math.floor((downloaded / total) * 100)) : 0;
      const filledPct = total ? pct : 30;
      line.innerHTML =
        `<span class="ts">${nowHms()}</span>` +
        `<span class="dim">downloading</span> ` +
        `<span class="bar"><span style="width:${filledPct}%"></span></span> ` +
        `<span>${fmtBytes(downloaded)}${total ? ` / ${fmtBytes(total)} (${pct}%)` : '…'}</span>`;
      umTerm.scrollTop = umTerm.scrollHeight;
    }

    function openModal(target, currentVersion) {
      umVersions.textContent = `${currentVersion} → ${target}`;
      umTerm.innerHTML = '';
      umState.className = 'state';
      umState.textContent = 'starting…';
      umAction.disabled = true;
      umAction.textContent = 'Working…';
      umAction.classList.remove('muted');
      modal.classList.add('show');
    }
    function closeModal(reload) {
      modal.classList.remove('show');
      if (reload) location.reload();
    }
    umCloseX.addEventListener('click', () => closeModal(false));

    function finish(ok, reason, didRestart) {
      umAction.disabled = false;
      if (ok) {
        umState.className = 'state done';
        umState.textContent = 'done';
        // Reload on success only when the service actually restarted; the
        // "already up to date" no-op path doesn't need a page refresh.
        if (didRestart) {
          umAction.textContent = 'Close & reload';
          umAction.onclick = () => closeModal(true);
        } else {
          umAction.textContent = 'Close';
          umAction.onclick = () => closeModal(false);
        }
      } else {
        umState.className = 'state failed';
        umState.textContent = `failed: ${reason}`;
        umAction.textContent = 'Close';
        umAction.classList.add('muted');
        umAction.onclick = () => closeModal(false);
      }
    }

    // Generic upgrade flow used by both the platform "Upgrade now" button
    // and per-module Upgrade buttons. Differences are passed via opts:
    //   endpoint     — where to POST (platform: /upgrade; module: /upgrade/module/<name>)
    //   body         — request payload ({version: 'latest'|tag})
    //   headerText   — first line shown in the modal terminal
    //   stagedNote   — message rendered when state transitions to 'staged'
    //   versionCheck — fn(info) → {done, label?} consulted after /info polls
    async function streamUpgrade(opts) {
      termHeader(opts.headerText);
      termLine('<span class="dim">requesting upgrade…</span>');

      let res;
      try {
        res = await fetch(opts.endpoint, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(opts.body),
        });
      } catch (e) {
        termLine(`request failed: ${esc(e.message)}`, 'err');
        return finish(false, 'request failed', false);
      }
      if (!res.ok) {
        const body = await res.text();
        termLine(`server returned HTTP ${res.status}`, 'err');
        if (body) termLine(esc(body.slice(0, 200)), 'dim');
        if (res.status === 409) {
          termLine('an upgrade is already in progress — try again in a minute', 'warn');
        }
        return finish(false, `HTTP ${res.status}`, false);
      }
      termLine('accepted (HTTP 202)', 'ok');

      // Poll /upgrade/status until done/failed, or until /info confirms the
      // new version is live (covers the gap where the service restarted and
      // status reset to 'idle').
      let progressLine = null;
      let lastState = null;
      let sawRestarting = false;
      let serviceWentDown = false;
      const start = Date.now();
      while (Date.now() - start < 240000) {  // 4 min cap
        let st = null;
        try {
          const r = await fetch('/upgrade/status', { cache: 'no-store' });
          if (r.ok) st = await r.json();
        } catch (_) { /* server probably restarting */ }

        if (st) {
          if (st.state !== lastState) {
            switch (st.state) {
              case 'downloading':
                termLine('<span class="accent">downloading…</span>');
                if (!progressLine) progressLine = termProgressLine();
                break;
              case 'staged':
                if (progressLine && st.bytes_total) {
                  termProgressUpdate(progressLine, st.bytes_total, st.bytes_total);
                }
                termLine(`<span class="ok">✓</span> ${opts.stagedNote}`);
                termLine('<span class="dim">spawning upgrade helper…</span>');
                break;
              case 'restarting':
                sawRestarting = true;
                termLine('<span class="dim">helper running — service stop will follow in ~3s</span>');
                umState.textContent = 'service restarting…';
                break;
              case 'done':
                if (st.message) {
                  termLine(`<span class="ok">✓ ${esc(st.message)}</span>`);
                } else {
                  termLine('<span class="ok">✓ upgrade complete</span>');
                }
                return finish(true, null, sawRestarting);
              case 'failed':
                termLine(`<span class="err">✗ failed: ${esc(st.message || 'unknown')}</span>`);
                return finish(false, st.message || 'failed', sawRestarting);
            }
            lastState = st.state;
            umState.textContent = st.state;
          }
          if (st.state === 'downloading' && progressLine) {
            termProgressUpdate(progressLine, st.bytes_downloaded || 0, st.bytes_total || 0);
          }
        }

        try {
          const ir = await fetch('/info', { cache: 'no-store' });
          if (ir.ok) {
            const info = await ir.json();
            const vc = opts.versionCheck(info);
            if (vc && vc.done) {
              if (serviceWentDown) {
                termLine('<span class="ok">✓</span> service is back online');
              }
              if (vc.label) {
                termLine(`<span class="ok">✓</span> ${vc.label}`);
              }
              termLine('<span class="ok">✓ upgrade complete</span>');
              return finish(true, null, sawRestarting);
            }
          } else if (!serviceWentDown) {
            serviceWentDown = true;
            termLine('<span class="warn">service is offline — waiting for restart</span>');
          }
        } catch (_) {
          if (!serviceWentDown) {
            serviceWentDown = true;
            termLine('<span class="warn">service is offline — waiting for restart</span>');
          }
        }

        await new Promise(r => setTimeout(r, 800));
      }
      termLine('<span class="err">timed out after 4 minutes — check /logs for details</span>');
      return finish(false, 'timed out', sawRestarting);
    }

    ubUpgrade.addEventListener('click', async () => {
      const target = ubVersion.textContent;
      const targetSemver = target.replace(/^v/, '');
      if (!confirm(`Upgrade this server to ${target}? The service will stop, swap binaries, and restart (~10–30 seconds). Connected MCP clients will see a brief outage.`)) return;
      openModal(target, 'v' + CURRENT.split('+')[0]);
      await streamUpgrade({
        endpoint: '/upgrade',
        body: { version: target },
        headerText: `WinMCP upgrade → ${target}`,
        stagedNote: 'binary verified (PE header OK)',
        versionCheck: info => {
          if (!info || !info.version) return null;
          const installed = info.version.split('+')[0];
          return {
            done: cmpVersion(installed, targetSemver) >= 0,
            label: `new version: <span class="accent">${esc(info.version)}</span>`,
          };
        },
      });
    });

    // Per-module Upgrade buttons. Each carries data-module + data-current
    // attributes; the click handler resolves them and drives streamUpgrade
    // with module-scoped endpoint + version check.
    document.querySelectorAll('.module-upgrade-btn').forEach(btn => {
      btn.addEventListener('click', async () => {
        const moduleName = btn.dataset.module;
        const currentVersion = btn.dataset.current;
        if (!confirm(`Upgrade module ${moduleName} to its latest release? The service will stop, swap the module folder, and restart (~10–30 seconds). Connected MCP clients will see a brief outage.`)) return;
        openModal('latest', `v${currentVersion}`);
        await streamUpgrade({
          endpoint: `/upgrade/module/${encodeURIComponent(moduleName)}`,
          body: { version: 'latest' },
          headerText: `Module ${moduleName} upgrade → latest`,
          stagedNote: 'archive extracted + manifest validated',
          versionCheck: info => {
            if (!info || !Array.isArray(info.modules)) return null;
            const mod = info.modules.find(m => m.name === moduleName);
            if (!mod || !mod.version) return null;
            return {
              done: mod.version !== currentVersion,
              label: `new version: <span class="accent">${esc(mod.version)}</span>`,
            };
          },
        });
      });
    });

    ubCheck.addEventListener('click', async (e) => {
      e.preventDefault();
      ubCheckResult.textContent = '…';
      try {
        localStorage.removeItem(CACHE_KEY);
        localStorage.removeItem(DISMISS_KEY);
        const res = await fetch(
          'https://api.github.com/repos/ryanhebert/WinMCP/releases/latest',
          { headers: { Accept: 'application/vnd.github+json' }, cache: 'no-store' });
        if (!res.ok) throw new Error(`GitHub returned ${res.status}`);
        const j = await res.json();
        if (j.prerelease || j.draft) {
          ubCheckResult.textContent = '(no stable release available)';
          setTimeout(() => { ubCheckResult.textContent = ''; }, 3000);
          return;
        }
        const latest = { tag: j.tag_name, htmlUrl: j.html_url };
        localStorage.setItem(CACHE_KEY, JSON.stringify({ ts: Date.now(), data: latest }));
        const latestSemver = latest.tag.replace(/^v/, '');
        if (cmpVersion(latestSemver, CURRENT.split('+')[0]) > 0) {
          ubVersion.textContent = latest.tag;
          ubDl.href = `https://github.com/ryanhebert/WinMCP/releases/download/${latest.tag}/WinMCP-${latest.tag}.exe`;
          ubNotes.href = latest.htmlUrl;
          banner.classList.add('show');
          ubCheckResult.textContent = `(${latest.tag} available)`;
        } else {
          banner.classList.remove('show');
          ubCheckResult.textContent = '(up to date)';
          setTimeout(() => { ubCheckResult.textContent = ''; }, 3000);
        }
      } catch (e) {
        ubCheckResult.textContent = `(check failed: ${e.message})`;
      }
    });

    checkUpdate();
  })();

  (function() {
    const tbody = document.getElementById('reqs-tbody');
    const summary = document.getElementById('reqs-summary');
    const initial = {{initialRequestsJson}};
    let lastItems = initial || [];

    const FILTER_KEY = 'winmcp.reqs.filter';
    const filterBtns = document.querySelectorAll('.reqs-filter button');
    let filterMode = localStorage.getItem(FILTER_KEY) === 'tools' ? 'tools' : 'all';
    function applyFilterButtonState() {
      filterBtns.forEach(b => {
        b.classList.toggle('active', b.dataset.filter === filterMode);
      });
    }
    filterBtns.forEach(b => b.addEventListener('click', () => {
      filterMode = b.dataset.filter === 'tools' ? 'tools' : 'all';
      localStorage.setItem(FILTER_KEY, filterMode);
      applyFilterButtonState();
      render(lastItems);
    }));
    applyFilterButtonState();

    function statusClass(s) { return s >= 500 ? 'err' : s >= 400 ? 'warn' : 'ok'; }
    function fmtTime(iso) {
      const d = new Date(iso);
      const pad = n => String(n).padStart(2, '0');
      const ms = String(d.getMilliseconds()).padStart(3, '0');
      return `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}.${ms}`;
    }
    function esc(s) {
      return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    }
    function colorFor(s) {
      let h = 0;
      const str = s || '-';
      for (let i = 0; i < str.length; i++) h = (h * 31 + str.charCodeAt(i)) & 0xffff;
      return `hsl(${h % 360}, 60%, 60%)`;
    }
    function hostOnly(host) {
      if (!host) return '—';
      const i = host.indexOf(':');
      return i > 0 ? host.slice(0, i) : host;
    }
    function render(items) {
      lastItems = items || [];
      if (!lastItems.length) {
        tbody.innerHTML = '<tr><td colspan="7"><div class="reqs-empty">No MCP requests yet.</div></td></tr>';
        summary.textContent = '—';
        return;
      }
      const filtered = filterMode === 'tools'
        ? lastItems.filter(r => r.method === 'tools/call')
        : lastItems;
      if (!filtered.length) {
        tbody.innerHTML = '<tr><td colspan="7"><div class="reqs-empty">No tool calls in the recent window. ' +
          'The buffer holds the last 50 MCP requests (any method); switch to <b>All</b> to see them.</div></td></tr>';
        summary.textContent = `0 of ${lastItems.length} are tool calls`;
        return;
      }
      tbody.innerHTML = filtered.slice(0, 10).map(r => {
        const host = hostOnly(r.host || '');
        const ip = r.remoteIp || '';
        const c = colorFor(host);
        const originHtml = host === '—'
          ? `<span class="origin"><span class="origin-dot" style="background:${c}"></span><span class="host">—</span></span>`
          : `<span class="origin" title="${esc(host)}${ip ? ' (' + esc(ip) + ')' : ''}">` +
              `<span class="origin-dot" style="background:${c}"></span>` +
              `<span class="host">${esc(host)}</span>` +
            `</span>`;
        return `<tr>` +
          `<td class="mono ts">${esc(fmtTime(r.timestampIso))}</td>` +
          `<td class="mono module">${esc(r.module)}</td>` +
          `<td class="mono">${originHtml}</td>` +
          `<td class="mono">${esc(r.method)}</td>` +
          `<td class="mono args">${esc(r.args)}</td>` +
          `<td><span class="status ${statusClass(r.status)}">${r.status}</span></td>` +
          `<td class="mono dur">${r.durationMs} ms</td>` +
        `</tr>`;
      }).join('');
      const shown = Math.min(filtered.length, 10);
      summary.textContent = filterMode === 'tools'
        ? `Last ${shown} tool calls (of ${lastItems.length} buffered requests)`
        : `Last ${shown} requests`;
    }
    render(initial);
    async function refresh() {
      try {
        const res = await fetch('/requests', { cache: 'no-store' });
        if (!res.ok) return;
        const data = await res.json();
        render(data);
      } catch (e) { /* swallow */ }
    }
    setInterval(refresh, 5000);
  })();
</script>
</body>
</html>
""";
    }

    private static string RenderModulesCard(IndexPageModel m)
    {
        if (m.Modules.Count == 0)
        {
            return """

      <div class="card" style="grid-column: 1 / -1">
        <h2>Modules</h2>
        <div class="reqs-empty">
          No modules loaded. Drop a module folder into
          <code>&lt;InstallDir&gt;\modules\&lt;name&gt;\</code> and restart the service.
        </div>
      </div>
""";
        }

        var rows = string.Join("\n", m.Modules.Select(RenderModuleRow));
        return $$"""

      <div class="card" style="grid-column: 1 / -1">
        <h2>Modules</h2>
        {{rows}}
      </div>
""";
    }

    private static string RenderModuleRow(ModuleDisplay m)
    {
        var maturityClass = $"maturity-{m.Maturity.ToLowerInvariant()}";
        var authClass = $"auth-{m.AuthMode.ToLowerInvariant()}";
        var tools = RenderSymbols(m.ToolNames, "kind-tool");
        var prompts = RenderSymbols(m.PromptNames, "kind-prompt");
        var resources = RenderSymbols(m.ResourceNames, "kind-resource");
        var anySymbols = m.ToolNames.Count + m.PromptNames.Count + m.ResourceNames.Count > 0;
        var symbolsBlock = anySymbols
            ? $"<div class=\"module-symbols\">{tools}{prompts}{resources}</div>"
            : "<div class=\"module-symbols\"><span class=\"symbols-empty\">No tools, prompts, or resources registered.</span></div>";

        var upgradeBtn = m.HasUpdateSource
            ? $"<button class=\"module-upgrade-btn\" data-module=\"{Esc(m.Name)}\" data-current=\"{Esc(m.Version)}\" title=\"Upgrade this module to its latest GitHub release\">Upgrade ↑</button>"
            : "";

        return $$"""
        <div class="module-row">
          <div class="name-line">
            <span class="mname">{{Esc(m.Name)}}</span>
            <span class="mver">v{{Esc(m.Version)}}</span>
            <span class="badge {{maturityClass}}">{{Esc(m.Maturity)}}</span>
            <span class="badge {{authClass}}">auth: {{Esc(m.AuthMode)}}</span>
            <span class="mdisplay">{{Esc(m.DisplayName)}}</span>
            <span style="margin-left:auto">{{upgradeBtn}}</span>
          </div>
          <div class="module-meta">
            <span>mount <strong>{{Esc(m.MountPath)}}</strong></span>
            <span>MCP <a href="{{Esc(m.MountPath)}}/mcp">{{Esc(m.MountPath)}}/mcp</a></span>
            <span>tools <strong>{{m.ToolNames.Count}}</strong></span>
            <span>prompts <strong>{{m.PromptNames.Count}}</strong></span>
            <span>resources <strong>{{m.ResourceNames.Count}}</strong></span>
          </div>
          {{symbolsBlock}}
        </div>
""";
    }

    private static string RenderSymbols(IReadOnlyList<string> names, string kindClass)
    {
        if (names.Count == 0) return "";
        return string.Join("", names.Select(n =>
            $"<span class=\"symbol {kindClass}\">{Esc(n)}</span>"));
    }

    private static string RenderEndpointsCard(IndexPageModel m)
    {
        var lines = new List<string>
        {
            "<a class=\"endpoint\" href=\"/\"><span class=\"method\">GET</span>/<span class=\"desc\">this page (HTML); JSON via <code>/info</code></span></a>",
            "<a class=\"endpoint\" href=\"/info\"><span class=\"method\">GET</span>/info<span class=\"desc\">platform + modules metadata (JSON)</span></a>",
            "<a class=\"endpoint\" href=\"/logs\"><span class=\"method\">GET</span>/logs<span class=\"desc\">live log viewer (HTML)</span></a>",
            "<a class=\"endpoint\" href=\"/health\"><span class=\"method\">GET</span>/health<span class=\"desc\">health probe</span></a>",
            "<a class=\"endpoint\" href=\"/requests\"><span class=\"method\">GET</span>/requests<span class=\"desc\">recent MCP requests (JSON)</span></a>",
        };

        if (m.PlatformDemo is not null)
        {
            lines.Add("<span class=\"endpoint\"><span class=\"method\">POST</span>/token<span class=\"desc\">OAuth2 client_credentials → bearer token</span></span>");
            lines.Add("<a class=\"endpoint\" href=\"/.well-known/oauth-authorization-server\"><span class=\"method\">GET</span>/.well-known/oauth-authorization-server<span class=\"desc\">OAuth 2.0 server metadata (RFC 8414)</span></a>");
        }

        foreach (var mod in m.Modules)
        {
            var mcp = $"{mod.MountPath}/mcp";
            var authNote = mod.AuthMode.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? "anonymous"
                : $"auth: {mod.AuthMode}";
            lines.Add($"<span class=\"endpoint\"><span class=\"method\">POST</span>{Esc(mcp)}<span class=\"desc\">MCP transport for module <code>{Esc(mod.Name)}</code> ({authNote})</span></span>");
        }

        return $$"""

      <div class="card" style="grid-column: 1 / -1">
        <h2>Endpoints</h2>
        {{string.Join("\n        ", lines)}}
      </div>
""";
    }

    private static string RenderAuthCard(IndexPageModel m)
    {
        var d = m.PlatformDemo!;
        return $$"""

      <div class="card auth-card" style="grid-column: 1 / -1">
        <h2>Platform demo credentials</h2>
        <div class="auth-banner">
          <strong>MCP default auth = demo</strong> — modules inheriting the
          default accept these credentials. Each module may override its own
          auth mode via its manifest or via <code>config.json</code>.
          Anonymous bearers (non-WinMCP-shaped) fall through as unauthenticated.
        </div>

        <div class="cred-group">
          <div class="cred-group-title">Static bearer token</div>
          <div class="cred">
            <span class="cred-label">token</span>
            <span class="cred-value" id="v-bearer">{{Esc(d.BearerToken)}}</span>
            <button class="copy-btn" data-copy="v-bearer">Copy</button>
          </div>
        </div>

        <div class="cred-group">
          <div class="cred-group-title">OAuth2 client credentials</div>
          <div class="cred">
            <span class="cred-label">client_id</span>
            <span class="cred-value" id="v-cid">{{Esc(d.ClientId)}}</span>
            <button class="copy-btn" data-copy="v-cid">Copy</button>
          </div>
          <div class="cred">
            <span class="cred-label">client_secret</span>
            <span class="cred-value" id="v-cs">{{Esc(d.ClientSecret)}}</span>
            <button class="copy-btn" data-copy="v-cs">Copy</button>
          </div>
          <div class="cred">
            <span class="cred-label">token URL</span>
            <span class="cred-value" id="v-turl">POST /token</span>
            <button class="copy-btn" data-copy="v-turl">Copy</button>
          </div>
          <div class="cred">
            <span class="cred-label">token TTL</span>
            <span class="cred-value">{{d.TokenTtlSeconds}} seconds</span>
            <span></span>
          </div>
        </div>
      </div>
""";
    }

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
         .Replace("\"", "&quot;").Replace("'", "&#39;");
}

internal sealed record IndexPageModel(
    string Version,
    string MachineName,
    string Os,
    int HttpPort,
    int HttpsPort,
    bool Port80Active,
    string StartedAtIso,
    string AdminAuthMode,
    string McpDefaultAuthMode,
    PlatformDemoCredentials? PlatformDemo,
    string CertFingerprint,
    string CertNotBefore,
    string CertNotAfter,
    IReadOnlyList<ModuleDisplay> Modules,
    IReadOnlyList<RequestLogEntry> RecentRequests);

internal sealed record PlatformDemoCredentials(
    string BearerToken,
    string ClientId,
    string ClientSecret,
    int TokenTtlSeconds);

internal sealed record ModuleDisplay(
    string Name,
    string Version,
    string DisplayName,
    string Description,
    string MountPath,
    string Maturity,
    string AuthMode,
    bool HasUpdateSource,
    IReadOnlyList<string> ToolNames,
    IReadOnlyList<string> PromptNames,
    IReadOnlyList<string> ResourceNames);
