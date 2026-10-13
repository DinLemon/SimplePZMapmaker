import {occupiedIndices} from './building.js';
export function globalBuildingMask(world,project){const mask=new Set();for(const b of world?.buildings||[]){if(b.x+b.width<project.worldX*256||b.y+b.height<project.worldY*256||b.x>=project.worldX*256+project.width||b.y>=project.worldY*256+project.height)continue;for(const i of occupiedIndices(b)){const x=b.x+i%(b.width+1)-project.worldX*256,y=b.y+Math.floor(i/(b.width+1))-project.worldY*256;if(x>=0&&y>=0&&x<project.width&&y<project.height)mask.add(y*project.width+x);}}return mask;}
export function overlapsGlobalBuilding(project,building,mask){return [...occupiedIndices(building)].some(i=>mask.has((building.y+Math.floor(i/(building.width+1)))*project.width+building.x+i%(building.width+1)));}
export function validateGlobalProtection(project,mask){
 if(project.buildings.some(b=>overlapsGlobalBuilding(project,b,mask))||project.spawns.some(s=>mask.has(s.y*project.width+s.x))||(project.staticModels||[]).some(m=>mask.has(m.y*project.width+m.x))||(project.decorations||[]).some(d=>d.parts.some(part=>mask.has((d.y+part.dy)*project.width+d.x+part.dx))))throw Error('Изменение пересекает общее здание мира. Редактируйте его в мастерской здания.');
 for(const i of mask)if(project.terrain[i]===3)throw Error('Нельзя залить водой общее здание мира.');
}
