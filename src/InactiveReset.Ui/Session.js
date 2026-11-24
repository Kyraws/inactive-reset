'use strict';
const $ = id => document.getElementById(id);
const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
let setup = null, catalog = null, navigation = null;
let section = 'selection', weatherSession = 'PRACTICE', weatherNode = 'START';
let saving = false, starting = false, menuReady = false, gameRunning = false, serverBusy = false;
let savingOptions = false;
const sessionOptions = [];
let gameProtected = false, launching = false;
let carSearch = '', settingSearch = '', pollTimer, polling = false;

const SECTION_INFO = {
  selection:['YOUR NEXT SESSION','Car & track','Choose where you drive and what you drive.'],
  sessions:['BUILD YOUR WEEKEND','Sessions','Type durations and start times directly; press Enter to save. The +/− buttons update immediately and save after a short pause.'],
  weather:['SET THE CONDITIONS','Weather','Shape the weather from the opening minutes to the end of each session.'],
  grid:['WHO YOU DRIVE WITH','Opponent grid','Choose the classes on your grid and how competitive they are.'],
  rules:['MAKE IT YOURS','Rules','Simulation settings and session rules.']
};
const LABELS = {
  AI_Aggression:'AI aggression', AI_Strength:'AI strength', Damage_Multi:'Damage multiplier', Finish_Criteria:'Race length mode', Fuel_Usage:'Fuel usage', Grid_Position:'Starting position', Mech_Failures:'Mechanical failures', Num_Opponents:'AI opponents', Practice_Length:'Practice duration', Qualify_Length:'Qualifying duration', Race_Laps:'Race laps', Tire_Wear:'Tyre wear', WarmUp_Length:'Warmup duration', adjust_frozen:'Frozen-order adjustment', blue_flags:'Blue flags', cut_rules:'Track limits', cuts_allowed:'Allowed cuts', flag_rules:'Flag Rules', force_formation:'Force formation', formation:'Race start', keep_tire_inv_on_track_change:'Keep tyre inventory between tracks', num_qual_sessions:'Qualifying sessions', num_race_sessions:'Race sessions', parc_ferme:'Parc fermé', pract1:'Practice 1', pract2:'Practice 2', pract3:'Practice 3', pract4:'Practice 4', pract1_realroad_init:'Starting grip', pract1_realroad_temperatures:'Track temperatures', pract1_realroad_wet:'Starting wetness', practice1_starting_time:'Start time', private_prac:'Private practice', private_qual:'Private qualifying', qual1_realroad_init:'Starting grip', qual1_realroad_temperatures:'Track temperatures', qual1_realroad_wet:'Starting wetness', qualify_starting_time:'Start time', race_realroad_init:'Starting grip', race_realroad_temperatures:'Track temperatures', race_realroad_wet:'Starting wetness', race_starting_time:'Start time', race_time:'Race duration', race_timer:'Race timer display interval', race_timescale:'Time acceleration', realroad_timescale_practice:'Grip evolution', realroad_timescale_qualify:'Grip evolution', realroad_timescale_race:'Grip evolution', recon_pit_closed:'Reconnaissance · pit closed', recon_pit_open:'Reconnaissance · pit open', recon_timer:'Reconnaissance timer', reconnaissance:'Reconnaissance laps', run_warmup:'Warmup', safetycar_thresh:'Safety car threshold', safetycarcollision:'Safety car collisions', timescaled_weather:'Scale weather to session length', tire_warmers:'Tyre warmers', tires_available_in_garage:'Available tyres', unsportsmanlike:'Unsportsmanlike sensitivity', walkthrough:'Grid walkthrough', warmup_starting_time:'Warmup start time', weather:'Weather mode',
  antilock_brakes:'Anti-lock brakes',auto_blip:'Automatic throttle blip',auto_clutch:'Automatic clutch',auto_headlights:'Automatic headlights',auto_lift:'Automatic throttle lift',auto_wipers:'Automatic wipers',automatic_pit_speed_limit:'Automatic pit speed limiter',autopit:'Automatic pit lane',brake_help:'Braking help',hold_brakes:'Hold brakes in the garage',hold_clutch:'Hold clutch at the start',invulnerable:'Invulnerability',opposite_lock:'Opposite lock assistance',repeat_shifts:'Accidental shift protection',shift_mode:'Automatic shifting',spin_recovery:'Spin recovery',stability_control:'Stability control',start_engine:'Automatic engine start',steering_help:'Steering help',throttle_control:'Traction control assistance',vis_fast_line:'Driving line',
  SKY:'Sky',RAIN_CHANCE:'Rain chance',TEMPERATURE:'Air temperature',HUMIDITY:'Humidity',WINDDIRECTION:'Wind direction',WINDSPEED:'Wind speed'
};
const NUMERIC = new Set(['SESSSET_AI_Strength','SESSSET_Damage_Multi','SESSSET_Num_Opponents','SESSSET_Practice_Length','SESSSET_Qualify_Length','SESSSET_WarmUp_Length','SESSSET_Race_Laps','SESSSET_race_time','SESSSET_cuts_allowed','SESSSET_tires_available_in_garage','SESSSET_recon_pit_closed','SESSSET_recon_pit_open']);
const SETTING_CHOICES = {
  WNV_SKY:['Clear','Light clouds','Partially cloudy','Mostly cloudy','Overcast','Cloudy with drizzle','Cloudy with light rain','Overcast with light rain','Overcast with rain','Overcast with heavy rain','Overcast with storms'],
  WNV_WINDDIRECTION:['North','North East','East','South East','South','South West','West','North West'],
  SESSSET_AI_Aggression:['Low','Medium','High'],
  SESSSET_weather:['Track default','Sunny','Cloudy','Rainy','Scripted'],
  SESSSET_formation:['Full formation lap','Fast rolling'],
  SESSSET_private_prac:['Off','On'],
  SESSSET_private_qual:['Off','On','Track default'],
  SESSSET_Mech_Failures:['Off','Normal','Time scaled'],
  SESSSET_flag_rules:['Off','Penalties only','Full','No disqualifications'],
  SESSSET_blue_flags:['Off','Show without penalties'],
  SESSSET_cut_rules:['Off','Default','Strict','Relaxed'],
  SESSSET_parc_ferme:['Off','On','Except in rain','Default'],
  SESSSET_Fuel_Usage:['Off','Realistic','2×','3×','4×','5×','6×','7×'],
  SESSSET_Tire_Wear:['Off','Realistic','2×','3×','4×','5×','6×','7×'],
  SESSSET_Finish_Criteria:['Time and laps','Laps','Time','% Time and laps','% Track Laps','% Track Time'],
  SESSSET_timescaled_weather:['Off','On'],
  SESSSET_tire_warmers:['Off','On'],
  SESSSET_keep_tire_inv_on_track_change:['Off','On'],
  SESSSET_safetycarcollision:['Off','On'],
  SESSSET_recon_timer:['Off','On']
};
function settingChoice(key,value){
  return value==null?'':SETTING_CHOICES[key]?.[Number(value)]||'';
}
function settingUnit(key){
  if(['SESSSET_AI_Strength','SESSSET_Damage_Multi','WNV_RAIN_CHANCE','WNV_HUMIDITY'].includes(key))return '%';
  if(key==='WNV_TEMPERATURE')return '°C';
  if(key==='WNV_WINDSPEED')return 'm/s';
  if(/Length$|^SESSSET_race_time$/.test(key))return 'min';
  if(/recon_pit_(open|closed)$|race_timer$/.test(key))return 's';
  return '';
}
function settingDisplay(key,value){
  if(value==null)return '—';
  return settingChoice(key,value)||(
    SETTING_CHOICES[key]?'Unknown option':key==='SESSSET_race_timescale'&&value===-1?'Race-distance percentage':
    /timescale/.test(key)?(Number(value)===-1?'Scaled with session length':Number(value)===0?'Static':Number(value)===1?'Normal':String(value)+'×'):
    key==='SESSSET_Grid_Position'&&Number(value)===0?'Random':String(value)+(settingUnit(key)?' '+settingUnit(key):''));
}
const TIME_SETTINGS = new Set(['SESSSET_practice1_starting_time','SESSSET_qualify_starting_time','SESSSET_race_starting_time','SESSSET_warmup_starting_time']);
const TIME_MODES = {'-3':'Random','-2':'Random daytime','-1':'Track default'};
const sessionValueEdits = new Map();
let sessionValueTimer, sessionValueSaving=false, sessionValueError='', sessionValueEditedAt=0, sessionValueRevision=0;
function clockValue(value){return value<0?'':String(Math.floor(value/60)).padStart(2,'0')+':'+String(value%60).padStart(2,'0');}
const NOTES = {
  SESSSET_private_prac:'Hides opponents from view during Practice.',
  SESSSET_private_qual:'Hides opponents from view during Qualifying.',
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
function currentCar() { return setup?.selection?.selectedCar; }
let vehicleModels = new Map();
function carModel(car) {
  return vehicleModels.get(car?.id?.toLowerCase()) ||
    car?.fullPathTree?.split(',')[2]?.trim().replace(/\bLMGT3\b/g,'GT3') || 'Unknown car';
}
function savedCarModel(id) {
  return vehicleModels.get(id?.toLowerCase()) || carModel(catalog?.cars?.find(car=>car.id===id));
}
function currentTrack() { return setup?.selection?.trackInfo; }
function settingValue(key) { const s = setup?.settings?.[key]; return s ? s.stringValue || String(s.currentValue) : '—'; }
function keyData(body) { return escapeHtml(JSON.stringify(body)); }
function stepper(key, setting, action = 'setting', extra = {}) {
  const title = label(key);
  const assist = action === 'assist';
  const value = action==='setting' && sessionValueEdits.has(key) ? sessionValueEdits.get(key) : setting.currentValue;
  const numeric = action === 'setting' && NUMERIC.has(key);
  const clock = action === 'setting' && TIME_SETTINGS.has(key);
  const edit = numeric || clock;
  const unavailable = assist && setting.minValue === setting.maxValue;
  const minus = assist ? {value:Math.max(setting.minValue,value-setting.stepValue)} : {steps:numeric || action === 'weather' ? -1 : 0};
  const plus = assist ? {value:Math.min(setting.maxValue,value+setting.stepValue)} : {steps:1};
  const base = {action,key,...extra};
  const unit = settingUnit(key);
  const editKey=edit?` data-session-value="${escapeHtml(key)}"`:'';
  const clockNote=TIME_MODES[value]||'';
  const center = clock ? `<input id="value-${key}" type="time" value="${clockValue(value)}" data-session-value="${escapeHtml(key)}" aria-label="${escapeHtml(title)}" step="60"><span class="clock-note" data-clock-note="${escapeHtml(key)}">${clockNote}</span><button type="button" class="clock-default" data-session-value="${escapeHtml(key)}" data-direct-value="-1" aria-label="Use track default ${escapeHtml(title)}">Default</button><div class="clock-modes"><button type="button" data-session-value="${escapeHtml(key)}" data-direct-value="-2" aria-pressed="${value===-2}">Random daytime</button><button type="button" data-session-value="${escapeHtml(key)}" data-direct-value="-3" aria-pressed="${value===-3}">Random</button></div>` : numeric ? `<input id="value-${key}" type="number" value="${escapeHtml(value)}" data-setting="${escapeHtml(key)}" data-session-value="${escapeHtml(key)}" data-before="${value}" aria-label="${escapeHtml(title)}" step="1">${unit ? `<span class="unit">${unit}</span>` : ''}` : `<output>${escapeHtml(setting.stringValue || value)}</output>`;
  return `<div class="stepper${clock?' clock-stepper':''}"><button type="button" aria-label="Decrease ${escapeHtml(title)}" data-change="${keyData({...base,...minus})}"${editKey}${edit?' data-value-step="-1"':''} ${unavailable || assist && value<=setting.minValue ? 'disabled' : ''}>−</button>${center}<button type="button" aria-label="Increase ${escapeHtml(title)}" data-change="${keyData({...base,...plus})}"${editKey}${edit?' data-value-step="1"':''} ${unavailable || assist && value>=setting.maxValue ? 'disabled' : ''}>+</button></div>`;
}

function canEditSessionDraft(){
  return !!setup && (menuReady || typeof editBuilder==='function' && !!catalog &&
    gameRunning && !!navigation?.state && !navigation.loadingStatus?.loading);
}
function updateSessionValueControls(){
  for(const el of document.querySelectorAll('[data-session-value]'))el.disabled=!(menuReady||canEditSessionDraft())||starting||serverBusy||(saving&&!sessionValueSaving&&!savingOptions)||!!el.closest('fieldset[disabled]');
  const pending=sessionValueEdits.size;
  $('sessionSaveNotice').hidden=!pending;
  $('sessionSaveText').textContent=sessionValueError?'Changes are not confirmed. '+sessionValueError:sessionValueSaving?'Saving your changes. You can keep adjusting numbers and times.':'Changes will save after you pause. You can keep editing.';
  $('retrySessionSave').hidden=!sessionValueError;
  $('retrySessionSave').disabled=!!saving||!menuReady||starting||serverBusy;$('discardSessionEdits').disabled=!!saving;
  if(pending){$('saveDot').className='save-dot '+(sessionValueError?'error':'saving');$('saveStatus').textContent=sessionValueError?'Changes not confirmed':sessionValueSaving?'Saving changes…':'Changes waiting to save…';}
}
function paintSessionValue(key,value){
  const input=$('value-'+key);if(input)input.value=TIME_SETTINGS.has(key)?clockValue(value):String(value);
  const note=document.querySelector('[data-clock-note="'+key+'"]');if(note)note.textContent=TIME_MODES[value]||'';
}
function scheduleSessionValues(){
  clearTimeout(sessionValueTimer);
  if(sessionValueEdits.size&&!sessionValueError)sessionValueTimer=setTimeout(flushSessionValues,Math.max(0,400-(Date.now()-sessionValueEditedAt)));
}
function queueSessionValue(key,value){
  if(!menuReady||starting||serverBusy||(saving&&!sessionValueSaving&&!savingOptions))return;
  if(!(NUMERIC.has(key)||TIME_SETTINGS.has(key))||!Number.isInteger(value)||value<(TIME_SETTINGS.has(key)?-3:0)||value>(TIME_SETTINGS.has(key)?1439:10000)){reportError('Enter a valid whole number, or choose a start time between 00:00 and 23:59.');return;}
  const hadError=!!sessionValueError;sessionValueError='';
  sessionValueRevision++;
  if(!sessionValueSaving&&!hadError&&value===setup.settings[key]?.currentValue)sessionValueEdits.delete(key);else sessionValueEdits.set(key,value);
  sessionValueEditedAt=Date.now();paintSessionValue(key,value);scheduleSessionValues();updateControls();
  if(!sessionValueEdits.size)setSaved('Values confirmed in LMU');
}
function renderAfterSessionValues(preserveInput=true){
  const focused=document.activeElement,key=focused?.dataset.sessionValue,id=focused?.id,raw=preserveInput&&focused?.tagName==='INPUT'?focused.value:null;
  if(preserveInput&&key){
    // Keep the live editor node, its caret and keyboard focus while replies arrive.
    for(const input of document.querySelectorAll('input[data-session-value]'))if(input.dataset.sessionValue!==key){const other=input.dataset.sessionValue;paintSessionValue(other,sessionValueEdits.get(other)??setup.settings[other]?.currentValue);}
    renderSummary();updateControls();return;
  }
  renderContent();renderSummary();updateControls();
  const input=key?$('value-'+key):id?$(id):null;if(input){if(raw!==null)input.value=raw;input.focus({preventScroll:true});}
}
async function flushSessionValues(){
  clearTimeout(sessionValueTimer);
  if(sessionValueSaving||saving||!sessionValueEdits.size||sessionValueError)return;
  if(!menuReady||starting||serverBusy){sessionValueError='Return to the single-player main menu, then retry or discard these edits.';updateControls();return;}
  const [key,value]=sessionValueEdits.entries().next().value;
  const before=setup.settings[key]?.currentValue;
  sessionValueSaving=true;saving=true;updateControls();
  try{
    setup=await request('/api/session/action',{action:'sessionValue',key,value});
    if(setup.settings[key]?.currentValue!==value)throw Error('LMU did not confirm the requested value.');
    if(sessionValueEdits.get(key)===value)sessionValueEdits.delete(key);
    if(before!==value&&typeof lastUndo!=='undefined'){lastUndo={type:'setting',key,value:before};$('lastChange').textContent=label(key)+' updated';}
  }catch(e){sessionValueError=e.message;try{setup=await request('/api/session/setup');}catch{}}
  finally{
    sessionValueSaving=false;saving=false;renderAfterSessionValues();
    if(!sessionValueEdits.size)setSaved('Values confirmed in LMU');else scheduleSessionValues();
  }
}
async function discardSessionValues(){
  if(saving)return;clearTimeout(sessionValueTimer);saving=true;updateControls();
  try{setup=await request('/api/session/setup');sessionValueEdits.clear();sessionValueError='';setSaved('Showing confirmed LMU values');}
  catch(e){sessionValueError='Could not read LMU. Your pending edits have been kept. '+e.message;}
  finally{saving=false;renderAfterSessionValues(false);}
}
function row(key, setting, action = 'setting', extra = {}) {
  if (!setting) return '';
  const assist = action === 'assist';
  const unavailable = assist && setting.minValue === setting.maxValue;
  let note = unavailable ? 'LMU does not allow this option for the current configuration.' : NOTES[key];
  const realRoad = /realroad_(init|wet|temperatures)/.test(key);
  const disabled = realRoad && setup.settings.SESSSET_weather?.currentValue !== 4 || key==='SESSSET_Grid_Position' && setup.amounts.QUALIFY>0;
  if (disabled) note = realRoad ? 'Choose Scripted weather to adjust this setting.' : 'Qualifying determines your starting position.';
  const check = assist && setting.minValue===0 && setting.maxValue===1;
  const control = check ? `<input type="checkbox" aria-label="${escapeHtml(label(key))}" data-assist="${escapeHtml(key)}" ${setting.currentValue ? 'checked' : ''}>` :
    !assist&&typeof builderSettingControl==='function'?builderSettingControl(key,setting,action,extra):stepper(key,setting,action,extra);
  return `<div class="setting-row"><div class="setting-label">${escapeHtml(label(key))}${note ? `<small>${escapeHtml(note)}</small>` : ''}</div>${disabled ? `<fieldset disabled>${control}</fieldset>` : control}</div>`;
}
function settingsRows(keys) { return keys.filter(k=>section!=='rules' || !settingSearch || label(k).toLowerCase().includes(settingSearch.toLowerCase())).map(k=>row(k,setup.settings[k])).join(''); }
function block(title, keys, caption = '') {
  const rows = settingsRows(keys);
  return rows ? `<section class="section-block"><h2>${escapeHtml(title)}</h2>${caption ? `<p class="section-caption">${escapeHtml(caption)}</p>` : ''}<div class="settings-card">${rows}</div></section>` : '';
}
function option(value, text, selected, disabled = false) { return `<option value="${escapeHtml(value)}" ${selected ? 'selected' : ''} ${disabled ? 'disabled' : ''}>${escapeHtml(text)}</option>`; }
const SESSION_NAMES = {PRACTICE:'Practice',QUALIFY:'Qualifying',WARMUP:'Warmup',RACE:'Race'};
const RACE_MODE = 'SESSSET_Finish_Criteria', RACE_MINUTES = 'SESSSET_race_time', RACE_LAPS = 'SESSSET_Race_Laps';
const TIMED_RACE = 2, DEFAULT_RACE_MINUTES = 20;
const SESSION_KEYS = {
  PRACTICE:['SESSSET_Practice_Length','SESSSET_practice1_starting_time','SESSSET_private_prac','SESSSET_pract1_realroad_init','SESSSET_pract1_realroad_wet','SESSSET_pract1_realroad_temperatures','SESSSET_realroad_timescale_practice'],
  QUALIFY:['SESSSET_Qualify_Length','SESSSET_qualify_starting_time','SESSSET_private_qual','SESSSET_qual1_realroad_init','SESSSET_qual1_realroad_wet','SESSSET_qual1_realroad_temperatures','SESSSET_realroad_timescale_qualify'],
  WARMUP:['SESSSET_WarmUp_Length','SESSSET_warmup_starting_time'],
  RACE:[RACE_MINUTES,'SESSSET_race_starting_time','SESSSET_formation','SESSSET_Grid_Position','SESSSET_race_realroad_init','SESSSET_race_realroad_wet','SESSSET_race_realroad_temperatures','SESSSET_realroad_timescale_race']
};
function sessionToggle(type, name) {
  const pending=sessionOptions.findLast(edit=>edit.body.action==='amount'&&edit.body.session===type);
  const enabled=(pending?.body.amount??setup.amounts[type])>0;
  return `<label class="session-toggle"><input type="checkbox" data-amount="${type}" aria-label="Enable ${name}" ${enabled?'checked':''}>Enabled</label>`;
}
function renderSessions() {
  return Object.entries(SESSION_NAMES).map(([type,name])=>{
    const amount = setup.amounts[type];
    return `<section class="section-block"><div class="settings-card"><div class="card-title-row"><h3>${name}</h3>${sessionToggle(type,name)}</div>${amount ? settingsRows(SESSION_KEYS[type]) : '<p class="disabled-note">Enable this session to set its duration and conditions.</p>'}</div></section>`;
  }).join('');
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
    `<section class="section-block"><h2>Grid composition</h2><div class="settings-card"><div class="setting-row"><span class="setting-label">Grid source</span><select id="gridSource">${option('season','Season grid',!setup.selection.fullGrid)}${option('fill','Fill grid',setup.selection.fullGrid)}</select></div></div><div class="class-grid">${classes.map(c=>`<label class="class-choice"><input type="checkbox" data-class="${escapeHtml(c)}" ${selected.includes(c)?'checked':''}>${typeof vehicleClassBadge==='function'?vehicleClassBadge(c):escapeHtml(c)}</label>`).join('')}</div><p class="small-note">Season grid uses LMU’s current season entries. Fill grid draws from the selected classes. Set AI opponents to 0 to drive alone.</p></section>`;
}
function renderRules() {
  const used = new Set([...Object.values(SESSION_KEYS).flat(),RACE_MODE,RACE_LAPS,'SESSSET_pract1','SESSSET_pract2','SESSSET_pract3','SESSSET_pract4','SESSSET_num_qual_sessions','SESSSET_num_race_sessions','SESSSET_run_warmup','SESSSET_Num_Opponents','SESSSET_AI_Strength','SESSSET_AI_Aggression','SESSSET_weather','SESSSET_timescaled_weather']);
  const simulation = ['SESSSET_Damage_Multi','SESSSET_Mech_Failures','SESSSET_Fuel_Usage','SESSSET_Tire_Wear','SESSSET_tire_warmers','SESSSET_tires_available_in_garage','SESSSET_keep_tire_inv_on_track_change'];
  const rules = ['SESSSET_flag_rules','SESSSET_cut_rules','SESSSET_cuts_allowed','SESSSET_blue_flags','SESSSET_parc_ferme'];
  simulation.concat(rules).forEach(k=>used.add(k));
  const remaining = Object.keys(setup.settings).filter(k=>!used.has(k));
  return `<input class="setting-search" type="search" id="settingSearch" value="${escapeHtml(settingSearch)}" placeholder="Find a rule" aria-label="Search rules">` +
    block('Simulation',simulation) + block('Rules',rules) +
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
function raceLengthSummary(settings, display) {
  const value=key=>settings ? settings[key] : setup.settings[key]?.currentValue;
  const valueLabel=key=>settings ? display?.[key] : setup.settings[key]?.stringValue;
  const mode=valueLabel(RACE_MODE)||SETTING_CHOICES[RACE_MODE][value(RACE_MODE)];
  const time=value(RACE_MINUTES)??'—', laps=value(RACE_LAPS)??'—';
  const percent=key=>/^\d+(?:\.\d+)?\s*%$/.test(valueLabel(key)) ? valueLabel(key) : (value(key)??'—')+'%';
  if(mode==='% Track Time') return percent(RACE_MINUTES)+' track time';
  if(mode==='% Track Laps') return percent(RACE_LAPS)+' track laps';
  if(mode?.startsWith('%')) return percent(RACE_MINUTES)+' track time / '+percent(RACE_LAPS)+' track laps';
  if(mode==='Laps') return laps+' laps';
  if(mode==='Time and laps') return time+' min / '+laps+' laps';
  return time+' min';
}
function renderSummary() {
  if (!setup) return;
  const track = currentTrack(), car = currentCar();
  $('selectionSummary').innerHTML = `<h3>${escapeHtml(track?.trackName || 'Choose a track')}</h3><p>${escapeHtml(track?.type || '')}</p><p class="summary-car">${escapeHtml(car ? carModel(car) : 'Choose a car')}</p>`;
  const lengths = {PRACTICE:'SESSSET_Practice_Length',QUALIFY:'SESSSET_Qualify_Length',WARMUP:'SESSSET_WarmUp_Length',RACE:'SESSSET_race_time'};
  const enabled = Object.entries(SESSION_NAMES).filter(([type])=>setup.amounts[type]>0);
  $('sessionSummary').innerHTML = enabled.length ? enabled.map(([type,name])=>`<div class="summary-session">${name}${setup.amounts[type]>1?' × '+setup.amounts[type]:''}<span>${type==='RACE' ? escapeHtml(raceLengthSummary()) : escapeHtml(setup.settings[lengths[type]]?.currentValue ?? '—')+' min'}</span></div>`).join('') : '<p class="muted">No sessions enabled</p>';
  $('summaryMeta').innerHTML = `<div>${escapeHtml(setup.settings.SESSSET_Num_Opponents?.currentValue || 0)} AI opponents · ${escapeHtml(settingValue('SESSSET_AI_Strength'))} strength</div><div>${escapeHtml(settingValue('SESSSET_weather'))} weather</div>`;
}
function updateControls() {
  if(typeof updateLabControls!=='function')
    $('editorFields').disabled = (saving&&!sessionValueSaving&&!savingOptions) || starting || !menuReady || serverBusy;
  const anySession = setup && Object.values(setup.amounts).some(n=>n>0);
  $('startButton').disabled = !setup || !menuReady || saving || starting || serverBusy || !anySession || !!sessionValueEdits.size;
  $('startButton').innerHTML = starting ? 'Loading session…' : 'Start session <span aria-hidden="true">↗</span>';
  $('reloadButton').disabled = saving || starting || !!sessionValueEdits.size;
  $('launchButton').disabled = $('launchEacButton').disabled = gameRunning || launching || saving || starting;
  $('launchHint').textContent = sessionValueEdits.size ? (sessionValueError?'Retry or discard pending edits before starting.':'Start session unlocks when your changes are confirmed.') : starting ? 'Loading progress appears below. You can switch to LMU.' : !gameRunning ? 'Choose a launch mode to open LMU.' : !menuReady ? 'Open the single-player main menu in LMU to start a local session.' : !anySession ? 'Enable at least one session in Sessions.' : 'Dismiss the startup prompt in LMU first. After loading, switch to LMU and press Drive.';
  updateSessionValueControls();
}
function renderNavigation() {
  const state = navigation?.state || {};
  gameProtected = !!navigation?.protected;
  menuReady = state.settingMode==='SETTING_GRANDPRIX' && state.gameState==='GSTATE_SETUP' && state.navigationState==='NAV_MAIN_MENU' && !navigation.loadingStatus?.loading;
  $('connection').textContent = !gameRunning ? 'LMU not running' : navigation?.loadingStatus?.loading ? 'Loading session' : menuReady ? 'Single-player main menu' : state.settingMode==='SETTING_MULTIPLAYER' ? 'Online session' : `${state.gameSession?.replace(/(\D)(\d)/,'$1 $2').toLowerCase() || 'Single-player session'} · loaded`;
  $('connection').className = 'connection' + (menuReady?' ready':'');
  const banner = $('stateBanner');
  banner.hidden = !gameRunning || menuReady && !serverBusy || !setup;
  if (!banner.hidden) banner.innerHTML = serverBusy ? 'An operation is in progress. Wait for it to finish before changing the session.' : navigation?.loadingStatus?.loading || starting ? 'LMU is loading a session.' : state.settingMode==='SETTING_MULTIPLAYER' ? 'LMU is in multiplayer. Return to the single-player main menu to apply your changes or start a local session.' : 'A session is loaded in LMU. You can edit your next session or load a preset here. Return to the single-player main menu to apply your changes.'+(state.settingMode==='SETTING_GRANDPRIX'?'<button class="button" data-dialog="menuDialog">Return to menu</button>':'');
  updateControls();
}
async function loadSetup() {
  if(sessionValueSaving||savingOptions)return;
  const revision=sessionValueRevision;
  try {
    const next = await request('/api/session/setup');
    if (!catalog) catalog = await request('/api/session/catalog');
    if(revision!==sessionValueRevision||sessionValueSaving||savingOptions)return;
    setup = next;
    renderContent(); renderSummary(); if(!sessionValueEdits.size)setSaved('In sync with LMU');
  } catch(e) {
    if (setup) reportError(e.message);
  }
}
function change(body, confirmed) {
  if ((saving&&!savingOptions) || starting || !menuReady || serverBusy || (sessionValueEdits.size&&!savingOptions)) return;
  return new Promise(resolve => {
    sessionOptions.push({body,confirmed,resolve});
    if(!savingOptions) flushSessionOptions();
  });
}
async function flushSessionOptions() {
  savingOptions=true;saving=true;sessionValueRevision++;updateControls();
  $('saveDot').className = 'save-dot saving'; $('saveStatus').textContent = 'Saving to LMU…';
  try {
    while(sessionOptions.length){
      if(!menuReady||serverBusy)throw Error('Return to the single-player main menu to change the session.');
      const {body,confirmed,resolve}=sessionOptions[0];
      const before=setup;
      setup=await request('/api/session/action',body);
      confirmed?.(before);
      sessionOptions.shift();
      resolve();
    }
    setSaved(); $('errorBar').hidden = true;
  } catch(e) {
    try { setup=await request('/api/session/setup'); } catch {}
    reportError(e.message+(sessionOptions.length>1?' Re-enter the remaining selections.':''));
    for(const edit of sessionOptions.splice(0))edit.resolve();
  } finally {
    savingOptions=false;saving=false;
    if(sessionValueEdits.size&&document.activeElement?.dataset.sessionValue)renderAfterSessionValues();
    else {renderContent();renderSummary();updateControls();}
    scheduleSessionValues();
  }
}
async function launchLocal() { return launchGame('direct'); }
async function launchGame(mode) {
  const menu=$('launchButton').closest('details');
  if(menu?.open){
    const hadFocus=menu.contains(document.activeElement);
    menu.open=false;
    if(hadFocus)menu.querySelector('summary').focus();
  }
  if (gameRunning || launching) return;
  launching=true;updateControls();
  try {
    await request('/api/launch',{mode});
    gameRunning=true;gameProtected=mode==='eac';$('saveStatus').textContent='Waiting for LMU’s menu…';
    $('emptyState').querySelector('p').textContent='LMU is starting. Dismiss its “Press any button” prompt, then return here to configure your session.';
    updateControls();
  } catch(e) { reportError(e.message); }
  finally { launching=false;updateControls(); }
}
async function poll() {
  if (polling) return;
  polling=true;
  try {
    navigation = await request('/api/session/state');
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
        gameProtected=!!state.game?.protected;
        serverBusy=!!state.busy;
        $('connection').textContent=gameRunning ? 'Waiting for LMU’s menu' : 'LMU not running';
      } catch { $('connection').textContent='App connection lost'; }
      if(!gameRunning) {setup=null;catalog=null;if(typeof discardBuilderDraft==='function')discardBuilderDraft(true);$('editorFields').hidden=true;$('emptyState').hidden=false;$('saveStatus').textContent='LMU is closed';$('saveDot').className='save-dot';}
    }
    updateControls();
  } finally { polling=false; }
}
async function startSession() {
  if ($('startButton').disabled) return;
  starting=true; $('stopMonitoring').hidden=false; updateControls(); $('loadProgress').hidden=false; $('loadProgressBar').removeAttribute('value'); $('loadProgressText').textContent='Preparing session…';
  try {
    await request('/api/session/action',{action:'start',setup});
    $('loadProgressBar').value=100; $('loadProgressText').textContent='Session loaded. Switch to LMU and press Drive.'; $('stopMonitoring').hidden=true;
    setSaved('Session loaded');
  } catch(e) { await loadSetup(); reportError(e.message); $('loadProgressText').textContent='Check LMU before trying again.'; }
  finally {starting=false;await poll();updateControls();}
}
function switchSection(next) {
  section=next;
  const [eyebrow,title,description]=SECTION_INFO[next];
  $('sectionEyebrow').textContent=eyebrow;$('sectionTitle').textContent=title;$('sectionDescription').textContent=description;
  renderContent();$('editor').scrollTop=0;
}
document.addEventListener('click',e=>{
  const button=e.target.closest('button');if(!button || button.disabled)return;
  if(button.dataset.section)switchSection(button.dataset.section);
  if(button.dataset.sessionValue){const key=button.dataset.sessionValue,current=sessionValueEdits.get(key)??(typeof builderSetting==='function'?builderSetting(key):setup.settings[key])?.currentValue;queueSessionValue(key,button.dataset.directValue!==undefined?Number(button.dataset.directValue):Math.max(0,Math.min(TIME_SETTINGS.has(key)?1439:10000,current+Number(button.dataset.valueStep))));}
  else if(button.dataset.change)change(JSON.parse(button.dataset.change));
  if(button.dataset.weatherSession){weatherSession=button.dataset.weatherSession;renderContent();}
  if(button.dataset.weatherNode){weatherNode=button.dataset.weatherNode;if(!button.dataset.builderOpen)renderContent();}
  if(button.dataset.dialog)$(button.dataset.dialog).showModal();
});
document.addEventListener('change',e=>{
  const el=e.target;
  if(el.dataset.sessionValue){const key=el.dataset.sessionValue,clock=TIME_SETTINGS.has(key),match=clock?/^(\d{2}):(\d{2})$/.exec(el.value):null,value=clock?(match?Number(match[1])*60+Number(match[2]):NaN):el.value.trim()?Number(el.value):NaN;if(!Number.isInteger(value)||value<0||value>(clock?1439:10000)){reportError(clock?'Enter a start time between 00:00 and 23:59, or choose Default.':'Enter a whole number within the available setting range.');paintSessionValue(key,sessionValueEdits.get(key)??setup.settings[key].currentValue);return;}queueSessionValue(key,value);}
  if(el.dataset.amount)change({action:'amount',session:el.dataset.amount,amount:el.checked?1:0});
  if(el.dataset.assist)change({action:'assist',key:el.dataset.assist,value:el.checked?1:0});
  if(el.id==='trackSelect')change({action:'track',id:el.value});
  if(el.id==='carSelect')change({action:'car',id:el.value});
  if(el.dataset.class || el.id==='gridSource'){const scope=el.closest('#builderDetailBody')||$('sectionContent');change({action:'grid',classes:[...scope.querySelectorAll('[data-class]:checked')].map(c=>c.dataset.class),fullGrid:scope.querySelector('#gridSource').value==='fill'});}
});
document.addEventListener('input',e=>{
  if(e.target.id==='carSearch'){carSearch=e.target.value;renderContent();}
  if(e.target.id==='settingSearch'){settingSearch=e.target.value;renderContent();}
});
$('launchButton').addEventListener('click',launchLocal);
$('launchEacButton').addEventListener('click',()=>launchGame('eac'));
$('launchLocalChoice').addEventListener('click',launchLocal);
$('launchEacChoice').addEventListener('click',()=>launchGame('eac'));
$('reloadButton').addEventListener('click',async()=>{await loadSetup();await poll();});
$('startButton').addEventListener('click',startSession);
$('retrySessionSave').addEventListener('click',()=>{sessionValueError='';flushSessionValues();});
$('discardSessionEdits').addEventListener('click',discardSessionValues);
document.addEventListener('keydown',e=>{if(e.key==='Enter'&&e.target.matches('input[data-session-value]')){e.preventDefault();e.target.dispatchEvent(new Event('change',{bubbles:true}));flushSessionValues();}});
$('dismissError').addEventListener('click',()=>{$('errorBar').hidden=true;});
$('keepSession').addEventListener('click',()=>$('menuDialog').close());
$('keepSettings').addEventListener('click',()=>$('defaultsDialog').close());
$('applyDefaults').addEventListener('click',()=>{$('defaultsDialog').close();change({action:'trackDefaults'});});
$('returnMenu').addEventListener('click',async()=>{
  $('menuDialog').close();if(saving || starting)return;saving=true;updateControls();
  try{setup=await request('/api/session/action',{action:'menu'});renderContent();renderSummary();setSaved('In sync with LMU');await poll();}catch(e){reportError(e.message);}finally{saving=false;updateControls();}
});
$('stopMonitoring').addEventListener('click',async()=>{try{await request('/api/cancel',{});}catch(e){reportError(e.message);}});
poll();pollTimer=setInterval(poll,1500);
