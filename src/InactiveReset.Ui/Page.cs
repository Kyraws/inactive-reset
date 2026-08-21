namespace InactiveReset.Ui;

/// <summary>
/// The whole UI, as one self-contained page. No CDN, no build step, no
/// framework - it is a control panel with about a dozen controls, and anything
/// heavier would be a liability to keep working across LMU patches.
///
/// DELIBERATELY PURE ASCII. Non-ASCII characters in this literal have been
/// corrupted before by a text pipeline that decoded UTF-8 as ANSI, turning an
/// em dash into three characters on screen. HTML entities survive that; raw
/// characters do not.
/// </summary>
internal static class Page
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Inactive Reset</title>
<style>
  :root {
    --bg:#f4f6f8; --card:#ffffff; --ink:#151a1f; --muted:#5d6b7a;
    --line:#e2e7ec; --accent:#1f7a4d; --accent-ink:#ffffff;
    --good:#1f7a4d; --bad:#b3261e; --warn:#8a5a00;
    --chip:#eef2f6; --shadow:0 1px 2px rgba(16,24,32,.06), 0 6px 18px rgba(16,24,32,.06);
  }
  @media (prefers-color-scheme: dark) {
    :root {
      --bg:#101316; --card:#191d22; --ink:#e8ecf0; --muted:#93a1b0;
      --line:#262c33; --accent:#48b07a; --accent-ink:#08160f;
      --good:#5cc98f; --bad:#e58a84; --warn:#dcae63;
      --chip:#212730; --shadow:0 1px 2px rgba(0,0,0,.4), 0 8px 22px rgba(0,0,0,.32);
    }
  }
  * { box-sizing:border-box; }
  html, body { height:100%; }
  body {
    margin:0; background:var(--bg); color:var(--ink);
    font:15px/1.55 ui-sans-serif, system-ui, "Segoe UI", Roboto, sans-serif;
    -webkit-font-smoothing:antialiased;
  }

  /* ---- banner ---- */
  .banner {
    position:sticky; top:0; z-index:10;
    background:var(--card); border-bottom:1px solid var(--line);
    padding:12px 20px; display:flex; align-items:center; gap:14px; flex-wrap:wrap;
  }
  .brand { font-size:16px; font-weight:680; letter-spacing:-.01em; margin-right:4px; }
  .status { display:flex; gap:8px; flex-wrap:wrap; align-items:center; }
  .badge {
    display:inline-flex; align-items:center; gap:6px;
    font-size:12.5px; font-weight:560; padding:4px 10px; border-radius:999px;
    background:var(--chip); color:var(--muted); white-space:nowrap;
  }
  .badge.ok { color:var(--good); }
  .badge.no { color:var(--bad); }
  .dot { width:7px; height:7px; border-radius:50%; background:currentColor; }
  .spacer { flex:1; }
  .meta { font-size:12.5px; color:var(--muted); font-variant-numeric:tabular-nums; }

  /* ---- layout ---- */
  main {
    max-width:1120px; margin:0 auto; padding:20px;
    display:grid; gap:16px; grid-template-columns:repeat(auto-fit, minmax(340px,1fr));
  }
  .card {
    background:var(--card); border:1px solid var(--line); border-radius:12px;
    padding:16px 18px; box-shadow:var(--shadow);
  }
  .card h2 {
    font-size:11.5px; text-transform:uppercase; letter-spacing:.08em;
    color:var(--muted); margin:0 0 14px; font-weight:700;
  }
  .wide { grid-column:1 / -1; }

  /* ---- key/value ---- */
  .kv { display:grid; grid-template-columns:auto 1fr; gap:7px 16px; align-items:baseline; }
  .kv dt { color:var(--muted); font-size:13.5px; }
  .kv dd { margin:0; text-align:right; font-variant-numeric:tabular-nums; }
  .big { font-size:17px; font-weight:620; letter-spacing:-.01em; }
  .mono { font-family:ui-monospace,"Cascadia Mono",Consolas,monospace; font-size:12.5px; }

  /* ---- toggles ---- */
  .toggles { display:grid; gap:10px; }
  .toggle {
    display:flex; align-items:center; gap:14px; width:100%; text-align:left;
    font:inherit; cursor:pointer; padding:13px 15px; border-radius:10px;
    border:1px solid var(--line); background:var(--chip); color:var(--ink);
    transition:border-color .12s ease, transform .06s ease;
  }
  .toggle:hover:not(:disabled) { border-color:var(--accent); }
  .toggle:active:not(:disabled) { transform:translateY(1px); }
  .toggle:disabled { opacity:.5; cursor:not-allowed; }
  .toggle .name { font-weight:620; }
  .toggle .detail { font-size:12.5px; color:var(--muted); }
  .switch {
    margin-left:auto; flex:none; width:52px; height:29px; border-radius:999px;
    background:var(--bad); position:relative; transition:background .16s ease;
  }
  .switch.off { background:var(--good); }
  .switch::after {
    content:""; position:absolute; top:3px; left:3px; width:23px; height:23px;
    border-radius:50%; background:#fff; transition:transform .16s ease;
    box-shadow:0 1px 3px rgba(0,0,0,.35);
  }
  .switch.off::after { transform:translateX(23px); }
  .state { font-size:12px; font-weight:650; width:30px; text-align:right; }

  /* ---- forms ---- */
  .field { display:flex; gap:9px; }
  input[type=text], select {
    font:inherit; padding:9px 11px; border-radius:9px; min-width:0; flex:1;
    border:1px solid var(--line); background:var(--card); color:var(--ink);
  }
  input[type=text]:focus, select:focus { outline:2px solid var(--accent); outline-offset:-1px; }
  button.act {
    font:inherit; font-weight:600; padding:9px 16px; border-radius:9px; cursor:pointer;
    border:1px solid var(--line); background:var(--chip); color:var(--ink); white-space:nowrap;
  }
  button.act:hover:not(:disabled) { border-color:var(--accent); }
  button.act.primary { background:var(--accent); border-color:var(--accent); color:var(--accent-ink); }
  button.act:disabled { opacity:.5; cursor:not-allowed; }
  .actions { display:flex; gap:8px; margin-top:10px; flex-wrap:wrap; }
  span.ok { color:var(--good); }
  span.warn { color:var(--warn); }
  .divider { height:1px; background:var(--line); margin:15px 0; }
  .label { font-size:12px; font-weight:650; color:var(--muted); margin-bottom:7px;
           text-transform:uppercase; letter-spacing:.05em; }
  /* Raw derived flags, so the toggle can be checked against reality. */
  .flags { margin-top:12px; display:grid; gap:5px; }
  .flagrow {
    display:flex; align-items:center; gap:10px; font-size:12.5px;
    font-family:ui-monospace,Consolas,monospace;
    padding:6px 10px; border-radius:7px; background:var(--chip);
  }
  .flagrow .fname { color:var(--muted); flex:1; }
  .flagrow .frva { color:var(--muted); opacity:.75; }
  .flagrow .fval {
    font-weight:700; width:16px; text-align:center; border-radius:4px;
  }
  .flagrow .fval.set   { color:var(--bad); }
  .flagrow .fval.clear { color:var(--good); }
  .flagrow .fnote { color:var(--warn); font-size:11.5px; }
  .hint { font-size:12.5px; color:var(--muted); margin-top:9px; }
  .hint.warn { color:var(--warn); }
  .hint.bad { color:var(--bad); }

  /* ---- activity ---- */
  .phasebar { display:flex; align-items:center; gap:12px; margin-bottom:12px; flex-wrap:wrap; }
  .phase {
    font-size:13px; font-weight:700; letter-spacing:.05em; text-transform:uppercase;
    padding:5px 12px; border-radius:8px; background:var(--chip); color:var(--muted);
  }
  .phase.live { background:var(--accent); color:var(--accent-ink); }
  .phase.bad  { background:var(--bad); color:#fff; }
  /* The moment the driver has to act on. With no audio cue this is the only
     signal, so it is deliberately loud. */
  .phase.armed {
    background:var(--warn); color:#fff; font-size:15px; padding:7px 16px;
    animation:pulse 1s ease-in-out infinite;
  }
  @keyframes pulse { 0%,100% { opacity:1; } 50% { opacity:.55; } }
  .step.now.act { background:var(--warn); color:#fff; }
  .phasemsg { color:var(--muted); font-size:13.5px; }
  .steps { display:flex; gap:6px; flex-wrap:wrap; margin-bottom:12px; }
  .step {
    font-size:11.5px; padding:3px 9px; border-radius:999px;
    background:var(--chip); color:var(--muted);
  }
  .step.done { color:var(--good); }
  .step.now  { background:var(--accent); color:var(--accent-ink); font-weight:650; }
  #log {
    font-family:ui-monospace,Consolas,monospace; font-size:12.5px;
    background:var(--chip); border-radius:9px; padding:12px;
    height:180px; overflow:auto; white-space:pre-wrap; line-height:1.5;
  }
  .results { margin-top:13px; display:grid; gap:7px 16px;
             grid-template-columns:repeat(auto-fit,minmax(160px,1fr)); }
  .result { background:var(--chip); border-radius:9px; padding:10px 12px; }
  .result .rk { font-size:11.5px; color:var(--muted); text-transform:uppercase;
                letter-spacing:.05em; }
  .result .rv { font-size:16px; font-weight:640; font-variant-numeric:tabular-nums; }
  .result .rv.good { color:var(--good); }
  .result .rv.bad  { color:var(--bad); }
  .empty { color:var(--muted); font-size:13.5px; }
</style>
</head>
<body>

<div class="banner">
  <span class="brand">Inactive Reset</span>
  <div class="status" id="status"></div>
  <span class="spacer"></span>
  <span class="meta" id="meta"></span>
</div>

<main>
  <section class="card">
    <h2>Game</h2>
    <div id="game"><div class="empty">looking for the install</div></div>
    <div class="actions">
      <button class="act primary" id="launchDirect" onclick="launch('direct')">Launch direct</button>
      <button class="act" id="launchEac" onclick="launch('eac')">Launch with EAC</button>
    </div>
    <div class="hint" id="launchHint">
      A direct launch has no anticheat in the process tree, and is the only kind
      of session this tool can attach to. Launch with EAC for online racing -
      this tool refuses to touch that session on purpose. Steam must already be
      running either way.
    </div>
  </section>

  <section class="card">
    <h2>Session</h2>
    <div id="session"><div class="empty">waiting for the game</div></div>
  </section>

  <section class="card">
    <h2>Penalties</h2>
    <div class="toggles" id="penalties"></div>
    <div class="flags" id="flags"></div>
    <div class="hint">
      Track limits are latched when the session starts, so this writes the
      engine's derived flags rather than the setting. The engine can re-arm
      them when you return to the garage, which is why the raw values are
      shown: they are read back from memory every refresh, so what you see is
      what the engine has, not what was last asked for.
    </div>
  </section>

  <section class="card">
    <h2>Checkpoints</h2>
    <div class="label">Capture the current position</div>
    <div class="field">
      <input type="text" id="capName" placeholder="name, for example turn-1-entry"
             autocomplete="off" spellcheck="false">
      <button class="act" id="capBtn" onclick="capture()">Capture</button>
    </div>
    <div class="hint" id="capHint"></div>

    <div class="divider"></div>

    <div class="label">Place the car at a checkpoint</div>
    <div class="field">
      <select id="cp"></select>
      <button class="act primary" id="placeBtn" onclick="place()">Place</button>
    </div>
    <div class="hint">
      Park in the garage, press Place, then press Drive when Activity below
      shows <strong>PRESS DRIVE</strong>.
    </div>
    <div class="hint warn" id="advisory"></div>
  </section>

  <section class="card wide">
    <h2>Activity</h2>
    <div class="phasebar">
      <span class="phase" id="phase">idle</span>
      <span class="phasemsg" id="phaseMsg">nothing running</span>
    </div>
    <div class="steps" id="steps"></div>
    <div id="log"></div>
    <div class="results" id="results"></div>
  </section>
</main>

<script>
'use strict';

// The trail the driver reads. Internal phases such as Settling and Restoring
// still appear in the log, but they are not moments anyone acts on, so putting
// them here would only dilute the one step that matters.
const STEPS = [
  {key:'Gating',         label:'not armed'},
  {key:'Armed',          label:'armed'},
  {key:'WaitingToClear', label:'keep pit limiter', conditional:true},
  {key:'Clear',          label:'drive'}
];

// Where each reported phase sits on that trail.
const PHASE_STEP = {
  Idle:-1, Gating:0, Armed:1, Settling:1, Restoring:1,
  WaitingToClear:2, Clear:3, Done:3, Failed:-1
};

function esc(s) {
  return String(s === null || s === undefined ? '' : s)
    .replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}

async function api(path, body) {
  const res = await fetch(path, {
    method: body ? 'POST' : 'GET',
    headers: body ? {'Content-Type':'application/json'} : {},
    body: body ? JSON.stringify(body) : undefined
  });
  return res.json();
}

function badge(label, ok) {
  return '<span class="badge ' + (ok ? 'ok' : 'no') + '">' +
         '<span class="dot"></span>' + esc(label) + '</span>';
}

function kv(rows) {
  return '<dl class="kv">' + rows.map(r =>
    '<dt>' + esc(r[0]) + '</dt><dd class="' + (r[2] || '') + '">' + r[1] + '</dd>'
  ).join('') + '</dl>';
}

function renderStatus(s) {
  const status = document.getElementById('status');
  const meta = document.getElementById('meta');

  if (!s.connected) {
    var mp = s.missingProfile;
    if (mp) {
      // A new game build. Not a fault, and not the same as "game not running":
      // there is exactly one action that helps, so offer it here.
      status.innerHTML = badge('new game build ' + mp.build, false);
      meta.innerHTML =
        'Le Mans Ultimate updated. This tool needs an offset profile for build '
        + mp.build + ' before it can read or write anything safely.'
        + '<br><br>'
        + (mp.consentGiven
            ? 'No profile has been published for this build yet.'
            : '<button id="allowFetch">Allow and fetch it</button>'
              + ' <span class="fnote">downloads one file over HTTPS from GitHub.'
              + ' This is the only time this tool uses the internet.</span>')
        + '<br><span class="fnote">' + mp.url + '</span>';
      var btn = document.getElementById('allowFetch');
      if (btn) btn.onclick = allowFetch;
      return;
    }
    status.innerHTML = badge('game not running', false);
    meta.textContent = '';
    return;
  }
  const gates = s.gates || [];
  const named = {process:'process', anticheat:'anti-cheat absent', build:'build', probe:'probe'};
  status.innerHTML = gates
    .filter(g => g.name === 'process' || g.name === 'anticheat')
    .map(g => badge(named[g.name] || g.name, g.passed)).join('');
  meta.textContent = 'pid ' + s.pid + '  |  build ' + s.build + '  |  v' + s.gameVersion;
}

async function allowFetch() {
  const btn = document.getElementById('allowFetch');
  if (btn) { btn.disabled = true; btn.textContent = 'fetching...'; }
  const res = await api('/api/allow-fetch', {});
  if (!res.ok) alert(res.message || 'fetch failed');
  refresh();
}

// The game card answers two questions that must not be allowed to look alike:
// is the game up, and if it is, can this tool touch it? A protected session is
// running perfectly well and is still unusable here.
function renderGame(s) {
  const el = document.getElementById('game');
  const g = s.game || {};
  const rows = [];

  if (g.running) {
    rows.push(['running', 'pid ' + g.pid + (g.protected
      ? ' <span class="warn">started with EasyAntiCheat - this tool will not attach</span>'
      : ' <span class="ok">started direct - attachable</span>')]);
  } else {
    rows.push(['running', 'no']);
  }

  if (g.install) {
    rows.push(['install', esc(g.install)]);
    rows.push(['found by', esc(g.foundBy)]);
  } else if (g.installError) {
    rows.push(['install', '<span class="warn">' + esc(g.installError) + '</span>']);
  }

  el.innerHTML = kv(rows);
}

async function launch(mode) {
  const direct = document.getElementById('launchDirect');
  const eac = document.getElementById('launchEac');
  direct.disabled = true;
  eac.disabled = true;
  try {
    const res = await api('/api/launch', {mode: mode});
    // The server reports a refused gate as an error with its reason. Showing it
    // verbatim matters: every one of them says what to do about it.
    if (res.error) alert(res.error);
  } catch (e) {
    alert('could not reach the server');
  }
  refresh();
}

function renderSession(s) {
  const el = document.getElementById('session');
  const d = s.session;
  if (!d) {
    el.innerHTML = '<div class="empty">' +
      (s.connected ? 'no car in shared memory (in menus?)' : 'waiting for the game') +
      '</div>';
    return;
  }
  el.innerHTML = kv([
    ['Track', '<span class="big">' + esc(d.track) + '</span>'],
    ['Car', esc(d.vehicle)],
    ['Lap distance', d.lapDistance.toFixed(1) + ' m'],
    ['Gear', d.gear === 0 ? 'neutral' : (d.gear < 0 ? 'reverse' : String(d.gear))],
    ['Position', '<span class="mono">' + d.x.toFixed(2) + ', ' + d.y.toFixed(2) +
                 ', ' + d.z.toFixed(2) + '</span>'],
    ['Heading', d.yaw.toFixed(4) + ' rad']
  ]);
  document.getElementById('capHint').textContent = d.capturable
    ? 'Ready to capture this spot.'
    : 'This pose cannot be captured: the heading is ill-conditioned.';
}

function renderPenalties(s) {
  const el = document.getElementById('penalties');
  const p = s.penalties;
  if (!p) { el.innerHTML = '<div class="empty">unavailable</div>'; return; }

  el.innerHTML = [['trackLimits', p.trackLimits]]
    .map(([key, v]) =>
      '<button class="toggle" ' + (v.known ? '' : 'disabled ') +
      'onclick="toggle(\'' + key + '\',' + (v.on ? 'false' : 'true') + ')">' +
        '<span><span class="name">' + esc(v.label) + '</span><br>' +
        '<span class="detail">' + esc(v.detail) + '</span></span>' +
        '<span class="switch ' + (v.on ? '' : 'off') + '"></span>' +
        '<span class="state">' + (v.on ? 'ON' : 'OFF') + '</span>' +
      '</button>'
    ).join('') + pitSpeedingStatus(s);

  renderFlags(s);
}

// The pit-speeding penalty is status, not a control. Every placement disables
// it, so a toggle would be a switch that flips itself back the moment you use
// the tool. An unusable control is worse than none.
//
// This is the game's penalty, NOT the car's pit limiter, which this tool never
// touches. The control was labelled "Pit Limiter" for a long time and the two
// were confused constantly. See CONTEXT.md.
function pitSpeedingStatus(s) {
  const v = s.pitSpeedingPenalty;
  if (!v) return '';
  const state = !v.known ? 'unknown' : (v.on ? 'ON' : 'off');
  const cls = !v.known ? '' : (v.on ? 'bad' : 'good');
  return '<div class="result"><div class="rk">' + esc(v.label) + '</div>' +
         '<div class="rv ' + cls + '">' + state + '</div>' +
         '<div class="detail">' + esc(v.detail) + '</div></div>';
}

/// Raw values read back from memory, so the toggle above can be checked
/// against what the engine actually holds rather than what was last written.
function renderFlags(s) {
  const el = document.getElementById('flags');
  const rules = (s.rules || []).filter(r =>
    r.name.indexOf('Track limits flag') === 0 || r.name.indexOf('Pit-speeding') === 0);

  let html = rules.map(r => {
    // An unresolved address is neither set nor clear: it was never read.
    const set = r.resolved !== false && r.value !== 0;
    const unknown = r.resolved === false;
    const invalidation = r.name.indexOf('lap invalidation') > 0;
    const name = r.name
      .replace('Pit-speeding gate (Flag Rules)', 'pit speeding gate')
      .replace('Track limits flag ', 'cut flag ');
    return '<div class="flagrow">' +
      '<span class="fname">' + esc(name) + '</span>' +
      (invalidation ? '<span class="fnote">gates lap invalidation</span>' : '') +
      '<span class="frva">' + esc(r.rva) + '</span>' +
      '<span class="fval ' + (unknown ? 'warn' : set ? 'set' : 'clear') + '">' +
        esc(r.display !== undefined ? r.display : String(r.value)) + '</span>' +
      '</div>';
  }).join('');

  html += renderLapValidity(s);
  el.innerHTML = html;
}

// Lap validity, read back from memory every refresh like the cut flags above.
// countLapFlag is the lap you are ON; lapCountsNext is the latch consumed at the
// next start/finish crossing. pitFlag is highlighted because when it is set it
// demotes that crossing to an out-lap, which is what costs a placed car a lap.
function renderLapValidity(s) {
  const v = s.lapValidity;
  if (!v) return '';

  const row = function (name, value, note, bad) {
    return '<div class="flagrow">' +
      '<span class="fname">' + esc(name) + '</span>' +
      (note ? '<span class="fnote">' + esc(note) + '</span>' : '') +
      '<span class="frva"></span>' +
      '<span class="fval ' + (bad ? 'set' : 'clear') + '">' + esc(String(value)) + '</span>' +
      '</div>';
  };

  return row('count lap flag', v.countLapFlag,
             v.currentLapTimed ? 'this lap is timed' : 'this lap is NOT timed',
             !v.currentLapTimed)
       + row('next lap latch', v.lapCountsNext ? 1 : 0, '', !v.lapCountsNext)
       + row('pit flag', v.pitFlag ? 1 : 0,
             v.pitFlag ? 'demotes the next crossing to an out-lap' : '',
             v.pitFlag);
}

function renderCheckpoints(s) {
  const select = document.getElementById('cp');
  const chosen = select.value;
  const list = s.checkpoints || [];
  select.innerHTML = list.length
    ? list.map(c => '<option value="' + esc(c.name) + '">' + esc(c.name) +
        ' &mdash; ' + esc(c.track) + ' (' + c.lapDistance.toFixed(0) + ' m)</option>').join('')
    : '<option value="">no checkpoints yet</option>';
  if (chosen) select.value = chosen;

  const advisory = (s.calibrations || []).find(c => c.advisory);
  document.getElementById('advisory').textContent = advisory
    ? 'Calibration note: placement is currently about 0.57 m off because of a known heading error.'
    : '';
}

function renderActivity(s) {
  const phase = s.phase || 'Idle';
  const el = document.getElementById('phase');
  const armed = phase === 'Armed';
  el.textContent = armed
    ? 'PRESS DRIVE'
    : (phase === 'Idle' ? 'idle' : phase.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase());
  el.className = 'phase' +
    (armed ? ' armed' : (phase === 'Failed' ? ' bad' : (s.busy ? ' live' : '')));
  document.getElementById('phaseMsg').textContent = s.phaseMessage || 'nothing running';

  // The pit-state wait only exists when the pit-speeding penalty is on. Every
  // placement now disables it, so this step is normally unreachable, and showing
  // a step nobody will ever reach makes the trail look stuck.
  const pitOn = s.pitSpeedingPenalty && s.pitSpeedingPenalty.on;
  const at = PHASE_STEP[phase] === undefined ? -1 : PHASE_STEP[phase];

  document.getElementById('steps').innerHTML = STEPS
    .filter(step => !step.conditional || pitOn)
    .map(step => {
      const i = STEPS.indexOf(step);
      let cls = at < 0 ? '' : (i < at ? ' done' : (i === at ? ' now' : ''));
      if (i === at && armed) cls += ' act';
      return '<span class="step' + cls + '">' + step.label + '</span>';
    }).join('');

  const log = document.getElementById('log');
  const atBottom = log.scrollTop + log.clientHeight >= log.scrollHeight - 24;
  log.textContent = (s.log || []).join('\n') || 'nothing yet';
  if (atBottom) log.scrollTop = log.scrollHeight;

  // The placement report, rendered exactly as it arrives.
  //
  // This block used to build its own list of cells, and that list quietly
  // disagreed with the one the CLI built: it showed the pit flag, which the
  // measurements call a secondary guard, and never showed the sector write,
  // which the same measurements call the fix. A driver placing from this page
  // could not see whether the next lap would count.
  //
  // So it decides nothing now. Add a line in PlacementReport and it appears
  // here with no change to this file, forever.
  const o = s.outcome;
  const results = document.getElementById('results');
  if (!o) { results.innerHTML = ''; return; }

  results.innerHTML = (o.lines || []).map(line =>
    '<div class="result"><div class="rk">' + esc(line.label) + '</div>' +
    '<div class="rv ' + severityClass(line.severity) + '">' + esc(line.value) + '</div>' +
    (line.sentence ? '<div class="detail">' + esc(line.sentence) + '</div>' : '') +
    '</div>'
  ).join('');
}

// Three levels, because "this worked and you should be reassured" is a
// different thing from "this is merely a number".
function severityClass(severity) {
  if (severity === 'good') return 'good';
  if (severity === 'warning') return 'bad';
  return '';
}

async function refresh() {
  let s;
  try { s = await api('/api/state'); }
  catch (e) {
    document.getElementById('status').innerHTML = badge('server unreachable', false);
    return;
  }
  renderStatus(s);
  renderGame(s);
  renderSession(s);
  renderPenalties(s);
  renderCheckpoints(s);
  renderActivity(s);

  document.getElementById('placeBtn').disabled = !!s.busy || !s.connected;
  document.getElementById('capBtn').disabled = !s.session;

  // Both launches are refused while the game is up, so say so with the control
  // rather than only in the error after it is pressed.
  const g = s.game || {};
  document.getElementById('launchDirect').disabled = !g.canLaunchDirect || !!g.running;
  document.getElementById('launchEac').disabled = !g.canLaunchProtected || !!g.running;

  schedule(s.busy ? 250 : 700);
}

async function toggle(which, enable) {
  await api('/api/rules', {
    enable: enable,
    pitSpeeding: false,
    trackLimits: which === 'trackLimits'
  });
  refresh();
}

async function capture() {
  const input = document.getElementById('capName');
  const name = input.value.trim();
  if (!name) { input.focus(); return; }
  const res = await api('/api/capture', {name: name});
  if (res.error) {
    alert(res.error + (res.failures ? '\n\n' + res.failures.join('\n') : ''));
  } else {
    input.value = '';
  }
  refresh();
}

async function place() {
  const checkpoint = document.getElementById('cp').value;
  if (!checkpoint) return;
  const res = await api('/api/place', {checkpoint: checkpoint});
  if (res.error) alert(res.error);
  refresh();
}

document.getElementById('capName').addEventListener('keydown', e => {
  if (e.key === 'Enter') capture();
});

// Poll faster while a placement is running. The screen is the only cue now, so
// latency on "press drive" is latency the driver actually feels; when nothing
// is happening there is no reason to work that hard.
let timer = null;
let period = 0;

function schedule(ms) {
  if (ms === period) return;
  period = ms;
  if (timer) clearInterval(timer);
  timer = setInterval(refresh, ms);
}

refresh();
schedule(700);
</script>
</body>
</html>
""";
}
