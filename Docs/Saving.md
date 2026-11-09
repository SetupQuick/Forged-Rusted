# World saving

## What is saved

Only what players changed - the terrain itself is regenerated from the
seed every time, so saves stay tiny and load instantly however far
people explored:

* seed and world-generator version,
* the day/night clock (the world continues at the same time of day),
* every block a player placed or broke (also in chunks nobody has loaded),
* every player's inventory, by player id.

10,000 changed blocks take about 38 KB and save in ~15 ms.

## Where

`Application.persistentDataPath/worlds/<world name>/world.frs`
(Windows: `%USERPROFILE%\AppData\LocalLow\<company>\<product>\worlds\`).
Copy the folder to back up or share a world.

## When

* Every 5 minutes if anything changed (`WorldSaver.AutosaveMinutes`).
* When the host leaves the game or quits, and when the server stops.

The data is copied on the main thread and written on a worker thread, so
autosaves do not stutter the game.

## Safety

* Written to `world.frs.tmp` first, then swapped in; the previous save is
  kept as `world.frs.bak`. A crash or power cut while saving can never
  leave a broken world.
* A damaged save automatically loads the backup. If both are broken, a new
  world starts and the old file is kept as `world.frs.broken-<date>`.
* A save from a newer version of the game is refused rather than misread.
* A save from an older world generator still loads (edits are kept); the
  log warns that unexplored land may look different.

## Hosting and servers

* **Host screen**: enter a world name. An existing name loads that world,
  a new name creates one. Saved worlds are listed under the field.
* **Dedicated server**: `-server -world <name>` (default `world`).
* **Command line host**: `-host -world <name>`.

## Players

Each PC has a random player id (created once, stored in PlayerPrefs) and
a name (entered on the host/join screens). The client sends both when it
connects (`PlayerProfile` message, protocol version 11). The server gives
back the saved inventory for that id; new ids get the starter items.

The id is not a password - copying someone's PlayerPrefs would let you
take their inventory. Fine among friends; real accounts would come with a
login service later.

Not saved yet: player positions (everyone spawns at the spawn point),
health/hunger (not in the game yet).

## Code

* `World/Saving/WorldSaveFile.cs` - file format, read/write, world folders.
* `World/Saving/WorldSaver.cs` - autosave, save on quit, open/close.
* `Game/HostedWorld.cs` - connects the saver to the server's inventories.
* `WorldManager.CaptureSave / LoadSave / StartNewWorld`.
* `ServerGameplayModule.ImportPlayers / ExportPlayers`, `LocalPlayerProfile`.
