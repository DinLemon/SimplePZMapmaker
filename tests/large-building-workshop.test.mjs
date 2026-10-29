import test from 'node:test';
import assert from 'node:assert/strict';
import {BuildingWorkshop} from '../src/SimplePZMapmaker.Web/wwwroot/workshop.js';
import {normalizeBuilding,refreshEdges} from '../src/SimplePZMapmaker.Web/wwwroot/building.js';

function building(width=64,height=64,floors=1){
 const b=normalizeBuilding({id:'large',name:'Large building',kind:'warehouse',x:1,y:1,width,height,design:{material:'brick',roof:'none',shape:'rect',variant:1,minLevel:floors>32?-32:0,floors:Array.from({length:floors},()=>({cells:'1'.repeat(width*height),rooms:{1:'warehouse'},edges:[],objects:[]}))}});
 refreshEdges(b,0);return b;
}
test('workshop resize crosses the old 64 tile boundary without losing existing cells',()=>{
 const w=Object.create(BuildingWorkshop.prototype);w.draft=building();
 const original=w.draft.design.floors[0].cells;
 w.resize(87,92);assert.equal(w.draft.width,87);assert.equal(w.draft.height,92);
 const floor=w.draft.design.floors[0];assert.equal(floor.cells.length,87*92);
 for(let y=0;y<64;y++)assert.equal(floor.cells.slice(y*87,y*87+64),original.slice(y*64,(y+1)*64));
 assert.equal(floor.cells.at(-1),'.');
});
test('workshop resize rejects floor-cell overflow before mutating any floor',()=>{
 const w=Object.create(BuildingWorkshop.prototype);w.draft=building(64,64,64);
 const before=structuredClone(w.draft);
 assert.throws(()=>w.resize(65,64),/floor.*(budget|limit|262|tiles|cells)/i);
 assert.deepEqual(w.draft,before);assert.throws(()=>w.resize(513,10),/size/i);
});
test('workshop boundary change rolls back an over-budget draft and keeps undo untouched',()=>{
 const w=Object.create(BuildingWorkshop.prototype),original=building(256,256,4);
 let saves=0,message='';Object.assign(w,{draft:original,level:0,room:'1',visual:{allLevels:false},history:{save(){saves++;}},update(){},message(text){message=text;}});
 assert.equal(w.change(()=>w.resize(257,256)),false);assert.equal(w.draft,original);assert.equal(saves,0);assert.match(message,/floor.*(budget|limit|262|tiles|cells)/i);
});
test('workshop plan fits all boundaries and maps the far corner back to the correct cell',()=>{
 const previousDocument=globalThis.document,previousWindow=globalThis.window;
 try{
  const rect={left:10,top:20,width:600,height:400},fills=[];
  const ctx=new Proxy({}, {get(target,key){return key in target?target[key]:key==='fillRect'?((x,y,width,height)=>fills.push({width,height})):()=>{};}});
  const canvas={getBoundingClientRect:()=>rect,getContext:()=>ctx};
  globalThis.window={devicePixelRatio:1};globalThis.document={getElementById:id=>id==='building-canvas'?canvas:id==='building-dialog'?{open:true}:id==='be-ghost'?{checked:false}:null};
  for(const [width,height] of [[8,8],[87,92],[263,24],[512,512]]){
   const w=Object.create(BuildingWorkshop.prototype);Object.assign(w,{draft:building(width,height),level:0,origin:{x:0,y:0}});w.floor.objects.push({kind:'storage',x:width-1,y:height-1,direction:'n'});w.draw();
   assert.ok(w.origin.x>=18-1e-7&&w.origin.y>=18-1e-7,`${width}x${height}: left/top boundary clipped`);
   assert.ok(w.origin.x+width*w.scale<=rect.width-18+1e-7&&w.origin.y+height*w.scale<=rect.height-18+1e-7,`${width}x${height}: right/bottom boundary clipped`);
   const last=w.point({clientX:rect.left+w.origin.x+(width-.5)*w.scale,clientY:rect.top+w.origin.y+(height-.5)*w.scale});
   assert.equal(last.x,width-1);assert.equal(last.y,height-1);
  }
  assert.ok(fills.every(f=>f.width>=0&&f.height>=0));
 }finally{globalThis.document=previousDocument;globalThis.window=previousWindow;}
});
