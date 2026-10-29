// Authoring footprint only. C# shares native height planning between map and world export.
export function snapRampNode(point,anchor=null){
 const p={x:Math.round(point.x),y:Math.round(point.y)};
 if(anchor){if(Math.abs(p.x-anchor.x)>=Math.abs(p.y-anchor.y))p.y=anchor.y;else p.x=anchor.x;}
 return p;
}
export function roadRampCells(f){
 if(!f||!['road','bridge'].includes(f.kind)||![1,2].includes(f.level)||f.access!=='ramps'||!Number.isInteger(f.width)||f.width<1||f.width>16||f.interpolation!=='polyline'||!Array.isArray(f.points)||f.points.length!==2||f.points.some(p=>!p||!Number.isInteger(p.x)||!Number.isInteger(p.y))||(f.deckArtwork??[]).length)
  throw Error('Vehicle ramps need a straight road or bridge, two integer points, floor +1/+2, width 1–16 whole tiles and no deck artwork.');
 const [a,b]=f.points,dx=Math.sign(b.x-a.x),dy=Math.sign(b.y-a.y),length=Math.abs(b.x-a.x)+Math.abs(b.y-a.y);
 if((dx===0)===(dy===0)||length+1<40*f.level+2)throw Error('Ramp approaches need 20 cells per storey at both ends and at least two flat deck cells. Use one cardinal run.');
 if((length+1)*f.width>100000)throw Error('Ramp footprint exceeds 100,000 tiles.');
 const result=new Map();
 for(let at=0;at<=length;at++)for(let lane=0;lane<f.width;lane++){
  const cross=lane-Math.floor(f.width/2),x=a.x+dx*at+(dx===0?cross:0),y=a.y+dy*at+(dy===0?cross:0);
  result.set(`${x}:${y}`,{x,y});
 }
 return result;
}
