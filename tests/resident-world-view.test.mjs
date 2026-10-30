import test from 'node:test';
import assert from 'node:assert/strict';
import {residentSections,residentBounds,residentFit,residentSectionAt,rebaseSectionPan} from '../src/SimplePZMapmaker.Web/wwwroot/resident-world-view.js';
import {sectionOverlays} from '../src/SimplePZMapmaker.Web/wwwroot/map-layers.js';
const project={worldX:104,worldY:100,width:1024,height:1024};
const west={...project,worldX:100},south={...project,worldY:104};
const loadedSections=new Map([['west',{member:{id:'west',project:west}}],['active',{member:{id:'active',project}}],['south',{member:{id:'south',project:south}}]]);
const context={project,memberId:'active',workspace:{loadedSections},sections:[{id:'west',...west},{id:'active',...project},{id:'south',...south}]};
test('resident editor bounds include negative offsets and every loaded section',()=>{
 const sections=residentSections(context);assert.equal(sections.length,3);
 assert.deepEqual(residentBounds(sections),{x:-1024,y:0,width:2048,height:2048});
 assert.equal(residentSectionAt(sections,{x:-1,y:512})?.id,'west');
 assert.equal(residentSectionAt(sections,{x:0,y:1024})?.id,'south');
 assert.equal(residentSectionAt(sections,{x:-1,y:1024}),undefined,'An empty gap must not become an editable section');
 assert.equal(sectionOverlays({...context,loadedSections}).filter(s=>s.loaded).length,3);
});
test('fit and section transitions keep the entire world and a stable geographic camera',()=>{
 const sections=residentSections(context),viewport={width:1000,height:700};
 for(const isometric of [false,true]){
  const {zoom,pan}=residentFit(residentBounds(sections),viewport,isometric);
  const screen=(x,y)=>({x:pan.x+(isometric?x-y:x)*zoom,y:pan.y+(isometric?(x+y)/2:y)*zoom});
  for(const {x,y,width,height} of sections)for(const [dx,dy] of [[0,0],[width,0],[width,height],[0,height]]){const p=screen(x+dx,y+dy);assert.ok(p.x>=0&&p.y>=0&&p.x<=1000&&p.y<=700);}
  const moved=rebaseSectionPan(pan,zoom,project,west,isometric),before=screen(-512,512);
  assert.deepEqual({x:moved.x+(isometric?512-512:512)*zoom,y:moved.y+(isometric?(512+512)/2:512)*zoom},before);
 }
});
test('opening only one section never invents resident neighbours',()=>{
 assert.deepEqual(residentSections({...context,workspace:{}}),[]);
});
