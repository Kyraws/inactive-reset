'use strict';
const $ = id => document.getElementById(id);
const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
let setup = null, catalog = null, navigation = null;
let section = 'selection', weatherSession = 'PRACTICE', weatherNode = 'START';
let saving = false, starting = false, menuReady = false, gameRunning = false, serverBusy = false;
let carSearch = '', settingSearch = '', pollTimer, polling = false;
let confirmedPid = 0;
try { confirmedPid = Number(sessionStorage.getItem('lmu-menu-confirmed-pid')) || 0; } catch {}
function rememberMenuConfirmation() {
  confirmedPid = $('menuConfirmed').checked ? navigation?.pid || 0 : 0;
  try { sessionStorage.setItem('lmu-menu-confirmed-pid',String(confirmedPid)); } catch {}
}

const SECTION_INFO = {
  selection:['YOUR NEXT SESSION','Car & track','Choose where you drive and what you drive.'],
  sessions:['BUILD YOUR WEEKEND','Sessions','Choose your session order, duration and track conditions.'],
  weather:['SET THE CONDITIONS','Weather','Shape the weather from the opening minutes to the end of each session.'],
  grid:['WHO YOU DRIVE WITH','Opponent grid','Choose the classes on your grid and how competitive they are.'],
  rules:['MAKE IT YOURS','Rules & assists','Set the simulation, rule enforcement and driving assistance.']
};
const LABELS = {
  AI_Aggression:'AI aggression', AI_Strength:'AI strength', Damage_Multi:'Damage multiplier', Finish_Criteria:'Race length mode', Fuel_Usage:'Fuel usage', Grid_Position:'Starting position', Mech_Failures:'Mechanical failures', Num_Opponents:'AI opponents', Practice_Length:'Practice duration', Qualify_Length:'Qualifying duration', Race_Laps:'Race laps', Tire_Wear:'Tyre wear', WarmUp_Length:'Warmup duration', adjust_frozen:'Frozen-order adjustment', blue_flags:'Blue flags', cut_rules:'Track limits', cuts_allowed:'Allowed cuts', flag_rules:'Flag Rules', force_formation:'Force formation', formation:'Race start', keep_tire_inv_on_track_change:'Keep tyre inventory between tracks', num_qual_sessions:'Qualifying sessions', num_race_sessions:'Race sessions', parc_ferme:'Parc fermé', pract1:'Practice 1', pract2:'Practice 2', pract3:'Practice 3', pract4:'Practice 4', pract1_realroad_init:'Starting grip', pract1_realroad_temperatures:'Track temperatures', pract1_realroad_wet:'Starting wetness', practice1_starting_time:'Start time', private_prac:'Private practice', private_qual:'Private qualifying', qual1_realroad_init:'Starting grip', qual1_realroad_temperatures:'Track temperatures', qual1_realroad_wet:'Starting wetness', qualify_starting_time:'Start time', race_realroad_init:'Starting grip', race_realroad_temperatures:'Track temperatures', race_realroad_wet:'Starting wetness', race_starting_time:'Start time', race_time:'Race duration', race_timer:'Race timer display interval', race_timescale:'Time acceleration', realroad_timescale_practice:'Grip evolution', realroad_timescale_qualify:'Grip evolution', realroad_timescale_race:'Grip evolution', recon_pit_closed:'Reconnaissance · pit closed', recon_pit_open:'Reconnaissance · pit open', recon_timer:'Reconnaissance timer', reconnaissance:'Reconnaissance laps', run_warmup:'Warmup', safetycar_thresh:'Safety car threshold', safetycarcollision:'Safety car collisions', timescaled_weather:'Scale weather to session length', tire_warmers:'Tyre warmers', tires_available_in_garage:'Available tyres', unsportsmanlike:'Unsportsmanlike sensitivity', walkthrough:'Grid walkthrough', warmup_starting_time:'Warmup start time', weather:'Weather mode',
  antilock_brakes:'Anti-lock brakes',auto_blip:'Automatic throttle blip',auto_clutch:'Automatic clutch',auto_headlights:'Automatic headlights',auto_lift:'Automatic throttle lift',auto_wipers:'Automatic wipers',automatic_pit_speed_limit:'Automatic pit speed limiter',autopit:'Automatic pit lane',brake_help:'Braking help',hold_brakes:'Hold brakes in the garage',hold_clutch:'Hold clutch at the start',invulnerable:'Invulnerability',opposite_lock:'Opposite lock assistance',repeat_shifts:'Accidental shift protection',shift_mode:'Automatic shifting',spin_recovery:'Spin recovery',stability_control:'Stability control',start_engine:'Automatic engine start',steering_help:'Steering help',throttle_control:'Traction control assistance',vis_fast_line:'Driving line',
  SKY:'Sky',RAIN_CHANCE:'Rain chance',TEMPERATURE:'Air temperature',HUMIDITY:'Humidity',WINDDIRECTION:'Wind direction',WINDSPEED:'Wind speed'
};
const NUMERIC = new Set(['SESSSET_AI_Strength','SESSSET_Damage_Multi','SESSSET_Num_Opponents','SESSSET_Practice_Length','SESSSET_Qualify_Length','SESSSET_WarmUp_Length','SESSSET_Race_Laps','SESSSET_race_time','SESSSET_cuts_allowed','SESSSET_tires_available_in_garage','SESSSET_recon_pit_closed','SESSSET_recon_pit_open']);
const NOTES = {
  SESSSET_AI_Strength:'LMU applies the available difficulty range.',
  SESSSET_Grid_Position:'Used when qualifying is disabled.',
  SESSSET_tire_warmers:'Availability depends on the loaded car.',
  SESSSET_flag_rules:'Includes pit-speeding penalties. The car’s pit limiter is separate.',
  SESSSET_Practice_Length:'Minutes · 0 uses the game’s default behaviour.',
  SESSSET_WarmUp_Length:'Minutes · 0 uses the track default.',
  DRIVEAIDS_throttle_control:'Game assistance, separate from the car’s onboard traction control.',
  DRIVEAIDS_antilock_brakes:'Game assistance, separate from the car’s onboard ABS.'
};
function label(key) {
  const short = key.replace(/^(SESSSET_|DRIVEAIDS_|WNV_)/,'');
  return LABELS[short] || short.replace(/_/g,' ').replace(/\b\w/g,c=>c.toUpperCase());
}
async function request(path, body) {
  const response = await fetch(path, body === undefined ? {} : {method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
  const result = await response.json();
  if (!response.ok || result.error) throw new Error(result.error || 'The app could not complete this request.');
  return result;
}
function reportError(message) {
  $('errorText').textContent = message;
  $('errorBar').hidden = false;
  $('saveDot').className = 'save-dot error';
  $('saveStatus').textContent = 'Change not confirmed';
}
function setSaved(message = 'Saved in LMU') {
  $('saveDot').className = 'save-dot';
  $('saveStatus').textContent = message;
}
function model(car) { return car.fullPathTree?.split(',').map(x=>x.trim()).join(' · ') || car.manufacturer || 'Other cars'; }
function currentCar() { return setup?.selection?.selectedCar; }
function currentTrack() { return setup?.selection?.trackInfo; }
function settingValue(key) { const s = setup?.settings?.[key]; return s ? s.stringValue || String(s.currentValue) : '—'; }
function keyData(body) { return escapeHtml(JSON.stringify(body)); }
function stepper(key, setting, action = 'setting', extra = {}) {
  const title = label(key);
  const assist = action === 'assist';
  const value = setting.currentValue;
  const numeric = action === 'setting' && NUMERIC.has(key);
  const unavailable = assist && setting.minValue === setting.maxValue;
  const minus = assist ? {value:Math.max(setting.minValue,value-setting.stepValue)} : {steps:numeric || action === 'weather' ? -1 : 0};
  const plus = assist ? {value:Math.min(setting.maxValue,value+setting.stepValue)} : {steps:1};
  const base = {action,key,...extra};
  const percentage = /^%/.test(setup.settings.SESSSET_Finish_Criteria?.stringValue || '') && /^(SESSSET_Race_Laps|SESSSET_race_time)$/.test(key);
  const unit = percentage ? '%' : /Length$|^SESSSET_race_time$/.test(key) ? 'min' : /recon_pit_(open|closed)$/.test(key) ? 'sec' : '';
  const center = numeric ? `<input id="value-${key}" type="number" value="${escapeHtml(value)}" data-setting="${escapeHtml(key)}" data-before="${value}" aria-label="${escapeHtml(title)}" step="1">${unit ? `<span class="unit">${unit}</span>` : ''}` : `<output>${escapeHtml(setting.stringValue || value)}</output>`;
  return `<div class="stepper"><button type="button" aria-label="Decrease ${escapeHtml(title)}" data-change="${keyData({...base,...minus})}" ${unavailable || assist && value<=setting.minValue ? 'disabled' : ''}>−</button>${center}<button type="button" aria-label="Increase ${escapeHtml(title)}" data-change="${keyData({...base,...plus})}" ${unavailable || assist && value>=setting.maxValue ? 'disabled' : ''}>+</button></div>`;
}
function row(key, setting, action = 'setting', extra = {}) {
  if (!setting) return '';
  const assist = action === 'assist';
  const unavailable = assist && setting.minValue === setting.maxValue;
  let note = unavailable ? 'LMU does not allow this option for the current configuration.' : NOTES[key];
  const realRoad = /realroad_(init|wet|temperatures)/.test(key);
  const mode = setup.settings.SESSSET_Finish_Criteria?.stringValue || '';
  const disabled = realRoad && setup.settings.SESSSET_weather?.currentValue !== 4 || key==='SESSSET_Race_Laps' && /^(Time|% Track Time)$/.test(mode) || key==='SESSSET_race_time' && /^(Laps|% Track Laps)$/.test(mode) || key==='SESSSET_Grid_Position' && setup.amounts.QUALIFY>0;
  if (disabled) note = realRoad ? 'Choose Scripted weather to adjust this setting.' : key==='SESSSET_Grid_Position' ? 'Qualifying determines your starting position.' : 'Choose a race length mode that uses this value.';
  const check = assist && setting.minValue===0 && setting.maxValue===1;
  const control = check ? `<input type="checkbox" aria-label="${escapeHtml(label(key))}" data-assist="${escapeHtml(key)}" ${setting.currentValue ? 'checked' : ''}>` : stepper(key,setting,action,extra);
  return `<div class="setting-row"><div class="setting-label">${escapeHtml(label(key))}${note ? `<small>${escapeHtml(note)}</small>` : ''}</div>${disabled ? `<fieldset disabled>${control}</fieldset>` : control}</div>`;
}
function settingsRows(keys) { return keys.filter(k=>section!=='rules' || !settingSearch || label(k).toLowerCase().includes(settingSearch.toLowerCase())).map(k=>row(k,setup.settings[k])).join(''); }
function block(title, keys, caption = '') {
  const rows = settingsRows(keys);
  return rows ? `<section class="section-block"><h2>${escapeHtml(title)}</h2>${caption ? `<p class="section-caption">${escapeHtml(caption)}</p>` : ''}<div class="settings-card">${rows}</div></section>` : '';
}
function option(value, text, selected, disabled = false) { return `<option value="${escapeHtml(value)}" ${selected ? 'selected' : ''} ${disabled ? 'disabled' : ''}>${escapeHtml(text)}</option>`; }
function renderSelection() {
  const car = currentCar(), track = currentTrack();
  const filtered = catalog.cars.filter(c=>c.isOwned && (!carSearch || `${c.desc} ${c.fullPathTree}`.toLowerCase().includes(carSearch.toLowerCase())));
  const models = [...new Set(filtered.map(model))].sort();
  if (car && !models.includes(model(car))) models.unshift(model(car));
  const selectedModel = model(car || {});
  const teams = catalog.cars.filter(c=>c.isOwned && model(c)===selectedModel).sort((a,b)=>a.desc.localeCompare(b.desc));
  const tracks = [...catalog.tracks].sort((a,b)=>(a.displayProperties?.shortName || a.shortName).localeCompare(b.displayProperties?.shortName || b.shortName));
  return `<section class="section-block"><h2>Circuit</h2><label class="input-label" for="trackSelect">Track & layout</label><select id="trackSelect">${tracks.map(t=>option(t.id,t.name,t.id===track?.id,!t.owned)).join('')}</select><div class="track-details">${track ? `<span class="pill">${escapeHtml(track.countryCode)}</span><span class="pill">${escapeHtml(track.trackLength)} km</span><span class="pill">${escapeHtml(track.type)}</span>` : ''}</div><p class="small-note">Your session settings are kept when you change tracks.</p></section>
  <section class="section-block"><h2>Your car</h2><div class="filter-row"><input type="search" id="carSearch" value="${escapeHtml(carSearch)}" placeholder="Search manufacturer, model or team" aria-label="Search cars"></div><div class="selection-fields"><div><label class="input-label" for="modelSelect">Season, class & model <small>· ${filtered.length} owned cars</small></label><select id="modelSelect">${models.map(m=>option(m,m,m===selectedModel)).join('')}</select></div><div><label class="input-label" for="carSelect">Team & livery</label><select id="carSelect">${teams.map(c=>option(c.id,c.desc,c.id===car?.id)).join('')}</select></div></div>${filtered.length===0 ? '<p class="small-note">No cars match that search. Your current car stays selected.</p>' : ''}</section>
  <section class="section-block"><h2>A starting point</h2><p class="section-caption">Prefer the circuit’s default weekend, grid and weather?</p><button class="button" data-dialog="defaultsDialog">Use track defaults</button></section>`;
}
const SESSION_NAMES = {PRACTICE:'Practice',QUALIFY:'Qualifying',WARMUP:'Warmup',RACE:'Race'};
const SESSION_KEYS = {
  PRACTICE:['SESSSET_Practice_Length','SESSSET_practice1_starting_time','SESSSET_private_prac','SESSSET_pract1_realroad_init','SESSSET_pract1_realroad_wet','SESSSET_pract1_realroad_temperatures','SESSSET_realroad_timescale_practice'],
  QUALIFY:['SESSSET_Qualify_Length','SESSSET_qualify_starting_time','SESSSET_private_qual','SESSSET_qual1_realroad_init','SESSSET_qual1_realroad_wet','SESSSET_qual1_realroad_temperatures','SESSSET_realroad_timescale_qualify'],
  WARMUP:['SESSSET_WarmUp_Length','SESSSET_warmup_starting_time'],
  RACE:['SESSSET_Finish_Criteria','SESSSET_race_time','SESSSET_Race_Laps','SESSSET_race_starting_time','SESSSET_formation','SESSSET_Grid_Position','SESSSET_race_realroad_init','SESSSET_race_realroad_wet','SESSSET_race_realroad_temperatures','SESSSET_realroad_timescale_race']
};
function renderSessions() {
  return Object.entries(SESSION_NAMES).map(([type,name])=>{
    const amount = setup.amounts[type];
    return `<section class="section-block"><div class="settings-card"><div class="card-title-row"><h3>${name}</h3><select aria-label="Number of ${name.toLowerCase()} sessions" data-amount="${type}">${Array.from({length:type==='WARMUP'?2:5},(_,i)=>option(i,i===0?'Disabled':i===1?'Enabled':`${i} sessions`,i===amount)).join('')}</select></div>${amount ? settingsRows(SESSION_KEYS[type]) : '<p class="disabled-note">Enable this session to set its duration and conditions.</p>'}${type==='PRACTICE' && amount>1 ? '<p class="session-note">Practice sessions share the duration and conditions shown here.</p>' : ''}</div></section>`;
  }).join('') + `<details class="subsection"><summary>Individual practice slots</summary><div class="settings-card">${settingsRows(['SESSSET_pract1','SESSSET_pract2','SESSSET_pract3','SESSSET_pract4'])}</div></details>`;
}
function renderWeather() {
  const names = {START:'Start',NODE_25:'25%',NODE_50:'50%',NODE_75:'75%',FINISH:'Finish'};
  const weather = setup.weather[weatherSession];
  return `<div class="segmented" aria-label="Weather session">${['PRACTICE','QUALIFY','RACE'].map(s=>`<button data-weather-session="${s}" class="${s===weatherSession?'active':''}" aria-pressed="${s===weatherSession}">${SESSION_NAMES[s]}</button>`).join('')}</div>
    ${block('Weather behaviour',['SESSSET_weather','SESSSET_timescaled_weather'])}
    <section class="section-block"><h2>Start with a forecast</h2><div class="preset-buttons">${[['SUNNY','Sunny'],['CLOUDY','Cloudy'],['RAINY','Rainy'],['DEFAULT','Track default']].map(([p,n])=>`<button class="button" data-change="${keyData({action:'weatherPreset',session:weatherSession,preset:p})}">${n}</button>`).join('')}</div>
    <h2>Weather timeline</h2><p class="section-caption">Select a point to edit its conditions. LMU transitions between these points.</p><div class="timeline">${Object.entries(names).map(([node,name])=>`<button data-weather-node="${node}" class="${node===weatherNode?'active':''}" aria-pressed="${node===weatherNode}">${name}<span>${escapeHtml(weather?.[node]?.WNV_SKY?.stringValue || '—')}</span></button>`).join('')}</div><div class="settings-card">${Object.entries(weather?.[weatherNode] || {}).map(([k,v])=>row(k,v,'weather',{session:weatherSession,node:weatherNode})).join('')}</div><p class="small-note">Editing a timeline value switches LMU to Scripted weather. Rain chance takes effect with a rain-capable sky.</p></section>`;
}
function renderGrid() {
  const classes = [...new Set(catalog.cars.map(c=>c.fullPathTree?.split(',')[1]?.trim()).map(c=>c==='Hypercar'?'Hyper':c).filter(Boolean))].sort();
  const selected = setup.selection.classesSelection || [];
  return block('Competition',['SESSSET_Num_Opponents','SESSSET_AI_Strength','SESSSET_AI_Aggression']) +
    `<section class="section-block"><h2>Grid composition</h2><div class="settings-card"><div class="setting-row"><span class="setting-label">Grid source</span><select id="gridSource">${option('season','Season grid',!setup.selection.fullGrid)}${option('fill','Fill grid',setup.selection.fullGrid)}</select></div></div><div class="class-grid">${classes.map(c=>`<label class="class-choice"><input type="checkbox" data-class="${escapeHtml(c)}" ${selected.includes(c)?'checked':''}>${escapeHtml(c==='Hyper'?'Hypercar':c.replace('_ELMS',' · ELMS'))}</label>`).join('')}</div><p class="small-note">Season grid uses LMU’s current season entries. Fill grid draws from the selected classes. Set AI opponents to 0 to drive alone.</p></section>`;
}
function renderRules() {
  const used = new Set([...Object.values(SESSION_KEYS).flat(),'SESSSET_pract1','SESSSET_pract2','SESSSET_pract3','SESSSET_pract4','SESSSET_num_qual_sessions','SESSSET_num_race_sessions','SESSSET_run_warmup','SESSSET_Num_Opponents','SESSSET_AI_Strength','SESSSET_AI_Aggression','SESSSET_weather','SESSSET_timescaled_weather']);
  const simulation = ['SESSSET_Damage_Multi','SESSSET_Mech_Failures','SESSSET_Fuel_Usage','SESSSET_Tire_Wear','SESSSET_tire_warmers','SESSSET_tires_available_in_garage','SESSSET_keep_tire_inv_on_track_change'];
  const rules = ['SESSSET_flag_rules','SESSSET_cut_rules','SESSSET_cuts_allowed','SESSSET_blue_flags','SESSSET_parc_ferme'];
  simulation.concat(rules).forEach(k=>used.add(k));
  const remaining = Object.keys(setup.settings).filter(k=>!used.has(k));
  const assists = Object.entries(setup.assists).filter(([k])=>!settingSearch || label(k).toLowerCase().includes(settingSearch.toLowerCase()));
  return `<input class="setting-search" type="search" id="settingSearch" value="${escapeHtml(settingSearch)}" placeholder="Find a rule or assist" aria-label="Search rules and assists">` +
    block('Simulation',simulation) + block('Rules',rules) +
    (assists.length ? `<section class="section-block"><h2>Driving assists</h2><div class="settings-card">${assists.map(([k,v])=>row(k,v,'assist')).join('')}</div></section>` : '') +
    (remaining.length ? `<details class="subsection" ${settingSearch?'open':''}><summary>Additional session controls</summary><div class="settings-card">${settingsRows(remaining)}</div></details>` : '');
}
function renderContent() {
  if (!setup || !catalog) return;
  const scroll = $('editor').scrollTop, focused = document.activeElement?.id;
  const selectionStart = document.activeElement?.selectionStart;
  const openDetails = [...$('sectionContent').querySelectorAll('details')].map(d=>d.open);
  $('sectionContent').innerHTML = ({selection:renderSelection,sessions:renderSessions,weather:renderWeather,grid:renderGrid,rules:renderRules})[section]();
  [...$('sectionContent').querySelectorAll('details')].forEach((d,i)=>{if(openDetails[i])d.open=true;});
  $('editor').scrollTop = scroll;
  if (focused && $(focused)) {
    $(focused).focus({preventScroll:true});
    if (selectionStart !== null && /search|text/.test($(focused).type)) $(focused).setSelectionRange(selectionStart,selectionStart);
  }
  $('emptyState').hidden = true;
  $('editorFields').hidden = false;
  updateControls();
}
function raceLengthSummary() {
  const mode=setup.settings.SESSSET_Finish_Criteria?.stringValue || 'Time';
  const time=escapeHtml(settingValue('SESSSET_race_time')), laps=escapeHtml(settingValue('SESSSET_Race_Laps'));
  if(mode==='% Track Time') return time+'% track time';
  if(mode==='% Track Laps') return laps+'% track laps';
  if(mode==='Time') return time+' min';
  if(mode==='Laps') return laps+' laps';
  return time+' min / '+laps+' laps';
}
function renderSummary() {
  if (!setup) return;
  const track = currentTrack(), car = currentCar();
  const parts = car?.fullPathTree?.split(',').map(p=>p.trim()) || [];
  $('selectionSummary').innerHTML = `<h3>${escapeHtml(track?.trackName || 'Choose a track')}</h3><p>${escapeHtml(track?.type || '')}</p><p class="summary-car">${escapeHtml(parts[2] || car?.manufacturer || 'Choose a car')}</p><p>${escapeHtml(car?.desc || '')}</p>`;
  const lengths = {PRACTICE:'SESSSET_Practice_Length',QUALIFY:'SESSSET_Qualify_Length',WARMUP:'SESSSET_WarmUp_Length',RACE:'SESSSET_race_time'};
  const enabled = Object.entries(SESSION_NAMES).filter(([type])=>setup.amounts[type]>0);
  $('sessionSummary').innerHTML = enabled.length ? enabled.map(([type,name])=>`<div class="summary-session">${name}${setup.amounts[type]>1?' × '+setup.amounts[type]:''}<span>${type==='RACE' ? raceLengthSummary() : escapeHtml(setup.settings[lengths[type]]?.currentValue ?? '—')+' min'}</span></div>`).join('') : '<p class="muted">No sessions enabled</p>';
  $('summaryMeta').innerHTML = `<div>${escapeHtml(setup.settings.SESSSET_Num_Opponents?.currentValue || 0)} AI opponents · ${escapeHtml(settingValue('SESSSET_AI_Strength'))} strength</div><div>${escapeHtml(settingValue('SESSSET_weather'))} weather</div>`;
}
function updateControls() {
  $('editorFields').disabled = saving || starting || !menuReady || serverBusy;
  const anySession = setup && Object.values(setup.amounts).some(n=>n>0);
  $('startButton').disabled = !setup || !menuReady || saving || starting || serverBusy || !anySession || !$('menuConfirmed').checked;
  $('startButton').innerHTML = starting ? 'Loading session…' : 'Start session <span aria-hidden="true">↗</span>';
  $('menuConfirmed').disabled = starting || !menuReady;
  $('reloadButton').disabled = saving || starting;
  $('launchButton').disabled = gameRunning || saving || starting;
  $('emptyLaunch').disabled = gameRunning || saving || starting;
  $('launchHint').textContent = starting ? 'Loading progress appears below. You can switch to LMU.' : !gameRunning ? 'Launch local play to begin.' : !menuReady ? 'Return to the main menu to start a new session.' : !anySession ? 'Enable at least one session in Sessions.' : !$('menuConfirmed').checked ? 'Dismiss the startup prompt in LMU, then confirm above.' : 'Loads this setup in LMU. Press Drive there when ready.';
}
function renderNavigation() {
  const state = navigation?.state || {};
  menuReady = !navigation?.protected && state.settingMode==='SETTING_GRANDPRIX' && state.gameState==='GSTATE_SETUP' && state.navigationState==='NAV_MAIN_MENU' && !navigation.loadingStatus?.loading;
  $('connection').textContent = !gameRunning ? 'LMU not running' : navigation?.loadingStatus?.loading ? 'Loading session' : menuReady ? 'Local play · main menu' : state.settingMode==='SETTING_MULTIPLAYER' ? 'Online session' : `${state.gameSession?.replace(/(\D)(\d)/,'$1 $2').toLowerCase() || 'Local session'} · loaded`;
  $('connection').className = 'connection' + (menuReady?' ready':'');
  const banner = $('stateBanner');
  banner.hidden = !gameRunning || menuReady && !serverBusy || !setup;
  if (!banner.hidden) banner.innerHTML = serverBusy ? 'A practice-tool operation is in progress. Wait for it to finish before changing session setup.' : state.settingMode==='SETTING_MULTIPLAYER' ? 'Session setup is available in local single-player mode. Close LMU and launch local play.' : navigation.protected ? 'This LMU launch uses EAC. Close the game and launch local play to edit sessions.' : navigation.loadingStatus?.loading || starting ? 'LMU is loading. Setup will be available again at the main menu.' : 'A session is loaded in LMU. Return to the main menu to edit your next session.<button class="button" data-dialog="menuDialog">Return to menu</button>';
  updateControls();
}
async function loadSetup() {
  try {
    const next = await request('/api/session/setup');
    if (!catalog) catalog = await request('/api/session/catalog');
    setup = next;
    renderContent(); renderSummary(); setSaved('In sync with LMU');
  } catch(e) {
    if (setup) reportError(e.message);
  }
}
async function change(body) {
  if (saving || starting || !menuReady || serverBusy) return;
  saving = true; updateControls();
  $('saveDot').className = 'save-dot saving'; $('saveStatus').textContent = 'Saving to LMU…';
  try {
    setup = await request('/api/session/action',body);
    renderContent(); renderSummary(); setSaved(); $('errorBar').hidden = true;
  } catch(e) {
    // A write may have succeeded before its readback failed. Refresh before showing values.
    try { setup=await request('/api/session/setup'); } catch {}
    renderContent(); renderSummary(); reportError(e.message);
  }
  finally { saving=false; updateControls(); }
}
async function launchLocal() {
  if (gameRunning) return;
  $('launchButton').disabled = $('emptyLaunch').disabled = true;
  try {
    await request('/api/launch',{mode:'direct'});
    gameRunning=true; $('saveStatus').textContent='Waiting for LMU’s menu…';
    $('emptyState').querySelector('p').textContent='LMU is starting. Dismiss its “Press any button” prompt, then return here to configure your session.';
    updateControls();
  } catch(e) { reportError(e.message); updateControls(); }
}
async function poll() {
  if (polling) return;
  polling=true;
  try {
    navigation = await request('/api/session/state');
    if (navigation.pid) $('menuConfirmed').checked = confirmedPid === navigation.pid;
    serverBusy=!!navigation.operationBusy && !saving && !starting;
    gameRunning=true;
    if (!setup && !saving && !starting) await loadSetup();
    if (starting) {
      const progress = navigation.loadingStatus;
      if(progress?.loading && progress.percentage>=0) $('loadProgressBar').value=Math.round(progress.percentage*100);
      else $('loadProgressBar').removeAttribute('value');
      $('loadProgressText').textContent=progress?.loading && progress.percentage>=0 ? `Loading · ${Math.round(progress.percentage*100)}%` : 'Preparing session…';
    }
    renderNavigation();
  } catch(e) {
    navigation=null; menuReady=false;
    if(!starting && !saving) {
      try {
        const state=await request('/api/state');
        gameRunning=!!state.game?.running;
        serverBusy=!!state.busy;
        $('connection').textContent=state.game?.protected ? 'EAC launch · setup unavailable' : gameRunning ? 'Waiting for LMU’s API' : 'LMU not running';
      } catch { $('connection').textContent='App connection lost'; }
      if(!gameRunning) {$('menuConfirmed').checked=false;rememberMenuConfirmation();setup=null;catalog=null;$('editorFields').hidden=true;$('emptyState').hidden=false;}
    }
    updateControls();
  } finally { polling=false; }
}
async function startSession() {
  if ($('startButton').disabled) return;
  starting=true; $('stopMonitoring').hidden=false; updateControls(); $('loadProgress').hidden=false; $('loadProgressBar').removeAttribute('value'); $('loadProgressText').textContent='Preparing session…';
  try {
    await request('/api/session/action',{action:'start',menuConfirmed:$('menuConfirmed').checked});
    $('loadProgressBar').value=100; $('loadProgressText').textContent='Session loaded. Switch to LMU and press Drive.'; $('stopMonitoring').hidden=true;
    setSaved('Session loaded');
  } catch(e) { reportError(e.message); $('loadProgressText').textContent='Check LMU before trying again.'; }
  finally {starting=false;await poll();updateControls();}
}
function switchSection(next) {
  section=next;
  const [eyebrow,title,description]=SECTION_INFO[next];
  $('sectionEyebrow').textContent=eyebrow;$('sectionTitle').textContent=title;$('sectionDescription').textContent=description;
  for(const button of $('sectionNav').querySelectorAll('button')) {const active=button.dataset.section===next;button.classList.toggle('active',active);if(active)button.setAttribute('aria-current','true');else button.removeAttribute('aria-current');}
  renderContent();$('editor').scrollTop=0;
}
document.addEventListener('click',e=>{
  const button=e.target.closest('button');if(!button || button.disabled)return;
  if(button.dataset.section)switchSection(button.dataset.section);
  if(button.dataset.change)change(JSON.parse(button.dataset.change));
  if(button.dataset.weatherSession){weatherSession=button.dataset.weatherSession;renderContent();}
  if(button.dataset.weatherNode){weatherNode=button.dataset.weatherNode;renderContent();}
  if(button.dataset.dialog)$(button.dataset.dialog).showModal();
});
document.addEventListener('change',e=>{
  const el=e.target;
  if(el.dataset.setting){const value=Number(el.value),before=Number(el.dataset.before);if(!el.value.trim() || !Number.isInteger(value) || Math.abs(value-before)>10000){reportError('Enter a whole number within the available setting range.');el.value=before;return;}const delta=value-before;if(delta)change({action:'setting',key:el.dataset.setting,steps:delta});}
  if(el.dataset.amount)change({action:'amount',session:el.dataset.amount,amount:Number(el.value)});
  if(el.dataset.assist)change({action:'assist',key:el.dataset.assist,value:el.checked?1:0});
  if(el.id==='trackSelect')change({action:'track',id:el.value});
  if(el.id==='carSelect')change({action:'car',id:el.value});
  if(el.id==='modelSelect'){const car=catalog.cars.find(c=>c.isOwned && model(c)===el.value);if(car)change({action:'car',id:car.id});}
  if(el.dataset.class || el.id==='gridSource')change({action:'grid',classes:[...$('sectionContent').querySelectorAll('[data-class]:checked')].map(c=>c.dataset.class),fullGrid:$('gridSource').value==='fill'});
  if(el.id==='menuConfirmed'){rememberMenuConfirmation();updateControls();}
});
document.addEventListener('input',e=>{
  if(e.target.id==='carSearch'){carSearch=e.target.value;renderContent();}
  if(e.target.id==='settingSearch'){settingSearch=e.target.value;renderContent();}
});
$('launchButton').addEventListener('click',launchLocal);$('emptyLaunch').addEventListener('click',launchLocal);
$('reloadButton').addEventListener('click',async()=>{await loadSetup();await poll();});
$('startButton').addEventListener('click',startSession);
$('dismissError').addEventListener('click',()=>{$('errorBar').hidden=true;});
$('keepSession').addEventListener('click',()=>$('menuDialog').close());
$('keepSettings').addEventListener('click',()=>$('defaultsDialog').close());
$('applyDefaults').addEventListener('click',()=>{$('defaultsDialog').close();change({action:'trackDefaults'});});
$('returnMenu').addEventListener('click',async()=>{
  $('menuDialog').close();if(saving || starting)return;saving=true;updateControls();
  try{setup=await request('/api/session/action',{action:'menu'});$('menuConfirmed').checked=false;rememberMenuConfirmation();renderContent();renderSummary();setSaved('In sync with LMU');await poll();}catch(e){reportError(e.message);}finally{saving=false;updateControls();}
});
$('stopMonitoring').addEventListener('click',async()=>{try{await request('/api/cancel',{});}catch(e){reportError(e.message);}});
poll();pollTimer=setInterval(poll,1500);
