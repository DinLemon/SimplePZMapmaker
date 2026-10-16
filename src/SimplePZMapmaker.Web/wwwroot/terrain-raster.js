import {environmentIndices,environmentPresets} from './appearance.js';
const rgb=environmentPresets.map(s=>s.colors.map(c=>[parseInt(c.slice(1,3),16),parseInt(c.slice(3,5),16),parseInt(c.slice(5,7),16)]));
export class TerrainRasterCache {
 constructor(canvas){this.canvas=canvas;this.context=canvas.getContext('2d');this.image=null;this.samples=null;this.styles=null;this.styleKey=null;this.source=null;this.visited=0;}
 update(project,blocks=null,{visible=()=>true,key:visibilityKey=''}={}){
  const key=JSON.stringify([project.width,project.height,project.seed,project.surfaceTheme,project.surfaceRegions||[],visibilityKey]);
  const reset=!this.image||this.styleKey!==key||this.canvas.width!==project.width||this.canvas.height!==project.height;
  if(reset){this.canvas.width=project.width;this.canvas.height=project.height;this.image=this.context.createImageData(project.width,project.height);this.samples=new Uint8Array(project.terrain.length);this.samples.fill(255);this.styles=environmentIndices(project);this.styleKey=key;}
  const starts=!reset&&this.source===project.terrain&&blocks!==null?[...new Set(blocks)]:null;let changed=false;this.visited=0;
  const update=i=>{this.visited++;const terrain=project.terrain[i];if(this.samples[i]===terrain)return;this.samples[i]=terrain;changed=true;const color=rgb[this.styles[i]][terrain],shade=terrain===1?((i*31+project.seed)>>>0)%7-3:0;this.image.data.set([color[0]+shade,color[1]+shade,color[2]+shade,visible(terrain)?255:0],i*4);};
  if(starts)for(const start of starts)for(let i=start;i<Math.min(project.terrain.length,start+4096);i++)update(i);else for(let i=0;i<project.terrain.length;i++)update(i);
  this.source=project.terrain;if(changed)this.context.putImageData(this.image,0,0);return changed;
 }
}
export function buildingVisible(building,screen,width,height,isometric=false){
 const points=[[building.x,building.y],[building.x+building.width,building.y],[building.x,building.y+building.height],[building.x+building.width,building.y+building.height]].map(([x,y])=>screen(x,y));
 const scale=Math.abs(screen(1,0).x-screen(0,0).x),levels=building.design?.floors?.length||1,min=building.design?.minLevel||0;
 const upper=isometric?Math.max(0,min+levels+1)*5*scale+24:8,lower=isometric?Math.max(0,-min)*5*scale+24:8;
 return Math.max(...points.map(p=>p.x))>=-24&&Math.min(...points.map(p=>p.x))<=width+24&&Math.max(...points.map(p=>p.y))+lower>=0&&Math.min(...points.map(p=>p.y))-upper<=height;
}
