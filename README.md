# Custom Islands (DynamicIslands)

A [Raft](https://raft-game.com/) mod for the [Raft Mod Loader](https://www.raftmodding.com/) (RML): build your own islands in an in-game editor, then find them in your worlds while sailing.

By FranzFischer78 (code) and MegaMatrixs (design). Version 3 was rebuilt for Raft 1.1 (Unity 2021.3) with SwedenJohansson.

> **EXPERIMENTAL ALPHA RELEASE** - this is the first release of Custom Islands (version 3.0), an early alpha. Things are likely to change, some
> systems might be unstable, and we do not guarantee that progress is always saved: back up the worlds you care about.
> The main menu shows this in a box (**Got it** folds it until the next version).

**New here? Start with the [illustrated guide](docs/GUIDE.md)** (also as a [PDF](docs/Custom-Islands-Guide.pdf) to download or print): installing, starting a world, what you meet while
sailing, building your own islands in the editor and giving them quests and stories, playing together, and at the end
the optional world systems (world rules, randomizer, extra options, the level up system), step by step with 57 pictures.
This README is the full reference, in the same order: island creation first, the optional systems after.

[![A custom island seen from the sea](docs/images/world-island-from-sea.jpg)](docs/GUIDE.md)

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
  - **Quests** (Island tab, **Edit quest...**): a title, an introduction, up to 10 steps in order (**go to** a trigger zone, **read** a note, **open** a chest, **defeat** or **catch** a number of animals, **collect** a number of a story item, **find** a number of journal pages on the island or anywhere), a reward and a closing message. Steps point at things on the island by name, and the editor lists the names it knows.
  - **Object groups:** select objects and click **Save as group...**. The group appears under "My groups" at the top of the object list, to place on any island as one piece. Once put down, it becomes its separate objects again, with their settings. Groups are files in `Mods\DynamicIslands\groups`.
  - **Terrain stamps** (Terrain tab): click to put down a Hill, Peak, Crater, Mesa, Lagoon or Ridge, as big as the brush; Q/E turn it. **Save stamp...** keeps the land under the brush as a stamp of your own (`Mods\DynamicIslands\stamps`).
  - **Behaviour & events** (select any object, "Behaviour & events..."), no code needed:
    - **a name** that actions refer to (objects with the same name act together)
    - **movement:** spin, bob up and down, or move and turn between two poses, either **back and forth** for ever or as a door, gate, bridge or lift that **opens and closes** (with a Preview in the editor)
    - **hidden at first** until an action shows it (a hidden creature spot is an ambush); hiding a creature spot again takes its animals away until it is shown
    - **players can use it:** Raft's "press E" hint with your own text ("Pull the lever")
    - **collision:** Raft's own, walk through, one box, or solid
    - **when … then:** a player uses it, walks into a zone, reads a note (first time), opens a chest, or all the animals of a creature spot are defeated → **show / hide / show-or-hide** objects, **open / close / open-or-close** doors, **say** a message, **give** items, **play** one of Raft's sounds, **teleport** the player to an object, **send a signal** (world plans and island rules can wait for it), write a **journal page**, or **wait** a number of seconds before the actions after it (a gate that falls shut again)
    - **only if …:** checks before the actions run: the player **has** an item or a story item, or **uses one up** (a key), an object **is** open / closed / shown / hidden, a **signal** was sent, the quest reached a step. Each check can be turned round (**not**: "the crew hasn't got the key yet"), and the checks can be **all of these** or **any of these**. **Otherwise** a message ("It's locked. Maybe there's a key somewhere...")
    - **Island events** (Island tab): when players first come to the island, and when its quest is done
    - **Locked chests:** a chest's "Only if..." decides whether it opens: without the key it says so and keeps its loot.
    - A door that is used without actions of its own opens and closes itself.
  - **Story items** (Island tab, **Story items...**): keys, map pieces, logs... with a name, a description and a picture (Raft's quest item pictures or any Raft item). Chests, zones, quest rewards and "give" actions hand them out (the item picker lists them first), and "only if" checks ask for them.
  - **Story sets** (in the Story items window): ready pieces of a story placed around the view in one undo step: **a locked door and its key** (in a chest with a note), **a trail of notes** that leads to a hidden chest with a story item, **a treasure map** (a message in a bottle gives the map; walking to the X with it digs up a buried chest), or **a locked chest** with driftwood nearby that hides its key.
  - **Invisible walls and ramps** ("Zones & triggers"): solid in a world but not seen. Block a path, keep players in an arena, or make a cliff or sea stack climbable. Scale and turn them to fit.
  - **Island rules** (Island tab): how many in-game days until chopped trees, picked items, killed or caught animals, looted chests and fired zones come back on this island (empty = the world's setting, 0 = never).
  - **Level up system** (Island tab, Rules: Off / On; also a **Level up** choice in the generator): an island with it turns levels on in the world it comes to. Players then earn EXP from monsters and spend stat points (see "The level up system" below).
  - Undo and redo everything, and save or load islands.
  - **Generate** an island to start from (see "The island generator" below): three tabs. **Normal**: seed, style, **layout** (round, atoll, archipelago, sea stacks, plateau, marsh, crescent, twin peaks), size and highest point with **Small island / Large island / Balboa** buttons measured from Raft's own islands, peaks and their shape, hills, coast, bays, beach, cliffs, stretch, valleys, lakes, terraces, erosion, **under water like Raft's own islands** (a deep sea floor 160 m down with a shelf and a drop-off, and corals, sea vines, rocks, stones, clay, scrap, ores, giant clams and sunken barrels placed from measurements of Raft's islands), a slider for each kind of object (trees, bushes, rocks, beach things, harvestables) up to a jungle you can barely walk through, **animals** (hostile, friendly, sea; which kinds, how tough) and **loot boxes** with tiers 1-5; your own saved presets. **Randomize existing**: pick one of Raft's 33 islands from its picture, then "something new like it" or "a variation of it" (its own ground, reshaped). **Ready-made**: the map types as cards. A live preview map shows the island as you move the sliders, and a line above the seed says **whether players can get onto it from their raft** (easy, by jumping onto a ledge, only by building); every setting has a "?" to hover for help. The same seed and settings always give the same island.
  - **Map types** (Generate window, **Ready-made** tab): whole islands with content, made from a seed and opened to edit: sandbar, atoll, archipelago, sea stacks, boss island, volcano, swamp, frozen spire, treasure island, old camp, sunken island, sky island and wreck (see "Map types" below).
  - **When the quest is done, bring a new island** (quest editor): a saved island or a new island of a map type, how far and which way from this island, a message for every player and a name on the Receiver. **Islands it brings...** (Island tab) edits all the island's rules: also "when step 2 is done", "when zone X fires", "when players first get here".
  - **World plans** (top bar): plans for new worlds, made of rules (see "World plans" below). A plan editor with rule cards, **Check** (finds rules that can't work) and a sketch of where islands go, plus ready-made templates.
- **In your worlds**
  - Islands appear on their own ahead of the raft while you sail. They're kept away from Raft's own islands, and far-away islands are unloaded to save memory.
  - Now and then a **brand-new random island** is generated instead: random size, style, and sometimes flying. It's saved as `gen-<style>-<seed>.island`, so it stays in that world. Set with `generated`, `generatedStyles` and `generatedFlyingChance` in `spawnpool.txt`. Lines like `type:wreck 0.4` mix in **map types** (sandbars, wrecks, atolls, sunken islands... see below).
  - **Custom islands show on Raft's Receiver** as green dots with their distance, so you can navigate to them. Turn this off with `showOnReceiver = 0`.
  - You can also spawn one yourself with `SpawnIsland`.
  - Islands are saved with the world. You can walk on them, the raft runs aground on them, and palms, mango trees, rocks and berry bushes can be harvested.
  - **Flying and underwater islands:** give an island a height in the Islands window, or with `SetElevation`, or when spawning. A flying island loses its seabed and gets a rocky underside, and the raft sails underneath it. An underwater island sits below the surface for divers.
  - Chopped trees and picked-up items stay that way, even after the island unloads or the world is reloaded. They grow back after 3 in-game days (set with `regrowDays` in `spawnpool.txt`; with several players, the host's number, and the host decides when: they come back when the island loads on the host after that many days).
  - **Creatures come alive:** the host spawns Raft's real animals at the island's creature spots with the builder's stats and colour. They roam around their spot, and Raft's own networking brings them to the other players. Killed and caught animals are remembered like harvested trees, and come back after `regrowDays` unless the builder turned that off. Caught animals become normal raft animals.
  - **Notes:** look at a readable object and press the interact key (E) to read it. Close it with E, Tab, Esc or the button.
  - **Chests:** look at one and press E: the items go into your inventory (what doesn't fit drops in front of you), and the chest is empty for everyone, also after reloading, until it fills up again. A chest with a note shows the note too.
  - **Trigger zones** fire for whoever walks in; an ambush creature appears the moment its zone fires.
  - **Quests:** near an island with a quest, a panel shows its steps (done ones ticked). The introduction shows when you arrive, and each step done shows what's next. The quest is shared by everyone in the world and saved with it. When it's done, every player near the island gets the reward's items (its story items go to the crew's journal once).
  - **Arriving** near an island that has a name or description shows it as a banner at the top of the screen, once per island per session.
  - **The journal** (J): the crew's story items with their pictures, and every custom note read (plus "journal" pages), on paper. Story items are held by the whole crew, like Raft's own quest items, saved with the world and sent to players who join.
  - **Behaviours:** doors, gates and lifts move for every player; things that move back and forth, spin or bob follow a clock all players share (Raft's water time), so everyone sees them in the same place; what is shown, hidden, open or closed is shared by all players, saved with the world and sent to players who join. Messages, items, sounds and teleports go to the player who did it (for defeated animals and finished quests: to everyone near the island).
  - **World plans** decide which islands a world gets (see below): chosen in Raft's **New Game** box ("Custom Islands plan"), or with `WorldPlan <name>` in a world. "Random islands" (the default) is the old behaviour.
  - **Rafts of blocks you can walk on:** Raft's building blocks on custom islands (wrecks, ghost rafts, a raft built in the editor) float like the player's raft, with the deck a player walks on (see [World settings](#world-settings-more-ways-to-play-raft-again)).
  - **New islands from rules:** when a rule brings an island (a quest done, a zone, a visit, km sailed...), every player sees a banner with the message and how far and which way it is, and the island's green dot on the Receiver carries its name.
- **Multiplayer (up to eight players, Raft's maximum):** the host's islands are sent to players who join, together with any island files they don't have and what has been harvested there. Harvesting and picking up items stay in sync. Only the host checks world plan and island rules; quests and zones done by other players count, because they reach the host. The World settings (world rules, randomizer, extra options, islands while sailing) are the host's for every player, also those who join later.
  - **Another player can host the world next time.** Raft keeps a world on the host's PC; copy its folder (`...\LocalLow\Redbeet Interactive\Raft\User\User_<Steam id>\World\<world name>`) to the next host's PC. The mod's state of the world goes along: the host writes it into that folder too (`CustomIslands.txt`), and every player keeps a copy of it (`worlds\<world id>.txt`, sent by the host each time Raft saves). When the world loads, the newest copy is read. Islands a player only has from joining are played from their downloaded copies (`<name>_<hash>.island`) under their own names, so quests, rules and journal pages that name them keep working. Everyone keeps their level, EXP and points, and the world keeps its World settings.
  - **One player gets a chest:** when several players open one chest (or walk into a zone that fires once) at the same moment, the host gives it to the first to ask; the others are told someone else got there first.
  - **Once is once for the crew:** a note read or an island reached runs its shared actions once, however many players do it together; a finished quest's checks are made once, by the host; story items from a quest's reward or a "give" action go to the crew once (Raft's items still go to every player near).
  - **A crew that splits up:** the host keeps an island loaded while any player is at it, so its animals, ambushes and doors keep working for players there while the host and the raft are far away.
  - **Everything the host decides is the same for every player, however they came in** (live, joining later, leaving and joining again, restarts), and never taken from a player's own files: the host's `spawnpool.txt` settings that change what players see (`regrowDays`, `showOnReceiver`, `unloadDistance`) go to every player with the world rules; **what grows back is the host's decision** (a player who has the island loaded sees it the next time it loads there); tough built creatures and the randomizer's alphas have the host's health on every player's screen, also for a player who joins mid-fight; only the host can change the host's settings (`Monsters`, `BuildCost`, `Randomizer`, `WorldPlan`, `CustomIslandsAuto`, `SpawnIsland`, `RemoveIsland` are refused for other players); a player's own last New Game choices only apply to worlds they make themselves.
- **Optional world systems**, separate from island building and off in a plain world (see the sections from [World settings](#world-settings-more-ways-to-play-raft-again) on):
  - **World settings window** (New Game box, **WORLD SETTINGS...**): Raft's New Game box stays as Raft made it; one window holds everything below that is chosen per world, with **Raft's own** to put it all back.
  - **Monster difficulty:** **Timid, Normal, Fierce, Savage or Nightmare**. Sharks and every other animal that fights players get ×0.75 to ×2 health and damage (see [World rules](#world-rules-monster-difficulty-and-build-cost)).
  - **Build cost:** everything in the build menu costs 0-100% more materials, always rounded up.
  - **World randomizer:** a normal Raft world that is different every time - animal and shark colours, rare alphas and a Big Bruce with trophy spoils, more animals, moved and extra loot, treasure hunts and camps on Raft's islands, oddity islands, large islands and boss lairs while sailing - without touching Raft's story (see [World randomizer](#world-randomizer)).
  - **Extra options:** scrambled blueprints (Raft's story blueprints on other story islands, never what the story needs), story islands in a new order (Utopia last), ghost rafts (abandoned rafts while sailing, large ones guarded by rats and screechers), private storages (a storage opens only for its builder).
  - **Islands while sailing:** which of your islands (and which map types and generated islands) turn up by chance in that world, from a list with tick boxes.
  - **The level up system** (switched on by an island made with it, not in the window): hitting a monster floats the EXP it gave over it (**+5 EXP**). An EXP bar styled like Raft's own sits above its thirst, hunger and health bars. Every level gives 2 stat points to spend on the **stats page** (**K**, the **Stats** button by the bar in the inventory, or a click on the level up box): walk, run and swim speed, jump height, damage, health, hunger, thirst and oxygen. Each point is +1%, and a stat takes at most 10. Other players see your level as a small **Lv 5** under your name (see [The level up system](#the-level-up-system)).
- **EXPERIMENTAL ALPHA RELEASE box** on Raft's main menu (top right): the mod's version and what an alpha means (things change, some systems may be unstable, progress isn't guaranteed to be saved). **Got it** folds it into a bar (its **Show** opens it again) until the next version of the mod (`notice.txt`).

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
| `<name>_<hash>.island` | Islands downloaded from a multiplayer host (also what a player hosts the world from later). |
| `worlds\<world id>.txt` of a world you joined | The host's copy of that world, kept so you can host it later (with its Raft folder copied to your PC). Raft's world folder has one too: `CustomIslands.txt`. |
| `world_rules.txt`, `randomizer.txt` | Your last World settings choices (monster difficulty, build cost, extra options, islands left out; the randomizer), the start for the next new world |
| `groups\`, `stamps\`, `generator_presets\` | Your saved object groups, terrain stamps and generator presets |
| `notice.txt` | The version of the mod whose alpha box you folded with **Got it** |

## The editor

**EDITOR** in the main menu (or `LoadEditor`) opens it. A loading box covers the screen until it is ready ("Opening the editor", then "Loading Raft's objects from its islands" with a bar): a few seconds the first time after starting Raft, less after that.

The screen has a **top bar**, a **tool panel** on the left (it scrolls when a tall inspector doesn't fit the screen), the **object browser** on the right (Objects tab) and a **status bar** at the bottom. The status bar explains the current tool, or the button under the mouse. Related buttons sit together in bordered groups, and the active choice of a group is lit like Raft's chosen tab (the others are dark). Main buttons (Save, Done) are Raft's green craft button, deleting ones its red button.

| Where | Control | What it does |
|---|---|---|
| Top bar | **New** / **Open** / **Save** / **Save as** | New asks first (click twice), then starts an empty sea. Open and Save as open the Islands window: a name, a height (metres above sea in game: 0 = normal, 60 = flying, −30 = under water) and the saved islands (click = pick, double-click = open, Enter = save, Delete asks first). Save saves straight away once the island has a name. |
| Top bar | **Undo** / **Redo** | Undo / redo sculpting, painting, placing, moving, rotating, scaling, duplicating and deleting (also Ctrl+Z / Ctrl+Y) |
| Top bar | **Terrain** / **Objects** / **Island** (F1 / F2 / F3) | The three tabs |
| Top bar | **Generate** | The island generator (see "The island generator"): tabs **Normal**, **Randomize existing** and **Ready-made (with content)**, a preview map, the seed, **Generate** (Enter) and **Close** (Esc). Generating replaces the current island; Ctrl+Z brings the old one back. On Ready-made, **Make** (click twice) makes an island of the chosen type from the seed, saves it as `gen-<type>-<seed>` and opens it. Every setting has a **?**: hover it (or click it) for a few sentences of help. |
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
| Island tab | **Rules** group | Days until things come back on this island (empty = the world's `regrowDays`, 0 = never); **Level up system** Off / On (see "The level up system") |
| Island tab | **Generate**, **About this island** | Opens the generator; object count, height and how many objects the list has |
| Keys | Ctrl+S / Ctrl+O | Save / open |
| Camera | | Like Unity's scene view: **right-drag** to look around (while held: **WASD** flies where you look, **Q/E** down and up, the **wheel** sets the flying speed); **WASD** or arrows alone move over the island at the same height; **middle-drag** pans (the ground follows the cursor); **Alt+left-drag** orbits around the selected objects, or the ground in the middle of the view; the **wheel** zooms towards whatever is under the cursor (never through it; not over a panel); **F** frames the selection, or the whole island when nothing is selected; **Shift** is three times faster. Moves are smoothed and faster high up; the camera stays above the ground but can dive under the sea. |

The blue plane is sea level. Anything below it is under water in game.

## The island generator

**Generate** (top bar, or the Island tab) opens the generator. The preview map on the right follows every change; the seed below it picks which island of all possible ones you get (the same seed with the same settings always gives exactly the same island). Every setting has a **?** to hover for help.

**Normal** tab (every setting; **My presets** keeps them under a name, without the seed, in `generator_presets\`):

| Group | Settings |
|---|---|
| Island | Style (ground textures, plants, animals: tropical, snowy, desert, forest, volcanic), layout (round, atoll, archipelago, sea stacks, plateau, marsh, crescent, twin peaks) |
| Size and height | Size (the land's width) and highest point (the top is exactly this high), each with **Small island / Large island / Balboa** buttons measured from Raft's islands; peaks; peak shape (full, rounded hills ... spires over flat lowland); hills (smooth ... rugged, ridged) |
| Coast and outline | Coast (smooth ... ragged, with points and islets), bays (coves ... deep inlets), beach width, cliffs (share of the coast that drops straight into deeper water), stretch (up to 3 times as long as wide, same area) and its direction |
| Land features | River valleys from the top to the sea, lakes (below sea level, so the sea fills them), terraces, erosion (raindrops wear gullies) |
| Under water | Sea floor: **deep, like Raft** (the island rises from a floor 160 m down: a shelf about 10 m deep, then a drop-off with spurs and gullies) or shallow (a flat seabed 20 m down, as before); width of the shallow water; drop-off (a long, gentle slope ... a sheer wall); seabed: sand, rocky, or a reef ring just under the surface |
| Nature | Trees, bushes and plants, rocks, beach things, harvestables (stone, clay, sand, berries or pineapples), groups (spread evenly ... groves and clearings, reefs), and quick buttons None / Sparse / Like Raft / Dense / Jungle |
| Life under water | Corals and plants (corals, sea vines with seaweed, kelp), rocks (boulders, rock formations on the drop-off), things to collect (stones, clay, sand, scrap, metal and copper ore, giant clams, silver algae), sunken barrels; quick buttons None / Sparse / **Like Raft** / Rich / Teeming. Placed like around Raft's own islands of the style (see "Under water, like Raft's islands"). |
| Animals | Hostile creature spots (the style's own animals, or the kinds you click), toughness (Easy / Normal / Hard / Boss), friendly animals to catch, sea creatures |
| Loot | Loot boxes, lowest and highest tier, in the open or hidden next to trees, bushes and rocks (about one in five lies sunken). Tier 1 planks, plastic, thatch, rope (a barrel or box) · 2 nails, stone, scrap, some food (a small chest) · 3 metal and copper ore, bolts, hinges (a crate) · 4 metal and copper ingots, circuit boards (a chest) · 5 titanium, explosive goo, batteries, good healing salves (a large chest). Only items your Raft has are used; every box can be changed afterwards. |

**Randomize existing**: Raft's islands with a picture each. **Something new like it** fills the settings in from the island's measurements (land size, highest point, peaks, slopes, coast, stretch, shallow water, style, how dense its trees, bushes, rocks and harvestables are) and makes a new shape; the Normal tab shows them to change. **A variation of it** starts from the island's own ground, measured from Raft: size, height, stretch and direction, mirror, roughen, coast wobble, peak shape, valleys, lakes, terraces, erosion and seabed change it.

**Ready-made (with content)**: the map types (see "Map types" above) as cards with a picture; **Make** creates one.

**Can players reach it?** Above the seed, a coloured line says whether a player arriving on a raft can get onto the island these settings make, worked out from the ground the generator makes (at the editor's resolution) with Raft's own player: walkable slopes up to 45°, steps of 0.3 m, a jump lifts the feet 1.23 m (jump speed 7, gravity 20), a swimmer floats with the feet about 1.5 m down (`CIPlayerJump`). Raft lets a player jump whenever they touch ground, even steep rock, so they can hop up a rock face: with Raft's own controller (`CIReachWorld`) a swimmer got onto ledges up to 1.2 m with one jump and up to 2.4 m by hopping on up the rock (the same from a raft's deck), never 2.7 m or more. **Easy** (beaches, walk to the top), **Reachable** (landing spots; how much of the land is walkable, with jumps, or needs building), **Possible but tricky** (only by jumping out of the sea or up from the shallows onto a ledge up to 1.2 m), **Possible but unlikely** (only by hopping up the rock to a ledge up to 2.4 m, extremely tricky), **Not from the raft without building** (cliffs all around; stairs, a ladder or foundations up the cliff). Flying ready-made islands need building up; sunken ones diving.

### On the land, like Raft's islands

`CIMeasureLand` measured where Raft puts every tree, bush, rock, harvestable and log on the land of its natural islands (`raft_land.txt`, shipped with the mod): how far inland from the coast, how steep, how high above the sea, on which ground texture, how big, how deep in the ground and how far apart. What it found, and what the generator now does:

- **The beach is nearly bare.** On Raft's tropical islands only bamboo grows there (on sand, 0.4-2.4 m above the sea, 2-9 m inland). Palms stand at least ~7 m inland and 3 m up, the big palms 13 m and more, mango trees far inland on flat ground; forest trees are 17 m and more inland. All of them, and every bush and flower, stand on grass (96-100 %), on slopes mostly under 30°.
- **Rocks** are what Raft has on steep ground: big boulders on 35-50° slopes, sunk about a third of their size into the ground.
- The generator sorts the island's land into the same habitats (distance inland x slope x height), places each kind of thing in them as densely as Raft's islands of the style have it there, picks the kinds as Raft mixes them in that habitat (bamboo by the beach, palms inland), keeps each kind within where it usually stands (no steeper than usual, not on the beach if it keeps off it, and on the ground texture it grows on - not on the rock the painting puts on slopes over about 35°), and sinks rocks as Raft does. `CILandLikeRaft` checks it against the measurement. Where Raft's islands of a style have too few of a kind to measure (harvestables on the forest and desert islands, snowy bushes) and for beach things, simple rules decide (only stones, clay and sand on the beach).

### Under water, like Raft's islands

`CIMeasureUnderwater` measured what lies under water around each of Raft's islands (`raft_underwater.txt`, shipped with the mod): every object by name with its depth, distance from the coast, slope under it, size and how deep it sits in the ground, the ground's depth profile and its textures by depth.

- **The sea floor.** Raft's islands rise from a floor about 150-165 m down: a shelf that deepens to about 10 m some 20-30 m out, then a drop-off (a long slope around the big islands, a near wall around the small ones). Generated islands do the same on the **deep sea floor** (the island's file has its sea 160 m above the terrain's base; older and hand-made islands keep the shallow 20 m seabed, and **New** starts one). In a world the ground is kept down to 110 m, where it is dark.
- **The life under water**, per depth band (0-2, 2-5, 5-10, 10-20, 20-40, 40-80, 80+ m), each kind as dense as around Raft's islands of the style: tropical (Raft's small and big islands) about 80 corals and plants per 1000 m² down to 40 m, mostly 4-25 m deep, reefs with sand between them, sea vines with seaweed to pick, tall kelp further down; stones, clay, sand and scrap on the shelf, metal and copper ore on the steep slopes, giant clams and silver algae now and then; boulders near the shore (only there do rocks break the surface) and big rock formations sunk into the drop-off. Forest (Balboa): sunken barrels, containers and buoys, ores and scrap, no corals. Desert (Caravan): sea vines and a few corals. Snowy (Temperance): bare rock (the finds are borrowed from the tropical islands). Volcanic: like the desert.
- **The ground's textures** follow Raft's: sand on the shelf, rock taking over below 10 m (a quarter at 10-20 m, over half at 20-40 m, most below 40 m) and on the steepest slopes, in patches.

### Raft's islands, measured

`CIMeasureIslands` loads each of Raft's island scenes (switched off, so none of its scripts run), copies it without scripts into a scene with its own physics, and measures the ground on a 2 m grid (raycasts), counts objects by kind, and renders its picture. Land = ground above the sea; radius = of a round island as big.

| Islands | Land (length x width) | Land radius | Highest point | Shallow water to (from the middle) |
|---|---|---|---|---|
| Small islands 1-10 (pooled) | 24-58 m x 14-48 m | 9-25 m, average **16 m** | 5-24 m, average **12 m** | 24-49 m |
| Big islands (OG, Cresent, Twin peak, Big) | 212-241 m x 140-212 m | 77-110 m, average **89 m** | 38-123 m, median **49 m** (Twin peak 123 m) | 99-127 m |
| Balboa Island | 481 x 454 m | **226 m** | **151 m** | 239 m |
| Balboa small 1-3 | 155-197 m x 109-148 m | 61-83 m | 23-41 m | 91-100 m |
| Caravan Island / small 1-3 | 173 x 145 m / 88-107 m x 59-70 m | 64 m / 38-41 m | 41 m / 16-36 m | 95 m / 40-81 m |
| Temperance / small 1-3 | 1074 x 926 m / 317-461 m x 188-417 m | 457 m / 118-203 m | 101 m / 30-46 m | 459 m / 118-204 m |

Raft's big islands carry about 45 objects per 1000 m² of land (OG: 11 trees, 12 bushes, 11 rocks, 10 harvestables), which the Nature sliders reach at about a third ("Like Raft"). Temperance is bigger than the 1000 m build area: its variations are scaled down to fit; Balboa fits.

### What dense islands cost

Measured with `CIGenBench` (editor) and `CIGenWorld` (a world, one PC; frame times include vsync at 60 Hz):

| Objects (land and under water) | Generate | Editor frame | File | Save / load | Median free walk (walks blocked within 5 m) |
|---|---|---|---|---|---|
| 1 771 | 1.3 s | 17 ms | 514 KB | 0.3 s / 0.3 s | 20 m (5 %) |
| 3 609 | 1.4 s | 17 ms | 560 KB | 0.3 s / 0.4 s | 20 m (19 %) |
| 6 551 | 1.5 s | 17 ms | 633 KB | 0.4 s / 0.6 s | 7.5 m (36 %) |
| 10 417 (a jungle, 220 m island) | 1.9 s | 19 ms | 729 KB | 0.4 s / 0.9 s | **4.5 m (53 %): barely walkable** |
| 11 501 (a Balboa-sized jungle, thinned to the cap) | 1.9 s | 21 ms | 896 KB | 0.4 s / 0.9 s | 18.5 m (16 %) |

(Measured again 2026-09-26, with the deep sea floor and the life under water: the same settings now also place Raft's reefs, rocks and pickups around the island, and the files keep ground 160 m deep.)

In a world, a 5 681-object jungle spawned in 0.6 s and its creatures' NavMesh built in 1.4 s; frame time went from 17 ms to 27 ms with the whole island in view 330 m away and 19 ms standing in it (9 000 objects: 23 ms / 18 ms). A second player received both islands complete in 7-12 s. One island gets at most 12 000 objects (denser settings are thinned, and the window says so).

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
| `ghostraft` | No land: an abandoned raft of Raft's blocks with loot and a note - small, medium, or large with huts and a lookout, guarded by rats and screechers (the extra option Ghost rafts brings them while sailing) |
| `tropical`, `snowy`, `desert`, `forest`, `volcanic`, `random` | A plain generated island of that style |
| `oddity` | A small island with something odd on it: one of the eight below, picked at random |
| `van`, `caravan`, `planecrash`, `boatwreck`, `shack`, `statue`, `rocket`, `hut` | A small island with a van, a caravan and tent, a crashed plane, a boat run aground, a hermit's shack (and hens), a statue with an offering chest, smoking rocket debris, or a hut of raft blocks; each with loot and a note (set pieces from Raft's other islands) |
| `lair` | A boss lair: a plateau where a named beast (Old Ironhide, Frostfang, Ashmaw, the Tusk King, the Laughing One: 6× health, 2.5× damage, twice the size) and two guards wake when you reach the top; a hoard and a trophy chest with the beast's head (quest; much harder than `boss`) |
| `large` | A large island made like Raft's big ones (80-110 m, 35-60 m high, trees and plants as dense as theirs): three warthog spots, four spots of animals to catch for the raft, puffer fish around it, a screecher, hidden loot boxes, a made-up name; one or two scenes from the quest islands, and a den (one of Balboa's cave outcrops) with a guard and a hoard |

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

## World settings: more ways to play Raft again

*This and the next sections (world rules, the world randomizer, the level up system) describe the optional systems: separate from island building, chosen per world, and off in a world where they are left alone.*

Raft's **New Game** box stays as Raft made it, with the mod's **Custom Islands plan** and a **WORLD SETTINGS...** button
at its bottom right (the button says how many settings differ from plain Raft). The button opens one window with
everything else, grouped: the **world rules** (monster difficulty, build cost), the **world randomizer** (its level and
parts) and the **extra options** below, each switched on and off with its own button and explained under it.
**Raft's own** puts all of them back to plain Raft. The last choice is remembered for the next world. Every player in
the world gets the host's settings, also players who join later or again.

**Islands while sailing** (the fourth group): **CHOOSE ISLANDS...** lists everything that can turn up by chance while
sailing - every saved or downloaded island, brand-new generated islands and the map types of `spawnpool.txt` - each with
a tick box. Untick what the world shouldn't have; a search field and **Tick shown** / **Untick shown** make that quick
with many islands. Only what is left out is kept (the world file's `@islandsoff=` line), so islands made later join
older worlds too; the host can change it in a world with `WorldIslands`. It counts with plans that use random islands
(Random islands, or a plan with random islands on).

- **Scrambled blueprints:** the blueprints lying on Raft's story islands are found on other story islands than usual.
  Each one is paired with another from the world's seed, never with itself. What the story needs is never moved - the
  Receiver and antenna, the steering wheel, the engine and its fuel, the machete, the zipline and the headlight - so the
  story can always be finished. Only what a pickup gives changes: its name says what you'll get.
- **Story islands in a new order:** Radio Tower, Vasagatan, Balboa, Caravan Town, Tangaroa, Varuna Point and Temperance
  come in a shuffled order (from the world's seed; Utopia, the ending, stays last). Raft unlocks each frequency with a note
  - the Receiver's first, then the one found on each island for the next - and with this option each note unlocks the
  island in the new order's place. The frequency numbers written on the notes follow. Each story island carries what it
  needs (its keys and parts), so any order can be finished.
- **Ghost rafts:** abandoned rafts of Raft's blocks lie on the sea and come up ahead while sailing (not in the first
  1.5 km, then about one per 3 km). Small ones (a few foundations, a barrel, a message in a bottle), medium ones (a hut, a
  barrel and a box, a captain's log, sometimes a rat or two) and now and then a large one: a wide raft with huts and a
  lookout, a hoard chest and barrels, guarded by rats on its deck and screechers circling above.
- **Private storages:** a storage opens only for the player who built it; looking at someone else's shows whose it is.
  Storages built while the option was off open for everyone.
- The host can change them in a world: `WorldOptions` shows them; `WorldOptions +ghostrafts -privatestorage` (any
  option: blueprints, storyorder, ghostrafts, privatestorage).
- Multiplayer: the options, their seed and the storages' builders are in the world file and sent to every player
  (network kind 17). The story order and the blueprints' pairs are worked out from the seed on every machine, and each
  machine notes who built a storage as Raft places it there.
- Raft's building blocks on custom islands (ghost rafts, wrecks, a raft built in the editor) float like the player's raft - a foundation 0.13 m above the sea, measured - and each foundation has a deck a player walks on, 0.22 m above it, as Raft gives its own raft (Raft adds that deck only to the player's raft: before, players fell through a raft of blocks and it sat under the waterline).

## World rules: monster difficulty and build cost

Two sliders in the **World settings** window (Raft's **New Game** box, **WORLD SETTINGS...**) set rules for the new world: the **Monster difficulty** and the **Build cost**. The text under each slider says what the chosen setting does, and each has a **?** with the details. Your last choices are kept for the next new world (`Mods\DynamicIslands\world_rules.txt`).

**Every player in a world has the same rules as the host.** They're saved with the world (`@monsters=`, `@buildcost=` in its file) and sent to every player who joins, and again to everyone when the host changes one. Leaving and joining again, loading the world again and restarting Raft change nothing, and every new world gets its own rules from the box.

### Monster difficulty

How tough a world's monsters are.

| Level | Monsters' health | Damage they deal to players | |
|---|---|---|---|
| **Timid** | ×0.75 (25% less) | ×0.75 | A gentler trip across the sea |
| **Normal** | ×1 | ×1 | As Raft made them (the default) |
| **Fierce** | ×1.25 (25% more) | ×1.25 | |
| **Savage** | ×1.5 (50% more) | ×1.5 | Keep your spear close |
| **Nightmare** | ×2 (twice) | ×2 | For seasoned survivors |

These are the Easy / Normal / Moderate / Hard / Impossible levels, named so they don't get mixed up with Raft's own Easy / Normal / Hard game modes. `Monsters easy`, `moderate`, `hard` and `impossible` still work.

- **Monsters** are the animals that fight players, on Raft's own islands and on custom ones: Bruce and the other sharks, warthogs, bears, mama bears, polar bears, screechers, puffer fish, rats, hyenas, bee swarms, angler fish, the butler bots and the bosses. Llamas, goats, chickens, pigs, seagulls and the sea life that only swims by don't change.
- **It stacks** with Raft's game mode and with what an island's builder gave an animal. For example, a warthog its builder gave ×2 health has ×3 Raft's health on Savage.
- **How it works:** every hit in Raft passes through one place (`Network_Host.DamageEntity`) on the machine where it happens. A monster's hit on a player is multiplied by the level's number, and every hit on a monster is divided by it, which works the same as giving it that much more health. Hits of 9999 or more (Raft's "kill it" values) are left alone.
- **Left as Raft has them:** puffer fish damage (their explosion, their cloud and the poison it leaves; only their health changes), and everything about Bruce and your raft: his bites on it, how often he comes for it and how soon a killed shark comes back. Also how fast monsters move, how far they see and how often land monsters attack: Raft's own difficulty doesn't change these either, and faster animals would trip up Raft's animations and paths.
- **Peaceful and Creative:** monsters can't hurt players there, so only their health changes, and the box says so.
- **For every player:** a guest's spear does the same to a shark as the host's, and a monster bites a guest as hard as the host.
- **Changing it later:** `Monsters` shows the world's level. The host can change it with `Monsters timid|normal|fierce|savage|nightmare`. At the main menu, `Monsters <level>` sets the choice for the next new world.

### Build cost

How many more materials everything in the **build menu** costs: the hammer's foundations, floors, walls, roofs, stairs, pillars and the rest. The slider goes from **0%** (Raft's own cost, the default) to **100%** (twice as much) in steps of 5.

- **Always rounded up:** at 50% one plank becomes two, two become three, three become five.
- **Removing a block** with the hammer gives back half of what it cost, as Raft does. At 50% a block that cost 3 planks gives 2 back.
- **Repairing and reinforcing** blocks cost more too, since they use the same cost list. What you make in the crafting menu (Tab) costs the same as in Raft.
- **For every player:** each player pays from their own inventory on their own machine, so every player who joins gets the host's percent.
- **Never on top:** the mod sets the numbers from Raft's own each time, and puts Raft's own back as soon as you leave a world. Leaving and joining again, loading again or changing the percent several times never makes building dearer than the percent says, and the main menu, the editor and the next world start from Raft's numbers.
- **Changing it later:** `BuildCost` shows the world's percent. The host can change it with `BuildCost <0-100>`. At the main menu, `BuildCost <0-100>` sets the choice for the next new world.

## World randomizer

Makes a **normal Raft world** play out differently every time: with or without custom islands, and without touching Raft's story. Choose it in the **World settings** window of Raft's **New Game box** under "World randomizer": **Off**, **Light**, **Normal** or **Wild** (how much is different), and which parts take part (click a part to switch it off). The last choice is remembered for the next world. Each world gets its own seed; everything follows from it.

| Part | What changes |
|---|---|
| **Colours** | Animals and sharks now and then have another colour: charcoal, ash, umber, rust, sand, moss, frost, night; birds crimson, slate or gold; sharks (Bruce too) midnight, tiger, rust, reef, olive, and rarely gold. Light: about 1 in 7, Normal 1 in 3, Wild every other one |
| **Animals** | Raft's islands that have animals get more of them near where Raft has them; islands without animals sometimes get a few (warthogs, chickens, goats, llamas; on Wild a bear); now and then puffer fish guard the reef |
| **Alphas** | Rare alpha animals (warthogs, pigs, bears, polar bears, hyenas, screechers): bigger, darker, 3× health, 1.6× damage. A huge dark **Big Bruce** now and then (2.5× health). A banner warns nearby players. Killed, they drop a trophy head (Head_Boar, Head_Bear, Head_Shark...), meat and leather, and sometimes titanium |
| **Loot** | Some of the crates and giant clams on Raft's islands lie somewhere else (on land stays on land, under water at a similar depth); islands sometimes get extra crates, barrels and chests, and sunken barrels on the reef |
| **Finds** | Now and then an island hides a **treasure hunt** (a map in a bottle on the beach, a chest buried at the island's highest point), an **abandoned camp** (big islands) or a **castaway's stash** by a tree, with a note and a quest. Raft's big islands now and then get a **den** (one of Balboa's cave outcrops, on a level spot clear of Raft's trees and rocks, its mouth towards the coast: a guard wakes when you walk in, a hoard at the back) and an **outpost** (a scene from a quest island) |
| **Oddities** | While sailing, small **oddity islands** appear (the map types above: a van, a caravan, a crashed plane...) |
| **Bosses** | Now and then (after the first few km) a **boss lair** appears |
| **Large** | Now and then (after the first few km) a **large island** like Raft's big ones appears (the `large` map type above) |

**Made to fit in Raft's world.** The randomizer's islands are made like Raft's own: the oddities, lairs and large islands
have trees, bushes, rocks and harvestables as dense as Raft's islands of that size (an oddity as dense as Raft's small
islands, a large one as its big ones), and trees to cut as Raft has them (2-6 palms on a small tropical island, about one per
1000 m² on a big one; pines and birches on forest ones, mango trees on desert ones - some of the island's own bamboo and big
palms become them, where they stand), in the places Raft puts them, with Raft's pickable flowers, pineapples,
watermelons and mango and banana trees on tropical ones. What makes them new comes from Raft's own quest islands, as
**scenes**: a scrapyard (Varuna Point's excavator, forklift, dumpsters and concrete pipes), an old market (Utopia's baskets
and crates under Tangaroa's sunshade), a caravan outpost (Caravan Town), bear country (Balboa's fences, bear signs and
couch, a bear nearby), a frozen camp (Temperance's igloos, on snowy islands), a hotel garden (Tangaroa's plants), a radio
outpost (the radio tower's windmill and dish) and a castaways' camp (the floating rafts'), each with a chest and a note;
and **dens**: Balboa's bear cave and dead end, whole rock outcrops with a den about 30 m deep at Raft's own size (the bear
cave is scaled x3 in Raft's scene), placed on levelled ground (generated islands) or a spot clear of Raft's things with the
den's floor on its highest ground and the mouth turned to open ground (Raft's islands). Raft's pickups and animals inside a
copied piece (Balboa's cave holds the Machete blueprint, Mama Bear's pickup and a bear) are left out: nothing of Raft's
story comes along. Every prop's size and footprint is measured
(`raft_props.txt`, `CIMeasureProps`): props stand on the lowest ground under them, and nothing that hangs from its top
(awnings, banners, dream catchers) is used.

- **Raft's story is safe:** only Raft's plain islands (the big and small tropical ones and the small islands around the story places) are changed; the story islands, the stranded boat, the pilot's island and floating rafts are left alone, and so are bosses, people and bees.
- **Multiplayer:** colours, alphas and moved crates are worked out on every machine from the world's seed and Raft's own network ids, so every player sees the same without extra messages. What is added to Raft's islands is a land-less custom island laid over Raft's island (its "extras", `rnd-<seed>-<id>.island`): it is saved, sent to other players, and its chests, animals and quests work like any custom island's. Raft saves picked-up crates by the place it put them; for moved crates the mod finds them by that place again, so a used crate stays used after loading and for players who join.
- **Changing it later:** `Randomizer` shows what it does in the current world; the host can change it: `Randomizer off|light|normal|wild`, `Randomizer -alphas` / `+alphas` (any part). Islands that were already looked at keep what they got.
- Raft switches the ground of its far islands off (about 1 km away), so an island is randomized when the raft comes within about a kilometre.

## The level up system

An island made with **Level up system: On** (Island tab, Rules; or the generator's **Level up** choice, on all three tabs) turns levels on in any world it comes to. A banner says so, and from then on it stays on in that world. It is off in every world without such an island.

- **EXP:** every hit a player makes on a monster gives EXP, anywhere in that world: the island's animals, Bruce and Raft's own animals. The number floats up over the monster for a moment (**+5 EXP**), and the level bar lights up. A hit gives the monster's EXP times the share of its health it took, so a kill gives exactly the monster's EXP. If two players fight it together, each gets their share. Animals that aren't monsters give nothing: chickens, goats, llamas, turtles, stingrays, dolphins, whales and people. The stats page counts the monsters each player has defeated (the killing hit counts).
- **What a monster is worth:** half from how tough it is, half from how hard it hits, both compared with Bruce (Raft's shark), who is worth **20 EXP**: `EXP = 20 × (0.5 × health / Bruce's health + 0.5 × damage / Bruce's damage)`, at least 1. Health and damage are the animal's own, so the editor's Easy / Hard / Boss settings count too (a Boss warthog is worth more). `CILevelTable` lists every monster's numbers.
- **Levels:**

  | From level | To level | EXP needed | About (kills of Bruce) | EXP in all |
  |---|---|---|---|---|
  | 1 | 2 | 100 | 5 | 100 |
  | 2 | 3 | 200 | 10 | 300 |
  | 3 | 4 | 400 | 20 | 700 |
  | 4 | 5 | 600 | 30 | 1300 |
  | 5 | 6 | 800 | 40 | 2100 |
  | *n* | *n*+1 | 200 × (*n* − 1) | 10 × (*n* − 1) | |

- **Stat points:** every level gives **2 points**. Spend them on the **stats page**. The page shows the level, the EXP bar, the monsters defeated and nine stats with ten pips each. **+** puts a point in, and **−** takes one back as long as the page is open. Each point is **+1%**, and a stat takes at most **10 points** (+10%). All 90 points are there at level 46. **The levels go on after that**, without points: the level up box says every stat is full.

  | Stat | What a point does |
  |---|---|
  | Walk speed / Run speed / Swim speed | 1% faster on land, sprinting, swimming and diving |
  | Jump height | Jumps 1% higher (on land and out of the water) |
  | Damage | 1% more damage to monsters with every weapon (melee, spears, arrows, thrown stones) |
  | Health | 1% more maximum health |
  | Hunger | Hunger drains 1% slower, so you go longer without eating |
  | Thirst | Thirst drains 1% slower, so you go longer without drinking |
  | Oxygen | Breath runs out 1% slower, so you last longer under water |

- **On the screen:**
  - **The EXP bar** is styled exactly like Raft's own stat bars and sits one row above them (thirst, hunger, health): it is a copy of Raft's hunger bar with a **star icon** (drawn like Raft's stat icons) on its badge, a gold fill, the level on the left (a **+** when points are waiting) and the EXP to the next level on the right. It glows (Raft's own blink glow) when EXP comes in. It is part of Raft's HUD, so it hides with it. (Should a Raft update change its bars, a plain bar of the mod's own is used instead.)
  - **The stats page** opens with **K**, with the **Stats** button that shows next to the level bar while Raft's inventory (Tab) is open, or by clicking the level up box. Esc or K closes it.
  - **The level up box** ("LEVEL 3! +2 stat points") stays for about 12 seconds. Click it while the mouse is free (in a menu) to open the stats page.
  - **Other players' levels:** a small gold **Lv 5** under the name Raft shows over each other player, nothing more. The host sends every player's level to everyone when it changes and when someone joins.
- **Saved with the world** for every player (`@levels=on` and `@level=<steam id>|<exp>|<points>|<monsters defeated>` in the world's island file). A player who joins, or joins again, gets their own level back from the host, also after both Rafts restart. Players earn EXP and spend points on their own machine, and their record goes to the host.
- **Multiplayer: the same for everyone.** Whether the system is on belongs to the world, and every player gets it when they join or join again. **The host works out every player's EXP:** it has every monster as it was built (the editor's toughness, the randomizer's alphas), so a monster is worth the same to everyone. A player's hit reaches the host as Raft's own damage message, and the EXP goes back to that player. A player's Health points count on every machine, so the host's copy of that player, which monsters bite, has the same maximum health. Each player earns and spends on their own stats page; their record goes to the host, which saves it and sends it back when they join again.
- **How it works in Raft:** the speeds and the jump are raised only while Raft moves the player (`PersonController.GroundControll` / `WaterControll`), so Raft's flippers and other changes stay as they are. Damage and EXP come from `Network_Host.DamageEntity`, which runs on the attacking player's own machine for every weapon. Health is the player's maximum health. The level bar is a child of Raft's `CanvasHelper.StatSliderParent`, and the level under a name a small `TextMesh` under `Network_Player.playerNameTextMesh`. Hunger and thirst go through `Stat_Consumable.LostPerSecond`, and oxygen through `Stat_Oxygen.Update`.

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
| `Monsters` / `Monsters <timid/normal/fierce/savage/nightmare>` | Game (changing: host); main menu | Shows the world's monster difficulty, or changes it. At the main menu it sets the choice for the next new world |
| `BuildCost` / `BuildCost <0-100>` | Game (changing: host); main menu | Shows how many % more the build menu costs in this world, or changes it. At the main menu it sets the choice for the next new world |
| `CustomIslandsAuto on` / `off` | Game, host | Turns automatic islands on or off for this world |
| `WorldPlan` / `WorldPlan <name>` | Game (changing: host) | Shows the world's plan and its rules (done or not), or gives the world another plan |
| `Randomizer` / `Randomizer <off/light/normal/wild> [-part] [+part]` | Game (changing: host) | Shows what the world randomizer does in this world, or changes it (parts: colours, animals, alphas, loot, finds, oddities, bosses) |
| `WorldOptions` / `WorldOptions +option -option` | Game (changing: host) | Shows the world's World settings, or changes them for every player (options: blueprints, storyorder, ghostrafts, privatestorage) |
| `WorldIslands` / `WorldIslands -<island>` / `+<island>` / `all` | Game (changing: host) | Which islands of the spawn pool turn up by chance while sailing in this world; leave one out, let it take part again (also `type:<map type>`, `<generated>`), or all |
| `SetToRaise`, `SetToLower`, `SetToFlatten`, `SetToSmooth`, `ChangeWidth <m>`, `ChangeStrength <m/s>`, `PaintTexture <sand/grass/rock/seabed>`, `SetToAutoPaint` | Editor | The terrain brush settings from the Terrain tab |

Development builds also include `CI*` test commands (`DevTests*.cs`); release builds leave them out.

## Documentation

The mod's documentation is kept up to date with the mod: **every change that adds or changes something a player or an
island builder sees updates it in the same commit.**

| File | For | Kept up to date |
|---|---|---|
| `docs/GUIDE.md` | Players and island builders: the illustrated guide, in the order they meet things (installing, sailing, the editor, stories, playing together; the optional world systems at the end) | Every new or changed feature gets its place in the matching section, in the same detail as the rest (what it does, where to find it, what players see, multiplayer, the console command), with a new picture where one helps (`docs/images`) |
| `docs/Custom-Islands-Guide.pdf` | The guide to download or print | Made again from `GUIDE.md` after every change to it (Node and Microsoft Edge: `guidepdf.ps1` in the project's tools) |
| `README.md` | The full reference: every feature, file, setting and command | The Features list, the section of the feature, the file table (Installing) and the console commands |

The guide and this README follow the same order: island creation, quests and stories first; the optional world systems
(world rules, the world randomizer, the extra options, the islands while sailing, the level up system) after them.

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
| `CreatureSpawner.cs` | Live creatures in a world: runtime NavMesh, spawning through Raft's `Network_Host_Entities`, stats, tint, killed/caught state, removal with the island, and Harmony patches for speed and players who join; on other players' machines the tint and the builder's health (`MatchHealth`, with the health Raft sent) |
| `MonsterDifficulty.cs` | The monster difficulty: the five levels, the Harmony prefix on Raft's `Network_Host.DamageEntity` that changes hits on and by monsters, the patch that leaves puffer fish damage alone, the `Monsters` command |
| `WorldRules.cs` | The world rules: saved with the world, sent to every player who joins (one network message with both, and the host's `spawnpool.txt` settings every player uses: Receiver, unload distance, regrow days), the last choices (`world_rules.txt`); the build cost (Raft's build menu items, their cost amounts set from Raft's own numbers while a world is open and put back outside, the `BuildCost` command); the two sliders in Raft's New Game box (which grows the box to make room) |
| `CustomNote.cs` | Readable notes in a world (Raft's interaction, `IRaycastable`) and the note reader |
| `IslandInfo.cs` | Island name, author and description, the banner shown when players arrive (also zone messages), and the island rules |
| `LootCrate.cs`, `ItemPickerWindow.cs` | Chests in a world (giving items, looted state shared with all players and saved) and the item picker |
| `TriggerZone.cs` | Trigger zones in a world: message, items, waking up ambush creatures |
| `AmbienceZones.cs`, `SoundPickerWindow.cs` | Atmosphere zones (fog, light, particles, applied only while the camera renders) and sound zones (Raft's FMOD events), and the sound picker |
| `Quest.cs`, `QuestEditorWindow.cs` | Quests: the steps, progress shared by all players and saved with the world, the quest panel, and the quest editor |
| `GroupLibrary.cs`, `TerrainStamps.cs`, `TextPromptWindow.cs` | Object groups ("My groups"), terrain stamps, and the small name window they use |
| `TerrainPainter.cs` | Automatic and hand texture painting, island styles (which of Raft's ground textures fill the four paint slots) |
| `PlacementTools.cs`, `ObjectPlacer.cs` | Placing objects: placement options, Ground, Duplicate, picking objects with the mouse |
| `UIKit.cs` | The editor's look: Raft's menu sprites and fonts (found in memory at the main menu; rounded sprites made at runtime stand in without them), panels, groups, the button looks (plain, choice, primary, delete, slot), sliders, fields, tabs, pictures, the "?" help marks and their popup, and the scrolling panel the tool panel uses |
| `EditorUI.cs`, `ObjectBrowser.cs`, `ObjectThumbnails.cs` | The editor screen (top bar, tool panels, status bar), the object browser, and the object pictures |
| `IslandSpawner.cs` | Builds an island in a world: cropped terrain, textures, objects, network ids. For flying islands it also cuts terrain holes and adds the underside mesh |
| `IslandWorldState.cs` | The world's island list: saved per world, follows world shifts |
| `CustomIslandSpawner.cs` | Automatic islands while sailing (saved ones and newly generated ones), and loading/unloading islands by distance (from the raft and the player) |
| `PlayerHold.cs` | A player who comes back to a world standing on a custom island is held there until it has loaded; the host's record of where each player stands on an island (`PlayerPlaces`, saved with the world) corrects a place Raft got wrong |
| `IslandRadar.cs` | Custom islands as dots on Raft's Receiver (Harmony postfix on `Reciever.HandleUI`) |
| `IslandObjectState.cs` | Harvested trees and picked-up items per island, and regrowing |
| `IslandNetwork.cs` | Multiplayer: island list, removals and island file transfer between host and clients, quests, used objects, announcements, object state and events |
| `Claims.cs` | Things only one player can have (a chest's loot, a zone that fires once): a client asks the host, the host grants the first to ask and holds it for them until their "used" arrives (message kind 16) |
| `PlaceableCatalog.cs` | The object catalog: the core objects (always loaded), Raft's buildable items, and the index of every other object of Raft's island scenes, loaded scene by scene when needed |
| `IslandGenerator.cs`, `GeneratorWindow.cs` | Procedural islands: heights from seeded noise in eight layouts or from a Raft island's measured ground, then peak shape, valleys, exact height, terraces, erosion, lakes and seabed; object scatter by zone and kind on a spatial grid; creature spots and tiered loot boxes; the preview map; the Generate window with its three tabs and saved presets |
| `RaftIslands.cs`, `RaftUnderwater.cs`, `RaftLand.cs` | Raft's islands as measured (`raft_islands.txt`, `island_thumbs`, `island_heights`; `raft_underwater.txt`; `raft_land.txt`): the size and height presets, Randomize existing, the life under water, and where things grow on the land |
| `IslandReach.cs` | Whether a player on a raft can get onto a generated island (the line above the seed) |
| `LevelSystem.cs`, `LevelWindow.cs` | The level up system: the numbers (`LevelRules`), each player's EXP and points saved with the world and sent over the network (`PlayerLevels`), the stats on Raft's player (`StatApply` and its Harmony patches), the floating EXP, the level bar by Raft's stat bars and the level up box (`LevelHud`), the "Lv n" under other players' names (`LevelTags`) and the stats page (K, `LevelWindow`) |
| `MapTypes.cs` | Map types: settings ranges, flying/sunken, and their content (`MapKit`: chests, notes, zones, creatures, atmosphere, quests) |
| `Behaviours.cs`, `BehaviourWindow.cs` | Behaviours and events: names, movement, "players can use it", collision, "when … then" actions, "only if" checks, waits, the shared clock for movers, their shared state and network messages; the Behaviour & events window |
| `StoryItems.cs`, `StoryItemsWindow.cs` | Story items (definitions, pictures), the crew's story book (items and journal pages: saved with the world, sent over the network), the journal window (J), the Story items window and the story sets |
| `WorldDirector.cs` | Rules (`IntroRule`), world plans (`WorldPlan`), the host's world director (conditions, placement, announcements, saved state), and the patch that keeps Raft's own islands off custom ones |
| `WorldPlanWindow.cs`, `ChoiceWindow.cs`, `NewWorldOptions.cs` | The world plan editor (also the island's rules) with templates, a list picker, and the plan and randomizer choices in Raft's New Game box |
| `WorldIslands.cs`, `IslandPickerWindow.cs` | The islands a world gets while sailing (the world file's `@islandsoff=`, the `WorldIslands` command) and the list with tick boxes in World settings |
| `WorldRandomizer.cs`, `RandomizerContent.cs` | The world randomizer: settings (saved with the world, sent to players), animal colours and alphas, moving Raft's crates and clams (and finding them again for Raft's saves), the extras laid over Raft's islands, oddity islands, boss lairs and large islands while sailing; `RandomizerContent`: what the extras, oddities and lairs contain |
| `RandomizerIslands.cs`, `RaftProps.cs` | The randomizer's islands: the quest-island themes and their scenes, dens (placing them, levelling the ground, what is inside), the large islands; the measured props and cave pieces (`raft_props.txt`) |
| `terraineditor.cs`, `TerrainPainter.cs`, `EditorTools.cs`, `EditorUI.cs`, `IslandFilesWindow.cs`, `ObjectPlacer.cs`, `EditorCamera.cs` | The editor (`EditorCamera.cs`: the Unity-style camera that replaced the 2021 RTS camera) |
| `RuntimeGizmo\`, `AwaitExtensions\` | Third-party move/rotate/scale gizmo and await helpers |

## Known limitations

- World settings: the notes on Raft's story islands still speak of the island that comes next in Raft's own order (only the frequency numbers on them follow the new order). A private storage is its builder's alone - there is no shared storage whose contents differ per player. Scrambled blueprints move only the blueprints lying on the story islands (the Radio Tower has none) and never what the story needs.
- Multiplayer was tested with two players on one PC (a second Steam account in Sandboxie), with the second player doing every in-world feature: islands and their files, flying and underwater islands, players who join later or again, harvesting and regrowing, chests, locked chests, zones and ambushes, creatures, levers, doors, waits, teleports, story items, the treasure map and the journal, quests and their rewards, island rules and world plans (also a new world with a plan), atmosphere and sound zones, movers, sailing and world shifts, the Receiver and announcements. More than two players (Raft allows eight) haven't played it together: the network code was read for everything that works with two and not with more (all fixed: see Multiplayer above), and what the host decides for a crowd - seven players asking for one chest, arriving together - is tested without them (`CIManyPlayers`). It hasn't been tested between two PCs over the internet yet. Players join through Steam (a friend's "Join Game"): Raft's own Join World list is empty in the current Raft version.
- Saving and loading, leaving and joining again, and restarting Raft are tested automatically, alone and with two players: the islands (in the same places), used objects, doors, chests, zones, creatures, quest progress, the journal, the players' items and the places they stood come back. A player who stood on a custom island is held there for a moment while the island loads, then set down (without that, Raft puts a player it finds swimming more than 100 m from the raft onto the raft). The raft itself keeps drifting while a world loads. Raft keeps a guest's items in the host's save only when the host saves while the guest is there. Raft itself once put a returning guest about 200 m from where they stood (after the host had loaded the world again): the host now keeps where each player stands on a custom island (saved with the world) and puts a player Raft misplaces back there.
- Support for Unity-built `.assets` islands from version 2 was removed. Rebuild those islands in the editor.
- Reaching a flying island is up to the player: build stairs or pillars up from the raft. The editor shows islands at sea level; the height only applies in game.
- Creatures: tested with two players (the host's creatures with their size and colour on the other player's screen, a second player defeating them and catching one with the net and carrying it, quests counting both), through Raft's own damage and capture paths but not yet by a person with a spear and a net launcher. Raft has no pets, so "catchable" means Raft's domestic animals. The screecher's stone look can't be tinted.
- Behaviours: actions waiting after a "wait" need the island to stay loaded: when the raft sails away (about 800 m) and it unloads, they are dropped. Item checks look at the inventory of the player who did it; for events no single player does (defeated animals, a finished quest) they look at the host's player.
- Story items live in the journal, not in Raft's inventory (Raft's own quest items are a fixed list, and new Raft items would break saves without the mod). Their ids are shared by all islands of a world: two islands that use the same id mean the same item.
- World plans and island rules are checked by the host only. A second player gets the announcement (with the direction from where they are) and the Receiver dots; this was checked in the two-player test's logs, not yet by a person looking at the screen.
- The generator: one island gets at most 12 000 objects (a Balboa-sized jungle is thinned to that). Lakes go below sea level, because Raft has one sea level. "Randomize existing" knows Raft's islands as measured with this Raft version (`raft_islands.txt`); built places (Tangaroa, Utopia, Varuna Point, the radio tower) count their buildings as ground, so their variations are rocky towers. Temperance is bigger than the build area and is scaled down. The life under water is placed from the catalog's core objects only (a few kinds seen on Raft's islands, such as Varuna Point's corals, load on demand and are left out).
- The Receiver shows every custom island as a dot, however far; a plan with many islands fills the radar. Map type content is placed by the generator: a chest can end up in an awkward spot now and then (the Generate window's map types let you check and fix one before sharing it).
- An island has one quest. Quest steps find things by name (zone name, note title, creature kind), so renaming a note breaks a step that points at it.
- Atmosphere zones change Unity's fog and ambient light plus a faint screen tint; how strong the fog looks depends on Raft's own sky at that moment.
- An island file with creatures, notes, a tint or island info is format 4: older versions of the mod can't open it. Islands without these are still saved in the older formats.
- Objects from Raft's other islands and Raft's buildable items are decoration: their scripts are removed, so a chest doesn't store anything and a character doesn't move. Only the harvestable trees, rocks, ores and plants keep their gameplay.
- World randomizer: colours only multiply an animal's own texture, so they are darker or tinted, never lighter (no white sharks). Alphas' extra spoils come from alphas of Raft's own islands; the extra animals on Raft's islands are never alphas. An oddity island or boss lair that is due waits until there is room ahead of the raft (Raft's sea is crowded near its islands). Tested with two players on one PC (same colours, crate spots and extras on both, a moved crate picked up by the second player, leaving and joining again, saving and loading), not yet by people playing a whole world.
- Tested before release (automatic, `alltests.ps1`): a story plan made by clicking in World Plans from saved islands, chosen in the New Game box, played in a new world (each island loaded exactly as saved, each rule once), saved and loaded, and joined by a second player; every editor window and the New Game box at eight screen sizes from 1024x768 to ultra-wide; every Raft graphics setting and all twelve of Raft's languages; hand-broken settings files and island files (warnings and safe values, never a crash); every click-twice button. Screens wider than the monitor are tested only as far as the monitor goes.
- An island that uses objects from one of Raft's story islands (e.g. Utopia) makes the mod load that island's scene for a moment when the island spawns in a world, to copy the objects. The scene is switched off as it arrives; this can take a second.

## License

GNU AGPLv3
