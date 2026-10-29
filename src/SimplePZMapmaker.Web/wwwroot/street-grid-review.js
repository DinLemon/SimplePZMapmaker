// A measured source is reviewed before changing the shared world frame.
const key=request=>JSON.stringify(request);
export function reviewStreetGrid(request,response){
 const a=response?.alignment;
 if(!a||a.rotationDegrees!==null&&(!Number.isFinite(a.rotationDegrees)||Math.abs(a.rotationDegrees)>45)||!Number.isFinite(a.strength)||a.strength<0||a.strength>1||!Number.isFinite(a.roadMetres)||a.roadMetres<0||!Number.isInteger(a.districtCount)||a.districtCount<0)throw Error('Invalid street-grid analysis.');
 if(!/^[a-f0-9]{32}$/.test(response.sourceId??''))throw Error('The measured geographic source was not saved. Retry analysis.');
 return {requestKey:key(request),alignment:structuredClone(a),sourceId:response.sourceId};
}
export function applyStreetGridReview(request,review){
 if(request.preserveEdits||request.warp)throw Error('Keep the original rotation of a preserved or deformed world.');
 if(!review||review.requestKey!==key(request))throw Error('World settings or source changed. Measure the street grid again.');
 if(review.alignment.rotationDegrees===null||review.alignment.strength<.25)throw Error('No consistent street grid was found; keep the manual rotation.');
 return {rotationDegrees:review.alignment.rotationDegrees,sourceId:review.sourceId};
}
export function streetGridReviewText(review){
 const a=review.alignment,detail=`${Math.round(a.strength*100)}% of measured street length · ${Math.round(a.roadMetres).toLocaleString('en-GB')} m · ${a.districtCount} ${a.districtCount===1?'district':'districts'}`;
 return `${a.rotationDegrees===null?'No rotation suggested':`Suggested rotation: ${a.rotationDegrees.toFixed(2)}°`} · ${detail}. ${a.reason??''}`;
}
