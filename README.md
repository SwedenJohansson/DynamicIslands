# Custom Islands (DynamicIslands)

A [Raft](https://raft-game.com/) mod for the [Raft Mod Loader](https://www.raftmodding.com/) (RML): build your own islands in an in-game editor, then find them in your worlds while sailing.

By FranzFischer78 (code) and MegaMatrixs (design). Version 3 was rebuilt for Raft 1.1 (Unity 2021.3) with SwedenJohansson.

## Features

- **Island editor** (EDITOR button in the main menu)
  - Sculpt the terrain with a round brush: Raise, Lower, Flatten and Smooth.
  - Paint it with Raft's own ground textures (Sand, Grass, Rock, Seabed), or let it texture automatically by height and slope.
  - **Island styles:** Tropical, Snowy (Temperance), Desert (Caravan Island), Forest (Balboa) and Volcanic. Each style has its own ground textures, and the paint buttons are named after them.
  - **An editor screen in Raft's own look** (its menu sprites, brown panels, tan buttons and fonts, taken from the game at start): a top bar (file, undo/redo, the Terrain / Objects / Island tabs), a tool panel with related buttons grouped in bordered boxes, an object browser with pictures, and a status bar that explains the tool or the button under the mouse.
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
  - **Loot (chest editor):** "Loot & chests" has chests, a sealed crate, a wooden box, barrels and a sunken barrel, and any object can hold loot. Choose the items from all of Raft's items with pictures and a search, set the amounts, or pick a ready-made set (Basics, Metal, Food, Treasure). A chest fills up again after the regrow time, or never.
  - **Trigger zones:** an invisible area (a sphere in the editor). When a player walks in, it shows your message, gives items, and wakes up the creatures that wait for it (an **ambush**). It fires once per world (again after the regrow time) or every time.
  - **Island info:** give the island a name, an author and a short description (Island tab). Players see them as a banner when they arrive.
  - **Signs:** a readable sign shows its note's title on its board, in the editor and in a world.
  - **Atmosphere zones:** fog colour and thickness, a light tint and particles (fireflies, mist, snow, embers) around a spot. Fly the camera in to see it. It fades in at the edge and never changes Raft's own weather.
  - **Sound zones:** one of Raft's own 500+ sounds (ambience, birds, wind, music...), chosen from a searchable list where you can listen first. It plays while a player is inside, louder towards the middle, or once when they walk in.
  - **Quests** (Island tab, **Edit quest...**): a title, an introduction, up to 10 steps in order (**go to** a trigger zone, **read** a note, **open** a chest, **defeat** or **catch** a number of animals), a reward and a closing message. Steps point at things on the island by name, and the editor lists the names it knows.
  - **Object groups:** select objects and click **Save as group...**. The group appears under "My groups" at the top of the object list, to place on any island as one piece. Once put down, it becomes its separate objects again, with their settings. Groups are files in `Mods\DynamicIslands\groups`.
  - **Terrain stamps** (Terrain tab): click to put down a Hill, Peak, Crater, Mesa, Lagoon or Ridge, as big as the brush; Q/E turn it. **Save stamp...** keeps the land under the brush as a stamp of your own (`Mods\DynamicIslands\stamps`).
  - **Behaviour & events** (select any object, "Behaviour & events..."), no code needed:
    - **a name** that actions refer to (objects with the same name act together)
    - **movement:** spin, bob up and down, or move and turn between two poses, either **back and forth** for ever or as a door, gate, bridge or lift that **opens and closes** (with a Preview in the editor)
    - **hidden at first** until an action shows it (a hidden creature spot is an ambush)
    - **players can use it:** Raft's "press E" hint with your own text ("Pull the lever")
    - **collision:** Raft's own, walk through, one box, or solid
    - **when … then:** a player uses it, walks into a zone, reads a note (first time), opens a chest, or all the animals of a creature spot are defeated → **show / hide / show-or-hide** objects, **open / close / open-or-close** doors, **say** a message, **give** items, **play** one of Raft's sounds, **teleport** the player to an object, **send a signal** (world plans and island rules can wait for it), write a **journal page**, or **wait** a number of seconds before the actions after it (a gate that falls shut again)
    - **only if …:** checks before the actions run: the player **has** an item or a story item, or **uses one up** (a key), an object **is** open / closed / shown / hidden, a **signal** was sent, the quest reached a step. **Otherwise** a message ("It's locked. Maybe there's a key somewhere...")
    - **Island events** (Island tab): when players first come to the island, and when its quest is done
    - A door that is used without actions of its own opens and closes itself.
  - **Story items** (Island tab, **Story items...**): keys, map pieces, logs... with a name, a description and a picture (Raft's quest item pictures or any Raft item). Chests, zones, quest rewards and "give" actions hand them out (the item picker lists them first), and "only if" checks ask for them.
  - **Story sets** (in the Story items window): ready pieces of a story placed around the view in one undo step: **a locked door and its key** (in a chest with a note), or **a trail of notes** that leads to a hidden chest with a story item.
  - **Invisible walls and ramps** ("Zones & triggers"): solid in a world but not seen. Block a path, keep players in an arena, or make a cliff or sea stack climbable. Scale and turn them to fit.
  - **Island rules** (Island tab): how many in-game days until chopped trees, picked items, killed or caught animals, looted chests and fired zones come back on this island (empty = the world's setting, 0 = never).
  - Undo and redo everything, and save or load islands.
  - **Generate** a random island to start from. You choose the seed, size, height, roughness, number of peaks, style, **layout** (round, atoll, archipelago, sea stacks, plateau, marsh), and how many trees, rocks and corals to scatter. The same seed always gives the same island. Volcanic islands get a cone with a crater.
  - **Map types** (Generate window, "Or a map type"): whole islands with content, made from a seed and opened to edit: sandbar, atoll, archipelago, sea stacks, boss island, volcano, swamp, frozen spire, treasure island, old camp, sunken island, sky island and wreck (see "Map types" below).
  - **When the quest is done, bring a new island** (quest editor): a saved island or a new island of a map type, how far and which way from this island, a message for every player and a name on the Receiver. **Islands it brings...** (Island tab) edits all the island's rules: also "when step 2 is done", "when zone X fires", "when players first get here".
  - **World plans** (top bar): plans for new worlds, made of rules (see "World plans" below). A plan editor with rule cards, **Check** (finds rules that can't work) and a sketch of where islands go, plus ready-made templates.
- **In your worlds**
  - Islands appear on their own ahead of the raft while you sail. They're kept away from Raft's own islands, and far-away islands are unloaded to save memory.
  - Now and then a **brand-new random island** is generated instead: random size, style, and sometimes flying. It's saved as `gen-<style>-<seed>.island`, so it stays in that world. Set with `generated`, `generatedStyles` and `generatedFlyingChance` in `spawnpool.txt`. Lines like `type:wreck 0.4` mix in **map types** (sandbars, wrecks, atolls, sunken islands... see below).
  - **Custom islands show on Raft's Receiver** as green dots with their distance, so you can navigate to them. Turn this off with `showOnReceiver = 0`.
  - You can also spawn one yourself with `SpawnIsland`.
  - Islands are saved with the world. You can walk on them, the raft runs aground on them, and palms, mango trees, rocks and berry bushes can be harvested.
  - **Flying and underwater islands:** give an island a height in the Islands window, or with `SetElevation`, or when spawning. A flying island loses its seabed and gets a rocky underside, and the raft sails underneath it. An underwater island sits below the surface for divers.
  - Chopped trees and picked-up items stay that way, even after the island unloads or the world is reloaded. They grow back after 3 in-game days (set with `regrowDays` in `spawnpool.txt`).
  - **Creatures come alive:** the host spawns Raft's real animals at the island's creature spots with the builder's stats and colour. They roam around their spot, and Raft's own networking brings them to the other players. Killed and caught animals are remembered like harvested trees, and come back after `regrowDays` unless the builder turned that off. Caught animals become normal raft animals.
  - **Notes:** look at a readable object and press the interact key (E) to read it. Close it with E, Tab, Esc or the button.
  - **Chests:** look at one and press E: the items go into your inventory (what doesn't fit drops in front of you), and the chest is empty for everyone, also after reloading, until it fills up again. A chest with a note shows the note too.
  - **Trigger zones** fire for whoever walks in; an ambush creature appears the moment its zone fires.
  - **Quests:** near an island with a quest, a panel shows its steps (done ones ticked). The introduction shows when you arrive, and each step done shows what's next. The quest is shared by everyone in the world and saved with it. When it's done, every player near the island gets the reward.
  - **Arriving** near an island that has a name or description shows it as a banner at the top of the screen, once per island per session.
  - **The journal** (J): the crew's story items with their pictures, and every custom note read (plus "journal" pages), on paper. Story items are held by the whole crew, like Raft's own quest items, saved with the world and sent to players who join.
  - **Behaviours:** doors, gates and lifts move for every player; things that move back and forth, spin or bob follow a clock all players share (Raft's water time), so everyone sees them in the same place; what is shown, hidden, open or closed is shared by all players, saved with the world and sent to players who join. Messages, items, sounds and teleports go to the player who did it (for defeated animals and finished quests: to everyone near the island).
  - **World plans** decide which islands a world gets (see below): chosen in Raft's **New Game** box ("Custom Islands plan"), or with `WorldPlan <name>` in a world. "Random islands" (the default) is the old behaviour.
  - **New islands from rules:** when a rule brings an island (a quest done, a zone, a visit, km sailed...), every player sees a banner with the message and how far and which way it is, and the island's green dot on the Receiver carries its name.
- **Multiplayer:** the host's islands are sent to players who join, together with any island files they don't have and what has been harvested there. Harvesting and picking up items stay in sync. Only the host checks world plan and island rules; quests and zones done by other players count, because they reach the host.

## World plans

A **world plan** says which custom islands a world gets, **when** and **where**. It's a list of rules; each rule brings one island:

| Part | Choices |
|---|---|
| **What** | a saved island · a new island of a **map type** (generated, e.g. a treasure island) · one from the spawn pool · one of a list of islands |
| **When** | the world starts · after sailing N km · on day N · the quest of an island is done · N steps of its quest are done · a trigger zone of an island fires · players first reach an island · an object sends a signal (Behaviour & events) · after another rule |
| **Where** | N m ahead of the raft · N m from an island, in a direction (north, north-east... or any) |
| **Tell** | a message every player sees (with how far and which way the island is), and a name for its dot on the Receiver |

Rules refer to islands by the id of the rule that brought them (e.g. "when the quest of `camp` is done, bring a treasure island 900 m north-east of `camp`"), or by island name. A plan can also keep the random islands of `spawnpool.txt` going.

- **Choosing a plan:** Raft's New Game box has a "Custom Islands plan" button: click it to go through the plans. `WorldPlan` shows the current world's plan and its rules (done or not); `WorldPlan <name>` gives the world another plan. `defaultPlan` in `spawnpool.txt` is the plan new worlds get when nobody chooses.
- **Built-in plans:** "Random islands" (islands by chance while sailing, as before) and "No custom islands".
- **Sample plans** (written once to `Mods\DynamicIslands\plans`): **Island hopping** (an old camp, then each island you reach shows the way to the next), **Adventure** (a story: each quest leads to the next island, with a wreck and a sunken island on the way) and **Growing sea** (random islands plus a special one every few km and days). They only use map types, so they work without any islands of your own.
- **Making plans:** top bar **World plans**: New / Copy / Delete, a description, "random islands while sailing" on or off, and the rules as cards. **Templates...** adds ready-made sets (story chain, sky chain, quest reward island...). **Check** lists what can't work, and a small map sketches where islands would go. Plans are text files, so they can also be edited by hand (the file explains the format).
- **Islands bring islands:** an island can carry its own rules ("when my quest is done, bring island X 600 m north of me"). These work in any world, with or without a plan, so a chain of shared island files is a story on its own.
- **Each rule fires once per world.** What has fired, the km sailed and which islands players have reached are saved with the world. The host places new islands clear of the raft, the other custom islands and Raft's own islands, and **Raft won't put its own islands on top of custom ones later**.

## Map types

Islands the generator makes by itself, with content. Plans use them (`type:<name>`), the quest editor can bring them, `spawnpool.txt` can mix them in (`type:<name> <weight>`), and the Generate window makes one to edit.

| Type | What |
|---|---|
| `sandbar` | A tiny island with a few palms and a small chest: a rest stop |
| `atoll` | A ring of low land around a shallow lagoon, turtles and a sunken barrel |
| `archipelago` | Several islets on a shallow shelf; quest: a castaway's note and three caches |
| `stacks` | Steep rock pillars; quest: a chest on top of the tallest (build your way up), a screecher |
| `boss` | A plateau with cliffs and a ramp; walking into the arena wakes a boss bear (quest, big reward) |
| `volcano` | A tall volcano with embers, red light and dark smoke near the crater |
| `swamp` | Low land with pools, green mist and fireflies; quest: rats guard a stash |
| `spire` | A snowy island with one very tall peak, falling snow, a polar bear and a cache on top |
| `treasure` | A map in a bottle on the beach leads to the X and a treasure chest (quest) |
| `camp` | An abandoned camp: fire, hammock, flag, notice board and supplies (quest); a good first island of a story |
| `sunken` | An island under water: corals, sunken barrels, puffer fish and a turtle |
| `sky` | A small island floating 45–90 m up, with a cache |
| `wreck` | No land: an abandoned raft of Raft's blocks with barrels to loot |
| `tropical`, `snowy`, `desert`, `forest`, `volcanic`, `random` | A plain generated island of that style |

## Installing

Install RML, then put `DynamicIslands.rmod` in Raft's `mods` folder, or get the mod from raftmodding.com. Every player in a multiplayer game needs the mod.

Island files and settings live in `<Raft>\Mods\DynamicIslands\`:

| File | What it is |
|---|---|
| `*.island` | Saved islands. Share them by copying the file. |
| `spawnpool.txt` | Which islands appear on their own while sailing, how often, and when harvested objects grow back. It's created on first use and explains itself. |
| `worlds\<world id>.txt` | The custom islands in each world, with their harvested and picked-up objects, the world's plan and which of its rules have fired. |
| `plans\*.plan` | World plans (text). `plans\samples.txt` lists the samples the mod has written once. |
| `placeables_generated.txt` | The core objects the editor offers. Small indoor clutter is listed at the end, commented out. Copy the file to `placeables.txt` and edit it to choose your own list. |
| `catalog_index.txt` | Only after a Raft update: which of Raft's island scenes each of the other objects comes from, made by the editor (the mod ships one for the current Raft). Delete it to scan again. |
| `<name>_<hash>.island` | Islands downloaded from a multiplayer host. |

## The editor

The screen has a **top bar**, a **tool panel** on the left, the **object browser** on the right (Objects tab) and a **status bar** at the bottom. The status bar explains the current tool, or the button under the mouse. Related buttons sit together in bordered groups, and the active choice of a group is lit like Raft's chosen tab (the others are dark). Main buttons (Save, Done) are Raft's green craft button, deleting ones its red button.

| Where | Control | What it does |
|---|---|---|
| Top bar | **New** / **Open** / **Save** / **Save as** | New asks first (click twice), then starts an empty sea. Open and Save as open the Islands window: a name, a height (metres above sea in game: 0 = normal, 60 = flying, −30 = under water) and the saved islands (click = pick, double-click = open, Enter = save, Delete asks first). Save saves straight away once the island has a name. |
| Top bar | **Undo** / **Redo** | Undo / redo sculpting, painting, placing, moving, rotating, scaling, duplicating and deleting (also Ctrl+Z / Ctrl+Y) |
| Top bar | **Terrain** / **Objects** / **Island** (F1 / F2 / F3) | The three tabs |
| Top bar | **Generate** | The island generator: seed, style, layout, size, height, roughness, peaks and objects. Generating replaces the current island; Ctrl+Z brings the old one back. **Or a map type**: ◄ ► and **Make** (click twice) makes an island of that type from the seed, saves it as `gen-<type>-<seed>` and opens it. |
| Top bar | **World plans** | The world plan editor: pick a plan, New / Copy / Delete, Templates..., random islands on/off, description, the rule cards (when · bring what · where · message · Receiver name), Check, the map, Save |
| Terrain tab | **Sculpt** group | Raise / Lower / Flatten / Smooth. A ring shows the brush. |
| Terrain tab | **Paint ground** group | The style's four textures (named after it, with a colour swatch) and **Auto** |
| Terrain tab | **Brush** group | Size and strength |
| Objects tab | **Transform** group | Move / Turn / Scale / All (keys 1–4) |
| Objects tab | **Selection** group | What's selected; **Ground**, **Duplicate** (Ctrl+D), **Deselect**, **Delete** (Delete key) |
| Objects tab | **Placing** group | **Random**, **Slope** and **Grid** toggles |
| Objects tab | **Inspector** (one object selected; replaces Placing and the tips) | **Creature** group: animals here, presets, health / damage / speed / size, comes back after N days or never. **Note** group: title, a preview of the text, **Edit note...** (the note editor), **Remove**; for other objects, **Add a note to it...**. **Colour** group: None, swatches, strength, **Custom colour...** (red/green/blue). **Loot** group: the items with their amounts (× takes one out), **Add items...** (the item picker), **Empty**, the Basics / Metal / Food / Treasure sets, fills up again after N days or never. **Trigger zone** group: name, size, message, fires once or every time, and what it gives. A creature's **Appears** button chooses "at once" or "when a zone fires". **Atmosphere zone** group: size, fog colour and strength, light tint and strength, particles. **Sound zone** group: **Choose sound...** (Raft's sounds, with listening), ► / ■, volume, "While inside" or "Once on entering", size. Plain objects offer **Readable...** and **A chest...**. **Behaviour & events** group (every object): what it does, and **Behaviour & events...** (name, movement with Preview, hidden at first, players can use it, collision, "when … then" actions, "only if" checks, waits). Every change can be undone. |
| Objects tab | Object browser | Search box, then the categories. Click a category to open or close it. Click an object, then click the ground: Q/E turn, [ and ] resize, Shift+click keeps placing, Esc cancels. |
| Island tab | **Island** group | Style (◄ ►), height in the world with At sea / Flying / Sunken presets |
| Island tab | **Shown to players** group | The island's name, author and description, shown as a banner when players arrive in a world |
| Terrain tab | **Stamps** group | Hill, Peak, Crater, Mesa, Lagoon, Ridge and your saved stamps (click the ground; Size = how big, Q/E turn); **Save stamp...** |
| Objects tab | **Save as group...** (Selection) | The selected objects become a group under "My groups" (`DeleteGroup <name>` removes one) |
| Island tab | **Quest** group | What the island's quest is; **Edit quest...** opens the quest editor (steps, reward, messages, and "when the quest is done, bring a new island"); **Islands it brings...** edits all the island's rules in the plan editor's cards; **Island events...**: what happens when players first arrive and when the quest is done; **Story items...**: the island's story items and the story sets |
| Island tab | **Rules** group | Days until things come back on this island (empty = the world's `regrowDays`, 0 = never) |
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
| `DeleteGroup <name>` | Editor | Deletes a saved object group |
| `SpawnIsland <name> [distance] [height]` | Game, host | Spawns an island ahead of the raft (default 250 m), at its saved height or the given one. Warns if it would overlap one of Raft's own islands (players can fall through the ground there). |
| `SetElevation <m>` | Editor | Height above sea the island will have in game (saved with it) |
| `SetStyle <Tropical/Snowy/Desert/Forest/Volcanic>` | Editor | The island's style (ground textures; saved with it) |
| `RemoveIsland <name>` / `RemoveIsland all` | Game, host | Removes spawned islands |
| `ListSpawned` | Game | Lists the world's custom islands, with distance and state |
| `SpawnPool` | Game | Shows which islands appear on their own, and how often |
| `CustomIslandsAuto on` / `off` | Game, host | Turns automatic islands on or off for this world |
| `WorldPlan` / `WorldPlan <name>` | Game (changing: host) | Shows the world's plan and its rules (done or not), or gives the world another plan |
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
| `IslandInfo.cs` | Island name, author and description, the banner shown when players arrive (also zone messages), and the island rules |
| `LootCrate.cs`, `ItemPickerWindow.cs` | Chests in a world (giving items, looted state shared with all players and saved) and the item picker |
| `TriggerZone.cs` | Trigger zones in a world: message, items, waking up ambush creatures |
| `AmbienceZones.cs`, `SoundPickerWindow.cs` | Atmosphere zones (fog, light, particles, applied only while the camera renders) and sound zones (Raft's FMOD events), and the sound picker |
| `Quest.cs`, `QuestEditorWindow.cs` | Quests: the steps, progress shared by all players and saved with the world, the quest panel, and the quest editor |
| `GroupLibrary.cs`, `TerrainStamps.cs`, `TextPromptWindow.cs` | Object groups ("My groups"), terrain stamps, and the small name window they use |
| `TerrainPainter.cs` | Automatic and hand texture painting, island styles (which of Raft's ground textures fill the four paint slots) |
| `PlacementTools.cs`, `ObjectPlacer.cs` | Placing objects: placement options, Ground, Duplicate, picking objects with the mouse |
| `UIKit.cs` | The editor's look: Raft's menu sprites and fonts (found in memory at the main menu; rounded sprites made at runtime stand in without them), panels, groups, the button looks (plain, choice, primary, delete, slot), sliders, fields |
| `EditorUI.cs`, `ObjectBrowser.cs`, `ObjectThumbnails.cs` | The editor screen (top bar, tool panels, status bar), the object browser, and the object pictures |
| `IslandSpawner.cs` | Builds an island in a world: cropped terrain, textures, objects, network ids. For flying islands it also cuts terrain holes and adds the underside mesh |
| `IslandWorldState.cs` | The world's island list: saved per world, follows world shifts |
| `CustomIslandSpawner.cs` | Automatic islands while sailing (saved ones and newly generated ones), and loading/unloading islands by distance |
| `IslandRadar.cs` | Custom islands as dots on Raft's Receiver (Harmony postfix on `Reciever.HandleUI`) |
| `IslandObjectState.cs` | Harvested trees and picked-up items per island, and regrowing |
| `IslandNetwork.cs` | Multiplayer: island list, removals and island file transfer between host and clients, quests, used objects, announcements, object state and events |
| `PlaceableCatalog.cs` | The object catalog: the core objects (always loaded), Raft's buildable items, and the index of every other object of Raft's island scenes, loaded scene by scene when needed |
| `IslandGenerator.cs`, `GeneratorWindow.cs` | Procedural islands: heights from seeded noise in six layouts (round, atoll, archipelago, sea stacks, plateau, marsh), object scatter by zone, and the Generate window |
| `MapTypes.cs` | Map types: settings ranges, flying/sunken, and their content (`MapKit`: chests, notes, zones, creatures, atmosphere, quests) |
| `Behaviours.cs`, `BehaviourWindow.cs` | Behaviours and events: names, movement, "players can use it", collision, "when … then" actions, "only if" checks, waits, the shared clock for movers, their shared state and network messages; the Behaviour & events window |
| `StoryItems.cs`, `StoryItemsWindow.cs` | Story items (definitions, pictures), the crew's story book (items and journal pages: saved with the world, sent over the network), the journal window (J), the Story items window and the story sets |
| `WorldDirector.cs` | Rules (`IntroRule`), world plans (`WorldPlan`), the host's world director (conditions, placement, announcements, saved state), and the patch that keeps Raft's own islands off custom ones |
| `WorldPlanWindow.cs`, `ChoiceWindow.cs`, `NewWorldOptions.cs` | The world plan editor (also the island's rules) with templates, a list picker, and the plan choice in Raft's New Game box |
| `terraineditor.cs`, `TerrainPainter.cs`, `EditorTools.cs`, `EditorUI.cs`, `IslandFilesWindow.cs`, `ObjectPlacer.cs`, `RTSCamera.cs` | The editor |
| `RuntimeGizmo\`, `AwaitExtensions\` | Third-party move/rotate/scale gizmo and await helpers |

## Known limitations

- Multiplayer has only been tested on one PC (message serialization and a looped-back file transfer). It hasn't been tested in a real two-player session yet.
- Support for Unity-built `.assets` islands from version 2 was removed. Rebuild those islands in the editor.
- Reaching a flying island is up to the player: build stairs or pillars up from the raft. The editor shows islands at sea level; the height only applies in game.
- Creatures: catching with the net launcher, the host's creatures reaching a second player, players who join later, and the colour on other players' screens are built on Raft's own mechanisms, but they haven't been tested by hand or with two players yet. Raft has no pets, so "catchable" means Raft's domestic animals. The screecher's stone look can't be tinted.
- Behaviours: hiding a creature spot after its animals have appeared doesn't remove them. Actions waiting after a "wait" need the island to stay loaded: when the raft sails away (about 800 m) and it unloads, they are dropped. Item checks look at the inventory of the player who did it; for events no single player does (defeated animals, a finished quest) they look at the host's player.
- Story items live in the journal, not in Raft's inventory (Raft's own quest items are a fixed list, and new Raft items would break saves without the mod). Their ids are shared by all islands of a world: two islands that use the same id mean the same item.
- World plans and island rules are checked by the host only. They were tested with one player and looped-back network messages; the announcement and the Receiver names on a second player's screen haven't been seen by a person yet.
- The Receiver shows every custom island as a dot, however far; a plan with many islands fills the radar. Map type content is placed by the generator: a chest can end up in an awkward spot now and then (the Generate window's map types let you check and fix one before sharing it).
- An island has one quest. Quest steps find things by name (zone name, note title, creature kind), so renaming a note breaks a step that points at it.
- Atmosphere zones change Unity's fog and ambient light plus a faint screen tint; how strong the fog looks depends on Raft's own sky at that moment.
- An island file with creatures, notes, a tint or island info is format 4: older versions of the mod can't open it. Islands without these are still saved in the older formats.
- Objects from Raft's other islands and Raft's buildable items are decoration: their scripts are removed, so a chest doesn't store anything and a character doesn't move. Only the harvestable trees, rocks, ores and plants keep their gameplay.
- An island that uses objects from one of Raft's story islands (e.g. Utopia) makes the mod load that island's scene for a moment when the island spawns in a world, to copy the objects. The scene is switched off as it arrives; this can take a second.

## License

GNU AGPLv3
