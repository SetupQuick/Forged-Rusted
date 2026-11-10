# Gameplay systems: mining, creatures, health, classes, inventory, chests

Everything here is decided by the server (cheat-safe) and predicted on
the client where it matters for feel. Content marked PLACEHOLDER (tools,
creatures, classes, skill trees, quests) exists so the systems can be
tested; the real materials, recipes, creatures, classes and quests
replace the data without changing the code around it.

## Controls

| Key | Action |
|---|---|
| Left mouse (hold) | break block (progress depends on the tool) / attack creature |
| Right mouse | place block / eat food / open chest |
| Q / Ctrl+Q | drop one / the whole stack |
| Tab or I | inventory |
| K | skill tree (class picker before a class is chosen) |
| J | quest log |
| F | dash (skill) |
| Space in mid-air | double jump (skill) |
| G | grappling hook (skill) - aim at a block, G again to let go |
| R | fireball (skill, mana) |
| V | invisibility (skill, mana) |

Inventory screen: shift-click quick move (chest <-> inventory, else
hotbar <-> backpack), drag a stack to spread it (left evenly, right one
per slot), double-click to collect the same item, 1-9 over a slot to
swap with the hotbar, Q over a slot to drop, click outside the window to
drop the cursor stack. Chest window: Quick stack, Deposit all, Take all.

## Mining (`Gameplay/Mining.cs`)

Per block: hardness, the right tool (pickaxe / axe / shovel / sword) and
the tier needed for a drop. Break time = hardness x (1.5 if it drops,
else 5) / tool speed. Stone and ores drop nothing without a good enough
pickaxe. The server times every break (`StartBreak` action) and refuses
breaks faster than the tool allows. Drops go straight into the inventory.

Tools (PLACEHOLDER, `Inventory/GameItems.cs`): wood / stone / iron /
gold pickaxe, axe, shovel, sword. An `ItemData` asset in
`Resources/Items` with the same id replaces a built-in item. New tool
fields on `ItemData`: tool type, tier, speed, attack damage, heal amount.

## Creatures and dropped items (`Entities/`)

`EntitySimulation` (pure code, tested) runs on the server: gravity,
collision with blocks, one-block steps, swimming; pigs and chickens
wander and flee when hit; zombies chase the nearest visible player
(20 m) and hit once a second. Spawned 12-26 m around players (more
zombies at night), removed beyond 96 m. Clients see them through one
network prefab built in code (`EntityPrefabs`), drawn by `EntityView`.
Dropped items: picked up by walking over them (after 1.5 s), gone after
5 minutes.

## Health

20 health, fall damage above 3.2 m (2 per extra metre), regeneration
after 6 s without damage, zombie damage, death screen and respawn at the
world spawn after 3 s (inventory is kept for now), eating food
(`healAmount` on the item; pork 6, chicken 4).

## Classes, skills, quests (PLACEHOLDER content in `Gameplay/ClassDefinitions.cs`)

Warrior, Mage and Rogue, each with a skill tree (cost, prerequisites)
and two quests. 2 skill points to start, +1 per level; XP from creatures
and quests (50 x level per level). Mana is unlocked by a skill.
Abilities the code understands: mana, mana flow, toughness (+25% melee),
dash, double jump, grappling hook, fireball, invisibility.
`Gameplay/PlayerProgress.cs` holds the rules (tested); everything is
saved with the world.

Dash, double jump and the hook run inside the movement code
(`CharacterMotor`), timed in input sequence numbers. Their state (last
presses, dash and double-jump timers, hook point) is part of the
movement state the server sends back (`MovementAbilityState`, only once
an ability has been used), so after a server correction the client
replays them exactly the same way - no rubber-banding on a dash under
lag. Fireball and invisibility run on the server.

## Chests

New block `Chest`. Contents are kept by the server per position, shown
to everyone who has the chest open, closed when you walk away, spilled
on the ground when the chest is broken, and saved with the world.

Chest clicks are predicted like inventory clicks: shown at once, then
checked against the server. The server's inventory snapshot carries the
open chest's contents at the same acknowledged action, so the client
resets both to the server's state and re-applies its own clicks the
server has not seen yet. When another player changes the chest, every
player looking into it gets a new snapshot the same way.

## Versions

* Network protocol 13 (12: player status/effects, chest contents, new
  actions, input with pitch and ability buttons; 13: ability state in
  movement corrections, chest contents in inventory snapshots).
* Save format 3 (2: player progress, 3: chests). Older saves load.

## Not done / known limits

* Not yet run in the Unity editor - needs a test on the PC.
* Creatures do not path-find (they walk straight and hop one block) and
  do not collide with players.
* No tool durability, no crafting (planned with your own recipes).
