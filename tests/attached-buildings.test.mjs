import test from 'node:test';
import assert from 'node:assert/strict';
import {blank,canPlace,attachedPair} from '../src/SimplePZMapmaker.Web/wwwroot/editor.js';
import {refreshEdges} from '../src/SimplePZMapmaker.Web/wwwroot/building.js';

const room=(id,x,y,width,height,extra={})=>{const b={id,name:id,kind:'warehouse',x,y,width,height,design:{material:'wood',roof:'flat',shape:'rect',variant:1,floors:[{cells:'1'.repeat(width*height),rooms:{'1':'storage'},edges:[],objects:[]}]},...extra};refreshEdges(b,0);return b;};

test('an attached shed may share the house wall line but never its rooms',()=>{
 const p=blank(),house=room('house',10,10,6,6);p.buildings=[house];
 const loose=room('shed',16,11,2,2);assert.equal(canPlace(p,loose),false,'touching without attachment keeps the free line');
 const shed=room('shed',16,11,2,2,{attachedTo:'house'});assert.equal(attachedPair(house,shed),true);assert.equal(canPlace(p,shed),true);
 assert.equal(canPlace(p,room('shed',15,11,2,2,{attachedTo:'house'})),false,'rooms of an attached pair must not overlap');
});
