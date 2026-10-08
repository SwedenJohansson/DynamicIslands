# Changelog

What changed in Custom Islands, newest first, for players and island makers. Internal clean-ups and the many test runs
behind each change are left out; the [guide](docs/GUIDE.md) explains every feature named here.

The last public release on raftmodding.com is **v1.1.1** by FranzFischer78 (2022). Everything below is the work on
**version 3.0**, the rebuild for Raft 1.1 (Unity 2021.3) that started in September 2026. Version 3.0 is an
**experimental alpha**: things will still change, so back up the worlds you care about.

---

## 3.0 alpha - 6 October 2026: editor tools, safety audit, versions on the network

### New
- **Unsaved work, one at a time:** in *Unsaved work from last time* each island's row has an **X** that throws away
  only that island's unsaved changes (to `deleted\autosave`); the window closes when none are left.
- **Journal island count:** *Islands reached* now also counts the saved islands ticked for the world in World
  settings (Islands while sailing) that haven't come yet - a world with about 50 ticked showed 3/19.
- **Quest islands need finding:** the quest panel and introduction now show within about 70 m of an island's
  shore (was 150 m), and stay up out to 150 m.
- **Plan picker:** **Choose plan...** in the New Game box opens a window with every world plan - a search, its
  picture (or a map of its first island), description, random/story and the islands it brings - instead of a list.
- **Terrain brushes:** brush edge (falloff), Noise and Erode brushes, a sea floor switch, and a second style mixed
  into the island (8 ground textures).
- **Selection tools:** box select, select all, select the same kind, and a list of placed objects with hide and lock.
- **Copy and paste between islands**, and **Place exactly** (typed position, turn and size).
- **Elevation preview:** the blue sea plane moves to where the sea will be in a world.
- **Raft's sky and sea in the editor** (an option, off by default).
- **Rename an island:** worlds, plans, rules, the spawn pool and kept copies follow the new name.
- **My islands** window: every island file with its source, the worlds using it and its pool weight; open, rename,
  delete, tidy up. **My groups and stamps**: rename and delete saved groups and stamps.
- **Several quests per island.**
- **Raft's other story islands rebuilt from their own pieces** (14 designs) for the generator.
- Raft's doors, hatches, cranks and levers as ready pieces; Raft's door walls open on islands.
- A skyscraper landmark; landmarks on the randomizer's large islands.
- Generator option **Raft's features**: vine caches, buried treasure, ziplines, code strongboxes, beehives, dirt.
- **World Plans:** *Test this plan* starts a new test world with it; *View...* a plan from the New Game box; plan
  descriptions up to 400 characters.
- The world window lists the islands in the world (distance, direction, quest).
- **Map types as data** (`.maptype` files you can write yourself), ReRollMapType, and a **Defaults** window for the
  `spawnpool.txt` settings.
- Version notices: plans and worlds remember which mod version wrote them and warn when they come from a newer one;
  players with different mod versions are told, and an update check offers the new version.

### Changed
- **Library updates (T10).** An entry shows **Update** also when one of its files changed in the library without a new
  version number (installs now record each file's SHA-256). Updating over files you changed offers **Update, keep a
  copy** (yours stay as "name (yours)", never at random while sailing), **Keep my changes** or **Cancel**, and names the
  saved worlds that use them. A world with an island an update replaced says so once when it loads.
- **Removing an island's rule or quest step keeps saved worlds safe:** like removing objects, it now keeps the old
  version for the worlds that have the island, so a fired rule doesn't fire again and a quest doesn't jump a step.
- **Single islands and plan islands written down:** the guide (7, 9.5, 16) and README now say that islands a world plan
  brings (and the islands their rules bring) never come by chance, which files are never in the sailing list, and how
  to make an island a single island.
- **New islands come first:** a random pick counts an island the world already has (left behind, not finished) at a
  quarter of its weight, so the same island no longer comes back so often early on.
- **"Comes by chance after ... km sailed"** (island editor, Island tab, Rules): the earliest an island may come by
  chance, with the sailing time it means (e.g. 40 km, about 5 h); at most 192 km.
- **Missed notes bring the island back:** an island whose quest is done but with a note not found yet can come
  again by chance, as you left it; only once every note on it is found is it finished for good.
- **Notice boards look written on:** a readable notice board shows lines of made-up writing on both faces, so
  players can see it holds a note.
- **Text that didn't fit:** status lines, island names in the lists, behaviour rows and titles, World plans hints,
  the quest reward line and inspector loot names wrap and shrink to stay in their boxes; names on the World plans map
  no longer write over each other.
- **Islands while sailing:** the row *Brand-new generated islands* now says what it does (hover it, and the list's
  intro): the mod makes up a random island now and then, saved as a gen-... file in that world; unticked, only made or
  downloaded islands and map types come. The New Game summary notes when it is off.
- Plan islands are matched by their content, so the host can ask players for exactly the right file; newer kept
  copies are set aside instead of overwritten.
- Build cost is rounded to the nearest whole amount and never goes below Raft's own; blocks give back what they cost
  when they were placed.
- Quest "find" steps count story items found in all; "near" is measured from the middle of the land.
- Switching the story order mid-game replaces islands in place.
- The plan Check warns about quest traps and story items that come out of order.
- The electric zipline blueprint is never scrambled away.

### Fixed
- **Remove stopped with "Cannot create a file"** when the deleted folder already had a dated copy of the same file
  from the same second (an island removed twice); the older copy now gets a number.
- **Limits against runaway growth:** journal pages (2000, titles 200 and texts 8000 letters) and story item kinds
  (500) are capped, the host drops claims that ran out once it holds many, and a file already on its way to a
  player isn't sent again when they ask twice.
- **Quest steps already met move on at once:** a collect or pages step the crew already has enough for is ticked
  together with the step before it, not half a second later.
- **Stats page:** open when the world is left (a disconnect), it closes and no longer locks the cursor on the main menu.
- **Island music:** music zones on an island switched on while a world was still loading now work (the fix for their
  red error in the editor skipped them).
- **Quests:** two steps asking for the same kill (Whiteout Reach's two polar bears) no longer lock when both animals
  are defeated before the first step comes: each counts for one step. More than a step needs carries on to the next same step.
- **Arrival texts:** an island's name and description, its quest's intro and other messages now come strictly one
  after another, each up long enough to read (5-16 s by length); the next one used to replace it after 3 s.
- **Generator:** regenerating an island with 0 quest steps also takes away its old quest (its objects were gone);
  undo brings both back. Brand-new islands made while sailing follow the world's seed when it has one.
- **Generator:** closing it while Raft's islands still load no longer generates later on its own; a second click while
  loading says it is still loading. The preview's count now includes the under-water kinds *Randomize* adds.
- **Editor keys:** a key Raft already uses, or a number key (hotbar slots), can't be picked for an editor action.
- **Sea plane** in the editor now fits the island's ground size, also after it grows.
- **Give my worlds this version** leaves the editor's test world alone (its next save wrote the old version back).
- **World plan list:** Receiver, by-chance and story rules now show [done] once their island is in the world.
- **Edited plans:** a saved world now follows the plan's *random islands while sailing* switch when you changed it in
  World Plans (it kept its old switch).
- **WorldIslandsGap** with words it can't read now says how to write it and changes nothing (it reset to 3-6).
- **Hardening:** library pictures claiming more than 4096 pixels a side aren't unpacked; NaN numbers in a hand-edited
  spawnpool.txt or generator preset are ignored; a negative quest step in a hand-edited world file counts as 0.
- **In-game texts** brought up to date: import window, Open, Throw them away, Share..., World Plans help and Delete,
  tab colours (nine), the Randomizer and plan file help, the report form, lighthouse storeys, desert palms, regrow
  tooltips.
- **Boss health:** an animal's health can be set up to ×12 (was capped at ×4), so the lair bosses set to ×5-×12
  are as tough as written. Damage stays at most ×4.
- A large audit (about 70 items) of saves, files and multiplayer: world copies written between Raft's saves no longer
  win after a crash; unloaded islands keep what happened on them; library installs check their files and roll back;
  half-written first saves are set aside; host file names are checked before use; deleted autosaves and plans go to
  the `deleted` folder.
- A raft that Raft left without its grounding colliders gets them back (it could drift through islands).
- Missing generated islands are made again; a stale object index in a world no longer mixes up objects.
- Randomizer extras appear at the island's place after a world shift.
- Two players reaching a once-only zone: the second one is told it was already used.
- The host can open a private storage whose builder is not in the game.
- Performance work on streaming islands and the library.

---

## 5 October 2026: Raft's own features for island makers

### New
- **Things to gather** on islands, with a Scatter tool in the editor; every library island now has them.
- Generator sliders for Things to gather and Finds in the shallows; 0-25 buildings and a Wrecks and landmarks kind.
- Raft's dirt spots and wild beehives, machete vines, ziplines (with a far end you choose), buried treasure for the
  metal detector and shovel, Raft's quest item pickups.
- **Keypad code locks** on any usable object.
- Raft's note frequencies name the custom island they now lead to; Receiver distances and rewards for players who
  were away.
- Lighter randomizer colours (snow, cream, pale).
- Plan Check shows which island gives which of Raft's blueprints, and warns when one is never given.
- About 25 new library islands (among them Cablecar Stacks, Sargasso Town, Rig Seventeen, Hollow Mountain,
  The Last Kingdom, Clockwork City, Starfall Crater).

---

## 4 October 2026: the quest book and the themed islands

### New
- **Quest book:** a world's main story gets its own tabs and pages in Raft's notebook; side quests stay in the
  journal. A *New main story* helper and *Preview notebook* in World Plans; endings for the five plans that come with
  the mod.
- **18 themed islands** (Gilded Skull Cove, Tiki Lagoon, The Safe Room, Primeval Park, Sundown Canyons and more) and
  the **Silver Screen Seas** world plan, plus a dozen more new islands.
- Raft's finds in the shallows around library islands.

### Changed
- Random islands: a finished one never comes again, and they come about once per 3-6 of Raft's islands.
- Main story islands always have coordinates on the Receiver.
- The guide can be opened from the library window.

### Fixed
- Spotlights no longer shoot at the player.

---

## 3 October 2026: Raft Remade, Raft 2 and a better journal

### New
- **Raft Remade:** every story island of Raft rebuilt (Radio Tower, Vasagatan, Balboa, Caravan Town, Tangaroa,
  Varuna Point, Temperance, Utopia) as a world plan played from start to end.
- **Raft 2:** ten new stations ending in The Frontier finale (32 steps in six chapters).
- Generator: **reefs** like Raft's, **buildings and caves**, a **quest made with the island**, and *Randomize existing
  / Rebuild it*.
- **Journal:** a Progress panel (quests, islands reached, notes, story items, pages), notes found per island, and
  "Quest Pages".
- Friendly animals on some islands.

### Changed
- Roofs rest on their pillars; generated huts stand on Raft's foundations.
- The quest panel shows five steps at a time and scrolls.
- Every library island rebuilt with the reef generator.

---

## 1-2 October 2026: the island library fills up

### New
- The **island library** reads the public library folder; the first library islands and world plans (The Long Voyage,
  The Abyss Expedition and others), each played through by an automatic tester.
- Very large islands (Ironreef Caverns, The Drowned Metropolis, Thornwood, Glacier Station, Scrapyard Haven).
- Dark zones really darken; air pockets under water; angler fish.
- Generator densities measured from Raft's own islands, per style and size ("Like Raft").

### Changed
- Nothing stands in the air: the generator, placing and sculpting set things down by their base.
- Editor camera: dragging up with the right mouse tilts the view down.

### Fixed
- Many fixes from scenario tests where Raft's gameplay meets the mod: ramming islands, flying and sunken islands,
  world shifts, full inventories, deaths, waits, quest counts with an older host, joining while islands are made.
- Moving a player lets go of a ladder first.

---

## 29-30 September 2026: safety, easier windows, settings in a running world

### New
- **Esc > Custom Islands** in a running world: rules, randomizer, options, levels, islands while sailing, plan and
  story; the host changes them and picks another plan from a list.
- **Test in a world** from the editor.
- **World Plans** made easier: each rule is a card (WHEN, BRING, WHERE, TELL, STORY) with drop-down lists that say
  what each choice does, and a thorough **Check** that walks the plan like a world plays it.
- Drop-down lists instead of cycling buttons in the quest editor, Behaviour and events, styles and layouts, Light
  and the New Game box.
- Hotbar key tabs for the journal (J) and the stats page (K).
- **Tidy up** in the library window.
- Raft's abandoned-raft crate as a placeable object.
- The level up system as a World setting and a host switch (Levels on/off).

### Changed
- Deleting an island moves it to `Mods\DynamicIslands\deleted` and names the worlds and plans that use it.
- Island settings are undo steps (they were lost on leaving).
- Saving over an island that saved worlds use keeps those worlds on the version they started with when the change
  would mix things up.
- Editor light presets retuned.

### Fixed
- Saves replace the old file in one step; a half-finished save is recovered.
- An object this Raft version doesn't have is kept as a red placeholder instead of being dropped.
- Joining players keep asking for the host's islands instead of giving up; a *Resync* command.
- File names Windows refuses, "in use by another program" errors, and a version handshake between players.

---

## 27-28 September 2026: world settings, story chain, packs and the library

### New
- **World settings** window in the New Game box: world rules (monster difficulty, build cost), the **world
  randomizer**, extra options (scrambled blueprints, story islands in a new order, ghost rafts, private storages) and
  islands while sailing.
- **Level up system:** EXP from monsters, stat points, the same for every player.
- **Story chain:** a world plan's islands in Raft's Receiver chain (first, after or instead of a story island),
  Raft's story on or off, islands on their own frequency.
- **Host swap:** another player can host the world next time.
- **Packs:** share islands and plans (Export / Import); each world keeps its own copy of its plan.
- **Island library** window to download islands and plans.
- Choose which islands a new world gets while sailing.
- The **illustrated guide** (docs/GUIDE.md and a PDF), help ? marks in the game, and how to report a problem
  (including the mod's Discord).
- The **EXPERIMENTAL ALPHA RELEASE** box on the main menu; a loading box while the editor opens.

### Changed
- Rafts of Raft's blocks on islands float like the player's raft and have a deck to walk on.

### Fixed
- Editor autosave; an edited plan plays in its world; older Raft saves load the mod's state of that save.

---

## 25-26 September 2026: a stronger generator, randomizer and multiplayer

### New
- A much more powerful **island generator**, with underwater slopes like Raft's islands and trees, bushes, rocks and
  resources where Raft puts them.
- A modern **editor camera** like Unity's scene view.
- **World randomizer:** a normal Raft world that plays out differently every time; randomized islands like Raft's;
  up to eight players.
- World rules shared by every player (monster difficulty, build cost, regrowing, creature health).

### Fixed
- First two-player tests: axes on custom trees, island sync while joining, world shifts after the menu, players put
  back where they stood.

---

## 23-24 September 2026: version 3.0 rebuilt for Raft 1.1

### New
- The mod compiles and runs on current Raft (Unity 2021.3) again, with save and load fixed.
- **Editor:** textured terrain, soft brush with a ring, undo, texture painting (Sand, Grass, Rock, Seabed), an islands
  window, and every object of Raft (about 1,900) placeable from a browser with pictures.
- **Island styles:** Tropical, Snowy, Desert, Forest, Volcanic.
- **Islands while sailing:** custom and brand-new generated islands appear on their own, far ones unload; islands on
  the Receiver; flying and underwater islands; sunken props and scrap to dive for.
- Islands behave like Raft's: saved with the world, walkable, harvestable, remembered when harvested.
- Huts and abandoned rafts from Raft's building blocks.
- **Making islands come alive:** creatures, notes and signs, loot chests (also locked), trigger zones, quests,
  atmosphere and sound zones, behaviours and events, story items and the journal, object groups and terrain stamps,
  invisible walls and ramps.
- **World plans and map types.**
- **Multiplayer:** players get the host's islands and missing files; harvesting and pickups sync.

### Changed
- Islands built as Unity `.assets` files are no longer supported; islands use the `.island` format.

---

## v1.1.1 and earlier (2022-2024, FranzFischer78)

The original Custom Islands: islands built in Unity and loaded into Raft, async loading, a shader fix, and the first
terrain editing (2023). See [raftmodding.com](https://www.raftmodding.com/) for that release.
