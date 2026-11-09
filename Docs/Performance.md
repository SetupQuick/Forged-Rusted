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
| Distant terrain simplified beyond | 48 m | 64 m | 80 m | 96 m |
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
| Low | 0.59 M | - | - | **0.6 M** |
| Medium | 1.00 M | - | 0.22 M | **1.2 M** |
| High | 1.61 M | - | 0.82 M | **2.4 M** |
| Ultra (192 m) | 2.95 M | - | 1.92 M | **4.9 M** |
| Ultra at 256 m view | 3.70 M | - | 1.92 M | **5.6 M** |

The default (High) draws about **4.7x fewer triangles** than before at
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
6. **Simplified distant chunks** (see below): High 3.1 M -> 2.4 M,
   Ultra 7.1 M -> 4.9 M, and a 256 m view now costs less than 128 m
   did before.

### Distant chunks (level of detail)

Chunks further away than the "Distant terrain" setting are built by
`ChunkLod`:

* **Half resolution**: 2x2x2 voxels become one 1 m block (4x fewer
  faces). A cell is solid if any of its voxels is, and takes the block
  of its highest voxel, so grass stays on top and thin tree trunks do
  not disappear.
* **Hidden caves filled**: air that cannot be reached from the sky
  inside the chunk becomes stone, removing cave walls nobody can see
  from far away.
* **No grass, no collider** (collider cooking was a big part of the
  loading cost). Chunks within 2 of *any* player stay full detail with a
  collider, also on a listen server where other players are far from
  the host's camera; if a player reaches a simplified chunk it is
  rebuilt immediately before physics needs it.
* Switching has one chunk of hysteresis, so walking back and forth over
  the boundary does not rebuild the same chunks.

| per chunk | full detail | simplified |
|---|---|---|
| Triangles (no grass) | 8 874 | **2 027** (2 343 without cave fill) |
| Build time | 5.2 ms | **3.6 ms** |
| Garbage | 87 KB | ~0 KB |

GPU mesh memory with grass 3 and simplified beyond 6 chunks:
148 MB at view 8 (was 186 MB originally), 184 MB at 12 (was 405 MB),
236 MB at 16 (was 713 MB).

Known limitation: where full and simplified chunks meet there can be a
thin 0.5 m step (simplified ground is rounded *up* to 1 m). Rounding
down would open see-through gaps instead, so this is the safer choice;
it sits inside the fog and is mostly visible through clear water.

### Memory

| Render distance | GPU mesh memory before | after (grass 3) |
|---|---|---|
| 6 | 113 MB | 120 MB |
| 8 | 186 MB | 174 MB |
| 12 | 405 MB | 335 MB |
| 16 | 713 MB | 561 MB |

Each vertex now carries 8 more bytes (the wind weight), which is why
small render distances use slightly more; from 8 chunks on, dropping far
grass saves more than that. Chunk block data in RAM is 110 KB per chunk
(47 MB at render distance 8, was 138 MB) - see "Multiplayer".

## Multiplayer and many players

Unlike Minecraft, the server never sends terrain: every machine
generates the same world from the seed, and only player edits, positions
and inventories travel over the network. So more players cost the
server CPU and RAM for the areas they stand in, not bandwidth for chunks.

### What the server does per player

* Keeps block data for 9 x 9 chunks around each player (shared when
  players are close together) and colliders for the 21 nearest.
* Builds **collision-only meshes**: just the surface between solid and
  non-solid blocks, with neighbouring faces merged into big rectangles.
  No colours, normals or visual mesh at all. 2 041 triangles instead of
  8 750 per chunk, built in 1.9 ms (the visual mesh took ~5 ms), and far
  cheaper to cook and collide against.

| | before | now |
|---|---|---|
| Block data per chunk | 320 KB | **110 KB** |
| Server RAM per player (exploring alone) | 25 MB | **8.7 MB** |
| Server CPU per new chunk | ~5 ms mesh + cooking 8 750 triangles | **1.9 ms + cooking 2 041 triangles** |
| Joining / teleporting | | ~0.4 s CPU once, spread over all cores |
| Sprinting into new land | | ~2 % of one core per player |

| Players exploring apart | Server RAM for terrain | Server CPU (one core = 100 %) |
|---|---|---|
| 4 | 35 MB (was 101) | 7 % |
| 10 | 87 MB (was 253) | 19 % |
| 20 | 174 MB (was 506) | 37 % |
| 50 | 435 MB (was 1 266) | 93 % (spread over cores) |

Players in the same area share chunks, so real numbers are lower.

### What changed for this

* **Chunk storage in 16-block sections**: a section that is one block
  type (all sky above the terrain, all stone deep down - two thirds of
  every chunk) is stored as a single value. 320 -> 110 KB per chunk, on
  clients too (view 8: 138 -> 47 MB). Reading a block costs the same.
* **Merged collision meshes** (above), on clients too.
* **Clients only cook colliders near players**: within 3 chunks of the
  camera or 2 of any player; further chunks are just drawn (colliders
  are dropped when you walk away, rebuilt before you arrive).
* **Hosting**: the host also simulates the areas around the other
  players. Where those are outside the host's own view they now get only
  collision meshes - nothing is built or drawn for them.

### Bandwidth

Each player is only told about objects within 210 m of them (they stay
until 240 m, so nothing flickers at the edge). Changed positions are sent
every tick within 48 m, every 2nd tick to 112 m and every 4th tick
beyond; when more updates are due than fit, the longest-waiting go first,
and crowds get up to two packets per tick so nearby players still update
every tick.

So traffic depends on how many players are *near* you, not on how many
are on the server. Measured: 50 moving players within 210 m of one
player = about 44 KB/s to that player; players spread across the world
cost each other nothing. Before, every player received every other
player every tick (server upload grew with players squared: ~23 Mbit/s
at 50 players).

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

* **World saving**: done - see [Saving.md](Saving.md).
* **Palette compression** of mixed sections (4-bit indices when a
  section has at most 16 block types): 110 -> ~48 KB per chunk. Costs a
  little speed on every block read, so only worth it for big servers.
* **Vertex compression**: positions as 16-bit, normals as one byte
  index, colours already 8-bit -> ~16 bytes per vertex instead of 36.
  Roughly halves GPU mesh memory and vertex bandwidth.
* **Greedy meshing**: merging equal neighbouring faces. Limited gain
  for us because every voxel has its own colour variation; worth it
  for a second, even coarser level (2 m) at very long view distances.
* **Occlusion**: skip chunks fully hidden behind mountains / underground
  caves (cheap flood-fill visibility per chunk section).
* **Upscaling**: URP's FSR 1 / STP upscaler with render scale 70-80 %
  gives most of the speed of a lower resolution with less blur.
