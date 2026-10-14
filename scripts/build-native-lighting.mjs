import assert from 'node:assert/strict';
import {readFileSync,writeFileSync,mkdirSync,copyFileSync,readdirSync,rmSync} from 'node:fs';
import {resolve,join,relative,isAbsolute} from 'node:path';
import {fileURLToPath} from 'node:url';
import {execFileSync} from 'node:child_process';
import {createHash,randomUUID} from 'node:crypto';

// Supply an existing compiler: --javac=<executable>, or --java=<executable> --ecj=<compiler.jar>.
// Optional --output=<workspace directory> can produce a reviewable build without replacing the shipped adapter.
const root=resolve(fileURLToPath(new URL('../',import.meta.url)));
const options=Object.fromEntries(process.argv.slice(2).map(arg=>{const match=/^--(javac|java|ecj|output)=(.+)$/.exec(arg);if(!match)throw Error('Use --javac=... or --java=... --ecj=..., with optional --output=...');return [match[1],match[2]];}));
if((!options.javac&&(!options.java||!options.ecj))||(options.javac&&(options.java||options.ecj)))throw Error('Select exactly one Java compiler: javac, or a Java runtime with an ECJ JAR.');
const inside=path=>{const target=resolve(root,path),rel=relative(root,target);if(rel.startsWith('..')||isAbsolute(rel))throw Error('Compiler output must stay inside the project workspace.');return target;};
const output=inside(options.output||'tools/NativeLighting'),job=inside(join('work','native-lighting-build',randomUUID())),tmp=join(job,'tmp'),home=join(job,'home'),source=join(root,'tools','NativeLighting','LightingProbe.java');
for(const path of [job,tmp,home])mkdirSync(path,{recursive:true});
const env={...process.env,TEMP:tmp,TMP:tmp};for(const name of ['JAVA_TOOL_OPTIONS','JDK_JAVA_OPTIONS','_JAVA_OPTIONS','CLASSPATH'])delete env[name];
const hash=bytes=>createHash('sha256').update(bytes).digest('hex');
try{
 const binaries=[];
 for(const pass of ['first','second']){
  const classes=join(job,pass);mkdirSync(classes,{recursive:true});
  const compiler=options.javac?resolve(options.javac):resolve(options.java),runtime=['-Duser.home='+home,'-Djava.io.tmpdir='+tmp,'-XX:ErrorFile='+join(job,'hs_err_%p.log'),'-XX:-CreateCoredumpOnCrash'];
  const args=options.javac?[...runtime.map(arg=>'-J'+arg),'--release','17','-g:none','-encoding','UTF-8','-d',classes,source]:[...runtime,'-jar',resolve(options.ecj),'-17','-g:none','-encoding','UTF-8','-d',classes,source];
  try{execFileSync(compiler,args,{cwd:job,env,timeout:30000,maxBuffer:1024*1024,stdio:['ignore','pipe','pipe']});}
  catch(error){writeFileSync(join(job,pass+'.log'),Buffer.concat([error.stdout||Buffer.alloc(0),error.stderr||Buffer.alloc(0)]));throw Error('Lighting adapter compilation failed; inspect work/native-lighting-build logs.');}
  assert.deepEqual(readdirSync(classes),['LightingProbe.class'],'Unexpected generated classes');const bytes=readFileSync(join(classes,'LightingProbe.class'));assert.equal(bytes.readUInt32BE(0),0xcafebabe);assert.equal(bytes.readUInt16BE(6),61);binaries.push(bytes);
 }
 assert.ok(binaries[0].equals(binaries[1]),'Two isolated builds produced different bytecode');
 mkdirSync(join(output,'classes'),{recursive:true});writeFileSync(join(output,'classes','LightingProbe.class'),binaries[0]);
 if(resolve(output,'LightingProbe.java')!==source)copyFileSync(source,join(output,'LightingProbe.java'));
 writeFileSync(join(output,'manifest.json'),JSON.stringify({description:'Original reflection adapter; no game code or libraries bundled',protocol:2,javaTarget:17,sourceEncoding:'UTF-8 with LF line endings',sourceSha256:hash(readFileSync(source,'utf8').replaceAll('\r\n','\n')),classSha256:hash(binaries[0])},null,2)+'\n');
 console.log('PASS original lighting adapter: two deterministic Java 17 builds, source and bytecode integrity manifest written.');
 // Both the generated path and its workspace containment were checked before recursive cleanup.
 rmSync(inside(job),{recursive:true,force:true});
}catch(error){console.error(error.message);process.exitCode=1;}
