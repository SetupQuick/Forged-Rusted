# World generation

How the voxel world is generated, streamed and synced. Code lives in
`Assets/Scripts/World/`.

## Overview

* Small voxels: 0.5 m each by default (`voxelSize`), so a 2 m player is
  4 voxels tall. Terrain sizes in the settings are in metres, so the
  world keeps its proportions if the voxel size changes.
* Infinite in X/Z, 320 voxels (160 m) tall, sea level at voxel 80 (all set
  in `Assets/Scripts/Settings/DefaultWorldSettings.asset`).
* Chunks are full-height columns of 32 x 320 x 32 voxels (16 m wide).
* Generation is **deterministic**: the same seed gives exactly the same
  world on every machine (Windows client, Linux dedicated server). It uses
  its own seeded noise (pure integer hashing + double math), not
  `Mathf.PerlinNoise`.
* Generation and meshing run on **worker threads**. Only uploading meshes
  and colliders happens on the main thread, limited per frame.

## Seed (server authoritative)

1. The server's `WorldManager` picks the seed from `WorldSettings`
   (`seed`, or a random one when `useRandomSeed` is on).
2. It calls `NetworkRuntime.SetServerWorldInfo(...)`.
3. When a client connects, `ServerWorldInfoModule` sends a `WorldInfo`
   message (reliable, ordered, before any object spawns).
4. The client's `WorldManager` waits for it and only then starts
   generating. It also checks the generator version and a fingerprint of
   the world settings, and logs an error if its build would generate a
   different world than the server's.

Without a `NetworkRuntime` in the scene the world runs offline with the
settings seed (handy for testing terrain).

Protocol version was bumped to 6 for the new `WorldInfo` (13) message.

## Generation passes (`Generation/NoiseWorldGenerator.cs`)

| Pass | File | What it does |
|------|------|--------------|
| Terrain | `TerrainSampler.cs`, `SurfaceRules.cs` | Height from layered noise maps: continentalness (ocean / coast / inland), erosion, ridged mountain ranges, hills, detail, and rivers carved down to sea level. Biome from temperature + humidity. Surface layers per biome (grass/dirt, sand/sandstone, snow, rock on cliffs). Water up to sea level, ice when cold. |
| Caves | `CaveCarver.cs` | Caverns (3D noise blobs) and winding tunnels (two 3D noises near zero). Noise is sampled on a 4-block grid and interpolated, so it is cheap. Tunnels open to the surface only in a few places (cave entrances); nothing is carved under water. |
| Ores | `OreGenerator.cs` | Coal, copper, iron, gold, crystal veins in stone, rarer deeper down. |
| Structures | `Structures/` | Round lumpy trees (some in autumn orange/red), tiered pines, flat savanna trees, bushes, cacti, mossy boulders; then grass tufts and flower patches. |

Biomes: ocean, frozen ocean, beach, plains, forest, desert, savanna, taiga,
tundra, mountains, snowy peaks, swamp (`Generation/Biomes.cs`).

Blocks and their placeholder colours: `Blocks/BlockRegistry.cs`. Never
renumber `BlockId` / `BiomeId` values, only append - they will be saved
and sent over the network.

## Adding structures (dungeons, ruins, ...)

Implement `IStructureGenerator` and pass it to the `NoiseWorldGenerator`
constructor (`extraStructures`). Rules:

* Every chunk asks every generator to place all structures that could
  reach into it, including ones rooted in neighbouring chunks, and keeps
  only the blocks inside itself. So **placement must be a pure function of
  world position + seed** (hash a grid cell, like
  `SurfaceFeatureGenerator` does) and must not depend on other chunks'
  block data.
* Set `MaxHorizontalReach` to how far a structure can extend from its
  origin. Large structures (big dungeons) need a bigger reach or a
  different approach (e.g. pre-planning the layout from the seed and
  each chunk stamping its part).
* Keep the generator immutable - it runs on several threads at once.
* Bump `TerrainConfig.GeneratorVersion` whenever a change makes the same
  seed generate a different world.

## Streaming (`Streaming/ChunkStreamer.cs`)

* Clients stream around the local player (`renderDistance`, default 8
  chunks).
* Servers keep `serverSimulationDistance` (default 2) chunks around
  **every** player, because the server simulates movement against chunk
  colliders. (Before, a dedicated server built terrain only around a
  local player, which it does not have.)
* The chunk under each player is built immediately if missing, so nobody
  falls through loading terrain. `WorldManager.FindSpawnPosition` picks a
  dry, flat spawn spot and builds the terrain around it first.
* Chunk data is kept two rings beyond the visible area and unloaded
  further out, except chunks a player modified (kept until saving exists).
* Distance fog ends at the render distance, so new chunks fade in instead
  of popping in (`ChunkStreamer` > Atmosphere).
* Measured outside Unity on 4 cores: the full render distance (225
  chunks) loads in about 1.3 s, nearest chunks first; the chunk under the
  player is ready on the first frame.

## Rendering

* `Rendering/ChunkMeshBuilder.cs` builds one quad per visible face with
  vertex colours (block colour x biome tint x per-voxel variation); baked
  ambient occlusion goes in the vertex alpha. Water is a separate
  transparent mesh, grass and flowers a separate detail mesh; neither
  has a collider. Colliders are cooked on worker threads
  (`Physics.BakeMesh`), except under players.
* `Rendering/VoxelAtmosphere.cs` (added automatically on clients): warm
  sun with soft shadows, sky/ground light that the voxel shader combines
  with the AO, ACES tone mapping, gentle bloom, colour grading.
* Shaders: `Assets/Materials/Shaders/Voxel/` (`ForgedRusted/Voxel Lit`,
  `ForgedRusted/Voxel Water`, URP). Materials:
  `Assets/Resources/World/ChunkMaterial.mat` and `WaterMaterial.mat`
  (loaded automatically if not assigned on `WorldManager`).
* No textures: the look comes from small voxels, per-voxel colour
  variation and lighting. Colours live in `BlockRegistry` and `Biomes`.

## Block editing

See `Docs/Building.md` (server-validated breaking/placing, edit sync).

## Not done yet

* Saving/loading modified chunks.
* Dungeons and other big structures (the hook above is ready).
* Lava, flowers/tall grass and other non-cube blocks.
