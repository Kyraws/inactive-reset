namespace InactiveReset.Ui;

/// <summary>Self-contained local control panel, shared by the desktop host and CLI.</summary>
internal static class Page
{
    public const string Html = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Inactive Reset</title><style>

:root{color-scheme:dark;--bg:#17191d;--card:#202329;--field:#191c21;--ink:#eef0f3;--muted:#b3bbc6;--line:#414750;--accent:#dda177;--accent-ink:#2b1b12;--good:#99d8b4;--bad:#ff9da3;--warn:#f2c87a}
*{box-sizing:border-box}html,body{height:100%;margin:0;overflow:hidden}body{display:flex;flex-direction:column;background:var(--bg);color:var(--ink);font:12px/1.35 'Segoe UI',system-ui,sans-serif}
button,input,select{font:inherit}button,select,input[type=checkbox]{cursor:pointer}button:disabled,input:disabled,select:disabled{opacity:.45;cursor:not-allowed}[hidden]{display:none!important}:focus-visible{outline:2px solid var(--accent);outline-offset:2px}
.banner{display:flex;align-items:center;gap:8px;padding:9px 12px;border-bottom:1px solid var(--line);flex-shrink:0}.brand{font-weight:650;font-size:13px}.spacer{flex:1}.actions{display:flex;gap:6px}
main{padding:10px 12px 12px;display:grid;grid-template-columns:210px minmax(0,1fr);grid-template-rows:28px minmax(0,1fr);gap:9px;flex:1;min-height:0;min-width:0}
.session-card{grid-column:1/-1;display:flex;align-items:center;gap:8px;min-width:0}.session{display:flex;gap:10px;align-items:center;flex:1;min-width:0}.session strong,.session-car{white-space:nowrap;overflow:hidden;text-overflow:ellipsis;min-width:0}.session strong{max-width:48%;font-size:12px}.session-car{flex:1;font-size:11px;color:var(--muted)}.session-state,#status{white-space:nowrap;color:var(--muted);font-size:10px;flex-shrink:0}.session-state{border:1px solid var(--line);border-radius:3px;padding:3px 6px}
.card{padding:9px;background:var(--card);border:1px solid var(--line);border-radius:4px;min-width:0;min-height:0}h2,p{margin:0}h2{font-size:12px;font-weight:650;margin-bottom:8px}
.checkpoints-card{display:flex;flex-direction:column;grid-column:1;grid-row:2;overflow:hidden}
input,select{background:var(--field);border:1px solid var(--line);color:var(--ink);border-radius:3px;padding:5px 7px;min-width:0}input[type=text]{width:0;flex:1}input[type=number]{width:64px}input[type=checkbox]{accent-color:var(--accent);margin:0;flex-shrink:0}
#cp{flex:1;min-height:0;width:100%;padding:3px;overflow:auto;scrollbar-width:thin;scrollbar-color:var(--line) var(--field);overscroll-behavior:contain}#cp option{padding:5px 6px;white-space:nowrap;text-overflow:ellipsis;overflow:hidden;line-height:18px;border-radius:2px}#cp option:checked{background:var(--accent);color:var(--accent-ink)}
.capture-area{border-top:1px solid var(--line);margin-top:8px;padding-top:8px;flex-shrink:0}.field{display:flex;gap:5px;align-items:center;min-width:0}.capture-area .act{flex-shrink:0}.hint{margin-top:5px;font-size:10px;color:var(--muted);overflow-wrap:anywhere}.hint:empty{display:none}
.work-area{grid-column:2;grid-row:2;display:flex;flex-direction:column;min-width:0;min-height:0}
.placement-strip{display:flex;align-items:center;gap:8px;margin-bottom:8px;flex-shrink:0}#destinationName{font-weight:600;flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.act{padding:5px 10px;background:var(--field);border:1px solid var(--line);color:var(--ink);font-size:11px;border-radius:3px;white-space:nowrap}.act:hover:not(:disabled){border-color:var(--accent)}.act.primary{background:var(--accent);border-color:var(--accent);color:var(--accent-ink);font-weight:650}
.controls-grid{display:grid;grid-template-columns:minmax(0,1.2fr) minmax(0,1fr);gap:8px;flex-shrink:0}
#tyreSetup{padding:0;margin:0;border:0;min-width:0}#tyreSetup:disabled{opacity:.55}.tyre-settings{width:100%;border-collapse:collapse;font-size:10px;table-layout:fixed}.tyre-settings th{font-weight:500;color:var(--muted);text-align:left;padding:0 3px 4px}.tyre-settings th:first-child{width:30px}.tyre-settings th:nth-child(2){width:38%}.tyre-settings td{padding:3px}.tyre-settings input[type=number]{width:100%;padding:4px 5px}.tyre-settings td:nth-child(3){width:22px;text-align:center}.tyre-settings tr>th[scope=row]{padding-top:5px;color:var(--ink)}
.checkbox-row{display:flex;align-items:center;gap:6px;font-size:11px;margin:9px 0 7px}#tyreSummary{line-height:1.4}#tyreResetHint,#placeHint{display:none}
.toggle,.rule-status{width:100%;display:flex;align-items:center;justify-content:space-between;gap:8px;padding:7px 0;font-size:11px}.toggle{border:0;background:transparent;color:var(--ink);text-align:left}.state,.rule-status span{font-size:10px;color:var(--muted)}.rules-note{font-size:10px;color:var(--muted);margin-top:7px}
.activity-card{display:flex;flex-direction:column;flex:1;margin-top:8px;overflow:hidden}.activity-head{display:flex;align-items:center;justify-content:space-between;gap:8px;flex-shrink:0}.activity-head h2{margin:0}.phasebar{display:flex;align-items:flex-start;gap:8px;margin:7px 0;flex-shrink:0;min-width:0}.phase{font-size:9px;white-space:nowrap;padding:3px 5px;border-radius:3px;border:1px solid var(--line);color:var(--muted)}.phase.armed{background:var(--accent);color:var(--accent-ink);border-color:var(--accent)}.phase.bad{color:var(--bad)}.phase.live{color:var(--accent)}.phasemsg{font-size:11px;min-width:0;overflow-wrap:anywhere}
#log{margin:0;padding-top:7px;border-top:1px solid var(--line);min-height:0;flex:1;overflow:auto;white-space:pre-wrap;overflow-wrap:anywhere;font:10px/1.6 Consolas,monospace;scrollbar-width:thin;scrollbar-color:var(--line) var(--card);overscroll-behavior:contain}
.toast{position:fixed;right:12px;bottom:12px;max-width:min(460px,calc(100vw - 24px));max-height:calc(100vh - 24px);overflow:auto;white-space:pre-wrap;padding:12px 15px;border:1px solid var(--bad);background:#30272a;color:var(--ink);border-radius:4px;z-index:10;box-shadow:0 8px 30px #0008}
@media(max-width:700px){main{grid-template-columns:175px minmax(0,1fr)}.session-car,#status{display:none}#tyreSetup{grid-template-columns:1fr}.controls-grid{grid-template-columns:minmax(0,1fr) minmax(0,1fr)}}

</style></head><body>

<header class="banner"><span class="brand">Inactive Reset</span><span class="spacer"></span><div class="actions"><button class="act" id="launchDirect" onclick="launch('direct')" title="Starts LMU without EAC for local sessions">Launch game</button><button class="act" id="launchEac" onclick="launch('eac')" title="Use EAC for online racing; Inactive Reset will not attach">Launch with EAC</button></div></header>
<main>
<section class="session-card" aria-label="Current session"><div class="session" id="session"><strong>Launch the game to begin</strong></div><span id="status"></span></section>
<section class="card checkpoints-card"><h2 id="destinationsLabel">Destinations</h2><select id="cp" size="12" aria-labelledby="destinationsLabel" onchange="updateDestination()"></select><div class="capture-area"><div class="field"><input id="capName" type="text" placeholder="Checkpoint name" aria-label="Capture name" autocomplete="off" spellcheck="false"><button class="act" id="capBtn" onclick="capture()">Capture</button></div><p class="hint" id="capHint"></p></div></section>
<div class="work-area">
<div class="placement-strip"><span id="destinationName">Choose a destination</span><button class="act primary" id="placeBtn" onclick="place()">Place</button></div><p id="placeHint"></p>
<div class="controls-grid">
<section class="card tyres-card"><h2>Tyres</h2><fieldset id="tyreSetup"><table class="tyre-settings"><thead><tr><th>Tyre</th><th>Condition %</th><th colspan="2">Initial heat °C</th></tr></thead><tbody><tr><th scope="row">FL</th><td><input id="tyreConditionFL" aria-label="Front left condition percent" type="number" min="0" max="100" value="100" oninput="updateTyreSettings()"></td><td><input id="tyreTemperatureEnabledFL" aria-label="Set front left initial heat" type="checkbox" onchange="updateTyreSettings()"></td><td><input id="tyreTemperatureFL" aria-label="Front left initial temperature Celsius" type="number" min="0" max="150" value="70" disabled oninput="updateTyreSettings()"></td></tr><tr><th scope="row">FR</th><td><input id="tyreConditionFR" aria-label="Front right condition percent" type="number" min="0" max="100" value="100" oninput="updateTyreSettings()"></td><td><input id="tyreTemperatureEnabledFR" aria-label="Set front right initial heat" type="checkbox" onchange="updateTyreSettings()"></td><td><input id="tyreTemperatureFR" aria-label="Front right initial temperature Celsius" type="number" min="0" max="150" value="70" disabled oninput="updateTyreSettings()"></td></tr><tr><th scope="row">RL</th><td><input id="tyreConditionRL" aria-label="Rear left condition percent" type="number" min="0" max="100" value="100" oninput="updateTyreSettings()"></td><td><input id="tyreTemperatureEnabledRL" aria-label="Set rear left initial heat" type="checkbox" onchange="updateTyreSettings()"></td><td><input id="tyreTemperatureRL" aria-label="Rear left initial temperature Celsius" type="number" min="0" max="150" value="70" disabled oninput="updateTyreSettings()"></td></tr><tr><th scope="row">RR</th><td><input id="tyreConditionRR" aria-label="Rear right condition percent" type="number" min="0" max="100" value="100" oninput="updateTyreSettings()"></td><td><input id="tyreTemperatureEnabledRR" aria-label="Set rear right initial heat" type="checkbox" onchange="updateTyreSettings()"></td><td><input id="tyreTemperatureRR" aria-label="Rear right initial temperature Celsius" type="number" min="0" max="150" value="70" disabled oninput="updateTyreSettings()"></td></tr></tbody></table></fieldset><label class="checkbox-row"><input id="freshOnPlace" type="checkbox" onchange="updateTyreSettings()">Apply on placement</label><button class="act" id="tyreResetBtn" onclick="resetTyres()" disabled>Prepare tyres</button><p class="hint" id="tyreSummary"></p><p id="tyreResetHint"></p></section>
<section class="card penalties-card"><h2>Rules</h2><div id="penalties"></div><div class="rule-status"><span>Flag Rules</span><span id="flagStatus">Unknown</span></div><p class="rules-note">Flag Rules are managed during pit cleanup. Keep the pit limiter on until CLEAR.</p></section>
</div>
<section class="card activity-card" aria-label="Activity and operation progress"><div class="activity-head"><h2>Activity</h2><button class="act" id="cancelBtn" onclick="cancelPlacement()" disabled>Cancel</button></div><div class="phasebar"><span class="phase" id="phase">IDLE</span><span class="phasemsg" id="phaseMsg" role="status" aria-live="polite" aria-atomic="true">Load a Practice session.</span></div><pre id="log" aria-label="Activity log"></pre></section>
</div>
</main><div id="notice" class="toast" role="alert" hidden></div>

<script>

'use strict';

function esc(s) {
  return String(s === null || s === undefined ? '' : s)
    .replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}

let noticeTimer;
function showNotice(message) {
  const el = document.getElementById('notice');
  el.textContent = message;
  el.hidden = false;
  clearTimeout(noticeTimer);
  noticeTimer = setTimeout(() => { el.hidden = true; }, 10000);
}

async function api(path, body) {
  try {
    const res = await fetch(path, {
    method: body ? 'POST' : 'GET',
    headers: body ? {'Content-Type':'application/json'} : {},
    body: body ? JSON.stringify(body) : undefined
    });
    return await res.json();
  } catch {
    return {error:'The app connection was lost. Check that Inactive Reset is still running.'};
  }
}

function renderStatus(s) {
  document.getElementById('status').textContent = s.connected ? 'Local Practice' : 'Not connected';
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
    if (res.error) showNotice(res.error);
  } catch (e) {
    showNotice('could not reach the server');
  }
  refresh();
}

function renderSession(s) {
  const d = s.session, garage = s.inGarage ?? s.tyreRules?.inGarage;
  document.getElementById('session').innerHTML = d
    ? '<strong title="' + esc(d.track) + '">' + esc(d.track) + '</strong><span class="session-car" title="' + esc(d.vehicle) + '">' + esc(d.vehicle) + '</span><span class="session-state">' + (garage === true ? 'In garage' : garage === false ? 'On track' : 'Session loaded') + '</span>'
    : '<strong>' + (s.connected ? 'Load single-player Practice' : 'Launch the game to begin') + '</strong>';
  document.getElementById('capHint').textContent = d ? (d.capturable ? 'Capture your current position.' : 'This position cannot be captured.') : 'Load a session to capture.';
}

function renderPenalties(s) {
  const p = s.penalties?.trackLimits;
  document.getElementById('penalties').innerHTML = p
    ? '<button class="toggle" aria-pressed="' + !!p.on + '" ' + (p.known && !s.busy ? '' : 'disabled ') +
      'onclick="toggle(\'trackLimits\',' + !p.on + ')"><span>Track limits</span><span class="state">' +
      (p.known ? (p.on ? 'ON' : 'OFF') : 'Unknown') + '</span></button>'
    : '<div class="rule-status">Track limits <span>Unavailable</span></div>';
  const flag = s.pitSpeedingPenalty;
  document.getElementById('flagStatus').textContent = !flag?.known ? 'Unknown' : flag.on ? 'ON' : 'OFF';
}

function updateDestination() {
  const select = document.getElementById('cp');
  document.getElementById('destinationName').textContent = select.options[select.selectedIndex]?.textContent || 'Choose a destination';
}

function renderCheckpoints(s) {
  const select = document.getElementById('cp');
  const chosen = select.value;
  const scroll = select.scrollTop;
  const sameTrack = select.dataset.track === (s.session?.track || '').toLowerCase();
  const session = s.session;
  const list = session ? (s.checkpoints || []).filter(c =>
    c.track.toLowerCase() === session.track.toLowerCase()) : [];
  const options = list.length
    ? list.map(c => '<option value="' + esc(c.id) + '">' + esc(c.name) + '</option>').join('')
    : '<option value="">' + (session ? 'No captures for this track yet' : 'Load a track to see captures') + '</option>';
  // Polling must not rebuild an open selector or interrupt keyboard selection.
  if (select.dataset.options !== options) {
    select.innerHTML = options;
    select.dataset.options = options;
  }
  if (list.some(c => c.id === chosen)) select.value = chosen;
  select.scrollTop = sameTrack ? scroll : 0;
  select.dataset.track = (session?.track || '').toLowerCase();
  select.disabled = !!s.busy || !list.length;
  document.getElementById('destinationName').textContent = list.find(c => c.id === select.value)?.name || 'Choose a destination';
}

const diagnosticValues = new Map();
const diagnosticLog = [];
function collectDiagnostics(s) {
  const calibration = s.session && (s.calibrations || []).find(c =>
    c.track.toLowerCase() === s.session.track.toLowerCase() && c.vehicle.toLowerCase() === s.session.vehicle.toLowerCase());
  const rows = [
    ['Connection', s.connected ? 'Local Practice connected' : s.error || 'Game not connected'],
    ['Build', s.connected ? [s.build, s.gameVersion].filter(Boolean).join(' / ') : null],
    ['Tyre discovery', s.tyreRules ? 'Validated; tyre preparation available' : s.tyreRulesError || 'Unavailable; tyre preparation disabled'],
    ['Calibration', calibration ? calibration.advisory || 'Saved profile; compatibility checked before placement' : s.session ? 'Compatibility checked before placement' : null],
    ['Game launch', s.game?.installError || (s.game?.protected ? 'EAC session; attachment refused' : null)]
  ];
  if (s.tyreRules && s.session) rows.push(['Tyre protection',
    'Effective invulnerability ' + (s.tyreRules.invulnerable ? 'ON' : 'OFF') +
    '; stored ' + s.tyreRules.storedInvulnerability + '; wear multiplier ' + s.tyreRules.storedWearMultiplier +
    '; damage multiplier ' + s.tyreRules.storedDamageMultiplier]);
  const checkpoint = (s.checkpoints || []).find(c => c.id === document.getElementById('cp').value);
  rows.push(['Destination', checkpoint ? checkpoint.name + ' / ' + checkpoint.id + ' / captured in ' + checkpoint.vehicle : null]);
  for (const [label,value] of rows) {
    if (diagnosticValues.get(label) !== value) {
      diagnosticValues.set(label,value);
      if (value) diagnosticLog.push(label + ': ' + value);
    }
  }
  if (diagnosticLog.length > 60) diagnosticLog.splice(0,diagnosticLog.length-60);
}

function renderActivity(s) {
  const phase = s.phase || 'Idle', el = document.getElementById('phase');
  el.textContent = phase === 'Armed' ? 'PRESS DRIVE' : phase.replace(/([a-z])([A-Z])/g, '$1 $2').toUpperCase();
  el.className = 'phase' + (phase === 'Armed' ? ' armed' : phase === 'Failed' ? ' bad' : s.busy ? ' live' : '');
  document.getElementById('phaseMsg').textContent = s.phaseMessage || (!s.connected
    ? 'Launch local Practice to begin.' : !s.session ? 'Load a Practice session.'
    : (s.inGarage ?? s.tyreRules?.inGarage) === true ? 'Choose a destination or prepare your tyres.' : 'Capture here; return to the garage to prepare.');
  collectDiagnostics(s);
  const log = document.getElementById('log');
  const atBottom = log.scrollTop + log.clientHeight >= log.scrollHeight - 24;
  const lines = [...diagnosticLog, ...(s.log || [])];
  if (s.outcome) {
    lines.push('', 'LAST RESULT');
    for (const line of s.outcome.lines || []) lines.push(line.label + ': ' + line.value + (line.sentence ? ' — ' + line.sentence : ''));
  }
  const text = lines.join('\n') || 'No activity yet.';
  if (log.textContent !== text) log.textContent = text;
  if (atBottom) log.scrollTop = log.scrollHeight;
}

let latestRefresh = 0;
async function refresh() {
  const request = ++latestRefresh;
  let s;
  try { s = await api('/api/state'); }
  catch (e) {
    document.getElementById('status').innerHTML = badge('server unreachable', false);
    return;
  }
  if (request !== latestRefresh) return;
  renderStatus(s);

  renderSession(s);

  renderPenalties(s);
  renderCheckpoints(s);
  renderActivity(s);

  const garage = s.inGarage ?? s.tyreRules?.inGarage;
  document.getElementById('placeBtn').disabled = !!s.busy || !s.connected || !s.session || garage !== true || !document.getElementById('cp').value;
  document.getElementById('placeHint').textContent = s.busy ? 'Follow the next action above. You can cancel the pending operation.' :
    !s.session ? 'Load a Practice session to use a checkpoint.' : garage !== true ? 'Return to the garage to prepare placement.' :
    !document.getElementById('cp').value ? 'Save a position on this track to create a destination.' : 'Prepare here, then press Drive in LMU when prompted.';
  document.getElementById('capBtn').disabled = !!s.busy || !s.session || !s.session.capturable;
  document.getElementById('cancelBtn').disabled = !s.busy;
  document.getElementById('tyreSetup').disabled = !!s.busy;
  document.getElementById('freshOnPlace').disabled = !!s.busy || !s.tyreRules;
  if (!s.tyreRules) document.getElementById('freshOnPlace').checked = false;
  updateTyreSettings();
  document.getElementById('tyreResetHint').textContent = s.tyreRulesError ? s.tyreRulesError : garage === true ? 'Press Drive when prompted. Temperature changes naturally afterward.' : 'Return to the garage to prepare four tyres.';
  document.getElementById('tyreResetBtn').disabled = !!s.busy || !s.connected || !s.session || garage !== true || !s.tyreRules;

  // Both launches are refused while the game is up, so say so with the control
  // rather than only in the error after it is pressed.
  const g = s.game || {};
  document.getElementById('launchDirect').disabled = !g.canLaunchDirect || !!g.running;
  document.getElementById('launchEac').disabled = !g.canLaunchProtected || !!g.running;

  schedule(s.busy ? 250 : 700);
}

async function toggle(which, enable) {
  const res = await api('/api/rules', {
    enable: enable,
    pitSpeeding: false,
    trackLimits: which === 'trackLimits'
  });
  if (res.error) showNotice(res.error);
  refresh();
}

async function capture() {
  const input = document.getElementById('capName');
  const name = input.value.trim();
  if (!name) { input.focus(); return; }
  const res = await api('/api/capture', {name: name});
  if (res.error) {
    showNotice(res.error + (res.failures ? '\n\n' + res.failures.join('\n') : ''));
  } else {
    input.value = '';
  }
  refresh();
}

async function place() {
  const checkpoint = document.getElementById('cp').value;
  if (!checkpoint) return;
  const body = {checkpoint: checkpoint};
  if (document.getElementById('freshOnPlace').checked) {
    try { body.tyres = tyreOptions(); } catch (e) { showNotice(e.message); return; }
  }
  const res = await api('/api/place', body);
  if (res.error) showNotice(res.error);
  refresh();
}

async function cancelPlacement() {
  const res = await api('/api/cancel', {});
  if (res.error) showNotice(res.error);
  refresh();
}

const TYRE_WHEELS = ['FL','FR','RL','RR'];
function updateTyreSettings() {
  for (const wheel of TYRE_WHEELS)
    document.getElementById('tyreTemperature' + wheel).disabled = !document.getElementById('tyreTemperatureEnabled' + wheel).checked;
  let description;
  try {
    const options = tyreOptions();
    description = options.wheels.map((w,i) => TYRE_WHEELS[i] + ' ' + w.conditionPercent + '% / ' +
      (w.temperatureCelsius === null ? 'game' : w.temperatureCelsius + '°C')).join(' · ');
  } catch (e) { description = e.message; }
  document.getElementById('tyreSummary').textContent = description;
}

function tyreOptions() {
  return {wheels: TYRE_WHEELS.map(wheel => {
    const condition = document.getElementById('tyreCondition' + wheel).value.trim();
    const conditionPercent = Number(condition);
    const temperature = document.getElementById('tyreTemperature' + wheel).value.trim();
    const temperatureCelsius = document.getElementById('tyreTemperatureEnabled' + wheel).checked ? Number(temperature) : null;
    if (!condition || !Number.isFinite(conditionPercent) || conditionPercent < 0 || conditionPercent > 100)
      throw new Error(wheel + ' condition must be between 0 and 100 percent.');
    if (temperatureCelsius !== null && (!temperature || !Number.isFinite(temperatureCelsius) || temperatureCelsius < 0 || temperatureCelsius > 150))
      throw new Error(wheel + ' temperature must be between 0 and 150 °C.');
    return {conditionPercent, temperatureCelsius};
  })};
}

async function resetTyres() {
  let tyres;
  try { tyres = tyreOptions(); } catch (e) { showNotice(e.message); return; }
  const res = await api('/api/tyres', {tyres});
  if (res.error) showNotice(res.error);
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

updateTyreSettings();
refresh();
schedule(700);

</script></body></html>
""";
}
