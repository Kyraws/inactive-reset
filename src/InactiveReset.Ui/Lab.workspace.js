'use strict';
// Redline presentation uses the existing session writer, practice gates and native bindings.
let eventCategory='all',presetSource='personal',libraryTrack='',libraryPreviewId='',libraryCar='',circuitQuery='';
let inspectedPreset=null,builderPreset=null,checkpointEdit=null,checkpointUndoToken='',checkpointMutation=false;
let savedMapTracks=[],mapRequests=new Map(),circuitMaps=new Map(),mapFailures=new Map();
const normalizeTrack=s=>String(s||'').trim().toLowerCase().replace(/\s+\d+\.\d+$/,'');

function renderWeekendSchedule(){
  const keys={PRACTICE:SESSION_KEYS.PRACTICE.slice(0,3),QUALIFY:SESSION_KEYS.QUALIFY.slice(0,3),WARMUP:SESSION_KEYS.WARMUP,RACE:SESSION_KEYS.RACE.slice(0,6)};
  return '<div class="weekend-grid">'+Object.entries(SESSION_NAMES).map(([type,name])=>`<section class="section-block"><div class="settings-card" data-session-card="${type}"><div class="card-title-row"><h3>${name}</h3><select aria-label="Number of ${name.toLowerCase()} sessions" data-amount="${type}">${Array.from({length:type==='WARMUP'?2:5},(_,i)=>option(i,i===0?'Disabled':i===1?'Enabled':i+' sessions',i===setup.amounts[type])).join('')}</select></div>${setup.amounts[type]?settingsRows(keys[type]):'<p class="disabled-note">Enable this session to edit its schedule.</p>'}</div></section>`).join('')+'</div><details class="subsection"><summary>Individual practice slots</summary><div class="settings-card">'+settingsRows(['SESSSET_pract1','SESSSET_pract2','SESSSET_pract3','SESSSET_pract4'])+'</div></details>';
}
function renderConditions(enabled){
  const names={START:'Start',NODE_25:'25%',NODE_50:'50%',NODE_75:'75%',FINISH:'Finish'},weather=setup.weather[weatherSession]||{};
  return `<div class="segmented" aria-label="Weather session">${enabled.map(s=>`<button data-weather-session="${s}" class="${s===weatherSession?'active':''}" aria-pressed="${s===weatherSession}">${SESSION_NAMES[s]}</button>`).join('')}</div><div class="conditions-columns"><section>${block('Track surface',SESSION_KEYS[weatherSession].filter(k=>k.includes('realroad')))}${block('Weather behaviour',['SESSSET_weather','SESSSET_timescaled_weather'])}<h3>Forecast</h3><div class="preset-buttons">${[['SUNNY','Sunny'],['CLOUDY','Cloudy'],['RAINY','Rainy'],['DEFAULT','Track default']].map(([preset,name])=>`<button class="button" data-change="${keyData({action:'weatherPreset',session:weatherSession,preset})}">${name}</button>`).join('')}</div></section><section><div class="weather-tools"></div><div class="timeline">${Object.entries(names).map(([node,name])=>`<button data-weather-node="${node}" class="${node===weatherNode?'active':''}" aria-pressed="${node===weatherNode}">${name}</button>`).join('')}</div><div class="settings-card">${Object.entries(weather[weatherNode]||{}).map(([key,value])=>row(key,value,'weather',{session:weatherSession,node:weatherNode})).join('')}</div><p class="small-note">Timeline edits select Scripted weather. Rain chance requires a rain-capable sky.</p></section></div>`;
}

renderContent=function(){
  if(!setup||!catalog)return;
  const scroll=$('editor').scrollTop,focused=document.activeElement,id=focused?.id,caret=focused?.selectionStart;
  $('sectionContent').dataset.section=section;
  const enabled=['PRACTICE','QUALIFY','RACE'].filter(s=>setup.amounts[s]>0&&setup.weather[s]);
  if(!enabled.includes(weatherSession))weatherSession=enabled[0]||'PRACTICE';
  const groups=[['selection','Car & circuit',renderSelection],['sessions','Weekend',renderWeekendSchedule],['weather','Conditions',()=>renderConditions(enabled)],['grid','Competition',renderGrid],['rules','Rules',renderRules]];
  $('sectionContent').innerHTML='<div class="builder-tabs" role="tablist" aria-label="Session builder">'+groups.map(([key,title])=>`<button role="tab" id="builder-tab-${key}" data-builder-tab="${key}" aria-controls="builder-${key}" aria-selected="${section===key}" tabindex="${section===key?0:-1}">${title}</button>`).join('')+'</div>'+groups.map(([key,title,render])=>`<section class="builder-group" data-builder="${key}" id="builder-${key}" role="tabpanel" aria-labelledby="builder-tab-${key}" ${section===key?'':'hidden'}><div class="builder-group-body">${key==='weather'&&!enabled.length?'<p class="empty-note">Enable Practice, Qualifying or Race to edit its weather. LMU does not expose a separate Warmup forecast.</p>':render()}</div></section>`).join('');
  for(const b of document.querySelectorAll('[data-weather-session]'))if(!enabled.includes(b.dataset.weatherSession))b.remove();
  if(enabled.length){addWeatherPreview();const preview=$('sectionContent').querySelector('.weather-preview');document.querySelector('[data-builder=weather] .weather-tools').prepend(preview);for(const bar of preview.querySelectorAll('.rain-bar'))bar.style.height=(parseFloat(bar.style.height)*.4)+'px';}
  for(const b of document.querySelectorAll('[data-weather-copy]'))if(b.dataset.weatherCopy!=='points'&&(!enabled.includes(b.dataset.weatherCopy)||b.dataset.weatherCopy===weatherSession))b.remove();
  addDurationShortcuts();
  for(const note of document.querySelectorAll('.builder-group .setting-label small'))note.parentElement.title=note.textContent;
  $('sectionTitle').textContent='Session builder';$('sectionEyebrow').textContent='BUILD YOUR NEXT SESSION';
  $('sectionDescription').textContent='Choose a tab to edit. Each change saves to LMU and confirms the accepted value; presets are reviewed before applying.';
  $('emptyState').hidden=true;$('editorFields').hidden=false;$('editor').scrollTop=scroll;
  if(id&&$(id)){$(id).focus({preventScroll:true});if(caret!==null&&/search|text/.test($(id).type))$(id).setSelectionRange(caret,caret);}
  updateControls();
};

function updateWorkspace(){
  $('workspaceTitle').textContent=({home:'Overview',setup:'Session builder',official:'Official races',presets:'Session presets',practice:'Practice tools',library:'Checkpoint library',activity:'Activity',settings:'Controls'})[labPage]||'';
  for(const button of document.querySelectorAll('[data-practice-action]')){
    const action=button.dataset.practiceAction;
    button.disabled=action==='capture'?$('quickCaptureButton').disabled:$(action==='place'?'placeBtn':'standingBtn').disabled;
  }
  $('primaryPracticeLabel').textContent=labState?.inGarage===false?'Reset on track':'Place checkpoint';
  $('captureShortcut').textContent=practiceShortcut('capture','Ctrl+Alt+C')+' · replace quick slot';
  $('builderSave').disabled=$('savePlanButton').disabled;
  for(const b of document.querySelectorAll('[data-checkpoint-action]'))b.disabled=checkpointMutation||!!labState?.busy;
  if(builderPreset&&setup){
    const modified=JSON.stringify(snapshotPlan())!==JSON.stringify(builderPreset.baseline);
    $('builderSource').hidden=false;
    $('builderSource').innerHTML=`<div><small>LOADED PRESET</small><strong>${escapeHtml(builderPreset.name)}</strong><span class="pill">${modified?'Modified':'Saved values'}</span></div><div class="button-group"><button class="button quiet" data-restore-builder ${!modified?'disabled':''}>Restore saved values</button>${builderPreset.kind==='personal'?`<button class="button" data-update-builder ${!modified?'disabled':''}>Update preset</button>`:'<span class="small-note">Official source · save a personal copy to keep changes</span>'}</div>`;
  }else $('builderSource').hidden=true;
  updatePresetTab();
  if(['home','setup','official','presets'].includes(labPage)&&currentTrack()?.id)renderCircuitMap($('summaryMap'),currentTrack().id,[],null);
}

function allMapTracks(){return [...new Map([...savedMapTracks,...(catalog?.tracks||[])].map(t=>[t.id,t])).values()].filter(t=>t.id&&t.type!=='Showroom');}
function mappedLayout(track){
  const chosen=labPreferences.mapLayouts?.[track];if(chosen)return chosen;
  const matches=allMapTracks().filter(t=>[t.trackName,t.shortName,t.name,t.displayProperties?.shortName,t.sceneDesc].some(n=>n&&normalizeTrack(n)===normalizeTrack(track)));
  return matches.length===1?matches[0].id:'';
}
function mapSvg(map,checkpoints,selected,previewOnly=false){
  const points=map.points.filter(p=>p.type===0||p.type===1),road=points.filter(p=>p.type===0);
  const bounds=points.reduce((b,p)=>[Math.min(b[0],p.z),Math.max(b[1],p.z),Math.min(b[2],p.x),Math.max(b[3],p.x)],[Infinity,-Infinity,Infinity,-Infinity]);
  const [minX,maxX,minY,maxY]=bounds;
  const scale=Math.min(552/Math.max(1,maxX-minX),292/Math.max(1,maxY-minY));
  const x=z=>300+(z-(minX+maxX)/2)*scale,y=x=>170+(x-(minY+maxY)/2)*scale;
  const path=list=>list.map((p,i)=>(i?'L':'M')+x(p.z).toFixed(2)+' '+y(p.x).toFixed(2)).join(' ');
  return `<svg viewBox="0 0 600 340" role="img" aria-label="LMU circuit map with saved checkpoint positions"><path class="map-pit" d="${path(points.filter(p=>p.type===1))}"/><path class="map-road-shadow" d="${path(road)} Z"/><path class="map-road" d="${path(road)} Z"/>${checkpoints.filter(c=>Number.isFinite(c.x)&&Number.isFinite(c.z)).map((c,i)=>`<g role="button" tabindex="0" aria-label="${previewOnly?'Preview':'Select'} ${escapeHtml(c.name)}" ${previewOnly?'data-preview-checkpoint':'data-map-checkpoint'}="${escapeHtml(c.id)}" class="map-marker ${c.id===selected?'selected':''}" transform="translate(${x(c.z).toFixed(2)},${y(c.x).toFixed(2)})"><circle r="10"/><text text-anchor="middle" y="4">${i+1}</text></g>`).join('')}</svg>`;
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
  }).catch(()=>mapFailures.set(id,Date.now())).finally(()=>{mapRequests.delete(id);if(labPage==='library')renderLibrary();if(labPage==='practice')renderPracticeManagement();updateWorkspace();});
  mapRequests.set(id,pending);
}
async function refreshCircuitMap(){
  const id=$('mapLayout').value;if(!id)return;
  $('refreshCircuitMap').disabled=true;
  try{circuitMaps.set(id,await request('/api/track-map?id='+encodeURIComponent(id)+'&refresh=true'));mapFailures.delete(id);}
  catch(e){notify(e.message,true);}finally{$('refreshCircuitMap').disabled=false;renderLibrary();}
}

renderLibrary=function(){
  const all=(labState?.checkpoints||[]).filter(c=>!c.quick&&c.name!=='Quick capture'),tracks=[...new Set(all.map(c=>c.track))].sort();
  if(!tracks.includes(libraryTrack))libraryTrack=tracks.find(t=>normalizeTrack(t)===normalizeTrack(labState?.session?.track))||tracks[0]||'';
  $('libraryTrackTitle').textContent=libraryTrack||'No named checkpoints yet';
  $('libraryCircuits').innerHTML=tracks.filter(t=>t.toLowerCase().includes(circuitQuery.toLowerCase())).map(t=>`<button class="circuit-choice ${t===libraryTrack?'active':''}" data-library-track="${escapeHtml(t)}"><span>${escapeHtml(t)}</span><small>${all.filter(c=>c.track===t).length} saved</small></button>`).join('')||'<p class="empty-note">No saved circuits match.</p>';
  const circuit=all.filter(c=>c.track===libraryTrack),cars=[...new Set(circuit.map(c=>c.vehicle))].sort();
  if(!cars.includes(libraryCar))libraryCar='';
  $('libraryCar').innerHTML='<option value="">All cars</option>'+cars.map(c=>option(c,c,c===libraryCar)).join('');
  const list=circuit.filter(c=>(!onlyFavourites||labPreferences.checkpoints.includes(c.id))&&(!libraryCar||c.vehicle===libraryCar)&&`${c.name} ${c.track} ${c.vehicle}`.toLowerCase().includes(libraryQuery.toLowerCase())).sort((a,b)=>librarySort==='distance'?a.lapDistance-b.lapDistance:a.name.localeCompare(b.name));
  if(!list.some(c=>c.id===libraryPreviewId))libraryPreviewId=list[0]?.id||'';
  const preview=list.find(c=>c.id===libraryPreviewId),active=$('cp').value;
  $('libraryMeta').textContent=`${list.length} of ${circuit.length} named checkpoints · previewing does not change your destination`;
  $('checkpointCards').innerHTML=list.map((c,i)=>`<article class="checkpoint-item ${c.id===libraryPreviewId?'previewed':''}"><button class="checkpoint-preview" data-preview-checkpoint="${escapeHtml(c.id)}" aria-pressed="${c.id===libraryPreviewId}"><span class="point-number">${i+1}</span><span><strong>${escapeHtml(c.name)}</strong><small>${escapeHtml(c.vehicle)} · ${Number.isFinite(c.speedKmh)?c.speedKmh.toFixed(1)+' km/h':'Speed unknown'} · G${c.gear??'—'}</small>${c.id===active?'<em>Active destination</em>':''}</span></button><button class="icon-button" data-fav-checkpoint="${escapeHtml(c.id)}" aria-label="Favourite ${escapeHtml(c.name)}">${labPreferences.checkpoints.includes(c.id)?'★':'☆'}</button></article>`).join('')||'<p class="empty-note">No matching named checkpoints. Save a Quick capture as named in Practice tools, or capture with a name.</p>';
  const matching=preview&&normalizeTrack(preview.track)===normalizeTrack(labState?.session?.track);
  $('libraryPreview').innerHTML=preview?`<small>HIGHLIGHTED CHECKPOINT</small><h2>${escapeHtml(preview.name)}</h2><p>${escapeHtml(preview.vehicle)}</p><div class="checkpoint-stats"><span>${Number.isFinite(preview.speedKmh)?preview.speedKmh.toFixed(1)+' km/h':'Unknown speed'}</span><span>Gear ${preview.gear??'—'}</span><span>${Math.round(preview.lapDistance||0)} m</span></div>${matching?'':'<p class="small-note">Load this checkpoint’s track to select it as a teleport destination.</p>'}<div class="button-group"><button class="button primary" data-map-checkpoint="${escapeHtml(preview.id)}" ${!matching||labState?.busy?'disabled':''}>${active===preview.id?'Selected destination':'Select checkpoint'}</button><button class="button quiet" data-checkpoint-action="rename" data-id="${escapeHtml(preview.id)}">Rename</button><button class="button critical" data-checkpoint-action="delete" data-id="${escapeHtml(preview.id)}">Delete</button></div>`:'<p class="empty-note">Select a checkpoint to inspect it.</p>';
  const mapped=mappedLayout(libraryTrack),available=allMapTracks();
  $('mapLayout').innerHTML='<option value="">Choose exact layout…</option>'+available.sort((a,b)=>(a.name||'').localeCompare(b.name||'')).map(t=>option(t.id,(t.displayProperties?.shortName||t.shortName||t.name)+' · '+(t.displayProperties?.name||t.name),t.id===mapped)).join('');
  $('refreshCircuitMap').disabled=!mapped;
  renderCircuitMap($('libraryMap'),mapped,list,libraryPreviewId,true);
  $('checkpointUndo').hidden=!checkpointUndoToken;
  for(const b of document.querySelectorAll('[data-checkpoint-action]'))b.disabled=checkpointMutation||!!labState?.busy;
};

function renderPracticeManagement(){
  const checkpoint=(labState?.checkpoints||[]).find(c=>c.id===$('cp').value),list=(labState?.checkpoints||[]).filter(c=>normalizeTrack(c.track)===normalizeTrack(labState?.session?.track));
  const quick=checkpoint&&(checkpoint.quick||checkpoint.name==='Quick capture');
  $('checkpointManagement').innerHTML=checkpoint?`<div class="section-heading"><div><small>${quick?'REPLACEABLE QUICK SLOT':'SAVED CHECKPOINT'}</small><strong>${escapeHtml(checkpoint.name)}</strong><small>Captured in ${escapeHtml(checkpoint.vehicle)}</small></div><div class="button-group"><button class="button quiet" data-checkpoint-action="${quick?'copy':'rename'}" data-id="${escapeHtml(checkpoint.id)}">${quick?'Save as named':'Rename'}</button><button class="button critical" data-checkpoint-action="delete" data-id="${escapeHtml(checkpoint.id)}">Delete</button></div></div>${checkpointUndoToken?'<button class="text-button" data-undo-checkpoint>Undo last checkpoint deletion</button>':''}`:'';
  renderCircuitMap($('practiceMap'),mappedLayout(labState?.session?.track),list,checkpoint?.id);
}
function selectMapCheckpoint(id){
  if(labState?.busy)return;
  const cp=(labState?.checkpoints||[]).find(c=>c.id===id);
  if(!cp||normalizeTrack(cp.track)!==normalizeTrack(labState?.session?.track)){notify('Load the checkpoint’s track before selecting it.',true);return;}
  $('cp').value=id;updateDestination();renderCheckpoints(labState);setPage('practice');
  notify('Destination selected. The car has not moved.');
}
async function manageCheckpoint(action,id){
  if(checkpointMutation)return;
  const cp=(labState?.checkpoints||[]).find(c=>c.id===id);
  if(action!=='delete'){
    checkpointEdit={action,id};$('checkpointEditTitle').textContent=action==='copy'?'Save permanent checkpoint copy':'Rename checkpoint';
    $('checkpointEditName').value=action==='copy'?'':cp?.name||'';$('checkpointEditError').textContent='';$('checkpointEdit').showModal();$('checkpointEditName').focus();return;
  }
  checkpointMutation=true;updateWorkspace();
  try{
    const result=await request('/api/checkpoints/manage',{action,id});checkpointUndoToken=result.token;
    $('checkpointUndoText').textContent=(cp?.name||'Checkpoint')+' deleted. Undo is available until the next deletion or app restart.';
    if($('cp').value===id){$('cp').value='';practicePreferences.checkpoint='';savePracticePreferences();}
    await refresh();renderLibrary();notify('Checkpoint deleted. Undo is available in the library.');
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
  const plans=labPreferences.plans.filter(p=>`${p.name} ${p.target.display?.car||''} ${p.target.display?.track||''}`.toLowerCase().includes(presetQuery.toLowerCase()));
  $('savedPlans').innerHTML=plans.length?'<div class="preset-table-head"><span>Preset</span><span>Car / circuit</span><span>Actions</span></div>'+plans.map(p=>`<article class="saved-plan preset-table-row"><div><h3><button class="preset-name" data-inspect-preset="${escapeHtml(p.id)}">${escapeHtml(p.name)}</button></h3><small>${Object.entries(p.target.amounts).filter(([,v])=>v>0).map(([s])=>SESSION_NAMES[s]).join(' · ')}</small></div><div class="preset-content"><strong>${escapeHtml(p.target.display?.track||'Saved circuit')}</strong><small>${escapeHtml(p.target.display?.car||'Saved car')}</small></div><div class="preset-actions"><button class="button" data-inspect-preset="${escapeHtml(p.id)}">Details</button><button class="button primary" data-plan="${escapeHtml(p.id)}">Review & apply</button></div></article>`).join(''):'<p class="empty-note">No matching personal presets. Save the current session to reuse it later.</p>';
  updateLabControls();updatePresetTab();
};
function targetRows(target){
  const rows=[['Car',target.display?.car||target.car||'Not saved'],['Circuit',target.display?.track||target.track||'Not saved'],...Object.entries(target.amounts||{}).map(([s,v])=>[SESSION_NAMES[s],v?v+' session'+(v===1?'':'s'):'Disabled'])];
  return '<dl class="preset-values">'+rows.map(([k,v])=>`<div><dt>${escapeHtml(k)}</dt><dd>${escapeHtml(v)}</dd></div>`).join('')+'</dl><details open><summary>Rules & environment</summary><dl class="preset-values">'+Object.entries(target.settings||{}).map(([k,v])=>`<div><dt>${escapeHtml(label(k))}</dt><dd>${escapeHtml(target.display?.[k]||v)}</dd></div>`).join('')+'</dl></details><details><summary>Weather timelines</summary>'+Object.entries(target.weather||{}).map(([s,points])=>`<h3>${SESSION_NAMES[s]}</h3>${Object.entries(points).map(([n,fields])=>`<p><strong>${n.replace('NODE_','')}</strong> · ${Object.entries(fields).map(([k,v])=>escapeHtml(label(k))+': '+escapeHtml(v)).join(' · ')}</p>`).join('')}`).join('')+'</details>';
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
function renderOfficialWorkspace(){
  $('eventFilters').innerHTML=[['all','All'],['daily','Daily'],['weekly','Weekly'],['special','Special']].map(([id,label])=>`<button data-event-category="${id}" class="${eventCategory===id?'active':''}">${label}</button>`).join('');
  $('workspaceOfficialStatus').textContent=$('officialStatus').textContent;
  $('refreshWorkspaceEvents').disabled=officialRefreshing;
  const cards=[...$('officialEvents').children],events=officialSessions.events||[];
  $('officialWorkspaceEvents').innerHTML=cards.filter((_,i)=>eventCategory==='all'||(eventCategory==='daily'?!['weekly','special'].includes(events[i]?.category):events[i]?.category===eventCategory)).map(card=>card.outerHTML).join('')||'<p class="empty-note">No cached events in this category. Refresh events when LMU is available.</p>';
  for(const [index,card] of [...$('officialEvents').children].entries()){
    const event=events[index];if(!event)continue;
    for(const target of [card,...[...$('officialWorkspaceEvents').children].filter(c=>c.querySelector('h3')?.textContent===event.title)]){
      target.dataset.category=event.category;
      if(!target.querySelector('.event-lengths'))target.insertAdjacentHTML('beforeend','<p class="event-lengths">'+(event.sessions||[]).map(s=>(s.type==='Qualifying'?'Q':'R')+' '+escapeHtml(s.duration)).join(' / ')+'</p>');
    }
  }
  updatePresetTab();
  for(const row of $('officialPresets').querySelectorAll('.saved-plan')){
    const id=row.querySelector('[data-official-preset]')?.dataset.officialPreset;
    if(id&&!row.querySelector('[data-inspect-official]'))row.querySelector('.preset-actions').insertAdjacentHTML('afterbegin',`<button class="button quiet" data-inspect-official="${escapeHtml(id)}">Details</button>`);
  }
}

function inspectOfficial(id){
  for(const event of officialSessions.events||[])for(const session of event.sessions||[])if(officialPresetId(event,session)===id){
    inspectedPreset={kind:'official',id};$('presetKind').textContent='OFFICIAL SOURCE · READ ONLY';$('presetDetailsTitle').textContent=event.title+' · '+session.type;
    $('presetDetailsBody').innerHTML=`<dl class="preset-values"><div><dt>Circuit</dt><dd>${escapeHtml(officialTrack(event))}</dd></div><div><dt>Solo Practice duration</dt><dd>${escapeHtml(session.duration)}</dd></div><div><dt>Start</dt><dd>${escapeHtml(session.timeOfDay)}</dd></div><div><dt>Starting grip</dt><dd>${escapeHtml(officialRoadLabel(session))}</dd></div></dl><h3>Five-point forecast</h3>${(session.weather||[]).map((p,i)=>`<p>${['Start','25%','50%','75%','Finish'][i]} · ${escapeHtml(p.temperature)}°C · ${escapeHtml(p.rainChange)}% rain chance</p>`).join('')}<p class="small-note">Applies solo Practice with zero AI. Online BoP, fixed setup, tyre allocation and traffic-created surface changes are not reproduced. Apply, then save as a personal preset to keep edits.</p>`;
    $('presetDetailsActions').innerHTML=`<button class="button primary" data-official-preset="${escapeHtml(id)}">Review & apply to builder</button>`;$('presetDetails').showModal();return;
  }
}

function toggleSummary(force){const open=force??!document.body.classList.contains('summary-open');document.body.classList.toggle('summary-open',open);$('summaryToggle').setAttribute('aria-expanded',String(open));$('summaryShade').hidden=!open;}
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
$('refreshWorkspaceEvents').addEventListener('click',refreshOfficialSessions);
$('undoCheckpointDelete').addEventListener('click',undoCheckpoint);$('refreshCircuitMap').addEventListener('click',refreshCircuitMap);
$('checkpointEditForm').addEventListener('submit',async e=>{
  e.preventDefault();if(!checkpointEdit||checkpointMutation)return;
  checkpointMutation=true;const button=e.submitter||$('checkpointEditForm').querySelector('[type=submit]');button.disabled=true;
  try{
    const old=checkpointEdit.id,result=await request('/api/checkpoints/manage',{...checkpointEdit,name:$('checkpointEditName').value.trim()});
    if(checkpointEdit.action==='rename')labPreferences.checkpoints=labPreferences.checkpoints.map(id=>id===old?result.id:id);
    await persistPreferences();await refresh();
    if(normalizeTrack((labState?.checkpoints||[]).find(c=>c.id===result.id)?.track)===normalizeTrack(labState?.session?.track)){$('cp').value=result.id;updateDestination();}
    libraryPreviewId=result.id;$('checkpointEdit').close();renderLibrary();renderPracticeManagement();notify('Checkpoint saved. Original capture data preserved.');
  }catch(error){$('checkpointEditError').textContent=error.message;}finally{checkpointMutation=false;button.disabled=false;updateWorkspace();}
});
document.addEventListener('click',e=>{
  const b=e.target.closest('button,[data-map-checkpoint],[data-preview-checkpoint]');if(!b||b.disabled)return;
  if(b.dataset.libraryTrack){libraryTrack=b.dataset.libraryTrack;libraryPreviewId='';renderLibrary();}
  if(b.dataset.builderTab)switchSection(b.dataset.builderTab);
  if(b.dataset.previewCheckpoint){libraryPreviewId=b.dataset.previewCheckpoint;renderLibrary();}
  if(b.dataset.mapCheckpoint)selectMapCheckpoint(b.dataset.mapCheckpoint);
  if(b.dataset.checkpointAction)manageCheckpoint(b.dataset.checkpointAction,b.dataset.id);
  if(b.hasAttribute('data-undo-checkpoint'))undoCheckpoint();
  if(b.dataset.practiceAction){if(b.dataset.practiceAction==='capture')quickCapture();else $(b.dataset.practiceAction==='place'?'placeBtn':'standingBtn').click();}
  if(b.dataset.presetTab){presetSource=b.dataset.presetTab;updatePresetTab();}
  if(b.dataset.inspectPreset)inspectPreset(b.dataset.inspectPreset);
  if(b.dataset.inspectOfficial)inspectOfficial(b.dataset.inspectOfficial);
  if(b.dataset.officialPreset||b.dataset.renamePlan||b.dataset.replacePlan||b.dataset.deletePlan)$('presetDetails').close();
  if(b.dataset.duplicatePreset)duplicatePreset(b.dataset.duplicatePreset);
  if(b.dataset.exportPreset){const p=labPreferences.plans.find(p=>p.id===b.dataset.exportPreset);if(p)download('inactive-reset-preset.json',JSON.stringify({schema:1,plans:[p]},null,2),'application/json');}
  if(b.dataset.loadBuilder){const p=labPreferences.plans.find(p=>p.id===b.dataset.loadBuilder);if(p){$('presetDetails').close();setPage('setup','selection');if(reviewPlan(p.name,planIntents(p.target)))pendingPlan.source={kind:'personal',id:p.id,name:p.name};}}
  if(b.hasAttribute('data-update-builder')&&builderPreset?.kind==='personal')editPreset(builderPreset.id,true);
  if(b.hasAttribute('data-restore-builder')&&builderPreset)reviewPlan('Restore '+builderPreset.name,planIntents(builderPreset.baseline));
  if(b.dataset.eventCategory){eventCategory=b.dataset.eventCategory;renderOfficialWorkspace();}
});
document.addEventListener('change',e=>{
  if(e.target.id==='libraryCar'){libraryCar=e.target.value;renderLibrary();}
  if(e.target.id==='mapLayout'){labPreferences.mapLayouts||={};labPreferences.mapLayouts[libraryTrack]=e.target.value;persistPreferences();renderLibrary();}
});
document.addEventListener('input',e=>{if(e.target.id==='circuitSearch'){circuitQuery=e.target.value;renderLibrary();}});
document.addEventListener('keydown',e=>{
  if(e.target.matches('[data-builder-tab]')&&['ArrowLeft','ArrowRight','Home','End'].includes(e.key)){
    e.preventDefault();const tabs=[...document.querySelectorAll('[data-builder-tab]')],at=tabs.indexOf(e.target),next=e.key==='Home'?0:e.key==='End'?tabs.length-1:(at+(e.key==='ArrowRight'?1:-1)+tabs.length)%tabs.length;
    switchSection(tabs[next].dataset.builderTab);document.querySelector('[data-builder-tab="'+tabs[next].dataset.builderTab+'"]').focus();
  }
  if(e.key==='Escape')toggleSummary(false);
  if(['Enter',' '].includes(e.key)&&e.target.matches('g[role=button]')){e.preventDefault();e.target.dispatchEvent(new MouseEvent('click',{bubbles:true}));}
});
const savePlanToLmu=applyPendingPlan;
applyPendingPlan=async function(){
  const source=pendingPlan?.source;await savePlanToLmu();
  if(source&&!$('planDialog').open&&setup&&!cancelPlan){builderPreset={...source,baseline:snapshotPlan()};setPage('setup','selection');}
};
// The listener was installed before this file loaded; route it through the current function.
$('applyPlanButton').removeEventListener('click',savePlanToLmu);$('applyPlanButton').addEventListener('click',()=>applyPendingPlan());
const refreshAppearance=applyAppearance;
applyAppearance=function(){refreshAppearance();summaryWidth(labPreferences.summaryWidth||288);};
document.querySelector('.dock-heading').insertAdjacentHTML('afterend','<div id="summaryMap" class="circuit-map summary-map"></div>');
request('/api/track-maps').then(result=>{savedMapTracks=result.tracks||[];if(labPage==='library')renderLibrary();}).catch(()=>{});
updateWorkspace();
