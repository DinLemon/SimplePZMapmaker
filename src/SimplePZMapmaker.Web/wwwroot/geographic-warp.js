// Keep limits, triangulation, predicates and operation order aligned with Core/GeographicWarp*.cs.
const maxAxisPoints=33,maxCoordinate=1_000_000,minSpacing=.01,minDeterminant=1e-6;
const coordinateTolerance=1e-8,weightTolerance=1e-10;
const compiled=new WeakMap();
const cross=(a,b,c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
const finite=p=>p&&Number.isFinite(p.x)&&Number.isFinite(p.y);
const weights=(p,a,b,c)=>{const area=cross(a,b,c);return [cross(a,p,c)/area,cross(a,b,p)/area];};
const map=(p,t,from,to)=>{const [u,v]=weights(p,from[t[0]],from[t[1]],from[t[2]]),a=to[t[0]],b=to[t[1]],c=to[t[2]];return {x:a.x+u*(b.x-a.x)+v*(c.x-a.x),y:a.y+u*(b.y-a.y)+v*(c.y-a.y)};};
function interval(axis,value){let lo=0,hi=axis.length-1;while(lo<=hi){const mid=(lo+hi)>>1;if(axis[mid]===value)return Math.min(mid,axis.length-2);if(axis[mid]<value)lo=mid+1;else hi=mid-1;}return Math.max(0,Math.min(hi,axis.length-2));}
function axisValid(axis){
 if(!Array.isArray(axis)||axis.length<2||axis.length>maxAxisPoints||axis[0]!==0)throw Error('Warp axes must start at zero and have 2-33 coordinates.');
 for(let i=0;i<axis.length;i++)if(!Number.isFinite(axis[i])||axis[i]<0||axis[i]>maxCoordinate||(i>0&&axis[i]-axis[i-1]<minSpacing))throw Error('Warp axes require finite increasing coordinates, spacing >=0.01 tile, and extent <=1000000 tiles.');
}
function segmentsTouch(a,b,c,d){
 if(Math.max(a.x,b.x)<Math.min(c.x,d.x)-coordinateTolerance||Math.max(c.x,d.x)<Math.min(a.x,b.x)-coordinateTolerance||Math.max(a.y,b.y)<Math.min(c.y,d.y)-coordinateTolerance||Math.max(c.y,d.y)<Math.min(a.y,b.y)-coordinateTolerance)return false;
 const abC=cross(a,b,c),abD=cross(a,b,d),cdA=cross(c,d,a),cdB=cross(c,d,b);
 return !((abC>coordinateTolerance&&abD>coordinateTolerance)||(abC< -coordinateTolerance&&abD< -coordinateTolerance)||(cdA>coordinateTolerance&&cdB>coordinateTolerance)||(cdA< -coordinateTolerance&&cdB< -coordinateTolerance));
}
function validateBoundary(destination,nx,ny){
 const boundary=[];
 for(let x=0;x<nx;x++)boundary.push(destination[x]);
 for(let y=1;y<ny;y++)boundary.push(destination[y*nx+nx-1]);
 for(let x=nx-2;x>=0;x--)boundary.push(destination[(ny-1)*nx+x]);
 for(let y=ny-2;y>0;y--)boundary.push(destination[y*nx]);
 for(let i=0;i<boundary.length;i++){
  const a=boundary[i],b=boundary[(i+1)%boundary.length],c=boundary[(i+2)%boundary.length];
  if(Math.abs(cross(a,b,c))<=coordinateTolerance&&(a.x-b.x)*(c.x-b.x)+(a.y-b.y)*(c.y-b.y)>=0)throw Error('Warp boundary doubles back or collapses.');
  for(let j=i+2;j<boundary.length;j++){
   if(i===0&&j===boundary.length-1)continue;
   if(segmentsTouch(a,b,boundary[j],boundary[(j+1)%boundary.length]))throw Error('Warp boundary self-intersects/touches; destination triangles overlap.');
  }
 }
}
function validateCoverage(mesh,reference){
 if(!reference)return;
 const b=reference.bounds,scale=reference.metresPerTile,angle=(reference.rotationDegrees??0)*Math.PI/180;
 if(!b||!Number.isFinite(scale)||scale<.5||scale>20||![b.west,b.east,b.north,b.south,angle].every(Number.isFinite))throw Error('Invalid affine warp reference.');
 const w=(b.east-b.west)*Math.cos((b.north+b.south)/2*Math.PI/180)*111320/scale,h=(b.north-b.south)*111320/scale;
 if(w<=0||h<=0||mesh.sourceWidth+1e-8<Math.abs(w*Math.cos(angle))+Math.abs(h*Math.sin(angle))||mesh.sourceHeight+1e-8<Math.abs(w*Math.sin(angle))+Math.abs(h*Math.cos(angle)))throw Error('Warp source lattice must cover the entire affine world footprint.');
}

/** Validate and snapshot an editable camelCase Warp DTO. Returns immutable forward/inverse methods.
 * Call again after in-place edits; inverseGeographicWarp/warpedWorldGeographic reuse the last snapshot.
 * The complete disk + positive faces + simple boundary rule is the same degree-one check as Core. */
export function validateGeographicWarp(definition,reference=null){
 if(!definition||typeof definition!=='object')throw Error('Missing geographic warp.');
 axisValid(definition.sourceX);axisValid(definition.sourceY);
 if(!Array.isArray(definition.destination)||definition.destination.length!==definition.sourceX.length*definition.sourceY.length)throw Error('Warp needs every lattice destination vertex; holes are forbidden.');
 const xs=[...definition.sourceX],ys=[...definition.sourceY],destination=definition.destination.map(p=>{
  if(!finite(p)||p.x<0||p.y<0||p.x>maxCoordinate||p.y>maxCoordinate)throw Error('Warp destination coordinates must be finite and between 0 and 1000000 tiles.');
  return {x:p.x,y:p.y};
 });
 const source=ys.flatMap(y=>xs.map(x=>({x,y}))),triangles=[];
 for(let y=0;y<ys.length-1;y++)for(let x=0;x<xs.length-1;x++){
  const a=y*xs.length+x,b=a+1,c=a+xs.length,d=c+1;triangles.push([a,b,d],[a,d,c]);
 }
 let minimumDeterminant=Infinity;
 for(const t of triangles){
  const area=cross(destination[t[0]],destination[t[1]],destination[t[2]]),determinant=area/cross(source[t[0]],source[t[1]],source[t[2]]);
  if(!Number.isFinite(determinant)||determinant<minDeterminant||area<=1e-8)throw Error('Geographic warp folds, collapses or is numerically degenerate.');
  minimumDeterminant=Math.min(minimumDeterminant,determinant);
 }
 validateBoundary(destination,xs.length,ys.length);
 const sourceWidth=xs.at(-1),sourceHeight=ys.at(-1);
 const mesh=Object.freeze({sourceWidth,sourceHeight,minimumDeterminant,
  forward(x,y){
   if(!Number.isFinite(x)||!Number.isFinite(y)||x< -coordinateTolerance||y< -coordinateTolerance||x>sourceWidth+coordinateTolerance||y>sourceHeight+coordinateTolerance)throw Error('Source coordinate is outside the bounded geographic warp.');
   const point={x:Math.max(0,Math.min(x,sourceWidth)),y:Math.max(0,Math.min(y,sourceHeight))},ix=interval(xs,point.x),iy=interval(ys,point.y);
   const u=(point.x-xs[ix])/(xs[ix+1]-xs[ix]),v=(point.y-ys[iy])/(ys[iy+1]-ys[iy]);
   return map(point,triangles[2*(iy*(xs.length-1)+ix)+(v<=u?0:1)],source,destination);
  },
  inverse(x,y){
   if(!Number.isFinite(x)||!Number.isFinite(y))return null;
   const point={x,y};
   for(const t of triangles){
    const a=destination[t[0]],b=destination[t[1]],c=destination[t[2]];
    if(x<Math.min(a.x,Math.min(b.x,c.x))-coordinateTolerance||x>Math.max(a.x,Math.max(b.x,c.x))+coordinateTolerance||y<Math.min(a.y,Math.min(b.y,c.y))-coordinateTolerance||y>Math.max(a.y,Math.max(b.y,c.y))+coordinateTolerance)continue;
    const [u,v]=weights(point,a,b,c);
    if(u>= -weightTolerance&&v>= -weightTolerance&&u+v<=1+weightTolerance)return map(point,t,destination,source);
   }
   return null;
  }
 });
 validateCoverage(mesh,reference);compiled.set(definition,mesh);return mesh;
}

/** Origin-relative destination tiles -> origin-relative affine source tiles; null outside.
 * Malformed meshes throw. The DTO is a job snapshot; revalidate or replace it after edits. */
export function inverseGeographicWarp(definition,x,y){return (compiled.get(definition)??validateGeographicWarp(definition)).inverse(x,y);}

/** Split at grid lines and triangle diagonals before mapping; endpoint-only projection loses bends. */
export function forwardGeographicWarpPath(definition,points){
 const mesh=compiled.get(definition)??validateGeographicWarp(definition),xs=definition.sourceX,ys=definition.sourceY,out=[];
 if(!Array.isArray(points)||points.length>100000)throw Error('Warp path exceeds 100000 input points.');
 if(!points.length)return out;out.push(mesh.forward(points[0][0],points[0][1]));
 for(let i=1;i<points.length;i++){
  const [ax,ay]=points[i-1],[bx,by]=points[i],dx=bx-ax,dy=by-ay,cuts=[0,1];mesh.forward(bx,by);
  if(dx)for(const x of xs){const t=(x-ax)/dx;if(t>0&&t<1)cuts.push(t);}
  if(dy)for(const y of ys){const t=(y-ay)/dy;if(t>0&&t<1)cuts.push(t);}
  const grid=[...new Set(cuts)].sort((a,b)=>a-b);
  for(let k=1;k<grid.length;k++){const t=(grid[k-1]+grid[k])/2,x=interval(xs,ax+dx*t),y=interval(ys,ay+dy*t),w=xs[x+1]-xs[x],h=ys[y+1]-ys[y],denominator=dx/w-dy/h;if(!denominator)continue;const split=((ay-ys[y])/h-(ax-xs[x])/w)/denominator;if(split>grid[k-1]&&split<grid[k])cuts.push(split);}
  for(const t of [...new Set(cuts)].sort((a,b)=>a-b).slice(1)){out.push(mesh.forward(ax+dx*t,ay+dy*t));if(out.length>100000)throw Error('Warp subdivision exceeds 100000 output points.');}
 }
 return out;
}

/** Absolute game tile coordinates -> WGS84, including global rotation and its exact min offsets.
 * Returns null outside a warped destination disk. Does not clip WGS84 to reference.bounds:
 * the complete rectangular mesh can include the corners outside a rotated geographic footprint. */
export function warpedWorldGeographic(reference,x,y){
 const b=reference.bounds,cosine=Math.cos((b.north+b.south)/2*Math.PI/180);
 let sx,sy;
 if(reference.warp){
  const mesh=compiled.get(reference.warp)??validateGeographicWarp(reference.warp,reference);validateCoverage(mesh,reference);
  const p=mesh.inverse(x-reference.originCellX*256,y-reference.originCellY*256);if(!p)return null;sx=p.x;sy=p.y;
 }else{sx=x-reference.originCellX*256;sy=y-reference.originCellY*256;}
 if(!reference.rotationDegrees)return {longitude:b.west+sx*reference.metresPerTile/(cosine*111320),latitude:b.north-sy*reference.metresPerTile/111320};
 const angle=reference.rotationDegrees*Math.PI/180,sin=Math.sin(angle),cos=Math.cos(angle),px=cosine*111320/reference.metresPerTile,py=111320/reference.metresPerTile,cx=(b.west+b.east)/2,cy=(b.north+b.south)/2;
 const rotated=(lon,lat)=>{const e=(lon-cx)*px,n=(cy-lat)*py;return [e*cos-n*sin,e*sin+n*cos];};
 const corners=[[b.west,b.north],[b.east,b.north],[b.west,b.south],[b.east,b.south]].map(c=>rotated(...c));
 const rx=sx+Math.min(...corners.map(c=>c[0])),ry=sy+Math.min(...corners.map(c=>c[1]));
 return {longitude:cx+(rx*cos+ry*sin)/px,latitude:cy-(-rx*sin+ry*cos)/py};
}
