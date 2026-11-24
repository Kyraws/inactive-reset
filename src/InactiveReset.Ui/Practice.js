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
  practicePreferences = {wheels,apply:document.getElementById('freshOnPlace').checked,
    checkpoint:destinationState.session ? document.getElementById('cp').value : practicePreferences.checkpoint};
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
  const recorder = document.getElementById('recorderStatus');
  if (recorder && s.recorder) {
    const r = s.recorder;
    recorder.textContent = 'Telemetry recorder: ' + r.state + ' · ' + r.samples + ' fresh frames · ' + r.dropped + ' dropped entries' + (r.error || r.reason ? ' · ' + (r.error || r.reason) : '');
  }
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

function practiceTrackLabel(track) {
  return typeof liveTrackDisplay==='function' ? liveTrackDisplay(track) : track;
}
function practiceVehicleModel(vehicle) {
  return vehicle?.vehicleModel || 'Unknown car';
}
function compatibleVehicle(session, vehicle, key) {
  return !!session && (session.vehicleCompatibilityKey && key
    ? session.vehicleCompatibilityKey === key
    : session.vehicle.toLowerCase() === vehicle.toLowerCase());
}
function renderSession(s) {
  const d = s.session, garage = s.inGarage ?? s.tyreRules?.inGarage;
  const track = d ? practiceTrackLabel(d.track) : '';
  const stage = garage === true ? 'In garage' : garage === false ? 'On track' : 'Session loaded';
  document.getElementById('session').innerHTML = d
    ? '<strong title="' + esc(d.track) + '">' + esc(track) + '</strong><span class="session-car">' + esc(practiceVehicleModel(d)) + '</span><span class="session-state">' + stage + '</span>'
    : '<strong>' + (s.connected ? 'Load single-player Practice' : 'Launch the game to begin') + '</strong>';
  document.getElementById('practiceContext').textContent = d ? [track,practiceVehicleModel(d),stage].join(' · ') : 'Load a Practice session in LMU';
  const hint = canCapture(s) ? practiceShortcut('capture', 'Ctrl+Alt+C') + ' · Quick capture' : captureAccessIssue(s);
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
  if (timelineSelection) {
    if (!compatibleVehicle(destinationState.session, timelineSelection.Checkpoint.VehicleName, timelineSelection.VehicleCompatibilityKey))
      return 'Speed & gear require a car compatible with the recorded chassis.';
    return timelineSelection.RollingUnavailableReason || (!destinationState.rollingStartSupported ? 'Speed & gear are unavailable for this build.' : '');
  }
  if (destinationState.game?.protected) return 'Teleporting is disabled with EAC on. Close LMU and choose Launch local play, then load single-player Practice.';
  const checkpoint = visibleCheckpoints.find(c => c.id === document.getElementById('cp').value);
  if (!checkpoint) return 'Choose a checkpoint first.';
  if (!destinationState.rollingStartSupported) return 'Speed & gear are unavailable for this game build. Use Standing teleport.';
  if (!checkpoint.rollingEligible) return (checkpoint.rollingUnavailableReason || 'Speed & gear are unavailable for this checkpoint.') + ' Use Standing teleport.';
  if (!compatibleVehicle(destinationState.session, checkpoint.vehicle, checkpoint.vehicleCompatibilityKey))
    return `Captured in ${practiceVehicleModel(checkpoint)}. Currently driving ${practiceVehicleModel(destinationState.session)}. Speed & gear require a compatible capture car. Use Standing teleport for this car.`;
  return '';
}
function canRestoreSpeed() { return !speedRestorationIssue(); }
function placementIssue() {
  const s = destinationState;
  const garage = s.inGarage ?? s.tyreRules?.inGarage;
  const directReset = garage === false && s.rollingStartSupported;
  return practiceAccessIssue() ||
    (timelinePendingRequest && timelinePendingRequest === timelineRequest ? 'Waiting for the selected recorded frame.' :
      s.busy && !(directReset && s.phase === 'WaitingToClear') ? 'Wait for the current operation to finish.' :
      !s.connected || !s.session ? 'Load single-player Practice to capture a position and teleport back to it.' :
      garage !== true && !directReset ? 'On-track reset is unavailable. Return to the garage to place a checkpoint.' :
      timelineSelection && (timelineSelection.Checkpoint.TrackName.toLowerCase() !== s.session.track.toLowerCase() || !compatibleVehicle(s.session, timelineSelection.Checkpoint.VehicleName, timelineSelection.VehicleCompatibilityKey)) ? 'Load the recorded layout and a compatible car to teleport.' :
      timelineSelection?.Checkpoint.ExecutableSha256 && s.build && !timelineSelection.Checkpoint.ExecutableSha256.toLowerCase().startsWith(s.build.toLowerCase()) ? 'Recorded teleport requires the LMU build used for this lap.' :
      !timelineSelection && !document.getElementById('cp').value ? 'Choose a teleport target.' : tyrePlacementIssue(s));
}
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
  timelineRequest++;
  timelineSelection = null;
  selectedLapPoint = null;
  updateLapControls();
  savePracticePreferences();
  updatePlacementControls();
}

function speedReadyCheckpoints(quick = false) {
  const s = destinationState;
  if (!(s.game?.running ?? s.connected) || !s.session) return [];
  return visibleCheckpoints.filter(c => !!c.quick === quick && compatibleVehicle(s.session, c.vehicle, c.vehicleCompatibilityKey) &&
    c.rollingEligible && Number.isFinite(c.speedKmh) && c.speedKmh >= 0.5 && Number.isInteger(c.gear) && c.gear > 0)
    .sort((a,b) => (a.lapDistance || 0) - (b.lapDistance || 0) || a.name.localeCompare(b.name) || a.id.localeCompare(b.id));
}

const cyclePositions = new Map();
function cycleCheckpoint(direction, quick = false) {
  if (destinationState.busy || placementSubmitting) return;
  const list = speedReadyCheckpoints(quick);
  if (!list.length) {
    showNotice('No speed-ready ' + (quick ? 'Quick Captures' : 'Saved Checkpoints') + ' for this layout and car.');
    return;
  }
  const select = document.getElementById('cp');
  const key = JSON.stringify([destinationState.session.track, destinationState.session.vehicleCompatibilityKey || destinationState.session.vehicle, quick]);
  const selected = list.some(c => c.id === select.value) ? select.value : cyclePositions.get(key);
  const index = list.findIndex(c => c.id === selected);
  const checkpoint = list[index < 0 ? (direction < 0 ? list.length - 1 : 0) : (index + direction + list.length) % list.length];
  cyclePositions.set(key, checkpoint.id);
  select.value = checkpoint.id;
  updateDestination();
  renderCheckpoints(destinationState);
  announceTarget(checkpoint.name);
  showNotice('Selected ' + checkpoint.name + ' · ' + checkpoint.speedKmh.toFixed(1) + ' km/h · gear ' + checkpoint.gear + (destinationState.game?.protected ? '. Saved for practice with EAC off.' : '. Teleport when ready.'));
}

async function runPracticeInput(action) {
  if (action === 'capture') await quickCapture();
  else if (action === 'teleport') await resetFromHotkey();
  else if (action === 'standing') await resetFromHotkey(true);
  else if (action === 'nextQuick') cycleCheckpoint(1, true);
  else if (action === 'clearQuick') await clearQuick();
  else if (action === 'previous' || action === 'next') cycleCheckpoint(action === 'previous' ? -1 : 1);
}

function renderCheckpoints(s) {
  const before = destinationState.session;
  if (before && (before.track !== s.session?.track || before.vehicle !== s.session?.vehicle)) {
    clearQuickScope = ''; timelineRequest++;
    document.getElementById('clearQuickStatus').textContent = '';
  }
  destinationState = s;
  const select = document.getElementById('cp');
  const chosen = select.value || practicePreferences.checkpoint;
  const scroll = select.scrollTop;
  const sameTrack = select.dataset.track === (s.session?.track || '').toLowerCase();
  const session = s.session;
  const list = session ? (s.checkpoints || []).filter(c =>
    c.track.toLowerCase() === session.track.toLowerCase() && (!c.quick || compatibleVehicle(session, c.vehicle, c.vehicleCompatibilityKey))) : [];
  visibleCheckpoints = list;
  const options = '<option value="">Choose a destination</option>' + list.map(c => '<option value="' + esc(c.id) + '">' + esc(c.name) + '</option>').join('');
  // Polling must not rebuild an open selector or interrupt keyboard selection.
  if (select.dataset.options !== options) {
    select.innerHTML = options;
    select.dataset.options = options;
  }
  select.value = list.some(c => c.id === chosen) ? chosen : '';
  if (session && chosen && !select.value) { practicePreferences.checkpoint = ''; savePracticePreferences(); }
  select.scrollTop = sameTrack ? scroll : 0;
  select.dataset.track = (session?.track || '').toLowerCase();
  select.disabled = !!s.busy || !list.length;
  updatePlacementControls();
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
  rows.push(['Destination', checkpoint ? checkpoint.name + ' / captured in ' + practiceVehicleModel(checkpoint) : null]);
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
  const progress = document.getElementById('practiceProgress');
  if (progress) progress.hidden = !s.busy && phase === 'Idle';
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
  document.getElementById('capBtn').disabled = !canCapture(s);
  document.getElementById('cancelBtn').disabled = !s.busy;
  document.getElementById('cancelBtn').hidden = !s.busy;
  document.getElementById('tyreSetup').disabled = !!s.busy;
  const applyTyres = document.getElementById('freshOnPlace');
  applyTyres.disabled = !!s.busy || !applyTyres.checked && (!!s.game?.protected || !s.tyreRules);
  updateTyreSettings();
  document.getElementById('tyreResetHint').textContent = accessIssue || (s.tyreRulesError ? s.tyreRulesError : !s.session ? 'Load single-player Practice, then prepare tyres from its garage.' : garage === true ? 'Press Drive when prompted.' : 'Return to the garage to prepare tyres.');
  updateTyreAction();

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
  if (s.busy || placementSubmitting) return 'Wait for the current operation before capturing.';
  if (!(s.game?.running ?? s.connected)) return 'Open LMU and load a session to capture a checkpoint.';
  const nav = typeof navigation === 'undefined' ? null : navigation;
  if (nav && (nav.state?.gameState !== 'GSTATE_DYN' || nav.loadingStatus?.loading))
    return 'Load a session and press Drive in LMU before capturing a checkpoint.';
  return s.session?.capturable ? '' : 'No current car position is available. Load a session and press Drive in LMU, then try capturing again.';
}
function canCapture(s) { return !captureAccessIssue(s); }
let quickCapturePending = false;
async function quickCapture() {
  if (quickCapturePending) return;
  const issue = captureAccessIssue(destinationState);
  if (issue) { showNotice(issue); return; }
  quickCapturePending = true;
  try {
    const res = await api('/api/capture', {quick:true});
    if (res.error) { showNotice(res.error + (res.failures ? '\n\n' + res.failures.join('\n') : '')); return; }
    if (!res.ok || !res.id) { showNotice('Quick capture was not saved.'); return; }
    // Clear the old selection so the next refresh selects the newly saved ID.
    timelineSelection = null;
    timelineRequest++;
    practicePreferences.checkpoint = res.id;
    document.getElementById('cp').value = '';
    await refresh();
    updateDestination();
    const selected = document.getElementById('cp').value === res.id;
    if (selected) announceTarget(visibleCheckpoints.find(c => c.id === res.id).name + ' saved');
    const next = selected ? placementIssue() || speedRestorationIssue() || 'Use Teleport with speed & gear to return.' :
      'Choose it in Practice when it appears for this track.';
    showNotice((selected ? 'Quick capture saved and selected. ' : 'Quick capture saved. ') + next);
  } finally { quickCapturePending = false; }
}

let placementSubmitting = false;
async function place(standing = false) {
  const issue = placementIssue() || (standing ? '' : speedRestorationIssue());
  if (issue) { showNotice(issue); return; }
  const checkpoint = document.getElementById('cp').value;
  if (placementSubmitting) return;
  const directReset = (destinationState.inGarage ?? destinationState.tyreRules?.inGarage) === false;
  const body = timelineSelection ? {recording:timelineSelection.SegmentId, sequence:timelineSelection.Sequence} : {checkpoint};
  Object.assign(body, {directReset, restoreSpeed:!standing});
  if (document.getElementById('freshOnPlace').checked) body.tyres = tyreOptions();
  placementSubmitting = true;
  try {
    const res = await api('/api/place', body);
    if (res.error) showNotice(res.error);
    await refresh();
  } finally { placementSubmitting = false; }
}

async function resetFromHotkey(standing = false) {
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
  if (destinationState.busy || placementSubmitting) return;
  let tyres;
  try { tyres = tyreOptions(); } catch (e) { showNotice(e.message); return; }
  placementSubmitting = true;
  try {
    const res = await api('/api/tyres', {tyres});
    if (!res.error && document.getElementById('practiceTyres').open) document.getElementById('practiceTyres').close();
    await refresh();
    if (res.error) document.getElementById('tyreResetHint').textContent = res.error;
  } finally { placementSubmitting = false; }
}

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

let timelineSelection = null, timelineRequest = 0, timelinePendingRequest = 0, lapRequest = 0;
let recordedLaps = [], lapPoints = [], previewLap = null, selectedLapPoint = null;
let lapRefresh = null, lapRefreshPending = false;
const lapElement = id => document.getElementById(id);

function updatePlacementControls() {
  const selected = visibleCheckpoints.find(c => c.id === lapElement('cp').value);
  lapElement('destinationName').textContent = timelineSelection?.label || (selected
    ? (selected.quick ? 'Quick Captures' : 'Saved Checkpoints') + ' · ' + selected.name : 'Choose a destination');
  renderDestinationSpeed();
  if (timelineSelection && Number.isFinite(timelineSelection.SpeedKmh))
    lapElement('destinationSpeed').textContent = timelineSelection.SpeedKmh.toFixed(1) + ' km/h · gear ' + timelineSelection.Checkpoint.Gear;
  const issue = placementIssue(), speed = speedRestorationIssue();
  lapElement('placeBtn').disabled = !!issue || !!speed;
  lapElement('standingBtn').disabled = !!issue;
  lapElement('placeHint').textContent = issue || (timelineSelection && speed) ||
    ((destinationState.inGarage ?? destinationState.tyreRules?.inGarage) === true ? 'Press Drive in LMU when prompted.' : '');
}

function tyreDraftIssue() {
  try { tyreOptions(); return ''; } catch (e) { return e.message; }
}

function updateTyreAction() {
  const s = destinationState, issue = tyreDraftIssue();
  lapElement('tyreDraftError').textContent = issue;
  lapElement('tyreResetBtn').disabled = !!practiceAccessIssue() || !!s.busy || !s.connected ||
    !s.session || (s.inGarage ?? s.tyreRules?.inGarage) !== true || !s.tyreRules || !!issue;
}

function tyrePlacementIssue(s) {
  if (!lapElement('freshOnPlace').checked) return '';
  if ((s.inGarage ?? s.tyreRules?.inGarage) !== true)
    return 'Return to the garage or turn off Apply tyres to teleport with your current tyres.';
  return !s.tyreRules ? s.tyreRulesError || 'Tyre preparation is unavailable.' : tyreDraftIssue();
}

function eligibleQuick(c, s = destinationState) {
  return c.quick && !!s.session && c.track.toLowerCase() === s.session.track.toLowerCase() &&
    c.vehicle.toLowerCase() === s.session.vehicle.toLowerCase();
}

function announceTarget(name) {
  if (localStorage.getItem('practice-voice') !== 'on' || !globalThis.speechSynthesis) return;
  speechSynthesis.cancel();
  speechSynthesis.speak(new SpeechSynthesisUtterance(name));
}

let clearQuickScope = '', clearingQuick = false;
async function clearQuick() {
  const s = destinationState, status = lapElement('clearQuickStatus');
  if (s.busy || placementSubmitting || clearingQuick || !s.session) return;
  const {track, vehicle} = s.session;
  const ids = visibleCheckpoints.filter(c => eligibleQuick(c)).map(c => c.id).sort();
  const scope = JSON.stringify([track, vehicle, ids]);
  if (!ids.length) { status.textContent = 'No Quick Captures for this layout and car.'; return; }
  if (scope !== clearQuickScope) {
    clearQuickScope = scope;
    status.textContent = 'Clear ' + ids.length + ' for ' + track + ' · ' + practiceVehicleModel(s.session) + '? Press Clear Quick Captures again to confirm.';
    showNotice(status.textContent);
    announceTarget('Press Clear Quick Captures again to confirm.');
    return;
  }
  clearQuickScope = '';
  clearingQuick = true;
  try {
    const result = await api('/api/checkpoints/manage', {action:'clearQuick', track, vehicle, ids});
    status.textContent = result.error || 'Cleared ' + result.count + ' Quick Captures.';
    showNotice(status.textContent);
    await refresh();
  } finally { clearingQuick = false; }
}

function toggleRecordedLaps() {
  if (lapElement('practicePanel').hidden) { clearInterval(lapRefresh); lapRefresh = null; return; }
  if (lapRefresh !== null) return;
  loadRecordedLaps();
  lapRefresh = setInterval(loadRecordedLaps, 8000);
}

function lapTime(seconds) {
  const ms = Math.round(seconds * 1000);
  return Math.floor(ms / 60000) + ':' + (ms % 60000 / 1000).toFixed(3).padStart(6, '0');
}

const recordedSessionId = lap => lap.RecordedSessionId || lap.SegmentId;
const recordedDateTime = new Intl.DateTimeFormat(undefined, {dateStyle:'medium',timeStyle:'medium'});
async function loadRecordedLaps() {
  if (lapRefreshPending) return;
  lapRefreshPending = true;
  const rows = await api('/api/recordings/laps');
  lapRefreshPending = false;
  const status = lapElement('timelineRange');
  if (!Array.isArray(rows)) { status.textContent = rows.error || 'Could not load recorded laps.'; return; }
  const complete = rows.filter(lap => !lap.IsOpeningRun);
  if (JSON.stringify(complete) !== JSON.stringify(recordedLaps) || lapElement('timelineSession').disabled && !complete.length) {
    recordedLaps = complete;
    renderLapRail();
  }
  status.textContent = complete.length ? '' : 'Completed laps appear after driving from one start/finish crossing to the next.';
}

function renderLapRail(reset = false) {
  const picker = lapElement('timelineSession'), rail = lapElement('lapRail');
  const previous = picker.value, groups = new Map();
  for (const lap of recordedLaps) {
    const id = recordedSessionId(lap);
    if (!groups.has(id)) groups.set(id, []);
    groups.get(id).push(lap);
  }
  const options = [...groups].map(([id,laps]) => {
    const lap = laps[0], when = lap.RecordedUtc && new Date(lap.RecordedUtc);
    const date = when && Number.isFinite(when.getTime()) ? recordedDateTime.format(when) : 'Date unknown';
    return new Option(date + ' · ' + practiceTrackLabel(lap.Identity.Track) + ' · ' + laps.length + ' completed ' + (laps.length === 1 ? 'lap' : 'laps'), id);
  });
  picker.replaceChildren(...(options.length ? options : [new Option('No completed laps', '')]));
  if (groups.has(previous)) picker.value = previous;
  picker.title = options.length ? picker.selectedOptions[0].text : '';
  const laps = (groups.get(picker.value) || []).sort((a,b) => a.Number - b.Number);
  if (reset || (previewLap && !laps.some(lap => lap.Id === previewLap.Id))) chooseRecordedLap(null);
  const best = laps.reduce((fastest,lap) => !fastest || lap.Duration < fastest.Duration ? lap : fastest, null);
  const scroll = reset ? 0 : rail.scrollLeft, focused = document.activeElement?.dataset.lap;
  rail.innerHTML = laps.map(lap => '<button class="button lap-chip' + (lap === best ? ' best-lap' : '') + '" data-lap="' + esc(lap.Id) + '" aria-pressed="' + (previewLap?.Id === lap.Id) + '" aria-label="Lap ' + lap.Number + (lap === best ? ', Best' : '') + '"' + (destinationState.busy ? ' disabled' : '') + '>' + lap.Number + (lap === best ? ' <small>Best</small>' : '') + '</button>').join('');
  picker.disabled = !groups.size || !!destinationState.busy;
  rail.scrollLeft = scroll;
  [...rail.children].find(button => button.dataset.lap === focused)?.focus({preventScroll:true});
}

async function chooseRecordedLap(id) {
  if (destinationState.busy || placementSubmitting) return;
  const request = ++lapRequest;
  timelineRequest++;
  updatePlacementControls();
  previewLap = recordedLaps.find(lap => lap.Id === id) || null;
  lapPoints = []; selectedLapPoint = null;
  for (const button of lapElement('lapRail').children) button.setAttribute('aria-pressed', String(button.dataset.lap === previewLap?.Id));
  const status = lapElement('timelineSelection');
  status.textContent = previewLap ? 'Loading recorded lap…' : '';
  lapElement('lapElapsed').removeAttribute('aria-invalid');
  updateLapControls();
  if (!previewLap) return;
  const points = await api('/api/recordings/lap?id=' + encodeURIComponent(id));
  if (request !== lapRequest) return;
  if (!Array.isArray(points)) { status.textContent = points.error || 'Could not load lap.'; return; }
  lapPoints = points;
  status.textContent = '';
  if (timelineSelection?.lapId === id) selectedLapPoint = points.find(p => p.Sequence === timelineSelection.Sequence) || null;
  updateLapControls();
}

function updateLapGuards() {
  const busy = !!destinationState.busy, disabled = !lapPoints.length || busy;
  lapElement('timelineScrub').disabled = lapElement('lapElapsed').disabled = disabled;
  lapElement('timelineSession').disabled = !recordedLaps.length || busy;
  for (const button of document.querySelectorAll('[data-nudge]')) button.disabled = disabled;
  for (const button of lapElement('lapRail').children) button.disabled = busy;
}

function updateLapControls() {
  updateLapGuards();
  const slider = lapElement('timelineScrub'), elapsed = lapElement('lapElapsed');
  slider.max = previewLap?.Duration || 1;
  slider.value = selectedLapPoint?.Elapsed ?? 0;
  slider.setAttribute('aria-valuetext', selectedLapPoint ? lapTime(selectedLapPoint.Elapsed) : 'No point selected');
  elapsed.value = selectedLapPoint ? lapTime(selectedLapPoint.Elapsed) : '';
  lapElement('timelineMapMeta').textContent = previewLap ? 'Recorded lap ' + previewLap.Number + (previewLap.LmuLapNumber == null ? '' : ' · LMU lap ' + previewLap.LmuLapNumber) + ' · ' + lapTime(previewLap.Duration) + ' · ' + practiceTrackLabel(previewLap.Identity.Track) + ' · ' + (previewLap.VehicleModel || 'Unknown car') : '';
  if (typeof renderPracticeManagement === 'function') renderPracticeManagement();
}

function parseLapElapsed(input) {
  const raw = input.trim();
  const clock = /^(\d+):([0-5]?\d(?:\.\d+)?)$/.exec(raw);
  if (clock) return Number(clock[1]) * 60 + Number(clock[2]);
  if (!/^\d+(?:\.\d+)?$/.test(raw)) return NaN;
  const [whole, fraction] = raw.split('.');
  if (whole.length < 3) return Number(raw);
  const seconds = Number(whole.slice(-2) + (fraction ? '.' + fraction : ''));
  return seconds < 60 ? Number(whole.slice(0,-2)) * 60 + seconds : NaN;
}

function selectElapsed(seconds) {
  if (!previewLap || !lapPoints.length || destinationState.busy || placementSubmitting) return;
  const rounded = Math.round(previewLap.Duration * 1000) / 1000;
  if (!Number.isFinite(seconds) || seconds < 0 || (seconds > previewLap.Duration && seconds !== rounded)) {
    lapElement('timelineSelection').textContent = 'Enter an elapsed time from 0:00 to ' + lapTime(previewLap.Duration) + '.';
    lapElement('lapElapsed').setAttribute('aria-invalid', 'true');
    return;
  }
  const point = lapPoints.reduce((nearest, p) => Math.abs(p.Elapsed-seconds) < Math.abs(nearest.Elapsed-seconds) ? p : nearest);
  return selectLapPoint(point);
}

async function selectLapPoint(point) {
  if (!point || !previewLap || destinationState.busy || placementSubmitting) return;
  const lap = previewLap, request = ++timelineRequest;
  const status = lapElement('timelineSelection');
  status.textContent = 'Loading selected point…';
  timelinePendingRequest = request;
  updatePlacementControls();
  lapElement('lapElapsed').removeAttribute('aria-invalid');
  const result = await api('/api/recordings/select', {recording:lap.SegmentId, sequence:point.Sequence});
  if (timelinePendingRequest === request) timelinePendingRequest = 0;
  if (request !== timelineRequest || destinationState.busy || placementSubmitting) return;
  if (result.error) {
    status.textContent = result.error;
    updateLapControls();
    updatePlacementControls();
    return;
  }
  timelineSelection = {...result, lapId:lap.Id, label:'Recorded lap ' + lap.Number + ' · ' + lapTime(point.Elapsed)};
  selectedLapPoint = point;
  lapElement('cp').value = '';
  practicePreferences.checkpoint = '';
  savePracticePreferences();
  status.textContent = '';
  updateLapControls();
  updatePlacementControls();
}

lapElement('timelineSession').addEventListener('change', () => renderLapRail(true));
lapElement('lapRail').addEventListener('click', event => {
  const id = event.target.closest('[data-lap]')?.dataset.lap;
  if (id) chooseRecordedLap(id);
});
lapElement('timelineScrub').addEventListener('input', event => {
  lapElement('lapElapsed').value = lapTime(Number(event.target.value));
});
lapElement('timelineScrub').addEventListener('change', event => selectElapsed(Number(event.target.value)));
lapElement('lapElapsed').addEventListener('change', event => selectElapsed(parseLapElapsed(event.target.value)));
lapElement('lapElapsed').addEventListener('keydown', event => { if (event.key === 'Enter') event.target.blur(); });
for (const button of document.querySelectorAll('[data-nudge]')) button.addEventListener('click', () => {
  const elapsed = (selectedLapPoint?.Elapsed ?? 0) + Number(button.dataset.nudge);
  selectElapsed(Math.max(0, Math.min(previewLap.Duration, elapsed)));
});
lapElement('clearQuickButton').addEventListener('click', clearQuick);
