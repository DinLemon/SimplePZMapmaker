export function validateAccess(b){
 const d=b.design,cell=(f,x,y)=>x<0||y<0||x>=b.width||y>=b.height?'.':f.cells[y*b.width+x],labels=[],links=[],roots=new Set();
 const key=(x,y,edge)=>y*(b.width+1)+x+(edge==='w'?(b.width+1)*(b.height+1):0);
 for(const f of d.floors){const map=new Int32Array(f.cells.length);map.fill(-1);labels.push(map);const walls=new Set(f.edges.filter(e=>['wall','window'].includes(e.kind)).map(e=>key(e.x,e.y,e.edge)));
  for(let start=0;start<map.length;start++){if(f.cells[start]==='.'||map[start]>=0)continue;const component=links.length;links.push([]);const queue=[start];map[start]=component;
   for(let j=0;j<queue.length;j++){const i=queue[j],x=i%b.width,y=Math.floor(i/b.width);for(const [dx,dy] of [[1,0],[-1,0],[0,1],[0,-1]]){const nx=x+dx,ny=y+dy;if(cell(f,nx,ny)==='.')continue;const next=ny*b.width+nx,k=dx?key(Math.max(x,nx),y,'w'):key(x,Math.max(y,ny),'n');if(map[next]<0&&!walls.has(k)){map[next]=component;queue.push(next);}}}
  }
 }
 const ground=-(d.minLevel||0),g=d.floors[ground];for(const e of g.edges.filter(e=>['door','open'].includes(e.kind))){const a=cell(g,e.x,e.y),nx=e.x-(e.edge==='w'?1:0),ny=e.y-(e.edge==='n'?1:0),other=cell(g,nx,ny);if((a==='.')===(other==='.'))continue;roots.add(labels[ground][(a==='.'?ny:e.y)*b.width+(a==='.'?nx:e.x)]);}
 if(!roots.size)throw Error('On the ground floor (z=0) an external entrance is required.');
 for(let z=0;z<d.floors.length-1;z++)for(const stair of d.floors[z].objects.filter(o=>o.kind==='stairs')){const i=stair.y*b.width+stair.x,lower=labels[z][i],upper=labels[z+1][i];links[lower].push(upper);links[upper].push(lower);}
 const seen=new Set(roots),queue=[...roots];for(let j=0;j<queue.length;j++)for(const next of links[queue[j]])if(!seen.has(next)){seen.add(next);queue.push(next);}
 if(seen.size!==links.length)throw Error('There is an isolated room or entrance without access to an external entrance. Add a door, passage or staircase.');
}
