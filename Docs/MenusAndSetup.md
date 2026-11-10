# Main menu, settings, and opening the project

## Opening the project

Everything is already assigned in the main scene; nothing has to be
dragged in by hand.

1. Get the project with Git LFS enabled. GitHub Desktop does this by
   itself; on the command line run `git lfs install` once, then clone (or
   `git lfs pull` in an existing clone). Without LFS the `.unity` scene
   files are tiny text stubs and Unity shows empty scenes.
2. In Unity Hub: **Add > Add project from disk**, pick the folder, open it
   with Unity **6000.4.5f1** (the version in
   `ProjectSettings/ProjectVersion.txt`). The first import takes a few
   minutes (Unity downloads the packages).
3. The main scene **`Assets/Scenes/Game.unity`** opens automatically the
   first time (or menu **Forged & Rusted > Open Game Scene**). It is the
   first scene in File > Build Profiles. It contains:
   * **Network** - NetworkWorld (prefab registry
     `Resources/Network/DefaultNetPrefabRegistry`), NetworkRuntime,
     PeerSpawnExample (player prefab `Resources/Network/NetworkPlayer`);
   * **World** - WorldManager (`Resources/World/DefaultWorldSettings`,
     chunk and water materials), ChunkStreamer;
   * **Sun** - directional light (the day/night cycle drives it).
4. Press Play. The main menu appears over a generated world.

`Assets/Scenes/Network.unity` is the networking test scene. It spawns the
bare `PlayerTest` prefab (no inventory, HUD or building) - the main menu
shows a red warning there. Use `Game.unity` to play.

If a scene is missing parts, the game completes it at runtime
(`Game/GameBootstrap`): it adds a NetworkRuntime (with the player prefab
from `Resources/Network`), a WorldManager (with
`Resources/World/DefaultWorldSettings`) and a ChunkStreamer if they are
missing. Problems it cannot fix are shown in red in the main menu and in
the console.

Created by code, never needed in a scene: main menu, pause menu, HUD,
inventory UI, EventSystem, sun settings and post-processing, sounds,
player model.

Assets moved into `Resources/` so code can find them (their GUIDs are
unchanged, so existing scene references keep working):
`Resources/World/DefaultWorldSettings.asset`,
`Resources/Network/NetworkPlayer.prefab`,
`Resources/Network/DefaultNetPrefabRegistry.asset`.

## Main menu (`Game/GameFlow`)

* **Host Game** - listen server on a port (default 25565); others join
  with your IP address.
* **Join Game** - server address and port (remembered).
* **Settings**, **Controls**, **Quit**.

Esc in game opens the **pause menu**: Resume, Settings, Controls,
Stop hosting / Disconnect, Quit. (Esc first closes the inventory if it is
open.) Multiplayer keeps running while paused.

Connection failures (time-out, server gone) return to the main menu
with a message.

## Settings (`Game/GameSettings`, saved in PlayerPrefs)

| Tab | Settings |
|-----|----------|
| Display | display mode (fullscreen / borderless / window), resolution, V-Sync, frame rate limit, field of view, render pipeline profile |
| Graphics | preset (Low / Medium / High / Ultra / Custom), render distance (4-16 chunks), shadows (off/low/medium/high), grass distance, clouds, wind, post-processing, render scale, anti-aliasing - see [Performance.md](Performance.md) |
| Audio | master volume, effects volume |
| Controls | mouse sensitivity, invert Y (key list on the Controls screen) |
| Interface | FPS counter, network statistics (F3) |

Changes apply immediately; Back saves. "Reset to defaults" is on every tab.

## Command line

```
Game.exe -server [-port 25565]     dedicated server, no menu (also in batch mode)
Game.exe -host [-port 25565]       host straight away
Game.exe -connect 1.2.3.4[:25565]  join straight away
Game.exe -nomenu                   old behaviour (NetworkRuntime starts by itself)
```

## Not done yet

* Rebinding keys (the list is shown; changing it needs an input
  rebinding screen).
* Music and its volume slider (there is no music yet).
* Player names, server browser / LAN discovery.

## Hosting a saved world

The host screen asks for a world name and your player name. Worlds are
saved automatically - see [Saving.md](Saving.md). The join screen also
asks for your name; your inventory is kept per world on the host.
