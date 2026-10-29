export async function readOvertureFile(file){
 if(!file)throw Error('Choose an Overture buildings GeoJSON file.');
 if(!Number.isFinite(file.size)||file.size<1||file.size>64_000_000)throw Error('The Overture buildings file must be at most 64 MB.');
 const json=await file.text();
 if(json.length>64_000_000||new TextEncoder().encode(json).length>64_000_000)throw Error('The Overture buildings file exceeds 64 MB.');
 let value;try{value=JSON.parse(json);}catch{throw Error('Could not read the Overture buildings GeoJSON.');}
 if(value?.type!=='FeatureCollection'||!Array.isArray(value.features))throw Error('Choose an Overture GeoJSON FeatureCollection.');
 return json;
}
export function overtureReport(result){
 for(const key of ['candidates','added','existing','tooSmall','outside'])if(!Number.isInteger(result?.[key])||result[key]<0)throw Error('Invalid Overture supplement report.');
 return `${result.added} added · ${result.existing} already mapped · ${result.tooSmall} too small · ${result.outside} outside. These are source roofprints, not verified interiors. Download the composed source to retain the baseline and source credits.`;
}
