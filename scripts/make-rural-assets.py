# Original procedural artwork; no game pixels copied. Emitted artwork: CC0-1.0.
import argparse, base64, hashlib, json, os, pathlib, struct, urllib.request, zlib

p = argparse.ArgumentParser()
p.add_argument("out", type=pathlib.Path)
p.add_argument("--file-number", type=int, required=True)
p.add_argument("--api", help="Optional existing editor, e.g. http://127.0.0.1:48777")
p.add_argument("--revision", type=int)
a = p.parse_args()
if not 100 <= a.file_number <= 8190:
    p.error("file-number must be 100..8190 and collision-checked")
a.out.mkdir(parents=True, exist_ok=True)
W, H = 128, 256
frames, bindings = [], {}
tileset, owner = "rural_original_01", "rural_original"

def png(pixels):
    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data))
    raw = b"".join(b"\0" + pixels[y*W*4:(y+1)*W*4] for y in range(H))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", W,H,8,6,0,0,0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))

def polygon(pix, points, color):
    for y in range(max(0,int(min(v[1] for v in points))), min(H,int(max(v[1] for v in points))+1)):
        for x in range(max(0,int(min(v[0] for v in points))), min(W,int(max(v[0] for v in points))+1)):
            inside = False
            for i, (xx,yy) in enumerate(points):
                bx,by = points[i-1]
                if (yy > y+.5) != (by > y+.5) and x+.5 < (bx-xx)*(y+.5-yy)/(by-yy)+xx:
                    inside = not inside
            if inside:
                pix[(y*W+x)*4:(y*W+x+1)*4] = bytes(color)

def artwork(kind, direction, color, panel=0):
    pix = bytearray(W*H*4)
    def point(u,v):
        return (64 + (1 if direction == "n" else -1)*u*63, 192-u*32-v*110)
    def quad(u0,v0,u1,v1,c):
        polygon(pix,[point(u0,v0),point(u1,v0),point(u1,v1),point(u0,v1)],c)
    dark = tuple(max(0,c-34) for c in color[:3])+(255,)
    if kind in ("fence", "wall", "window", "door", "gate_closed"):
        quad(0,0,1,1,color)
        for i in range(16):
            quad(i/16,0,i/16+.018,1,dark)
        quad(0,.03,1,.06,dark); quad(0,.91,1,.94,dark)
        if kind in ("window", "door"):
            low = .35 if kind == "window" else 0
            quad(.25,low,.75,.81,(208,195,153,255))
            quad(.30,low+.025,.70,.77,(0,0,0,0))
        if kind == "gate_closed":
            # These rails meet across three panels as one authored sliding leaf.
            quad(0,.20,1,.25,dark); quad(0,.72,1,.77,dark)
            if panel == 2: quad(.80,.42,.84,.60,(190,187,164,255))
    elif kind == "gate_open":
        # Retracted leaf behind its end post; no door/collision properties.
        quad(.02,.02,.12,.95,dark)
    elif kind == "corner":
        quad(0,0,.10,1,dark)
    elif kind in ("trim", "shutters", "awning"):
        if kind == "trim":
            for box in ((.20,.30,.27,.86),(.73,.30,.80,.86),(.20,.80,.80,.87),(.20,.30,.80,.35)):
                quad(*box,(224,214,178,255))
        elif kind == "shutters":
            for u in (.06,.80):
                quad(u,.34,u+.14,.79,color)
                for v in range(4): quad(u,.36+v*.1,u+.14,.38+v*.1,dark)
        else:
            polygon(pix,[point(.10,.82),point(.90,.82),point(.92,1.02),point(.08,1.02)],color)
    elif kind == "chimney":
        quad(.34,.1,.65,.92,(143,78,63,255)); quad(.28,.90,.71,1,(63,58,56,255))
        for v in range(7): quad(.34,.17+v*.1,.65,.185+v*.1,(78,68,61,255))
    else:  # antenna mast and elements
        quad(.48,.02,.50,1,(125,133,138,255))
        for v in (.40,.65,.90): quad(.17,v,.83,v+.018,(155,161,163,255))
    return png(pix)

def add(key, kind, direction, color, props, panel=0):
    index = len(frames)
    data = artwork(kind,direction,color,panel)
    (a.out / (key+".png")).write_bytes(data)
    props = dict(props, Facing=direction.upper())
    frames.append(dict(key=key,direction=direction,width=W,height=H,properties=props))
    return tileset+"_"+str(index)

colors = {"green":(54,93,67,255), "maroon":(114,49,58,255)}
for name,c in colors.items():
    for d in ("w","n"):
        add("corrugated_"+name+"_"+d,"fence",d,c,
            {"CustomName":"rural-corrugated-"+name,"collide"+d.upper():"","cut"+d.upper():""})
    ids = []
    for kind in ("wall","corner","window","door"):
        for d in ("w","n"):
            props = {"wall":"", "Wall"+d.upper():"", "CustomName":"rural-boards-"+name}
            if kind == "window": props["Window"+d.upper()] = ""
            if kind == "door": props["DoorWall"+d.upper()] = ""
            ids.append(add("boards_"+name+"_"+kind+"_"+d,kind,d,c,props))
    bindings["boards_"+name] = dict(zip(("West","North","NorthWest","SouthEast","WestWindow","NorthWindow","WestDoor","NorthDoor"),ids))

for role in ("trim","shutters","chimney","awning","antenna"):
    for d in ("w","n"):
        props = {"CustomName":"rural-"+role}
        if role in ("trim","shutters","awning"):
            props.update({"WallOverlay":"", "attached"+d.upper():""})
        add(role+"_"+d,role,d,colors["green"],props)

gate = {"Id":"rural-original-three-panel", "Width":3}
for state in ("closed","open"):
    for d in ("w","n"):
        ids = []
        for i in range(3):
            props = {"CustomName":"rural-three-panel-"+state}
            if state == "closed": props.update({"collide"+d.upper():"", "cut"+d.upper():""})
            ids.append(add("gate_"+state+"_"+d+"_"+str(i),"gate_"+state,d,colors["green"],props,i))
        gate[state.capitalize()+d.upper()] = ids
bindings["editorVehicleGate"] = gate
bindings["vehicleGateRuntimeVerified"] = False
bindings["modId"] = owner
spec = dict(modId=owner,name="Original rural procedural fixtures",tileset=tileset,fileNumber=a.file_number,
    provenance=dict(kind="generated",source="RURAL-PROFILES.md procedural rasterizer",
        reference="Original geometry and colors; no game image pixels copied",license="CC0-1.0",
        generator="Python standard library original procedural rasterizer",
        prompt="Original isometric rural corrugated green/maroon panels, boards, trim, shutters, chimney, awning, antenna and three-panel editor gate"),frames=frames)
(a.out/"spec.json").write_text(json.dumps(spec,indent=2),encoding="utf-8")
(a.out/"bindings.json").write_text(json.dumps(bindings,indent=2),encoding="utf-8")
manifest = {f["key"]: hashlib.sha256((a.out/(f["key"]+".png")).read_bytes()).hexdigest() for f in frames}
(a.out/"sha256.json").write_text(json.dumps(manifest,indent=2),encoding="utf-8")
print(str(len(frames))+" original PNG frames and owned native spec written to "+str(a.out))

if a.api:
    from urllib.parse import urlparse
    uri = urlparse(a.api)
    if uri.scheme not in ("http","https") or uri.hostname not in ("127.0.0.1","localhost","::1"):
        p.error("api must be your loopback editor")
    token = os.environ["PZMAP_SESSION_TOKEN"]
    if a.revision is None: p.error("provide current --revision for your existing agent session")
    def request(path, body=None):
        data = None if body is None else json.dumps(body).encode()
        req = urllib.request.Request(a.api.rstrip("/")+path,data=data,headers={"Content-Type":"application/json"})
        with urllib.request.urlopen(req,timeout=120) as r: return r.read()
    draft = json.loads(request("/api/agent/tilesets",dict(sessionToken=token,expectedRevision=a.revision,spec=spec)))
    base = "/api/agent/tilesets/"+token+"/"+draft["id"]
    for i,f in enumerate(frames):
        draft = json.loads(request(base+"/frames/"+str(i),dict(expectedDraftRevision=draft["revision"],
            pngBase64=base64.b64encode((a.out/(f["key"]+".png")).read_bytes()).decode())))
    draft = json.loads(request(base+"/build",dict(expectedDraftRevision=draft["revision"])))
    (a.out/(owner+".zip")).write_bytes(request(base+"/mod.zip"))
    for i,f in enumerate(frames):
        (a.out/("native_"+f["key"]+".png")).write_bytes(request(base+"/frames/"+str(i)+".png"))
    (a.out/"draft.json").write_text(json.dumps(draft,indent=2),encoding="utf-8")
    print("Built separate native mod ZIP and native pack previews. Inspect before attach; token was not printed.")
