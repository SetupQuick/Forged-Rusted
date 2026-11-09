# Performance, graphics settings and the day/night cycle

How heavy each part of the world is, what the graphics options change,
how to measure on your own PC, and what is planned next.

## Measuring on your PC (in-game benchmark)

Main menu -> **Benchmark** (or start the game with `-benchmark`, add
`-quit` to close it afterwards). It flies a camera along the same path
over the offline world once per preset (Low, Medium, High, Ultra), 20 s
each at 9 m/s - so new chunks keep streaming in, like real play - and
reports per preset:

* average FPS, **1% low** FPS (how bad the stutters are), frame time
  average / 99th percentile / worst,
* CPU main-thread ms and GPU ms (GPU needs *Player Settings > Other >
  Frame Timing Stats* enabled, otherwise "n/a"),
* triangles and draw calls (editor / development builds), memory,
  how long the area took to load.

The time of day is fixed at noon so runs compare. Your own settings are
restored afterwards. Results are shown on screen and saved as CSV in
`Application.persistentDataPath` (Windows:
`%USERPROFILE%\AppData\LocalLow\<company>\<product>\benchmark-*.csv`).
Run it once per PC in the team and compare the files.

Rule of thumb: pick the highest preset whose **1% low** stays above your
monitor's refresh rate (or 60).

## Graphics presets

| | Low | Medium | High (default) | Ultra |
|---|---|---|---|---|
| Render distance | 5 chunks (80 m) | 6 (96 m) | 8 (128 m) | 12 (192 m) |
| Shadows | off | 35 m, 1 cascade, 1024, hard | 60 m, 2 cascades, 2048, soft | 100 m, 4 cascades, 4096, soft |
| Grass & flowers | 1 chunk | 2 chunks | 3 chunks | 5 chunks |
| Clouds / wind | off / off | on / on | on / on | on / on |
| Post-processing | off | on | on | on |
| Render scale | 80 % | 100 % | 100 % | 100 % |
| Anti-aliasing | FXAA | FXAA | SMAA | SMAA |

Every option can also be changed on its own (the preset then shows
"Custom"). Settings are in *Settings > Graphics* and *Settings > Display*.

## What things cost

Numbers below are measured with the real generator and mesher code
(`World/Generation`, `ChunkMeshBuilder`) on one CPU core, plus triangle
counts computed from those meshes. GPU time depends on your card - use
the benchmark for that.

### Per chunk (32 x 320 x 32 voxels = 16 x 160 x 16 m)

| | before | after |
|---|---|---|
| Generate (worker thread) | 4.1 ms | 4.3 ms |
| Mesh with grass (worker thread) | 6.4 ms | 5.8 ms |
| Mesh without grass (far chunks) | - | 5.1 ms |
| Garbage created per mesh (causes GC stutter) | **2 973 KB** | **87 KB** |
| Triangles: blocks / grass / water | 8 750 / 6 624 / 123 | same |

Garbage is down 97 % because mesh buffers are now pooled and reused
instead of allocated for every chunk. Grass and flowers are ~43 % of all
triangles but are almost invisible further away, so only chunks near the
camera get them now.

### Triangles drawn per frame (everything loaded, before frustum culling)

| Setup | Main pass | Extra depth pass | Shadow pass | **Total** |
|---|---|---|---|---|
| Before (old defaults) | 3.49 M | 3.46 M (SSAO) | 4.50 M | **11.4 M** |
| Low | 0.92 M | - | - | **0.9 M** |
| Medium | 1.38 M | - | 0.22 M | **1.6 M** |
| High | 2.32 M | - | 0.82 M | **3.1 M** |
| Ultra | 5.14 M | - | 1.92 M | **7.1 M** |

The default (High) draws about **3.6x fewer triangles** than before at
the same 128 m view distance. Where that came from:

1. **SSAO was on** in the URP renderer (`PC_Renderer`). It forces a
   second depth+normals pass over the whole world every frame - and the
   voxel shader never used its result, because ambient occlusion is
   already baked into the vertices. Turned off.
2. **Opaque texture and depth texture copies** were on in `PC_RPAsset`
   (a full-screen copy each frame). Nothing reads them. Turned off.
3. **Grass cast shadows** - thousands of tiny boxes in the shadow map,
   barely visible. Off.
4. **Shadow distance 120 m with 4 cascades** -> 60 m with 2 cascades on
   High. Each cascade draws the terrain again into the shadow map.
5. **Grass only near the camera** (3 chunks on High).

### Memory

| Render distance | GPU mesh memory before | after (grass 3) |
|---|---|---|
| 6 | 113 MB | 120 MB |
| 8 | 186 MB | 174 MB |
| 12 | 405 MB | 335 MB |
| 16 | 713 MB | 561 MB |

Each vertex now carries 8 more bytes (the wind weight), which is why
small render distances use slightly more; from 8 chunks on, dropping far
grass saves more than that. Chunk block data in RAM is 320 KB per chunk
(138 MB at render distance 8) - see "next steps".

## How heavy are wind and shadows?

**Wind** is almost free. It is a few sine waves in the vertex shader,
only for vertices that have a wind weight (leaves, grass tips, the open
water surface - everything else skips it with a branch). The same offset
is applied in the shadow and depth passes so shadows move with the
leaves. Cost: well under 0.1 ms on a mid-range GPU. Its only real cost
is the extra 8 bytes per vertex (see memory). The toggle exists for very
old GPUs and for people who dislike motion.

**Shadows** are the most expensive graphics option. The cost is the
terrain drawn again into the shadow map once per cascade, plus the
shadow map size (1024 = 2 MB, 2048 = 8 MB, 4096 = 32 MB of video memory)
and soft-shadow filtering per pixel. If the game is slow, turn shadows
down first, then render distance, then render scale. At night the
moon's shadows are skipped entirely when the light is too faint to see
them.

Other per-frame costs: clouds are one draw call of ~5-10 k triangles,
rebuilt only when the camera crosses a 12 m cell; the sky is one
full-screen pass with no textures; day/night updates a few shader
globals per frame.

## Day and night

* `WorldClock` (static): world time, day length (default 20 minutes,
  `WorldSettings > Day and night`), start time (0.3 = morning).
* The server sends the clock to clients every 5 s (`TimeSync`, protocol
  version 10); small differences are smoothed, big ones snap. Everyone
  sees the same sun, moon, stars and clouds.
* `VoxelAtmosphere` turns that into: one directional light that is the
  sun by day and the moon by night (never two shadow maps), sky/ground
  ambient, the procedural sky (`ForgedRusted/Voxel Sky`: gradient,
  square sun and moon, stars), fog colour = horizon colour, and wind.
  The light turns in 0.2 degree steps so shadow edges do not shimmer.
* `WorldClock.SetTimeOfDay(0.5f)` jumps to noon (for a future `/time`
  command or debugging).

## Underwater

* Fog colour and distance depend on depth below the surface: blue-green
  and ~13 m visibility near the top, nearly black and ~7 m deep down,
  darker at night.
* Light that reaches surfaces under water is absorbed per colour (red
  first), so the lake floor is teal instead of bright sand, with moving
  caustics.
* Looking up you see the surface from inside: a bright window straight
  up and a dark ceiling at shallow angles. The sky shader shows the
  water colour, so the sky can never be seen from inside a lake (that
  was the "swimming in the sky" look).

## Next steps (not done yet)

* **World saving**: there is none yet. Plan: save only player edits (the
  edit overlay already holds them) per region file, written on a worker
  thread every few minutes and on quit - never the generated terrain,
  which is recreated from the seed. Keeps saves tiny and fast.
* **Chunk data compression**: 320 KB per chunk in RAM is mostly air and
  stone. Storing each 32-high section as "all one block" or as a
  palette with 4-8 bit indices would cut RAM by ~80 %.
* **Vertex compression**: positions as 16-bit, normals as one byte
  index, colours already 8-bit -> ~16 bytes per vertex instead of 36.
  Roughly halves GPU mesh memory and vertex bandwidth.
* **Greedy meshing for distant chunks / LOD**: merge equal faces, or
  build far chunks at half resolution (1 m voxels) - 3-4x fewer
  triangles beyond ~100 m.
* **Occlusion**: skip chunks fully hidden behind mountains / underground
  caves (cheap flood-fill visibility per chunk section).
* **Upscaling**: URP's FSR 1 / STP upscaler with render scale 70-80 %
  gives most of the speed of a lower resolution with less blur.
