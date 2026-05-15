using System.Runtime.Versioning;
using System.Text.Json;

namespace WinMcp.Platform.Web;

/// <summary>
/// Editable settings page at <c>/settings</c>. Three client-side tabs
/// (Identity providers / Admin auth / MCP default auth) backed by the
/// REST endpoints under <c>/api/settings/*</c>. Restart-required apply
/// semantics — successful saves flip an in-process flag that the
/// dashboard renders as a global "Restart pending" banner.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SettingsPage
{
    public static string Render(SettingsPageModel m)
    {
        var initialJson = JsonSerializer.Serialize(m.Snapshot, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>WinMCP — Settings</title>
<link rel="icon" href="/favicon.svg" type="image/svg+xml">
<style>
  :root {
    --bg: #0b1020; --bg-card: #131a30; --bg-card-2: #1a2240;
    --fg: #e6ecff; --fg-muted: #8a93b3;
    --accent: #7c5cff; --accent-2: #4ad6ff;
    --ok: #34d399; --warn: #fbbf24; --err: #f87171;
    --border: rgba(255,255,255,0.08); --code-bg: #0f1428;
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
  .crumbs { color: var(--fg-muted); font-size: 12px; margin-bottom: 8px; }
  .crumbs a { color: var(--accent-2); text-decoration: none; }
  header { display: flex; align-items: center; gap: 16px; margin-bottom: 24px; }
  h1 { margin: 0; font-size: 26px; }
  .btn {
    background: transparent; color: var(--fg);
    border: 1px solid var(--border); border-radius: 8px;
    padding: 6px 12px; font: inherit; font-size: 12.5px;
    cursor: pointer;
    transition: all 0.15s ease;
  }
  .btn:hover { border-color: var(--accent-2); color: var(--accent-2); }
  .btn.primary {
    background: rgba(124,92,255,0.10);
    border-color: rgba(124,92,255,0.5);
    color: var(--accent);
  }
  .btn.primary:hover { background: rgba(124,92,255,0.18); }
  .btn:disabled { opacity: 0.45; cursor: not-allowed; }
  /* Tabs */
  .tabs { display: flex; gap: 4px; border-bottom: 1px solid var(--border); margin-bottom: 20px; }
  .tab {
    background: transparent; color: var(--fg-muted);
    border: none; cursor: pointer;
    padding: 10px 18px; font: inherit; font-size: 13px;
    border-bottom: 2px solid transparent;
  }
  .tab:hover { color: var(--fg); }
  .tab.on { color: var(--fg); border-bottom-color: var(--accent); }
  .tab-panel { display: none; }
  .tab-panel.on { display: block; }
  /* Forms */
  .field { margin: 12px 0; display: grid; grid-template-columns: 160px 1fr; gap: 10px; align-items: center; }
  .field label { color: var(--fg-muted); font-size: 13px; }
  .field input[type="text"], .field input[type="url"], .field select {
    background: var(--code-bg); color: var(--fg);
    border: 1px solid var(--border); border-radius: 6px;
    padding: 6px 10px; font: inherit; font-size: 13px;
    font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace;
  }
  .field input:focus, .field select:focus { outline: none; border-color: var(--accent); }
  .field-error { grid-column: 2; color: var(--err); font-size: 12px; }
  .helper { grid-column: 2; color: var(--fg-muted); font-size: 12px; }
  /* Provider card */
  .provider {
    background: var(--code-bg); border: 1px solid var(--border);
    border-radius: 10px; padding: 14px; margin: 10px 0;
  }
  .provider-row { display: flex; gap: 12px; align-items: baseline; flex-wrap: wrap; }
  .provider-name { font-weight: 600; font-family: ui-monospace, "SF Mono", Menlo, Consolas, monospace; }
  .provider-issuer { color: var(--fg-muted); font-size: 12px; word-break: break-all; }
  .provider-status { font-size: 11px; }
  .provider-status.ok { color: var(--ok); }
  .provider-status.err { color: var(--err); }
  .provider-actions { margin-left: auto; display: flex; gap: 6px; }
  /* Restart banner */
  .restart-banner {
    display: none;
    background: linear-gradient(90deg, rgba(251,191,36,0.18) 0%, rgba(124,92,255,0.12) 100%);
    border: 1px solid rgba(251,191,36,0.4);
    border-radius: 10px;
    padding: 10px 14px; margin-bottom: 18px;
    font-size: 13px;
    align-items: center; gap: 12px;
  }
  .restart-banner.show { display: flex; }
  .restart-banner .dot {
    width: 8px; height: 8px; border-radius: 50%;
    background: var(--warn); box-shadow: 0 0 8px rgba(251,191,36,0.6);
  }
  /* Banners */
  .lockout-warn {
    background: rgba(248,113,113,0.08); border: 1px solid rgba(248,113,113,0.35);
    color: var(--err); padding: 10px 14px; border-radius: 8px;
    font-size: 13px; margin: 12px 0;
  }
  .none-warn {
    background: rgba(251,191,36,0.06); border: 1px solid rgba(251,191,36,0.25);
    color: var(--warn); padding: 8px 12px; border-radius: 8px;
    font-size: 12px; margin: 8px 0;
  }
  /* Empty state */
  .empty {
    color: var(--fg-muted); font-size: 13px; text-align: center;
    padding: 32px; border: 1px dashed var(--border); border-radius: 10px;
  }
</style>
</head>
<body>
  <div class="wrap">
    <div class="crumbs"><a href="/">← Dashboard</a></div>

    <div class="restart-banner" id="restart-banner">
      <span class="dot"></span>
      <span><strong>Restart pending</strong> — your settings changes won't take effect until WinMCP restarts.</span>
      <span style="margin-left:auto"><button class="btn primary" id="restart-now-btn">Restart now</button></span>
    </div>

    <header>
      <h1>Settings</h1>
    </header>

    <div class="tabs" role="tablist">
      <button class="tab on" data-tab="providers">Identity providers</button>
      <button class="tab" data-tab="admin">Admin auth</button>
      <button class="tab" data-tab="mcp">MCP default auth</button>
    </div>

    <div class="tab-panel on" id="tab-providers">
      <div id="providers-list"></div>
      <button class="btn primary" id="add-provider-btn">+ Add provider</button>
      <form id="add-provider-form" style="display:none; margin-top:12px;">
        <div class="field">
          <label for="np-name">Name</label>
          <input type="text" id="np-name" placeholder="okta" required>
        </div>
        <div class="field">
          <label for="np-issuer">Issuer URL</label>
          <input type="url" id="np-issuer" placeholder="https://issuer.example.com" required>
        </div>
        <div class="field">
          <label for="np-audience">Audience</label>
          <input type="text" id="np-audience" value="winmcp">
        </div>
        <div class="field"><span></span><span class="field-error" id="np-error" style="display:none"></span></div>
        <div class="field">
          <span></span>
          <span>
            <button class="btn primary" type="submit" id="np-save">Discover + save</button>
            <button class="btn" type="button" id="np-cancel">Cancel</button>
          </span>
        </div>
      </form>
    </div>

    <div class="tab-panel" id="tab-admin">
      <div id="admin-form-container"></div>
    </div>

    <div class="tab-panel" id="tab-mcp">
      <div id="mcp-form-container"></div>
    </div>
  </div>

<script>
  const INITIAL = {{initialJson}};
  let state = INITIAL;

  // ---- Tabs ----
  document.querySelectorAll('.tab').forEach(t => t.addEventListener('click', () => {
    document.querySelectorAll('.tab').forEach(x => x.classList.toggle('on', x === t));
    document.querySelectorAll('.tab-panel').forEach(p =>
      p.classList.toggle('on', p.id === 'tab-' + t.dataset.tab));
  }));

  // ---- Helpers ----
  function esc(s) {
    return String(s).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  }
  async function api(method, path, body) {
    const res = await fetch(path, {
      method, cache: 'no-store',
      headers: body ? { 'Content-Type': 'application/json' } : {},
      body: body ? JSON.stringify(body) : undefined,
    });
    const text = await res.text();
    let json = null;
    if (text) {
      try { json = JSON.parse(text); } catch (_) { /* non-JSON body, leave null */ }
    }
    return { ok: res.ok, status: res.status, json };
  }

  // ---- Restart banner ----
  async function pollRestartPending() {
    try {
      const r = await api('GET', '/api/settings/restart-pending');
      if (r.ok && r.json.pending) {
        document.getElementById('restart-banner').classList.add('show');
      }
    } catch (_) { /* ignore */ }
  }
  document.getElementById('restart-now-btn').addEventListener('click', async () => {
    if (!confirm('Restart WinMCP now? Connected MCP clients will see a brief outage (~10s).')) return;
    await api('POST', '/api/settings/restart');
    waitForRestartComplete();
  });
  async function waitForRestartComplete() {
    document.getElementById('restart-banner').innerHTML =
      '<span class="dot"></span><span>Restarting… waiting for the service to come back online.</span>';
    const start = Date.now();
    while (Date.now() - start < 240000) {
      await new Promise(r => setTimeout(r, 1000));
      try {
        const r = await fetch('/info', { cache: 'no-store' });
        if (r.ok) { location.reload(); return; }
      } catch (_) { /* still down */ }
    }
    document.getElementById('restart-banner').innerHTML =
      '<span class="dot" style="background:var(--err)"></span><span>Restart timed out. Check /logs.</span>';
  }
  pollRestartPending();
  setInterval(pollRestartPending, 10000);

  // ---- Identity providers ----
  function renderProviders() {
    const list = document.getElementById('providers-list');
    if (!state.oidcProviders || state.oidcProviders.length === 0) {
      list.innerHTML = '<div class="empty">No identity providers configured. Add one to use OIDC mode for admin or MCP auth.</div>';
      return;
    }
    list.innerHTML = state.oidcProviders.map(p => `
      <div class="provider" data-name="${esc(p.name)}">
        <div class="provider-row">
          <span class="provider-name">${esc(p.name)}</span>
          <span class="provider-status ${p.discoveredAtIso ? 'ok' : 'err'}">
            ${p.discoveredAtIso ? '✓ Discovered ' + esc(p.discoveredAtIso.slice(0, 19)) : '✗ Not yet discovered'}
          </span>
          <div class="provider-actions">
            <button class="btn" data-action="rediscover" data-name="${esc(p.name)}">Re-discover</button>
            <button class="btn" data-action="delete" data-name="${esc(p.name)}">Delete</button>
          </div>
        </div>
        <div class="provider-issuer">${esc(p.issuer)} &middot; audience: ${esc(p.audience || 'winmcp')}</div>
        ${p.inUseBy && p.inUseBy.length ? '<div class="provider-issuer">In use by: ' + p.inUseBy.map(esc).join(', ') + '</div>' : ''}
      </div>
    `).join('');
    list.querySelectorAll('button[data-action]').forEach(b => {
      b.addEventListener('click', () => providerAction(b.dataset.action, b.dataset.name));
    });
  }
  async function providerAction(action, name) {
    if (action === 'rediscover') {
      const r = await api('POST', `/api/settings/oidc-providers/${encodeURIComponent(name)}/rediscover`);
      if (!r.ok) { alert(`Re-discover failed: ${r.json?.message ?? r.status}`); return; }
      const snap = await api('GET', '/api/settings');
      state = snap.json; renderProviders(); renderAdminForm(); renderMcpForm();
    }
    if (action === 'delete') {
      if (!confirm(`Delete provider ${name}?`)) return;
      const r = await api('DELETE', `/api/settings/oidc-providers/${encodeURIComponent(name)}`);
      if (!r.ok) { alert(`Delete failed: ${r.json?.message ?? r.status}`); return; }
      const snap = await api('GET', '/api/settings');
      state = snap.json; renderProviders(); renderAdminForm(); renderMcpForm();
      pollRestartPending();
    }
  }
  document.getElementById('add-provider-btn').addEventListener('click', () => {
    document.getElementById('add-provider-form').style.display = '';
    document.getElementById('add-provider-btn').style.display = 'none';
  });
  document.getElementById('np-cancel').addEventListener('click', () => {
    document.getElementById('add-provider-form').style.display = 'none';
    document.getElementById('add-provider-btn').style.display = '';
    document.getElementById('np-error').style.display = 'none';
  });
  document.getElementById('add-provider-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const body = {
      name: document.getElementById('np-name').value.trim(),
      issuer: document.getElementById('np-issuer').value.trim(),
      audience: document.getElementById('np-audience').value.trim() || 'winmcp',
    };
    const errEl = document.getElementById('np-error');
    errEl.style.display = 'none';
    document.getElementById('np-save').disabled = true;
    document.getElementById('np-save').textContent = 'Discovering…';
    const r = await api('POST', '/api/settings/oidc-providers', body);
    document.getElementById('np-save').disabled = false;
    document.getElementById('np-save').textContent = 'Discover + save';
    if (!r.ok) {
      errEl.textContent = r.json?.message ?? `HTTP ${r.status}`;
      errEl.style.display = '';
      return;
    }
    document.getElementById('add-provider-form').reset();
    document.getElementById('np-audience').value = 'winmcp';
    document.getElementById('add-provider-form').style.display = 'none';
    document.getElementById('add-provider-btn').style.display = '';
    const snap = await api('GET', '/api/settings');
    state = snap.json; renderProviders(); renderAdminForm(); renderMcpForm();
    pollRestartPending();
  });

  // ---- Auth tabs ----
  function renderAdminForm() { renderAuthForm('admin', 'admin-form-container'); }
  function renderMcpForm()   { renderAuthForm('mcp',   'mcp-form-container'); }

  function renderAuthForm(scope, containerId) {
    const cur = scope === 'admin' ? state.admin : state.mcp;
    const providerOptions = (state.oidcProviders || []).map(p =>
      `<option value="${esc(p.name)}" ${cur.providerRef === p.name ? 'selected' : ''}>${esc(p.name)}</option>`).join('');
    const lockoutWarn = scope === 'admin'
      ? `<div class="lockout-warn" id="${scope}-lockout-warn" style="display:${cur.mode === 'oidc' ? '' : 'none'}">
           <strong>Lockout risk</strong>: real OIDC validation lands in v1.1. Switching admin auth to OIDC will
           return 503 on every dashboard request — locking you out. Select this only on a staging install
           where you can recover by editing config.json on the host.
         </div>`
      : '';
    const noneWarn = scope === 'admin'
      ? `<div class="none-warn" id="${scope}-none-warn" style="display:${cur.mode === 'none' ? '' : 'none'}">
           Dashboard is unauthenticated — anyone on the network can edit these settings.
         </div>`
      : '';
    const demoDisabled = scope === 'admin' ? 'disabled' : '';
    const demoHelper = scope === 'admin'
      ? '<div class="field"><span></span><span class="helper">Demo mode is intended for the MCP auth domain only — admin endpoints don\'t issue tokens.</span></div>'
      : '';
    document.getElementById(containerId).innerHTML = `
      ${lockoutWarn}
      ${noneWarn}
      <form id="${scope}-form">
        <div class="field">
          <label>Mode</label>
          <span>
            <label><input type="radio" name="${scope}-mode" value="none" ${cur.mode === 'none' ? 'checked' : ''}> none</label>
            &nbsp;
            <label><input type="radio" name="${scope}-mode" value="demo" ${cur.mode === 'demo' ? 'checked' : ''} ${demoDisabled}> demo</label>
            &nbsp;
            <label><input type="radio" name="${scope}-mode" value="oidc" ${cur.mode === 'oidc' ? 'checked' : ''}> oidc</label>
          </span>
        </div>
        ${demoHelper}
        <div class="field" id="${scope}-provider-row" style="${cur.mode === 'oidc' ? '' : 'display:none'}">
          <label>Identity provider</label>
          <select name="providerRef" required>
            <option value="">— select —</option>
            ${providerOptions}
          </select>
        </div>
        ${scope === 'mcp' ? `
        <div class="field" id="mcp-scopes-row" style="${cur.mode === 'oidc' ? '' : 'display:none'}">
          <label>Required scopes</label>
          <input type="text" name="requiredScopes" value="${esc((cur.requiredScopes || []).join(' '))}" placeholder="openid email">
        </div>
        ` : ''}
        <div class="field"><span></span><span class="field-error" id="${scope}-error" style="display:none"></span></div>
        <div class="field">
          <span></span>
          <button class="btn primary" type="submit" id="${scope}-save">Save</button>
        </div>
      </form>
    `;
    document.getElementById(`${scope}-form`).addEventListener('change', () => {
      const mode = document.querySelector(`input[name="${scope}-mode"]:checked`)?.value;
      const providerRow = document.getElementById(`${scope}-provider-row`);
      if (providerRow) providerRow.style.display = mode === 'oidc' ? '' : 'none';
      const scopesRow = document.getElementById('mcp-scopes-row');
      if (scopesRow && scope === 'mcp') scopesRow.style.display = mode === 'oidc' ? '' : 'none';
      const warn = document.getElementById(`${scope}-lockout-warn`);
      if (warn) warn.style.display = (scope === 'admin' && mode === 'oidc') ? '' : 'none';
      const noneWarnEl = document.getElementById(`${scope}-none-warn`);
      if (noneWarnEl) noneWarnEl.style.display = mode === 'none' ? '' : 'none';
    });
    document.getElementById(`${scope}-form`).addEventListener('submit', async (e) => {
      e.preventDefault();
      const mode = document.querySelector(`input[name="${scope}-mode"]:checked`)?.value;
      const body = {
        mode,
        providerRef: mode === 'oidc' ? document.querySelector(`#${scope}-provider-row select`)?.value : null,
        requiredScopes: [],
        requiredClaims: {},
      };
      if (scope === 'mcp' && mode === 'oidc') {
        const raw = document.querySelector('#mcp-scopes-row input')?.value || '';
        body.requiredScopes = raw.split(/\s+/).filter(s => s.length > 0);
      }
      if (scope === 'admin' && mode === 'oidc') {
        const ok = confirm(
          'Switching admin auth to OIDC will return 503 on every dashboard request ' +
          'until WinMCP v1.1 ships the OIDC validator. You will be locked out and will ' +
          'need to edit C:\\\\Program Files\\\\WinMCP\\\\config.json on the host to recover. Continue?');
        if (!ok) return;
      }
      const errEl = document.getElementById(`${scope}-error`);
      errEl.style.display = 'none';
      document.getElementById(`${scope}-save`).disabled = true;
      const r = await api('PUT', `/api/settings/${scope === 'admin' ? 'admin-auth' : 'mcp-auth'}`, body);
      document.getElementById(`${scope}-save`).disabled = false;
      if (!r.ok) {
        errEl.textContent = r.json?.message ?? `HTTP ${r.status}`;
        errEl.style.display = '';
        return;
      }
      state = r.json;
      renderProviders(); renderAdminForm(); renderMcpForm();
      pollRestartPending();
    });
  }

  renderProviders();
  renderAdminForm();
  renderMcpForm();
</script>
</body>
</html>
""";
    }
}

internal sealed record SettingsPageModel(WinMcp.Platform.Hosting.SettingsApi.SettingsSnapshotDto Snapshot);
