import {chromium} from '../work/qa/node_modules/playwright/index.mjs';
import assert from 'node:assert/strict';
import {mkdir} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
await mkdir(new URL('../work/qa-results/',import.meta.url),{recursive:true});
const browser=await chromium.launch({headless:true});
const page=await browser.newPage({viewport:{width:1440,height:960}});
const errors=[];page.on('pageerror',e=>errors.push(e.message));
try {
 await page.goto('http://127.0.0.1:48765');
 await page.getByRole('button',{name:'Новая карта',exact:true}).waitFor({timeout:10000});
 assert.equal(await page.title(),'SimplePZMapmaker');
 await page.waitForFunction(()=>document.querySelector('#map-summary')?.textContent.includes('256'));
 await page.getByRole('button',{name:'Новая карта',exact:true}).click();
 await page.locator('#new-kind').selectOption('blank');
 await page.getByRole('button',{name:'Создать карту',exact:true}).click();
 await page.waitForFunction(()=>document.querySelector('#building-count')?.textContent==='0');
 await page.screenshot({path:fileURLToPath(new URL('../work/qa-results/editor.png',import.meta.url)),fullPage:true});
 assert.deepEqual(errors,[]);console.log('PASS live editor creates a blank map without browser errors');
} finally {await browser.close();}
