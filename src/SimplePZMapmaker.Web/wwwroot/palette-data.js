import {validateDependencies} from './resource-data.js';
import {tileId} from './building.js';
export function validatePalettes(palettes){
 if(!Array.isArray(palettes)||palettes.length>16)throw Error('A project can contain up to 16 resource palettes.');
 const ids=new Set();
 for(const p of palettes){
  if(!p||typeof p.id!=='string'||!/^([a-f0-9]{32})$/.test(p.id)||ids.has(p.id)||typeof p.name!=='string'||!p.name.trim()||p.name.length>100||/[\x00-\x1f\x7f]/.test(p.name)||!Array.isArray(p.resources)||p.resources.length<1||p.resources.length>64)throw Error('Invalid resource palette: unique ID, name and 1-64 resource references are required.');
  ids.add(p.id);const entries=new Set();
  for(const r of p.resources){
   if(!r||!['floor','wall','door','window','object','roof'].includes(r.role)||!tileId(r.id)||!Array.isArray(r.requiredMods))throw Error('Invalid palette resource reference.');
   validateDependencies(r.requiredMods,{[r.id]:r.sourceId});
   const key=r.role+':'+r.id;if(entries.has(key))throw Error('Duplicate palette resource reference.');entries.add(key);
  }
 }
 return palettes;
}
