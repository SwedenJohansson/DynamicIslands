# Custom Islands (DynamicIslands)

A [Raft](https://raft-game.com/) mod for the [Raft Mod Loader](https://www.raftmodding.com/) (RML): build your own islands in an in-game editor, then find them in your worlds while sailing.

By FranzFischer78 (code) and MegaMatrixs (design). Version 3 was rebuilt for Raft 1.1 (Unity 2021.3) with SwedenJohansson.

## Features

- **Island editor** (EDITOR button in the main menu)
  - Sculpt the terrain with a round brush: Raise, Lower, Flatten and Smooth.
  - Paint it with Raft's own ground textures (Sand, Grass, Rock, Seabed), or let it texture automatically by height and slope.
  - Place about 300 objects taken from Raft's islands: palms, trees, bushes, boulders, corals, harvestable palms, rocks and berry bushes, and props from Vasagatan. Move, rotate, scale and delete them.
  - Undo and redo everything, and save or load islands.
  - **Generate** a random island to start from. You choose the seed, size, height, roughness, number of peaks, and how many trees, rocks and corals to scatter. The same seed always gives the same island.
- **In your worlds**
  - Islands appear on their own ahead of the raft while you sail. They're kept away from Raft's own islands, and far-away islands are unloaded to save memory.
  - You can also spawn one yourself with `SpawnIsland`.
  - Islands are saved with the world. You can walk on them, the raft runs aground on them, and palms, mango trees, rocks and berry bushes can be harvested.
  - Chopped trees and picked-up items stay that way, even after the island unloads or the world is reloaded. They grow back after 3 in-game days (set with `regrowDays` in `spawnpool.txt`).
- **Multiplayer:** the host's islands are sent to players who join, together with any island files they don't have and what has been harvested there. Harvesting and picking up items stay in sync.

## Installing

Install RML, then put `DynamicIslands.rmod` in Raft's `mods` folder, or get the mod from raftmodding.com. Every player in a multiplayer game needs the mod.

Island files and settings live in `<Raft>\Mods\DynamicIslands\`:

| File | What it is |
|---|---|
| `*.island` | Saved islands. Share them by copying the file. |
| `spawnpool.txt` | Which islands appear on their own while sailing, how often, and when harvested objects grow back. It's created on first use and explains itself. |
| `worlds\<world id>.txt` | The custom islands in each world, with their harvested and picked-up objects. |
| `placeables_generated.txt` | All objects the editor offers. Small indoor clutter is listed at the end, commented out. Copy the file to `placeables.txt` and edit it to choose your own list. |
| `<name>_<hash>.island` | Islands downloaded from a multiplayer host. |

## The editor

| Control | What it does |
|---|---|
| ISLANDS button, Menu > Save/Load, Ctrl+O | Islands window: a name field and the list of saved islands. Click to pick, double-click to load, Enter to save. |
| Ctrl+S | Save the current island |
| Ctrl+Z / Ctrl+Y | Undo / redo sculpting, painting, placing, moving, rotating, scaling and deleting |
| Terrain tab | Raise / Lower / Flatten / Smooth, texture paint, Auto, brush size and strength. A ring shows the brush. |
| Terrain tab > Generate, Menu > Generate island... | Opens the island generator. Generating replaces the current island; Ctrl+Z brings the old one back. |
| Objects tab | Object list, plus Move / Rotate / Scale / Delete (keys 1–4). Delete deletes the selection, P toggles pivot/center, X toggles global/local. |
| Camera | WASD or arrows to move, Shift for faster, right-drag to rotate, mouse wheel to change height |

The blue plane is sea level. Anything below it is under water in game.

## Console commands (F10)

| Command | Where | What it does |
|---|---|---|
| `LoadEditor` | Main menu | Opens the editor |
| `SaveIsland <name>` / `LoadIsland <name>` | Editor | Saves or loads an island |
| `GenerateIsland [seed] [size m] [height m] [roughness 0-1] [peaks] [objects 0-1]` | Editor | Generates a random island (a random seed if none is given) |
| `ListIslands` | Anywhere | Lists saved islands |
| `SpawnIsland <name> [distance]` | Game, host | Spawns an island ahead of the raft (default 250 m) |
| `RemoveIsland <name>` / `RemoveIsland all` | Game, host | Removes spawned islands |
| `ListSpawned` | Game | Lists the world's custom islands, with distance and state |
| `SpawnPool` | Game | Shows which islands appear on their own, and how often |
| `CustomIslandsAuto on` / `off` | Game, host | Turns automatic islands on or off for this world |
| `SetToRaise`, `SetToLower`, `SetToFlatten`, `SetToSmooth`, `ChangeWidth <m>`, `ChangeStrength <m/s>`, `PaintTexture <sand/grass/rock/seabed>`, `SetToAutoPaint` | Editor | The terrain brush settings from the Terrain tab |

Development builds also include `CI*` test commands (`DevTests.cs`); release builds leave them out.

## Building

RML compiles the `.cs` files itself when Raft starts. An `.rmod` is just a zip of the `DynamicIslands\` folder.

```powershell
powershell -ExecutionPolicy Bypass -File compile.ps1             # compile check with Visual Studio's MSBuild
powershell -ExecutionPolicy Bypass -File pack.ps1 -Install       # build DynamicIslands.rmod and copy it to <Raft>\mods
powershell -ExecutionPolicy Bypass -File pack.ps1 -Release       # release build without the CI* dev commands
```

The project references Raft's and RML's assemblies through the `RaftDir` and `RMLDir` properties in `DynamicIslands.csproj`. Start Raft once with RML first, so it creates the publicized assemblies. Stay within C# 7.3, which is what RML compiles with.

The editor UI is a separate Unity 2021.3.45 project, [Custom-Islands-UI](https://github.com/FranzFischer78/Custom-Islands-UI). Its asset bundles are `editorsceneci.assets` and `maincustomislandsbundle.assets`.

### Source overview

| File | What it holds |
|---|---|
| `DynamicIslands.cs` | Mod entry: main menu button, editor setup, save/load, spawn commands, network message hook |
| `IslandFile.cs` | `.island` format 2: compressed binary with heights, texture paint and paint mask, plus objects |
| `IslandSpawner.cs` | Builds an island in a world: cropped terrain, textures, objects, network ids |
| `IslandWorldState.cs` | The world's island list: saved per world, follows world shifts |
| `CustomIslandSpawner.cs` | Automatic islands while sailing, and loading/unloading islands by distance |
| `IslandObjectState.cs` | Harvested trees and picked-up items per island, and regrowing |
| `IslandNetwork.cs` | Multiplayer: island list, removals and island file transfer between host and clients |
| `PlaceableCatalog.cs` | The object catalog, built from Raft's island scenes |
| `IslandGenerator.cs`, `GeneratorWindow.cs` | Procedural islands: heights from seeded noise, object scatter by zone, and the Generate window |
| `terraineditor.cs`, `TerrainPainter.cs`, `EditorTools.cs`, `EditorUI.cs`, `IslandFilesWindow.cs`, `ObjectPlacer.cs`, `RTSCamera.cs` | The editor |
| `RuntimeGizmo\`, `AwaitExtensions\` | Third-party move/rotate/scale gizmo and await helpers |

## Known limitations

- Multiplayer has only been tested on one PC (message serialization and a looped-back file transfer). It hasn't been tested in a real two-player session yet.
- Support for Unity-built `.assets` islands from version 2 was removed. Rebuild those islands in the editor.

## License

GNU AGPLv3
