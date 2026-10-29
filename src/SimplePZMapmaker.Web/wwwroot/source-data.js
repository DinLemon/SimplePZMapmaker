const text=(s,n=100)=>typeof s==='string'&&s.trim().length>0&&s.length<=n&&!/[\x00-\x1f\x7f]/.test(s);
export function siteContains(s,x,y){const ring=r=>{let inside=false;for(let i=0,j=r.length-1;i<r.length;j=i++){const a=r[i],b=r[j],cross=(b.x-a.x)*(y-a.y)-(b.y-a.y)*(x-a.x);if(Math.abs(cross)<1e-8&&x>=Math.min(a.x,b.x)-1e-8&&x<=Math.max(a.x,b.x)+1e-8&&y>=Math.min(a.y,b.y)-1e-8&&y<=Math.max(a.y,b.y)+1e-8)return true;if((a.y>y)!==(b.y>y)&&x<(b.x-a.x)*(y-a.y)/(b.y-a.y)+a.x)inside=!inside;}return inside;};return !!s.boundary?.length&&ring(s.boundary[0])&&!s.boundary.slice(1).some(ring);}
export function validateSourceSites(p){
 const fail=()=>{throw Error('Source evidence or site grouping is damaged.');},source=p.sourceFeatures===undefined?[]:p.sourceFeatures,sites=p.sites===undefined?[]:p.sites;
 if(!Array.isArray(source)||source.length>30000||!Array.isArray(sites)||sites.length>512)fail();
 let points=0,projectedPoints=0;const ids=new Set();
 for(const e of source){if(!e||!text(e.id)||ids.has(e.id)||!text(e.sourceId)||!['building','site','entrance','poi','barrier','address'].includes(e.kind)||!['point','line','polygon'].includes(e.geometryKind)||typeof e.name!=='string'||e.name.length>300||!['source','source-edited','authored'].includes(e.origin)||typeof e.modified!=='boolean'||!e.tags||Array.isArray(e.tags)||Object.entries(e.tags).length>64||Object.entries(e.tags).some(([k,v])=>!k||k.length>100||typeof v!=='string'||v.length>300)||!Array.isArray(e.geometry)||e.geometry.length<1||e.geometry.length>1024||!Array.isArray(e.sourceGeometry)||e.sourceGeometry.length<1||e.sourceGeometry.length>1024||(p.version<15&&e.sourceGeometry.length!==e.geometry.length))fail();ids.add(e.id);
  const minimum=e.geometryKind==='point'?1:e.geometryKind==='line'?2:4;
  for(const a of e.sourceGeometry){if(!Array.isArray(a)||a.length<minimum||e.geometryKind==='point'&&a.length!==1||a.some(q=>!q||!Number.isFinite(q.longitude)||!Number.isFinite(q.latitude)||Math.abs(q.longitude)>180||Math.abs(q.latitude)>85)||e.geometryKind==='polygon'&&(a[0].longitude!==a.at(-1).longitude||a[0].latitude!==a.at(-1).latitude))fail();points+=a.length;}
  for(const b of e.geometry){if(!Array.isArray(b)||b.length<minimum||e.geometryKind==='point'&&b.length!==1||b.some(q=>!q||!Number.isFinite(q.x)||!Number.isFinite(q.y)||Math.abs(q.x)>2e6||Math.abs(q.y)>2e6)||e.geometryKind==='polygon'&&(b[0].x!==b.at(-1).x||b[0].y!==b.at(-1).y))fail();projectedPoints+=b.length;}
  if(p.version<15&&e.sourceGeometry.some((r,i)=>r.length!==e.geometry[i].length))fail();
 }if(points>250000||projectedPoints>250000)fail();ids.clear();let vertices=0,members=0;
 for(const s of sites){if(s?.parcelGeneration!=null){const g=s.parcelGeneration;if(!g||Array.isArray(g)||!g.objects||Array.isArray(g.objects)||Object.keys(g.objects).length>256||Object.entries(g.objects).some(([k,v])=>k.length>128||! /^(building|fence|decoration):/.test(k)||typeof v!=="string"||! /^[a-f0-9]{64}$/i.test(v))||!g.ground||Array.isArray(g.ground)||Object.keys(g.ground).length>100000||Object.entries(g.ground).some(([k,v])=>! /^(0|[1-9][0-9]*)$/.test(k)||+k>=p.width*p.height||!v||![v.before,v.after].every(t=>Number.isInteger(t)&&t>=0&&t<=6)))fail();}if(!s||!text(s.id)||!text(s.name)||ids.has(s.id)||!['source','source-edited','authored','inferred'].includes(s.origin)||typeof s.locked!=='boolean'||!Array.isArray(s.boundary)||s.boundary.length<1||s.boundary.length>1024)fail();ids.add(s.id);let count=0;
  for(const r of s.boundary){if(!Array.isArray(r)||r.length<3||r.length>4096||r.some(q=>!q||!Number.isFinite(q.x)||!Number.isFinite(q.y)||Math.abs(q.x)>258048||Math.abs(q.y)>258048))fail();count+=r.length;let area=0;for(let i=0;i<r.length;i++){const a=r[i],b=r[(i+1)%r.length];area+=a.x*b.y-b.x*a.y;}if(Math.abs(area)<.002)fail();}vertices+=count;if(count>4096||vertices>65536)fail();
  for(const [key,objects] of [['buildingIds',p.buildings],['decorationIds',p.decorations],['staticModelIds',p.staticModels],['linearFeatureIds',p.linearFeatures]]){const refs=s[key]??[],available=new Set((objects??[]).map(o=>o.id));if(!Array.isArray(refs)||refs.length>10000||new Set(refs).size!==refs.length||refs.some(id=>!text(id)||!available.has(id)))fail();members+=refs.length;}
 }if(members>50000)fail();
}
// A revision token plus AbortSignal prevents stale diagnostics from crossing edits, reloads or world sections.
export class LatestSourceAudit{
 constructor(){this.revision=0;this.controller=null;this.value=null;}
 invalidate(){this.revision++;this.controller?.abort();this.controller=null;this.value=null;}
 begin(){this.invalidate();this.controller=new AbortController();return {revision:this.revision,signal:this.controller.signal};}
 accept(ticket,value){if(ticket.revision!==this.revision||ticket.signal.aborted)return false;this.value=value;return true;}
}
export function detachMissingSiteMembers(p){
 if(!p.sites)return p;
 const sets=[['buildingIds','buildings'],['decorationIds','decorations'],['staticModelIds','staticModels'],['linearFeatureIds','linearFeatures']].map(([key,list])=>[key,new Set((p[list]??[]).map(o=>o.id))]);
 p.sites=p.sites.map(s=>{let changed=false;const copy={...s};for(const [key,available]of sets){const refs=s[key]??[];copy[key]=refs.filter(id=>available.has(id));changed||=copy[key].length!==refs.length;}if(changed&&copy.origin==='source')copy.origin='source-edited';return copy;});return p;
}
