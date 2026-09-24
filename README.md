# Custom Islands (DynamicIslands)

A [Raft](https://raft-game.com/) mod for the [Raft Mod Loader](https://www.raftmodding.com/) (RML): build your own islands in an in-game editor, then find them in your worlds while sailing.

By FranzFischer78 (code) and MegaMatrixs (design). Version 3 was rebuilt for Raft 1.1 (Unity 2021.3) with SwedenJohansson.

## Features

- **Island editor** (EDITOR button in the main menu)
  - Sculpt the terrain with a round brush: Raise, Lower, Flatten and Smooth.
  - Paint it with Raft's own ground textures (Sand, Grass, Rock, Seabed), or let it texture automatically by height and slope.
  - **Island styles:** Tropical, Snowy (Temperance), Desert (Caravan Island), Forest (Balboa) and Volcanic. Each style has its own ground textures, and the paint buttons are named after them.
  - **A modern editor screen:** a top bar (file, undo/redo, the Terrain / Objects / Island tabs), a tool panel with related buttons grouped in bordered boxes, an object browser with pictures, and a status bar that explains the tool or the button under the mouse.
  - Place **every object of Raft**, about 1,900 in all, from a browser with a picture of each one:
    - palms, snowy pines, birches, cacti, bushes, boulders, snowdrifts, icicles, corals
    - sunken barrels, containers and buoys, and scrap on the ocean floor to dive for; generated islands scatter some around their underwater slopes
    - harvestable palms, pines, birches, mango trees, rocks, berry bushes, pineapples, and copper, iron, clay and sand
    - props from Vasagatan
    - **Raft's own building blocks** (88: foundations, floors, walls, doors, windows, pillars, stairs, ladders, fences, roofs), to build huts on islands or **your own abandoned rafts**. Over water they float at the sea surface. An island of only objects (no land) spawns as just those objects, and players can walk on them.
    - **everything else you can build on a raft** (about 230: storage, beds, grills, lights, decorations, plant pots, sails...), as decoration
    - **the objects of every one of Raft's islands and landmarks:** the abandoned rafts, the radio tower, Balboa, Caravan Town, Tangaroa, Varuna Point, Temperance and Utopia. These load from Raft's island scenes the first time their category is opened (about a second each). The mod ships a list of which island each object comes from (`catalog_index.txt`); after a Raft update the editor makes a new one in the background (about half a minute).
  - Move, rotate, scale, duplicate and delete objects. Click an object to select it; Shift+click adds to the selection.
  - **Placing objects:**
    - a search box finds objects in every category, loaded or not
    - Q/E turns the object, [ and ] resize it, and Shift+click keeps placing copies
    - **Random** gives each placed object a random turn and size
    - **Slope** leans objects with the ground
    - **Ground** drops the selected objects onto the terrain
    - **Grid** snaps to Raft's 1.5 m building grid, and Q/E then turn in 90° steps
  - **Creatures:** place Raft's own animals on your island (20 kinds), from the object browser:
    - **Animals: catchable:** chicken, goat and llama. Players catch them with Raft's net launcher and keep them on the raft, as on Raft's own islands.
    - **Animals: hostile:** warthog, pig, bear, mama bear, polar bear, hyena, rats, roach, bee swarm and screecher.
    - **Sea creatures:** puffer fish, angler fish, turtle, stingray, dolphin and whale.
    - The editor shows each one as a coloured marker with its name, or as Raft's real model once you've been in a world since starting Raft.
  - **Creature editor** (select a creature): how many live at that spot (1–8, a herd), difficulty presets (Easy / Normal / Hard / Boss), **health, damage, speed and size**, and whether killed or caught animals come back.
  - **Colour:** tint any object, creatures included, with swatches, a strength slider, or your own red/green/blue mix.
  - **Notes:** "Notes & signs" has a paper, a bundle of papers, an open book, a sign, a notice board and a message in a bottle, and **any object can be made readable**. The **note editor** has a title, the text (several lines) and a preview of how players will see it.
  - **Island info:** give the island a name, an author and a short description (Island tab). Players see them as a banner when they arrive.
  - Undo and redo everything, and save or load islands.
  - **Generate** a random island to start from. You choose the seed, size, height, roughness, number of peaks, style, and how many trees, rocks and corals to scatter. The same seed always gives the same island. Volcanic islands get a cone with a crater.
- **In your worlds**
  - Islands appear on their own ahead of the raft while you sail. They're kept away from Raft's own islands, and far-away islands are unloaded to save memory.
  - Now and then a **brand-new random island** is generated instead: random size, style, and sometimes flying. It's saved as `gen-<style>-<seed>.island`, so it stays in that world. Set with `generated`, `generatedStyles` and `generatedFlyingChance` in `spawnpool.txt`.
  - **Custom islands show on Raft's Receiver** as green dots with their distance, so you can navigate to them. Turn this off with `showOnReceiver = 0`.
  - You can also spawn one yourself with `SpawnIsland`.
  - Islands are saved with the world. You can walk on them, the raft runs aground on them, and palms, mango trees, rocks and berry bushes can be harvested.
  - **Flying and underwater islands:** give an island a height in the Islands window, or with `SetElevation`, or when spawning. A flying island loses its seabed and gets a rocky underside, and the raft sails underneath it. An underwater island sits below the surface for divers.
  - Chopped trees and picked-up items stay that way, even after the island unloads or the world is reloaded. They grow back after 3 in-game days (set with `regrowDays` in `spawnpool.txt`).
  - **Creatures come alive:** the host spawns Raft's real animals at the island's creature spots with the builder's stats and colour. They roam around their spot, and Raft's own networking brings them to the other players. Killed and caught animals are remembered like harvested trees, and come back after `regrowDays` unless the builder turned that off. Caught animals become normal raft animals.
  - **Notes:** look at a readable object and press the interact key (E) to read it. Close it with E, Tab, Esc or the button.
  - **Arriving** near an island that has a name or description shows it as a banner at the top of the screen, once per island per session.
- **Multiplayer:** the host's islands are sent to players who join, together with any island files they don't have and what has been harvested there. Harvesting and picking up items stay in sync.

## Installing

Install RML, then put `DynamicIslands.rmod` in Raft's `mods` folder, or get the mod from raftmodding.com. Every player in a multiplayer game needs the mod.

Island files and settings live in `<Raft>\Mods\DynamicIslands\`:

| File | What it is |
|---|---|
| `*.island` | Saved islands. Share them by copying the file. |
| `spawnpool.txt` | Which islands appear on their own while sailing, how often, and when harvested objects grow back. It's created on first use and explains itself. |
| `worlds\<world id>.txt` | The custom islands in each world, with their harvested and picked-up objects. |
| `placeables_generated.txt` | The core objects the editor offers. Small indoor clutter is listed at the end, commented out. Copy the file to `placeables.txt` and edit it to choose your own list. |
| `catalog_index.txt` | Only after a Raft update: which of Raft's island scenes each of the other objects comes from, made by the editor (the mod ships one for the current Raft). Delete it to scan again. |
| `<name>_<hash>.island` | Islands downloaded from a multiplayer host. |

## The editor

The screen has a **top bar**, a **tool panel** on the left, the **object browser** on the right (Objects tab) and a **status bar** at the bottom. The status bar explains the current tool, or the button under the mouse. Related buttons sit together in bordered groups, and the active choice of a group is lit orange.

| Where | Control | What it does |
|---|---|---|
| Top bar | **New** / **Open** / **Save** / **Save as** | New asks first (click twice), then starts an empty sea. Open and Save as open the Islands window: a name, a height (metres above sea in game: 0 = normal, 60 = flying, −30 = under water) and the saved islands (click = pick, double-click = open, Enter = save, Delete asks first). Save saves straight away once the island has a name. |
| Top bar | **Undo** / **Redo** | Undo / redo sculpting, painting, placing, moving, rotating, scaling, duplicating and deleting (also Ctrl+Z / Ctrl+Y) |
| Top bar | **Terrain** / **Objects** / **Island** (F1 / F2 / F3) | The three tabs |
| Top bar | **Generate** | The island generator: seed, style, size, height, roughness, peaks and objects. Generating replaces the current island; Ctrl+Z brings the old one back. |
| Terrain tab | **Sculpt** group | Raise / Lower / Flatten / Smooth. A ring shows the brush. |
| Terrain tab | **Paint ground** group | The style's four textures (named after it, with a colour swatch) and **Auto** |
| Terrain tab | **Brush** group | Size and strength |
| Objects tab | **Transform** group | Move / Turn / Scale / All (keys 1–4) |
| Objects tab | **Selection** group | What's selected; **Ground**, **Duplicate** (Ctrl+D), **Deselect**, **Delete** (Delete key) |
| Objects tab | **Placing** group | **Random**, **Slope** and **Grid** toggles |
| Objects tab | **Inspector** (one object selected; replaces Placing and the tips) | **Creature** group: animals here, presets, health / damage / speed / size, comes back after N days or never. **Note** group: title, a preview of the text, **Edit note...** (the note editor), **Remove**; for other objects, **Add a note to it...**. **Colour** group: None, swatches, strength, **Custom colour...** (red/green/blue). Every change can be undone. |
| Objects tab | Object browser | Search box, then the categories. Click a category to open or close it. Click an object, then click the ground: Q/E turn, [ and ] resize, Shift+click keeps placing, Esc cancels. |
| Island tab | **Island** group | Style (◄ ►), height in the world with At sea / Flying / Sunken presets |
| Island tab | **Shown to players** group | The island's name, author and description, shown as a banner when players arrive in a world |
| Island tab | **Generate**, **About this island** | Opens the generator; object count, height and how many objects the list has |
| Keys | Ctrl+S / Ctrl+O | Save / open |
| Camera | | WASD or arrows to move, Shift for faster, right-drag to rotate, mouse wheel to change height (not over a panel) |

The blue plane is sea level. Anything below it is under water in game.

## Console commands (F10)

| Command | Where | What it does |
|---|---|---|
| `LoadEditor` | Main menu | Opens the editor |
| `SaveIsland <name>` / `LoadIsland <name>` | Editor | Saves or loads an island |
| `GenerateIsland [seed] [size m] [height m] [roughness 0-1] [peaks] [objects 0-1] [style]` | Editor | Generates a random island (a random seed if none is given) |
| `ListIslands` | Anywhere | Lists saved islands |
| `SpawnIsland <name> [distance] [height]` | Game, host | Spawns an island ahead of the raft (default 250 m), at its saved height or the given one. Warns if it would overlap one of Raft's own islands (players can fall through the ground there). |
| `SetElevation <m>` | Editor | Height above sea the island will have in game (saved with it) |
| `SetStyle <Tropical/Snowy/Desert/Forest/Volcanic>` | Editor | The island's style (ground textures; saved with it) |
| `RemoveIsland <name>` / `RemoveIsland all` | Game, host | Removes spawned islands |
| `ListSpawned` | Game | Lists the world's custom islands, with distance and state |
| `SpawnPool` | Game | Shows which islands appear on their own, and how often |
| `CustomIslandsAuto on` / `off` | Game, host | Turns automatic islands on or off for this world |
| `SetToRaise`, `SetToLower`, `SetToFlatten`, `SetToSmooth`, `ChangeWidth <m>`, `ChangeStrength <m/s>`, `PaintTexture <sand/grass/rock/seabed>`, `SetToAutoPaint` | Editor | The terrain brush settings from the Terrain tab |

Development builds also include `CI*` test commands (`DevTests*.cs`); release builds leave them out.

## Building

RML compiles the `.cs` files itself when Raft starts. An `.rmod` is just a zip of the `DynamicIslands\` folder.

```powershell
powershell -ExecutionPolicy Bypass -File compile.ps1             # compile check with Visual Studio's MSBuild
powershell -ExecutionPolicy Bypass -File pack.ps1 -Install       # build DynamicIslands.rmod and copy it to <Raft>\mods
powershell -ExecutionPolicy Bypass -File pack.ps1 -Release       # release build without the CI* dev commands
```

The project references Raft's and RML's assemblies through the `RaftDir` and `RMLDir` properties in `DynamicIslands.csproj`. Start Raft once with RML first, so it creates the publicized assemblies. Stay within C# 7.3, which is what RML compiles with.

The editor scene and the gizmo shaders come from a separate Unity 2021.3.45 project, [Custom-Islands-UI](https://github.com/FranzFischer78/Custom-Islands-UI). Its asset bundles are `editorsceneci.assets` and `maincustomislandsbundle.assets`. The editor's panels themselves are built in code (`UIKit.cs`, `EditorUI.cs`); the bundle's old toolbar is switched off.

### Source overview

| File | What it holds |
|---|---|
| `DynamicIslands.cs` | Mod entry: main menu button, editor setup, save/load, spawn commands, network message hook |
| `IslandFile.cs` | `.island` format 2 (format 3 when the island has a height or a style, format 4 when it or its objects have settings): compressed binary with heights, texture paint, paint mask, objects, elevation, style, island settings and per-object settings |
| `ObjectProps.cs` | Per-object settings (creature stats, note text, tint): keys, limits, tinting, and the undo step for changing them |
| `ContentCatalog.cs` | Creatures and notes in the object catalog: the creature list, markers and name tags, Raft's creature models for the editor |
| `ObjectInspector.cs`, `NoteEditorWindow.cs` | The Objects tab's inspector (creature editor, note, colour) and the note editor window |
| `CreatureSpawner.cs` | Live creatures in a world: runtime NavMesh, spawning through Raft's `Network_Host_Entities`, stats, tint, killed/caught state, removal with the island, and Harmony patches for speed and players who join |
| `CustomNote.cs` | Readable notes in a world (Raft's interaction, `IRaycastable`) and the note reader |
| `IslandInfo.cs` | Island name, author and description, and the banner shown when players arrive |
| `TerrainPainter.cs` | Automatic and hand texture painting, island styles (which of Raft's ground textures fill the four paint slots) |
| `PlacementTools.cs`, `ObjectPlacer.cs` | Placing objects: placement options, Ground, Duplicate, picking objects with the mouse |
| `UIKit.cs` | The editor's look: theme, rounded and outlined sprites made at runtime, panels, bordered groups, buttons, sliders, fields |
| `EditorUI.cs`, `ObjectBrowser.cs`, `ObjectThumbnails.cs` | The editor screen (top bar, tool panels, status bar), the object browser, and the object pictures |
| `IslandSpawner.cs` | Builds an island in a world: cropped terrain, textures, objects, network ids. For flying islands it also cuts terrain holes and adds the underside mesh |
| `IslandWorldState.cs` | The world's island list: saved per world, follows world shifts |
| `CustomIslandSpawner.cs` | Automatic islands while sailing (saved ones and newly generated ones), and loading/unloading islands by distance |
| `IslandRadar.cs` | Custom islands as dots on Raft's Receiver (Harmony postfix on `Reciever.HandleUI`) |
| `IslandObjectState.cs` | Harvested trees and picked-up items per island, and regrowing |
| `IslandNetwork.cs` | Multiplayer: island list, removals and island file transfer between host and clients |
| `PlaceableCatalog.cs` | The object catalog: the core objects (always loaded), Raft's buildable items, and the index of every other object of Raft's island scenes, loaded scene by scene when needed |
| `IslandGenerator.cs`, `GeneratorWindow.cs` | Procedural islands: heights from seeded noise, object scatter by zone, and the Generate window |
| `terraineditor.cs`, `TerrainPainter.cs`, `EditorTools.cs`, `EditorUI.cs`, `IslandFilesWindow.cs`, `ObjectPlacer.cs`, `RTSCamera.cs` | The editor |
| `RuntimeGizmo\`, `AwaitExtensions\` | Third-party move/rotate/scale gizmo and await helpers |

## Known limitations

- Multiplayer has only been tested on one PC (message serialization and a looped-back file transfer). It hasn't been tested in a real two-player session yet.
- Support for Unity-built `.assets` islands from version 2 was removed. Rebuild those islands in the editor.
- Reaching a flying island is up to the player: build stairs or pillars up from the raft. The editor shows islands at sea level; the height only applies in game.
- Creatures: catching with the net launcher, the host's creatures reaching a second player, players who join later, and the colour on other players' screens are built on Raft's own mechanisms, but they haven't been tested by hand or with two players yet. Raft has no pets, so "catchable" means Raft's domestic animals. The screecher's stone look can't be tinted.
- An island file with creatures, notes, a tint or island info is format 4: older versions of the mod can't open it. Islands without these are still saved in the older formats.
- Objects from Raft's other islands and Raft's buildable items are decoration: their scripts are removed, so a chest doesn't store anything and a character doesn't move. Only the harvestable trees, rocks, ores and plants keep their gameplay.
- An island that uses objects from one of Raft's story islands (e.g. Utopia) makes the mod load that island's scene for a moment when the island spawns in a world, to copy the objects. The scene is switched off as it arrives; this can take a second.

## License

GNU AGPLv3
