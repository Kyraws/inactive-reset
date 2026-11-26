'use strict';
let labPage='setup', labState=null, preferencesReady=false, preferencesWrites=Promise.resolve(), toastTimer;
let labPreferences={schema:1,theme:'midnight',style:'original',density:'comfortable',pins:['SESSSET_Num_Opponents','SESSSET_AI_Strength','SESSSET_weather'],cars:[],tracks:[],checkpoints:[],plans:[]};
let vehicleSeason='all',vehicleClass='',favouriteCarsOnly=false,libraryQuery='',librarySort='name',onlyFavourites=false,logFilter='all';
let pendingPlan=null,applyingPlan=false,cancelPlan=false,lastUndo=null,editingPreset=null,presetQuery='';
const clone=v=>JSON.parse(JSON.stringify(v));
const sameNumber=(a,b)=>Math.abs(Number(a)-Number(b))<0.0001;
function notify(message,error=false){$('labToastText').textContent=message;$('labToast').className='lab-toast'+(error?' error':'');$('labToast').hidden=false;clearTimeout(toastTimer);if(!error)toastTimer=setTimeout(()=>$('labToast').hidden=true,6500);}
reportError=message=>{$('saveStatus').textContent='Change not confirmed';$('saveDot').className='save-dot error';notify(message,true);};
showNotice=message=>notify(message,true);
const baseRequest=request;
request=async function(path,body){
  // State.error describes game attachment; it is not an HTTP connection failure.
  if(path==='/api/state'&&body===undefined){const response=await fetch(path),result=await response.json();if(!response.ok)throw Error(result.error||'The app connection was lost.');return result;}
  return baseRequest(path,body);
};
function persistPreferences(){
  if(!preferencesReady)return Promise.resolve(false);
  const snapshot=clone(labPreferences);
  if(new Blob([JSON.stringify(snapshot)]).size>512000){notify('Your saved plans are full. Export or remove a plan before adding more.',true);return Promise.resolve(false);}
  preferencesWrites=preferencesWrites.then(async()=>{try{await request('/api/lab/preferences',snapshot);return true;}catch(e){notify('Your Lab choices could not be saved. '+e.message,true);return false;}});
  return preferencesWrites;
}
function setPage(page,nextSection){
  const panels={setup:'setupPanel',practice:'practicePanel',library:'libraryPanel',activity:'activityPanel',settings:'settingsPanel',presets:'presetsPanel',official:'officialPanel'};
  if(!panels[page])return;
  if(typeof toggleSummary==='function')toggleSummary(false);
  labPage=page;for(const id of Object.values(panels))$(id).hidden=id!==panels[page];
  toggleRecordedLaps();
  document.body.dataset.page=page;
  $('setupDock').hidden=['practice','library','activity'].includes(page);$('practiceDock').hidden=!$('setupDock').hidden;
  $('sessionSummary').hidden=$('summaryMeta').hidden=document.querySelector('.dock-journal').hidden=$('setupDock').hidden;
  $('dockEyebrow').textContent=$('setupDock').hidden?'YOUR PRACTICE':'YOUR SESSION';
  if(nextSection)baseSwitchSection(nextSection);
  for(const b of document.querySelectorAll('.workspace-nav [data-page]')){
    if(b.dataset.page===page)b.setAttribute('aria-current','page');
    else b.removeAttribute('aria-current');
  }
  if(['practice','library','activity'].includes(page))refresh();else{clearInterval(timer);timer=null;period=0;}
  if(page==='official'&&typeof renderOfficialWorkspace==='function')renderOfficialWorkspace();if(page==='presets'){renderPlans();renderOfficialSessions();}if(page==='library')renderLibrary();if(page==='activity')renderLabActivity();
  $('editor').scrollTop=0;updateLabControls();
  if(typeof updateWorkspace==='function')updateWorkspace();
}
const baseSwitchSection=switchSection;
switchSection=next=>setPage('setup',next);
const baseSchedule=schedule;
schedule=ms=>{if(['practice','library','activity'].includes(labPage))baseSchedule(ms);else{clearInterval(timer);timer=null;period=0;}};
const baseRow=row;
row=function(key,setting,action='setting',extra={}){
  if(!setting)return '';
  if(setting.uiSelectionType==='Switch' || action==='assist' && setting.minValue===0 && setting.maxValue===1){
    const on=!!setting.currentValue,body=action==='assist'?{action,key,value:on?0:1}:{action,key,steps:on?0:1};
    return `<div class="setting-row"><div class="setting-label">${escapeHtml(label(key))}</div><button class="lab-switch" role="switch" aria-checked="${on}" aria-label="${escapeHtml(label(key))}" data-change="${keyData(body)}">${on?'On':'Off'}</button></div>`;
  }
  return baseRow(key,setting,action,extra);
};
const baseRenderContent=renderContent;
renderContent=function(){baseRenderContent();$('sectionContent').dataset.section=section;if(setup&&catalog){if(section==='weather')addWeatherPreview();if(section==='sessions')addDurationShortcuts();}updateLabControls();};
const baseNavigation=renderNavigation;
renderNavigation=function(){baseNavigation();updateLabControls();maybeRefreshOfficialSessions();};
const baseUpdateControls=updateControls;
updateControls=function(){baseUpdateControls();updateLabControls();};
function updateLabControls(){
  const locked=!canEditSessionDraft()||starting||serverBusy||applyingPlan||(saving&&!sessionValueSaving&&!savingOptions);
  const optionsLocked=locked||!!sessionValueEdits.size;
  const busy=saving||starting||serverBusy||applyingPlan||!!sessionValueEdits.size;
  $('editorFields').disabled=locked;$('savePlanButton').disabled=!setup||!preferencesReady||busy;
  for(const b of document.querySelectorAll('[data-dialog=defaultsDialog]'))b.disabled=optionsLocked||!menuReady;
  $('refreshLabButton').disabled=busy;
  for(const b of document.querySelectorAll('[data-plan],[data-official-preset]'))b.disabled=busy;
  for(const b of document.querySelectorAll('[data-change],[data-setting],[data-assist],[data-amount],[data-class],[data-car],[data-track],#circuitSelect,#trackSelect,#carSelect,#gridSource,[data-target],[data-weather-copy]')){if(b.dataset.lmuDisabled===undefined)b.dataset.lmuDisabled=String(b.disabled);b.disabled=(b.dataset.sessionValue?locked:optionsLocked)||b.dataset.lmuDisabled==='true';}
  if(!applyingPlan&&pendingPlan){
    const stage=pendingPlan.allowDraft&&!menuReady;
    $('applyPlanButton').disabled=busy||locked||!(stage?pendingPlan.allIntents.length:menuReady&&pendingPlan.intents.length);
    $('applyPlanButton').textContent=stage?'Load into draft':pendingPlan.official?'Apply to Session Builder':'Apply to LMU';
  }
  const practiceBusy=!!labState?.busy;
  $('quickCaptureButton').disabled=!canCapture(labState||{});
  $('allCondition').disabled=$('allTemperature').disabled=practiceBusy;
  $('undoButton').disabled=busy||locked||!menuReady||!lastUndo;
  renderAccessMessage();
  updateSessionValueControls();
  if(typeof updateWorkspace==='function')updateWorkspace();
}
function renderAccessMessage(){
  const switchHint=gameRunning?'LMU is already running. Close it before choosing a different launch mode.':null;
  $('launchButton').title=switchHint||'EAC off: all tools in single-player Practice. Online racing is unavailable.';
  $('launchEacButton').title=switchHint||'EAC on: online racing, local session setup and checkpoint capture. Teleporting and tyre preparation are disabled.';
  $('launchModeBadge').textContent=!gameRunning?'LMU CLOSED':gameProtected?'EAC ON':'EAC OFF';
  const online=navigation?.state?.settingMode==='SETTING_MULTIPLAYER';
  $('accessTitle').textContent=!gameRunning?'Start here':gameProtected?'EAC is on · teleporting and tyre preparation are disabled':online?'Multiplayer session · practice changes are disabled':'EAC is off · full practice tools available';
  $('accessText').textContent=!gameRunning?'Choose a launch below. You can browse saved presets and checkpoints before opening LMU. To practise an official event, choose Qualifying practice or Race practice, review the changes, then press Start session.'
    :gameProtected?'Easy Anti-Cheat is active. Events, saved presets and checkpoint capture remain available when LMU provides a car position. '+(online?'You are in multiplayer. Open LMU’s single-player main menu to edit or start a local session. ':'You can edit and start local sessions from LMU’s single-player main menu. ')+'To teleport or prepare tyres, close LMU and choose Launch local play, then load single-player Practice.'
    :online?'Events and saved data remain available. Session editing needs LMU’s single-player main menu; teleporting and tyre preparation need single-player Practice.'
    :'Choose an official event or build a session. Review and apply it, press Start session, then switch to LMU and press Drive. Teleporting and tyre preparation work only in single-player Practice. For online racing, close LMU first and choose Launch with EAC.';
  $('accessNotice').hidden=['official','practice'].includes(labPage)||gameRunning&&(labPage==='setup'||!gameProtected&&!online);
  $('launchChoices').hidden=gameRunning;
  $('launchLocalChoice').disabled=$('launchEacChoice').disabled=gameRunning||launching||saving||starting;
}
function parts(car){return (car.fullPathTree||'Other, Other, Other').split(',').map(s=>s.trim());}
function modelKey(car){return carModel(car).toLowerCase()+'|'+parts(car)[1].toLowerCase();}
function shortVenue(name){
  const venues=[
    ['Le Mans',/lemans|sarthe/],['Daytona',/daytona/],['Bahrain',/bahrain/],['Qatar',/qatar|lusail/],
    ['COTA',/cota|circuitoftheamericas/],['Fuji',/fuji/],['Interlagos',/interlagos|josecarlospace/],
    ['Barcelona',/barcelona/],['Imola',/imola|enzodinoferrari/],['Laguna Seca',/lagunaseca/],
    ['Long Beach',/longbeach/],['Monza',/monza/],['Paul Ricard',/paulricard/],
    ['Portimão',/portimao|algarve/],['Road Atlanta',/roadatlanta/],['Sebring',/sebring/],
    ['Silverstone',/silverstone/],['Spa',/spa(?:wec|francorchamps|$)/]
  ];
  const key=name.normalize('NFD').replace(/[^a-z0-9]/gi,'').toLowerCase();
  return venues.find(([,pattern])=>pattern.test(key))?.[0]||'';
}
function circuitName(track){
  if(!track)return '';
  track=catalog?.tracks?.find(t=>t.id===track.id)||track;
  const name=track.displayProperties?.shortName||track.shortName||track.trackName||track.name||'Unknown circuit';
  return shortVenue(track.sceneDesc||'')||shortVenue(name)||name.replace(/\s+\d+\.\d+$/, '');
}
function layoutName(track){
 const scene=track.sceneDesc||'',name=track.displayProperties?.name||track.name||track.eventName||track.type||'Grand Prix';
 if(/^(\d+ Hours|\d+ Heures|\d+ Miles|Rolex|Lone Star|Qatar \d+)/i.test(name))return /ELMS/i.test(scene)?'Grand Prix · ELMS':'Grand Prix · WEC';
 const base=circuitName(track);return name.startsWith(base)?name.slice(base.length).trim()||'Grand Prix':name;
}
function trackDisplay(track){
  if(!track)return '';
  const layout=layoutName(track);
  return [circuitName(track),/^Grand Prix(?: · .+)?$/.test(layout)?'':layout].filter(Boolean).join(' · ');
}
function layoutQualifier(name){
  return name.replaceAll('_',' ').match(/\b(Outer|Paddock|School|National|Short|No Chicane)\b/i)?.[0];
}
function liveTrackDisplay(name){
  const id=typeof mappedLayout==='function'?mappedLayout(name):null;
  const track=catalog?.tracks?.find(t=>t.id===id);
  if(track)return trackDisplay(track);
  const venue=shortVenue(name), layout=layoutQualifier(name);
  return venue?[venue,layout].filter(Boolean).join(' · '):name;
}
function brandLogoUrl(brand){return '/api/brands/'+encodeURIComponent(brand);}
function brandName(car){return car.manufacturer||parts(car)[2].match(/^(Aston Martin|Mercedes-AMG|Isotta Fraschini|[A-Za-z]+)/)?.[0]||'LMU';}
const vehicleClasses = new Map([
 ['GT3', ['GT3', 'gt3']], ['LMGT3', ['GT3', 'gt3']], ['GTE', ['GTE', 'gte']],
 ['LMP2', ['LMP2 (WEC)', 'lmp2']], ['LMP3', ['LMP3', 'lmp3']],
 ['HYPER', ['Hypercar', 'hypercar']], ['HYPERCAR', ['Hypercar', 'hypercar']], ['LMP2_ELMS', ['LMP2 (ELMS)', 'lmp2-elms']]
]);
function vehicleClassBadge(value) {
 const [label, color] = vehicleClasses.get(value.toUpperCase()) || [value, ''];
 return `<span class="vehicle-class" data-class-color="${color}">${escapeHtml(label)}</span>`;
}
function renderSelection(panel){
 const car=currentCar(),track=currentTrack(),owned=catalog.cars.filter(c=>c.isOwned),key=car?modelKey(car):'';
 if(!vehicleClass)vehicleClass='all';
 const filtered=owned.filter(c=>(vehicleSeason==='all'||parts(c)[0]===vehicleSeason)&&(vehicleClass==='all'||parts(c)[1]===vehicleClass)&&(!carSearch||`${c.desc} ${c.fullPathTree} ${carModel(c)} ${brandName(c)}`.toLowerCase().includes(carSearch.toLowerCase())));
 const favs=new Set(owned.filter(c=>labPreferences.cars.includes(c.id)).map(modelKey));
 const models=[...new Map(filtered.filter(c=>!favouriteCarsOnly||favs.has(modelKey(c))).map(c=>[modelKey(c),c])).values()].sort((a,b)=>carModel(a).localeCompare(carModel(b)));
 const seasons=[...new Set(owned.map(c=>parts(c)[0]))].sort().reverse(),classes=[...new Set(owned.map(c=>parts(c)[1]))].sort();
 const tracks=catalog.tracks.filter(t=>t.type!=='Showroom'),circuits=[...new Set(tracks.map(circuitName))].sort(),circuit=circuitName(track),layouts=tracks.filter(t=>circuitName(t)===circuit),teams=owned.filter(c=>modelKey(c)===key);
 const circuitMarkup = `<section class="section-block circuit-picker"><div><label class="input-label" for="circuitSelect">Circuit</label><select id="circuitSelect">${circuits.map(n=>option(n,n,n===circuit,!tracks.some(t=>circuitName(t)===n&&t.owned))).join('')}</select></div><div><label class="input-label" for="trackSelect">Layout / event configuration</label><select id="trackSelect">${layouts.map(t=>option(t.id,layoutName(t),t.id===track?.id,!t.owned)).join('')}</select></div><button class="icon-button" data-fav-track="${escapeHtml(track?.id||'')}" aria-label="Favourite current layout">${labPreferences.tracks.includes(track?.id)?'★':'☆'}</button><div class="track-details"><span class="pill">${escapeHtml(track?.countryCode||'')}</span><span class="pill">${escapeHtml(track?.trackLength||tracks.find(t=>t.id===track?.id)?.length||'—')} km</span></div><div class="favourite-tracks">${tracks.filter(t=>t.owned&&labPreferences.tracks.includes(t.id)).map(t=>`<button class="button" data-track="${escapeHtml(t.id)}">${escapeHtml(circuitName(t))} · ${escapeHtml(layoutName(t))}</button>`).join('')}</div></section>`;
 const carMarkup = `<section class="section-block"><h2>Car model</h2><div class="vehicle-filters"><input id="carSearch" type="search" value="${escapeHtml(carSearch)}" placeholder="Search model, brand or team" aria-label="Search cars"><button class="button quiet" data-favourite-cars aria-pressed="${favouriteCarsOnly}">${favouriteCarsOnly?'★':'☆'} Favourites</button><select id="vehicleSeason" aria-label="Livery season">${option('all','All livery seasons',vehicleSeason==='all')}${seasons.map(s=>option(s,s,s===vehicleSeason)).join('')}</select></div><div class="vehicle-class-filters" role="group" aria-label="Vehicle class"><button class="button" data-vehicle-class="all" aria-pressed="${vehicleClass==='all'}">All classes</button>${classes.map(value=>`<button class="button" data-vehicle-class="${escapeHtml(value)}" aria-pressed="${vehicleClass===value}">${vehicleClassBadge(value)}</button>`).join('')}</div><div class="livery-choice" ${panel==='car'?'hidden':''}><label class="input-label" for="carSelect">Season · team · livery</label><select id="carSelect">${teams.sort((a,b)=>parts(b)[0].localeCompare(parts(a)[0])||a.desc.localeCompare(b.desc)).map(c=>option(c.id,parts(c)[0]+' · '+c.desc,c.id===car?.id)).join('')}</select></div><div id="carModelRail" class="vehicle-grid horizontal-rail" role="group" aria-label="Car models" tabindex="${models.length?0:-1}">${models.map(c=>`<article class="vehicle-card ${modelKey(c)===key?'active':''}"><button class="car-pick" data-car="${escapeHtml(c.id)}" aria-pressed="${modelKey(c)===key}"><span class="brand-art"><img class="brand-logo" data-brand="${escapeHtml(brandName(c))}" src="${brandLogoUrl(brandName(c))}" alt="${escapeHtml(brandName(c))}" onerror="this.hidden=true;this.nextElementSibling.hidden=false"><span class="brand-wordmark" hidden>${escapeHtml(brandName(c))}</span></span><strong>${escapeHtml(carModel(c))}</strong>${vehicleClassBadge(parts(c)[1])}</button><button class="icon-button" data-fav-car="${escapeHtml(c.id)}" aria-label="Favourite ${escapeHtml(carModel(c))}">${favs.has(modelKey(c))?'★':'☆'}</button></article>`).join('')||'<p class="empty-note">No matching models.</p>'}</div><h3>Liveries</h3><div id="liveryRail" class="horizontal-rail" role="group" aria-label="Liveries" tabindex="${teams.length?0:-1}">${teams.sort((a,b)=>parts(b)[0].localeCompare(parts(a)[0])||a.desc.localeCompare(b.desc)).map(c=>`<button class="button livery-tile" data-livery="${escapeHtml(c.id)}" aria-label="${escapeHtml(parts(c)[0]+' · '+c.desc)}" title="${escapeHtml(parts(c)[0]+' · '+c.desc)}" aria-pressed="${c.id===car?.id}"><span class="livery-art">${typeof artwork==='function'?artwork('car',carArtKey(c)):''}</span><strong class="livery-fallback">${escapeHtml(c.desc)}<br><small>${escapeHtml(parts(c)[0])}</small></strong></button>`).join('')}</div></section>`;
 if(panel==='car')return carMarkup;
 const defaults='<button class="button quiet" data-dialog="defaultsDialog">Use LMU circuit defaults</button>';
 return circuitMarkup+(panel==='circuit'?'':carMarkup)+defaults;
};
const baseRenderSummary=renderSummary;
renderSummary=function(){baseRenderSummary();const track=currentTrack();if(track){$('selectionSummary').querySelector('h3').textContent=circuitName(track);$('selectionSummary').querySelector('p').textContent=layoutName(catalog?.tracks.find(t=>t.id===track.id)||track);}};

function addDurationShortcuts(){for(const [type,key] of [['PRACTICE','SESSSET_Practice_Length'],['QUALIFY','SESSSET_Qualify_Length'],['RACE','SESSSET_race_time']]){if(!setup.amounts[type])continue;const input=$('value-'+key);if(!input||input.closest('fieldset[disabled]'))continue;const bar=document.createElement('div');bar.className='duration-shortcuts';bar.innerHTML='<span>Quick duration</span>'+[15,30,60].map(v=>`<button class="button" data-target="${key}" data-value="${v}">${v} min</button>`).join('');input.closest('.section-block').prepend(bar);}}
function addWeatherPreview(){
  const names={START:'Start',NODE_25:'25%',NODE_50:'50%',NODE_75:'75%',FINISH:'Finish'},w=setup.weather[weatherSession]||{};
  const preview=document.createElement('section');preview.className='weather-preview';preview.innerHTML='<div class="section-heading" style="margin:0"><h2>Forecast at a glance</h2><span class="muted">Rain chance / air temperature</span></div><div class="weather-chart">'+Object.entries(names).map(([n,title])=>`<button class="weather-point" data-weather-node="${n}" aria-label="${title}: ${w[n]?.WNV_RAIN_CHANCE?.currentValue??0}% rain chance, ${w[n]?.WNV_TEMPERATURE?.currentValue??'unknown'} degrees"><span class="rain-percent">${escapeHtml(w[n]?.WNV_RAIN_CHANCE?.currentValue??0)}%</span><span class="rain-bar" style="height:${Math.max(3,Math.min(55,Number(w[n]?.WNV_RAIN_CHANCE?.currentValue||0)*.55))}px"></span><strong>${escapeHtml(w[n]?.WNV_TEMPERATURE?.currentValue??'—')}°</strong><span>${title}</span></button>`).join('')+'</div><div class="weather-actions"><button class="button" data-weather-copy="points">Copy this point to all</button><button class="button" data-weather-copy="QUALIFY">Copy to qualifying</button><button class="button" data-weather-copy="RACE">Copy to race</button></div>';
  $('sectionContent').prepend(preview);
}
const baseChange=change;
change=function(body){
  if(body.action==='assist'){notify('Driving assists are managed in LMU.',true);return;}
  return baseChange(body,before=>{
    let intent;
    if(body.action==='setting'&&!sameNumber(before.settings[body.key]?.currentValue,setup.settings[body.key]?.currentValue))
      intent={type:'setting',key:body.key,value:before.settings[body.key].currentValue};
    if(body.action==='amount')
      intent={type:'amount',session:body.session,value:Math.min(before.amounts[body.session],1)};
    if(intent){
      lastUndo=intent;
      $('lastChange').textContent=(body.key?label(body.key):SESSION_NAMES[body.session])+' updated ? '+new Date().toLocaleTimeString([],{hour:'2-digit',minute:'2-digit'});
    }
  });
};
const baseCheckpoints=renderCheckpoints;
renderCheckpoints=function(s){
 labState=s;baseCheckpoints(s);$('libraryCount').textContent=(s.checkpoints||[]).filter(c=>!c.quick).length;
 if(labPage==='library')renderLibrary();
 if(typeof renderPracticeManagement==='function')renderPracticeManagement();
};
const baseActivity=renderActivity;
renderActivity=function(s){baseActivity(s);labState=s;$('operationRibbon').hidden=!s.busy;$('operationText').textContent=s.phase==='Armed'?'LMU is ready. Press Drive in the game.':s.phaseMessage||'An operation is in progress.';if(labPage==='activity')renderLabActivity();updateLabControls();};
function renderLibrary(){
  const all=labState?.checkpoints||[],loaded=labState?.session?.track||'';
  const list=all.filter(c=>(!onlyFavourites||labPreferences.checkpoints.includes(c.id))&&`${c.name} ${c.track} ${c.vehicle}`.toLowerCase().includes(libraryQuery.toLowerCase())).sort((a,b)=>librarySort==='distance'?a.lapDistance-b.lapDistance:librarySort==='track'?a.track.localeCompare(b.track)||a.name.localeCompare(b.name):a.name.localeCompare(b.name));
  $('libraryMeta').textContent=`${list.length} of ${all.length} checkpoints · ${loaded?'loaded track: '+loaded:'load Practice to place a checkpoint'}`;
  $('checkpointCards').innerHTML=list.map(c=>{const usable=c.track.toLowerCase()===loaded.toLowerCase()&&!labState?.busy;return `<article class="checkpoint-item ${usable?'':'dimmed'}"><button class="icon-button" data-fav-checkpoint="${escapeHtml(c.id)}" aria-label="Favourite ${escapeHtml(c.name)}">${labPreferences.checkpoints.includes(c.id)?'★':'☆'}</button><div><h3>${escapeHtml(c.name)}</h3><p>${escapeHtml(c.track)} · captured in ${escapeHtml(c.vehicle)}${Number.isFinite(c.speedKmh)?' · captured at '+c.speedKmh.toFixed(1)+' km/h':''}</p></div><span class="distance">${Math.round(c.lapDistance||0).toLocaleString()} m</span><button class="button" data-pick-checkpoint="${escapeHtml(c.id)}" ${usable?'':'disabled'}>${usable?'Select ↗':'Other track'}</button></article>`;}).join('')||'<p class="empty-note">No matching checkpoints. Capture a position in Practice or change your filter.</p>';
}
function activityText(){return [...diagnosticLog,...(labState?.checkpointErrors||[]),...(labState?.log||[]),...(labState?.outcome?.lines||[]).map(l=>l.label+': '+l.value)].join('\n')||'No activity yet.';}
function renderLabActivity(){const text=activityText(),filtered=logFilter==='issues'?text.split('\n').filter(l=>/error|fail|refus|unavail|unknown|cancel|mismatch|skipped checkpoint/i.test(l)).join('\n'):text;$('log').textContent=filtered;$('activityEmpty').hidden=!!filtered;}
function toggleFavourite(group,id){if(!id)return;const list=labPreferences[group],at=list.indexOf(id);if(at<0){if(list.length>=200){notify('Your favourites list is full.',true);return;}list.push(id);}else list.splice(at,1);persistPreferences();renderLibrary();if(setup&&labPage==='setup')renderContent();}
function snapshotPlan(){
  if(!setup)throw Error('Load LMU’s menu setup first.');
  const target={car:currentCar()?.id,track:currentTrack()?.id,settings:{},assists:{},amounts:Object.fromEntries(Object.entries(setup.amounts).map(([session, amount])=>[session, Math.min(amount,1)])),classes:clone(setup.selection.classesSelection||[]),fullGrid:!!setup.selection.fullGrid,weather:{},display:{}};
  for(const [k,v] of Object.entries(setup.settings))if(!['SESSSET_num_qual_sessions','SESSSET_num_race_sessions','SESSSET_run_warmup'].includes(k))target.settings[k]=v.currentValue;

  for(const [k,v] of Object.entries(setup.settings))target.display[k]=v.stringValue||String(v.currentValue)+(k.endsWith('_Length')?' min':'');
  target.display.car=carModel(currentCar());target.display.track=trackDisplay(currentTrack())||'Saved circuit';
  if(RACE_MODE in target.settings)target.display[RACE_MODE]=setup.settings[RACE_MODE].stringValue||SETTING_CHOICES[RACE_MODE][target.settings[RACE_MODE]];
  if(setup.settings.SESSSET_weather?.currentValue===4)for(const [s,nodes] of Object.entries(setup.weather)){target.weather[s]={};for(const [n,fields] of Object.entries(nodes)){target.weather[s][n]={};for(const [k,v] of Object.entries(fields))target.weather[s][n][k]=v.currentValue;}}
  return target;
}
function validatePlan(plan){
  if(!plan||typeof plan.name!=='string'||!plan.name.trim()||plan.name.length>60||typeof plan.id!=='string'||plan.id.length>80||!plan.target)throw Error('This is not a Lab 02 plan.');
  const t=plan.target;if(!t.settings||!t.assists||!t.amounts||!Array.isArray(t.classes)||t.classes.length>16||typeof t.fullGrid!=='boolean'||!t.weather)throw Error('This plan is incomplete.');
  for(const [group,prefix] of [['settings','SESSSET_'],['assists','DRIVEAIDS_']]){if(Object.keys(t[group]).length>100)throw Error('This plan has too many controls.');for(const [k,v] of Object.entries(t[group]))if(!k.startsWith(prefix)||typeof v!=='number'||!Number.isFinite(v)||Math.abs(v)>2147483647)throw Error('This plan contains an invalid control.');}
  for(const [s,v] of Object.entries(t.amounts))if(!Object.hasOwn(SESSION_NAMES,s)||!Number.isInteger(v)||v<0||v>(s==='WARMUP'?1:4))throw Error('This plan has an invalid session count.');
  for(const [s,nodes] of Object.entries(t.weather)){if(!['PRACTICE','QUALIFY','RACE'].includes(s))throw Error('Unknown weather session.');for(const [n,fields] of Object.entries(nodes)){if(!['START','NODE_25','NODE_50','NODE_75','FINISH'].includes(n))throw Error('Unknown weather point.');for(const [k,v] of Object.entries(fields))if(!k.startsWith('WNV_')||!Number.isFinite(v)||Math.abs(v)>1000)throw Error('Invalid weather value.');}}
  return plan;
}
function planIntents(target){
  const intents=[];if(target.track)intents.push({type:'track',id:target.track,display:target.display?.track});if(target.car)intents.push({type:'car',id:target.car});
  for(const [s,v] of Object.entries(target.amounts))intents.push({type:'amount',session:s,value:Math.min(v,1)});
  if(Object.hasOwn(target.settings,'SESSSET_weather'))intents.push({type:'setting',key:'SESSSET_weather',value:target.settings.SESSSET_weather});
  const mode=target.settings[RACE_MODE], minutes=target.settings[RACE_MINUTES];
  const timed=mode===TIMED_RACE&&Number.isInteger(minutes)&&minutes>=1&&minutes<=2000;
  if(timed)intents.push({type:'raceMinutes',value:minutes});
  else if(mode!==undefined)intents.push({type:'setting',key:RACE_MODE,value:mode,display:target.display?.[RACE_MODE]});
  for(const [k,v] of Object.entries(target.settings))
    if(k!=='SESSSET_weather'&&k!==RACE_MODE&&!(timed&&(k===RACE_MINUTES||k===RACE_LAPS)))
      intents.push({type:'setting',key:k,value:v,display:target.display?.[k]});
  // Old exports may contain assists; never apply user driving preferences.
  if(target.classes)intents.push({type:'grid',classes:target.classes,fullGrid:target.fullGrid});
  for(const [s,nodes] of Object.entries(target.weather))for(const [n,fields] of Object.entries(nodes))for(const [k,v] of Object.entries(fields))intents.push({type:'weather',session:s,node:n,key:k,value:v});
  return intents;
}
function intentValue(i){if(i.type==='setting')return setup?.settings[i.key]?.currentValue;if(i.type==='assist')return setup?.assists[i.key]?.currentValue;if(i.type==='weather')return setup?.weather[i.session]?.[i.node]?.[i.key]?.currentValue;if(i.type==='amount')return setup?.amounts[i.session];if(i.type==='car')return currentCar()?.id;if(i.type==='track')return currentTrack()?.id;if(i.type==='grid')return JSON.stringify([setup?.selection.classesSelection,!!setup?.selection.fullGrid]);return null;}
function needed(i){if(i.type==='weatherPreset'||i.type==='practiceConditions')return true;if(i.type==='raceMinutes')return setup?.settings[RACE_MODE]?.currentValue!==TIMED_RACE||setup?.settings[RACE_MINUTES]?.currentValue!==i.value;if(i.type==='grid')return JSON.stringify([i.classes,i.fullGrid])!==intentValue(i);if(['car','track'].includes(i.type))return i.id!==intentValue(i);return !sameNumber(i.value,intentValue(i));}
function describeIntent(i){if(i.type==='raceMinutes')return ['Race duration',i.value+' min'];if(i.type==='setting'&&TIME_SETTINGS.has(i.key))return [label(i.key),TIME_MODES[i.value]||clockValue(i.value)];if(i.type==='practiceConditions')return ['Practice time & surface',String(Math.floor(i.time/60)).padStart(2,'0')+':'+String(i.time%60).padStart(2,'0')+' · '+i.road.replace('preset:','').replace('.RRBIN','').toLowerCase()+' · water '+(i.waterDepth<0?'track default':Math.round(i.waterDepth*100)+'%')];if(i.type==='car')return ['Car',savedCarModel(i.id)];if(i.type==='track')return ['Circuit',i.display||catalog?.tracks.find(t=>t.id===i.id)?.name||'Saved circuit'];if(i.type==='grid')return ['Opponent classes',(i.classes.join(', ')||'None')+' · '+(i.fullGrid?'fill grid':'season grid')];if(i.type==='amount')return [SESSION_NAMES[i.session],i.value===0?'Disabled':i.value+' session'+(i.value>1?'s':'')];if(i.type==='weatherPreset')return [SESSION_NAMES[i.session]+' forecast',i.preset.toLowerCase()];return [(i.type==='weather'?SESSION_NAMES[i.session]+' · '+i.node.replace('NODE_','')+' · ':'')+label(i.key),settingChoice(i.key,i.value)||i.display||settingDisplay(i.key,i.value)];}
function reviewPlan(title,intents,description='Review the preset before loading its settings.',allowDraft=false){
  if(saving||starting||serverBusy||applyingPlan||sessionValueEdits.size){notify('Wait for your changes to save before reviewing another plan.',true);return;}
  const allIntents=intents.filter(i=>i.type!=='assist');
  const raceMayChange=allIntents.some(i=>needed(i)&&(i.key===RACE_MODE||['car','track'].includes(i.type)||i.type==='amount'&&i.session==='RACE'));
  pendingPlan={title,allIntents,allowDraft,intents:allIntents.filter(i=>needed(i)||raceMayChange&&(i.type==='raceMinutes'||[RACE_MODE,RACE_MINUTES,RACE_LAPS].includes(i.key)))};
  const stage=allowDraft&&!menuReady&&canEditSessionDraft();
  $('planTitle').textContent=title;$('planDescription').textContent=description;$('planProgress').textContent='';
  const raceIntents=allIntents.filter(i=>[RACE_MODE,RACE_MINUTES,RACE_LAPS].includes(i.key));
  const raceFields=Object.fromEntries(raceIntents.map(i=>[i.key,i.value]));
  const changes=(stage?allIntents:pendingPlan.intents);
  const rows=changes.filter(i=>!Object.hasOwn(raceFields,i.key)).map(describeIntent);
  if(changes.some(i=>Object.hasOwn(raceFields,i.key)))rows.push(['Race duration',raceLengthSummary(raceFields,Object.fromEntries(raceIntents.map(i=>[i.key,i.display])))]);
  $('planChanges').innerHTML=rows.map(([k,v])=>`<div class="plan-change"><span>${escapeHtml(k)}</span><strong>${escapeHtml(v)}</strong></div>`).join('')||'<p class="empty-note">This setup already matches. No changes are needed.</p>';
  $('cancelPlanButton').textContent='Keep current setup';
  if(!menuReady)$('planProgress').textContent=stage?'This replaces your draft. Return to the single-player main menu to apply it.':'Open the single-player main menu in LMU to apply.';
  updateLabControls();$('planDialog').showModal();return true;
}
async function applyIntent(i){
  if(!needed(i))return;
  if(i.type==='raceMinutes'){setup=await request('/api/session/action',{action:i.type,value:i.value});return;}
  if(i.type==='setting'&&(NUMERIC.has(i.key)||TIME_SETTINGS.has(i.key))){setup=await request('/api/session/action',{action:'sessionValue',key:i.key,value:i.value});return;}
  if(i.type==='practiceConditions'){setup=await request('/api/session/action',{action:'practiceConditions',time:i.time,road:i.road,waterDepth:i.waterDepth,loadTemperatures:i.loadTemperatures});return;}
  if(['car','track','grid','weatherPreset'].includes(i.type)){setup=await request('/api/session/action',{action:i.type,...i});return;}
  if(i.type==='amount'){setup=await request('/api/session/action',{action:'amount',session:i.session,amount:i.value});return;}
  if(i.type==='assist')throw Error('Driving assists are managed in LMU.');
  for(let step=0;step<64;step++){
    const before=intentValue(i);if(before===undefined)throw Error('LMU does not expose '+label(i.key)+' for this setup.');if(sameNumber(before,i.value))return;if(cancelPlan)return;
    const numeric=i.type==='weather'||NUMERIC.has(i.key);const delta=numeric?i.value-before:i.value>before?1:0;
    setup=await request('/api/session/action',{action:i.type,key:i.key,...(i.type==='weather'?{session:i.session,node:i.node}:{}),steps:delta});
    if(sameNumber(before,intentValue(i)))throw Error('LMU did not accept the requested '+label(i.key)+'. Review its available range.');
  }
  if(!sameNumber(intentValue(i),i.value))throw Error(label(i.key)+' needs more steps than this plan can apply. Adjust it manually.');
}
async function applyPendingPlan(){
  if(!pendingPlan||applyingPlan)return;if(!setup||!menuReady||serverBusy||starting){notify('Return to LMU’s local main menu before applying a plan.',true);return;}applyingPlan=true;saving=true;cancelPlan=false;updateControls();$('applyPlanButton').disabled=true;$('cancelPlanButton').textContent='Stop after current change';let applied=0;
  try{for(const intent of pendingPlan.intents){if(cancelPlan)break;$('planProgress').textContent=`Applying ${applied+1} of ${pendingPlan.intents.length} · ${describeIntent(intent)[0]}`;await applyIntent(intent);if(cancelPlan)break;applied++;}lastUndo=null;$('lastChange').textContent=pendingPlan.title+' · '+applied+' changes confirmed';notify(cancelPlan?'Plan stopped. Confirmed changes remain in LMU.':'Plan applied. Your session is ready to review.');$('planDialog').close();if(!cancelPlan&&pendingPlan.official)setPage('setup','sessions');}
  catch(e){notify(`Stopped after ${applied} confirmed changes. ${e.message}`,true);$('planProgress').textContent='Stopped. Confirmed changes remain in LMU. Refresh and review before trying again.';try{setup=await request('/api/session/setup');}catch{}}
  finally{applyingPlan=false;saving=false;renderContent();renderSummary();updateControls();$('applyPlanButton').disabled=true;$('cancelPlanButton').textContent='Close review';}
}
function renderPlans(){
 const plans=labPreferences.plans.filter(p=>`${p.name} ${savedCarModel(p.target.car)} ${p.target.display?.track||''}`.toLowerCase().includes(presetQuery.toLowerCase()));
 $('savedPlans').innerHTML=plans.map(p=>`<article class="saved-plan"><div><h3>${escapeHtml(p.name)}</h3><p>${escapeHtml(p.target.display?.track||'Saved circuit')} · ${escapeHtml(savedCarModel(p.target.car))}</p></div><div class="preset-actions"><button class="button" data-plan="${escapeHtml(p.id)}">Use preset</button><button class="button quiet" data-rename-plan="${escapeHtml(p.id)}">Rename</button><button class="button quiet" data-replace-plan="${escapeHtml(p.id)}" ${!setup?'disabled':''}>Update from session</button><button class="icon-button" data-delete-plan="${escapeHtml(p.id)}" aria-label="Delete ${escapeHtml(p.name)}">×</button></div></article>`).join('')||'<p class="empty-note">No matching presets. Save the current session to reuse it later.</p>';updateLabControls();
}
function download(name,text,type='text/plain'){const url=URL.createObjectURL(new Blob([text],{type})),a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
// A blank temperature delegates to LMU's selected compound and warmer setting.
tyreOptions=function(){return {wheels:TYRE_WHEELS.map(wheel=>{
 const raw=$('tyreCondition'+wheel).value.trim(),conditionPercent=Number(raw),heat=$('tyreTemperature'+wheel).value.trim(),temperatureCelsius=heat===''||Number(heat)===0?null:Number(heat);
 if($('tyreCondition'+wheel).validity.badInput||raw===''||!Number.isFinite(conditionPercent)||conditionPercent<0||conditionPercent>100)throw Error(wheel+' condition must be between 0 and 100 percent.');
 if($('tyreTemperature'+wheel).validity.badInput||temperatureCelsius!==null&&(!Number.isFinite(temperatureCelsius)||temperatureCelsius<0||temperatureCelsius>150))throw Error(wheel+' temperature must be between 0 and 150 °C.');
 return {conditionPercent,temperatureCelsius};
})};};
updateTyreSettings=function(){
 const conditions=TYRE_WHEELS.map(wheel=>$('tyreCondition'+wheel).value);
 const sameCondition=conditions.every(value=>value===conditions[0]);
 $('allConditionValue').textContent=sameCondition&&conditions[0]!==''?conditions[0]+'%':'Mixed';
 $('allCondition').setAttribute('aria-valuetext',$('allConditionValue').textContent);
 if(sameCondition&&conditions[0]!=='')$('allCondition').value=conditions[0];
 const heats=TYRE_WHEELS.map(wheel=>$('tyreTemperature'+wheel).value.trim()),sameHeat=heats.every(v=>v===heats[0]);
 const heatLabel=sameHeat?(heats[0]===''||heats[0]==='0'?'Game':heats[0]+'°C'):'Mixed';
 $('allTemperatureValue').textContent=heatLabel;$('allTemperature').setAttribute('aria-valuetext',heatLabel);
 if(sameHeat)$('allTemperature').value=heats[0]===''?'0':heats[0];
 for(const wheel of TYRE_WHEELS){$('tyreTemperature'+wheel).disabled=false;$('tyreTemperatureEnabled'+wheel).checked=$('tyreTemperature'+wheel).value.trim()!=='';}
 try{$('tyreSummary').textContent=tyreOptions().wheels.map((w,i)=>TYRE_WHEELS[i]+' '+w.conditionPercent+'% / '+(w.temperatureCelsius===null?'game default':w.temperatureCelsius+'°C')).join(' · ');}catch(e){$('tyreSummary').textContent=e.message;}
 updatePlacementControls();
 updateTyreAction();
 savePracticePreferences();
};
for(const wheel of TYRE_WHEELS)if(!$('tyreTemperatureEnabled'+wheel).checked)$('tyreTemperature'+wheel).value='';
updateTyreSettings();
function editPreset(id,replace=false){const p=labPreferences.plans.find(p=>p.id===id);if(!p)return;editingPreset={id,replace};$('planName').value=p.name;$('savePlanTitle').textContent=replace?'Replace preset with current session':'Rename preset';$('savePlanDescription').textContent=replace?'This replaces the saved setup. Driving assists are excluded.':'Only the name changes; the saved session stays the same.';$('savePlanDialog').showModal();$('planName').focus();}

document.addEventListener('click',async e=>{
  const b=e.target.closest('button');if(!b||b.disabled)return;
  if(b.dataset.page)setPage(b.dataset.page);
  if(b.dataset.close){const d=$(b.dataset.close);if(d.id==='planDialog'&&applyingPlan){cancelPlan=true;return;}d.close();}
  if(b.dataset.vehicleClass){
    vehicleClass=b.dataset.vehicleClass;
    renderContent();
    [...document.querySelectorAll('[data-vehicle-class]')].find(button=>button.dataset.vehicleClass===vehicleClass)?.focus();
  }
  if(b.hasAttribute('data-favourite-cars')){favouriteCarsOnly=!favouriteCarsOnly;renderContent();}
  if(b.dataset.favCar)toggleFavourite('cars',b.dataset.favCar);if(b.dataset.favTrack)toggleFavourite('tracks',b.dataset.favTrack);if(b.dataset.favCheckpoint)toggleFavourite('checkpoints',b.dataset.favCheckpoint);
  if(b.dataset.car){
    const pendingCar=typeof builderDraft!=='undefined'&&builderDraft&&labPage==='setup'?builderDraft.selection.selectedCar?.id:sessionOptions.findLast(edit=>edit.body.action==='car')?.body.id;
    const selected=pendingCar ? catalog.cars.find(c=>c.id===pendingCar) : currentCar();
    if(modelKey(catalog.cars.find(c=>c.id===b.dataset.car)||{})!==modelKey(selected||{}))change({action:'car',id:b.dataset.car});
  }
  if(b.dataset.track)change({action:'track',id:b.dataset.track});
  if(b.dataset.target)queueSessionValue(b.dataset.target,Number(b.dataset.value));
  if(b.dataset.plan){const p=labPreferences.plans.find(p=>p.id===b.dataset.plan);try{validatePlan(p);if(reviewPlan(p.name,planIntents(p.target),undefined,true))pendingPlan.source={kind:'personal',id:p.id,name:p.name};}catch(err){notify(err.message,true);}}
  if(b.dataset.deletePlan){const p=labPreferences.plans.find(p=>p.id===b.dataset.deletePlan);if(p&&confirm('Delete saved preset “'+p.name+'”?')){const before=clone(labPreferences.plans);labPreferences.plans=labPreferences.plans.filter(v=>v.id!==p.id);if(!await persistPreferences())labPreferences.plans=before;renderPlans();}}
  if(b.dataset.weatherCopy){if(typeof copyBuilderWeather==='function'&&labPage==='setup'&&builderDraft){copyBuilderWeather();return;}const source=setup.weather[weatherSession]||{},intents=[];if(b.dataset.weatherCopy==='points'){for(const node of Object.keys(source))if(node!==weatherNode)for(const [key,v] of Object.entries(source[weatherNode]||{}))intents.push({type:'weather',session:weatherSession,node,key,value:v.currentValue});}else{for(const [node,fields] of Object.entries(source))for(const [key,v] of Object.entries(fields))intents.push({type:'weather',session:b.dataset.weatherCopy,node,key,value:v.currentValue});}reviewPlan('Copy weather conditions',intents);}
  if(b.dataset.renamePlan)editPreset(b.dataset.renamePlan);if(b.dataset.replacePlan)editPreset(b.dataset.replacePlan,true);
  if(b.dataset.pickCheckpoint&&typeof selectMapCheckpoint==='function')selectMapCheckpoint(b.dataset.pickCheckpoint);
  if(b.dataset.logFilter){logFilter=b.dataset.logFilter;for(const v of document.querySelectorAll('[data-log-filter]'))v.classList.toggle('active',v===b);renderLabActivity();}

});
document.addEventListener('change',e=>{const t=e.target;if(t.id==='circuitSelect'){const next=catalog.tracks.find(v=>circuitName(v)===t.value&&v.owned);if(next)change({action:'track',id:next.id});}if(t.id==='vehicleSeason'){vehicleSeason=t.value;renderContent();}if(t.id==='librarySort'){librarySort=t.value;renderLibrary();}});
document.addEventListener('input',e=>{if(e.target.id==='librarySearch'){libraryQuery=e.target.value;renderLibrary();}if(e.target.id==='presetSearch'){presetQuery=e.target.value;renderPlans();renderOfficialSessions();}if(e.target.id==='allTemperature'){for(const w of TYRE_WHEELS)$('tyreTemperature'+w).value=e.target.value==='0'?'':e.target.value;updateTyreSettings();}if(e.target.id==='allCondition'){for(const w of TYRE_WHEELS)$('tyreCondition'+w).value=e.target.value;$('allConditionValue').textContent=e.target.value+'%';updateTyreSettings();}});
$('applyPlanButton').addEventListener('click',applyPendingPlan);
$('cancelPlanButton').addEventListener('click',()=>{if(applyingPlan){cancelPlan=true;$('planProgress').textContent='Stopping after the current request finishes…';}else $('planDialog').close();});
$('planDialog').addEventListener('cancel',e=>{if(applyingPlan){e.preventDefault();cancelPlan=true;}});
$('undoButton').addEventListener('click',()=>{if(lastUndo)reviewPlan('Undo last edit',[lastUndo]);});
$('savePlanButton').addEventListener('click',()=>{editingPreset=null;$('savePlanTitle').textContent='Save session preset';$('savePlanDescription').textContent='Keep the car, layout, sessions, weather and rules. Your driving assists stay untouched.';$('planName').value='';$('savePlanDialog').showModal();$('planName').focus();});
$('confirmSavePlan').addEventListener('click',async()=>{
 const name=$('planName').value.trim();if(!name){$('planName').focus();return;}
 if(!editingPreset&&labPreferences.plans.length>=40){notify('You can save up to 40 presets.',true);return;}
 $('confirmSavePlan').disabled=true;const before=clone(labPreferences.plans);
 try{
  const old=editingPreset?labPreferences.plans.find(p=>p.id===editingPreset.id):null;
  const p=validatePlan({id:old?.id||crypto.randomUUID(),name,target:old&&!editingPreset.replace?clone(old.target):snapshotPlan()});
  // Keep old file formats readable without reintroducing assist writes.
  p.target.assists={};if(old)labPreferences.plans=labPreferences.plans.map(v=>v.id===p.id?p:v);else labPreferences.plans.push(p);
  if(!await persistPreferences()){labPreferences.plans=before;return;}
  renderPlans();$('savePlanDialog').close();notify('Session preset saved.');
  if(typeof builderPreset!=='undefined'&&old&&builderPreset?.id===old.id){builderPreset={kind:'personal',id:p.id,name:p.name,baseline:clone(p.target)};updateWorkspace();}
 }catch(e){labPreferences.plans=before;notify(e.message,true);}finally{$('confirmSavePlan').disabled=false;}
});

$('importPlansButton').addEventListener('click',()=>$('importPlansFile').click());
$('importPlansFile').addEventListener('change',async e=>{try{const f=e.target.files[0];if(!f)return;if(f.size>512000)throw Error('This plan file is too large.');const json=JSON.parse(await f.text());if(json.schema!==1||!Array.isArray(json.plans)||json.plans.length+labPreferences.plans.length>40)throw Error('Use a Lab 02 export with at most 40 total plans.');const plans=json.plans.map(p=>({...validatePlan(p),id:crypto.randomUUID()}));labPreferences.plans.push(...plans);if(!await persistPreferences()){const ids=new Set(plans.map(p=>p.id));labPreferences.plans=labPreferences.plans.filter(p=>!ids.has(p.id));return;}renderPlans();notify(plans.length+' plans imported.');}catch(err){notify(err.message,true);}finally{e.target.value='';}});
$('exportPlansButton').addEventListener('click',()=>download('inactive-reset-plans.json',JSON.stringify({schema:1,plans:labPreferences.plans},null,2),'application/json'));
$('exportActivityButton').addEventListener('click',()=>download('inactive-reset-activity.txt','Inactive Reset · Lab 02\n'+new Date().toISOString()+'\n\n'+activityText()));
$('favouritesOnly').addEventListener('click',()=>{onlyFavourites=!onlyFavourites;$('favouritesOnly').setAttribute('aria-pressed',String(onlyFavourites));$('favouritesOnly').textContent=onlyFavourites?'★ Favourites':'☆ Favourites';renderLibrary();});
$('quickCaptureButton').addEventListener('click',quickCapture);

$('refreshLabButton').addEventListener('click',async()=>{await loadSetup();await poll();if(['practice','library','activity'].includes(labPage))await refresh();notify('Refreshed from LMU.');});
$('dismissLabToast').addEventListener('click',()=>$('labToast').hidden=true);
async function initLab(){void request('/api/vehicle-models').then(models=>{vehicleModels=new Map(Object.entries(models).map(([name,model])=>[name.toLowerCase(),model]));if(labPage==='presets')renderPlans();if(setup){renderContent();renderSummary();}}).catch(()=>{});try{const p=await request('/api/lab/preferences');labPreferences=p;preferencesReady=true;}catch(e){notify('Saved Lab choices could not be read. '+e.message,true);}if(typeof summaryWidth==='function')summaryWidth(labPreferences.summaryWidth||288);loadOfficialSessions();setPage('setup');document.documentElement.removeAttribute('data-loading');}
initLab();
