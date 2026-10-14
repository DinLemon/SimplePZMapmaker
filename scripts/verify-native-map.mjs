import {readFile,stat} from 'node:fs/promises';
import {inflateRawSync} from 'node:zlib';
import assert from 'node:assert/strict';

const file=process.argv[2]||'examples/Pilna/Pilna-B42.zip';
const zip=await readFile(file);let end=-1;
for(let i=zip.length-22;i>=Math.max(0,zip.length-65557);i--)if(zip.readUInt32LE(i)===0x06054b50&&i+22+zip.readUInt16LE(i+20)===zip.length){end=i;break;}
assert.ok(end>=0,'ZIP directory absent');
const entries=new Map();let offset=zip.readUInt32LE(end+16);
for(let i=0;i<zip.readUInt16LE(end+10);i++){
 assert.equal(zip.readUInt32LE(offset),0x02014b50);const method=zip.readUInt16LE(offset+10),size=zip.readUInt32LE(offset+20),nameSize=zip.readUInt16LE(offset+28),extra=zip.readUInt16LE(offset+30),comment=zip.readUInt16LE(offset+32),local=zip.readUInt32LE(offset+42);
 const name=zip.subarray(offset+46,offset+46+nameSize).toString('utf8');assert.ok(!name.includes('..')&&!name.startsWith('/')&&!entries.has(name),'Unsafe or duplicate path');
 assert.equal(zip.readUInt32LE(local),0x04034b50);const start=local+30+zip.readUInt16LE(local+26)+zip.readUInt16LE(local+28);const compressed=zip.subarray(start,start+size);
 const bytes=method===0?compressed:method===8?inflateRawSync(compressed):null;assert.ok(bytes,'Unsupported compression');assert.equal(bytes.length,zip.readUInt32LE(offset+24));entries.set(name,bytes);offset+=46+nameSize+extra+comment;
}
class Reader{
 constructor(bytes){this.bytes=bytes;this.offset=0;}
 int(){const value=this.bytes.readInt32LE(this.offset);this.offset+=4;return value;}
 magic(){const text=this.bytes.subarray(this.offset,this.offset+4).toString('ascii');this.offset+=4;return text;}
 line(){const end=this.bytes.indexOf(10,this.offset);assert.ok(end>=this.offset&&end-this.offset<200);const text=this.bytes.subarray(this.offset,end).toString('ascii');this.offset=end+1;return text;}
}
const projectEntry=[...entries].find(([name])=>name.endsWith('/project.pzmap.json')),worldEntry=[...entries].find(([name])=>name.endsWith('/world.pzworld.json'));assert.ok(projectEntry||worldEntry);
const world=worldEntry?JSON.parse(worldEntry[1].toString('utf8')):null;
const projects=projectEntry?[JSON.parse(projectEntry[1].toString('utf8'))]:world.maps.map(m=>{const p=m.project;return {...p,buildings:[...p.buildings,...(world.buildings||[]).filter(b=>b.x<p.worldX*256+p.width&&b.y<p.worldY*256+p.height&&b.x+b.width>=p.worldX*256&&b.y+b.height>=p.worldY*256).map(b=>({...b,x:b.x-p.worldX*256,y:b.y-p.worldY*256}))]};});
const headers=[...entries].filter(([name])=>name.endsWith('.lotheader'));
assert.equal(headers.length,projects.reduce((sum,p)=>sum+p.width*p.height/65536,0));
const allTiles=new Set();let chunks=0,rooms=0,buildings=0;
for(const [name,bytes] of headers){
 const r=new Reader(bytes);assert.equal(r.magic(),'LOTH');assert.equal(r.int(),1);const tileCount=r.int();assert.ok(tileCount>0&&tileCount<1000);for(let i=0;i<tileCount;i++)allTiles.add(r.line());
 assert.equal(r.int(),8);assert.equal(r.int(),8);const min=r.int(),max=r.int();assert.ok(min>=-32&&min<=0);assert.ok(max>=1&&max<=31);const roomCount=r.int();rooms+=roomCount;
 for(let i=0;i<roomCount;i++){r.line();const z=r.int();assert.ok(z>=min&&z<=max);const rects=r.int();assert.ok(rects>0&&rects<=4096);for(let j=0;j<rects;j++){const x=r.int(),y=r.int(),w=r.int(),h=r.int();assert.ok(x>=0&&y>=0&&w>0&&h>0&&x+w<=256&&y+h<=256);}const objects=r.int();assert.ok(objects>=0&&objects<1000);for(let j=0;j<objects*3;j++)r.int();}
 const buildingCount=r.int();buildings+=buildingCount;for(let i=0;i<buildingCount;i++){const count=r.int();assert.ok(count>0&&count<=roomCount);for(let j=0;j<count;j++){const id=r.int();assert.ok(id>=0&&id<roomCount);}}
 assert.equal(bytes.length-r.offset,1024,'Zombie grid is not32x32');
 const match=name.match(/(\d+)_(\d+)\.lotheader$/);assert.ok(match);const cx=+match[1],cy=+match[2];const owners=projects.filter(project=>cx>=project.worldX&&cx<project.worldX+project.width/256&&cy>=project.worldY&&cy<project.worldY+project.height/256);assert.equal(owners.length,1,'Native cell has no unique source section');
 const folder=name.slice(0,name.lastIndexOf('/')+1),pack=entries.get(folder+`world_${cx}_${cy}.lotpack`);assert.ok(pack);
 const p=new Reader(pack);assert.equal(p.magic(),'LOTP');assert.equal(p.int(),1);assert.equal(p.int(),1024);const positions=[];for(let i=0;i<1024;i++){positions.push(Number(pack.readBigInt64LE(p.offset)));p.offset+=8;}
 for(let i=0;i<1024;i++){
  assert.ok(positions[i]>=8204&&positions[i]<pack.length);p.offset=positions[i];let squares=0;
  const squareCount=64*(max-min+1);while(squares<squareCount){const n=p.int();if(n===-1){const skip=p.int();assert.ok(skip>0);squares+=skip;}else{assert.ok(n>=2&&n<30);const room=p.int();assert.ok(room>=-1&&room<roomCount);for(let j=1;j<n;j++){const tile=p.int();assert.ok(tile>=0&&tile<tileCount);}squares++;}}
  assert.equal(squares,squareCount);assert.equal(p.offset,i<1023?positions[i+1]:pack.length);chunks++;
 }
 const navigation=entries.get(folder+`chunkdata_${cx}_${cy}.bin`);assert.ok(navigation);assert.equal(navigation.readUInt16BE(0),1);let cursor=2;
 for(let i=0;i<1024;i++){const kind=navigation[cursor++];assert.ok(kind<=5);if(kind===2){assert.ok(cursor+64<=navigation.length);for(let j=0;j<64;j++)assert.ok(navigation[cursor+j]<=63);cursor+=64;}}
 assert.equal(cursor,navigation.length);
}
const expectedBuildings=projects.reduce((sum,project)=>sum+project.buildings.reduce((n,b)=>{const cells=new Set();for(let y=0;y<b.height;y++)for(let x=0;x<b.width;x++)if(b.x+x>=0&&b.y+y>=0&&b.x+x<project.width&&b.y+y<project.height&&(!b.design||b.design.floors.some(f=>f.cells[y*b.width+x]!=='.')))cells.add(`${Math.floor((b.x+x)/256)},${Math.floor((b.y+y)/256)}`);return n+cells.size;},0),0);assert.equal(buildings,expectedBuildings);
const worldmap=[...entries].find(([name])=>name.endsWith('/worldmap.xml'));assert.ok(worldmap[1].toString('utf8').includes('cellSize="256"'));
const attribution=[...entries].find(([name])=>name.endsWith('/ATTRIBUTION.txt'));assert.ok(attribution);for(const project of projects)for(const item of [...project.attribution,...project.buildings.flatMap(b=>b.attribution||[]),...(project.buildingLibrary||[]).flatMap(b=>b.attribution||[])])assert.ok(attribution[1].toString('utf8').includes(item),'Attribution lost');
if(process.argv[3]){const source=process.argv[3],directory=(await stat(source)).isDirectory(),definitions=directory?(await Promise.all(['newtiledefinitions.tiles.txt','tiledefinitions_erosion.tiles.txt','jumbo_trees.tiles.txt','jumbo_trees_big.tiles.txt'].map(file=>readFile(source+'/'+file,'utf8').catch(()=>'')))).join('\n'):await readFile(source,'utf8'),known=new Set([...definitions.matchAll(/\/\/ ([a-zA-Z0-9_]+_\d+)/g)].map(m=>m[1]));for(const name of allTiles)assert.ok(known.has(name),`Unknown installed game tile: ${name}`);
 const catalog=JSON.parse(await readFile(new URL('../src/SimplePZMapmaker.Web/wwwroot/building-objects.json',import.meta.url),'utf8'));for(const [kind,directions] of Object.entries(catalog))for(const [direction,parts] of Object.entries(directions))for(const part of parts){const props=definitions.match(new RegExp('// '+part.tile+'\\r?\\n\\s*tile\\s*\\{([^}]+)\\}'))?.[1];assert.ok(props,'Missing furniture tile '+part.tile);assert.equal(props.match(/Facing = (\w+)/)?.[1],direction.toUpperCase(),kind+' orientation');const grid=props.match(/SpriteGridPos = ([\d,]+)/)?.[1];if(parts.length>1)assert.equal(grid,`${part.dx},${part.dy}`,kind+' grid position');else assert.equal(grid,undefined,kind+' must be a complete single-square object');}
}
console.log(JSON.stringify({file,cells:headers.length,chunks,rooms,buildings,tileNames:allTiles.size,status:'valid native B42 structure',gameplay:'not playtested'}));
