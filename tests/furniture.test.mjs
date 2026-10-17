import test from 'node:test';import assert from 'node:assert/strict';
import {objectParts,furnitureItem} from '../src/SimplePZMapmaker.Web/wwwroot/building.js';
import {transformFacing} from '../src/SimplePZMapmaker.Web/wwwroot/building-transform.js';
test('TileZed furniture resolves all four layouts and rotates with the building',()=>{
 const bed={kind:'furniture',asset:'bed:0',direction:'w',x:2,y:3};
 assert.deepEqual(objectParts(bed).map(p=>[p.dx,p.dy]),[[0,0],[1,0]]);assert.deepEqual(objectParts({...bed,direction:'s'}).map(p=>[p.dx,p.dy]),[[0,0],[0,1]]);
 assert.equal(furnitureItem('no-such','n'),undefined);assert.equal(transformFacing('W','cw'),'N');assert.equal(transformFacing('N','mirror-y'),'S');
});
