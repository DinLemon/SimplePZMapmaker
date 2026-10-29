"""Production offline import above the old ceiling; isolated local preview only."""
import json, math, os, sys, time, urllib.request, urllib.error
from pathlib import Path
base=os.environ.get('PZMAP_TEST_URL','http://127.0.0.1:48822').rstrip('/')
assert base.startswith('http://127.0.0.1:') and base.rsplit(':',1)[1] not in ('48814','48821','48779','48813')
out=Path(__file__).resolve().parents[1]/'work'/'incremental-world-import'; out.mkdir(parents=True,exist_ok=True)
def call(path,data=None):
 req=urllib.request.Request(base+path,data=json.dumps(data).encode() if data is not None else None,headers={'Content-Type':'application/json','Origin':base})
 try:
  with urllib.request.urlopen(req,timeout=180) as r:return json.load(r)
 except urllib.error.HTTPError as e:raise RuntimeError(f'{e.code}: {e.read(2000).decode()}')
columns,rows,size=54,18,1024
north=50.2; south=north-(rows*size-1)/111320
west=30; east=west+(columns*size-1)/(111320*math.cos((north+south)/2*math.pi/180))
bounds=dict(west=west,east=east,south=south,north=north)
def point(x,y):return [west+x/(111320*math.cos((north+south)/2*math.pi/180)),north-y/111320]
def feature(id,kind,coordinates,properties):return dict(type='Feature',id=id,geometry=dict(type=kind,coordinates=coordinates),properties=properties)
source=dict(type='FeatureCollection',features=[
 feature('seam-house','Polygon',[[point(1018,30),point(1036,30),point(1036,44),point(1018,44),point(1018,30)]],{'building':'house'}),
 feature('road','LineString',[point(20,80),point(1800,80)],{'highway':'residential'}),
 feature('rail','LineString',[point(20,140),point(1800,140)],{'railway':'rail'}),
 feature('fence','LineString',[point(1000,60),point(1060,60)],{'barrier':'fence'}),
 feature('gate-left','Point',point(1010,60),{'barrier':'gate'}),feature('gate-right','Point',point(1040,60),{'barrier':'gate'}),
 feature('river','LineString',[point(1024,165),point(1024,225)],{'waterway':'stream','width':'8'}),
 feature('bridge','LineString',[point(990,190),point(1060,190)],{'highway':'residential','bridge':'yes','width':'6'})])
request=dict(bounds=bounds,metresPerTile=1,sectionSize=size,originCellX=100,originCellY=100,name='15552-cell incremental verification',allowExpandedLayout=True,preferSections=True,sourceMode='file')
limits=call('/api/world/osm/limits'); assert limits['maxCells']==65536 and limits['maxResidentCells']==256
saved=call('/api/osm/sources',dict(name='Incremental seam and transport proof',bounds=bounds,json=json.dumps(source)))
request['sourceId']=saved['id'];plan=call('/api/world/osm/plan',request);assert plan['cells']==15552 and plan['columns']==columns and plan['rows']==rows
assert plan['assemblyMode']=='incremental-sections' and plan['maxResidentCells']==256
started=time.monotonic();job=call('/api/world/osm/jobs',request)['id'];last=None
(out/'active-job.json').write_text(json.dumps(dict(job=job,request=request,plan=plan),indent=2))
while True:
 state=call('/api/world/osm/jobs/'+job)
 progress=state['progress']; marker=(progress['phase'],progress['completed']//50)
 if marker!=last: print(state['status'],progress['phase'],progress['completed'],progress['total'],flush=True);last=marker
 if state['status'] in ('failed','cancelled'):raise RuntimeError(state)
 if state['status']=='completed':break
 assert time.monotonic()-started<1200,'Import exceeded verification budget'
 time.sleep(.35)
assert state['assembly']['mode']=='incremental-sections' and state['assembly']['sectionsWritten']==972
assert state['assembly']['peakResidentCells']<=256 and state['assembly']['sharedBuildings']==1 and state['assembly']['sharedFences']==1
manifest=call('/api/world/workspaces/'+state['workspaceId']);assert len(manifest['sections'])==972
samples=[]
for index in [0,1,486,971]:
 s=manifest['sections'][index];section=call('/api/world/workspaces/'+state['workspaceId']+'/sections/'+s['id'])
 assert section['member']['project']['width']==1024 and section['shared']['geoReference']['bounds']==bounds and section['shared']['geoReference']['metresPerTile']==1
 if index<2:
  assert len(section['shared']['buildings'])==1 and len(section['shared']['fences'])==1 and len(section['shared']['fences'][0]['gates'])==2
  paths=section['member']['project']['linearFeatures'];assert any(f['kind']=='railway' for f in paths) and any(f['kind']=='bridge' for f in paths)
  assert all(b.get('sourceId')!='geo-seam-house' for b in section['member']['project']['buildings'])
 samples.append(dict(index=index,id=s['id'],worldX=s['worldX'],worldY=s['worldY']))
result=dict(job=job,workspaceId=state['workspaceId'],elapsedSeconds=round(time.monotonic()-started,2),cells=plan['cells'],sections=len(manifest['sections']),bounds=bounds,metresPerTile=1,assembly=state['assembly'],sampledSections=samples)
(out/'large-import-results.json').write_text(json.dumps(result,indent=2))
print('PASS',json.dumps(result),flush=True)
