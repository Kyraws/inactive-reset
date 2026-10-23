'use strict';
let officialSessions={events:[],warnings:[]},officialReady=false,officialRefreshing=false,officialAttempt=0,officialError='';
const OFFICIAL_CATEGORIES={beginner:'Beginner',intermediate:'Intermediate',advanced:'Advanced',weekly:'Weekly',special:'Special event'};
const OFFICIAL_POINTS=['START','NODE_25','NODE_50','NODE_75','FINISH'];
const OFFICIAL_WEATHER={sky:'WNV_SKY',rainChange:'WNV_RAIN_CHANCE',temperature:'WNV_TEMPERATURE',humidity:'WNV_HUMIDITY',windSpeed:'WNV_WINDSPEED',windDirection:'WNV_WINDDIRECTION'};

async function loadOfficialSessions(){
  try{officialSessions=await request('/api/official-sessions');officialError='';}
  catch(e){officialError=e.message;}
  officialReady=true;renderOfficialSessions();maybeRefreshOfficialSessions();
}
function maybeRefreshOfficialSessions(){
  if(!officialReady||!gameRunning||officialRefreshing||Date.now()-officialAttempt<60000)return;
  const age=Date.now()-Date.parse(officialSessions.fetchedAt||'');
  const missingRoad=(officialSessions.events||[]).some(e=>!e.stale&&(e.sessions||[]).some(s=>s.realRoad==='natural'&&!Object.hasOwn(s,'practiceRealRoad')));
  if(missingRoad||!Number.isFinite(age)||age>15*60000)refreshOfficialSessions();
}
async function refreshOfficialSessions(){
  if(officialRefreshing)return;
  officialRefreshing=true;officialAttempt=Date.now();officialError='';renderOfficialSessions();
  try{officialSessions=await request('/api/official-sessions/refresh',{});}
  catch(e){officialError=e.message;}
  finally{officialRefreshing=false;renderOfficialSessions();}
}
function officialPresetId(event,session){return event.id+':'+session.type;}
function officialNextTime(event){
  const future=(event.times||[]).map(t=>new Date(t)).filter(t=>Number.isFinite(t.getTime())&&t.getTime()>Date.now()).sort((a,b)=>a-b);
  return future.length?'Next: '+future[0].toLocaleString([], {weekday:'short',hour:'2-digit',minute:'2-digit'}):'Check LMU for the next start';
}
function officialButtons(event){
  return (event.sessions||[]).map(s=>`<button class="button" data-official-preset="${escapeHtml(officialPresetId(event,s))}" ${officialRefreshing||saving||starting||serverBusy||applyingPlan?'disabled':''}>${s.type==='Qualifying'?'Qualifying':'Race'} practice</button>`).join('');
}
function officialTrack(event){return event.content?.track?.subItems?.[0]?.name||event.content?.track?.name||'Unknown circuit';}
function officialRoadName(road){return ({green:'Green','preset:LIGHT.RRBIN':'Light','preset:MEDIUM.RRBIN':'Medium','preset:HEAVY.RRBIN':'Heavy','preset:SATURATED.RRBIN':'Saturated'})[road]||'Unresolved';}
function officialRoadLabel(session){return officialRoadName(session.practiceRealRoad||(session.realRoad!=='natural'?session.realRoad:null))+(session.realRoad==='natural'?' base · inherited online':'');}
function renderOfficialSessions(){
  const events=officialSessions.events||[];
  $('refreshOfficialButton').disabled=officialRefreshing;
  $('refreshOfficialButton').textContent=officialRefreshing?'Refreshing…':'Refresh events';
  const saved=officialSessions.fetchedAt?new Date(officialSessions.fetchedAt).toLocaleString():'';
  $('officialStatus').textContent=[officialRefreshing?'Refreshing official events…':saved?'Saved '+saved+'. Available offline.':'Launch LMU and refresh to save official sessions.',officialError,...(officialSessions.warnings||[])].filter(Boolean).join(' ');
  $('officialEvents').innerHTML=events.map(e=>`<article class="settings-card official-event"><div class="official-event-meta"><span>${escapeHtml(OFFICIAL_CATEGORIES[e.category]||'Official')}</span>${e.stale?'<span>Saved · refresh unavailable</span>':''}</div><h3>${escapeHtml(e.title)}</h3><p>${escapeHtml(officialTrack(e))}</p><p class="small-note">${escapeHtml(officialNextTime(e))}</p><div class="preset-actions">${officialButtons(e)}</div></article>`).join('')||'<p class="empty-note">No official sessions saved yet. Launch local play, wait for LMU’s main menu, then refresh events.</p>';
  const query=typeof presetQuery==='string'?presetQuery.toLowerCase():'';
  const presets=events.flatMap(e=>(e.sessions||[]).map(s=>({event:e,session:s}))).filter(({event:e,session:s})=>`${e.title} ${officialTrack(e)} ${s.type}`.toLowerCase().includes(query));
  $('officialPresets').innerHTML=presets.map(({event:e,session:s})=>`<article class="saved-plan"><div><h3>${escapeHtml(e.title)} · ${escapeHtml(s.type)}</h3><p>${escapeHtml(officialTrack(e))} · ${escapeHtml(s.duration)} · ${escapeHtml(s.timeOfDay)} · solo Practice</p><p class="small-note">Starting grip: ${escapeHtml(officialRoadLabel(s))}</p>${e.stale?'<p class="small-note">Saved conditions; source refresh unavailable.</p>':''}</div><div class="preset-actions"><button class="button" data-official-preset="${escapeHtml(officialPresetId(e,s))}" ${saving||starting||serverBusy||applyingPlan?'disabled':''}>Use preset</button></div></article>`).join('')||'<p class="empty-note">No matching official presets. Refresh official events on Overview to save them.</p>';
}
function officialNumber(value,label,min,max){
  if(value===null||value===undefined||value===''||!Number.isFinite(Number(value))||Number(value)<min||Number(value)>max)throw Error('Unsupported official '+label+'.');
  return Number(value);
}
function officialDuration(text){
  if(typeof text!=='string'||! /^(?:\d+h)?(?:\d+m)?(?:\d+s)?$/.test(text)||!text)throw Error('Unsupported official session duration.');
  const units={h:60,m:1,s:1/60};let minutes=0;for(const m of text.matchAll(/(\d+)([hms])/g))minutes+=Number(m[1])*units[m[2]];
  if(!Number.isInteger(minutes)||minutes<1||minutes>1440)throw Error('This session duration cannot be reproduced in local Practice.');
  return minutes;
}
function officialTime(text){
  const match=/^(\d{1,2})h(?:(\d{1,2})m)?$/.exec(text||'');
  if(!match||Number(match[1])>23||Number(match[2]||0)>59)throw Error('Unsupported official session start time.');
  return Number(match[1])*60+Number(match[2]||0);
}
function officialPlan(event,session){
  if(!setup||!catalog)throw Error('Open LMU and wait for its single-player main menu to prepare this preset. Its saved conditions are available offline.');
  const scene=event.content?.track?.subItems?.[0]?.scn;
  const tracks=catalog.tracks.filter(t=>t.owned&&t.sceneDesc?.toLowerCase()===scene?.toLowerCase());
  if(tracks.length!==1)throw Error('Install and own the exact '+officialTrack(event)+' layout before using this preset.');
  const packages=new Set((event.content.cars||[]).flatMap(c=>[c.name,c.friendly]).filter(Boolean).map(s=>s.toLowerCase()));
  const eligible=catalog.cars.filter(c=>c.isOwned&&(c.vehFile||'').replaceAll('\\','/').split('/').some(p=>packages.has(p.toLowerCase())));
  const car=eligible.find(c=>c.id===currentCar()?.id)||eligible.find(c=>c.primaryVehFile)||eligible[0];
  if(!car)throw Error('Install and own one of this event’s eligible cars before using its preset.');
  const s=event.settings||{},settings={SESSSET_Practice_Length:officialDuration(session.duration),SESSSET_Num_Opponents:0,SESSSET_weather:4,SESSSET_timescaled_weather:1};
  const rules={raceTimeScale:['SESSSET_race_timescale',0,60],realRoadScale:['SESSSET_realroad_timescale_practice',0,60],fuelConsumptionMultiplier:['SESSSET_Fuel_Usage',0,7],tyreWearMultiplier:['SESSSET_Tire_Wear',0,7],damageMultiplier:['SESSSET_Damage_Multi',0,100]};
  for(const [source,[key,min,max]] of Object.entries(rules))if(s[source]!==undefined)settings[key]=officialNumber(s[source],source,min,max);
  if(typeof s.tyreWarmers==='boolean')settings.SESSSET_tire_warmers=s.tyreWarmers?1:0;
  for(const key of Object.keys(settings))if(!setup.settings[key])throw Error('LMU does not expose '+label(key)+' for this preset.');
  if(!Array.isArray(session.weather)||session.weather.length!==5)throw Error('This preset needs five valid official weather points.');
  const weather={};const ranges={sky:[0,8],rainChange:[0,100],temperature:[-20,60],humidity:[0,100],windSpeed:[0,100],windDirection:[0,7]};
  session.weather.forEach((w,index)=>{const point={};for(const [source,key] of Object.entries(OFFICIAL_WEATHER)){
    point[key]=officialNumber(w[source],source,...ranges[source]);
    if(!setup.weather.PRACTICE?.[OFFICIAL_POINTS[index]]?.[key])throw Error('LMU does not expose all required Practice weather controls.');
  }weather[OFFICIAL_POINTS[index]]=point;});
  const road=session.practiceRealRoad||(session.realRoad!=='natural'?session.realRoad:null);
  if(!road)throw Error(Object.hasOwn(session,'practiceRealRoad')?'This event inherits grip but supplies no explicit starting surface. Choose the track grip manually.':'Refresh official events to load the inherited starting grip for this preset.');
  if(!['green','preset:LIGHT.RRBIN','preset:MEDIUM.RRBIN','preset:HEAVY.RRBIN','preset:SATURATED.RRBIN'].includes(road))throw Error('This preset uses an unsupported track surface.');
  return {car:car.id,track:tracks[0].id,settings,assists:{},amounts:{PRACTICE:1,QUALIFY:0,WARMUP:0,RACE:0},classes:[],fullGrid:false,weather:{PRACTICE:weather},display:{car:car.desc,track:officialTrack(event)},practiceConditions:{time:officialTime(session.timeOfDay),road,waterDepth:officialNumber(session.waterDepth,'track water depth',-0.01,1),loadTemperatures:officialNumber(session.loadTempsFromFile??0,'surface temperature source',0,1)===1}};
}
function reviewOfficialPreset(id){
  for(const event of officialSessions.events||[])for(const session of event.sessions||[])if(officialPresetId(event,session)===id){
    try{const target=officialPlan(event,session),intents=planIntents(target),weather=intents.findIndex(i=>i.type==='weather');intents.splice(weather<0?intents.length:weather,0,{type:'practiceConditions',...target.practiceConditions});const inherited=session.realRoad==='natural'?' Starting grip uses the event’s '+officialRoadName(target.practiceConditions.road)+' base. Additional rubber or water carried over from online traffic is not reproduced.':'';if(reviewPlan(event.title+' · '+session.type,intents,'Solo Practice using this session’s time, surface, five weather points and supported simulation rules.'+inherited+' Online BoP, fixed setups and tyre allocation are not copied.'))pendingPlan.official=true;}
    catch(e){notify(e.message,true);}return;
  }
}
document.addEventListener('click',e=>{const button=e.target.closest('button');if(!button||button.disabled)return;if(button.id==='refreshOfficialButton')refreshOfficialSessions();if(button.dataset.officialPreset)reviewOfficialPreset(button.dataset.officialPreset);});
