import {chromium} from '../tools/BrowserSmoke/node_modules/playwright/index.mjs';
import assert from 'node:assert/strict';
import {mkdir,readFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const base=process.env.PZMAP_TEST_URL||'http://127.0.0.1:48768';
const out=new URL('../work/actions-smoke/',import.meta.url);await mkdir(out,{recursive:true});
const browser=await chromium.launch({headless:true});
const page=await browser.newPage({viewport:{width:1440,height:1000}}),errors=[];
page.on('pageerror',error=>errors.push(error.message));
try {
 await page.goto(base);assert.equal(await page.title(),'SimplePZMapmaker');
 assert.equal(await page.locator('html').getAttribute('lang'),'en');
 await page.waitForFunction(()=>document.querySelector('#map-summary')?.textContent.includes('256'));
 await page.locator('#new-map').click();await page.locator('#new-kind').selectOption('blank');
 await page.locator('#new-form [type=submit]').click();
 await page.waitForFunction(()=>document.querySelector('#building-count')?.textContent==='0');
 assert.ok(!/[А-Яа-яЁё]/.test(await page.locator('body').innerText()),'Visible interface must be English');
 const saved=page.waitForEvent('download');await page.locator('#save-project').click();
 const file=fileURLToPath(new URL('browser-project.pzmap.json',out));await(await saved).saveAs(file);
 const project=JSON.parse(await readFile(file,'utf8'));assert.equal(project.width,256);assert.equal(project.buildings.length,0);
 await page.locator('#project-name').fill('Unsaved change before reopening');await page.locator('#project-name').press('Tab');
 assert.equal(await page.locator('#project-name').inputValue(),'Unsaved change before reopening');
 await page.locator('#project-file').setInputFiles(file);
 await page.waitForFunction(name=>document.querySelector('#project-name').value===name&&!document.querySelector('#operation-dialog').open,project.name);
 assert.equal(await page.locator('#building-count').textContent(),'0');
 assert.ok((await page.locator('#map-summary').textContent()).includes('256'));
 await page.screenshot({path:fileURLToPath(new URL('editor.png',out)),fullPage:true});
 assert.deepEqual(errors,[]);console.log('PASS English editor fresh load, blank project, save/reopen and browser runtime errors');
} finally {await browser.close();}
