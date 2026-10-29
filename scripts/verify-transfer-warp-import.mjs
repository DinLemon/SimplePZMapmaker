// Synthetic source, actual offline Web import, linked-world save/load, native export and CLI scene.
import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48778',out=new URL('../work/transfer-acceptance/',import.meta.url);await mkdir(out,{recursive:true});
async function api(path,body){const response=await fetch(base+path,body===undefined?{}:{method:'POST',headers:{'Content-Type':'application/json',Origin:base},body:JSON.stringify(body)}),data=await response.json();assert.ok(response.ok,`${path}: ${response.status} ${data.error||data.message}`);return data;}
const reference={bounds:{west:45,east:45+600/(Math.cos(55*Math.PI/180)*111320),north:55+300/111320,south:55},metresPerTile:1,originCellX:900,originCellY:900,rotationDegrees:0};
// Reverse only the original, undeformed affine frame when authoring the source fixture.
const cosine=Math.cos((reference.bounds.north+reference.bounds.south)/2*Math.PI/180),ll=([x,y])=>[45+x/(cosine*111320),reference.bounds.north-y/111320];
const feature=(id,properties,geometry)=>({type:'Feature',id,properties,geometry});const polygon=points=>({type:'Polygon',coordinates:[points.map(ll)]});
const source={type:'FeatureCollection',features:[
 feature('street',{highway:'residential'}, {type:'LineString',coordinates:[[-20,155],[620,155]].map(ll)}),
 feature('seam-house',{building:'house','building:levels':'1'},polygon([[325,110],[345,110],[345,130],[325,130],[325,110]])),
 feature('seam-fence',{barrier:'fence'},{type:'LineString',coordinates:[[310,90],[365,90]].map(ll)}),
 feature('plot',{'pz:parcel':'yes','source:attribution':'Synthetic seam survey'}, {type:'Polygon',coordinates:[[[300,70],[380,70],[380,145],[300,145],[300,70]].map(ll),[[355,115],[360,115],[360,120],[355,120],[355,115]].map(ll)]})
]};
const proposal=await api('/api/geographic/warp/preview',{reference,options:{fieldScaleX:.75,fieldScaleY:1}});assert.ok(proposal.reference.warp);
const saved=await api('/api/osm/sources',{name:'deformed-seam.geojson',json:JSON.stringify(source),bounds:reference.bounds});
const request={...proposal.reference,sectionSize:256,name:'Shared deformation seam',sourceMode:'file',sourceId:saved.id,regionalProfile:'ru-rural'};
const start=await api('/api/world/osm/jobs',request);let status;
for(let i=0;i<240;i++){status=await api('/api/world/osm/jobs/'+start.id);if(status.status==='completed')break;if(status.status==='failed'||status.status==='cancelled')throw Error(status.error);await new Promise(r=>setTimeout(r,250));}
assert.equal(status.status,'completed');const world=await api('/api/world/osm/jobs/'+start.id+'/result');assert.equal(world.format,'SimplePZWorld');assert.equal(world.version,10);assert.equal(world.maps.length,4);assert.ok(world.buildings.some(b=>b.sourceId?.includes('seam-house')));assert.ok(world.fences.some(f=>f.sourceId?.includes('seam-fence')));
const holes=world.maps.flatMap(m=>m.project.sourceFeatures??[]).filter(f=>f.sourceId.includes('plot'));assert.ok(holes.length&&holes.every(f=>f.sourceGeometry.length===2&&f.geometry.length===2));assert.ok(world.maps.every(m=>m.project.worldSection));
const validated=await api('/api/world/validate',world);assert.deepEqual(validated.world.geoReference,world.geoReference);
const refresh=await api('/api/world/osm/jobs',{...request,existingWorld:world,preserveEdits:true});let refreshed;
for(let i=0;i<240;i++){const value=await api('/api/world/osm/jobs/'+refresh.id);if(value.status==='completed'){refreshed=await api('/api/world/osm/jobs/'+refresh.id+'/result');break;}if(value.status==='failed')throw Error(value.error);await new Promise(r=>setTimeout(r,250));}
assert.ok(refreshed);assert.deepEqual(refreshed.geoReference,world.geoReference);assert.equal(refreshed.buildings.length,world.buildings.length);assert.equal(refreshed.fences.length,world.fences.length);
await writeFile(new URL('deformed-seam.geojson',out),JSON.stringify(source));await writeFile(new URL('deformed-seam.pzworld.json',out),JSON.stringify(world));await writeFile(new URL('deformed-scene.json',out),JSON.stringify({format:'SimplePZScene',version:1,name:'Shared deformation seam',source:'deformed-seam.geojson',reference:world.geoReference,sectionSize:256,skipEmptySections:false}));
await writeFile(new URL('deformed-web-proof.json',out),JSON.stringify({sections:world.maps.length,sharedBuildings:world.buildings.length,sharedFences:world.fences.length,retainedHoleRecords:holes.length,status:status.status,preservedOnRefresh:true},null,2));console.log(`PASS deformed source: ${world.maps.length} linked sections, ${world.buildings.length} shared building, ${world.fences.length} shared fence, holes and preserved frame on refresh.`);
