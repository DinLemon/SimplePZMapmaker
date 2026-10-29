"""Independent fixture metadata and isolated-install byte comparison (standard library only)."""
import argparse
import json
import re
import zipfile
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--directory", type=Path, default=Path(__file__).resolve().parents[1] / "work/knoxmap-research/transport-import")
out = parser.parse_args().directory.resolve()
proof = json.loads((out / "api-browser-proof.json").read_text())
project = json.loads((out / "section.json").read_text())
mods = Path(proof["modsDirectory"]).resolve()
assert mods == (out.parent / "transport-isolated/mods").resolve(), "Expected disposable mods directory"
installed_map = (mods / proof["fixtureMod"] / "42/media/maps" / proof["fixtureMod"]).resolve()
assert installed_map.is_relative_to(mods) and installed_map.is_dir()

with zipfile.ZipFile(out / "TransportSource-B42.zip") as archive:
    objects = archive.read(next(n for n in archive.namelist() if n.endswith("/objects.lua"))).decode()
    actual = []
    for line in objects.splitlines():
        if 'type="ParkingStall"' not in line:
            continue
        assert 'name="junkyard"' in line and 'Direction="N"' in line
        match = re.search(r"x=(\d+), y=(\d+), z=0, width=(\d+), height=(\d+)", line)
        assert match, line
        actual.append(tuple(map(int, match.groups())))
    expected = [(z["x"] + project["worldX"] * 256, z["y"] + project["worldY"] * 256, z["width"], z["height"])
                for z in project["gameZones"] if z["type"] == "ParkingStall"]
    assert sorted(actual) == sorted(expected)
    assert sum(w * h // 15 for x, y, w, h in actual) == 283
    assert not [n for n in archive.namelist() if n.endswith(".lua") and
                (b"setPipedFuelAmount" in archive.read(n) or b"fuelStationGasInfinite" in archive.read(n))]
    compared = 0
    for name in archive.namelist():
        if not (name.endswith((".lotheader", ".lotpack", ".bin")) or "/maps/biomemap_" in name):
            continue
        relative = Path(name.split("/42/media/maps/", 1)[1].split("/", 1)[1])
        target = (installed_map / relative).resolve()
        assert target.is_relative_to(installed_map), "Unsafe archive-relative path"
        assert target.read_bytes() == archive.read(name), str(target)
        compared += 1
    assert compared > 0
result = {"status": "passed", "nativeZoneRows": len(actual), "nativeStalls": 283,
          "nativeDistribution": "junkyard", "zoneDirection": "N", "isolatedInstalledFilesCompared": compared,
          "fuelPowerOverrideLua": False, "closedStationContextRetained": proof["sourceLifecycleRetained"]}
(out / "archive-install-proof.json").write_text(json.dumps(result, indent=2))
print("PASS native zones and byte-identical isolated installed files:", result)
