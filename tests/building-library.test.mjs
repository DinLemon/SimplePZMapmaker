import test from 'node:test';
import assert from 'node:assert/strict';
import {mountBuildingLibrary,buildingLibraryItems} from '../src/SimplePZMapmaker.Web/wwwroot/building-library.js';
import {HousingGallery} from '../src/SimplePZMapmaker.Web/wwwroot/housing-gallery.js';

test('saving an edited legacy template upgrades the project for models and furnished objects',async()=>{
 const {templateProjectVersion}=await import('../src/SimplePZMapmaker.Web/wwwroot/building-library.js');
 const b={design:{floors:[{staticModels:[{profile:'pickup'}],objects:[],edges:[]}]}};
 assert.equal(templateProjectVersion(b),6);
 b.design.floors[0].objects=[{kind:'furniture'}];assert.equal(templateProjectVersion(b),14);
});

// A small DOM adapter keeps the interaction tests dependency-free. Browser QA
// additionally exercises the real dialog, focus, CSS and canvas implementation.
class Element extends EventTarget {
 constructor(tag,document){super();this.tagName=tag.toUpperCase();this.ownerDocument=document;this.children=[];this.attributes=new Map();this.dataset={};this.value='';this.textContent='';this.hidden=false;this.disabled=false;this.tabIndex=0;}
 setAttribute(key,value){this.attributes.set(key,String(value));if(key==='id')this.id=String(value);}
 getAttribute(key){return key==='id'?this.id??null:this.attributes.get(key)??null;}
 removeAttribute(key){this.attributes.delete(key);}
 append(...children){for(const child of children){child.remove();child.parentElement=this;this.children.push(child);}}
 insertBefore(child,next){child.remove();child.parentElement=this;const i=this.children.indexOf(next);this.children.splice(i<0?this.children.length:i,0,child);return child;}
 before(child){this.parentElement.insertBefore(child,this);}
 after(child){this.parentElement.insertBefore(child,this.parentElement.children[this.parentElement.children.indexOf(this)+1]);}
 remove(){if(this.parentElement){const list=this.parentElement.children;list.splice(list.indexOf(this),1);this.parentElement=null;}}
 replaceChildren(...children){for(const child of [...this.children])child.remove();this.append(...children);}
 matches(selector){if(selector.startsWith('#'))return this.id===selector.slice(1);if(selector.startsWith('.'))return this.className?.split(' ').includes(selector.slice(1));const attr=selector.match(/^\[([^=\]]+)(?:="?([^"\]]+)"?)?\]$/);return attr?this.getAttribute(attr[1])!==null&&(attr[2]===undefined||this.getAttribute(attr[1])===attr[2]):this.tagName===selector.toUpperCase();}
 querySelectorAll(selector){return this.children.flatMap(child=>[...(child.matches(selector)?[child]:[]),...child.querySelectorAll(selector)]);}
 querySelector(selector){return this.querySelectorAll(selector)[0]??null;}
 focus(){this.ownerDocument.activeElement=this;}
 click(){if(this.disabled)return;this.onclick?.(new Event('click'));this.dispatchEvent(new Event('click'));}
 showModal(){this.open=true;}
 close(){if(!this.open)return;this.open=false;this.dispatchEvent(new Event('close'));}
 get isConnected(){return !!this.parentElement;}
 getContext(){return this.context??={draws:0,fillRect(){},drawImage(){this.draws++;}};}
}
function fixture(){
 const document={createElement:tag=>new Element(tag,document),getElementById:id=>document.body.querySelector('#'+id)};document.body=document.createElement('body');
 const el=(tag,id,parent=document.body)=>{const node=document.createElement(tag);node.id=id;parent.append(node);return node;};
 const dialog=el('dialog','building-list-dialog');el('div','heading',dialog).className='dialog-heading';
 el('button','new-building-template',dialog);const hint=el('p','create-hint',dialog);hint.className='hint';hint.textContent='Design and save a template separately.';
 const presets=el('section','presets-section',dialog);presets.className='housing-catalog';
 el('select','housing-preset-category',presets);el('p','housing-preset-state',presets);el('div','building-presets-grid',presets);
 el('input','building-list-search',dialog);el('div','building-list',dialog);
 return {document,dialog,$:id=>document.getElementById(id)};
}
const house=(id,name,kind='house')=>({id,name,kind,width:12,height:8,design:{minLevel:0,floors:[{cells:'1'.repeat(96)}]}});
const presets=[{id:'village',name:'Timber home',category:'village',description:'A rural cottage',width:12,height:8,levels:1,references:[]},{id:'urban',name:'Brick block',category:'urban',description:'Apartments around a courtyard',width:24,height:16,levels:4,references:['https://example.com/reference']}];
const flush=()=>new Promise(resolve=>setImmediate(resolve));
const deferred=()=>{let resolve,reject;const promise=new Promise((yes,no)=>{resolve=yes;reject=no;});return {promise,resolve,reject};};
function setup({project={buildings:[house('map-1','Map cottage')],buildingLibrary:[house('mine-1','Library cottage')]},api=async path=>path.startsWith('/api/preview/')?{sprites:[]}:house('preview','Preview'),request=async()=>({ok:true,json:async()=>presets}),images={meta:new Map(),load:async()=>true,draw(){}} ,...callbacks}={}){
 const f=fixture(),gallery=new HousingGallery({api,select:callbacks.onSelectPreset??(()=>{}),root:f.document,request,images});
 const library=mountBuildingLibrary({dialog:f.dialog,getProject:()=>project,housingGallery:gallery,...callbacks});
 return {...f,gallery,library,project};
}

test('search matches all words across names and readable types without mixing map and library records',()=>{
 const p={buildings:[house('a','East Lodge'),house('b','East Lodge','apartments')],buildingLibrary:[house('c','East Lodge','clinic')]};
 assert.deepEqual(buildingLibraryItems(p,{tab:'map',search:'  EAST apartment  '}).map(b=>b.id),['b']);
 assert.deepEqual(buildingLibraryItems(p,{tab:'mine',search:'hospital lodge'}).map(b=>b.id),['c']);
 assert.deepEqual(buildingLibraryItems(p,{tab:'map',search:'hospital'}),[]);
});

test('missing projects, optional arrays and invalid entries produce safe empty results',()=>{
 for(const p of [null,undefined,{}, {buildings:null,buildingLibrary:{}},{buildings:[null,{}],buildingLibrary:[false]}]){
  assert.deepEqual(buildingLibraryItems(p,{tab:'map'}),[]);assert.deepEqual(buildingLibraryItems(p,{tab:'mine'}),[]);
 }
});

test('mount keeps public IDs, puts common search first, and shows only On map without loading presets',async()=>{
 let requests=0;const f=setup({request:async()=>{requests++;return {ok:true,json:async()=>presets};}});f.library.open();await flush();
 const tabs=f.dialog.querySelectorAll('[role=tab]');assert.deepEqual(tabs.map(t=>t.textContent),['On map','Templates','My templates']);
 assert.equal(tabs[0].getAttribute('aria-selected'),'true');assert.equal(tabs[1].tabIndex,-1);
 const search=f.$('building-list-search');assert.ok(search.getAttribute('aria-label'));assert.ok(f.dialog.children.indexOf(search)<f.dialog.children.indexOf(tabs[0].parentElement));
 assert.equal(f.$('building-list').querySelectorAll('button')[0].textContent.includes('Map cottage'),true);
 assert.equal(f.$('presets-section').hidden,true);assert.equal(requests,0);
 f.library.dispose();
});

test('keyboard tabs wrap with arrows and Home/End; search persists across tabs and category filters',async()=>{
 const f=setup();f.library.open();f.library.setSearch('cottage');
 const tabs=f.dialog.querySelectorAll('[role=tab]');
 const key=(node,value)=>{const event=new Event('keydown',{cancelable:true});Object.defineProperty(event,'key',{value});node.dispatchEvent(event);assert.equal(event.defaultPrevented,true);};
 key(tabs[0],'End');assert.equal(f.library.tab,'mine');assert.equal(f.document.activeElement,tabs[2]);assert.equal(f.library.search,'cottage');
 key(tabs[2],'ArrowRight');assert.equal(f.library.tab,'map');key(tabs[0],'Home');
 f.$('housing-preset-category').value='urban';f.library.setTab('templates');await flush();
 assert.equal(f.$('building-presets-grid').children.length,0);assert.match(f.$('housing-preset-state').textContent,/no .*match/i);
 f.library.setSearch('courtyard');await flush();assert.equal(f.$('building-presets-grid').children.length,1);
 assert.equal(f.$('building-presets-grid').querySelector('h3').textContent,'Brick block');
 f.library.setTab('map');assert.equal(f.library.search,'courtyard');f.library.setTab('templates');await flush();assert.equal(f.$('housing-preset-category').value,'urban');
 f.library.dispose();
});

test('My templates uses buildingLibrary and delegates edit/place with IDs and independent snapshots',async()=>{
 const edits=[],placements=[],f=setup({onEditTemplate:(...args)=>edits.push(args),onPlaceTemplate:(...args)=>placements.push(args)});
 f.library.open({tab:'mine'});let buttons=f.$('building-library-mine-list').querySelectorAll('button');
 buttons.find(b=>b.textContent==='Edit template').click();await flush();assert.equal(f.dialog.open,false);assert.equal(edits[0][0],'mine-1');
 edits[0][1].name='Changed draft';assert.equal(f.project.buildingLibrary[0].name,'Library cottage');
 f.library.open({tab:'mine'});buttons=f.$('building-library-mine-list').querySelectorAll('button');buttons.find(b=>b.textContent==='Place on map').click();await flush();
 assert.equal(placements[0][0],'mine-1');assert.equal(placements[0][1].name,'Library cottage');assert.equal(f.project.buildings.length,1);f.library.dispose();
});

test('map edit delegates selected ID; create/import stay reachable in an empty library and import refreshes',async()=>{
 const edited=[],f=setup({project:{buildings:[house('a','Same name'),house('b','Same name')],buildingLibrary:[]},onEditBuilding:id=>edited.push(id),onCreateTemplate:()=>edited.push('new'),onImportTemplate:async()=>{f.project.buildingLibrary.push(house('imported','Imported house'));}});
 f.library.open();f.$('building-list').querySelectorAll('button')[1].click();await flush();assert.deepEqual(edited,['b']);
 f.library.open({tab:'mine'});assert.match(f.$('building-library-state').textContent,/no .*templates/i);
 f.$('building-library-import').click();await flush();assert.equal(f.dialog.open,true);assert.match(f.$('building-library-mine-list').querySelector('h3').textContent,/Imported house/);
 f.$('new-building-template').click();await flush();assert.deepEqual(edited,['b','new']);assert.equal(f.dialog.open,false);f.library.dispose();
});

test('empty map, no search results, and unavailable callbacks do not create unusable actions',()=>{
 const f=setup({project:null});f.library.open();assert.match(f.$('building-library-state').textContent,/no buildings/i);assert.equal(f.$('building-list').children.length,0);
 f.library.setTab('mine');assert.equal(f.$('new-building-template').disabled,true);assert.equal(f.$('building-library-import').disabled,true);
 f.library.setSearch('<img src=x>');assert.match(f.$('building-library-state').textContent,/no .*match/i);assert.equal(f.$('building-library-mine-list').children.length,0);f.library.dispose();
});

test('refresh reads the current project and handles renamed/deleted records after reopen',()=>{
 let project={buildings:[house('old','Old')],buildingLibrary:[]};const f=setup();f.library.dispose();const library=mountBuildingLibrary({dialog:f.dialog,getProject:()=>project,housingGallery:f.gallery,onEditBuilding(){}});
 library.open();assert.match(f.$('building-list').querySelector('button').textContent,/Old/);project={buildings:[house('new','New')],buildingLibrary:[]};library.refresh();
 assert.match(f.$('building-list').querySelector('button').textContent,/New/);library.close();project={};library.open();assert.equal(f.$('building-list').children.length,0);library.dispose();
});

test('switching away aborts a pending preset list and a stale response never starts thumbnail work',async()=>{
 const pending=deferred(),calls=[];let signal;
 const f=setup({request:async(_url,options)=>{signal=options.signal;return pending.promise;},api:async path=>{calls.push(path);return house('x','X');}});
 f.library.open({tab:'templates'});f.library.setTab('mine');assert.equal(signal.aborted,true);
 pending.resolve({ok:true,json:async()=>presets});await flush();assert.deepEqual(calls,[]);assert.equal(f.$('building-presets-grid').children.length,0);f.library.dispose();
});

test('switching tabs/closing cancels preview work and a stale building response never requests its scene',async()=>{
 const pending=deferred(),calls=[];let signal;
 const f=setup({api:async(path,_data,s)=>{calls.push(path);signal=s;return pending.promise;}});
 f.library.open({tab:'templates'});await flush();assert.equal(calls.length,1);f.library.setTab('map');assert.equal(signal.aborted,true);
 pending.resolve(house('x','X'));await flush();assert.equal(calls.length,1);
 const second=deferred();f.gallery.api=async(_p,_d,s)=>{signal=s;return second.promise;};f.library.setTab('templates');await flush();f.dialog.close();assert.equal(signal.aborted,true);
 second.resolve(house('x','X'));await flush();assert.equal(f.gallery.active,false);f.library.dispose();
});

test('preset selection and safe reference links survive the tabbed mount; failed thumbnails leave usable cards',async()=>{
 const selected=[],f=setup({onSelectPreset:id=>selected.push(id),api:async()=>{throw Error('Game folder not loaded');}});
 f.library.open({tab:'templates'});await flush();const grid=f.$('building-presets-grid');assert.equal(grid.children.length,2);
 assert.match(grid.querySelector('canvas').getAttribute('aria-label'),/not available/);grid.querySelector('button').click();assert.deepEqual(selected,['village']);
 const link=grid.querySelector('a');assert.equal(link.rel,'noopener noreferrer');assert.equal(link.href,'https://example.com/reference');f.library.dispose();
});

test('preset request failures are retryable and close does not show a late error',async()=>{
 let fail=true;const f=setup({request:async()=>{if(fail)throw Error('Offline');return {ok:true,json:async()=>[]};}});
 f.library.open({tab:'templates'});await flush();assert.match(f.$('housing-preset-state').textContent,/Offline/);fail=false;f.library.refresh();await flush();assert.match(f.$('housing-preset-state').textContent,/no .*templates/i);
 const pending=deferred();f.gallery.items=null;f.gallery.request=()=>pending.promise;f.library.refresh();f.library.close();const before=f.$('housing-preset-state').textContent;pending.reject(Error('Late failure'));await flush();assert.equal(f.$('housing-preset-state').textContent,before);f.library.dispose();
});

test('finishing import keeps unavailable template actions disabled',async()=>{
 const f=setup({onImportTemplate:async()=>{}});f.library.open({tab:'mine'});f.$('building-library-import').click();await flush();
 assert.ok(f.$('building-library-mine-list').querySelectorAll('button').every(button=>button.disabled));f.library.dispose();
});

test('callback failures reopen the catalog with a readable error and allow retry',async()=>{
 let attempts=0;const f=setup({onEditTemplate:()=>{if(++attempts===1)throw Error('Template is locked');}});f.library.open({tab:'mine'});
 f.$('building-library-mine-list').querySelectorAll('button')[0].click();await flush();assert.equal(f.dialog.open,true);assert.match(f.$('building-library-state').textContent,/Template is locked/);
 f.$('building-library-mine-list').querySelectorAll('button')[0].click();await flush();assert.equal(attempts,2);assert.equal(f.dialog.open,false);f.library.dispose();
});

test('the default thumbnail loader aborts metadata work before any atlas is fetched',async()=>{
 const f=fixture(),pending=deferred(),requests=[];let metadataSignal;
 const gallery=new HousingGallery({root:f.document,select(){},api:async(path,_body,signal)=>{
  if(path==='/api/preview/building')return {sprites:[{tile:'floor_0',x:0,y:0,z:0}]};
  if(path==='/api/assets/describe'){metadataSignal=signal;return pending.promise;}return house('x','X');
 },request:async path=>{requests.push(path);return {ok:true,json:async()=>presets};}});
 f.dialog.showModal();void gallery.open();await flush();assert.ok(metadataSignal);gallery.close();assert.equal(metadataSignal.aborted,true);
 pending.resolve([{id:'floor_0',page:'floors',sprite:{}}]);await flush();assert.deepEqual(requests,['/api/building/presets']);gallery.dispose();
});

test('default thumbnails decode and draw full sprite bounds, reusing the atlas for subsequent cards',async()=>{
 const oldImage=globalThis.Image,oldElement=globalThis.HTMLImageElement;let decodes=0;
 globalThis.Image=globalThis.HTMLImageElement=class {naturalWidth=64;naturalHeight=32;async decode(){decodes++;}};
 const f=fixture(),requests=[],metadata=[];
 const gallery=new HousingGallery({root:f.document,select(){},api:async(path,body,signal)=>{
  if(path==='/api/preview/building')return {sprites:[{tile:'floor_0',x:0,y:0,z:0}]};
  if(path==='/api/assets/describe'){metadata.push({body,signal});return [{id:'floor_0',page:'floors',sprite:{x:0,y:0,width:64,height:32,offsetX:0,offsetY:0,frameWidth:64,frameHeight:32}}];}return house('x','X');
 },request:async(path,options)=>{requests.push({path,signal:options.signal});return {ok:true,json:async()=>presets,blob:async()=>new Blob(['atlas'])};}});
 try{
  f.dialog.showModal();await gallery.open();assert.equal(decodes,1);assert.deepEqual(metadata[0].body,['floor_0']);
  assert.equal(requests.filter(r=>r.path==='/api/assets/page/floors').length,1);assert.equal(requests[1].signal,metadata[0].signal);
  assert.deepEqual(f.$('building-presets-grid').querySelectorAll('canvas').map(canvas=>canvas.getContext('2d').draws),[1,1]);
 }finally{gallery.dispose();globalThis.Image=oldImage;globalThis.HTMLImageElement=oldElement;}
});

test('closing during atlas decode cancels the image and does not start the next thumbnail',async()=>{
 const oldImage=globalThis.Image;let image,decodeReject;
 globalThis.Image=class {constructor(){image=this;}set src(value){this.source=value;if(value==='')decodeReject?.(Error('Decode cancelled'));}get src(){return this.source;}decode(){return new Promise((_resolve,reject)=>{decodeReject=reject;});}};
 const f=fixture(),calls=[],pageSignals=[];
 const gallery=new HousingGallery({root:f.document,select(){},api:async path=>{
  calls.push(path);if(path==='/api/preview/building')return {sprites:[{tile:'floor_0',x:0,y:0,z:0}]};
  if(path==='/api/assets/describe')return [{id:'floor_0',page:'floors',sprite:{}}];return house('x','X');
 },request:async(path,options)=>{if(path.includes('/page/'))pageSignals.push(options.signal);return {ok:true,json:async()=>presets,blob:async()=>new Blob(['atlas'])};}});
 try{
  f.dialog.showModal();const work=gallery.open();await flush();assert.match(image.src,/^blob:/);f.dialog.close();await work;
  assert.equal(image.src,'');assert.equal(pageSignals[0].aborted,true);assert.deepEqual(calls,['/api/building/presets/village','/api/preview/building','/api/assets/describe']);
 }finally{gallery.dispose();globalThis.Image=oldImage;}
});

test('a category change cancels older preview work; common input filters only the active list',async()=>{
 const pending=deferred();let oldSignal;const f=setup({api:async(path,_body,signal)=>{oldSignal??=signal;return pending.promise;}});
 f.library.open({tab:'templates'});await flush();f.$('housing-preset-category').value='urban';f.$('housing-preset-category').dispatchEvent(new Event('change'));
 assert.equal(oldSignal.aborted,true);assert.equal(f.$('building-presets-grid').querySelector('h3').textContent,'Brick block');
 f.library.setTab('map');f.$('building-list-search').value='absent';f.$('building-list-search').oninput();assert.equal(f.$('building-list').children.length,0);
 pending.resolve(house('x','X'));await flush();f.library.close();f.library.open();assert.equal(f.library.search,'');assert.equal(f.$('building-list').children.length,1);f.library.dispose();
});

test('legacy create binding remains usable and dispose restores dialog nodes and search binding',()=>{
 const f=fixture();let created=0;const input=()=>{},create=()=>created++;f.$('building-list-search').oninput=input;f.$('new-building-template').onclick=create;const original=[...f.dialog.children];
 const gallery=new HousingGallery({root:f.document,api:async()=>{},select(){}}),library=mountBuildingLibrary({dialog:f.dialog,getProject:()=>({}),housingGallery:gallery});
 library.open({tab:'mine'});f.$('new-building-template').click();assert.equal(created,1);library.dispose();
 assert.deepEqual(f.dialog.children,original);assert.equal(f.$('building-list-search').oninput,input);assert.equal(f.$('new-building-template').onclick,create);gallery.dispose();
});
