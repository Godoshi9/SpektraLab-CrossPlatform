'use strict';
const $ = id => document.getElementById(id);
let serial = 0;
const pending = new Map();
window.nativeReply = message => {
  const item = pending.get(message.id);
  if (!item) return;
  pending.delete(message.id);
  message.error ? item.reject(new Error(message.error)) : item.resolve(message.result);
};
function native(action, payload = {}) {
  return new Promise((resolve, reject) => {
    const id = ++serial;
    pending.set(id, {resolve, reject});
    const message = {id, action, payload};
    if (window.chrome?.webview) window.chrome.webview.postMessage(message);
    else if (window.Spektra) window.Spektra.postMessage(JSON.stringify(message));
    else { pending.delete(id); reject(new Error('请在 SpektraLab 应用中打开。')); }
  });
}
window.chrome?.webview?.addEventListener('message', event => window.nativeReply(event.data));
const defaults = () => ({film_stock:'kodak_portra_400', print_stock:'kodak_portra_endura', scan_film:false,
  exposure_compensation_ev:0, print_exposure:1, grain_active:true, grain_amount:1,
  halation_active:true, halation_amount:1, glare_active:false, glare_amount:0,
  y_filter_shift:0, m_filter_shift:0, film_format_mm:36});
let settings=defaults(), renderedSettings=defaults(), catalog={films:[],papers:[]};
let loaded=false, locked=false, rendering=false, queued=false, filter='all', mode='after', zoomed=false;
let ready=false, initializing=false, undo=[], renderTimer, toastTimer;
let width=0,height=0;
function toast(text) { $('toast').textContent=text; $('toast').classList.remove('hidden'); clearTimeout(toastTimer); toastTimer=setTimeout(()=>$('toast').classList.add('hidden'),8000); }
function busy(value,text='正在显影…') { $('busy').classList.toggle('hidden',!value); $('busy-text').textContent=text; refreshEnabled(); }
function refreshEnabled() {
  $('open').disabled=$('empty-open').disabled=initializing||locked||rendering;
  $('export').disabled=!loaded||locked||rendering||queued;
  $('reset').disabled=!loaded||locked;
  $('undo').disabled=!loaded||locked||!undo.length;
  $('zoom').disabled=!loaded;
  document.querySelectorAll('[data-view]').forEach(b=>b.disabled=!loaded);
  $('paper').disabled=!ready||locked||settings.scan_film;
  ['brightness','warmth','tint','glare'].forEach(id=>$(id).disabled=locked||settings.scan_film);
  ['exposure','grain','halation','format'].forEach(id=>$(id).disabled=locked);
  document.querySelectorAll('.stock').forEach(b=>b.disabled=locked);
}
function filmKind(f) { return f.monochrome?'mono':f.positive?'positive':'negative'; }
function drawStocks() {
  const query=$('search').value.trim().toLowerCase();
  const films=catalog.films.filter(f=>(filter==='all'||filmKind(f)===filter)&&f.name.toLowerCase().includes(query));
  $('stocks').replaceChildren(); $('stock-count').textContent=`${catalog.films.length} 种胶片`;
  films.forEach(f=>{
    const button=document.createElement('button');button.className='stock';button.dataset.kind=filmKind(f);
    button.classList.toggle('active',settings.film_stock===f.id);button.setAttribute('aria-pressed',settings.film_stock===f.id);
    const kind=document.createElement('span');kind.className='stock-kind';kind.textContent=f.monochrome?'BLACK & WHITE':f.positive?'REVERSAL FILM':'COLOR NEGATIVE';
    const name=document.createElement('strong');name.textContent=f.name.replace(/^(Kodak|Fujifilm|Ilford)\s+/i,'');
    const brand=document.createElement('small');brand.textContent=f.name.match(/^(Kodak|Fujifilm|Ilford)/i)?.[0]||'SpektraLab';
    button.append(kind,name,brand);button.onclick=()=>change(()=>{
      settings.film_stock=f.id;settings.scan_film=f.positive;
      if(f.monochrome) settings.print_stock='ilford_multigrade_iv_rc';
      else if(catalog.papers.find(p=>p.id===settings.print_stock)?.monochrome) settings.print_stock='kodak_portra_endura';
      drawStocks();drawPaper();syncControls();
    });$('stocks').append(button);
  });
  if(!films.length){const empty=document.createElement('p');empty.className='no-results';empty.textContent='没有找到这卷胶片';$('stocks').append(empty);}
  refreshEnabled();
}
function drawPaper() {
  const mono=catalog.films.find(f=>f.id===settings.film_stock)?.monochrome||false;
  $('paper').replaceChildren();
  catalog.papers.filter(p=>p.monochrome===mono).forEach(p=>{
    const option=document.createElement('option');option.value=p.id;option.textContent=p.name;$('paper').append(option);
  });$('paper').value=settings.print_stock;
  $('paper-hint').textContent=settings.scan_film?'正片直接扫描，无需印相纸。':'胶片与相纸，共同决定最终色彩。';
  refreshEnabled();
}
function syncControls() {
  $('brightness').value=-Math.log2(settings.print_exposure);$('exposure').value=settings.exposure_compensation_ev;
  $('warmth').value=settings.y_filter_shift;$('tint').value=settings.m_filter_shift;
  $('grain').value=settings.grain_active?settings.grain_amount:0;
  $('halation').value=settings.halation_active?settings.halation_amount:0;
  $('glare').value=settings.glare_active?settings.glare_amount:0;$('format').value=settings.film_format_mm;
  labels();refreshEnabled();
}
function labels() {
  ['brightness','exposure'].forEach(id=>$(id+'-value').textContent=(+$ (id).value).toFixed(2)+(id==='exposure'?' EV':''));
  ['warmth','tint'].forEach(id=>$(id+'-value').textContent=Math.round(+$(id).value*100));
  ['grain','halation','glare'].forEach(id=>$(id+'-value').textContent=Math.round(+$(id).value*100)+'%');
}
function snapshot(){return JSON.parse(JSON.stringify(settings));}
function change(fn,history=true){if(locked)return;const before=snapshot();fn();if(JSON.stringify(before)===JSON.stringify(settings))return;
  if(history&&loaded){undo.push(before);if(undo.length>32)undo.shift();}
  schedule();
}
function schedule(){if(!loaded){refreshEnabled();return;}queued=true;clearTimeout(renderTimer);renderTimer=setTimeout(render,220);refreshEnabled();}
async function render(){
  if(rendering||locked||!loaded)return;
  queued=false;rendering=true;const wanted=snapshot();busy(true);
  try{const result=await native('render',{settings:wanted});if(!queued){publish(result);renderedSettings=wanted;}}
  catch(e){if(!queued){settings={...renderedSettings};syncControls();drawStocks();drawPaper();}toast('显影失败：'+e.message);}
  finally{rendering=false;busy(false);if(queued)render();}
}
function publish(result){
  if(result.image)$('rendered').src=result.image;
  if(result.original)$('original').src=result.original;
  width=result.width||width;height=result.height||height;
  if(result.name)$('filename').textContent=result.name;
  $('dimensions').textContent=`${width} × ${height} · sRGB`;
  $('export-size').textContent=`${width} × ${height} 像素 · sRGB · 8-bit。导出不覆盖源照片。`;
  $('render-time').textContent=`${Math.round(result.elapsed||0)} ms`;
  $('image-note').textContent=catalog.films.find(f=>f.id===settings.film_stock)?.name||'胶片效果';
  $('empty').classList.add('hidden');$('picture').classList.remove('hidden');$('image-label').classList.remove('hidden');
  loaded=true;fit();setView(mode);refreshEnabled();
}
async function openPhoto(){
  if(initializing||locked||rendering)return;
  if(!ready){await init();if(!ready)return;}
  locked=true;busy(true,'正在导入照片…');const wanted=snapshot();
  try{const result=await native('open',{settings:wanted});if(result&&!result.cancelled){undo=[];publish(result);renderedSettings=wanted;zoomed=false;fit();if(result.notice)toast(result.notice);}}
  catch(e){toast('无法导入：'+e.message);}finally{locked=false;busy(false);}
}
function fit(){
  if(!loaded)return;const rect=$('picture').getBoundingClientRect();
  const scale=zoomed?1:Math.min(rect.width/width,rect.height/height,1);
  const wrap=$('image-wrap');wrap.style.width=Math.max(1,Math.round(width*scale))+'px';wrap.style.height=Math.max(1,Math.round(height*scale))+'px';
  wrap.style.maxWidth=zoomed?'none':'100%';wrap.style.maxHeight=zoomed?'none':'100%';
  $('picture').style.alignItems=zoomed?'flex-start':'center';$('picture').style.justifyContent=zoomed?'flex-start':'center';
  $('zoom').textContent=zoomed?'100% · 点击适应':'适应画布';
}
function setView(view){mode=view;const before=$('before-wrap');before.style.display=view==='after'?'none':'block';before.style.clipPath=view==='before'?'none':`inset(0 ${100-+$('split').value}% 0 0)`;
  $('split-line').style.display=view==='split'?'block':'none';$('split-line').style.left=$('split').value+'%';
  $('compare-control').classList.toggle('hidden',view!=='split');$('image-label').textContent=view==='before'?'原始照片':view==='split'?'原图 / 胶片效果':'胶片效果';
  document.querySelectorAll('[data-view]').forEach(b=>b.classList.toggle('active',b.dataset.view===view));fit();
}
$('open').onclick=$('empty-open').onclick=openPhoto;
document.querySelectorAll('[data-tab]').forEach(b=>b.onclick=()=>{document.querySelectorAll('[data-tab]').forEach(x=>x.classList.toggle('active',x===b));document.querySelectorAll('.panel').forEach(p=>p.classList.toggle('active',p.id==='panel-'+b.dataset.tab));});
document.querySelectorAll('[data-filter]').forEach(b=>b.onclick=()=>{filter=b.dataset.filter;document.querySelectorAll('[data-filter]').forEach(x=>x.classList.toggle('active',x===b));drawStocks();});
$('search').oninput=drawStocks;$('paper').onchange=()=>change(()=>settings.print_stock=$('paper').value);
const bindings={brightness:v=>settings.print_exposure=2**(-v),exposure:v=>settings.exposure_compensation_ev=v,
  warmth:v=>settings.y_filter_shift=v,tint:v=>settings.m_filter_shift=v,
  grain:v=>{settings.grain_active=v>0;settings.grain_amount=v;},halation:v=>{settings.halation_active=v>0;settings.halation_amount=v;},
  glare:v=>{settings.glare_active=v>0;settings.glare_amount=v;}};
for(const[id,update]of Object.entries(bindings)){
  let initial=null;
  $(id).addEventListener('pointerdown',()=>initial=snapshot());
  $(id).addEventListener('keydown',()=>{if(!initial)initial=snapshot();});
  $(id).oninput=()=>{update(+$(id).value);labels();schedule();};
  $(id).onchange=()=>{if(initial&&loaded&&JSON.stringify(initial)!==JSON.stringify(settings)){undo.push(initial);if(undo.length>32)undo.shift();}initial=null;refreshEnabled();};
}
$('format').onchange=()=>change(()=>settings.film_format_mm=+$('format').value);
$('reset').onclick=()=>change(()=>{settings=defaults();drawStocks();drawPaper();syncControls();});
$('undo').onclick=()=>{if(!undo.length||locked)return;settings=undo.pop();drawStocks();drawPaper();syncControls();schedule();};
document.querySelectorAll('[data-view]').forEach(b=>b.onclick=()=>setView(b.dataset.view));$('split').oninput=()=>setView('split');
$('zoom').onclick=()=>{zoomed=!zoomed;fit();};new ResizeObserver(fit).observe($('stage'));
$('export').onclick=()=>{if(!locked&&!rendering&&!queued)$('export-dialog').showModal();};
$('save').onclick=async()=>{
  if(locked||rendering||queued)return;$('export-dialog').close();locked=true;busy(true,'正在保存作品…');
  try{const result=await native('export',{format:$('export-format').value});if(!result.cancelled)toast('已保存：'+result.name);}
  catch(e){toast('保存失败：'+e.message);}finally{locked=false;busy(false);}
};
$('about').onclick=()=>$('about-dialog').showModal();$('licenses').onclick=async()=>{try{$('license-text').textContent=await native('licenses');$('license-text').classList.remove('hidden');}catch(e){toast(e.message);}};
document.addEventListener('keydown',e=>{
  if(e.target.matches('input,select,textarea'))return;
  if(e.ctrlKey&&e.key.toLowerCase()==='o'){e.preventDefault();openPhoto();}
  if(e.ctrlKey&&e.key.toLowerCase()==='e'&&!$('export').disabled){e.preventDefault();$('export').click();}
  if(e.ctrlKey&&e.key.toLowerCase()==='z'){e.preventDefault();$('undo').click();}
  if(e.code==='Space'&&loaded&&!e.repeat){e.preventDefault();setView('before');}
});
document.addEventListener('keyup',e=>{if(e.code==='Space'&&loaded&&!e.target.matches('input,select,textarea'))setView('after');});
async function init(){if(initializing)return;initializing=true;refreshEnabled();try{
  const info=await native('init');catalog=info.catalog;
  $('app-version').textContent=info.version||'0.1.0';
  catalog.films.sort((a,b)=>a.name.localeCompare(b.name));catalog.papers.sort((a,b)=>a.name.localeCompare(b.name));
  ready=true;$('status').textContent=`${info.platform} · 本地胶片引擎已就绪`;
  if(info.sample){settings={...defaults(),...info.sample.settings};renderedSettings={...settings};publish(info.sample);}
  drawStocks();drawPaper();syncControls();
}catch(e){ready=false;$('status').textContent='引擎未就绪 · 可点击导入重试';toast('无法启动：'+e.message);}
finally{initializing=false;refreshEnabled();}}
init();
