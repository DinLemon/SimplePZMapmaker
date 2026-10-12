import {readFile,writeFile} from 'node:fs/promises';
const root=new URL('../examples/Pilna/',import.meta.url);
const recipe=JSON.parse(await readFile(new URL('recipe.json',root),'utf8'));
const source=(await readFile(new URL(recipe.sourceFile,root),'utf8')).replace(/^\uFEFF/,'');
async function post(path,body){const response=await fetch('http://127.0.0.1:48765'+path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});if(!response.ok)throw Error(await response.text());return response;}
const initial=await post('/api/generate',{size:recipe.size,seed:recipe.seed,kind:'blank',name:recipe.name}).then(r=>r.json());
initial.worldX=recipe.worldX;initial.worldY=recipe.worldY;
const result=await post('/api/import/geojson',{project:initial,json:source,bounds:recipe.bounds}).then(r=>r.json());
await writeFile(new URL('pilna.pzmap.json',root),JSON.stringify(result.project));
const {project,...report}=result;
const tally=key=>project.buildings.reduce((counts,b)=>(counts[key(b)]=(counts[key(b)]||0)+1,counts),{});
await writeFile(new URL('import-report.json',root),JSON.stringify({...report,sourceDate:recipe.sourceDate,mapSize:project.width,metresPerTile:project.metresPerTile,buildings:project.buildings.length,buildingKinds:tally(b=>b.kind),buildingLevels:tally(b=>b.design?.floors.length??1),levelOrigins:tally(b=>b.levelsOrigin),buildingShapes:tally(b=>b.design?.shape??'legacy'),terrainCounts:project.terrain.reduce((counts,t)=>(counts[t]=(counts[t]||0)+1,counts),{})},null,2));
const zip=await post('/api/export',project).then(r=>r.arrayBuffer());await writeFile(new URL('Pilna-B42.zip',root),Buffer.from(zip));
console.log(JSON.stringify({size:project.width,sourceFeatures:result.sourceFeatures,sourceBuildings:result.sourceBuildings,buildings:project.buildings.length,skipped:result.skippedBuildings,reasons:result.skipReasons,metresPerTile:project.metresPerTile,zipBytes:zip.byteLength}));
