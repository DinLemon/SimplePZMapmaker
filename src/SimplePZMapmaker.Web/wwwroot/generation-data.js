export const defaultRules=()=>({preset:'contextual',era:'mixed',landUse:'mixed',setback:3,minimumFloors:1,maximumFloors:5,courtyards:true,density:55});
export const generationControl=p=>{const g={lockedBuildingIds:[],protectedAreas:[],generatedBuildingIds:[],rules:null,lastSeed:null,lastVariant:0,lastMode:null,...p.generation};if(g.rules)g.rules={...defaultRules(),preset:'legacy',...g.rules};return structuredClone(g);};
const whole=(v,min,max)=>Number.isInteger(v)&&v>=min&&v<=max;
const text=v=>typeof v==='string'&&v.length<=100&&!/[\x00-\x1f\x7f]/.test(v);
export function validateRules(r){
 if(r&&typeof r==='object'&&!Array.isArray(r))r={...defaultRules(),preset:'legacy',...r};
 if(!r||!['legacy','contextual'].includes(r.preset)||!['prewar','soviet','modern','mixed'].includes(r.era)||!['residential','mixed','industrial','rural'].includes(r.landUse)||!whole(r.setback,1,16)||!whole(r.minimumFloors,1,32)||!whole(r.maximumFloors,r.minimumFloors,32)||!whole(r.density,0,100)||typeof r.courtyards!=='boolean')throw Error('Check the district rules, floor interval, setback and density.');
 return r;
}
export function validateGeneration(p){
 const g=p.generation;if(g==null)return;
 if(typeof g!=='object'||Array.isArray(g))throw Error('Invalid generation settings.');
 for(const key of ['lockedBuildingIds','generatedBuildingIds']){const ids=g[key]===undefined?[]:g[key];if(!Array.isArray(ids)||ids.length>4000||new Set(ids).size!==ids.length||ids.some(id=>!text(id)||!id.trim()))throw Error('Invalid generation building IDs.');}
 const areas=g.protectedAreas===undefined?[]:g.protectedAreas;if(!Array.isArray(areas)||areas.length>128)throw Error('Too many protected generation areas.');
 for(const a of areas)validateArea(a,p);
 if(!whole(g.lastVariant??0,0,10000)||!['fill','regenerate',null].includes(g.lastMode??null)||g.lastSeed!=null&&!whole(g.lastSeed,-2147483648,2147483647))throw Error('Invalid generation run metadata.');
 if(g.rules!=null)validateRules(g.rules);
}
export function validateArea(a,p){if(!a||!whole(a.x,0,p.width-1)||!whole(a.y,0,p.height-1)||!whole(a.width,1,p.width-a.x)||!whole(a.height,1,p.height-a.y)||a.id!=null&&!text(a.id)||a.name!=null&&!text(a.name))throw Error('Choose a rectangle entirely inside the map.');return a;}
export function footprintOrigin(b){return b.footprintOrigin==='manual-source-edit'?'source-edited':b.sourceOutline?.length||b.footprintOrigin==='source-grid'?'osm-source':b.sourceId?'source-approximated':!b.modified&&b.levelsOrigin!=='manual'&&(!b.footprintOrigin||b.footprintOrigin==='authored')&&(b.sourceKind==='settlement-generator'||/^(generated-|settlement-)/.test(b.id))?'generated':'authored';}
export function studioRequest(p,values){const rules=validateRules({...defaultRules(),...values.rules}),area=validateArea({x:+values.x,y:+values.y,width:+values.width,height:+values.height},p),seed=+values.seed,variant=+values.variant;if(!whole(seed,-2147483648,2147483647)||!whole(variant,0,10000)||!['fill','regenerate'].includes(values.mode))throw Error('Check the seed, variant and generation mode.');return {project:structuredClone(p),area,seed,variant,mode:values.mode,rules};}
export class GenerationReview{
 constructor(){this.clear();}
 clear(){this.snapshot=null;this.variants=[];this.selected=-1;}
 add(project,proposal){const snapshot=JSON.stringify(project);if(this.snapshot!==snapshot)this.clear();this.snapshot=snapshot;this.variants.push(proposal);if(this.variants.length>2)this.variants.shift();this.selected=this.variants.length-1;}
 current(project){if(this.snapshot!==JSON.stringify(project))throw Error('The map changed. Preview again before applying.');const proposal=this.variants[this.selected];if(!proposal?.proposalId)throw Error('Select a generation preview first.');return proposal;}
}
