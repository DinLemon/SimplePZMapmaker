import assert from 'node:assert/strict';
const base=process.env.PZMAP_TEST_URL??'http://127.0.0.1:48827';
const status=await fetch(base+'/api/assets/status').then(r=>r.json());
assert.equal(status.ready,false,'Run this check against an isolated application with game discovery disabled.');
async function post(path,data){const response=await fetch(base+path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(data)}),body=await response.json();assert(response.ok,JSON.stringify(body));return body;}
const project=await post('/api/generate',{seed:7,rules:{preset:'contextual',era:'soviet',landUse:'residential'}});
assert(project.buildings.length>0);
const proposal=await post('/api/generation/studio/preview',{project,area:{x:0,y:0,width:256,height:256},seed:8,mode:'regenerate',rules:{preset:'contextual',era:'soviet',landUse:'residential'}});
assert(proposal.summary.warnings.some(w=>w.includes('catalog unavailable')));
const applied=await post('/api/generation/studio/apply',{project,proposalId:proposal.proposalId});
const quality=await post('/api/generation/quality',{project:applied.project});
assert.equal(quality.complete,false);assert(quality.notes.some(n=>n.includes('catalog unavailable')));
console.log(JSON.stringify({status:'passed',offline:true,generated:project.buildings.length,applied:applied.project.buildings.length,qualityComplete:quality.complete,errors:quality.counts.errors}));
