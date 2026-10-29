"""Cold regional import smoke against an isolated running editor; never touches its UI map."""
import argparse
import json
import math
import os
import sys
from pathlib import Path
import time
import urllib.error
import urllib.request

sys.stdout.reconfigure(encoding="utf-8")

parser = argparse.ArgumentParser()
parser.add_argument("--cities", nargs="+", default=["monaco", "berlin", "tokyo"])
parser.add_argument("--plan-only", action="store_true")
parser.add_argument("--world", action="store_true")
args = parser.parse_args()
base = os.environ.get("PZMAP_TEST_URL", "http://127.0.0.1:48778")
out = Path(os.environ.get("PZMAP_TEST_OUTPUT", "work/global-geodata-live"))
out.mkdir(parents=True, exist_ok=True)
cities = {"monaco": (43.7387, 7.4256), "berlin": (52.5200, 13.4050),
          "tokyo": (35.6812, 139.7671), "vaduz": (47.141, 9.5215),
          "louisville": (38.256, -85.758), "smolensk": (54.778, 32.047)}

def request(path, body=None):
    data = None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(base + path, data, {"Content-Type": "application/json", "Origin": base})
    try:
        return urllib.request.urlopen(req, timeout=3700)
    except urllib.error.HTTPError as e:
        raise RuntimeError(f"{path}: HTTP {e.code}: {e.read(10000).decode()}") from e

def api(path, body=None):
    with request(path, body) as response:
        return json.load(response)

for city in args.cities:
    lat, lon = cities[city]
    dy = 200 / 111320
    dx = dy / math.cos(lat * math.pi / 180)
    bounds = dict(south=lat-dy, north=lat+dy, west=lon-dx, east=lon+dx)
    plan = api("/api/osm/regions/plan", bounds)
    (out / f"{city}-plan.json").write_text(json.dumps(plan, indent=2), encoding="utf-8")
    print(city, "PLAN", [(r["id"], r["bytes"], r["cached"]) for r in plan["regions"]], flush=True)
    if args.plan_only:
        continue
    original = api("/api/generate", dict(kind="blank", size=512, seed=42, name=f"Global {city}"))
    start = time.monotonic()
    result = None
    phases = set()
    providers = set()
    progress_bytes = 0
    last_log = 0
    with request("/api/import/osm/stream", dict(project=original, bounds=bounds, sourceMode="global")) as response:
        while True:
            line = response.readline(128_000_000 + 65537)
            if not line:
                break
            assert len(line) < 128_000_000 + 65537 and line.endswith(b"\n"), "Truncated/oversized stream"
            if not line.strip():
                continue
            record = json.loads(line)
            if record["kind"] == "error":
                raise RuntimeError(record["error"])
            if record["kind"] == "result":
                result = record["result"]
            else:
                progress_bytes += len(line)
                assert progress_bytes <= 1024 * 1024, "Progress exceeds the browser stream envelope"
                progress = record["progress"]
                phase = progress["phase"]
                if phase == "downloaded":
                    providers.add(progress.get("source"))
                now = time.monotonic()
                if phase not in phases or now - last_log > 20:
                    print(city, "PROGRESS", progress, flush=True)
                    last_log = now
                phases.add(phase)
    assert result and result["sourceId"] and result["importedFeatures"] > 0
    assert providers == {"download.geofabrik.de"}, providers
    assert api("/api/validate", result["project"])["valid"]
    report = dict(features=result["importedFeatures"], buildings=len(result["project"]["buildings"]),
                  seconds=round(time.monotonic()-start, 2), warnings=result["warnings"], phases=sorted(phases), providers=sorted(providers), progressBytes=progress_bytes, sourceId=result["sourceId"])
    (out / f"{city}-result.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    (out / f"{city}-map.json").write_text(json.dumps(result["project"]), encoding="utf-8")
    print(city, "PASS", report, flush=True)
    if args.world:
        payload = dict(bounds=bounds, metresPerTile=1, sectionSize=256, name=f"Global {city} world", sourceMode="global")
        job = api("/api/world/osm/jobs", payload)
        deadline = time.monotonic() + 3700
        while True:
            status = api("/api/world/osm/jobs/" + job["id"])
            if status["status"] in ["completed", "failed", "cancelled"]:
                break
            assert time.monotonic() < deadline
            time.sleep(.5)
        assert status["status"] == "completed", status
        world = api("/api/world/osm/jobs/" + job["id"] + "/result")
        assert len(world["maps"]) == 4
        assert len({(m["project"]["worldX"], m["project"]["worldY"]) for m in world["maps"]}) == 4
        (out / f"{city}-world.pzworld.json").write_text(json.dumps(world), encoding="utf-8")
        print(city, "WORLD PASS: four adjacent sections", flush=True)
