# Player, swimming, inventory and HUD

## Movement (`Assets/Scripts/Player/CharacterMotor.cs`)

Simulated once per network tick on the server (authoritative) and on the
owning client (prediction). Controls come from `Player_actions.inputactions`.

| Action | Behaviour |
|--------|-----------|
| Move | Walk (3 m/s by default) |
| Sprint | x2 speed |
| Jump | Jump (~1.3 blocks high); hold to swim up |
| Crouch (hold) | Slower, lower camera, no auto-step up blocks; in water: dive |

* Standing up from a crouch is blocked under a low ceiling.
* Water and headroom checks read block data, not physics, so server and
  client prediction always agree.

### Swimming

Water has no collider; the motor swims instead:

* waist in water = swimming: slower horizontal movement (sprint helps),
  no gravity;
* idle: you float up and bob at the surface with your head above water;
* hold Jump: swim up; hold Crouch: dive;
* at the surface, hold Jump and swim into a ledge to climb out;
* knee-deep water just slows walking;
* the HUD tints the screen blue while your head is underwater.

`PlayerInputButtons.JumpHeld` was added for this (protocol version 7).

## Inventory (`Assets/Scripts/Inventory/`)

* `ItemData` - item definition (ScriptableObject). Put item assets in
  `Assets/Resources/Items/` (Pork and Chicken are there). Leave `icon`
  empty to get a generated one.
* Block items are generated automatically for every block
  (`ItemDatabase.GetBlockItem(BlockId)`), with voxel-cube icons.
* `InventoryContainer` - slots with stacking, split, swap, quick-move.
  Plain C#, so the server can own it later.
* `PlayerInventory` - on the player prefab, active only for the local
  player: 9 hotbar + 27 backpack slots, selection (1-9 / mouse wheel),
  pickups (look at a `PickupItem` with a collider and press E). Gives a
  stack of each basic block at spawn for testing (`giveStarterBlocks`).

The server owns the inventory; the client predicts changes (see
`Docs/Building.md`). Scene-object pickups (E) work offline only.

## HUD (`Assets/Scripts/UI/GameHud.cs`, `Inventory/InventoryUI.cs`)

Built from code when the local player spawns - no scene setup needed.

* crosshair, hotbar with selection and key numbers, selected item name,
  pickup prompt, short messages, underwater tint, controls hint;
* inventory screen: Tab or I (Esc closes). Click to move stacks,
  right-click to split / place one, shift-click to move between hotbar
  and backpack. Movement and mouse look pause while it is open.

Not done yet: health bar (there is no health system yet), dropping
items into the world.

## Looking at things

* Blocks: every frame a voxel raycast (`Gameplay/VoxelRaycast`) steps
  through the block data from the camera, up to 5 m. The hit voxel gets
  an outline and its name is shown under the crosshair. It also hits
  grass and flowers (no colliders needed).
* Items lying in the world (`PickupItem`, offline only): a physics raycast;
  the prompt shows "[E] Pick up <name>", and the item is outlined if it
  has an `OutlineObject` component.

## Collision

* Tree canopies keep at least 2.4 m of free space above the ground in
  every column (also on slopes), so players never catch on leaves at
  head height. Measured over a generated forest: 0 head-height traps
  (was 8.9% of the forest floor). Bushes and boulders stay low on purpose:
  walk around them or step onto them (step height 1 m standing).
* Jumping into a ceiling (cave roof, canopy, block) stops the upward
  speed immediately instead of sticking to it.
* The chunk under every player is built immediately, and the colliders of
  the 8 chunks around it are finished before you can reach them, so you
  never walk into terrain whose collision is still being built.
* Placing a block inside any player is refused by the server.

## Character model and animation (`Player/AvatarModel`, `Player/PlayerAvatar`)

There was no player model in the project, so there is a voxel character
built in code: boots, trousers with knees, tunic with belt, face, hair;
colours differ per player. Animated in code from how the player moves
(works the same for every player without extra network data): walk/run
cycle with knee bend, idle breathing, jump/fall pose, swimming strokes,
crouch (own player). Your own body only casts a shadow (first person).

Not replicated yet: other players' crouch and look-up/down pitch.
Replace with a modelled + animated character whenever the team has one
(keep `PlayerAvatar` as the place that drives it).

## Sounds (`Audio/`)

No audio files exist yet, so sounds are generated at startup
(`SoundSynth`): footsteps by surface (grass, dirt, stone, sand, snow,
wood, water), landing, block break and place by material, splash when
entering water, swim strokes. Other players' sounds are positional.
Hearing is muffled while your head is under water. Swap in recorded
sounds later in `GameSounds`.
