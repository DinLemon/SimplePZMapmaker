// Schematic authoring evidence: actual room cells, wall edges and proposed changes.
export function drawDesignComparison(canvas,before,after,constraints,changes=[]){
 const ctx=canvas.getContext('2d');canvas.width=640;canvas.height=320;ctx.fillStyle='#162119';ctx.fillRect(0,0,640,320);const level=constraints.lightingLevel??0;
 const relevant=[...before.buildings,...after.buildings].filter(b=>b.x<constraints.x+constraints.width&&b.y<constraints.y+constraints.height&&b.x+b.width>constraints.x&&b.y+b.height>constraints.y);
 const x=relevant.length?Math.max(constraints.x,Math.min(...relevant.map(b=>b.x))-2):constraints.x,y=relevant.length?Math.max(constraints.y,Math.min(...relevant.map(b=>b.y))-2):constraints.y;
 const right=relevant.length?Math.min(constraints.x+constraints.width,Math.max(...relevant.map(b=>b.x+b.width))+3):constraints.x+constraints.width,bottom=relevant.length?Math.min(constraints.y+constraints.height,Math.max(...relevant.map(b=>b.y+b.height))+3):constraints.y+constraints.height;
 const scale=Math.min(288/(right-x),264/(bottom-y));
 for(const [p,offset,label] of [[before,0,'Source'],[after,320,'Isolated proposal']]){
  const px=v=>offset+16+(v-x)*scale,py=v=>38+(v-y)*scale;ctx.fillStyle='#d8d3b5';ctx.font='13px Segoe UI';ctx.fillText(`${label} · Z=${level}`,offset+14,22);
  for(let yy=y;yy<bottom;yy++)for(let xx=x;xx<right;xx++){ctx.fillStyle=['#667b51','#3f5a3e','#787975','#507b85','#c6b58d','#8e765e','#a5a395'][p.terrain[yy*p.width+xx]]||'#667b51';ctx.fillRect(px(xx),py(yy),scale,scale);}
  for(const b of p.buildings){const d=b.design,index=level-(d?.minLevel??0),f=d?.floors[index];if(!f)continue;
   for(let yy=0;yy<b.height;yy++)for(let xx=0;xx<b.width;xx++){const code=f.cells[yy*b.width+xx];if(code==='.'||!code)continue;ctx.fillStyle=`hsl(${35+(code.charCodeAt(0)*31)%65} 27% ${55+(code.charCodeAt(0)*7)%16}%)`;ctx.fillRect(px(b.x+xx),py(b.y+yy),scale,scale);}
   ctx.lineWidth=Math.max(1,scale*.12);for(const e of f.edges){ctx.strokeStyle=e.kind==='door'?'#52dfc2':e.kind==='window'?'#7bb4e8':e.kind==='open'?'#d5ad6f':'#463b31';ctx.beginPath();ctx.moveTo(px(b.x+e.x),py(b.y+e.y));ctx.lineTo(px(b.x+e.x+(e.edge==='n'?1:0)),py(b.y+e.y+(e.edge==='w'?1:0)));ctx.stroke();}
   for(const obj of [...f.objects,...f.staticModels||[]]){ctx.fillStyle=obj.kind==='stairs'?'#e5bb69':'#3c4a45';ctx.fillRect(px(b.x+obj.x)+scale*.2,py(b.y+obj.y)+scale*.2,scale*.6,scale*.6);}
  }
  if(p===after){ctx.strokeStyle='#fff2ad';ctx.lineWidth=2;for(const c of changes)if(c.z===level)ctx.strokeRect(px(c.x)-2,py(c.y)-2,scale+4,scale+4);}
 }
 ctx.fillStyle='#cfc7ad';ctx.font='11px Segoe UI';ctx.fillText('Teal: doors · blue: windows · gold boxes: proposed repairs',16,313);
}
