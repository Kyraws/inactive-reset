'use strict';
let labPage='home', labState=null, preferencesReady=false, preferencesWrites=Promise.resolve(), toastTimer;
let labPreferences={schema:1,theme:'midnight',style:'original',density:'comfortable',pins:['SESSSET_Num_Opponents','SESSSET_AI_Strength','SESSSET_weather'],cars:[],tracks:[],checkpoints:[],plans:[]};
let vehicleSeason='all',vehicleClass='',favouriteCarsOnly=false,libraryQuery='',librarySort='name',onlyFavourites=false,logFilter='all';
let pendingPlan=null,applyingPlan=false,cancelPlan=false,lastUndo=null,mutationFailed=false,editingPreset=null,presetQuery='';
const clone=v=>JSON.parse(JSON.stringify(v));
const sameNumber=(a,b)=>Math.abs(Number(a)-Number(b))<0.0001;
function notify(message,error=false){$('labToastText').textContent=message;$('labToast').className='lab-toast'+(error?' error':'');$('labToast').hidden=false;clearTimeout(toastTimer);if(!error)toastTimer=setTimeout(()=>$('labToast').hidden=true,6500);}
reportError=message=>{mutationFailed=true;$('saveStatus').textContent='Change not confirmed';$('saveDot').className='save-dot error';notify(message,true);};
showNotice=message=>notify(message,true);
const classicRequest=request;
request=async function(path,body){
  // State.error describes game attachment; it is not an HTTP connection failure.
  if(path==='/api/state'&&body===undefined){const response=await fetch(path),result=await response.json();if(!response.ok)throw Error(result.error||'The app connection was lost.');return result;}
  return classicRequest(path,body);
};
function persistPreferences(){
  if(!preferencesReady)return Promise.resolve(false);
  const snapshot=clone(labPreferences);
  if(new Blob([JSON.stringify(snapshot)]).size>512000){notify('Your saved plans are full. Export or remove a plan before adding more.',true);return Promise.resolve(false);}
  preferencesWrites=preferencesWrites.then(async()=>{try{await request('/api/lab/preferences',snapshot);return true;}catch(e){notify('Your Lab choices could not be saved. '+e.message,true);return false;}});
  return preferencesWrites;
}
function effectiveTheme(){return labPreferences.style==='redline'?'midnight':labPreferences.theme;}
function applyAppearance(){
 const racing=labPreferences.style==='redline';document.body.dataset.style=racing?'redline':'original';document.body.dataset.theme=effectiveTheme();document.body.dataset.density=labPreferences.density;
 for(const img of document.querySelectorAll('.brand-logo')){img.hidden=false;img.nextElementSibling.hidden=true;img.src=brandLogoUrl(img.dataset.brand);}
 for(const b of document.querySelectorAll('[data-theme]')){b.classList.toggle('active',b.dataset.theme===effectiveTheme());b.disabled=racing;}
 for(const b of document.querySelectorAll('[data-density]'))b.classList.toggle('active',b.dataset.density===labPreferences.density);
 for(const b of document.querySelectorAll('[data-style-choice]')){const selected=b.dataset.styleChoice===(labPreferences.style||'original');b.classList.toggle('active',selected);b.setAttribute('aria-pressed',String(selected));}
 $('themeDescription').textContent=racing?'Redline lives after dark. Original restores your previous colour theme.':'Midnight for the pit wall. Daylight for a brighter workspace.';
 document.querySelector('.lab-brand small').textContent=racing?'ONE MORE LAP. THEN WE GO.':'THE PRACTICE LAB';
}
function setPage(page,nextSection){
  const panels={home:'homePanel',setup:'setupPanel',practice:'practicePanel',library:'libraryPanel',activity:'activityPanel',settings:'settingsPanel',presets:'presetsPanel'};
  if(!panels[page])return;
  labPage=page;for(const id of Object.values(panels))$(id).hidden=id!==panels[page];
  $('setupDock').hidden=['practice','library','activity'].includes(page);$('practiceDock').hidden=!$('setupDock').hidden;
  $('dockEyebrow').textContent=$('setupDock').hidden?'YOUR PRACTICE':'YOUR SESSION';
  if(nextSection)classicSwitchSection(nextSection);
  for(const b of document.querySelectorAll('.rail-link'))b.classList.toggle('active',b.dataset.page===page || page==='setup' && b.dataset.section===section);
  if(['practice','library','activity'].includes(page))refresh();else{clearInterval(timer);timer=null;period=0;}
  if(page==='presets')renderPlans();if(page==='library')renderLibrary();if(page==='activity')renderLabActivity();
  $('editor').scrollTop=0;updateLabControls();
}
const classicSwitchSection=switchSection;
switchSection=next=>setPage('setup',next);
const classicSchedule=schedule;
schedule=ms=>{if(['practice','library','activity'].includes(labPage))classicSchedule(ms);else{clearInterval(timer);timer=null;period=0;}};
const classicRow=row;
row=function(key,setting,action='setting',extra={}){
  if(!setting)return '';
  if(setting.uiSelectionType==='Switch' || action==='assist' && setting.minValue===0 && setting.maxValue===1){
    const on=!!setting.currentValue,body=action==='assist'?{action,key,value:on?0:1}:{action,key,steps:on?0:1};
    return `<div class="setting-row"><div class="setting-label">${escapeHtml(label(key))}</div><button class="lab-switch" role="switch" aria-checked="${on}" aria-label="${escapeHtml(label(key))}" data-change="${keyData(body)}">${on?'On':'Off'}</button></div>`;
  }
  return classicRow(key,setting,action,extra);
};
function decoratePins(container){for(const r of container.querySelectorAll('.setting-row')){const control=r.querySelector('[data-change]');if(!control)continue;const b=JSON.parse(control.dataset.change),key=b.key;if(!/^SESSSET_/.test(key)||r.querySelector('.pin-control'))continue;const pin=document.createElement('button');pin.type='button';pin.className='pin-control';pin.dataset.pin=key;pin.setAttribute('aria-pressed',String(labPreferences.pins.includes(key)));pin.setAttribute('aria-label','Pin '+label(key));pin.textContent=labPreferences.pins.includes(key)?'★':'☆';r.prepend(pin);}}
const classicRenderContent=renderContent;
renderContent=function(){classicRenderContent();$('sectionContent').dataset.section=section;if(setup&&catalog){decoratePins($('sectionContent'));renderHome();if(section==='weather')addWeatherPreview();if(section==='sessions')addDurationShortcuts();}updateLabControls();};
const classicNavigation=renderNavigation;
renderNavigation=function(){classicNavigation();renderMetrics();updateLabControls();};
const classicUpdateControls=updateControls;
updateControls=function(){classicUpdateControls();updateLabControls();};
function updateLabControls(){
  const locked=!setup||!menuReady||saving||starting||serverBusy||applyingPlan;
  const busy=saving||starting||serverBusy||applyingPlan;
  $('pinnedFields').disabled=busy;$('editorFields').disabled=busy;$('savePlanButton').disabled=!setup||!preferencesReady||busy;
  for(const b of document.querySelectorAll('[data-template],[data-plan]'))b.disabled=busy;
  for(const b of document.querySelectorAll('[data-change],[data-setting],[data-assist],[data-amount],[data-class],[data-car],[data-track],#circuitSelect,#trackSelect,#carSelect,#gridSource,[data-target],[data-weather-copy]')){if(b.dataset.lmuDisabled===undefined)b.dataset.lmuDisabled=String(b.disabled);b.disabled=locked||b.dataset.lmuDisabled==='true';}
  if(!applyingPlan)$('applyPlanButton').disabled=locked||!pendingPlan?.intents.length;
  const practiceBusy=!!labState?.busy;
  $('quickCaptureButton').disabled=practiceBusy||!labState?.session?.capturable;
  $('allCondition').disabled=$('allTemperature').disabled=practiceBusy;
  for(const b of document.querySelectorAll('[data-tyre-preset]'))b.disabled=practiceBusy;
  $('undoButton').disabled=locked||!lastUndo;
}
function renderMetrics(){
  if(!setup){$('dashboardMetrics').innerHTML='<div class="metric"><span>SESSION</span><strong>Not loaded</strong></div><div class="metric"><span>MODE</span><strong>Local play</strong></div><div class="metric"><span>STARTUP</span><strong>One button press</strong></div>';return;}
  const enabled=Object.values(setup.amounts).reduce((a,b)=>a+b,0);
  $('dashboardMetrics').innerHTML=`<div class="metric"><span>ENABLED SESSIONS</span><strong>${enabled}</strong></div><div class="metric"><span>AI OPPONENTS</span><strong>${escapeHtml(setup.settings.SESSSET_Num_Opponents?.currentValue??0)}</strong></div><div class="metric"><span>CONDITIONS</span><strong>${escapeHtml(settingValue('SESSSET_weather'))}</strong></div>`;
  $('heroTitle').textContent=circuitName(currentTrack())||'Your next lap starts here.';
  $('heroSubtitle').textContent=(currentCar()?.fullPathTree?.split(',').map(s=>s.trim()).join(' · ')||'Choose your car')+' — '+(menuReady?'ready to configure.':'your setup, ready for the next run.');
}
function renderHome(){
  renderMetrics();if(setup){const focused=document.activeElement?.id;$('pinnedControls').innerHTML=labPreferences.pins.filter(k=>k.startsWith('SESSSET_')).map(k=>row(k,setup.settings[k],'setting').replaceAll('id="value-','id="home-value-')).join('')||'<p class="empty-note">Pin a control from the session builder to put it here.</p>';decoratePins($('pinnedControls'));if(focused&&$(focused))$(focused).focus({preventScroll:true});}
  renderPlans();
}
function parts(car){return (car.fullPathTree||'Other, Other, Other').split(',').map(s=>s.trim());}
function modelKey(car){return parts(car)[2].toLowerCase()+'|'+parts(car)[1].toLowerCase();}
function circuitName(track){if(!track)return '';track=catalog?.tracks?.find(t=>t.id===track.id)||track;return (track.displayProperties?.shortName||track.shortName||track.trackName||track.name||'Unknown circuit').replace(/\s+\d+\.\d+$/, '').replace(/Aut.dromo Jos. Carlos Pace/,'Autódromo José Carlos Pace');}
function layoutName(track){
 const scene=track.sceneDesc||'',name=track.displayProperties?.name||track.name||track.eventName||track.type||'Grand Prix';
 if(/^(\d+ Hours|\d+ Heures|\d+ Miles|Rolex|Lone Star|Qatar \d+)/i.test(name))return /ELMS/.test(scene)?'Grand Prix · ELMS':'Grand Prix · WEC';
 const base=circuitName(track);return name.startsWith(base)?name.slice(base.length).trim()||'Grand Prix':name;
}
function brandLogoUrl(brand){return '/api/brands/'+encodeURIComponent(brand)+'?theme='+effectiveTheme();}
function brandName(car){return car.manufacturer||parts(car)[2].match(/^(Aston Martin|Mercedes-AMG|Isotta Fraschini|[A-Za-z]+)/)?.[0]||'LMU';}
renderSelection=function(){
 const car=currentCar(),track=currentTrack(),owned=catalog.cars.filter(c=>c.isOwned),key=car?modelKey(car):'';
 if(!vehicleClass)vehicleClass='all';
 const filtered=owned.filter(c=>(vehicleSeason==='all'||parts(c)[0]===vehicleSeason)&&(vehicleClass==='all'||parts(c)[1].replace('_ELMS','')===vehicleClass)&&(!carSearch||`${c.desc} ${c.fullPathTree} ${brandName(c)}`.toLowerCase().includes(carSearch.toLowerCase())));
 const favs=new Set(owned.filter(c=>labPreferences.cars.includes(c.id)).map(modelKey));
 const models=[...new Map(filtered.filter(c=>!favouriteCarsOnly||favs.has(modelKey(c))).map(c=>[modelKey(c),c])).values()].sort((a,b)=>parts(a)[2].localeCompare(parts(b)[2]));
 const seasons=[...new Set(owned.map(c=>parts(c)[0]))].sort().reverse(),classes=[...new Set(owned.map(c=>parts(c)[1].replace('_ELMS','')))].sort();
 const tracks=catalog.tracks.filter(t=>t.type!=='Showroom'),circuits=[...new Set(tracks.map(circuitName))].sort(),circuit=circuitName(track),layouts=tracks.filter(t=>circuitName(t)===circuit),teams=owned.filter(c=>modelKey(c)===key);
 return `<section class="section-block circuit-picker"><div><label class="input-label" for="circuitSelect">Circuit</label><select id="circuitSelect">${circuits.map(n=>option(n,n,n===circuit,!tracks.some(t=>circuitName(t)===n&&t.owned))).join('')}</select></div><div><label class="input-label" for="trackSelect">Layout / event configuration</label><select id="trackSelect">${layouts.map(t=>option(t.id,layoutName(t),t.id===track?.id,!t.owned)).join('')}</select></div><button class="icon-button" data-fav-track="${escapeHtml(track?.id||'')}" aria-label="Favourite current layout">${labPreferences.tracks.includes(track?.id)?'★':'☆'}</button><div class="track-details"><span class="pill">${escapeHtml(track?.countryCode||'')}</span><span class="pill">${escapeHtml(track?.trackLength||tracks.find(t=>t.id===track?.id)?.length||'—')} km</span></div><div class="favourite-tracks">${tracks.filter(t=>t.owned&&labPreferences.tracks.includes(t.id)).map(t=>`<button class="button" data-track="${escapeHtml(t.id)}">${escapeHtml(circuitName(t))} · ${escapeHtml(layoutName(t))}</button>`).join('')}</div></section>
 <section class="section-block"><h2>Car model</h2><div class="vehicle-filters"><input id="carSearch" type="search" value="${escapeHtml(carSearch)}" placeholder="Search model, brand or team" aria-label="Search cars"><button class="button quiet" data-favourite-cars aria-pressed="${favouriteCarsOnly}">${favouriteCarsOnly?'★':'☆'} Favourites</button><select id="vehicleSeason" aria-label="Livery season">${option('all','All livery seasons',vehicleSeason==='all')}${seasons.map(s=>option(s,s,s===vehicleSeason)).join('')}</select><select id="vehicleClass" aria-label="Vehicle class">${option('all','All classes',vehicleClass==='all')}${classes.map(s=>option(s,s,s===vehicleClass)).join('')}</select></div><div class="livery-choice"><label class="input-label" for="carSelect">Season · team · livery</label><select id="carSelect">${teams.sort((a,b)=>parts(b)[0].localeCompare(parts(a)[0])||a.desc.localeCompare(b.desc)).map(c=>option(c.id,parts(c)[0]+' · '+c.desc,c.id===car?.id)).join('')}</select></div><div class="vehicle-grid">${models.map(c=>`<article class="vehicle-card ${modelKey(c)===key?'active':''}"><button class="car-pick" data-car="${escapeHtml(c.id)}" aria-pressed="${modelKey(c)===key}"><span class="brand-art"><img class="brand-logo" data-light-variant="${['Alpine','Aston Martin','BMW','Cadillac','Genesis','Isotta Fraschini','Lexus','Ligier','Oreca','Toyota','Vanwall'].includes(brandName(c))}" data-brand="${escapeHtml(brandName(c))}" src="${brandLogoUrl(brandName(c))}" alt="${escapeHtml(brandName(c))}" onerror="this.hidden=true;this.nextElementSibling.hidden=false"><span class="brand-wordmark" hidden>${escapeHtml(brandName(c))}</span></span><strong>${escapeHtml(parts(c)[2])}</strong><small>${escapeHtml(parts(c)[1].replace('_ELMS',' · ELMS'))} · ${owned.filter(v=>modelKey(v)===modelKey(c)).length} liveries</small></button><button class="icon-button" data-fav-car="${escapeHtml(c.id)}" aria-label="Favourite ${escapeHtml(parts(c)[2])}">${favs.has(modelKey(c))?'★':'☆'}</button></article>`).join('')||'<p class="empty-note">No matching models.</p>'}</div><p class="small-note">${models.length} models. Season entries share one card; distinct Evo models and classes stay separate.</p></section><button class="button quiet" data-dialog="defaultsDialog">Use circuit defaults</button>`;
};
const classicRenderRules=renderRules;
renderRules=function(){const assists=setup.assists;try{setup.assists={};return classicRenderRules().replaceAll('rule or assist','rule').replaceAll('rules and assists','rules');}finally{setup.assists=assists;}};
SECTION_INFO.rules=['MAKE IT YOURS','Rules','Simulation settings and session rules. Your driving assists stay as configured in LMU.'];
const classicRenderSummary=renderSummary;
renderSummary=function(){classicRenderSummary();const track=currentTrack();if(track){$('selectionSummary').querySelector('h3').textContent=circuitName(track);$('selectionSummary').querySelector('p').textContent=layoutName(catalog?.tracks.find(t=>t.id===track.id)||track);}};

function addDurationShortcuts(){for(const [type,key] of [['PRACTICE','SESSSET_Practice_Length'],['QUALIFY','SESSSET_Qualify_Length'],['RACE','SESSSET_race_time']]){if(!setup.amounts[type])continue;const input=$('value-'+key);if(!input||input.closest('fieldset[disabled]'))continue;const bar=document.createElement('div');bar.className='duration-shortcuts';bar.innerHTML='<span>Quick duration</span>'+[15,30,60].map(v=>`<button class="button" data-target="${key}" data-value="${v}">${v} min</button>`).join('');input.closest('.section-block').prepend(bar);}}
function addWeatherPreview(){
  const names={START:'Start',NODE_25:'25%',NODE_50:'50%',NODE_75:'75%',FINISH:'Finish'},w=setup.weather[weatherSession]||{};
  const preview=document.createElement('section');preview.className='weather-preview';preview.innerHTML='<div class="section-heading" style="margin:0"><h2>Forecast at a glance</h2><span class="muted">Rain chance / air temperature</span></div><div class="weather-chart">'+Object.entries(names).map(([n,title])=>`<button class="weather-point" data-weather-node="${n}" aria-label="${title}: ${w[n]?.WNV_RAIN_CHANCE?.currentValue??0}% rain chance, ${w[n]?.WNV_TEMPERATURE?.currentValue??'unknown'} degrees"><span class="rain-percent">${escapeHtml(w[n]?.WNV_RAIN_CHANCE?.currentValue??0)}%</span><span class="rain-bar" style="height:${Math.max(3,Math.min(55,Number(w[n]?.WNV_RAIN_CHANCE?.currentValue||0)*.55))}px"></span><strong>${escapeHtml(w[n]?.WNV_TEMPERATURE?.currentValue??'—')}°</strong><span>${title}</span></button>`).join('')+'</div><div class="weather-actions"><button class="button" data-weather-copy="points">Copy this point to all</button><button class="button" data-weather-copy="QUALIFY">Copy to qualifying</button><button class="button" data-weather-copy="RACE">Copy to race</button></div>';
  $('sectionContent').prepend(preview);
}
const classicChange=change;
change=async function(body){if(body.action==='assist'){notify('Driving assists are managed in LMU.',true);return;}const before=setup?clone(setup):null;mutationFailed=false;await classicChange(body);if(before&&setup&&!mutationFailed){let intent=null;if(body.action==='setting'&&!sameNumber(before.settings[body.key]?.currentValue,setup.settings[body.key]?.currentValue))intent={type:'setting',key:body.key,value:before.settings[body.key].currentValue};if(body.action==='assist')intent={type:'assist',key:body.key,value:before.assists[body.key].currentValue};if(body.action==='amount')intent={type:'amount',session:body.session,value:before.amounts[body.session]};if(intent){lastUndo=intent;$('lastChange').textContent=(body.key?label(body.key):SESSION_NAMES[body.session])+' updated · '+new Date().toLocaleTimeString([],{hour:'2-digit',minute:'2-digit'});}updateLabControls();}};
const classicCheckpoints=renderCheckpoints;
renderCheckpoints=function(s){labState=s;classicCheckpoints(s);$('libraryCount').textContent=(s.checkpoints||[]).length;
 const list=(s.checkpoints||[]).filter(c=>c.track.toLowerCase()===(s.session?.track||'').toLowerCase());
 $('destinationList').innerHTML=list.map(c=>`<button class="destination-option ${$('cp').value===c.id?'active':''}" data-pick-checkpoint="${escapeHtml(c.id)}" ${s.busy?'disabled':''} aria-pressed="${$('cp').value===c.id}"><strong>${escapeHtml(c.name)}</strong><span>${Math.round(c.lapDistance||0).toLocaleString()} m</span></button>`).join('')||'<p class="empty-note">'+(s.session?'No captures for this track yet.':'Load Practice to see destinations.')+'</p>';if(labPage==='library')renderLibrary();};
const classicActivity=renderActivity;
renderActivity=function(s){classicActivity(s);labState=s;$('operationRibbon').hidden=!s.busy;$('operationText').textContent=s.phase==='Armed'?'LMU is ready. Press Drive in the game.':s.phaseMessage||'An operation is in progress.';if(labPage==='activity')renderLabActivity();updateLabControls();};
function renderLibrary(){
  const all=labState?.checkpoints||[],loaded=labState?.session?.track||'';
  const list=all.filter(c=>(!onlyFavourites||labPreferences.checkpoints.includes(c.id))&&`${c.name} ${c.track} ${c.vehicle}`.toLowerCase().includes(libraryQuery.toLowerCase())).sort((a,b)=>librarySort==='distance'?a.lapDistance-b.lapDistance:librarySort==='track'?a.track.localeCompare(b.track)||a.name.localeCompare(b.name):a.name.localeCompare(b.name));
  $('libraryMeta').textContent=`${list.length} of ${all.length} checkpoints · ${loaded?'loaded track: '+loaded:'load Practice to place a checkpoint'}`;
  $('checkpointCards').innerHTML=list.map(c=>{const usable=c.track.toLowerCase()===loaded.toLowerCase()&&!labState?.busy;return `<article class="checkpoint-item ${usable?'':'dimmed'}"><button class="icon-button" data-fav-checkpoint="${escapeHtml(c.id)}" aria-label="Favourite ${escapeHtml(c.name)}">${labPreferences.checkpoints.includes(c.id)?'★':'☆'}</button><div><h3>${escapeHtml(c.name)}</h3><p>${escapeHtml(c.track)} · captured in ${escapeHtml(c.vehicle)}</p></div><span class="distance">${Math.round(c.lapDistance||0).toLocaleString()} m</span><button class="button" data-pick-checkpoint="${escapeHtml(c.id)}" ${usable?'':'disabled'}>${usable?'Select ↗':'Other track'}</button></article>`;}).join('')||'<p class="empty-note">No matching checkpoints. Capture a position in Practice or change your filter.</p>';
}
function activityText(){return [...diagnosticLog,...(labState?.log||[]),...(labState?.outcome?.lines||[]).map(l=>l.label+': '+l.value)].join('\n')||'No activity yet.';}
function renderLabActivity(){const text=activityText(),filtered=logFilter==='issues'?text.split('\n').filter(l=>/error|fail|refus|unavail|unknown|cancel|mismatch/i.test(l)).join('\n'):text;$('log').textContent=filtered;$('activityEmpty').hidden=!!filtered;}
function toggleFavourite(group,id){if(!id)return;const list=labPreferences[group],at=list.indexOf(id);if(at<0){if(list.length>=200){notify('Your favourites list is full.',true);return;}list.push(id);}else list.splice(at,1);persistPreferences();renderHome();renderLibrary();if(setup&&labPage==='setup')renderContent();}
function snapshotPlan(){
  if(!setup)throw Error('Load LMU’s menu setup first.');
  const target={car:currentCar()?.id,track:currentTrack()?.id,settings:{},assists:{},amounts:clone(setup.amounts),classes:clone(setup.selection.classesSelection||[]),fullGrid:!!setup.selection.fullGrid,weather:{},display:{}};
  for(const [k,v] of Object.entries(setup.settings))if(!['SESSSET_num_qual_sessions','SESSSET_num_race_sessions','SESSSET_run_warmup'].includes(k))target.settings[k]=v.currentValue;

  for(const [k,v] of Object.entries(setup.settings))target.display[k]=v.stringValue||String(v.currentValue)+(k.endsWith('_Length')?' min':'');
  target.display.car=(currentCar()?.desc||'')+' · '+parts(currentCar()||{})[2];target.display.track=currentTrack()?.eventName||currentTrack()?.trackName||'Saved circuit';
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
  const intents=[];if(target.track)intents.push({type:'track',id:target.track,display:target.display?.track});if(target.car)intents.push({type:'car',id:target.car,display:target.display?.car});
  for(const [s,v] of Object.entries(target.amounts))intents.push({type:'amount',session:s,value:v});
  if(Object.hasOwn(target.settings,'SESSSET_weather'))intents.push({type:'setting',key:'SESSSET_weather',value:target.settings.SESSSET_weather});
  for(const [k,v] of Object.entries(target.settings))if(k!=='SESSSET_weather')intents.push({type:'setting',key:k,value:v,display:target.display?.[k]});
  // Old exports may contain assists; never apply user driving preferences.
  if(target.classes)intents.push({type:'grid',classes:target.classes,fullGrid:target.fullGrid});
  for(const [s,nodes] of Object.entries(target.weather))for(const [n,fields] of Object.entries(nodes))for(const [k,v] of Object.entries(fields))intents.push({type:'weather',session:s,node:n,key:k,value:v});
  return intents;
}
function intentValue(i){if(i.type==='setting')return setup?.settings[i.key]?.currentValue;if(i.type==='assist')return setup?.assists[i.key]?.currentValue;if(i.type==='weather')return setup?.weather[i.session]?.[i.node]?.[i.key]?.currentValue;if(i.type==='amount')return setup?.amounts[i.session];if(i.type==='car')return currentCar()?.id;if(i.type==='track')return currentTrack()?.id;if(i.type==='grid')return JSON.stringify([setup?.selection.classesSelection,!!setup?.selection.fullGrid]);return null;}
function needed(i){if(i.type==='weatherPreset')return true;if(i.type==='grid')return JSON.stringify([i.classes,i.fullGrid])!==intentValue(i);if(['car','track'].includes(i.type))return i.id!==intentValue(i);return !sameNumber(i.value,intentValue(i));}
function describeIntent(i){if(i.type==='car')return ['Car',i.display||catalog?.cars.find(c=>c.id===i.id)?.desc||'Saved car'];if(i.type==='track')return ['Circuit',i.display||catalog?.tracks.find(t=>t.id===i.id)?.name||'Saved circuit'];if(i.type==='grid')return ['Opponent classes',(i.classes.join(', ')||'None')+' · '+(i.fullGrid?'fill grid':'season grid')];if(i.type==='amount')return [SESSION_NAMES[i.session],i.value===0?'Disabled':i.value+' session'+(i.value>1?'s':'')];if(i.type==='weatherPreset')return [SESSION_NAMES[i.session]+' forecast',i.preset.toLowerCase()];return [(i.type==='weather'?SESSION_NAMES[i.session]+' · '+i.node.replace('NODE_','')+' · ':'')+label(i.key),i.display||(i.key==='SESSSET_Finish_Criteria'&&i.value===2?'Time':String(i.value))];}
function reviewPlan(title,intents,description='These changes save to LMU. Review them before applying.'){
  if(saving||starting||serverBusy||applyingPlan){notify('Wait for the current operation before reviewing another plan.',true);return;}
  pendingPlan={title,intents:intents.filter(i=>i.type!=='assist').filter(needed)};$('planTitle').textContent=title;$('planDescription').textContent=description;$('planProgress').textContent='';$('planChanges').innerHTML=pendingPlan.intents.map(i=>{const [k,v]=describeIntent(i);return `<div class="plan-change"><span>${escapeHtml(k)}</span><strong>${escapeHtml(v)}</strong></div>`;}).join('')||'<p class="empty-note">This setup already matches. No changes are needed.</p>';
  $('applyPlanButton').disabled=!pendingPlan.intents.length||!setup||!menuReady;$('cancelPlanButton').textContent='Keep current setup';if(!menuReady)$('planProgress').textContent='You can review this plan now. Launch local play and return to its main menu to apply it.';$('planDialog').showModal();
}
async function applyIntent(i){
  if(!needed(i))return;
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
  try{for(const intent of pendingPlan.intents){if(cancelPlan)break;$('planProgress').textContent=`Applying ${applied+1} of ${pendingPlan.intents.length} · ${describeIntent(intent)[0]}`;await applyIntent(intent);if(cancelPlan)break;applied++;}lastUndo=null;$('lastChange').textContent=pendingPlan.title+' · '+applied+' changes confirmed';notify(cancelPlan?'Plan stopped. Confirmed changes remain in LMU.':'Plan applied. Your session is ready to review.');$('planDialog').close();}
  catch(e){notify(`Stopped after ${applied} confirmed changes. ${e.message}`,true);$('planProgress').textContent='Stopped. Confirmed changes remain in LMU. Refresh and review before trying again.';try{setup=await request('/api/session/setup');}catch{}}
  finally{applyingPlan=false;saving=false;renderContent();renderSummary();updateControls();$('applyPlanButton').disabled=true;$('cancelPlanButton').textContent='Close review';}
}
const templates={solo:{name:'Solo focus',icon:'◎',description:'30 minutes. No traffic. One section at a time.',intents:[{type:'amount',session:'PRACTICE',value:1},{type:'amount',session:'QUALIFY',value:0},{type:'amount',session:'WARMUP',value:0},{type:'amount',session:'RACE',value:0},{type:'setting',key:'SESSSET_Practice_Length',value:30},{type:'setting',key:'SESSSET_Num_Opponents',value:0}]},sprint:{name:'Sprint rehearsal',icon:'⚑',description:'Practice, qualifying and a 20-minute race.',intents:[{type:'amount',session:'PRACTICE',value:1},{type:'amount',session:'QUALIFY',value:1},{type:'amount',session:'WARMUP',value:0},{type:'amount',session:'RACE',value:1},{type:'setting',key:'SESSSET_Practice_Length',value:15},{type:'setting',key:'SESSSET_Qualify_Length',value:10},{type:'setting',key:'SESSSET_Finish_Criteria',value:2},{type:'setting',key:'SESSSET_race_time',value:20}]},wet:{name:'Wet-weather reps',icon:'☂',description:'A solo Practice session with a rainy forecast.',intents:[{type:'amount',session:'PRACTICE',value:1},{type:'amount',session:'QUALIFY',value:0},{type:'amount',session:'RACE',value:0},{type:'amount',session:'WARMUP',value:0},{type:'setting',key:'SESSSET_Num_Opponents',value:0},{type:'weatherPreset',session:'PRACTICE',preset:'RAINY'}]}};
function renderPlans(){
 $('quickPlans').innerHTML=Object.entries(templates).map(([id,p])=>`<button class="plan-card" data-template="${id}"><span class="plan-icon">${p.icon}</span><strong>${p.name}</strong><small>${p.description}</small><span class="card-arrow">Review preset ↗</span></button>`).join('');
 const plans=labPreferences.plans.filter(p=>`${p.name} ${p.target.display?.car||''} ${p.target.display?.track||''}`.toLowerCase().includes(presetQuery.toLowerCase()));
 $('savedPlans').innerHTML=plans.map(p=>`<article class="saved-plan"><div><h3>${escapeHtml(p.name)}</h3><p>${escapeHtml(p.target.display?.track||'Saved circuit')} · ${escapeHtml(p.target.display?.car||'Saved car')}</p></div><div class="preset-actions"><button class="button" data-plan="${escapeHtml(p.id)}">Use preset</button><button class="button quiet" data-rename-plan="${escapeHtml(p.id)}">Rename</button><button class="button quiet" data-replace-plan="${escapeHtml(p.id)}" ${!setup?'disabled':''}>Update from session</button><button class="icon-button" data-delete-plan="${escapeHtml(p.id)}" aria-label="Delete ${escapeHtml(p.name)}">×</button></div></article>`).join('')||'<p class="empty-note">No matching presets. Save the current session to reuse it later.</p>';updateLabControls();
}
function download(name,text,type='text/plain'){const url=URL.createObjectURL(new Blob([text],{type})),a=document.createElement('a');a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}
function tyrePreset(kind){
 for(const wheel of TYRE_WHEELS){if(kind!=='reset')$('tyreCondition'+wheel).value=kind==='worn'?60:100;$('tyreTemperature'+wheel).value=kind==='warm'?'70':'';}
 $('allCondition').value=kind==='worn'?60:100;$('allConditionValue').textContent=$('allCondition').value+'%';
 updateTyreSettings();notify('Tyre choices updated. Prepare tyres to apply them.');
}
// A blank temperature delegates to LMU's selected compound and warmer setting.
tyreOptions=function(){return {wheels:TYRE_WHEELS.map(wheel=>{
 const raw=$('tyreCondition'+wheel).value.trim(),conditionPercent=Number(raw),heat=$('tyreTemperature'+wheel).value.trim(),temperatureCelsius=heat===''?null:Number(heat);
 if(raw===''||!Number.isFinite(conditionPercent)||conditionPercent<0||conditionPercent>100)throw Error(wheel+' condition must be between 0 and 100 percent.');
 if(temperatureCelsius!==null&&(!Number.isFinite(temperatureCelsius)||temperatureCelsius<0||temperatureCelsius>150))throw Error(wheel+' temperature must be between 0 and 150 °C.');
 return {conditionPercent,temperatureCelsius};
})};};
updateTyreSettings=function(){
 const heats=TYRE_WHEELS.map(wheel=>$('tyreTemperature'+wheel).value.trim()),sameHeat=heats.every(v=>v===heats[0]);
 const heatLabel=sameHeat?(heats[0]===''?'Game default':heats[0]+'°C'):'Mixed';
 $('allTemperatureValue').textContent=heatLabel;$('allTemperature').setAttribute('aria-valuetext',heatLabel);
 if(sameHeat)$('allTemperature').value=heats[0]===''?'0':heats[0];
 for(const wheel of TYRE_WHEELS){$('tyreTemperature'+wheel).disabled=false;$('tyreTemperatureEnabled'+wheel).checked=$('tyreTemperature'+wheel).value.trim()!=='';}
 try{$('tyreSummary').textContent=tyreOptions().wheels.map((w,i)=>TYRE_WHEELS[i]+' '+w.conditionPercent+'% / '+(w.temperatureCelsius===null?'game default':w.temperatureCelsius+'°C')).join(' · ');}catch(e){$('tyreSummary').textContent=e.message;}
 savePracticePreferences();
};
for(const wheel of TYRE_WHEELS)if(!$('tyreTemperatureEnabled'+wheel).checked)$('tyreTemperature'+wheel).value='';
updateTyreSettings();
function editPreset(id,replace=false){const p=labPreferences.plans.find(p=>p.id===id);if(!p)return;editingPreset={id,replace};$('planName').value=p.name;$('savePlanTitle').textContent=replace?'Replace preset with current session':'Rename preset';$('savePlanDescription').textContent=replace?'This replaces the saved setup. Driving assists are excluded.':'Only the name changes; the saved session stays the same.';$('savePlanDialog').showModal();$('planName').focus();}

document.addEventListener('click',async e=>{
  const b=e.target.closest('button');if(!b||b.disabled)return;
  if(b.dataset.page)setPage(b.dataset.page);if(b.dataset.goto)setPage('setup',b.dataset.goto);
  if(b.dataset.close){const d=$(b.dataset.close);if(d.id==='planDialog'&&applyingPlan){cancelPlan=true;return;}d.close();}
  if(b.dataset.styleChoice){labPreferences.style=b.dataset.styleChoice;applyAppearance();persistPreferences();}
  if(b.dataset.theme){labPreferences.theme=b.dataset.theme;applyAppearance();persistPreferences();}if(b.dataset.density){labPreferences.density=b.dataset.density;applyAppearance();persistPreferences();}
  if(b.hasAttribute('data-favourite-cars')){favouriteCarsOnly=!favouriteCarsOnly;renderContent();}
  if(b.dataset.pin){const k=b.dataset.pin,at=labPreferences.pins.indexOf(k);if(at>=0)labPreferences.pins.splice(at,1);else if(labPreferences.pins.length<100)labPreferences.pins.push(k);persistPreferences();renderContent();}
  if(b.dataset.favCar)toggleFavourite('cars',b.dataset.favCar);if(b.dataset.favTrack)toggleFavourite('tracks',b.dataset.favTrack);if(b.dataset.favCheckpoint)toggleFavourite('checkpoints',b.dataset.favCheckpoint);
  if(b.dataset.car&&modelKey(catalog.cars.find(c=>c.id===b.dataset.car)||{})!==modelKey(currentCar()||{}))change({action:'car',id:b.dataset.car});if(b.dataset.track)change({action:'track',id:b.dataset.track});
  if(b.dataset.target)reviewPlan('Quick duration',[{type:'setting',key:b.dataset.target,value:Number(b.dataset.value)}]);
  if(b.dataset.template){const p=templates[b.dataset.template];reviewPlan(p.name,p.intents,p.description);}if(b.dataset.plan){const p=labPreferences.plans.find(p=>p.id===b.dataset.plan);try{validatePlan(p);reviewPlan(p.name,planIntents(p.target));}catch(err){notify(err.message,true);}}
  if(b.dataset.deletePlan){const p=labPreferences.plans.find(p=>p.id===b.dataset.deletePlan);if(p&&confirm('Delete saved preset “'+p.name+'”?')){const before=clone(labPreferences.plans);labPreferences.plans=labPreferences.plans.filter(v=>v.id!==p.id);if(!await persistPreferences())labPreferences.plans=before;renderPlans();}}
  if(b.dataset.weatherCopy){const source=setup.weather[weatherSession]||{},intents=[];if(b.dataset.weatherCopy==='points'){for(const node of Object.keys(source))if(node!==weatherNode)for(const [key,v] of Object.entries(source[weatherNode]||{}))intents.push({type:'weather',session:weatherSession,node,key,value:v.currentValue});}else{for(const [node,fields] of Object.entries(source))for(const [key,v] of Object.entries(fields))intents.push({type:'weather',session:b.dataset.weatherCopy,node,key,value:v.currentValue});}reviewPlan('Copy weather conditions',intents);}
  if(b.dataset.tyrePreset)tyrePreset(b.dataset.tyrePreset);if(b.dataset.renamePlan)editPreset(b.dataset.renamePlan);if(b.dataset.replacePlan)editPreset(b.dataset.replacePlan,true);
  if(b.dataset.pickCheckpoint){$('cp').value=b.dataset.pickCheckpoint;updateDestination();renderCheckpoints(labState);setPage('practice');notify('Checkpoint selected. Place it from the garage when ready.');}
  if(b.dataset.logFilter){logFilter=b.dataset.logFilter;for(const v of document.querySelectorAll('[data-log-filter]'))v.classList.toggle('active',v===b);renderLabActivity();}

  if(b.dataset.classic)$('classicFrame').src=b.dataset.classic;
});
document.addEventListener('change',e=>{const t=e.target;if(t.id==='circuitSelect'){const next=catalog.tracks.find(v=>circuitName(v)===t.value&&v.owned);if(next)change({action:'track',id:next.id});}if(t.id==='vehicleSeason'){vehicleSeason=t.value;renderContent();}if(t.id==='vehicleClass'){vehicleClass=t.value;renderContent();}if(t.id==='librarySort'){librarySort=t.value;renderLibrary();}});
document.addEventListener('input',e=>{if(e.target.id==='librarySearch'){libraryQuery=e.target.value;renderLibrary();}if(e.target.id==='presetSearch'){presetQuery=e.target.value;renderPlans();}if(e.target.id==='allTemperature'){for(const w of TYRE_WHEELS)$('tyreTemperature'+w).value=e.target.value==='0'?'':e.target.value;updateTyreSettings();}if(e.target.id==='allCondition'){for(const w of TYRE_WHEELS)$('tyreCondition'+w).value=e.target.value;$('allConditionValue').textContent=e.target.value+'%';updateTyreSettings();}});
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
 }catch(e){labPreferences.plans=before;notify(e.message,true);}finally{$('confirmSavePlan').disabled=false;}
});

$('importPlansButton').addEventListener('click',()=>$('importPlansFile').click());
$('importPlansFile').addEventListener('change',async e=>{try{const f=e.target.files[0];if(!f)return;if(f.size>512000)throw Error('This plan file is too large.');const json=JSON.parse(await f.text());if(json.schema!==1||!Array.isArray(json.plans)||json.plans.length+labPreferences.plans.length>40)throw Error('Use a Lab 02 export with at most 40 total plans.');const plans=json.plans.map(p=>({...validatePlan(p),id:crypto.randomUUID()}));labPreferences.plans.push(...plans);if(!await persistPreferences()){const ids=new Set(plans.map(p=>p.id));labPreferences.plans=labPreferences.plans.filter(p=>!ids.has(p.id));return;}renderPlans();notify(plans.length+' plans imported.');}catch(err){notify(err.message,true);}finally{e.target.value='';}});
$('exportPlansButton').addEventListener('click',()=>download('inactive-reset-plans.json',JSON.stringify({schema:1,plans:labPreferences.plans},null,2),'application/json'));
$('exportActivityButton').addEventListener('click',()=>download('inactive-reset-activity.txt','Inactive Reset · Lab 02\n'+new Date().toISOString()+'\n\n'+activityText()));
$('favouritesOnly').addEventListener('click',()=>{onlyFavourites=!onlyFavourites;$('favouritesOnly').setAttribute('aria-pressed',String(onlyFavourites));$('favouritesOnly').textContent=onlyFavourites?'★ Favourites':'☆ Favourites';renderLibrary();});
$('quickCaptureButton').addEventListener('click',()=>{if(!labState?.session?.capturable){notify('Drive to a capturable position in Practice first.',true);return;}$('capName').value=Math.round(labState.session.lapDistance||0)+'m-'+new Date().toISOString().replace(/[-:.TZ]/g,'');capture();});

$('refreshLabButton').addEventListener('click',async()=>{await loadSetup();await poll();if(['practice','library','activity'].includes(labPage))await refresh();notify('Refreshed from LMU.');});
$('compareButton').addEventListener('click',()=>{$('classicFrame').src='/session';$('compareDialog').showModal();});
$('compareDialog').addEventListener('close',()=>{$('classicFrame').src='about:blank';loadSetup();});
$('dismissLabToast').addEventListener('click',()=>$('labToast').hidden=true);
async function initLab(){try{const p=await request('/api/lab/preferences');labPreferences=p;preferencesReady=true;}catch(e){notify('Saved Lab choices could not be read. '+e.message,true);}applyAppearance();renderHome();setPage('home');}
initLab();
