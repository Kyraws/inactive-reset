namespace InactiveReset.Ui;

/// <summary>Practice workspace inside the shared application shell.</summary>
internal static class Page
{
    public const string Html = """
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Practice tools · Inactive Reset</title><link rel="stylesheet" href="/app.css"></head><body>
<header class="topbar"><a class="brand" href="/session"><span class="brand-mark">IR</span>Inactive Reset</a><nav aria-label="Workspaces"><a class="workspace" href="/session">Session setup</a><a class="workspace active" href="/" aria-current="page">Practice tools</a></nav><span class="connection" id="status">Connecting to LMU</span><button class="button" id="launchDirect" onclick="launch('direct')">Launch local play</button></header>
<div class="workspace-grid practice-workspace">
<aside class="sidebar checkpoints-card"><p class="eyebrow">PRACTICE LIBRARY</p><h2 id="destinationsLabel">Checkpoints</h2><select id="cp" size="12" aria-labelledby="destinationsLabel" onchange="updateDestination()"></select><div class="capture-area"><label class="input-label" for="capName">Save current position</label><input id="capName" type="text" placeholder="Checkpoint name" autocomplete="off" spellcheck="false"><button class="button" id="capBtn" onclick="capture()">Capture checkpoint</button><p class="hint" id="capHint"></p></div><details class="online-launch"><summary>Online launch</summary><p class="small-note">Practice tools are unavailable with EAC.</p><button class="button" id="launchEac" onclick="launch('eac')">Launch with EAC</button></details></aside>
<main class="editor practice-editor"><div class="page-heading"><p class="eyebrow">MAKE EVERY LAP COUNT</p><h1>Practice tools</h1><p>Prepare your tyres and return to a saved point on this track.</p></div><div class="controls-grid"><section class="settings-card practice-card tyres-card"><h2>Tyres</h2><fieldset id="tyreSetup"><table class="tyre-settings"><thead><tr><th>Tyre</th><th>Condition %</th><th colspan="2">Initial heat °C</th></tr></thead><tbody><tr><th scope="row">FL</th><td><input id="tyreConditionFL" aria-label="Front left condition percent" type="number" min="0" max="100" value="100" oninput="updateTyreSettings()"></td><td><input id="tyreTemperatureEnabledFL" aria-label="Set front left initial heat" type="checkbox" onchange="updateTyreSettings()"></td><td><input id="tyreTemperatureFL" aria-label="Front left initial temperature Celsius" type="number" min="0" max="150" value="70" disabled oninput="updateTyreSettings()"></td></tr><tr><th scope="row">FR</th><td><input id="tyreConditionFR" aria-label="Front right condition percent" type="number" min="0" max="100" value="100" oninput="updateTyreSettings()"></td><td><input id="tyreTemperatureEnabledFR" aria-label="Set front right initial heat" type="checkbox" onchange="updateTyreSettings()"></td><td><input id="tyreTemperatureFR" aria-label="Front right initial temperature Celsius" type="number" min="0" max="150" value="70" disabled oninput="updateTyreSettings()"></td></tr><tr><th scope="row">RL</th><td><input id="tyreConditionRL" aria-label="Rear left condition percent" type="number" min="0" max="100" value="100" oninput="updateTyreSettings()"></td><td><input id="tyreTemperatureEnabledRL" aria-label="Set rear left initial heat" type="checkbox" onchange="updateTyreSettings()"></td><td><input id="tyreTemperatureRL" aria-label="Rear left initial temperature Celsius" type="number" min="0" max="150" value="70" disabled oninput="updateTyreSettings()"></td></tr><tr><th scope="row">RR</th><td><input id="tyreConditionRR" aria-label="Rear right condition percent" type="number" min="0" max="100" value="100" oninput="updateTyreSettings()"></td><td><input id="tyreTemperatureEnabledRR" aria-label="Set rear right initial heat" type="checkbox" onchange="updateTyreSettings()"></td><td><input id="tyreTemperatureRR" aria-label="Rear right initial temperature Celsius" type="number" min="0" max="150" value="70" disabled oninput="updateTyreSettings()"></td></tr></tbody></table></fieldset><label class="checkbox-row"><input id="freshOnPlace" type="checkbox" onchange="updateTyreSettings()">Apply on placement</label><button class="button" id="tyreResetBtn" onclick="resetTyres()" disabled>Prepare tyres</button><p class="hint" id="tyreSummary"></p><p id="tyreResetHint"></p></section>
<section class="settings-card practice-card penalties-card"><h2>Rules</h2><div id="penalties"></div><div class="rule-status"><span>Flag Rules</span><span id="flagStatus">Unknown</span></div><p class="rules-note">Flag Rules are managed during pit cleanup. Keep the pit limiter on until CLEAR.</p></section>
</div><section class="settings-card practice-card activity-card" aria-label="Activity and operation progress"><div class="activity-head"><h2>Activity</h2></div><pre id="log" aria-label="Activity log"></pre></section>
</main>
<aside class="summary" aria-label="Practice summary"><p class="eyebrow">CURRENT SESSION</p><h2>Your practice</h2><div class="selection-summary session" id="session"><strong>Launch local play to begin</strong></div><div class="destination-summary"><p class="eyebrow">SELECTED CHECKPOINT</p><h3 id="destinationName">Choose a destination</h3></div><div class="launch-area"><p class="eyebrow">NEXT STEP</p><div class="phasebar"><span class="phase" id="phase">IDLE</span><span class="phasemsg" id="phaseMsg" role="status" aria-live="polite" aria-atomic="true">Load a Practice session.</span></div><button class="button cancel-operation" id="cancelBtn" onclick="cancelPlacement()" disabled hidden>Cancel operation</button><button class="button primary start-button" id="placeBtn" onclick="place()" disabled>Place checkpoint <span aria-hidden="true">↗</span></button><p class="launch-hint" id="placeHint">Load a Practice session to use a checkpoint.</p><a class="practice-link" href="/session">Edit session setup <span aria-hidden="true">→</span></a><p class="scope-note">Placement and tyre preparation require an offline Practice session. Return to the garage before preparing.</p></div></aside>
</div><div id="notice" class="error-bar" role="alert" hidden><span id="noticeText"></span><button aria-label="Dismiss error" onclick="document.getElementById('notice').hidden=true">×</button></div>
<script>

'use strict';

function esc(s) {
  return String(s === null || s === undefined ? '' : s)
    .replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}

// Keep preparation choices when moving between the application's workspaces.
let practicePreferences = {};
try { practicePreferences = JSON.parse(sessionStorage.getItem('practice-preferences') || '{}'); } catch {}
function savePracticePreferences() {
  const wheels = {};
  for (const wheel of TYRE_WHEELS) wheels[wheel] = {
    condition:document.getElementById('tyreCondition'+wheel).value,
    heat:document.getElementById('tyreTemperature'+wheel).value,
    enabled:document.getElementById('tyreTemperatureEnabled'+wheel).checked
  };
  practicePreferences = {wheels,apply:document.getElementById('freshOnPlace').checked,checkpoint:document.getElementById('cp').value || practicePreferences.checkpoint};
  try { sessionStorage.setItem('practice-preferences',JSON.stringify(practicePreferences)); } catch {}
}
let noticeTimer;
function showNotice(message) {
  const el = document.getElementById('notice');
  document.getElementById('noticeText').textContent = message;
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
  const status = document.getElementById('status');
  status.textContent = s.game?.protected ? 'EAC launch · tools unavailable' : s.connected ? 'Local play · ' + ((s.inGarage ?? s.tyreRules?.inGarage) === true ? 'garage' : 'session loaded') : s.game?.running ? 'Waiting for LMU' : 'LMU not running';
  status.className = 'connection' + (s.connected ? ' ready' : '');
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
  savePracticePreferences();
}

function renderCheckpoints(s) {
  const select = document.getElementById('cp');
  const chosen = select.value || practicePreferences.checkpoint;
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
  document.getElementById('placeHint').textContent = s.busy ? 'Follow the Activity instructions. You can cancel the pending operation.' :
    !s.session ? 'Load a Practice session to use a checkpoint.' : garage !== true ? 'Return to the garage to prepare placement.' :
    !document.getElementById('cp').value ? 'Save a position on this track to create a destination.' : 'Prepare here, then press Drive in LMU when prompted.';
  document.getElementById('capBtn').disabled = !!s.busy || !s.session || !s.session.capturable;
  document.getElementById('cancelBtn').disabled = !s.busy;
  document.getElementById('cancelBtn').hidden = !s.busy;
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
  savePracticePreferences();
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

for (const wheel of TYRE_WHEELS) {
  const saved = practicePreferences.wheels?.[wheel];
  if (!saved) continue;
  document.getElementById('tyreCondition'+wheel).value = saved.condition;
  document.getElementById('tyreTemperature'+wheel).value = saved.heat;
  document.getElementById('tyreTemperatureEnabled'+wheel).checked = !!saved.enabled;
}
document.getElementById('freshOnPlace').checked = !!practicePreferences.apply;
updateTyreSettings();
refresh();
schedule(700);

</script></body></html>
""";
}
