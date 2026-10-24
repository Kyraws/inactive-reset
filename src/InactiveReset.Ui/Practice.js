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
function practiceShortcut(action, fallback) { return globalThis.practiceInputLabels?.[action] ?? fallback; }
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
  status.textContent = s.game?.protected ? 'EAC on · capture and saved destinations available' : s.connected ? 'Local play · ' + ((s.inGarage ?? s.tyreRules?.inGarage) === true ? 'garage' : 'session loaded') : s.game?.running ? 'Waiting for LMU' : 'LMU not running';
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
  const hint = canCapture(s) ? practiceShortcut('capture', 'Ctrl+Alt+C') + ': replace Quick capture for this track and car. Enter a name and choose Capture to keep a separate checkpoint.' : captureAccessIssue(s);
  document.getElementById('capHint').textContent = hint;
  document.getElementById('capHint').hidden = !hint;
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

let visibleCheckpoints = [];
let destinationState = {};
function practiceAccessIssue() {
  if (destinationState.game?.protected) return 'Teleporting and tyre preparation are disabled with EAC on. Close LMU and choose Launch local play, then load single-player Practice.';
  const nav = typeof navigation === 'undefined' ? null : navigation;
  if (nav && (nav.state?.settingMode !== 'SETTING_GRANDPRIX' || nav.state?.gameState !== 'GSTATE_DYN' ||
      !nav.state?.gameSession?.startsWith('PRACTICE') || nav.loadingStatus?.loading))
    return 'Teleporting and tyre preparation need an active single-player Practice session. Set one up, press Start session, then switch to LMU.';
  return '';
}
function speedRestorationIssue() {
  if (destinationState.game?.protected) return 'Teleporting is disabled with EAC on. Close LMU and choose Launch local play, then load single-player Practice.';
  const checkpoint = visibleCheckpoints.find(c => c.id === document.getElementById('cp').value);
  if (!checkpoint) return 'Choose a checkpoint first.';
  if (!destinationState.rollingStartSupported) return 'Speed & gear are unavailable for this game build. Use Standing teleport.';
  if (!checkpoint.rollingEligible) return 'This checkpoint needs a straight, level moving capture for speed & gear restoration. Use Standing teleport.';
  const car = destinationState.session?.vehicle || '';
  if (checkpoint.vehicle.toLowerCase() !== car.toLowerCase())
    return `Captured in ${checkpoint.vehicle}; currently driving ${car || 'an unknown car'}. Speed & gear require the capture car. Load that car or use Standing teleport.`;
  return '';
}
function canRestoreSpeed() { return !speedRestorationIssue(); }
function renderDestinationSpeed() {
  const checkpoint = visibleCheckpoints.find(c => c.id === document.getElementById('cp').value);
  const eligible = canRestoreSpeed();
  const hint = !checkpoint ? '' : eligible
    ? ((destinationState.inGarage ?? destinationState.tyreRules?.inGarage) === true ? 'Speed & gear restore automatically. Neutral, pedals released after Drive.' : 'Speed & gear restore automatically.')
    : speedRestorationIssue();
  document.getElementById('restoreSpeedHint').textContent = hint;
  document.getElementById('restoreSpeedHint').hidden = !hint;
  document.getElementById('destinationSpeed').textContent = !checkpoint ? ''
    : Number.isFinite(checkpoint.speedKmh) ? checkpoint.speedKmh.toFixed(1) + ' km/h captured'
    : 'Captured speed unavailable';
}
function updateDestination() {
  const select = document.getElementById('cp');
  document.getElementById('destinationName').textContent = select.options[select.selectedIndex]?.textContent || 'Choose a destination';
  renderDestinationSpeed();
  savePracticePreferences();
}

function speedReadyCheckpoints() {
  const s = destinationState;
  if (!(s.game?.running ?? s.connected) || !s.session) return [];
  return visibleCheckpoints.filter(c => c.vehicle.toLowerCase() === s.session.vehicle.toLowerCase() &&
    c.rollingEligible && Number.isFinite(c.speedKmh) && c.speedKmh >= 0.5 && Number.isInteger(c.gear) && c.gear > 0)
    .sort((a,b) => (a.lapDistance || 0) - (b.lapDistance || 0) || a.name.localeCompare(b.name) || a.id.localeCompare(b.id));
}

function cycleCheckpoint(direction) {
  if (destinationState.busy && destinationState.phase !== 'WaitingToClear') return;
  const list = speedReadyCheckpoints();
  if (!list.length) { showNotice('No speed-ready checkpoints for this track and car. Capture while moving in a forward gear on a straight.'); return; }
  const select = document.getElementById('cp');
  const index = list.findIndex(c => c.id === select.value);
  const checkpoint = list[index < 0 ? (direction < 0 ? list.length - 1 : 0) : (index + direction + list.length) % list.length];
  select.value = checkpoint.id;
  updateDestination();
  renderCheckpoints(destinationState);
  showNotice('Selected ' + checkpoint.name + ' · ' + checkpoint.speedKmh.toFixed(1) + ' km/h · gear ' + checkpoint.gear + (destinationState.game?.protected ? '. Saved for practice with EAC off.' : '. Teleport when ready.'));
}

async function runPracticeInput(action) {
  if (action === 'capture') await quickCapture();
  else if (action === 'teleport') await resetFromHotkey();
  else if (action === 'standing') await resetFromHotkey(true);
  else if (action === 'previous' || action === 'next') cycleCheckpoint(action === 'previous' ? -1 : 1);
}

function renderCheckpoints(s) {
  destinationState = s;
  const select = document.getElementById('cp');
  const chosen = select.value || practicePreferences.checkpoint;
  const scroll = select.scrollTop;
  const sameTrack = select.dataset.track === (s.session?.track || '').toLowerCase();
  const session = s.session;
  const list = session ? (s.checkpoints || []).filter(c =>
    c.track.toLowerCase() === session.track.toLowerCase()) : [];
  visibleCheckpoints = list;
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
  renderDestinationSpeed();
}

const diagnosticValues = new Map();
const diagnosticLog = [];
function collectDiagnostics(s) {
  const calibration = s.session && (s.calibrations || []).find(c =>
    c.track.toLowerCase() === s.session.track.toLowerCase() && c.vehicle.toLowerCase() === s.session.vehicle.toLowerCase());
  const rows = [
    ['Connection', s.game?.protected ? 'EAC on; telemetry capture available when LMU shares a position' : s.connected ? 'Local Practice connected' : s.error || 'Game not connected'],
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
  document.getElementById('phaseMsg').textContent = s.phaseMessage || (s.game?.protected
    ? (canCapture(s) ? 'You can save checkpoints with EAC on. Teleporting and tyre preparation need Launch local play.' : captureAccessIssue(s)) : !s.connected
    ? 'Launch local Practice to begin.' : !s.session ? 'Load a Practice session.'
    : (s.inGarage ?? s.tyreRules?.inGarage) === true ? 'Choose a destination or prepare your tyres.' : (s.rollingStartSupported ? 'Capture here or reset the selected checkpoint on track.' : 'Capture here; return to the garage to prepare.'));
  collectDiagnostics(s);
  const log = document.getElementById('log');
  const atBottom = log.scrollTop + log.clientHeight >= log.scrollHeight - 24;
  const lines = [...diagnosticLog, ...(s.checkpointErrors || []), ...(s.log || [])];
  if (s.outcome) {
    lines.push('', 'LAST RESULT');
    for (const line of s.outcome.lines || []) lines.push(line.label + ': ' + line.value + (line.sentence ? ' — ' + line.sentence : ''));
  }
  const text = lines.join('\n') || 'No activity yet.';
  if (log.textContent !== text) log.textContent = text;
  if (atBottom) log.scrollTop = log.scrollHeight;
}

let latestRefresh = 0;
let activeRefreshes = 0;
async function refresh() {
  activeRefreshes++;
  try { return await refreshState(++latestRefresh); }
  finally { activeRefreshes--; }
}

async function refreshState(request) {
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
  const accessIssue = practiceAccessIssue();
  const directReset = garage === false && !!s.rollingStartSupported;
  const placementBlocked = !!accessIssue || (!!s.busy && !(directReset && s.phase === 'WaitingToClear')) || !s.connected || !s.session || (garage !== true && !directReset) || !document.getElementById('cp').value;
  document.getElementById('placeBtn').disabled = placementBlocked || !canRestoreSpeed();
  document.getElementById('standingBtn').disabled = placementBlocked;
  document.getElementById('placeBtn').textContent = directReset ? 'Reset on track ↺' : 'Place checkpoint ↗';
  const placeHint = accessIssue || (s.busy ? 'Wait for the current operation to finish.' : !s.session ? 'Load single-player Practice to capture a position and teleport back to it.' : directReset ? practiceShortcut('teleport', 'Ctrl+Alt+R') + ' · speed & gear. ' + practiceShortcut('standing', 'Ctrl+Alt+Shift+R') + ' · standing.' : garage !== true ? 'Return to the garage to place.' :
    !document.getElementById('cp').value ? 'Capture a position first.' : 'Press Drive when prompted.');
  document.getElementById('placeHint').textContent = placeHint;
  document.getElementById('placeHint').hidden = !placeHint;
  document.getElementById('capBtn').disabled = !canCapture(s);
  document.getElementById('cancelBtn').disabled = !s.busy;
  document.getElementById('cancelBtn').hidden = !s.busy;
  document.getElementById('tyreSetup').disabled = !!s.busy;
  document.getElementById('freshOnPlace').disabled = !!s.game?.protected || !!s.busy || !s.tyreRules;
  if (!s.tyreRules) document.getElementById('freshOnPlace').checked = false;
  updateTyreSettings();
  document.getElementById('tyreResetHint').textContent = accessIssue || (s.tyreRulesError ? s.tyreRulesError : !s.session ? 'Load single-player Practice, then prepare tyres from its garage.' : garage === true ? 'Press Drive when prompted.' : 'Return to the garage to prepare tyres.');
  document.getElementById('tyreResetBtn').disabled = !!accessIssue || !!s.busy || !s.connected || !s.session || garage !== true || !s.tyreRules;

  // Both launches are refused while the game is up, so say so with the control
  // rather than only in the error after it is pressed.
  const g = s.game || {};
  document.getElementById('launchDirect').disabled = !g.canLaunchDirect || !!g.running;
  document.getElementById('launchEac').disabled = !g.canLaunchProtected || !!g.running;

  schedule(s.busy ? 250 : 700);
  if (typeof updateWorkspace === 'function') updateWorkspace();
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

function captureAccessIssue(s) {
  if (s.busy) return 'Wait for the current operation before capturing.';
  if (!(s.game?.running ?? s.connected)) return 'Open LMU and load a session to capture a checkpoint.';
  const nav = typeof navigation === 'undefined' ? null : navigation;
  if (nav && (nav.state?.gameState !== 'GSTATE_DYN' || nav.loadingStatus?.loading))
    return 'Load a session and press Drive in LMU before capturing a checkpoint.';
  return s.session?.capturable ? '' : 'No current car position is available. Load a session and press Drive in LMU, then try capturing again.';
}
function canCapture(s) { return !captureAccessIssue(s); }
async function capture() {
  if (!canCapture(destinationState)) { showNotice(captureAccessIssue(destinationState)); return; }
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

let quickCapturePending = false;
async function quickCapture() {
  if (quickCapturePending || !canCapture(destinationState)) return;
  quickCapturePending = true;
  try {
    const res = await api('/api/capture', {quick:true});
    if (res.error) { showNotice(res.error); return; }
    if (!res.ok || !res.id) { showNotice('Quick capture was not saved.'); return; }
    // Clear the old selection so the next refresh selects the newly saved ID.
    practicePreferences.checkpoint = res.id;
    document.getElementById('cp').value = '';
    await refresh();
    updateDestination();
    showNotice(destinationState.game?.protected ? 'Quick capture saved and selected. To teleport back to it, restart LMU using Launch local play and load single-player Practice.' : 'Quick capture saved and selected. ' + practiceShortcut('teleport', 'Ctrl+Alt+R') + ' to teleport; each quick capture replaces the previous one for this track and car.');
  } finally { quickCapturePending = false; }
}

async function place(standing = false) {
  if (practiceAccessIssue()) { showNotice(practiceAccessIssue()); return; }
  const checkpoint = document.getElementById('cp').value;
  if (!checkpoint) return;
  const directReset = (destinationState.inGarage ?? destinationState.tyreRules?.inGarage) === false;
  const body = {checkpoint: checkpoint, directReset: directReset, restoreSpeed: !standing};
  if (document.getElementById('freshOnPlace').checked) {
    if (directReset) { showNotice('Tyre conditioning requires the garage Drive handoff. Return to the garage, or uncheck Apply on garage teleport to teleport with your current tyres.'); return; }
    try { body.tyres = tyreOptions(); } catch (e) { showNotice(e.message); return; }
  }
  const res = await api('/api/place', body);
  if (res.error) showNotice(res.error);
  refresh();
}

async function resetFromHotkey(standing = false) {
  if (practiceAccessIssue()) { showNotice(practiceAccessIssue()); return; }
  const garage = destinationState.inGarage ?? destinationState.tyreRules?.inGarage;
  if ((destinationState.busy && destinationState.phase !== 'WaitingToClear') || !destinationState.connected || !destinationState.session ||
      (garage !== true && (garage !== false || !destinationState.rollingStartSupported))) return;
  if (!standing && !canRestoreSpeed()) { showNotice(speedRestorationIssue()); return; }
  try { await place(standing); } catch (e) { showNotice(e.message); }
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
  if (practiceAccessIssue()) { showNotice(practiceAccessIssue()); return; }
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
  timer = setInterval(() => { if (!activeRefreshes) return refresh(); }, ms);
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
