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

The inventory is client-side for now. It should become server
authoritative together with networked block placing/breaking.

## HUD (`Assets/Scripts/UI/GameHud.cs`, `Inventory/InventoryUI.cs`)

Built from code when the local player spawns - no scene setup needed.

* crosshair, hotbar with selection and key numbers, selected item name,
  pickup prompt, short messages, underwater tint, controls hint;
* inventory screen: Tab or I (Esc closes). Click to move stacks,
  right-click to split / place one, shift-click to move between hotbar
  and backpack. Movement and mouse look pause while it is open.

Not done yet: health bar (there is no health system yet), block
placing/breaking, dropping items into the world.
