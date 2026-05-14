namespace WinMcp.Platform.Web;

/// <summary>
/// Live log viewer served at <c>/logs</c>. The HTML is a single self-contained
/// document; all interactivity (date picker, level/source/host filters, raw vs
/// enhanced view, click-to-filter, auto-pause on scroll) runs client-side
/// against <c>/logs/tail</c> and <c>/logs/dates</c>.
/// </summary>
/// <remarks>
/// Carried forward from math-mcp's LogsPage; reclassified <c>srcCategory</c>
/// so that loaded modules (typically <c>WinMcpModule.*</c>) share the platform
/// app color instead of falling into the framework bucket.
/// </remarks>
internal static class LogsPage
{
    public static string Render(LogsPageModel m) => $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>WinMCP — Logs</title>
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
    --src-app: #7c5cff;
    --src-mcp-fw: #4ad6ff;
    --src-fw:  #5a607a;
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
  .wrap { max-width: 1200px; margin: 0 auto; padding: 32px 24px 48px; }
  header { display: flex; align-items: center; gap: 16px; margin-bottom: 20px; }
  .logo {
    width: 40px; height: 40px; flex: 0 0 40px;
    border-radius: 10px;
    background: linear-gradient(135deg, var(--accent) 0%, var(--accent-2) 100%);
    display: grid; place-items: center;
    font-weight: 700; font-size: 18px; color: #0b1020;
  }
  h1 { margin: 0; font-size: 22px; letter-spacing: -0.01em; }
  .subtitle { color: var(--fg-muted); font-size: 13px; margin-top: 2px; }
  .crumbs { color: var(--fg-muted); font-size: 12px; margin-bottom: 8px; }
  .crumbs a { color: var(--accent-2); text-decoration: none; }
  .crumbs a:hover { text-decoration: underline; }
  .pill {
    display: inline-flex; align-items: center; gap: 6px;
    padding: 4px 10px; border-radius: 999px;
    background: rgba(74,214,255,0.10);
    color: var(--accent-2);
    font-size: 12px; font-weight: 600;
    border: 1px solid rgba(74,214,255,0.3);
  }
  .pill.paused {
    background: rgba(251,191,36,0.10);
    color: var(--warn);
    border-color: rgba(251,191,36,0.3);
  }
  .pill .dot {
    width: 8px; height: 8px; border-radius: 50%;
    background: var(--accent-2);
    box-shadow: 0 0 0 0 rgba(74,214,255,0.6);
    animation: pulse 2s infinite;
  }
  .pill.paused .dot { background: var(--warn); animation: none; }
  @keyframes pulse {
    0%   { box-shadow: 0 0 0 0 rgba(74,214,255,0.55); }
    70%  { box-shadow: 0 0 0 10px rgba(74,214,255,0); }
    100% { box-shadow: 0 0 0 0 rgba(74,214,255,0); }
  }
  .toolbar {
    display: flex; align-items: center; gap: 12px;
    background: linear-gradient(180deg, var(--bg-card) 0%, var(--bg-card-2) 100%);
    border: 1px solid var(--border);
    border-radius: 12px;
    padding: 10px 14px;
    margin-bottom: 12px;
    flex-wrap: wrap;
    position: sticky;
    top: 8px;
    z-index: 10;
    box-shadow: 0 4px 16px rgba(0,0,0,0.3);
  }
  .search-box {
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 8px;
    padding: 5px 10px 5px 28px;
    color: var(--fg);
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 12.5px;
    width: 180px;
    background-image: url("data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16' fill='none' stroke='%238a93b3' stroke-width='2'><circle cx='7' cy='7' r='5'/><line x1='10.5' y1='10.5' x2='14' y2='14'/></svg>");
    background-repeat: no-repeat;
    background-position: 8px center;
    background-size: 14px;
    outline: none;
  }
  .search-box:focus { border-color: var(--accent); }
  .counters { display: inline-flex; gap: 6px; }
  .counter {
    display: inline-flex; align-items: center; gap: 4px;
    padding: 3px 8px; border-radius: 999px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 11px; font-weight: 600;
    cursor: pointer;
    user-select: none;
  }
  .counter.warn { background: rgba(251,191,36,0.10); color: var(--warn); border: 1px solid rgba(251,191,36,0.3); }
  .counter.err  { background: rgba(248,113,113,0.10); color: var(--err);  border: 1px solid rgba(248,113,113,0.3); }
  .counter.zero { opacity: 0.4; }
  .counter:hover:not(.zero) { filter: brightness(1.2); }
  .meta { display: flex; align-items: center; gap: 16px; flex: 1; flex-wrap: wrap; }
  .meta-item { font-size: 12.5px; color: var(--fg-muted); }
  .meta-item strong { color: var(--fg); font-weight: 600; font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace; }
  .toolbar-actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
  .btn {
    background: transparent;
    color: var(--fg);
    border: 1px solid var(--border);
    border-radius: 8px;
    padding: 6px 12px;
    font-family: inherit; font-size: 12.5px;
    cursor: pointer;
    transition: all 0.15s ease;
  }
  .btn:hover { border-color: var(--accent-2); color: var(--accent-2); }
  .btn.active { border-color: var(--accent); color: var(--accent); background: rgba(124,92,255,0.08); }
  .segmented {
    display: inline-flex;
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 8px;
    overflow: hidden;
  }
  .segmented button {
    background: transparent; color: var(--fg-muted);
    border: none; cursor: pointer;
    padding: 6px 12px;
    font-family: inherit; font-size: 12px;
    border-right: 1px solid var(--border);
    transition: all 0.15s ease;
  }
  .segmented button:last-child { border-right: none; }
  .segmented button.on {
    color: var(--accent); background: rgba(124,92,255,0.10);
  }
  .filter-chips { display: flex; gap: 6px; flex-wrap: wrap; }
  .chip {
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 999px;
    padding: 3px 10px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 11px;
    cursor: pointer;
    color: var(--fg-muted);
    transition: all 0.15s ease;
    user-select: none;
  }
  .chip:hover { color: var(--fg); }
  .chip.on { color: var(--fg); border-color: rgba(255,255,255,0.25); background: rgba(255,255,255,0.04); }
  .chip[data-level="ERR"].on { color: var(--err); border-color: rgba(248,113,113,0.4); background: rgba(248,113,113,0.08); }
  .chip[data-level="WRN"].on { color: var(--warn); border-color: rgba(251,191,36,0.4); background: rgba(251,191,36,0.08); }
  .chip[data-level="INF"].on { color: var(--accent-2); border-color: rgba(74,214,255,0.4); background: rgba(74,214,255,0.08); }
  .chip[data-level="DBG"].on { color: var(--fg-muted); border-color: rgba(255,255,255,0.18); background: rgba(255,255,255,0.04); }
  .chip[data-special="framework"] { font-size: 10.5px; text-transform: uppercase; letter-spacing: 0.05em; }
  .active-filters {
    display: flex; flex-wrap: wrap; gap: 6px;
    margin-bottom: 10px; padding: 0 4px;
    align-items: center;
  }
  .active-filters .label { font-size: 11px; color: var(--fg-muted); text-transform: uppercase; letter-spacing: 0.06em; }
  .filter-tag {
    display: inline-flex; align-items: center; gap: 6px;
    padding: 3px 8px 3px 10px;
    background: rgba(124,92,255,0.10);
    border: 1px solid rgba(124,92,255,0.35);
    color: var(--accent);
    border-radius: 999px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 11px;
  }
  .filter-tag .x {
    cursor: pointer; padding: 0 4px;
    color: var(--accent); opacity: 0.6;
  }
  .filter-tag .x:hover { opacity: 1; }
  .log {
    background: var(--code-bg);
    border: 1px solid var(--border);
    border-radius: 12px;
    padding: 12px 0;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 12.5px;
    line-height: 1.55;
    max-height: calc(100vh - 240px);
    overflow-y: auto;
    overflow-x: hidden;
    scroll-behavior: auto;
  }
  .row {
    border-left: 3px solid transparent;
    padding: 2px 16px 2px 13px;
    transition: background 0.1s ease;
  }
  .row:hover { background: rgba(255,255,255,0.025); }
  .row.src-app    { border-left-color: var(--src-app); }
  .row.src-mcp-fw { border-left-color: var(--src-mcp-fw); }
  .row.src-fw     { border-left-color: var(--src-fw); }
  .row.WRN { background: rgba(251,191,36,0.05); }
  .row.ERR { background: rgba(248,113,113,0.07); }
  .row.FTL { background: rgba(248,113,113,0.12); }
  .line {
    display: grid;
    grid-template-columns: max-content 40px max-content 1fr;
    gap: 12px;
    white-space: pre-wrap;
    word-break: break-word;
  }
  .line .ts { color: var(--fg-muted); }
  .line .lvl { font-weight: 700; }
  .row.INF .lvl, .row.VRB .lvl { color: var(--accent-2); }
  .row.DBG .lvl { color: var(--fg-muted); }
  .row.WRN .lvl { color: var(--warn); }
  .row.ERR .lvl, .row.FTL .lvl { color: var(--err); }
  .line .src {
    color: var(--accent); cursor: pointer;
    border-radius: 4px; padding: 0 2px; margin: 0 -2px;
  }
  .line .src:hover { background: rgba(124,92,255,0.15); }
  .row.src-mcp-fw .line .src { color: var(--accent-2); }
  .row.src-mcp-fw .line .src:hover { background: rgba(74,214,255,0.15); }
  .row.src-fw .line .src { color: var(--fg-muted); }
  .row.src-fw .line .src:hover { background: rgba(255,255,255,0.08); }
  /* Truncate long host badges; full host on hover (title attr). */
  .line .msg .host-badge {
    max-width: 240px;
    overflow: hidden; white-space: nowrap; text-overflow: ellipsis;
  }
  .line .msg .host-badge .dot { flex: 0 0 7px; }
  /* Stack-trace collapse: hide conts past the first; toggle to expand. */
  .stack-toggle {
    cursor: pointer; padding: 1px 16px 1px 132px;
    color: var(--fg-muted); font-size: 11.5px;
    user-select: none;
  }
  .stack-toggle:hover { color: var(--accent-2); }
  .stack-toggle::before { content: "▶ "; }
  .stack-toggle.expanded::before { content: "▼ "; }
  .row.cont.hidden-stack { display: none; }
  .line .msg { color: var(--fg); }
  .line .msg .host-badge {
    display: inline-flex; align-items: center; gap: 5px;
    padding: 1px 6px; margin: 0 1px;
    border-radius: 999px; border: 1px solid var(--border);
    background: rgba(255,255,255,0.03);
    cursor: pointer;
    font-size: 11px;
  }
  .line .msg .host-badge .dot {
    width: 7px; height: 7px; border-radius: 50%;
  }
  .line .msg .host-badge:hover { border-color: rgba(255,255,255,0.25); }
  .row.cont { background: transparent !important; padding-top: 0; padding-bottom: 0; border-left-color: transparent; }
  .row.cont .line { grid-template-columns: 1fr; padding-left: 120px; }
  .row.cont.ERR .line, .row.cont.FTL .line { color: var(--err); opacity: 0.85; }

  /* Raw mode — minimal styling, literal file dump. */
  .raw-pre {
    margin: 0; padding: 14px 18px;
    white-space: pre-wrap; word-break: break-word;
    font-family: inherit; font-size: 12.5px; line-height: 1.55;
    color: var(--fg);
  }
  .raw-pre .lvl-inf { color: var(--accent-2); font-weight: 700; }
  .raw-pre .lvl-dbg { color: var(--fg-muted); font-weight: 700; }
  .raw-pre .lvl-wrn { color: var(--warn); font-weight: 700; }
  .raw-pre .lvl-err, .raw-pre .lvl-ftl { color: var(--err); font-weight: 700; }
  .raw-pre .ts { color: var(--fg-muted); }

  /* Enhanced — key=value styling */
  .kv {
    display: inline-block;
    padding: 0 6px; margin: 0 1px;
    border-radius: 4px;
    font-size: 11.5px;
    border: 1px solid var(--border);
    background: rgba(255,255,255,0.03);
  }
  .kv.status-ok   { color: var(--ok);   border-color: rgba(52,211,153,0.35);  background: rgba(52,211,153,0.10); }
  .kv.status-warn { color: var(--warn); border-color: rgba(251,191,36,0.35);  background: rgba(251,191,36,0.10); }
  .kv.status-err  { color: var(--err);  border-color: rgba(248,113,113,0.35); background: rgba(248,113,113,0.10); }
  .kv.dim { color: var(--fg-muted); }

  /* Disabled state for filter UI when Raw is active */
  body.is-raw #filters,
  body.is-raw #active-filters { opacity: 0.35; pointer-events: none; }
  body.is-raw .filter-hint { display: inline; }
  .filter-hint { display: none; font-size: 11px; color: var(--fg-muted); margin-left: 6px; }
  .footer-info {
    text-align: center; color: var(--fg-muted); font-size: 12px;
    margin-top: 14px;
  }
  .footer-info a { color: var(--accent-2); text-decoration: none; }
  .footer-info a:hover { text-decoration: underline; }
  .empty {
    text-align: center; color: var(--fg-muted); padding: 24px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
    font-size: 12.5px;
  }
  .auto-paused-hint {
    font-size: 10.5px; color: var(--warn);
    margin-left: 6px;
  }
</style>
</head>
<body>
  <div class="wrap">
    <div class="crumbs"><a href="/">← WinMCP</a></div>
    <header>
      <div class="logo">≡</div>
      <div style="flex:1">
        <h1>Logs</h1>
        <div class="subtitle">Today, tail of last 500 lines</div>
      </div>
      <div class="counters">
        <span class="counter warn zero" id="c-wrn" title="Warnings — click to filter">0 W</span>
        <span class="counter err zero" id="c-err" title="Errors — click to filter">0 E</span>
      </div>
      <div class="pill" id="status"><span class="dot"></span> <span id="status-text">Live</span><span id="auto-pause-hint" class="auto-paused-hint" hidden>(auto)</span></div>
    </header>

    <div class="toolbar">
      <div class="meta">
        <span class="meta-item">Date
          <select id="m-date" class="search-box" style="width:auto; padding-left:8px; background-image:none; cursor:pointer; margin-left:4px;">
            <option value="">Today (live)</option>
          </select>
        </span>
        <span class="meta-item">Size <strong id="m-size">—</strong></span>
        <span class="meta-item">Shown <strong id="m-shown">—</strong> / <strong id="m-total">—</strong></span>
        <span class="meta-item">Refreshed <strong id="last-refresh">just now</strong></span>
      </div>
      <div class="toolbar-actions">
        <div class="segmented" id="view-mode" title="View mode">
          <button data-view="enhanced" class="on">Enhanced</button>
          <button data-view="raw">Raw</button>
        </div>
        <span class="filter-hint">filters apply to Enhanced only</span>
        <div class="segmented" id="order-mode" title="Order">
          <button data-order="newest" class="on">Newest first</button>
          <button data-order="oldest">Oldest first</button>
        </div>
        <div class="filter-chips" id="filters">
          <span class="chip on" data-level="INF">INF</span>
          <span class="chip on" data-level="DBG">DBG</span>
          <span class="chip on" data-level="WRN">WRN</span>
          <span class="chip on" data-level="ERR">ERR</span>
          <span class="chip" data-special="framework" title="Show framework (Microsoft.*) lines in Enhanced view">Framework</span>
          <span class="chip" data-special="mcp-fw" title="Show MCP SDK (ModelContextProtocol.*) lifecycle lines">MCP SDK</span>
        </div>
        <input type="text" class="search-box" id="search" placeholder="Search…" autocomplete="off">
        <button class="btn" id="errors-btn" title="Show only WRN/ERR/FTL">Errors only</button>
        <button class="btn" id="clear-btn" title="Reset all filters">Clear</button>
        <button class="btn" id="pause-btn">Pause</button>
      </div>
    </div>

    <div class="active-filters" id="active-filters" hidden>
      <span class="label">Filtering:</span>
    </div>

    <div class="log" id="log"><div class="empty">Loading…</div></div>

    <div class="footer-info">
      Path: <code>{{m.LogFilePath}}</code>
      &middot; <a href="/" target="_blank" rel="noopener">Home</a>
      &middot; <a href="/info" target="_blank" rel="noopener">JSON</a>
      &middot; <a href="/health" target="_blank" rel="noopener">Health</a>
      &middot; <a href="https://github.com/ryanhebert/WinMCP" target="_blank" rel="noopener">GitHub ↗</a>
      &middot; <a href="https://github.com/ryanhebert/WinMCP/releases" target="_blank" rel="noopener">Releases ↗</a>
    </div>
  </div>

<script>
  // ===== State =====
  const enabled = { INF: true, DBG: true, WRN: true, ERR: true, FTL: true, VRB: true };
  let viewMode = 'enhanced';        // 'enhanced' | 'raw'
  let orderMode = 'newest';         // 'newest' | 'oldest'
  let showFramework = false;
  let showMcpFw = false;            // hide ModelContextProtocol.* INF/DBG by default
  let sourceFilter = null;          // exact src match
  let hostFilter = null;            // exact host match
  let searchQuery = '';
  let pausedManual = false;
  let pausedAuto = false;
  let lastRefreshTime = Date.now();
  let records = [];
  let rawText = '';
  let selectedDate = '';   // '' = today/live; 'yyyy-mm-dd' = historical

  const logEl     = document.getElementById('log');
  const filtersEl = document.getElementById('filters');
  const pauseBtn  = document.getElementById('pause-btn');
  const errorsBtn = document.getElementById('errors-btn');
  const clearBtn  = document.getElementById('clear-btn');
  const searchEl  = document.getElementById('search');
  const cWrn      = document.getElementById('c-wrn');
  const cErr      = document.getElementById('c-err');
  const dateSel   = document.getElementById('m-date');
  const statusEl  = document.getElementById('status');
  const statusTxt = document.getElementById('status-text');
  const autoHint  = document.getElementById('auto-pause-hint');
  const lastRef   = document.getElementById('last-refresh');
  const mSize     = document.getElementById('m-size');
  const mShown    = document.getElementById('m-shown');
  const mTotal    = document.getElementById('m-total');
  const viewSel   = document.getElementById('view-mode');
  const orderSel  = document.getElementById('order-mode');
  const activeFiltersEl = document.getElementById('active-filters');

  // ===== Persistence =====
  const STATE_KEY = 'winmcp.logs.v1';
  function saveState() {
    try {
      localStorage.setItem(STATE_KEY, JSON.stringify({
        viewMode, orderMode, enabled, showFramework, showMcpFw, searchQuery
      }));
    } catch (e) { /* private mode etc. — ignore */ }
  }
  function loadState() {
    try {
      const raw = localStorage.getItem(STATE_KEY);
      if (!raw) return;
      const s = JSON.parse(raw);
      if (s.viewMode === 'raw' || s.viewMode === 'enhanced') viewMode = s.viewMode;
      if (s.orderMode === 'newest' || s.orderMode === 'oldest') orderMode = s.orderMode;
      if (s.enabled && typeof s.enabled === 'object') Object.assign(enabled, s.enabled);
      if (typeof s.showFramework === 'boolean') showFramework = s.showFramework;
      if (typeof s.showMcpFw === 'boolean')     showMcpFw     = s.showMcpFw;
      if (typeof s.searchQuery === 'string')    searchQuery   = s.searchQuery;
    } catch (e) { /* ignore */ }
  }
  loadState();

  // ===== Helpers =====
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
    if (!host) return '';
    const i = host.indexOf(':');
    return i > 0 ? host.slice(0, i) : host;
  }
  function srcCategory(src) {
    if (!src) return 'other';
    // Platform code lives under WinMcp.*; modules ship under WinMcpModule.*.
    // Both colour as the "app" channel so module logs stand out from
    // framework noise the same way platform logs do.
    if (src.startsWith('WinMcp')) return 'app';
    if (src.startsWith('ModelContextProtocol')) return 'mcp-fw';
    if (src.startsWith('Microsoft')) return 'fw';
    return 'other';
  }
  function srcClass(cat) {
    return cat === 'app'    ? 'src-app' :
           cat === 'mcp-fw' ? 'src-mcp-fw' :
           cat === 'fw'     ? 'src-fw'  : '';
  }

  // ===== Parse =====
  const LINE_RE = /^(\d{4}-\d{2}-\d{2} (\d{2}:\d{2}:\d{2}\.\d{3}) [+\-]\d{2}:\d{2}) \[([A-Z]{3})\] (\S+)? ?(.*)$/;
  const HOST_RE = /\bhost=([^\s]+)/;

  function parseRecords(text) {
    // Serilog on Windows writes CRLF; split on either newline form so the
    // regex's $ anchor isn't tripped by a trailing \r.
    const lines = text.split(/\r?\n/);
    if (lines.length && lines[lines.length - 1] === '') lines.pop();
    const out = [];
    let cur = null;
    for (const raw of lines) {
      const m = raw.match(LINE_RE);
      if (m) {
        const [, ts, hms, level, src, msg] = m;
        const cat = srcCategory(src || '');
        const hostMatch = (msg || '').match(HOST_RE);
        cur = {
          ts, hms,
          level,
          src: src || '',
          msg: msg || '',
          cat,
          host: hostMatch ? hostMatch[1] : '',
          conts: [],
        };
        out.push(cur);
      } else if (raw.trim() && cur) {
        cur.conts.push(raw);
      }
    }
    return out;
  }

  // ===== Filter =====
  function visible(rec) {
    if (!enabled[rec.level]) return false;
    if (sourceFilter && rec.src !== sourceFilter) return false;
    if (hostFilter && rec.host !== hostFilter) return false;
    if (viewMode === 'enhanced') {
      const isImportant = rec.level === 'WRN' || rec.level === 'ERR' || rec.level === 'FTL';
      if (rec.cat === 'fw'     && !isImportant && !showFramework) return false;
      if (rec.cat === 'mcp-fw' && !isImportant && !showMcpFw)     return false;
    }
    if (searchQuery) {
      const q = searchQuery.toLowerCase();
      const hay = (rec.msg + ' ' + rec.src + ' ' + rec.host + ' ' + rec.level + ' ' +
                   rec.conts.join(' ')).toLowerCase();
      if (!hay.includes(q)) return false;
    }
    return true;
  }

  // ===== Render =====
  function statusKvClass(code) {
    const n = parseInt(code, 10);
    if (n >= 500) return 'status-err';
    if (n >= 400) return 'status-warn';
    return 'status-ok';
  }

  function renderMsg(msg, host) {
    let html = esc(msg);
    // status=NNN → colored chip
    html = html.replace(/\b(status)=(\d{3})\b/g, (_, k, v) =>
      `<span class="kv ${statusKvClass(v)}">${k}=${v}</span>`);
    // dur=Xms or expires_in=Xs → dim chip
    html = html.replace(/\b(dur|expires_in|content_type|grant_type|reason)=("[^"]*"|\S+)/g,
      (_, k, v) => `<span class="kv dim">${k}=${v}</span>`);
    // ip=… and client_id=… → neutral chip
    html = html.replace(/\b(ip|client_id|method)=(\S+)/g,
      (_, k, v) => `<span class="kv">${k}=${v}</span>`);
    // host=… → clickable host badge with color dot (kept last so above regex doesn't grab it)
    if (host) {
      const dotColor = colorFor(hostOnly(host));
      const safeHost = host.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      const re = new RegExp(`(host=)(${safeHost})`);
      html = html.replace(re, (_, p1, p2) =>
        `<span class="host-badge" data-host="${esc(host)}" title="Click to filter by this origin">` +
        `<span class="dot" style="background:${dotColor}"></span>${p1}${esc(p2)}</span>`);
    }
    return html;
  }

  function buildRow(rec) {
    const row = document.createElement('div');
    row.className = `row ${rec.level} ${srcClass(rec.cat)}`;
    if (rec.host) row.dataset.host = rec.host;
    if (rec.src)  row.dataset.src  = rec.src;
    row.innerHTML =
      `<div class="line">` +
        `<span class="ts">${esc(rec.hms)}</span>` +
        `<span class="lvl">${esc(rec.level)}</span>` +
        `<span class="src" data-click="src" title="Click to filter by this source">${esc(rec.src)}</span>` +
        `<span class="msg">${renderMsg(rec.msg, rec.host)}</span>` +
      `</div>`;
    const out = [row];
    // Stack-trace collapse: when 2+ continuation lines exist, show the first
    // inline and tuck the rest behind a clickable "▶ N more" toggle.
    const conts = rec.conts;
    const collapseFrom = conts.length >= 2 ? 1 : conts.length;
    for (let i = 0; i < collapseFrom; i++) {
      const cd = document.createElement('div');
      cd.className = `row cont ${rec.level}`;
      cd.innerHTML = `<div class="line">${esc(conts[i])}</div>`;
      out.push(cd);
    }
    if (conts.length > collapseFrom) {
      const hidden = [];
      for (let i = collapseFrom; i < conts.length; i++) {
        const cd = document.createElement('div');
        cd.className = `row cont hidden-stack ${rec.level}`;
        cd.innerHTML = `<div class="line">${esc(conts[i])}</div>`;
        hidden.push(cd);
      }
      const toggle = document.createElement('div');
      toggle.className = 'stack-toggle';
      toggle.textContent = `${conts.length - collapseFrom} more stack frame${conts.length - collapseFrom === 1 ? '' : 's'}`;
      toggle.addEventListener('click', () => {
        const expanded = toggle.classList.toggle('expanded');
        hidden.forEach(h => h.classList.toggle('hidden-stack', !expanded));
        toggle.textContent = expanded
          ? `hide ${conts.length - collapseFrom} stack frame${conts.length - collapseFrom === 1 ? '' : 's'}`
          : `${conts.length - collapseFrom} more stack frame${conts.length - collapseFrom === 1 ? '' : 's'}`;
      });
      out.push(toggle);
      out.push(...hidden);
    }
    return out;
  }

  function updateCounters() {
    const w = records.reduce((n, r) => n + (r.level === 'WRN' ? 1 : 0), 0);
    const e = records.reduce((n, r) => n + (r.level === 'ERR' || r.level === 'FTL' ? 1 : 0), 0);
    cWrn.textContent = `${w} W`;
    cErr.textContent = `${e} E`;
    cWrn.classList.toggle('zero', w === 0);
    cErr.classList.toggle('zero', e === 0);
  }

  function render() {
    const wasAtNewest = atNewestEdge();
    if (viewMode === 'raw') {
      renderRaw();
    } else {
      renderEnhanced();
    }
    renderActiveFilters();
    updateCounters();
    if (wasAtNewest) snapToNewest();
  }

  // Sync UI controls with current state (called once at startup after loadState).
  function syncUi() {
    document.body.classList.toggle('is-raw', viewMode === 'raw');
    viewSel.querySelectorAll('button').forEach(b =>
      b.classList.toggle('on', b.dataset.view === viewMode));
    orderSel.querySelectorAll('button').forEach(b =>
      b.classList.toggle('on', b.dataset.order === orderMode));
    filtersEl.querySelectorAll('.chip[data-level]').forEach(c => {
      c.classList.toggle('on', !!enabled[c.dataset.level]);
    });
    filtersEl.querySelector('.chip[data-special="framework"]').classList.toggle('on', showFramework);
    filtersEl.querySelector('.chip[data-special="mcp-fw"]').classList.toggle('on', showMcpFw);
    searchEl.value = searchQuery;
  }
  syncUi();

  function renderRaw() {
    // True file dump — no filtering. Lines reordered if newest-first; otherwise
    // exactly as on disk.
    if (!rawText) {
      logEl.innerHTML = '<div class="empty">Log file is empty.</div>';
      mShown.textContent = '0';
      mTotal.textContent = '0';
      return;
    }
    const lines = rawText.split(/\r?\n/);
    if (lines.length && lines[lines.length - 1] === '') lines.pop();
    const ordered = orderMode === 'newest' ? [...lines].reverse() : lines;
    // Build HTML: escape, tint level tokens, dim timestamps. No filters applied.
    let html = ordered.map(esc).join('\n');
    html = html.replace(/\[INF\]/g, '<span class="lvl-inf">[INF]</span>');
    html = html.replace(/\[DBG\]/g, '<span class="lvl-dbg">[DBG]</span>');
    html = html.replace(/\[VRB\]/g, '<span class="lvl-dbg">[VRB]</span>');
    html = html.replace(/\[WRN\]/g, '<span class="lvl-wrn">[WRN]</span>');
    html = html.replace(/\[ERR\]/g, '<span class="lvl-err">[ERR]</span>');
    html = html.replace(/\[FTL\]/g, '<span class="lvl-ftl">[FTL]</span>');
    html = html.replace(
      /^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2})/gm,
      '<span class="ts">$1</span>');
    logEl.innerHTML = `<pre class="raw-pre">${html}</pre>`;
    mShown.textContent = String(lines.length);
    mTotal.textContent = String(lines.length);
  }

  function renderEnhanced() {
    const filtered = records.filter(visible);
    const ordered = orderMode === 'newest' ? [...filtered].reverse() : filtered;
    if (!ordered.length) {
      logEl.innerHTML = '<div class="empty">No log lines match current filters.</div>';
    } else {
      const frag = document.createDocumentFragment();
      for (const r of ordered) for (const el of buildRow(r)) frag.appendChild(el);
      logEl.replaceChildren(frag);
    }
    mShown.textContent = String(filtered.length);
    mTotal.textContent = String(records.length);
  }

  function atNewestEdge() {
    if (orderMode === 'newest') return logEl.scrollTop < 8;
    return (logEl.scrollHeight - logEl.scrollTop - logEl.clientHeight) < 8;
  }
  function snapToNewest() {
    if (orderMode === 'newest') logEl.scrollTop = 0;
    else logEl.scrollTop = logEl.scrollHeight;
  }

  function renderActiveFilters() {
    const tags = [];
    if (sourceFilter) tags.push({ kind: 'source', value: sourceFilter });
    if (hostFilter)   tags.push({ kind: 'host',   value: hostFilter });
    if (!tags.length) { activeFiltersEl.hidden = true; activeFiltersEl.innerHTML = '<span class="label">Filtering:</span>'; return; }
    activeFiltersEl.hidden = false;
    activeFiltersEl.innerHTML = '<span class="label">Filtering:</span>' +
      tags.map(t => `<span class="filter-tag">${t.kind}=${esc(t.value)}<span class="x" data-clear="${t.kind}">×</span></span>`).join('');
  }

  // ===== Pause / live state =====
  function setStatusUi() {
    const paused = pausedManual || pausedAuto;
    statusEl.classList.toggle('paused', paused);
    statusTxt.textContent = paused ? 'Paused' : 'Live';
    autoHint.hidden = !(pausedAuto && !pausedManual);
    pauseBtn.textContent = pausedManual ? 'Resume' : 'Pause';
    pauseBtn.classList.toggle('active', pausedManual);
  }

  pauseBtn.addEventListener('click', () => {
    pausedManual = !pausedManual;
    if (!pausedManual) {
      // Re-evaluate auto-pause based on current scroll
      pausedAuto = !atNewestEdge();
    }
    setStatusUi();
  });

  // Auto-pause when reader scrolls away from the "newest" edge; resume when back.
  let scrollTick = false;
  logEl.addEventListener('scroll', () => {
    if (scrollTick) return;
    scrollTick = true;
    requestAnimationFrame(() => {
      scrollTick = false;
      const atEdge = atNewestEdge();
      const wasAuto = pausedAuto;
      pausedAuto = !atEdge;
      if (wasAuto !== pausedAuto) setStatusUi();
    });
  });

  // ===== Filter chips =====
  filtersEl.addEventListener('click', e => {
    const chip = e.target.closest('.chip');
    if (!chip) return;
    if (chip.dataset.level) {
      const lvl = chip.dataset.level;
      enabled[lvl] = !enabled[lvl];
      chip.classList.toggle('on', enabled[lvl]);
    } else if (chip.dataset.special === 'framework') {
      showFramework = !showFramework;
      chip.classList.toggle('on', showFramework);
    } else if (chip.dataset.special === 'mcp-fw') {
      showMcpFw = !showMcpFw;
      chip.classList.toggle('on', showMcpFw);
    }
    saveState(); render();
  });

  // ===== View / Order toggles =====
  viewSel.addEventListener('click', e => {
    const btn = e.target.closest('button[data-view]');
    if (!btn) return;
    viewMode = btn.dataset.view;
    viewSel.querySelectorAll('button').forEach(b => b.classList.toggle('on', b.dataset.view === viewMode));
    document.body.classList.toggle('is-raw', viewMode === 'raw');
    saveState(); render();
  });
  orderSel.addEventListener('click', e => {
    const btn = e.target.closest('button[data-order]');
    if (!btn) return;
    orderMode = btn.dataset.order;
    orderSel.querySelectorAll('button').forEach(b => b.classList.toggle('on', b.dataset.order === orderMode));
    saveState(); render();
    snapToNewest();
  });

  // ===== Search =====
  let searchTimer = null;
  searchEl.addEventListener('input', () => {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => {
      searchQuery = searchEl.value;
      saveState(); render();
    }, 120);
  });

  // ===== Presets =====
  errorsBtn.addEventListener('click', () => {
    enabled.INF = false; enabled.DBG = false; enabled.VRB = false;
    enabled.WRN = true;  enabled.ERR = true;  enabled.FTL = true;
    syncUi(); saveState(); render();
  });
  clearBtn.addEventListener('click', () => {
    enabled.INF = true; enabled.DBG = true; enabled.WRN = true;
    enabled.ERR = true; enabled.FTL = true; enabled.VRB = true;
    showFramework = false; showMcpFw = false;
    sourceFilter = null; hostFilter = null;
    searchQuery = '';
    syncUi(); saveState(); render();
  });

  // Counter pills click to filter to that level.
  cWrn.addEventListener('click', () => {
    if (cWrn.classList.contains('zero')) return;
    enabled.INF = false; enabled.DBG = false; enabled.VRB = false;
    enabled.WRN = true;  enabled.ERR = false; enabled.FTL = false;
    syncUi(); saveState(); render();
  });
  cErr.addEventListener('click', () => {
    if (cErr.classList.contains('zero')) return;
    enabled.INF = false; enabled.DBG = false; enabled.VRB = false;
    enabled.WRN = false; enabled.ERR = true;  enabled.FTL = true;
    syncUi(); saveState(); render();
  });

  // ===== Click-to-filter =====
  logEl.addEventListener('click', e => {
    const hostEl = e.target.closest('[data-host]');
    if (hostEl && (e.target.closest('.host-badge') || e.target.dataset.click === 'host')) {
      hostFilter = hostEl.dataset.host;
      render();
      return;
    }
    const srcEl = e.target.closest('[data-click="src"]');
    if (srcEl) {
      const row = srcEl.closest('.row');
      if (row && row.dataset.src) {
        sourceFilter = row.dataset.src;
        render();
      }
    }
  });

  activeFiltersEl.addEventListener('click', e => {
    const x = e.target.closest('[data-clear]');
    if (!x) return;
    if (x.dataset.clear === 'source') sourceFilter = null;
    if (x.dataset.clear === 'host')   hostFilter = null;
    render();
  });

  // ===== Date picker =====
  async function loadDates() {
    try {
      const r = await fetch('/logs/dates', { cache: 'no-store' });
      if (!r.ok) return;
      const dates = await r.json();
      const today = new Date().toISOString().slice(0, 10);
      for (const d of dates) {
        if (d === today) continue; // already covered by the "Today" entry
        const opt = document.createElement('option');
        opt.value = d;
        opt.textContent = d;
        dateSel.appendChild(opt);
      }
    } catch (_) { /* offline; ignore */ }
  }
  dateSel.addEventListener('change', () => {
    selectedDate = dateSel.value;
    // Reset auto-pause state; for historical dates we don't want to auto-pause
    // since there's no new content arriving anyway.
    pausedAuto = false;
    setStatusUi();
    refresh();
    restartLivePoll();
  });
  loadDates();

  // ===== Fetch loop =====
  async function refresh() {
    // Live tail pauses on manual or auto-pause. Historical dates always refresh
    // once (no new data to skip).
    if (!selectedDate && (pausedManual || pausedAuto)) return;
    try {
      const url = selectedDate
        ? `/logs/tail?n=500&date=${encodeURIComponent(selectedDate)}`
        : '/logs/tail?n=500';
      const res = await fetch(url, { cache: 'no-store' });
      if (!res.ok) return;
      const text = await res.text();
      mSize.textContent = `${(text.length / 1024).toFixed(1)} KB`;
      rawText = text;
      records = parseRecords(text);
      render();
      lastRefreshTime = Date.now();
      lastRef.textContent = selectedDate ? `loaded ${selectedDate}` : 'just now';
    } catch (e) { /* swallow */ }
  }

  setInterval(() => {
    if (pausedManual || pausedAuto) return;
    const elapsed = Math.floor((Date.now() - lastRefreshTime) / 1000);
    lastRef.textContent = elapsed === 0 ? 'just now' : `${elapsed}s ago`;
  }, 1000);

  let livePollTimer = null;
  function restartLivePoll() {
    if (livePollTimer) clearInterval(livePollTimer);
    // Only auto-refresh when viewing the live (today) tail; historical files
    // don't change, so polling is wasted work.
    if (!selectedDate) livePollTimer = setInterval(refresh, 3000);
  }

  refresh().then(() => snapToNewest());
  restartLivePoll();
</script>
</body>
</html>
""";
}

internal sealed record LogsPageModel(
    string LogFileName,
    string LogFilePath);
