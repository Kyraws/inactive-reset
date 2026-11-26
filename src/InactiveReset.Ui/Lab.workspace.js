'use strict';
let eventCategory='all',presetSource='personal',libraryTrack='',libraryPreviewId='',libraryCar='',circuitQuery='';
let inspectedPreset=null,builderPreset=null,checkpointEdit=null,checkpointUndoToken='',checkpointMutation=false;
let savedMapTracks=[],mapRequests=new Map(),circuitMaps=new Map(),mapFailures=new Map();
const normalizeTrack=s=>String(s||'').trim().toLowerCase().replace(/\s+\d+\.\d+$/,'');

let builderDetail = '', timingStage = 'PRACTICE', builderTrigger, previewCircuit = '';
let overviewMarkup = '', detailMarkup = '', circuitCardCatalog;
const builderTitles = {car:'Car & livery',circuit:'Circuit',sessions:'Session & time',grid:'Multi-class AI grid',weather:'Weather timeline',rules:'Session rules'};
const lengthKey = {PRACTICE:'SESSSET_Practice_Length',QUALIFY:'SESSSET_Qualify_Length',WARMUP:'SESSSET_WarmUp_Length',RACE:'SESSSET_race_time'};
const clockKey = {PRACTICE:'SESSSET_practice1_starting_time',QUALIFY:'SESSSET_qualify_starting_time',WARMUP:'SESSSET_warmup_starting_time',RACE:'SESSSET_race_starting_time'};
const automaticStarts = new Set();
const pointNames = {START:'Start',NODE_25:'25%',NODE_50:'50%',NODE_75:'75%',FINISH:'Finish'};
const stagesEnabled = () => Object.keys(SESSION_NAMES).filter(stage=>setup?.amounts[stage]>0);

function stageSwitches(){
  return `<div class="stage-switches">${Object.entries(SESSION_NAMES).map(([stage,name])=>`<div class="stage-switch"><span>${name}</span>${sessionToggle(stage,name)}<small>${setup.amounts[stage]&&setup.settings[lengthKey[stage]] ? escapeHtml(stage==='RACE'?raceLengthSummary():setup.settings[lengthKey[stage]].currentValue+' min') : ''}</small></div>`).join('')}</div>`;
}
function forecasts(enabled){
  const pending = builderEdits.get('weatherPreset:'+weatherSession)?.preset;
  const mode = setup.settings.SESSSET_weather?.stringValue;
  const presets = [['SUNNY','Sunny'],['CLOUDY','Cloudy'],['RAINY','Rainy'],['DEFAULT','Track default']];
  return `<div class="forecast-controls"><div class="segmented" aria-label="Weather session">${enabled.map(stage=>`<button data-weather-session="${stage}" class="${stage===weatherSession?'active':''}" aria-pressed="${stage===weatherSession}">${SESSION_NAMES[stage]}</button>`).join('')}</div><label class="forecast-picker">Forecast preset<select data-forecast>${option('',mode||'Choose forecast…',!pending)}${presets.map(([id,title])=>option(id,title,id===pending)).join('')}</select></label>${pending?'<small>LMU loads this forecast when you apply.</small>':''}</div>`;
}
function forecastPoints(openDetails){
  return `<div class="forecast-points">${Object.entries(pointNames).map(([node,label])=>{
    const point=setup.weather[weatherSession]?.[node];
    return `<button class="button" data-weather-node="${node}" ${openDetails?'data-builder-open="weather"':''} aria-pressed="${weatherNode===node}"><span>${label}</span><strong>${escapeHtml(settingDisplay('WNV_SKY',point?.WNV_SKY?.currentValue))}</strong><small>${escapeHtml(point?.WNV_TEMPERATURE?.currentValue ?? '—')}°C · ${escapeHtml(point?.WNV_RAIN_CHANCE?.currentValue ?? '—')}% rain</small></button>`;
  }).join('')}</div>`;
}
function renderTimingDetail() {
  const enabled=stagesEnabled();
  if(!enabled.includes(timingStage))timingStage=enabled[0] || 'PRACTICE';
  const stage=timingStage, clock=setup.settings[clockKey[stage]], duration=setup.settings[lengthKey[stage]];
  const privateKey=stage==='PRACTICE'?'SESSSET_private_prac':stage==='QUALIFY'?'SESSSET_private_qual':'';
  const race=stage==='RACE', follows=enabled.indexOf(stage)>0;
  const clockMode=automaticStarts.has(stage)&&follows?'follow':clock?.currentValue<0?String(clock.currentValue):'fixed';
  return stageSwitches()+`<div class="segmented timing-tabs" role="group" aria-label="Stage to configure">${enabled.map(stage=>`<button data-timing-stage="${stage}" aria-pressed="${stage===timingStage}">${SESSION_NAMES[stage]}</button>`).join('')}</div>` +
    (enabled.length?`<section class="settings-card timing-options" aria-label="${SESSION_NAMES[stage]} timing">
      ${duration?`<div class="timing-field"><span>Duration</span>${builderSettingControl(lengthKey[stage],duration)}</div>`:''}
      ${clock?`<div class="timing-field"><label for="value-${clockKey[stage]}">Start time</label><div class="timing-input"><input id="value-${clockKey[stage]}" type="time" data-session-value="${clockKey[stage]}" value="${clockValue(clock.currentValue)}"><select aria-label="Start time mode" data-clock-stage="${stage}">${option('fixed','Fixed time',clockMode==='fixed',clockMode!=='fixed')}${follows?option('follow','Follow previous session',clockMode==='follow'):''}${Object.entries(TIME_MODES).map(([value,label])=>option(value,label,clockMode===value)).join('')}</select></div></div>`:''}
      ${privateKey?timingSelect(privateKey):''}
      ${race?timingSelect('SESSSET_formation')+row('SESSSET_Grid_Position',setup.settings.SESSSET_Grid_Position):''}
    </section>`: '<p class="small-note">Enable a stage to configure its timing.</p>') +
    (enabled.length>1?'<p class="small-note">Follow previous session adds five minutes after the previous scheduled finish.</p>':'') +
    block('Event time scale',['SESSSET_race_timescale']);
}
function timingSelect(key){
  const field=setup.settings[key];
  if(!field)return '';
  return `<div class="timing-field"><span>${escapeHtml(label(key))}</span>${builderSettingControl(key,field)}</div>`;
}
function renderCircuitDetail(){
  const tracks=catalog.tracks.filter(track=>track.type!=='Showroom');
  const circuits=Map.groupBy(tracks,circuitName);
  const selected=currentTrack(), venue=previewCircuit||circuitName(selected);
  const layouts=circuits.get(venue)||[];
  const favourites=tracks.filter(track=>track.owned&&labPreferences.tracks.includes(track.id));
  const body=$('builderDetailBody');
  // Keep the venue photos mounted while the user browses layouts.
  if(circuitCardCatalog!==catalog||!body.querySelector('.circuit-cards')){
    body.innerHTML=`<div class="circuit-cards" role="group" aria-label="Circuits">${[...circuits.keys()].sort().map(name=>{
      const options=circuits.get(name), track=options.find(t=>t.owned)||options[0];
      return `<button class="button circuit-card" data-preview-circuit="${escapeHtml(name)}">${circuitPhoto(track,true)}<strong>${escapeHtml(name)}</strong></button>`;
    }).join('')}</div><section class="circuit-layouts"><h3></h3><div class="circuit-layout-row" role="group" aria-label="Circuit layouts"></div><div class="circuit-tools"></div></section>`;
    circuitCardCatalog=catalog;
    detailMarkup='';
  }
  for(const button of body.querySelectorAll('[data-preview-circuit]'))
    button.setAttribute('aria-pressed',String(button.dataset.previewCircuit===venue));
  const row=body.querySelector('.circuit-layout-row');
  const layoutMarkup=layouts.map(track=>`<button class="button layout-tile" data-track="${escapeHtml(track.id)}" ${track.owned?'':'disabled'}>${builderOutline(track)}<strong>${escapeHtml(layoutName(track))}</strong>${track.owned?'':'<small>Not owned</small>'}</button>`).join('');
  if(layoutMarkup!==detailMarkup){
    row.innerHTML=layoutMarkup;
    detailMarkup=layoutMarkup;
  }
  for(const button of row.querySelectorAll('[data-track]'))
    button.setAttribute('aria-pressed',String(button.dataset.track===selected?.id));
  body.querySelector('.circuit-layouts h3').textContent=venue?`${venue} layouts`:'Layouts';
  const tools=body.querySelector('.circuit-tools');
  const toolsMarkup=(layouts.some(track=>track.id===selected?.id)?`<button class="icon-button" data-fav-track="${escapeHtml(selected.id)}" aria-label="Favourite current layout">${labPreferences.tracks.includes(selected.id)?'★':'☆'}</button><button class="button quiet" data-dialog="defaultsDialog">Use LMU circuit defaults</button>`:'')+
    favourites.map(track=>`<button class="button quiet" data-track="${escapeHtml(track.id)}" aria-label="Favourite layout: ${escapeHtml(trackDisplay(track))}">${escapeHtml(trackDisplay(track))}</button>`).join('');
  if(tools.dataset.markup!==toolsMarkup){
    tools.innerHTML=toolsMarkup;
    tools.dataset.markup=toolsMarkup;
  }
}
function circuitPhoto(track,lazy=false){
  return track?artwork('track-card',track.sceneDesc,circuitName(track).normalize('NFD').replace(/[^a-z0-9]/gi,''),lazy):'';
}
function builderOutline(track){
  return track?`<span class="event-outline" data-builder-map="${escapeHtml(track.id)}" aria-hidden="true"></span>`:'';
}
function renderWeatherDetail(enabled) {
  if(!enabled.length)return '<p class="small-note">Enable Practice, Qualifying or Race to edit weather.</p>';
  const weather=setup.weather[weatherSession]?.[weatherNode] || {};
  return forecasts(enabled)+`<div class="conditions-columns"><section>${block('Weather behaviour',['SESSSET_weather','SESSSET_timescaled_weather'])}${block('Track surface',SESSION_KEYS[weatherSession].filter(k=>k.includes('realroad')))}</section><section class="section-block weather-point-editor"><h2>Forecast timeline</h2>${forecastPoints(false)}<div class="weather-point-heading"><h3>${pointNames[weatherNode]} conditions</h3><button class="button" data-weather-copy="points">Copy to all points</button></div><div class="settings-card">${Object.entries(weather).map(([key,value])=>
    key==='WNV_RAIN_CHANCE'&&weather.WNV_SKY?.currentValue<5 ? `<div class="setting-row"><span>Rain chance</span><small>Choose a rain-capable sky.</small></div>` : row(key,value,'weather',{session:weatherSession,node:weatherNode})).join('')}</div></section></div>`;
}
function builderCard(id,body,tools='') {
  return `<section class="builder-overview-card settings-card" data-card="${id}"><div class="builder-card-heading"><h2><button class="card-open" data-builder-open="${id}" aria-haspopup="dialog" aria-controls="builderDetail">${builderTitles[id]}<span aria-hidden="true">↗</span></button></h2>${tools}</div>${body}</section>`;
}
function quickRange(key,title,id,min,max,formatter=String) {
  const field=setup.settings[key];
  if(!field)return '';
  const [lower,upper]=TIME_SETTINGS.has(key)?[min,max]:builderBounds(field,min,max), value=field.currentValue;
  if(!Number.isFinite(lower)||!Number.isFinite(upper)||lower>=upper)return row(key,field);
  return `<label class="quick-range" for="${id}"><span>${title}<output id="${id}Value">${escapeHtml(formatter(value))}</output></span><input id="${id}" type="range" min="${lower}" max="${upper}" step="${field.stepValue||1}" value="${Math.max(lower,Math.min(upper,value))}" data-builder-range="${key}"></label>`;
}
function renderBuilderOverview(enabled) {
  const car=currentCar(), track=currentTrack(), owned=catalog.cars.filter(c=>c.isOwned);
  const models=[...new Map(owned.map(c=>[modelKey(c),c])).values()].sort((a,b)=>carModel(a).localeCompare(carModel(b)));
  const tracks=catalog.tracks.filter(t=>t.owned&&t.type!=='Showroom');
  const circuits=[...new Set(tracks.map(circuitName))].sort();
  const layouts=tracks.filter(t=>circuitName(t)===circuitName(track));
  const opponentCount=setup.settings.SESSSET_Num_Opponents?.currentValue ?? 0;
  const strength=setup.settings.SESSSET_AI_Strength;
  const controls=(strength?`<div class="setting-row"><span class="setting-label">AI strength</span>${builderSettingControl('SESSSET_AI_Strength',strength,'setting',{},'quickStrength')}</div>`:'')+
    row('SESSSET_AI_Aggression',setup.settings.SESSSET_AI_Aggression);
  const activeStages = stagesEnabled();
  const clock = activeStages.includes(timingStage) ? timingStage : activeStages[0];
  const clockSetting = setup.settings[clockKey[clock]], clockTitle=SESSION_NAMES[clock]+' start time';
  return '<div class="builder-overview">' +
    builderCard('car',`<div class="selection-preview"><div class="builder-art car-art">${artwork('car',carArtKey(car))}</div><div>${car?vehicleClassBadge(parts(car)[1]) : ''}<strong>${escapeHtml(car?carModel(car):'Choose a car')}</strong><label>Quick car<select id="quickCar">${models.map(c=>option(c.id,carModel(c)+' · '+parts(c)[1],car&&modelKey(c)===modelKey(car))).join('')}</select></label></div></div>`) +
    builderCard('circuit',`<div class="selection-preview"><div class="builder-art circuit-art">${circuitPhoto(track)}<div class="circuit-art-label"><strong>${escapeHtml(circuitName(track))}</strong><small>${track?escapeHtml(layoutName(track)):''}</small></div>${builderOutline(track)}</div><div class="quick-circuit-controls"><label>Quick circuit<select id="quickCircuit">${circuits.map(name=>option(name,name,name===circuitName(track))).join('')}</select></label></div><div class="builder-layouts" role="group" aria-label="Circuit layouts">${layouts.map(t=>`<button class="button" data-track="${escapeHtml(t.id)}" aria-pressed="${t.id===track?.id}">${escapeHtml(layoutName(t))}</button>`).join('')}</div></div>`) +
    builderCard('sessions',stageSwitches()+(clockSetting?`<div class="quick-clock"><input id="quickClockTime" type="time" value="${clockValue(clockSetting.currentValue)}" data-session-value="${clockKey[clock]}" aria-label="${clockTitle}" step="60">${quickRange(clockKey[clock],clockTitle,'quickClock',0,1439,value=>TIME_MODES[value]||clockValue(value))}</div>`:'<p class="small-note">Enable at least one stage to apply.</p>')) +
    builderCard('grid',`<div class="button-group"><button class="button" data-grid-choice="solo" aria-pressed="${opponentCount===0}">Solo</button><button class="button" data-grid-choice="wec" aria-pressed="${opponentCount>0&&!setup.selection.fullGrid}">WEC</button><button class="button" data-grid-choice="compact" aria-pressed="${opponentCount>0&&!!setup.selection.fullGrid}">Compact</button></div><p class="small-note">${opponentCount} AI · ${setup.selection.fullGrid?'Fill grid':'Season grid'}</p>${'<div class="quick-grid-ranges">'+controls+'</div>'}`) +
    builderCard('weather',enabled.length?forecastPoints(true):'<p class="small-note">Enable a stage with a forecast.</p>',enabled.length?forecasts(enabled):'') +
    '</div><button class="button quiet" data-builder-open="rules" aria-haspopup="dialog">Session rules ↗</button>';
}

renderContent=function(){
  if(!setup||!catalog)return;
  if(!builderDraft)makeBuilderDraft();
  const live=setup;
  setup=builderDraft;
  try {
  const focus=document.activeElement;
  const focusedId=focus?.id, caret=focus?.selectionStart, modal=$('builderDetail'), activeStage=focus?.dataset.amount;
  const focusData=[...(focus?.attributes || [])].filter(attribute=>attribute.name.startsWith('data-')&&attribute.name!=='data-lmu-disabled');
  const focusScope=modal.contains(focus)?modal:$('sectionContent');
  const scroll=focusScope.querySelector('.workflow-dialog-body')?.scrollTop;
  const enabled=stagesEnabled().filter(stage=>setup.weather[stage]);
  if(!enabled.includes(weatherSession))weatherSession=enabled[0]||'PRACTICE';
  $('sectionContent').dataset.section=section;
  const overview=renderBuilderOverview(enabled);
  if(overview!==overviewMarkup){
    $('sectionContent').innerHTML=overview;
    overviewMarkup=overview;
  }
  if(builderDetail){
    modal.dataset.detail=builderDetail;
    $('builderDetailTitle').textContent=builderTitles[builderDetail];
    if(builderDetail==='circuit')renderCircuitDetail();
    else{
      const detail=({car:()=>renderSelection('car'),sessions:renderTimingDetail,grid:renderGrid,weather:()=>renderWeatherDetail(enabled),rules:renderRules})[builderDetail]();
      if(detail!==detailMarkup){
        const body=$('builderDetailBody');
        const railOffsets=[...body.querySelectorAll('.horizontal-rail')].map(rail=>[rail.id,rail.scrollLeft]);
        body.innerHTML=detail;
        for(const [id,offset] of railOffsets){
          const rail=$(id);
          if(rail)rail.scrollLeft=offset;
        }
        detailMarkup=detail;
      }
    }
  }
  renderBuilderOutlines();
  $('sectionTitle').textContent='Session & Conditions';
  $('sectionEyebrow').textContent='';
  $('sectionDescription').textContent='';
  $('emptyState').hidden=true;$('editorFields').hidden=false;
  const replacement=focusedId&&(focusScope.querySelector('#'+CSS.escape(focusedId))||$(focusedId))||
    activeStage&&focusScope.querySelector('[data-amount="'+activeStage+'"]')||
    focusData.length&&focusScope.querySelector(focus.tagName+focusData.map(a=>'['+a.name+'="'+CSS.escape(a.value)+'"]').join(''));
  if(replacement&&replacement!==focus){replacement.focus({preventScroll:true});if(caret!==null&&/search|text/.test(replacement.type))replacement.setSelectionRange(caret,caret);}
  if(scroll!==undefined) $('builderDetailBody').scrollTop=scroll;
  } finally { setup=live; }
  renderSummary();
  updateControls();
};

const liveSummary=renderSummary;
renderSummary=function(){
  const live=setup;
  if(labPage==='setup'&&builderDraft)setup=builderDraft;
  try{liveSummary();}finally{setup=live;}
};

function updateWorkspace(){
  $('builderSave').disabled=$('savePlanButton').disabled || [...builderEdits.values()].some(edit=>['weatherPreset','practiceConditions'].includes(edit.type));
  $('builderLoad').hidden=!labPreferences.plans.length;
  $('builderReset').disabled=!builderEditReady();
  $('builderApply').disabled=!builderEdits.size||!builderMenuReady()||!Object.values(builderDraft?.amounts||{}).some(amount=>amount>0);
  $('builderApply').classList.toggle('primary',builderEdits.size>0);
  $('builderStart').classList.toggle('primary',builderEdits.size===0);
  if(builderEdits.size)$('startButton').disabled=true;
  $('builderStart').disabled=$('startButton').disabled;
  $('builderStatus').textContent=builderStatus || (!menuReady ? (builderEdits.size?'Draft changes ready to apply at the main menu.':'') :
    !Object.values(builderDraft?.amounts || {}).some(amount=>amount>0) ? 'Enable at least one stage to apply.' :
    builderEdits.size ? 'Apply your changes before starting.' : 'Session settings match LMU.');
  $('builderDetailBody').disabled=!builderEditReady();
  for(const b of document.querySelectorAll('[data-checkpoint-action]'))b.disabled=checkpointMutation||!!labState?.busy;
  if(builderPreset&&setup){
    const modified=JSON.stringify(snapshotPlan())!==JSON.stringify(builderPreset.baseline);
    $('builderSource').hidden=false;
    $('builderSource').innerHTML=`<div><small>LOADED PRESET</small><strong>${escapeHtml(builderPreset.name)}</strong><span class="pill">${modified?'Modified':'Saved values'}</span></div><div class="button-group"><button class="button quiet" data-restore-builder ${!modified?'disabled':''}>Restore saved values</button>${builderPreset.kind==='personal'?`<button class="button" data-update-builder ${!modified?'disabled':''}>Update preset</button>`:'<span class="small-note">Official source · save a personal copy to keep changes</span>'}</div>`;
  }else $('builderSource').hidden=true;
  updatePresetTab();
  if(['setup','official','presets'].includes(labPage)&&currentTrack()?.id)renderCircuitMap($('summaryMap'),currentTrack().id,[],null);
}

function allMapTracks(){return [...new Map([...savedMapTracks,...(catalog?.tracks||[])].map(t=>[t.id,t])).values()].filter(t=>t.id&&t.type!=='Showroom');}
function mappedLayout(track){
  const chosen=labPreferences.mapLayouts?.[track];if(chosen)return chosen;
  const all=allMapTracks(), key=normalizeTrack(track);
  const exact=all.filter(t=>[t.eventName,t.sceneDesc,t.displayProperties?.name].some(n=>n&&normalizeTrack(n)===key));
  if(exact.length===1)return exact[0].id;
  const matches=all.filter(t=>[t.trackName,t.shortName,t.name,t.displayProperties?.shortName,t.sceneDesc].some(n=>n&&normalizeTrack(n)===key));
  return matches.length===1?matches[0].id:'';
}
function mapProjection(points){
  const bounds=points.reduce((b,p)=>[Math.min(b[0],p.z),Math.max(b[1],p.z),Math.min(b[2],p.x),Math.max(b[3],p.x)],[Infinity,-Infinity,Infinity,-Infinity]);
  const [minX,maxX,minY,maxY]=bounds;
  const scale=Math.min(552/Math.max(1,maxX-minX),292/Math.max(1,maxY-minY));
  return p => [Number((300+(p.z-(minX+maxX)/2)*scale).toFixed(2)),Number((170+(p.x-(minY+maxY)/2)*scale).toFixed(2))];
}
function mapSvg(map,checkpoints,selected,previewOnly=false){
  const points=map.points.filter(p=>p.type===0||p.type===1),road=points.filter(p=>p.type===0),project=mapProjection(points);
  const path=list=>list.map((p,i)=>(i?'L':'M')+project(p).join(' ')).join(' ');
  return `<svg viewBox="0 0 600 340" role="img" aria-label="LMU circuit map with saved checkpoint positions"><path class="map-pit" d="${path(points.filter(p=>p.type===1))}"/><path class="map-road-shadow" d="${path(road)} Z"/><path class="map-road" d="${path(road)} Z"/>${checkpoints.filter(c=>Number.isFinite(c.x)&&Number.isFinite(c.z)).map((c,i)=>`<g role="button" tabindex="0" aria-label="${previewOnly?'Preview':'Select'} ${escapeHtml(c.name)}" ${previewOnly?'data-preview-checkpoint':'data-map-checkpoint'}="${escapeHtml(c.id)}" class="map-marker ${c.id===selected?'selected':''}" transform="translate(${project(c).join(" ")})"><circle r="10"/><text text-anchor="middle" y="4">${i+1}</text></g>`).join('')}</svg>`;
}
function renderCircuitMap(element,id,checkpoints,selected,previewOnly=false){
  if(!element)return;
  if(!id){element.innerHTML='<div class="map-empty">Choose the exact map layout above to display saved positions.</div>';return;}
  const map=circuitMaps.get(id);
  if(map){element.innerHTML=mapSvg(map,checkpoints,selected,previewOnly)+`<small>${map.cached?'Saved':'LMU'} layout geometry${map.warning?' · '+escapeHtml(map.warning):''}</small>`;return;}
  element.innerHTML='<div class="map-empty">'+(mapFailures.has(id)?'Map unavailable. Launch LMU and refresh the map; checkpoint lists remain available.':'Loading LMU circuit geometry…')+'</div>';
  if(mapRequests.has(id)||Date.now()-(mapFailures.get(id)||0)<60000)return;
  const pending=request('/api/track-map?id='+encodeURIComponent(id)).then(map=>{
    circuitMaps.set(id,map);mapFailures.delete(id);if(map.track)savedMapTracks=[...savedMapTracks.filter(t=>t.id!==id),map.track];
  }).catch(()=>mapFailures.set(id,Date.now())).finally(()=>{mapRequests.delete(id);if(labPage==='library')renderLibrary();if(labPage==='practice')renderPracticeManagement();if(labPage==='setup')renderBuilderOutlines(id);if(labPage==='official')for(const outline of $('officialPanel').querySelectorAll('[data-outline]'))renderEventOutline(outline);updateWorkspace();});
  mapRequests.set(id,pending);
}
function renderBuilderOutlines(updatedId){
  for(const element of document.querySelectorAll('[data-builder-map]')){
    const id=element.dataset.builderMap;
    if(updatedId?id===updatedId:!element.querySelector('svg'))renderCircuitMap(element,id,[],null);
  }
}
async function refreshCircuitMap(){
  const id=$('mapLayout').value;if(!id)return;
  $('refreshCircuitMap').disabled=true;
  try{circuitMaps.set(id,await request('/api/track-map?id='+encodeURIComponent(id)+'&refresh=true'));mapFailures.delete(id);renderBuilderOutlines(id);}
  catch(e){notify(e.message,true);}finally{$('refreshCircuitMap').disabled=false;renderLibrary();}
}

renderLibrary=function(){
  const all=(labState?.checkpoints||[]).filter(c=>!c.quick&&c.name!=='Quick capture'),tracks=[...new Set(all.map(c=>c.track))].sort();
  if(!tracks.includes(libraryTrack))libraryTrack=tracks.find(t=>normalizeTrack(t)===normalizeTrack(labState?.session?.track))||tracks[0]||'';
  $('libraryTrackTitle').textContent=libraryTrack?liveTrackDisplay(libraryTrack):'No named checkpoints yet';
  $('libraryCircuits').innerHTML=tracks.filter(t=>`${t} ${liveTrackDisplay(t)}`.toLowerCase().includes(circuitQuery.toLowerCase())).map(t=>`<button class="circuit-choice ${t===libraryTrack?'active':''}" data-library-track="${escapeHtml(t)}"><span>${escapeHtml(liveTrackDisplay(t))}</span><small>${all.filter(c=>c.track===t).length} saved</small></button>`).join('')||'<p class="empty-note">No saved circuits match.</p>';
  const circuit=all.filter(c=>c.track===libraryTrack),cars=[...new Set(circuit.map(practiceVehicleModel))].sort();
  if(!cars.includes(libraryCar))libraryCar='';
  $('libraryCar').innerHTML='<option value="">All cars</option>'+cars.map(c=>option(c,c,c===libraryCar)).join('');
  const list=circuit.filter(c=>(!onlyFavourites||labPreferences.checkpoints.includes(c.id))&&(!libraryCar||practiceVehicleModel(c)===libraryCar)&&`${c.name} ${c.track} ${practiceVehicleModel(c)}`.toLowerCase().includes(libraryQuery.toLowerCase())).sort((a,b)=>librarySort==='distance'?a.lapDistance-b.lapDistance:a.name.localeCompare(b.name));
  if(!list.some(c=>c.id===libraryPreviewId))libraryPreviewId=list[0]?.id||'';
  const preview=list.find(c=>c.id===libraryPreviewId),active=$('cp').value;
  $('libraryMeta').textContent=`${list.length} of ${circuit.length} checkpoints`;
  $('checkpointCards').innerHTML=list.map((c,i)=>`<article class="checkpoint-item ${c.id===libraryPreviewId?'previewed':''}"><button class="checkpoint-preview" data-preview-checkpoint="${escapeHtml(c.id)}" aria-pressed="${c.id===libraryPreviewId}"><span class="point-number">${i+1}</span><span><strong>${escapeHtml(c.name)}</strong><small>${escapeHtml(practiceVehicleModel(c))} · ${Number.isFinite(c.speedKmh)?c.speedKmh.toFixed(1)+' km/h':'Speed unknown'} · G${c.gear??'—'}</small>${c.id===active?'<em>Active destination</em>':''}</span></button><button class="icon-button" data-fav-checkpoint="${escapeHtml(c.id)}" aria-label="Favourite ${escapeHtml(c.name)}">${labPreferences.checkpoints.includes(c.id)?'★':'☆'}</button></article>`).join('')||'<p class="empty-note">No matching named checkpoints. Save a Quick capture as named in Practice tools, or capture with a name.</p>';
  const matching=preview&&normalizeTrack(preview.track)===normalizeTrack(labState?.session?.track);
  $('libraryPreview').innerHTML=preview?`<h2>${escapeHtml(preview.name)}</h2><p>${escapeHtml(practiceVehicleModel(preview))}</p><div class="checkpoint-stats"><span>${Number.isFinite(preview.speedKmh)?preview.speedKmh.toFixed(1)+' km/h':'Unknown speed'}</span><span>Gear ${preview.gear??'—'}</span><span>${Math.round(preview.lapDistance||0)} m</span></div>${matching?'':'<p class="small-note">Load this checkpoint’s track to select it as a teleport destination.</p>'}<div class="button-group"><button class="button primary" data-map-checkpoint="${escapeHtml(preview.id)}" ${!matching||labState?.busy?'disabled':''}>${active===preview.id?'Selected destination':'Select checkpoint'}</button><button class="button quiet" data-checkpoint-action="rename" data-id="${escapeHtml(preview.id)}">Rename</button><button class="button critical" data-checkpoint-action="delete" data-id="${escapeHtml(preview.id)}">Delete</button></div>`:'<p class="empty-note">Select a checkpoint to inspect it.</p>';
  const mapped=mappedLayout(libraryTrack),available=allMapTracks();
  $('mapLayout').innerHTML='<option value="">Choose exact layout…</option>'+available.sort((a,b)=>(a.name||'').localeCompare(b.name||'')).map(t=>option(t.id,circuitName(t)+' · '+layoutName(t),t.id===mapped)).join('');
  $('refreshCircuitMap').disabled=!mapped;
  renderCircuitMap($('libraryMap'),mapped,list,libraryPreviewId,true);
  $('checkpointUndo').hidden=!checkpointUndoToken;
  for(const b of document.querySelectorAll('[data-checkpoint-action]'))b.disabled=checkpointMutation||!!labState?.busy;
};

function renderPracticeManagement() {
  if (!$('quickList')) return;
  const s = destinationState, active = $('cp').value, busy = !!s.busy;
  for (const [id,quick] of [['quickList',true],['destinationList',false]]) {
    const list = visibleCheckpoints.filter(point => !!point.quick === quick);
    const rows = list.map(point => {
      const key = escapeHtml(point.id), name = escapeHtml(point.name);
      return `<div class="checkpoint-row" data-selected="${point.id===active}"><button class="destination-option" data-map-checkpoint="${key}" aria-pressed="${point.id===active}" ${busy?'disabled':''}><strong>${name}</strong><small>${Math.round(point.lapDistance)} m</small></button><button class="button row-action" data-checkpoint-action="${quick?'copy':'delete'}" data-id="${key}" aria-label="${quick?'Save a copy of':'Delete'} ${name}" ${busy||checkpointMutation?'disabled':''}>${quick?'+':'−'}</button></div>`;
    }).join('') || '<p class="small-note">No captures for this layout'+(quick?' and car':'')+'.</p>';
    const container = $(id);
    if (container.dataset.rows !== rows) {
      const focused = container.contains(document.activeElement) ? {...document.activeElement.dataset} : null;
      container.innerHTML = rows;
      container.dataset.rows = rows;
      if(focused) [...container.querySelectorAll('button')].find(b=>
        focused.mapCheckpoint ? b.dataset.mapCheckpoint===focused.mapCheckpoint : b.dataset.id===focused.id)?.focus({preventScroll:true});
    }
  }
  $('practiceUndo').hidden = !checkpointUndoToken;
  $('clearQuickButton').disabled = busy || !visibleCheckpoints.some(point=>eligibleQuick(point));
  updateLapGuards();
  if (labPage === 'practice') renderPracticeMap();
}

let practiceMapKey = '', practiceMapTargets = [], practiceMapProject;
function moveRecordedMarker(marker) {
  const point = timelineSelection && selectedLapPoint;
  marker.setAttribute('visibility', point ? 'visible' : 'hidden');
  if (!point) return;
  const [x,y] = practiceMapProject({x:point.X,z:point.Z});
  marker.setAttribute('cx', x);
  marker.setAttribute('cy', y);
}
function renderPracticeMap() {
  const lapTrack = previewLap?.Identity.Track, liveTrack = destinationState.session?.track;
  const track = lapTrack || liveTrack;
  const id = mappedLayout(track), map = circuitMaps.get(id);
  const visible = normalizeTrack(track) === normalizeTrack(liveTrack) ? visibleCheckpoints : [];
  const pins = visible.filter(p=>Number.isFinite(p.x)&&Number.isFinite(p.z));
  const key = JSON.stringify([id,!!map, previewLap?.Id,lapPoints.length,$('cp').value,pins]);
  const element = $('practiceMap');
  const marker = element.querySelector('.recorded-marker');
  if (key === practiceMapKey && marker) { moveRecordedMarker(marker); return; }
  practiceMapKey = key;
  closeNearbyTargets();
  practiceMapTargets = [];
  if (id && !map) renderCircuitMap(element,id,[],null);
  const road = (map?.points || []).filter(p=>p.type===0 || p.type===1);
  const frames = lapPoints.map((p,index)=>({x:p.X,z:p.Z,point:p,index}));
  const bounds = road.length ? road : frames.length ? frames : pins;
  if (!bounds.length) {
    if(!id || map) element.innerHTML='<div class="map-empty">Circuit geometry appears when an exact layout or recorded lap is available.</div>';
    return;
  }
  const project = mapProjection(bounds);
  practiceMapProject = project;
  const path = points => points.map(p=>project(p).join(',')).join(' ');
  practiceMapTargets = pins.map(p=>({x:p.x,z:p.z,id:p.id,label:p.name,kind:p.quick?'quick':'saved'}));
  practiceMapTargets.push(...frames);
  element.innerHTML = `<svg viewBox="0 0 600 340" role="img" aria-label="Circuit map with checkpoint positions and recorded lap">
    <polygon class="map-road-shadow" points="${path(road.filter(p=>p.type===0))}"/><polygon class="map-road" fill="none" points="${path(road.filter(p=>p.type===0))}"/>
    <polyline class="map-pit" points="${path(road.filter(p=>p.type===1))}"/><polyline class="recorded-line" points="${path(frames)}"/>
    ${pins.map((p,i)=>`<g class="combined-pin ${p.quick?'quick-pin':'saved-pin'} ${$('cp').value===p.id?'selected':''}" transform="translate(${project(p).join(' ')})"><title>${escapeHtml(p.name)}</title><circle r="10"/><text y="4" text-anchor="middle">${i+1}</text></g>`).join('')}
    <circle class="recorded-marker" r="9" visibility="hidden"/></svg>`;
  moveRecordedMarker(element.querySelector('.recorded-marker'));
  practiceMapTargets = practiceMapTargets.map(p=>({...p, xy:project(p)}));
}

let nearbyFocus;
function closeNearbyTargets(restore = false) {
  if ($('nearbyTargets').hidden) return;
  $('nearbyTargets').hidden = true;
  if (restore) (nearbyFocus?.isConnected && nearbyFocus !== document.body ? nearbyFocus : $('timelineSession').disabled ? $('practiceMapStage') : $('timelineSession')).focus();
}

$('practiceMapStage').tabIndex = -1;
$('practiceMap').addEventListener('click', event => {
  if (destinationState.busy || placementSubmitting) return;
  const svg = event.target.closest('svg');
  if (!svg) return;
  const matrix = svg.getScreenCTM(), click = new DOMPoint(event.clientX,event.clientY);
  const nearby = practiceMapTargets.map(target=>{
    const screen = new DOMPoint(...target.xy).matrixTransform(matrix);
    return {...target,distance:Math.hypot(click.x-screen.x,click.y-screen.y)};
  }).filter(target=>target.distance<=14);
  const candidates = nearby.filter(target=>target.id);
  const passes = [];
  let previousIndex = -2;
  for (const frame of nearby.filter(target=>target.point)) {
    if (frame.index !== previousIndex+1) passes.push(frame);
    else if (frame.distance < passes.at(-1).distance) passes[passes.length-1] = frame;
    previousIndex = frame.index;
  }
  candidates.push(...passes.map(frame=>({...frame,kind:'lap',label:'Lap '+previewLap.Number+' · '+lapTime(frame.point.Elapsed)})));
  const choose = target => target.id ? selectMapCheckpoint(target.id) : selectLapPoint(target.point);
  closeNearbyTargets();
  if (candidates.length===1) { choose(candidates[0]); return; }
  if (!candidates.length) return;
  const picker = $('nearbyTargets'), rect = $('practiceMapStage').getBoundingClientRect();
  nearbyFocus = document.activeElement;
  picker.replaceChildren(...candidates.map(candidate=>{
    const button=document.createElement('button');
    button.className='button';
    button.dataset.kind=candidate.kind;
    const type=document.createElement('small');
    type.textContent={quick:'Quick capture',saved:'Saved checkpoint',lap:'Recorded lap'}[candidate.kind];
    button.append(type,document.createTextNode(candidate.label));
    button.addEventListener('click',()=>{closeNearbyTargets(true);choose(candidate);});
    return button;
  }));
  picker.hidden=false;
  picker.style.left=Math.max(0,Math.min(click.x-rect.left,rect.width-picker.offsetWidth))+'px';
  picker.style.top=Math.max(0,Math.min(click.y-rect.top,rect.height-picker.offsetHeight))+'px';
  picker.firstElementChild.focus();
});
document.addEventListener('click',event=>{if(!event.target.closest('#practiceMapStage'))closeNearbyTargets();});
document.addEventListener('keydown',event=>{if(event.key==='Escape')closeNearbyTargets(true);});
function selectMapCheckpoint(id){
  if(labState?.busy||placementSubmitting)return;
  const cp=visibleCheckpoints.find(c=>c.id===id);
  if(!cp||normalizeTrack(cp.track)!==normalizeTrack(labState?.session?.track)){notify('Load the checkpoint’s track before selecting it.',true);return;}
  $('cp').value=id;updateDestination();renderCheckpoints(labState);if(labPage!=='practice')setPage('practice');
}
async function manageCheckpoint(action,id){
  if(checkpointMutation||labState?.busy||placementSubmitting)return;
  const cp=(labState?.checkpoints||[]).find(c=>c.id===id);
  if(action!=='delete'){
    checkpointEdit={action,id};$('checkpointEditTitle').textContent=action==='copy'?'Save checkpoint copy':action==='capture'?'Save current position':'Rename checkpoint';
    $('checkpointEditName').value=action==='copy'?'':cp?.name||'';$('checkpointEditError').textContent='';$('checkpointEdit').showModal();$('checkpointEditName').focus();return;
  }
  checkpointMutation=true;updateWorkspace();
  try{
    const result=await request('/api/checkpoints/manage',{action,id});checkpointUndoToken=result.token;
    $('checkpointUndoText').textContent=(cp?.name||'Checkpoint')+' deleted. Undo is available until the next deletion or app restart.';
    if($('cp').value===id){$('cp').value='';practicePreferences.checkpoint='';savePracticePreferences();}
    await refresh();renderLibrary();notify('Checkpoint deleted. Undo is available.');
  }catch(e){notify(e.message,true);}finally{checkpointMutation=false;updateWorkspace();}
}
async function undoCheckpoint(){
  if(!checkpointUndoToken||checkpointMutation)return;
  checkpointMutation=true;
  try{await request('/api/checkpoints/manage',{action:'undo',token:checkpointUndoToken});checkpointUndoToken='';await refresh();renderLibrary();notify('Checkpoint restored.');}
  catch(e){notify(e.message,true);}finally{checkpointMutation=false;updateWorkspace();}
}

function updatePresetTab(){
  $('officialPresets').hidden=presetSource!=='official';$('savedPlans').hidden=presetSource!=='personal';
  $('presetsPanel').dataset.presetSource=presetSource;
  $('officialPresets').previousElementSibling.hidden=presetSource!=='official';
  $('officialPresets').previousElementSibling.previousElementSibling.hidden=presetSource!=='official';
  $('savedPlans').previousElementSibling.hidden=presetSource!=='personal';
  for(const b of document.querySelectorAll('[data-preset-tab]')){b.classList.toggle('active',b.dataset.presetTab===presetSource);b.setAttribute('aria-pressed',String(b.dataset.presetTab===presetSource));}
}
renderPlans=function(){
  const plans=labPreferences.plans.filter(p=>`${p.name} ${savedCarModel(p.target.car)} ${p.target.display?.track||''}`.toLowerCase().includes(presetQuery.toLowerCase()));
  $('savedPlans').innerHTML=plans.length?'<div class="preset-table-head"><span>Preset</span><span>Car / circuit</span><span>Actions</span></div>'+plans.map(p=>`<article class="saved-plan preset-table-row"><div><h3><button class="preset-name" data-inspect-preset="${escapeHtml(p.id)}">${escapeHtml(p.name)}</button></h3><small>${Object.entries(p.target.amounts).filter(([,v])=>v>0).map(([s])=>SESSION_NAMES[s]).join(' · ')}</small></div><div class="preset-content"><strong>${escapeHtml(p.target.display?.track||'Saved circuit')}</strong><small>${escapeHtml(savedCarModel(p.target.car))}</small></div><div class="preset-actions"><button class="button" data-inspect-preset="${escapeHtml(p.id)}">Details</button><button class="button primary" data-plan="${escapeHtml(p.id)}">Review & apply</button></div></article>`).join(''):'<p class="empty-note">No matching personal presets. Save the current session to reuse it later.</p>';
  updateLabControls();updatePresetTab();
};
function targetRows(target){
  const rows=[['Car',savedCarModel(target.car)],['Circuit',target.display?.track||target.track||'Not saved'],...Object.entries(target.amounts||{}).map(([s,v])=>[SESSION_NAMES[s],!v?'Disabled':s==='RACE'?raceLengthSummary(target.settings,target.display):v+' session'+(v===1?'':'s')])];
  return '<dl class="preset-values">'+rows.map(([k,v])=>`<div><dt>${escapeHtml(k)}</dt><dd>${escapeHtml(v)}</dd></div>`).join('')+'</dl><details open><summary>Rules & environment</summary><dl class="preset-values">'+Object.entries(target.settings||{}).filter(([k])=>![RACE_MODE,RACE_MINUTES,RACE_LAPS].includes(k)).map(([k,v])=>`<div><dt>${escapeHtml(label(k))}</dt><dd>${escapeHtml(settingChoice(k,v)||target.display?.[k]||settingDisplay(k,v))}</dd></div>`).join('')+'</dl></details><details><summary>Weather timelines</summary>'+Object.entries(target.weather||{}).map(([s,points])=>`<h3>${SESSION_NAMES[s]}</h3>${Object.entries(points).map(([n,fields])=>`<p><strong>${n.replace('NODE_','')}</strong> · ${Object.entries(fields).map(([k,v])=>escapeHtml(label(k))+': '+escapeHtml(settingDisplay(k,v))).join(' · ')}</p>`).join('')}`).join('')+'</details>';
}
function inspectPreset(id){
  const personal=labPreferences.plans.find(p=>p.id===id);inspectedPreset=personal?{kind:'personal',id}:null;
  $('presetKind').textContent='PERSONAL SESSION PRESET';
  if(!personal)return;
  $('presetDetailsTitle').textContent=personal.name;$('presetDetailsBody').innerHTML=targetRows(personal.target);
  $('presetDetailsActions').innerHTML=`<button class="button primary" data-load-builder="${escapeHtml(id)}">Review & load into builder</button><button class="button" data-duplicate-preset="${escapeHtml(id)}">Duplicate</button><button class="button" data-export-preset="${escapeHtml(id)}">Export one</button><button class="button quiet" data-rename-plan="${escapeHtml(id)}">Rename</button><button class="button quiet" data-replace-plan="${escapeHtml(id)}" ${!setup?'disabled':''}>Update from session</button><button class="button critical" data-delete-plan="${escapeHtml(id)}">Delete</button>`;
  $('presetDetails').showModal();
}
async function duplicatePreset(id){
  if(labPreferences.plans.length>=40){notify('You can save up to 40 personal presets.',true);return;}
  const preset=labPreferences.plans.find(p=>p.id===id);if(!preset)return;
  const duplicate=clone(preset);duplicate.id=crypto.randomUUID();duplicate.name=(preset.name.slice(0,55)+' copy').slice(0,60);duplicate.target.assists={};
  labPreferences.plans.push(duplicate);if(!await persistPreferences())labPreferences.plans=labPreferences.plans.filter(p=>p.id!==duplicate.id);
  renderPlans();
}
const renderEvents=renderOfficialSessions;
renderOfficialSessions=function(){renderEvents();renderOfficialWorkspace();};
let featuredEventId = '', featuredEventMarkup = '', featuredEventArt = '', eventBoardMarkup = '';
const eventGroups = {beginner:'Bronze',intermediate:'Silver',advanced:'Gold',weekly:'Weekly',special:'Special'};
function catalogueLayout(scene) {
  return allMapTracks().find(track => scene && track.sceneDesc?.toLowerCase()===scene.toLowerCase());
}
function artwork(kind,key,fallback='',lazy=false) {
  const source=key||fallback;
  return source ? `<img src="/api/artwork?kind=${kind}&amp;key=${encodeURIComponent(source)}" alt="" decoding="async"${lazy?' loading="lazy"':''}${fallback&&fallback!==source?` data-fallback="/api/artwork?kind=${kind}&amp;key=${encodeURIComponent(fallback)}"`:''} onerror="if(this.dataset.fallback){this.src=this.dataset.fallback;delete this.dataset.fallback}else{this.hidden=true}">` : '';
}
function carArtKey(car) {
  return car?.vehFile?.split(/[/\\]/).at(-1)?.replace(/\.veh$/i,'');
}
const eventOutlineMaps=new WeakMap();
function renderEventOutline(element) {
  if(element.closest('[hidden]'))return;
  const layout = catalogueLayout(element.dataset.outline);
  if(!layout){
    element.replaceChildren();
    eventOutlineMaps.delete(element);
    return;
  }
  const map=circuitMaps.get(layout.id);
  if(map&&eventOutlineMaps.get(element)===map&&element.dataset.renderedLayout===layout.id)return;
  renderCircuitMap(element,layout.id,[],null);
  element.dataset.renderedLayout=layout.id;
  eventOutlineMaps.set(element,map);
}
function renderOfficialWorkspace(){
  const categories = [['all','All'],['daily','Daily'],['weekly','Weekly'],['special','Special']];
  const filters = $('eventFilters');
  if (!filters.childElementCount) filters.innerHTML = categories.map(([id,label]) => `<button data-event-category="${id}">${label}</button>`).join('');
  for (const filter of filters.children) {
    const active = filter.dataset.eventCategory === eventCategory;
    filter.classList.toggle('active',active);
    filter.setAttribute('aria-pressed',String(active));
  }
  const events = officialSessions.events || [];
  const visibleGroups = Object.keys(eventGroups).filter(category=>eventCategory==='all'||
    (eventCategory==='daily' ? !['weekly','special'].includes(category) : category===eventCategory));
  const visible = visibleGroups.flatMap(category=>events.filter(event=>event.category===category));
  const selected = visible.find(event=>event.id===featuredEventId) || visible[0];
  featuredEventId = selected?.id || '';
  const eventPhoto = (e,lazy) => artwork('track-card',officialScene(e),officialVenue(e).normalize('NFD').replace(/[^a-z0-9]/gi,''),lazy);
  const featured = $('officialFeatured');
  featured.hidden = !selected;
  const featureArt = selected ? eventPhoto(selected) : '';
  if(featureArt!==featuredEventArt){
    featured.querySelector('.event-photo').innerHTML=featureArt;
    featuredEventArt=featureArt;
  }
  const featureMarkup = selected ? `<p class="rank-label">${escapeHtml(OFFICIAL_CATEGORIES[selected.category] || 'Official')}${selected.category==='beginner'?' · Open to all':''}</p>
    <h2>${escapeHtml(selected.title)}</h2><p>${escapeHtml(officialTrack(selected))} · ${escapeHtml(officialNextTime(selected))}</p>
    ${selected.stale?'<p class="small-note">Saved event · refresh unavailable</p>':''}<div class="button-group">${officialButtons(selected)}</div>` : '';
  if(featureMarkup!==featuredEventMarkup){
    featured.querySelector('.event-feature-copy').innerHTML=featureMarkup;
    featuredEventMarkup=featureMarkup;
  }
  featured.querySelector('.event-outline').dataset.outline=selected?officialScene(selected):'';
  featured.dataset.category = selected?.category || '';
  const board = $('officialWorkspaceEvents');
  const activeId = board.contains(document.activeElement) ? document.activeElement.dataset.eventId : '';
  const boardMarkup = Object.entries(eventGroups).map(([category,label])=>{
    const list = events.filter(event=>event.category===category);
    return list.length ? `<section class="event-group" data-category="${category}"><h2>${label}</h2><div class="event-tiles">${list.map(e=>`<button class="button event-tile" data-event-id="${escapeHtml(e.id)}"><span class="event-photo" aria-hidden="true">${eventPhoto(e,true)}</span><span class="event-outline" data-outline="${escapeHtml(officialScene(e))}" aria-hidden="true"></span><span><strong>${escapeHtml(e.title)}</strong><small>${escapeHtml(officialTrack(e))}</small><small class="event-time"></small></span></button>`).join('')}</div></section>` : '';
  }).join('')+'<p class="empty-note">No cached events in this category.</p>';
  if(boardMarkup!==eventBoardMarkup){
    board.innerHTML=boardMarkup;
    eventBoardMarkup=boardMarkup;
    if(activeId) [...board.querySelectorAll('.event-tile')].find(button=>button.dataset.eventId===activeId)?.focus({preventScroll:true});
  }
  board.querySelector('.empty-note').hidden=visible.length>0;
  for(const group of board.querySelectorAll('.event-group'))group.hidden=!visibleGroups.includes(group.dataset.category);
  const byId=new Map(events.map(event=>[event.id,event]));
  for(const tile of board.querySelectorAll('.event-tile')){
    const event=byId.get(tile.dataset.eventId);
    const pressed=String(event.id===featuredEventId);
    if(tile.getAttribute('aria-pressed')!==pressed)tile.setAttribute('aria-pressed',pressed);
    const time=tile.querySelector('.event-time');
    const label=officialNextTime(event)+(event.stale?' · Saved':'');
    if(time.textContent!==label)time.textContent=label;
  }
  if(labPage==='official') for(const outline of $('officialPanel').querySelectorAll('[data-outline]'))renderEventOutline(outline);
  updatePresetTab();
  for(const row of $('officialPresets').querySelectorAll('.saved-plan')){
    const id=row.querySelector('[data-official-preset]')?.dataset.officialPreset;
    if(id&&!row.querySelector('[data-inspect-official]'))row.querySelector('.preset-actions').insertAdjacentHTML('afterbegin',`<button class="button quiet" data-inspect-official="${escapeHtml(id)}">Details</button>`);
  }
}

function inspectOfficial(id){
  for(const event of officialSessions.events||[])for(const session of event.sessions||[])if(officialPresetId(event,session)===id){
    inspectedPreset={kind:'official',id};$('presetKind').textContent='OFFICIAL SOURCE · READ ONLY';$('presetDetailsTitle').textContent=event.title+' · '+session.type;
    $('presetDetailsBody').innerHTML=`<dl class="preset-values"><div><dt>Circuit</dt><dd>${escapeHtml(officialTrack(event))}</dd></div><div><dt>Solo Practice duration</dt><dd>${escapeHtml(session.duration)}</dd></div><div><dt>Start</dt><dd>${escapeHtml(session.timeOfDay)}</dd></div><div><dt>Starting grip</dt><dd>${escapeHtml(officialRoadLabel(session))}</dd></div></dl><h3>Five-point forecast</h3>${officialForecast(session)}<p class="small-note">Applies solo Practice with zero AI. Online BoP, fixed setup, tyre allocation and traffic-created surface changes are not reproduced. Apply, then save as a personal preset to keep edits.</p>`;
    $('presetDetailsActions').innerHTML=`<button class="button primary" data-official-preset="${escapeHtml(id)}">Review & apply to builder</button>`;$('presetDetails').showModal();return;
  }
}

function toggleSummary(force){
  const wasOpen=document.body.classList.contains('summary-open'),open=force??!wasOpen;
  if(open===wasOpen)return;
  document.body.classList.toggle('summary-open',open);
  $('summaryToggle').setAttribute('aria-expanded',String(open));
  $('summaryShade').hidden=!open;
  $('editor').inert=open;
  if(open)$('refreshLabButton').focus();
  else if($('sessionDock').contains(document.activeElement)||document.activeElement===$('summaryShade'))$('summaryToggle').focus();
}
function summaryWidth(value,save=false){const width=Math.round(Math.max(240,Math.min(440,value)));document.documentElement.style.setProperty('--summary-width',width+'px');$('summaryHandle').setAttribute('aria-valuenow',String(width));if(save){labPreferences.summaryWidth=width;persistPreferences();}}
$('summaryHandle').addEventListener('pointerdown',e=>{
  const start=e.clientX,width=Number($('summaryHandle').getAttribute('aria-valuenow'));
  $('summaryHandle').setPointerCapture(e.pointerId);
  const move=event=>summaryWidth(width+start-event.clientX);
  const up=()=>{$('summaryHandle').removeEventListener('pointermove',move);$('summaryHandle').removeEventListener('pointerup',up);summaryWidth(Number($('summaryHandle').getAttribute('aria-valuenow')),true);};
  $('summaryHandle').addEventListener('pointermove',move);$('summaryHandle').addEventListener('pointerup',up);
});
$('summaryHandle').addEventListener('keydown',e=>{if(!['ArrowLeft','ArrowRight'].includes(e.key))return;e.preventDefault();summaryWidth(Number($('summaryHandle').getAttribute('aria-valuenow'))+(e.key==='ArrowLeft'?16:-16),true);});
$('summaryToggle').addEventListener('click',()=>toggleSummary());$('summaryShade').addEventListener('click',()=>toggleSummary(false));
$('builderSave').addEventListener('click',()=>$('savePlanButton').click());
$('capBtn').addEventListener('click',()=>manageCheckpoint('capture'));
$('practiceUndo').addEventListener('click',undoCheckpoint);
$('undoCheckpointDelete').addEventListener('click',undoCheckpoint);$('refreshCircuitMap').addEventListener('click',refreshCircuitMap);
$('checkpointEditForm').addEventListener('submit',async e=>{
  e.preventDefault();if(!checkpointEdit||checkpointMutation)return;
  checkpointMutation=true;const button=e.submitter||$('checkpointEditForm').querySelector('[type=submit]');button.disabled=true;
  try{
    const {action,id:old}=checkpointEdit,name=$('checkpointEditName').value.trim();
    const renameActive=action==='rename'&&$('cp').value===old;
    const result=await request(action==='capture'?'/api/capture':'/api/checkpoints/manage',{...checkpointEdit,name});
    if(checkpointEdit.action==='rename')labPreferences.checkpoints=labPreferences.checkpoints.map(id=>id===old?result.id:id);
    await persistPreferences();await refresh();
    if(renameActive){$('cp').value=result.id;updateDestination();}
    libraryPreviewId=result.id;$('checkpointEdit').close();renderLibrary();renderPracticeManagement();notify('Checkpoint saved. Original capture data preserved.');
  }catch(error){$('checkpointEditError').textContent=error.message;}finally{checkpointMutation=false;button.disabled=false;updateWorkspace();}
});
document.addEventListener('click',e=>{
  const b=e.target.closest('button,[data-map-checkpoint],[data-preview-checkpoint]');if(!b||b.disabled)return;
  if(b.dataset.libraryTrack){libraryTrack=b.dataset.libraryTrack;libraryPreviewId='';renderLibrary();}

  if(b.dataset.previewCheckpoint){libraryPreviewId=b.dataset.previewCheckpoint;renderLibrary();}
  if(b.dataset.mapCheckpoint)selectMapCheckpoint(b.dataset.mapCheckpoint);
  if(b.dataset.checkpointAction)manageCheckpoint(b.dataset.checkpointAction,b.dataset.id);
  if(b.hasAttribute('data-undo-checkpoint'))undoCheckpoint();
  if(b.dataset.presetTab){presetSource=b.dataset.presetTab;updatePresetTab();}
  if(b.dataset.inspectPreset)inspectPreset(b.dataset.inspectPreset);
  if(b.dataset.inspectOfficial)inspectOfficial(b.dataset.inspectOfficial);
  if(b.dataset.officialPreset||b.dataset.renamePlan||b.dataset.replacePlan||b.dataset.deletePlan)$('presetDetails').close();
  if(b.dataset.duplicatePreset)duplicatePreset(b.dataset.duplicatePreset);
  if(b.dataset.exportPreset){const p=labPreferences.plans.find(p=>p.id===b.dataset.exportPreset);if(p)download('inactive-reset-preset.json',JSON.stringify({schema:1,plans:[p]},null,2),'application/json');}
  if(b.dataset.loadBuilder){const p=labPreferences.plans.find(p=>p.id===b.dataset.loadBuilder);if(p){$('presetDetails').close();setPage('setup','selection');if(reviewPlan(p.name,planIntents(p.target),undefined,true))pendingPlan.source={kind:'personal',id:p.id,name:p.name};}}
  if(b.hasAttribute('data-update-builder')&&builderPreset?.kind==='personal')editPreset(builderPreset.id,true);
  if(b.hasAttribute('data-restore-builder')&&builderPreset){
    if(reviewPlan('Restore '+builderPreset.name,builderPreset.intents||planIntents(builderPreset.baseline),undefined,true))pendingPlan.source=builderPreset;
  }
  if(b.dataset.eventId){featuredEventId=b.dataset.eventId;renderOfficialWorkspace();}
  if(b.dataset.eventCategory){eventCategory=b.dataset.eventCategory;renderOfficialWorkspace();}
});
document.addEventListener('change',e=>{
  if(e.target.id==='libraryCar'){libraryCar=e.target.value;renderLibrary();}
  if(e.target.id==='mapLayout'){labPreferences.mapLayouts||={};labPreferences.mapLayouts[libraryTrack]=e.target.value;persistPreferences();renderLibrary();}
});
document.addEventListener('input',e=>{if(e.target.id==='circuitSearch'){circuitQuery=e.target.value;renderLibrary();}});
document.addEventListener('keydown',e=>{
  if(e.key==='Escape')toggleSummary(false);
  if(['Enter',' '].includes(e.key)&&e.target.matches('g[role=button]')){e.preventDefault();e.target.dispatchEvent(new MouseEvent('click',{bubbles:true}));}
});
const savePlanToLmu=applyPendingPlan;
applyPendingPlan=async function(){
  if(!menuReady&&pendingPlan?.allowDraft&&builderEditReady()&&pendingPlan.allIntents.length){
    stageBuilderPreset(pendingPlan);
    return;
  }
  const source=pendingPlan?.source;await savePlanToLmu();
  if(source&&!$('planDialog').open&&setup&&!cancelPlan){builderDraft=clone(setup);builderBaseline=setup;raceDurationChosen=setup.amounts.RACE>0;builderEdits.clear();automaticStarts.clear();builderStatus='Session applied. Ready to start.';renderContent();}
  if(source&&!$('planDialog').open&&setup&&!cancelPlan){builderPreset={...source,intents:pendingPlan.allIntents,baseline:liveSnapshot()};setPage('setup','selection');}
};
// The listener was installed before this file loaded; route it through the current function.
$('applyPlanButton').removeEventListener('click',savePlanToLmu);$('applyPlanButton').addEventListener('click',()=>applyPendingPlan());
document.querySelector('.dock-heading').insertAdjacentHTML('afterend','<div id="summaryMap" class="circuit-map summary-map"></div>');
request('/api/track-maps').then(result=>{savedMapTracks=result.tracks||[];if(labPage==='library')renderLibrary();}).catch(()=>{});
const voicePreference = $('practiceVoice');
voicePreference.checked = localStorage.getItem('practice-voice') === 'on';
voicePreference.addEventListener('change', () => {
  localStorage.setItem('practice-voice', voicePreference.checked ? 'on' : 'off');
  if (!voicePreference.checked) globalThis.speechSynthesis?.cancel();
});
for (const wheel of TYRE_WHEELS) {
  for (const [field,min,max,unit] of [['Condition',0,100,'%'],['Temperature',0,150,'°C']]) {
    const input = $('tyre'+field+wheel);
    input.addEventListener('input', updateTyreSettings);
    input.addEventListener('change', () => {
      const raw = input.value;
      const adjustment = $('tyreAdjustment');
      if (raw !== '' && (Number(raw) < min || Number(raw) > max)) {
        const value = Math.min(max, Math.max(min, Number(raw)));
        input.value = value;
        adjustment.textContent = wheel + ' ' + field.toLowerCase() + ' adjusted to ' + (field==='Temperature' && value===0 ? 'Game' : value+unit) + '.';
      }
      if (field==='Temperature' && input.value === '0') input.value = '';
      updateTyreSettings();
    });
  }
}

document.addEventListener('wheel', event => {
  const rail = event.target.closest('.horizontal-rail');
  if (!rail || Math.abs(event.deltaX) >= Math.abs(event.deltaY) || event.ctrlKey) return;
  const previous = rail.scrollLeft;
  rail.scrollLeft += event.deltaY * (event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? rail.clientWidth : 1);
  if (rail.scrollLeft !== previous) event.preventDefault();
}, {passive:false});

let builderDraft = null, builderBaseline = null, builderStatus = '', confirmBuilderReload = false;
let keepDraftAfterReloadFailure = false;
function copyBuilderWeather(){
  const source=builderDraft.weather[weatherSession];
  const fields=Object.entries(source?.[weatherNode] || {}).sort(([a],[b])=>Number(b==='WNV_SKY')-Number(a==='WNV_SKY'));
  for(const node of Object.keys(source||{}))if(node!==weatherNode)for(const [key,field] of fields)
    editBuilder({action:'weather',session:weatherSession,node,key,value:field.currentValue},false);
  renderContent();
}
const builderEdits = new Map();
let raceDurationChosen = false;
function discardBuilderDraft(whenDisconnected=false){
  confirmBuilderReload=false;
  $('builderReset').textContent='Reload from LMU';
  if(whenDisconnected&&keepDraftAfterReloadFailure&&builderEdits.size)return;
  builderDraft=null;builderBaseline=null;builderEdits.clear();automaticStarts.clear();builderStatus='';builderPreset=null;
  keepDraftAfterReloadFailure=false;
}
const builderEditReady = () => canEditSessionDraft() && !saving && !starting && !serverBusy && !applyingPlan;
const builderMenuReady = () => menuReady && builderEditReady();
const builderBounds = (field,min,max) => [
  Number.isFinite(field?.minValue) ? field.minValue : min,
  Number.isFinite(field?.maxValue) ? field.maxValue : max
];
const CONTROL_RANGES = {
  SESSSET_AI_Strength:[75,105], SESSSET_Damage_Multi:[0,100],
  SESSSET_race_timescale:[-1,60], SESSSET_Grid_Position:[0,null],
  SESSSET_recon_pit_closed:[30,900], SESSSET_recon_pit_open:[30,900],
  WNV_TEMPERATURE:[-20,60], WNV_RAIN_CHANCE:[0,100], WNV_HUMIDITY:[0,100], WNV_WINDSPEED:[0,100]
};
function controlRange(key,field){
  if(key===RACE_MINUTES)return [1,2000];
  const step=field.stepValue>0?field.stepValue:1;
  const choices=SETTING_CHOICES[key];
  const numeric=NUMERIC.has(key)||CONTROL_RANGES[key]||Number.isFinite(field.minValue);
  const [lower,upper]=CONTROL_RANGES[key]||(choices?[0,choices.length-1]:[numeric?0:-10000,null]);
  const minimum=Number.isFinite(field.minValue)?field.minValue:lower;
  const inferred=Number.isFinite(field.numStepsTotal)&&field.numStepsTotal>=0&&field.numStepsTotal<=10000
    ? minimum+field.numStepsTotal*step : 10000;
  return builderBounds(field,lower,choices?Math.min(upper,inferred):upper??(numeric?inferred:10000));
}
function builderSettingControl(key,field,action='setting',extra={},strengthId=''){
  const title=escapeHtml(label(key)), body={action,key,...extra}, data=keyData(body);
  const legacyRace=key===RACE_MINUTES&&setup.settings[RACE_MODE]?.currentValue!==TIMED_RACE;
  const value=legacyRace?'':field.currentValue, choices=SETTING_CHOICES[key];
  if(choices){
    const [min,max]=controlRange(key,field);
    return `<select class="setting-choice" aria-label="${title}" data-builder-choice="${data}">${choices[value]?'':option(value,field.stringValue||'Current LMU option',true,true)}${choices.map((name,index)=>index>=min&&index<=max?option(index,name,index===value):'').join('')}</select>`;
  }
  if(!NUMERIC.has(key)&&!CONTROL_RANGES[key]&&!Number.isFinite(field.minValue))return stepper(key,field,action,extra);
  const [min,max]=controlRange(key,field), step=key!==RACE_MINUTES&&field.stepValue>0?field.stepValue:1,unit=settingUnit(key);
  const id=Object.values(lengthKey).includes(key)?` id="value-${key}"`:'';
  const number=`<div class="stepper builder-number"><button type="button" aria-label="Decrease ${title}" data-builder-step="${keyData({...body,steps:-1})}" ${!legacyRace&&value<=min?'disabled':''}>−</button><input${id} type="number" aria-label="${title}" value="${escapeHtml(value)}" min="${min}" max="${max}" step="${step}" data-builder-number="${data}">${unit?`<span class="unit">${unit}</span>`:''}<button type="button" aria-label="Increase ${title}" data-builder-step="${keyData({...body,steps:1})}" ${value>=max?'disabled':''}>+</button></div>`;
  return key==='SESSSET_AI_Strength'?`<div class="quick-ai-strength"><input ${strengthId?`id="${strengthId}"`:''} type="range" aria-label="AI strength slider" data-builder-range="${key}" min="${min}" max="${max}" value="${value}" step="${step}">${number}</div>`:number;
}
function draftControlField({action,key,session,node}){
  return action==='weather'?builderDraft?.weather[session]?.[node]?.[key]:builderDraft?.settings[key];
}
function setDraftControl(body,value){
  const field=draftControlField(body);
  const race=body.key===RACE_MINUTES;
  if(!field||!Number.isFinite(value)||!builderEditReady())return race&&builderDraft?.settings[RACE_MODE]?.currentValue!==TIMED_RACE?'':field?.currentValue;
  const [min,max]=controlRange(body.key,field),step=!race&&field.stepValue>0?field.stepValue:1;
  const bounded=Math.min(max,Math.max(min,Number((min+Math.round((value-min)/step)*step).toFixed(8))));
  editBuilder(race?{action:'raceMinutes',value:bounded}:{...body,value:bounded});
  return bounded;
}

function makeBuilderDraft() {
  builderBaseline = setup;
  builderDraft = clone(setup);
  raceDurationChosen = setup.amounts.RACE>0;
  builderEdits.clear();
  automaticStarts.clear();
  for(const stage of stagesEnabled().slice(1))
    if(builderDraft.settings[clockKey[stage]]?.currentValue===-1)automaticStarts.add(stage);
  const count = builderDraft.settings.SESSSET_Num_Opponents;
  if (count && count.currentValue !== 0) {
    count.currentValue = 0;
    count.stringValue = '';
    builderEdits.set('setting:SESSSET_Num_Opponents',{type:'setting',key:'SESSSET_Num_Opponents',value:0});
  }
  updateSequentialStarts();
}
function trackDefaultMinutes(field){
  if(field?.currentValue!==-1)return null;
  const match=/\b(\d{1,2}):(\d{2})(?:\s*([AP]M))?\b/i.exec(field?.stringValue||'');
  if(!match)return null;
  const [,hours,minutes,period]=match;
  return (period?Number(hours)%12+(period.toUpperCase()==='PM'?12:0):Number(hours))*60+Number(minutes);
}
function updateSequentialStarts(){
  let next=null;
  const originalTrack=builderDraft.selection.trackInfo?.id===builderBaseline.selection.trackInfo?.id;
  for(const stage of Object.keys(SESSION_NAMES).filter(stage=>builderDraft.amounts[stage]>0)){
    const key=clockKey[stage], field=builderDraft.settings[key];
    if(!field){next=null;continue;}
    if(automaticStarts.has(stage)){
      const time=next??-1;
      if(field.currentValue!==time)editBuilder({action:'setting',key,value:time},false);
    }
    const time=field.currentValue>=0?field.currentValue:
      field.currentValue===-1&&originalTrack?trackDefaultMinutes(builderBaseline.settings[key]):null;
    const duration=stage==='RACE'&&builderDraft.settings[RACE_MODE]?.currentValue!==TIMED_RACE ? null : builderDraft.settings[lengthKey[stage]]?.currentValue;
    next=time!==null&&duration>0?(time+duration+5)%1440:null;
  }
}
function draftValue(key,value) {
  if (!builderEditReady()) return;
  const field=builderDraft?.settings[key];
  if (!field) return;
  const [min,max] = TIME_SETTINGS.has(key) ? [-3,1439] : builderBounds(field,0,10000);
  if (!Number.isInteger(value)||value<min||value>max) {
    builderStatus='Enter a value between '+min+' and '+max+'.';
    renderContent();
    return;
  }
  const stage=Object.keys(clockKey).find(stage=>clockKey[stage]===key);
  if(stage)automaticStarts.delete(stage);
  if(key===RACE_MINUTES)setDraftControl({action:'setting',key},value);
  else editBuilder({action:'setting',key,value});
}

function editBuilder(body, repaint = true) {
  if (!builderEditReady() || !builderDraft) return;
  const {action:type,key,id,session:stage,node,steps} = body, draft=builderDraft;
  let intent = {type,key}, identity=type;
  if (type==='raceMinutes') {
    if(!draft.settings[RACE_MODE]||!draft.settings[RACE_MINUTES])return;
    draft.settings[RACE_MODE].currentValue=TIMED_RACE;
    draft.settings[RACE_MODE].stringValue='Time';
    draft.settings[RACE_MINUTES].currentValue=body.value;
    draft.settings[RACE_MINUTES].stringValue='';
    raceDurationChosen=true;
    for(const key of [RACE_MODE,RACE_MINUTES,RACE_LAPS])builderEdits.delete('setting:'+key);
    intent={type,value:body.value};
  } else if (type==='car' || type==='track') {
    const item=(type==='car'?catalog.cars:catalog.tracks).find(value=>value.id===id);
    if (!item || !(type==='car'?item.isOwned:item.owned)) return;
    draft.selection[type==='car'?'selectedCar':'trackInfo']=item;
    if(type==='track'&&builderDetail==='circuit')previewCircuit=circuitName(item);
    intent={type,id};
  } else if (type==='amount') {
    draft.amounts[stage]=body.amount;
    intent={type,session:stage,value:body.amount};
    identity+=':'+stage;
    if(repaint&&stage==='RACE'&&body.amount&&!raceDurationChosen)
      editBuilder({action:'raceMinutes',value:DEFAULT_RACE_MINUTES},false);
    if(repaint&&body.amount&&draft.settings[clockKey[stage]]?.currentValue===-1){
      const enabled=Object.keys(SESSION_NAMES).filter(stage=>draft.amounts[stage]>0);
      if(enabled.indexOf(stage)>0)automaticStarts.add(stage);
    }
  } else if (type==='grid') {
    draft.selection.classesSelection=body.classes;
    draft.selection.fullGrid=body.fullGrid;
    intent={type,classes:body.classes,fullGrid:body.fullGrid};
  } else if (type==='setting' || type==='weather') {
    const weather=type==='weather';
    const field=weather?draft.weather[stage]?.[node]?.[key]:draft.settings[key];
    if (!field) return;
    const [min,max]=controlRange(key,field);
    const value=body.value ?? Math.min(max,Math.max(min,field.currentValue+(steps===0?-1:steps)*(field.stepValue||1)));
    if([RACE_MODE,RACE_MINUTES,RACE_LAPS].includes(key)&&body.display)field.stringValue=body.display;
    else if(value!==field.currentValue)field.stringValue=settingDisplay(key,value);
    field.currentValue=value;
    if (weather) {
      intent={type,key,session:stage,node,value};
      identity+=':'+stage+':'+node+':'+key;
      if(draft.settings.SESSSET_weather) {
        draft.settings.SESSSET_weather.currentValue=4;
        draft.settings.SESSSET_weather.stringValue='Scripted';
        builderEdits.delete('setting:SESSSET_weather');
        builderEdits.set('setting:SESSSET_weather',{type:'setting',key:'SESSSET_weather',value:4});
      }
    } else {
      intent.value=value;
      identity+=':'+key;
    }
  } else if (type==='weatherPreset') {
    intent={type,session:stage,preset:body.preset};
    identity+=':'+stage;
    for (const id of builderEdits.keys()) if (id.startsWith('weather:'+stage+':')) builderEdits.delete(id);
    draft.weather[stage]=clone(builderBaseline.weather[stage]);
  } else if (type==='practiceConditions') {
    intent={type,time:body.time,road:body.road,waterDepth:body.waterDepth,loadTemperatures:body.loadTemperatures};
    const time=draft.settings.SESSSET_practice1_starting_time;
    if(time){time.currentValue=body.time;time.stringValue='';}
  } else return;
  builderEdits.delete(identity);
  builderEdits.set(identity,intent);
  builderStatus='';
  if(repaint){
    if(type==='raceMinutes'||type==='amount'||type==='track'||Object.values(clockKey).includes(key)||Object.values(lengthKey).includes(key))
      updateSequentialStarts();
    renderContent();
  }
  return true;
}

function stageBuilderPreset(plan) {
  builderBaseline=setup;
  builderEdits.clear();
  automaticStarts.clear();
  builderDraft=clone(setup);
  raceDurationChosen=false;
  for(const intent of plan.allIntents){
    if(!editBuilder({...intent,action:intent.type,amount:intent.value},false))
      builderEdits.set(Symbol(),intent);
  }
  raceDurationChosen=builderDraft.amounts.RACE>0;
  builderPreset=plan.source?{...plan.source,intents:plan.allIntents}:null;
  $('planDialog').close();
  setPage('setup','selection');
  if(plan.source)builderPreset.baseline=snapshotPlan();
  renderContent();
}
const liveChange=change;
change=function(body,confirmed){
  if(labPage==='setup'&&builderDraft){
    if(body.action==='trackDefaults')return liveChange(body,()=>{
      makeBuilderDraft();
      builderPreset=null;
      builderStatus='Circuit defaults loaded. Apply Solo before starting.';
    });
    if(body.action!=='assist')return editBuilder(body);
  }
  return liveChange(body,confirmed);
};
const liveQueue=queueSessionValue;
queueSessionValue=function(key,value){
  if(labPage==='setup'&&builderDraft) return draftValue(key,value);
  return liveQueue(key,value);
};
function builderSetting(key) {
  return (labPage==='setup'&&builderDraft?builderDraft:setup)?.settings[key];
}
const liveSnapshot=snapshotPlan;
snapshotPlan=function(){
  if(labPage!=='setup'||!builderDraft||applyingPlan)return liveSnapshot();
  const live=setup;
  setup=builderDraft;
  try{return liveSnapshot();}finally{setup=live;}
};

async function applyBuilder() {
  if (!builderEdits.size || !builderMenuReady() || !Object.values(builderDraft?.amounts || {}).some(amount=>amount>0)) return;
  const edits=[...builderEdits.values()];
  const order={track:0,car:1,amount:2,grid:3,practiceConditions:4,setting:5,raceMinutes:5,weatherPreset:6,weather:6};
  const changes=edits.sort((a,b)=>(order[a.type] ?? 4)-(order[b.type] ?? 4));
  saving=true;
  cancelPlan=false;
  builderStatus='Applying session…';
  updateControls();
  try {
    setup=await request('/api/session/setup');
    for (const intent of changes) await applyIntent(intent);
    setup=await request('/api/session/setup');
    const missing=changes.find(intent=>!['practiceConditions','weatherPreset','grid'].includes(intent.type)&&needed(intent));
    if (missing) throw Error('LMU did not confirm '+describeIntent(missing)[0]+'.');
    builderDraft=clone(setup);
    builderBaseline=setup;
    raceDurationChosen=setup.amounts.RACE>0;
    builderEdits.clear();
    keepDraftAfterReloadFailure=false;
    builderStatus='Session applied. Ready to start.';
    setSaved('Values confirmed in LMU');
  } catch(error) {
    builderStatus=error.message + ' Adjust or apply the remaining changes again.';
    try { setup=await request('/api/session/setup'); } catch {}
  } finally {
    saving=false;
    renderContent();
    renderSummary();
  }
}

document.addEventListener('input',event=>{
  const input=event.target;if(!input.dataset.builderRange)return;
  if(input.id==='quickClock'){
    $('quickClockTime').value=clockValue(Number(input.value));
    $('quickClockValue').textContent=$('quickClockTime').value;
  }else if(input.dataset.builderRange==='SESSSET_AI_Strength')input.closest('.quick-ai-strength').querySelector('[data-builder-number]').value=input.value;
});

function openBuilderDetail(id,trigger) {
  const dialog=$('builderDetail');
  if(id==='circuit')previewCircuit=circuitName(builderDraft?.selection.trackInfo);
  builderDetail=id;
  builderTrigger=trigger.dataset.builderOpen;
  renderContent();
  if(!dialog.open)dialog.showModal();
}
$('builderDetail').addEventListener('close',()=>{
  const returnTo=builderTrigger;
  builderDetail='';
  detailMarkup='';
  $('builderDetailBody').replaceChildren();
  document.querySelector('[data-builder-open="'+returnTo+'"]')?.focus();
});
$('builderApply').addEventListener('click',applyBuilder);
$('builderReset').addEventListener('click',async()=>{
  if(!builderEditReady())return;
  const button=$('builderReset');
  if(!confirmBuilderReload){
    confirmBuilderReload=true;
    button.textContent='Are you sure?';
    return;
  }
  const restoreFocus=document.activeElement===button;
  confirmBuilderReload=false;
  button.textContent='Reload from LMU';
  saving=true;
  builderStatus='Reading LMU settings…';
  updateControls();
  try{
    const current=await request('/api/session/setup');
    discardBuilderDraft();
    setup=builderBaseline=current;
    builderDraft=clone(current);
    builderStatus='Reloaded current LMU settings.';
    setSaved('In sync with LMU');
  }catch(error){
    keepDraftAfterReloadFailure=true;
    builderStatus='Could not reload from LMU. '+error.message;
  }finally{
    saving=false;
    renderContent();
    renderSummary();
    if(restoreFocus&&document.activeElement===document.body)button.focus();
  }
});
$('builderLoad').addEventListener('click',()=>{presetSource='personal';setPage('presets');});
$('builderStart').addEventListener('click',()=>{if(!builderEdits.size)startSession();});

document.addEventListener('click',event=>{
  const button=event.target.closest('button');
  if(button?.disabled)return;
  const card=event.target.closest('.builder-overview-card');
  const trigger=button?.dataset.builderOpen ? button : card && !event.target.closest('button,input,select,label') ? card.querySelector('.card-open') : null;
  if(trigger && !trigger.closest('fieldset[disabled]'))openBuilderDetail(trigger.dataset.builderOpen,trigger);
  if(button?.dataset.livery)editBuilder({action:'car',id:button.dataset.livery});
  if(button?.dataset.timingStage){timingStage=button.dataset.timingStage;renderContent();}
  if(button?.dataset.previewCircuit){previewCircuit=button.dataset.previewCircuit;renderContent();}
  if(button?.dataset.builderStep&&!button.disabled){
    const body=JSON.parse(button.dataset.builderStep),input=button.parentElement.querySelector('input');
    const value=Number.isFinite(input.valueAsNumber)?input.valueAsNumber:body.key===RACE_MINUTES?DEFAULT_RACE_MINUTES:draftControlField(body)?.currentValue;
    input.value=setDraftControl(body,value+Number(input.step)*body.steps)??'';
  }
  const choice=button?.dataset.gridChoice;
  if(choice && builderEditReady()) {
    if(choice==='solo'){draftValue('SESSSET_Num_Opponents',0);return;}
    const classes=[...new Set(catalog.cars.map(car=>parts(car)[1]).filter(Boolean).map(value=>value==='Hypercar'?'Hyper':value))];
    const wec=classes.filter(value=>['HYPER','HYPERCAR','LMP2','GT3','LMGT3'].includes(value.toUpperCase()));
    const count=builderDraft.settings.SESSSET_Num_Opponents;
    if(!count)return;
    const [,max]=builderBounds(count,0,count.numStepsTotal ?? count.currentValue);
    const seasonCars=catalog.cars.filter(car=>parts(car)[0]===parts(builderDraft.selection.selectedCar || {})[0]).length;
    const fullGrid=choice==='compact';
    editBuilder({action:'grid',classes:wec,fullGrid});
    draftValue('SESSSET_Num_Opponents',Math.max(0,Math.min(max,fullGrid?Math.floor(seasonCars/2):seasonCars-1)));
  }
});
document.addEventListener('pointerdown',event=>{
  if(event.target.closest('[data-builder-step]')?.parentElement.contains(document.activeElement))
    event.preventDefault();
});
document.addEventListener('keydown',event=>{
  if(event.key==='Enter'&&event.target.matches('[data-builder-number]')){
    event.preventDefault();
    event.target.dispatchEvent(new Event('change',{bubbles:true}));
  }
});
document.addEventListener('change',event=>{
  const input=event.target;
  if(input.dataset.builderRange){
    if(TIME_SETTINGS.has(input.dataset.builderRange))draftValue(input.dataset.builderRange,Number(input.value));
    else setDraftControl({action:'setting',key:input.dataset.builderRange},Number(input.value));
  }
  if(input.dataset.builderChoice)setDraftControl(JSON.parse(input.dataset.builderChoice),Number(input.value));
  if(input.dataset.builderNumber)input.value=setDraftControl(JSON.parse(input.dataset.builderNumber),input.valueAsNumber)??'';
  if(input.dataset.clockStage){
    const stage=input.dataset.clockStage;
    if(input.value==='follow'){
      automaticStarts.add(stage);
      updateSequentialStarts();
      renderContent();
    }else if(input.value!=='fixed')draftValue(clockKey[stage],Number(input.value));
  }
  if(input.id==='quickCar')editBuilder({action:'car',id:input.value});
  if(input.id==='quickCircuit'){
    const track=catalog.tracks.find(track=>track.owned&&circuitName(track)===input.value);
    if(track)editBuilder({action:'track',id:track.id});
  }
  if(input.matches('[data-forecast]')&&input.value)editBuilder({action:'weatherPreset',session:weatherSession,preset:input.value});
});

updateWorkspace();
