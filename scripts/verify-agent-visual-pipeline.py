"""Verify the real HTTP + SDK MCP reference route against an isolated running host.

Python standard library only. Does not build, start the game, install mods, or
claim vision inference. The source PNG and all observations are synthetic/unknown.
"""
import argparse
import base64
import copy
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import struct
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
import zlib


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def digest(value):
    if not isinstance(value, bytes):
        value = json.dumps(value, sort_keys=True, separators=(",", ":")).encode()
    return hashlib.sha256(value).hexdigest()


def synthetic_png():
    """A real decodable RGB house icon; no photograph or geographical evidence."""
    width = height = 64
    rows = []
    for y in range(height):
        row = bytearray()
        for x in range(width):
            color = (195, 214, 224)
            if y >= 53:
                color = (76, 116, 67)
            if 15 <= x <= 49 and 29 <= y <= 52:
                color = (196, 166, 112)
            if 11 <= y <= 30 and abs(x - 32) <= y - 9:
                color = (121, 58, 52)
            if 28 <= x <= 36 and 39 <= y <= 52:
                color = (74, 57, 42)
            row.extend(color)
        rows.append(b"\0" + row)

    def chunk(kind, data):
        return (struct.pack(">I", len(data)) + kind + data
                + struct.pack(">I", zlib.crc32(kind + data) & 0xffffffff))

    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(b"".join(rows))) + chunk(b"IEND", b""))


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class Verification:
    def __init__(self, base, out):
        self.base, self.out = base.rstrip("/"), out
        parsed = urllib.parse.urlsplit(self.base)
        require(parsed.scheme == "http" and parsed.hostname == "127.0.0.1"
                and parsed.path in ("", "/") and not parsed.query and not parsed.fragment
                and parsed.username is None, "Use an explicit loopback http://127.0.0.1:port host")
        self.http = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
        self.token = None
        self.rpc_id = 0
        self.mcp_headers = {}
        self.checks = []
        self.transcript = []
        self.report = {"startedAt": datetime.now(timezone.utc).isoformat(), "baseUrl": self.base,
                       "status": "running", "checks": self.checks,
                       "limitations": ["Synthetic PNG; no vision model inference was performed.",
                                       "All six exterior observations are unknown; interiors unobserved.",
                                       "Access-only checks, no native lighting or B42 in-game verification.",
                                       "Custom sprites/tilesets are a separate verification scope."]}

    def save(self, name, value):
        text = json.dumps(value, indent=2, ensure_ascii=False)
        if self.token:
            text = text.replace(self.token, "<isolated-session>")
        (self.out / name).write_text(text + "\n", encoding="utf-8")

    def check(self, name, condition, **evidence):
        require(condition, name)
        self.checks.append({"name": name, "passed": True, **evidence})
        print("PASS " + name, flush=True)

    def request(self, path, body=None, expected=200, extra=None):
        headers = {"Origin": self.base, "Accept": "application/json"}
        data = None
        if body is not None:
            headers["Content-Type"] = "application/json"
            data = json.dumps(body).encode()
        headers.update(extra or {})
        for attempt in range(16):
            try:
                req = urllib.request.Request(self.base + path, data=data, headers=headers)
                with self.http.open(req, timeout=45) as response:
                    status, info, raw = response.status, dict(response.headers), response.read()
                break
            except urllib.error.HTTPError as error:
                status, info, raw = error.code, dict(error.headers), error.read()
                break
            except urllib.error.URLError as error:
                # Connection-refused is safe to retry even for a mutation: it never reached the host.
                refused = (isinstance(error.reason, ConnectionRefusedError)
                           or getattr(error.reason, "winerror", None) == 10061)
                if not refused or attempt == 15:
                    raise
                time.sleep(2)
        mime = next((v for k, v in info.items() if k.lower() == "content-type"), "")
        payload = json.loads(raw) if "application/json" in mime and raw else None
        entry = {"path": path, "method": "POST" if data is not None else "GET", "status": status,
                 "contentType": mime, "bytes": len(raw), "sha256": digest(raw), "origin": self.base}
        if status >= 400:
            entry["response"] = payload if payload is not None else raw[:1000].decode(errors="replace")
        self.transcript.append(entry)
        require(status == expected, f"{path}: expected HTTP {expected}, got {status}: "
                + (str(payload) if payload is not None else raw[:1000].decode(errors="replace")))
        return payload, raw, info

    def api(self, path, body=None, expected=200):
        return self.request(path, body, expected)[0]

    def rpc(self, method, params=None, notification=False):
        body = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            body["params"] = params
        if not notification:
            self.rpc_id += 1
            body["id"] = self.rpc_id
        payload, raw, headers = self.request("/mcp", body, 202 if notification else 200,
                                            {"Accept": "application/json, text/event-stream", **self.mcp_headers})
        if notification:
            return None
        if payload is None:
            messages = []
            for event in raw.decode().replace("\r\n", "\n").split("\n\n"):
                data = "\n".join(line[5:].lstrip() for line in event.splitlines() if line.startswith("data:"))
                if data:
                    messages.append(json.loads(data))
            matching = [m for m in messages if m.get("id") == body["id"]]
            require(len(matching) == 1, "MCP SSE response must contain the matching JSON-RPC result")
            payload = matching[0]
        require(payload.get("id") == body["id"] and "error" not in payload, "MCP JSON-RPC error: " + str(payload))
        for key, value in headers.items():
            if key.lower() == "mcp-session-id":
                self.mcp_headers["Mcp-Session-Id"] = value
        return payload["result"]

    def unchanged(self, label, snapshot):
        current = self.api("/api/agent/session/" + self.token)
        self.save(label + ".json", {"revision": current["revision"], "project": current["project"]})
        self.check(label, current["revision"] == snapshot["revision"]
                   and current["project"] == snapshot["project"], revision=current["revision"],
                   projectSha256=digest(current["project"]))

    def run(self):
        capabilities = self.api("/api/visual-references/capabilities")
        self.save("capabilities.json", capabilities)
        self.check("own upload available; Mapillary missing configuration explicit",
                   capabilities["own"]["available"] and not capabilities["mapillary"]["available"]
                   and "PZMAP_MAPILLARY_TOKEN" in capabilities["mapillary"]["message"])

        project = self.api("/api/generate", {"kind": "blank", "size": 256, "seed": 42,
                                            "name": "Synthetic visual pipeline verification"})
        # Arbitrary synthetic bounds exercise geographic selection, not real site attribution.
        project["bounds"] = {"south": -0.001, "west": -0.001, "north": 0.001, "east": 0.001}
        building = self.api("/api/building/design", {
            "building": {"id": uuid.uuid4().hex, "name": "Authored house fixture, not observed",
                         "kind": "house", "x": 100, "y": 100, "width": 12, "height": 10},
            "levels": 1, "seed": 42, "shape": "rect", "basements": 0, "style": ""})
        for floor in building["design"]["floors"]:
            floor["objects"] = []
        project["buildings"] = [building]
        session = self.api("/api/agent/session", project)
        self.token = session["token"]
        baseline = {"revision": session["revision"], "project": session["project"]}
        self.save("source-before.json", baseline)
        revision = session["revision"]
        target = {"scope": "map", "buildingId": building["id"]}
        route = "/api/visual-references/" + self.token
        binding = self.api(route + "/target", {"revision": revision, "target": target})
        self.save("target.json", binding)
        self.check("fixture building bound at current revision", binding["buildingId"] == building["id"]
                   and binding["baseRevision"] == revision)

        png = synthetic_png()
        (self.out / "synthetic-house.png").write_bytes(png)
        provenance = {"author": "verify-agent-visual-pipeline.py; synthetic procedurally drawn house icon",
                      "license": "CC0-1.0; synthetic test artwork, not a real photo or geographic evidence"}
        upload = {"revision": revision, "target": target, "provenance": provenance,
                  "base64": base64.b64encode(png).decode(), "mimeType": "image/png"}
        self.save("upload-request.json", upload)
        reference = self.api(route + "/own", upload)
        self.save("reference.json", reference)
        self.check("Origin + JSON own upload preserves synthetic provenance and PNG hash",
                   reference["sha256"] == digest(png) and reference["byteLength"] == len(png)
                   and reference["width"] == 64 and reference["height"] == 64
                   and reference["author"] == provenance["author"] and reference["license"] == provenance["license"]
                   and reference["capturedAt"] is None and reference["heading"] is None)
        items = self.api(route + "/list", {"revision": revision, "target": target})
        self.save("references-list.json", items)
        self.check("reference listed for exact target", len(items) == 1 and items[0]["reference"] == reference)
        _, served, image_headers = self.request(route + "/" + reference["id"] + "/image")
        (self.out / "http-served.png").write_bytes(served)
        self.check("HTTP image contains exact original PNG bytes", served == png)
        self.save("image-response-headers.json", image_headers)

        initialized = self.rpc("initialize", {"protocolVersion": "2025-11-25", "capabilities": {},
                                             "clientInfo": {"name": "visual-pipeline-verifier", "version": "1.0"}})
        self.save("mcp-initialize.json", initialized)
        self.mcp_headers["MCP-Protocol-Version"] = initialized["protocolVersion"]
        self.rpc("notifications/initialized", notification=True)
        tools, cursor = [], None
        while True:
            page = self.rpc("tools/list", {"cursor": cursor} if cursor else {})
            tools.extend(page["tools"])
            cursor = page.get("nextCursor")
            if not cursor:
                break
        self.save("mcp-tools-list.json", {"tools": tools})
        names = {tool["name"] for tool in tools}
        self.check("connected /mcp tools/list exposes reference/design/tileset tools",
                   {"visual_reference_view", "visual_reference_observe", "visual_reference_propose",
                    "design_task_check", "design_task_commit", "tileset_frame_preview"} <= names,
                   toolCount=len(tools), protocol=initialized["protocolVersion"])
        viewed = self.rpc("tools/call", {"name": "visual_reference_view",
                                        "arguments": {"sessionToken": self.token, "referenceId": reference["id"]}})
        self.save("mcp-view.json", viewed)
        require(not viewed.get("isError"), "visual_reference_view returned a tool error")
        images = [item for item in viewed["content"] if item["type"] == "image"]
        texts = [json.loads(item["text"]) for item in viewed["content"] if item["type"] == "text"]
        receipt = next(item for item in texts if "receiptId" in item)
        decoded = base64.b64decode(images[0]["data"], validate=True) if len(images) == 1 else b""
        (self.out / "mcp-returned.png").write_bytes(decoded)
        self.check("SDK MCP image block has actual PNG bytes plus exact source receipt",
                   len(images) == 1 and images[0]["mimeType"] == "image/png" and decoded == png
                   and receipt["source"] == reference and len(receipt["receiptId"]) == 32,
                   imageSha256=digest(decoded), receiptId=receipt["receiptId"])

        unknown = {"value": None, "confidence": None, "visibility": "unknown",
                   "uncertainty": "Synthetic transport fixture; no real-world feature inferred."}
        observations = {name: dict(unknown) for name in
                        ("floors", "wallMaterial", "roof", "doors", "fences", "landscaping")}
        observations.update({"hiddenFacades": [], "uncertainty":
                             "Not a photograph. No vision inference performed. Interiors and facades unobserved."})
        observation_request = {"revision": revision, "receiptId": receipt["receiptId"], "observations": observations}
        invalid = dict(observation_request, receiptId="0" * 32)
        rejected = self.api(route + "/" + reference["id"] + "/observations", invalid, 400)
        self.save("invalid-receipt-rejection.json", rejected)
        evidence = self.api(route + "/" + reference["id"] + "/observations", observation_request)
        self.save("observations.json", evidence)
        self.check("actual MCP receipt accepted; unknowns remain null and interiors unobserved",
                   evidence["receiptId"] == receipt["receiptId"] and evidence["source"] == reference
                   and evidence["observations"]["interiors"] == "unobserved"
                   and all(evidence["observations"][name] == unknown for name in
                           ("floors", "wallMaterial", "roof", "doors", "fences", "landscaping")))
        recorded = self.api(route + "/" + reference["id"] + "/observations")
        self.check("immutable observations readable over HTTP", recorded == [evidence])

        for provider in ("google", "yandex"):
            rejected = self.api(route + "/mapillary/import", {"revision": revision, "target": target,
                                "sourceId": "123", "provider": provider}, 400)
            self.check(provider + " image import rejected; navigation only",
                       "navigation" in rejected["error"].lower())
        geo = {"scope": "map", "longitude": 0, "latitude": 0}
        for operation in ("search", "import"):
            payload = {"revision": revision, "target": geo, "provider": "mapillary"}
            payload.update({"radiusMetres": 150} if operation == "search" else {"sourceId": "123"})
            unavailable = self.api(route + "/mapillary/" + operation, payload, 503)
            self.save("mapillary-" + operation + "-503.json", unavailable)
            self.check("Mapillary " + operation + " returns 503 for missing server token",
                       "PZMAP_MAPILLARY_TOKEN" in unavailable["error"])

        constraints = {"x": 96, "y": 96, "width": 32, "height": 32, "requireLighting": False}
        original = session["project"]["buildings"][0]
        changed = copy.deepcopy(original)
        changed["design"]["material"] = "wood" if original["design"]["material"] == "brick" else "brick"
        summaries = []
        for name, draft in (("unchanged", original), ("exterior-changed", changed)):
            payload = {"revision": revision, "observationId": evidence["id"], "draft": draft,
                       "constraints": constraints, "interiorMode": "preserve"}
            self.save(name + "-proposal-request.json", payload)
            summary = self.api(route + "/propose", payload)
            self.save(name + "-proposal.json", summary)
            taskpath = "/api/agent/design/" + self.token + "/" + summary["id"]
            task = self.api(taskpath)
            self.save(name + "-task-before-check.json", task)
            staged = task["candidate"]["buildings"][0]
            self.check(name + " isolated draft retains evidence, source facts and interior",
                       staged == draft and evidence["id"] in task["objective"]
                       and "interiors unobserved" in task["objective"] and not task["passed"]
                       and task["baseRevision"] == revision)
            rejected = self.api(taskpath + "/commit", {"taskRevision": task["taskRevision"]}, 400)
            self.save(name + "-unchecked-commit-rejection.json", rejected)
            checked = self.api(taskpath + "/check", {"taskRevision": task["taskRevision"]})
            self.save(name + "-check.json", checked)
            self.check(name + " access-only check passed with complete coverage",
                       checked["passed"] and checked["iterations"][-1]["access"]["complete"]
                       and checked["iterations"][-1]["lightingStatus"] == "not-requested")
            stale = self.api(taskpath + "/commit", {"taskRevision": task["taskRevision"]}, 409)
            self.save(name + "-stale-task-rejection.json", stale)
            self.check(name + " stale task revision blocked", stale["revision"] == checked["taskRevision"])
            task = self.api(taskpath)
            self.save(name + "-task-checked.json", task)
            summaries.append((taskpath, checked))
            self.unchanged("source-after-" + name, baseline)

        edited = copy.deepcopy(session["project"])
        edited["name"] += " (isolated concurrent edit)"
        published = self.api("/api/agent/session/" + self.token + "/publish", {"revision": revision, "project": edited})
        concurrent = self.api("/api/agent/session/" + self.token)
        self.save("source-concurrent-edit.json", {"revision": concurrent["revision"], "project": concurrent["project"]})
        self.check("only deliberate isolated name edit advances source revision",
                   published["revision"] == revision + 1 and concurrent["project"] == edited)
        for name, (taskpath, checked) in zip(("unchanged", "exterior-changed"), summaries):
            stale = self.api(taskpath + "/commit", {"taskRevision": checked["taskRevision"]}, 409)
            self.save(name + "-stale-source-commit-rejection.json", stale)
            self.check(name + " stale source revision blocks checked commit",
                       stale["revision"] == concurrent["revision"])
        self.api(route + "/propose", {"revision": revision, "observationId": evidence["id"], "draft": changed,
                                      "constraints": constraints, "interiorMode": "preserve"}, 409)
        self.unchanged("source-after-rejected-stale-writes", concurrent)
        self.report.update({"status": "passed", "referenceId": reference["id"],
                            "sourceProjectSha256": digest(baseline["project"]),
                            "syntheticPngSha256": digest(png), "toolCount": len(tools),
                            "exteriorChange": {"field": "design.material", "before": original["design"]["material"],
                                               "after": changed["design"]["material"],
                                               "basis": "Explicit authored transport-test edit; not an observed feature."}})

    def finish(self):
        if self.token:
            try:
                closed = self.api("/api/agent/session/" + self.token + "/close", {})
                require(closed.get("closed") is True, "Session close not acknowledged")
                self.report["sessionClosed"] = True
            except Exception as error:
                self.report.update({"sessionClosed": False, "cleanupError": str(error), "status": "failed"})
        self.report["finishedAt"] = datetime.now(timezone.utc).isoformat()
        self.report["checkCount"] = len(self.checks)
        self.save("http-transcript.json", self.transcript)
        self.save("report.json", self.report)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", default=os.environ.get("PZMAP_TEST_URL", "http://127.0.0.1:48778"))
    parser.add_argument("--out", type=Path, help="New results directory (must not already exist)")
    args = parser.parse_args()
    out = args.out or Path("work/agent-visual-pipeline") / (
        datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8])
    out.mkdir(parents=True, exist_ok=False)
    verification = Verification(args.base, out)
    try:
        verification.run()
    except Exception as error:
        verification.report.update({"status": "failed", "error": str(error)})
        print("FAIL " + str(error).replace(verification.token or "<no-token>", "<isolated-session>"), flush=True)
    finally:
        verification.finish()
    print(str((out / "report.json").resolve()), flush=True)
    return 0 if verification.report["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
