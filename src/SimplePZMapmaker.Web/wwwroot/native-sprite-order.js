// PZ paints the floor pass before objects at the same signed level.
// A roof sprite may cover a neighbouring square, so sorting every tile by x+y
// first lets the next ceiling square incorrectly paint over that roof.
export function nativeSpriteOrder(a,b,metadata){
 const layer=n=>metadata.get(n.tile)?.floor?0:(n.layer??1);
 return a.z-b.z||layer(a)-layer(b)||a.x+a.y-b.x-b.y||a.x-b.x;
}
