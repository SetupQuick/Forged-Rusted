# Building, breaking and the server-owned inventory

## Controls

| Input | Action |
|-------|--------|
| Left mouse (hold to repeat) | Break the targeted voxel |
| Right mouse (hold to repeat) | Place the selected hotbar block on the targeted face |
| 1-9 / mouse wheel | Select hotbar slot |
| Tab / I | Inventory screen |

Reach is 5 m (`BlockEditRules.ReachMetres`). The targeted voxel gets a
dark outline and its name is shown under the crosshair. Right-clicking
grass or flowers places the block in their place.

## Who decides what (server authoritative)

```
client: predict locally -> PlayerAction (reliable, ordered) -> server
server: validate -> apply to its inventory + world
        -> BlockChanged to every client (or a correction to the sender)
        -> InventorySnapshot (+ last processed action) to the sender
client: inventory = snapshot, then re-apply actions the server has not
        processed yet (same idea as movement reconciliation)
```

* `ServerGameplayModule` (server) owns every player's inventory and
  validates edits: in reach (5 m + 1.5 m latency allowance), block
  breakable (not bedrock/water), target replaceable (air, water, grass),
  attached to an existing block, not inside a player, and the hotbar
  slot actually holds that block.
* `ClientGameplayModule` (client) sends actions and applies block changes
  from the server. On a listen server it ignores them, because the host
  shares the server's world.
* `PlayerInventory` predicts every action, so clicks and edits feel
  instant, and reconciles when the server answers.
* Shared rules live in `Assets/Scripts/Gameplay/` (`BlockEditRules`,
  `InventoryLogic`, `InventoryLayout`), so server and client always agree.

## World edits

`WorldManager` remembers every edit per chunk and re-applies it whenever
the chunk is generated. So edits survive chunks unloading, edits for
chunks a client has not loaded yet are applied when it loads them, and
`SetBlock` never generates far-away chunks. A player who joins later gets
all earlier edits (in batches of 100, paced to the reliable channel).
This edit list is also what world saving should write to disk.

## Messages (protocol version 8)

| Type | Direction | Contents |
|------|-----------|----------|
| `PlayerAction` (14) | client -> server | sequence, type, slot / button / shift or voxel position |
| `BlockChanged` (15) | server -> clients | voxel position, block |
| `InventorySnapshot` (16) | server -> owner | last processed action, all slots, held stack |
| `WorldEdits` (17) | server -> joining client | up to 100 block changes |

Items travel as compact numeric ids (`ItemDatabase.GetNetworkId`), which
are identical on every machine running the same build.

## Not done yet

* Saving the world edits and inventories to disk.
* Item drops as world objects (pickups of scene objects work offline only).
* Breaking time / tools (breaking is instant for now).
* Only players near an edit should receive it (everyone does for now,
  fine for ~10 players).
