import {readFile} from 'node:fs/promises';
import {execFileSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {inflateRawSync} from 'node:zlib';
import assert from 'node:assert/strict';
const root=fileURLToPath(new URL('../',import.meta.url)).replace(/[\\/]$/,'');
const privatePaths=[root,process.env.PZMAP_GAME_PATH,process.env.PZMAP_WORKSHOP_ROOT].filter(p=>p&&p.length>6);
const needles=[...new Set(privatePaths.flatMap(p=>[p,p.replaceAll('\\','/'),p.replaceAll('\\','\\\\')]))];
function check(bytes,name){for(const encoding of ['utf8','utf16le']){const text=bytes.toString(encoding).toLowerCase();for(const p of needles)assert.ok(!text.includes(p.toLowerCase()),`Local installation path found in ${name}`);}}
function zip(bytes,name){let end=bytes.length-22;while(end>=Math.max(0,bytes.length-65557)&&bytes.readUInt32LE(end)!==0x06054b50)end--;assert.ok(end>=0,'Invalid ZIP: '+name);let offset=bytes.readUInt32LE(end+16);for(let i=0;i<bytes.readUInt16LE(end+10);i++){const method=bytes.readUInt16LE(offset+10),size=bytes.readUInt32LE(offset+20),n=bytes.readUInt16LE(offset+28),extra=bytes.readUInt16LE(offset+30),comment=bytes.readUInt16LE(offset+32),local=bytes.readUInt32LE(offset+42),file=bytes.subarray(offset+46,offset+46+n).toString(),start=local+30+bytes.readUInt16LE(local+26)+bytes.readUInt16LE(local+28);if(!file.endsWith('/')){assert.ok(!file.endsWith('.pdb'),'Debug symbols must not be shipped: '+file);const data=bytes.subarray(start,start+size);assert.ok(method===0||method===8);check(method===8?inflateRawSync(data):data,name+'/'+file);}offset+=46+n+extra+comment;}}
const files=execFileSync('git',['ls-files','-z'],{cwd:root,encoding:'utf8'}).split('\0').filter(Boolean);
for(const f of files){const data=await readFile(new URL('../'+f,import.meta.url));if(f.endsWith('.zip'))zip(data,f);else check(data,f);}
if(process.argv[2])zip(await readFile(process.argv[2]),process.argv[2]);
console.log('PASS public source and archive contents contain no configured local installation paths or debug symbols.');
