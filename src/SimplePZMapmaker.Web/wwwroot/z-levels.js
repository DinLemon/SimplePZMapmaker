// Build 42 uses 64 signed levels; UI floor numbers include the ground floor.
export const minZ=-32,maxZ=31,aboveGroundCount=32,basementCount=32,totalLevels=64;
export const templateByteLimit=16_000_000;
export function validateTemplateBytes(size){if(!Number.isSafeInteger(size)||size<0||size>templateByteLimit)throw Error('The building template exceeds the 16 MB limit.');}
export const validZ=z=>Number.isInteger(z)&&z>=minZ&&z<=maxZ;
export const zLabel=z=>z<0?`Basement ${z}`:`Floor ${z+1}`;
export function usesExtendedZ(building){const d=building?.design;if(!d)return false;const min=d.minLevel??0;return min< -16||(Array.isArray(d.floors)&&min+d.floors.length-1>15)||d.roof==='none';}
export const highestAuthoredZ=b=>(b?.design?.minLevel??0)+(b?.design?.floors?.length??1)-1;
export function ensureRoofFits(b){if(highestPreviewZ(b)>maxZ)b.design.roof=highestAuthoredZ(b)===maxZ?'none':'flat';return b;}
export function highestPreviewZ(b){let top=highestAuthoredZ(b);const d=b?.design;if(!d||d.roof==='none'||!Array.isArray(d.floors))return top;
 const min=d.minLevel??0,covered=(f,x,y)=>{if(x<0||y<0||x>=b.width||y>=b.height)return false;const code=f.cells[y*b.width+x];return code!=='.'&&f.rooms[code]!=='balcony';};
 for(let i=0;i<d.floors.length;i++){const f=d.floors[i],z=min+i;if(z===-1)continue;for(let y=0;y<b.height;y++)for(let x=0;x<b.width;x++){if(!covered(f,x,y)||i+1<d.floors.length&&d.floors[i+1].cells[y*b.width+x]!=='.')continue;let roof=z+1;
  if(i===d.floors.length-1&&d.roof!=='flat'){let w=0,e=0,n=0,s=0;while(covered(f,x-w-1,y))w++;while(covered(f,x+e+1,y))e++;while(covered(f,x,y-n-1))n++;while(covered(f,x,y+s+1))s++;const acrossX=w+e<=n+s,first=acrossX?w:n,second=acrossX?e:s,depth=d.roof==='hipped'?Math.min(w,e,n,s):d.roof==='shed'?first:Math.min(first,second);roof+=Math.floor(depth/6);}
  top=Math.max(top,roof);
 }}return top;
}
