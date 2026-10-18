# CI scene

A small synthetic scene for the command line: 512×256 m at 1 m per tile, built as 256-tile sections (two of them hold content). It has a secondary road with a residential side road (a stop sign), houses on both sections, one house across the section seam (a shared world building), a fence across the seam (a shared world fence), a parking area and farmland.

```bash
simplepzmap build examples/CiScene/ci-scene.json --out work/ci/ci.pzworld.json --export work/ci/ci.zip --strict
```

The geometry is invented for testing and describes no real place.
