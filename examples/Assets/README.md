# Game-resource example (0.4.0)

Open `resources.pzmap.json` with **Open**. Six appearance styles, two above-ground levels and one basement per building. The first building has an explicit floor and wall choice; outside are a complete bed and road surface. The forest uses different game tree resources.

`Resources-B42.zip` is a ready Build 42 mod. Follow its `INSTALL.txt`; enable separately from Pilna/basements because examples overlap native origin 100,100.

Export references 122 tile IDs without bundling textures/game classes. The installed game verified their presence and loaded 84 rooms/1,024 chunks. Character traversal and lighting remain untested.
