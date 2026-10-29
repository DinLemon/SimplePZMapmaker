"""Live API smoke check against an isolated editor workspace (uses its regional cache)."""
import json
import os
from pathlib import Path
import time
import urllib.request

base = os.environ.get("PZMAP_TEST_URL", "http://127.0.0.1:48778")
out = Path("work/osm-resilience-api")
out.mkdir(parents=True, exist_ok=True)

def api(path, data=None):
    raw = None if data is None else json.dumps(data).encode("utf-8")
    request = urllib.request.Request(base + path, raw, {"Content-Type": "application/json", "Origin": base})
    try:
        with urllib.request.urlopen(request, timeout=300) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"{path}: {error.code}: {error.read().decode('utf-8')}") from error

lat, lon, extent = float(os.environ.get("PZMAP_TEST_LAT", "43.7387")), float(os.environ.get("PZMAP_TEST_LON", "7.4256")), 400
import math
dy = extent / 111320 / 2
dx = dy / math.cos(lat * math.pi / 180)
bounds = dict(south=lat-dy, north=lat+dy, west=lon-dx, east=lon+dx)
original = api("/api/generate", dict(kind="blank", size=512, seed=42, name="OSM resilience check"))
online = api("/api/import/osm", dict(project=original, bounds=bounds, sourceMode="auto"))
assert online["importedFeatures"] > 0 and online["sourceId"]
assert any("Region:" in warning for warning in online["warnings"])
print(f"PASS automatic regional map: {online['importedFeatures']} features, {len(online['project']['buildings'])} buildings", flush=True)

source = api(f"/api/osm/sources/{online['sourceId']}/download")
saved = api("/api/osm/sources", dict(name="offline-source.json", bounds=bounds, json=json.dumps(source)))
offline = api("/api/import/osm", dict(project=original, bounds=bounds, sourceMode="file", sourceId=saved["id"]))
assert offline["importedFeatures"] == online["importedFeatures"]
assert offline["project"]["buildings"] == online["project"]["buildings"]
assert any("No OSM server" in warning for warning in offline["warnings"])
assert api("/api/validate", offline["project"])["valid"]
print("PASS source download/upload and identical validated map from offline file", flush=True)

request = dict(bounds=bounds, metresPerTile=1, sectionSize=256, name="Offline source world", sourceMode="file", sourceId=saved["id"])
plan = api("/api/world/osm/plan", request)
assert plan["columns"] == 2 and plan["rows"] == 2
job = api("/api/world/osm/jobs", request)
start = time.monotonic()
while True:
    status = api("/api/world/osm/jobs/" + job["id"])
    if status["status"] in ["completed", "failed", "cancelled"]:
        break
    assert time.monotonic()-start < 300, status
    time.sleep(.25)
assert status["status"] == "completed", status
world = api("/api/world/osm/jobs/" + job["id"] + "/result")
assert len(world["maps"]) == 4
assert len({(member["project"]["worldX"], member["project"]["worldY"]) for member in world["maps"]}) == 4
assert world["geoReference"]["metresPerTile"] == 1
assert status["sourceId"] == saved["id"]
(out / "world.pzworld.json").write_text(json.dumps(world), encoding="utf-8")
(out / "result.json").write_text(json.dumps(dict(mapFeatures=online["importedFeatures"], mapBuildings=len(online["project"]["buildings"]), worldSections=len(world["maps"]), worldBuildings=sum(len(m["project"]["buildings"]) for m in world["maps"])+len(world["buildings"]), sourceId=saved["id"], warnings=status["warnings"]), indent=2), encoding="utf-8")
print("PASS offline world assembled into four adjacent sections; complete result and reusable source available", flush=True)
