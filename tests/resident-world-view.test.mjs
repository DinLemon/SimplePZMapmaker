import test from 'node:test';
import assert from 'node:assert/strict';
import {residentSections,residentBounds,residentFit,residentSectionAt,rebaseSectionPan,ResidentWorldView} from '../src/SimplePZMapmaker.Web/wwwroot/resident-world-view.js';
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

test('neighbour plans project and clip shared fences and decorations in section coordinates',()=>{
 const line={points:[{x:25590,y:25610},{x:25610,y:25610}]},decoration={x:25604,y:25612,parts:[{dx:1,dy:2,tile:'fixture_1'}]},p={...west,fences:[],decorations:[]};
 const loaded=new Map([['west',{member:{id:'west',project:p},shared:{fences:[line],decorations:[decoration]}}]]),calls=[];
 const ctx=new Proxy({},{get:(_,name)=> (...args)=>calls.push([name,...args]),set:()=>true});
 const renderer=new ResidentWorldView();renderer.raster=()=>({});
 renderer.draw(ctx,{project,memberId:'active',workspace:{loadedSections:loaded},transform:{isometric:false}},{screen:(x,y)=>({x:x+1100,y}),zoom:1,viewport:{width:2000,height:2000},layers:{isVisible:id=>['paths','decorations'].includes(id)}});
 assert.ok(calls.some(c=>c[0]==='lineTo'&&c[1]===10&&c[2]===10),'Shared fence endpoint must project into the west section');
 assert.ok(calls.some(c=>c[0]==='fillRect'&&c[1]===5&&c[2]===14),'Shared decoration parts must project into the west section');
 assert.ok(calls.some(c=>c[0]==='clip'),'Shared geometry must not duplicate outside the member boundary');
 assert.deepEqual(line.points,[{x:25590,y:25610},{x:25610,y:25610}]);assert.equal(decoration.x,25604);
});
