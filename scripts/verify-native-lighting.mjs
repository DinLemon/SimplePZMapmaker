import assert from 'node:assert/strict';
import {readFileSync,writeFileSync,mkdirSync,existsSync} from 'node:fs';
import {resolve,join,delimiter} from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import {createHash} from 'node:crypto';

// Optional installed-library regression. It starts only the isolated adapter, never a game session.
const root=resolve(fileURLToPath(new URL('../',import.meta.url))),helper=join(root,'tools','NativeLighting');
const hash=path=>createHash('sha256').update(readFileSync(path)).digest('hex');
const manifest=JSON.parse(readFileSync(join(helper,'manifest.json'),'utf8'));
assert.equal(manifest.protocol,2);assert.equal(manifest.javaTarget,17);
const sourceHash=createHash('sha256').update(readFileSync(join(helper,'LightingProbe.java'),'utf8').replaceAll('\r\n','\n')).digest('hex');
assert.equal(sourceHash,manifest.sourceSha256,'Lighting helper source and manifest differ');
assert.equal(hash(join(helper,'classes','LightingProbe.class')),manifest.classSha256,'Lighting helper bytecode and manifest differ');
const bytecode=readFileSync(join(helper,'classes','LightingProbe.class'));assert.equal(bytecode.readUInt32BE(0),0xcafebabe);assert.equal(bytecode.readUInt16BE(6),61,'The adapter requires Java bytecode target 17');
const required=process.argv.includes('--required');
let game=process.env.PZMAP_GAME_PATH;
const local=join(root,'work','local-settings.json');if(!game&&existsSync(local))game=JSON.parse(readFileSync(local,'utf8')).gamePath;
if(process.platform!=='win32'||!game){if(required)throw Error('A compatible Windows game installation is required for native lighting checks.');console.log('SKIP installed native lighting: no Windows game installation configured. Helper manifest verified.');process.exit(0);}
const java=join(game,'jre64','bin','java.exe'),jar=join(game,'projectzomboid.jar'),dll=join(game,'Lighting64.dll');
if(![java,jar,dll].every(existsSync)){if(required)throw Error('The configured installation lacks the JRE, game JAR or lighting library.');console.log('SKIP installed native lighting: required installed components are unavailable. Helper manifest verified.');process.exit(0);}
const work=join(root,'work','native-lighting-regressions'),home=join(work,'home'),tmp=join(work,'tmp');for(const dir of [work,home,tmp])mkdirSync(dir,{recursive:true});
const F={floor:1,wallN:2,wallW:4,windowN:16,windowW:32,doorN:64,doorW:128,openN:256,openW:512,air:2048,thumpN:4096,thumpW:8192};
function fixture(name,{level=0,barrier=0,north=false,point=true,active=true,sky=false,roof=false,roomPower=null}={}){
 const size=24,worldX=80,worldY=160,min=level,max=roof?Math.min(31,level+1):level,squares=[];
 for(let y=0;y<size;y++)for(let x=0;x<size;x++)squares.push([x,y,level,F.floor|(sky&&!roof&&level>=0?F.air:0)|((north?y:x)===12?barrier:0),roomPower===null?-1:1,roomPower===null?-1:2]);
 if(roof)for(let y=0;y<size;y++)for(let x=0;x<size;x++)squares.push([x,y,level+1,F.floor|F.air,-1,-1]);
 const lights=point?[[worldX+(north?12:8),worldY+(north?8:12),level,10,.5,.1,.05,active]]:[];
 const rooms=roomPower===null?[]:[[1,2,worldX+4,worldY+4,level,16,16,roomPower]];
 const bytes=Buffer.alloc(1024+squares.length*32+lights.length*29+rooms.length*37);let offset=0;
 const i=v=>{bytes.writeInt32LE(v,offset);offset+=4;},f=v=>{bytes.writeFloatLE(v,offset);offset+=4;},l=v=>{bytes.writeBigInt64LE(BigInt(v),offset);offset+=8;},b=v=>{bytes.writeUInt8(v?1:0,offset);offset++;};
 i(0x324c5a50);for(const value of [3,3,worldX/8,worldY/8,min,max,level,0,0,size])i(value);
 for(const value of sky?[.7,.6,.4,.4]:[0,0,0,.1])f(value);
 i(squares.length);for(const [x,y,z,flags,building,room] of squares){for(const value of [x,y,z,flags])i(value);l(building);l(room);}
 i(lights.length);for(const light of lights){light.slice(0,4).forEach(i);light.slice(4,7).forEach(f);b(light[7]);}
 i(rooms.length);for(const room of rooms){l(room[0]);l(room[1]);room.slice(2,7).forEach(i);b(room[7]);}
 const input=join(work,name+'.bin'),output=join(work,name+'.json');writeFileSync(input,bytes.subarray(0,offset));
 try{execFileSync(java,['-Djava.awt.headless=true','-Duser.home='+home,'-Djava.io.tmpdir='+tmp,'-XX:ErrorFile='+join(work,'hs_err_%p.log'),'-XX:-CreateCoredumpOnCrash','--enable-native-access=ALL-UNNAMED','-cp',join(helper,'classes')+delimiter+jar,'LightingProbe',dll,input,output],{cwd:work,timeout:20000,maxBuffer:1024*1024,stdio:['ignore','pipe','pipe'],env:{...process.env,TEMP:tmp,TMP:tmp}});}
 catch(error){writeFileSync(join(work,name+'.log'),Buffer.concat([error.stdout||Buffer.alloc(0),error.stderr||Buffer.alloc(0)]));throw Error(`Native helper failed for ${name}; inspect its log in work/native-lighting-regressions.`);}
 // This ABI starts its update counter at -1; twelve adapter frames therefore finish at 11.
 const result=JSON.parse(readFileSync(output,'utf8'));assert.equal(result.protocol,2);assert.ok(result.counter>=11,'Cached native geometry did not survive twelve update frames');assert.equal(result.cells.length,576);const seen=new Set();
 for(const cell of result.cells){assert.equal(cell.length,15);assert.ok(cell.every(Number.isFinite));assert.equal(cell[2],level);assert.ok(cell[0]>=0&&cell[0]<24&&cell[1]>=0&&cell[1]<24);assert.ok(cell.slice(3,6).every(v=>v>=0&&v<=1));assert.ok(cell.slice(7).every(Number.isInteger));assert.ok(!seen.has(`${cell[0]}:${cell[1]}`));seen.add(`${cell[0]}:${cell[1]}`);}
 const sample=result.cells.find(c=>c[0]===(north?12:16)&&c[1]===(north?16:12));assert.ok(sample);return {result,sample,near:result.cells.find(c=>c[0]===(north?12:10)&&c[1]===(north?10:12))};
}
const open=fixture('open'),wall=fixture('wall',{barrier:F.wallW}),window=fixture('window',{barrier:F.windowW}),doorOpen=fixture('door-open',{barrier:F.doorW|F.openW}),doorClosed=fixture('door-closed',{barrier:F.doorW});
assert.ok(open.sample[3]>.1&&open.sample[3]>open.sample[4]*2,'The colored point light did not reach the far sample');
assert.ok(open.near[3]>open.sample[3]+.1,'Native point light has no radial falloff');
assert.ok(wall.sample[3]<.01,'An opaque wall failed to block the native light');
assert.ok(window.sample[3]>wall.sample[3]+.1,'Window did not transmit light');
assert.ok(doorOpen.sample[3]>wall.sample[3]+.1&&doorOpen.sample[3]>doorOpen.sample[4]*2,'Open door did not transmit colored light');
assert.notDeepEqual(doorClosed.sample.slice(3,6),doorOpen.sample.slice(3,6),'Closed and open native doors returned identical light');
for(const north of [false,true]){
 const closed=fixture('model-door-'+(north?'north':'west')+'-closed',{north,barrier:north?F.doorN|F.thumpN:F.doorW|F.thumpW});
 const opened=fixture('model-door-'+(north?'north':'west')+'-open',{north,barrier:north?F.doorN|F.thumpN|F.openN:F.doorW|F.thumpW|F.openW});
 assert.ok(opened.sample[3]>.1,'Open model door did not transmit light');assert.notDeepEqual(closed.sample,opened.sample,'Model door state did not affect native results');
}
const off=fixture('light-off',{active:false});assert.ok(off.sample[3]<.01&&off.sample[4]<.01&&off.sample[5]<.01,'Disabled point light still illuminates');
for(const level of [-16,-1,31]){const signed=fixture('signed-'+level,{level});assert.ok(Math.abs(signed.sample[3]-open.sample[3])<.01,'Native point light changed at an equivalent signed floor');}
const sky=fixture('sky',{sky:true,point:false}),roof=fixture('sky-roof',{sky:true,point:false,roof:true}),basement=fixture('sky-basement',{sky:true,point:false,level:-1});
assert.ok(Math.abs(sky.sample[3]-.7)<.01&&Math.abs(sky.sample[4]-.6)<.01&&Math.abs(sky.sample[5]-.4)<.01,'Controlled sky RGB channels changed order');
assert.ok(sky.sample[3]>roof.sample[3]+.05,'Roof did not change the native sky field');assert.ok(sky.sample[3]>basement.sample[3]+.05,'Basement received direct sky light');
const roomOn=fixture('room-on',{point:false,roomPower:true}),roomOff=fixture('room-off',{point:false,roomPower:false});assert.ok(roomOn.sample[3]>roomOff.sample[3]+.05,'Explicit room power did not affect native light');
const report={protocol:2,gameSession:false,nativeSha256:hash(dll),helperSha256:manifest.classSha256,fixtures:18,openRgb:open.sample.slice(3,6),wallRgb:wall.sample.slice(3,6),skyRgb:sky.sample.slice(3,6),roofRgb:roof.sample.slice(3,6),basementRgb:basement.sample.slice(3,6)};writeFileSync(join(work,'report.json'),JSON.stringify(report,null,2));
console.log('PASS native lighting: walls, windows, native/model doors, radial/color/disabled lights, signed floors, roof/sky/basement and room power. No game session started.');
