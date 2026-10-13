import {readFile,writeFile,mkdir} from 'node:fs/promises';
import assert from 'node:assert/strict';
const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48765';
async function post(path,value){const r=await fetch(base+path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(value)});assert.equal(r.status,200,await r.clone().text());return r;}
const maps=[];for(let i=0;i<2;i++){const p=await(await post('/api/generate',{kind:'blank',size:256,name:i?'East section':'West section',surfaceTheme:'rural'})).json();p.worldX=100+i;p.worldY=100;p.metresPerTile=1;maps.push({id:crypto.randomUUID().replaceAll('-',''),project:p});}
maps[0].project.terrain[20*256+255]=2;maps[0].project.terrain[40*256+255]=3;
const b=JSON.parse(await readFile(new URL('../examples/HousingTemplates/village-izba.pzbuilding.json',import.meta.url),'utf8')).building;b.id=crypto.randomUUID().replaceAll('-','');b.x=25850;b.y=25680;
const model=await(await post('/api/models/placement/Base.ToolBox_Ground',{})).json();model.profile='container';model.capacity=75;model.health=500;model.x=100;model.y=60;model.scale=2;maps[0].project.staticModels.push(model);maps[0].project.version=6;
let world={format:'SimplePZWorld',version:1,id:crypto.randomUUID().replaceAll('-',''),name:'Two sections - shared house and measured elevations',maps,buildings:[b],geoReference:{bounds:{south:54.7777,north:54.78,west:32.045,east:32.053},originCellX:100,originCellY:100,metresPerTile:1}};
const repaired=await(await post('/api/world/seams/repair',{world,mode:'both'})).json();world=repaired.world;assert.equal(world.maps[1].project.terrain[20*256],2);assert.equal(world.maps[1].project.terrain[40*256],3);
const downloaded=await(await post('/api/world/elevation/download',world)).json();world=downloaded.world;assert.ok(world.elevation.values.some(v=>Number.isFinite(v)));assert.match(world.elevation.source,/Terrain Tiles/);
world=(await(await post('/api/world/validate',world)).json()).world;
const archive=await post('/api/world/export',world);const out=new URL('../examples/Worlds/',import.meta.url);await mkdir(out,{recursive:true});
await writeFile(new URL('World-DEM.pzworld.json',out),JSON.stringify(world));await writeFile(new URL('World-DEM-B42.zip',out),Buffer.from(await archive.arrayBuffer()));
await writeFile(new URL('World-DEM-source.json',out),JSON.stringify({description:'Original authored house/roads/river demo. Only the DEM values are real surveyed dataset samples; this is not an OSM reconstruction.',importedUtc:world.elevation.importedUtc,source:world.elevation.source,license:world.elevation.license,coordinateSystem:world.elevation.coordinateSystem,columns:world.elevation.columns,rows:world.elevation.rows,minimum:downloaded.minimum,maximum:downloaded.maximum,geoReference:world.geoReference,nativeGround:'flat; elevation is survey overlay and elevation.asc sidecar'},null,2));
console.log('PASS real DEM example: two sections, one shared basement house, joined road/river, actual native container model and source-attributed height grid',JSON.stringify({columns:world.elevation.columns,rows:world.elevation.rows,minimum:downloaded.minimum,maximum:downloaded.maximum}));
