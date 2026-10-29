import test from 'node:test';
import assert from 'node:assert/strict';
import {WorkspaceFlowState,workspaceForGuideContext} from '../src/SimplePZMapmaker.Web/wwwroot/workspace-flow.js';

test('workspace navigation changes presentation only and rejects busy navigation',()=>{
 const state=new WorkspaceFlowState();assert.equal(state.mode,'create');
 assert.equal(state.show('edit',{busy:true}),false);assert.equal(state.mode,'create');
 assert.equal(state.show('edit'),true);assert.equal(state.mode,'edit');
 assert.throws(()=>state.show('erase-project'),/workspace/i);
 assert.equal(Object.hasOwn(state,'project'),false);
});
test('project adoption enters editing only after an explicit create/open command',()=>{
 const state=new WorkspaceFlowState();state.projectOpened();assert.equal(state.mode,'create');
 state.expectProject();state.projectOpened();assert.equal(state.mode,'edit');
 state.show('check');state.projectOpened();assert.equal(state.mode,'check');
});
test('guides reveal the relevant task without triggering creation or export',()=>{
 assert.equal(workspaceForGuideContext('menu-import'),'create');assert.equal(workspaceForGuideContext('import'),'create');
 assert.equal(workspaceForGuideContext('overview'),'edit');assert.equal(workspaceForGuideContext('layers'),'edit');
 assert.equal(workspaceForGuideContext('jobs'),'check');assert.equal(workspaceForGuideContext('unknown'),null);
});
