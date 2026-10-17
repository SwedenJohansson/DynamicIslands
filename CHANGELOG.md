# Changelog

What changed in Custom Islands, newest first, for players and island makers. Internal clean-ups and the many test runs
behind each change are left out; the [guide](docs/GUIDE.md) explains every feature named here.

The last public release on raftmodding.com is **v1.1.1** by FranzFischer78 (2022). Everything below is the work on
**version 3.0**, the rebuild for Raft 1.1 (Unity 2021.3) that started in September 2026. Version 3.0 is an
**experimental alpha**: things will still change, so back up the worlds you care about.

---

## 3.0 alpha - 6 October 2026: editor tools, safety audit, versions on the network

### New
- **Story items as Raft's own items:** the Story items window's **Raft item...** links a story item to one of Raft's
  items; in the journal, **Take out** turns one into that item in your inventory (the host decides who gets the last).
- **Sounds and music from a trigger:** the when/then actions **play sound/music** (one of Raft's sound events or
  music cues, picked from a list, with a volume, once or on a loop, near the object or for everyone on the island) and
  **stop sound**. Every player near the island hears it; it stops when they leave or the island unloads, and a loop
  comes back after a reload. Check warns about a sound name Raft doesn't have.
- **Camera moments:** the when/then action **camera moment** shows the view from a marker toward an object for some
  seconds, then glides back; the player is held meanwhile and Esc skips it. Only players within its range see it.
- **Credits for a plan's ending:** a plan's ending card has a CREDITS section (a title and the makers' names). When
  the main story ends, every player sees the credits and the closing note once; the world plays on and Raft's own
  ending is untouched.
- **Story islands keep their look when randomized:** Something new like it and A variation of it for Caravan Town,
  Tangaroa, Utopia, Varuna Point, Balboa, Temperance and the Radio Tower lay the new island out like the original from
  its own big pieces, placed the way the original has them - red rock mesas with ladders, huts, caravans and rope
  bridges; the whole glass dome with its hexagon roads; Utopia's white towers; the motherlode floors on their pillars
  inside its walls with the crane on top; giant firs; snow rocks and the antenna; low rocks with concrete footings.
  Since 17 October the Vasagatan gets its lounge furniture and safe washed up on the beach, the Radio Tower's dish is
  put after the design (a tower on the top no longer clears it), and Caravan Town's sand between the mesas has
  caravans, vans, acacias, dead trees and boulders as thick as the original's.
- **Islands missing when a world loads:** when a world uses island files this PC doesn't have, the host (or single
  player) gets a box naming them, and the library entry each came with. **Play without them** leaves them out (no
  Receiver dot) but keeps them in the world, so they come back once the file is here; **Main menu** leaves without
  saving, to download or restore them first. Joining players get missing files from the host as before.
- **Again until done** for world plan rules **On a day** and **After sailing a distance**: a tick box on the rule card
  (`day:5:again` in the plan file). The island comes back ahead of the raft every N days (or km) after the players were
  last near it - even if they never reached it - until they reached it and finished its quest; the same island each
  time, with no limit. Check gives a tip when the island is on the Receiver (it doesn't come back by itself there).
- **Monster size up to x3:** a creature spot's **Size** now goes from x0.5 to x3 of Raft's own animal (was x2.5); its
  body and hit box grow with it, its health stays (set **Health** for a tougher one), and every player sees the same
  size. Balboa Remade's Mama bear is Raft's bear at x2 - as tall as Balboa's own.
- **Brand-new generated islands explained:** the row in World settings > Islands while sailing has a **?** saying what
  it does, how often one comes, where each is saved (`gen-<style>-<seed>.island`, kept in that world) and how to keep
  one; EDITOR > An island > **Open an island...** now lists generated islands too, to open and **Save as** your own.
- **Survivors to rescue:** a new object, **Survivor in a cage**, holds one of Raft's characters locked in with a story
  item. Bring the key and press E: the survivor says a line in a speech bubble and then stands on your raft. One per
  survivor id in a world, saved with it and seen by every player; the rescue sends a signal for quests and plans.
- **Boss phases:** a hostile creature spot can get a second phase: at a share of its health (half by default) it calls
  more creatures, gets faster and sends a signal, once per fight. A reload mid-fight keeps the phase. The Boss map
  type's beast has one.
- **Editor camera settings:** **Camera...** in the editor's status bar (and **Defaults... > Editor camera**) sets
  this PC's look sensitivity, move, zoom and pan speed, which way dragging up looks and the wheel's direction; saved in
  `world_rules.txt`, and the right mouse + wheel flying speed is now kept too. **Alt + right-drag** zooms, as in Unity.
- **Keycard doors and buried treasure (RF4):** select Raft's card reader door or another key door for its
  **Keycard door** settings: the story item that opens it (the island's own or one of Raft's quest items) and whether
  the card is used up. Without it the door stays shut; with it the reader beeps it through, for the whole crew and
  after loading. Check warns about a key door that no story item opens. Buried treasure is found only with the metal
  detector (it beeps faster nearer), dug with the shovel, and its chest can give **Extra loot** - story items too,
  once for the whole crew.
- **Islands and world plans apart:** **EDITOR** now opens a start window, "What do you want to make?", with cards
  for **An island**, **A world plan** and **The guide**; the one you made last is lit, with a **Continue** button. A
  world plan is made right on the main menu, without the 3D editor. In the island editor, **Use in a plan...**
  (replacing the **World plans** button) puts the island in a plan, next in the Main story line or as an island that
  comes on its own; a plan's **Edit island** opens it in the editor, with **Back to the plan**. A **?** after it
  explains it, and how it differs from **Save as**.
- **The plan window has three tabs:** **Main story line** (the islands the Receiver leads players to, in play order,
  with Raft's own islands as grey lines; **+ Add a saved island**, **+ New island**, and ▲/▼ to move an island along
  the story), **Other islands** (the WHEN / BRING / WHERE / TELL cards) and **Settings**. The window has a top row with
  **Plan**, **New...** (empty, step by step, a template or a copy), **Open...**, **Copy...**, **Delete**, **Templates...**.
- **Learn from the islands:** a new guide chapter (17) on opening any island in the editor - from the library, a
  ready-made one or your own - and looking at what it is made of, with a table of every editor feature and an island
  to look at for it. Two small library islands made to learn from, with a sign at each place: **The Lockkeeper's
  Isle** (a keypad code lock, three quests on one island, the island rules, an island its quest brings) and
  **Lantern Spire** (a flying island).
- **World plan Check: one story item on two islands:** Check warns when two islands of a plan each give a story item
  with the same id (two 'keycard's) - the one found on either would open the other's locks.
- **Raft's progression in order in world plans:** the plan Check warns about a blueprint given before the ones it
  builds on (the engine controls before the motor wheel and steering wheel, fuel pipes before a fuel tank...), and
  Progression shows it in yellow. A lock whose story item an island met before gives says where the item was kept from.
- **Keys kept for later islands in every library plan:** Raft Remade (Vasagatan's crowbar opens a crate at Caravan
  Town), The Long Voyage and The Abyss Expedition (Ironreef's pump store key, a box on the Drowned Metropolis), Silver
  Screen Seas (the liner's steward's keys, a sea chest at The Arena). Mayor's Wharf's keycard no longer shares its id
  with the Drowned Metropolis' council keycards in The Long Voyage. Embers Isle's chained dog cage is scenery now (in Silver
  Screen Seas it was a lock wanting bolt cutters only a later island gives). Blueprint names in Check read as words.
- **A clickable error note:** when something goes wrong in the mod, a note at the bottom left opens the details
  (whose code it was, the message), with Copy, the log folder and where to report. `errors.txt` keeps them.
- **The Islands window explains itself:** a **?** for Name, Height in the world, the island list and the sharing
  buttons. The height's ? says what 0, above 0 (flying) and below 0 (sunk) mean, and how players reach the island.
- **Giant rogue sharks:** with the Rogue shark option on, about one rogue in five is a giant - twice Bruce's size, three
  times his health, bites half again as hard on players (not on the raft).
- **The daily quest in the journal:** with the world's Daily quest option on, the journal shows today's quest under
  its page: what to do and how far along (3 / 14 planks, with a bar), the reward, whether it's done and when the next
  one comes.
- **Head start raft:** a new World setting (levels 1-3) starts the world on a bigger raft that is already set up: 6 x 12
  foundations with a grill, a purifier, the research table, a bed and first supplies; level 2 (8 x 14) adds smelters, a sail,
  10 item nets and island resources; level 3 (10 x 20) the Receiver with its antennas. Built once by the host.
- **More islands with Raft's special features:** every feature that was on one library island is on a second one
  now, and the mod's puzzle pieces (laser, water wheels, pipes, wires, claw, scales) are on six islands. In Raft 2,
  three locks want an item another island of the plan gives earlier (a keycard, bolt cutters, a radio part).
- **No more locks before their key in the library plans:** Raft's ready pieces that stood on islands only as scenery
  (wrecked engines and generators, cable hubs, shark and dog cages, dead radios) no longer want a story item the plan
  gives later. Whiteout Reach's summit radio now wants the crystal from the boiler's drawer, and Shelter Atoll's
  generator also runs its radio room. The plan Check no longer warns about a ready radio on an island with a generator:
  the generator runs it without the battery part.
- **Progression panel:** the world plan editor's new **Progression** button shows, in the plan's order, which rule's
  islands give which of Raft's story-island blueprints and story items, the locks that come before their item, and the
  blueprints the plan never gives.
- **Things to pick up indoors:** eighteen of the library's islands have a little plastic, planks or scrap lying on the
  floors of some of their houses; they come back after a few days like Raft's. The generator's huts and cabins (and
  the world randomizer's huts) get them too: none, one or two per house, on free floor beside the furniture.
- **Upgrade research table:** the extra upgrades are learned at a research table of their own (violet, four times the cost
  of Raft's, there from the start with the option on) instead of with their base item. Its research is separate from
  Raft's table; a world from before keeps the upgrades it had.
- **Laser beams:** Temperance's laser emitter shoots a beam the mirrors send on (turn a mirror, the beam turns); what
  it reaches gets the new event "a laser beam reaches it" - the laser door panel answers with the signal `laser`.
- **Unlock a character:** a new action gives the player one of Raft's characters (by name or number), as Raft's story does.
- **Boss arenas:** on a boss island, stakes burst up round the arena when the crew steps in and sink when the beast is
  defeated (two minutes at most); the spoils chest shows only then.
- **Ready puzzle pieces:** Utopia's water wheels and pipes (and the radio tower's pipe bend) turn a quarter each use
  like Temperance's mirrors; Utopia's wire connectors take Raft's electrical cable once and stay connected; Tangaroa's
  claw crane console and the justice scales send a signal. Each sends its own signal (`wheel`, `pipe`, `wire`, `claw`,
  `scales`) for quests, world plans and other objects.
- **The world travels with the players:** after each save, and when a player joins, the host sends Raft's newest save
  of the world to every player, into their own Raft World folder. Whoever hosts next loads it from Load Game; copying
  the world folder by hand is only needed for a player who missed the last save. A player's own world of the same name
  is left alone, and only the last 3 received saves are kept.
- **World option Extra upgrades:** better versions of Raft's things to craft, each tinted and crafted next to the
  thing it improves once that is learned: **large battery** (twice the charge; fits every battery slot), **titanium
  grill** and **blast furnace** (cook twice as fast), **reinforced storage** (half as many slots again), **wide net**
  (twice as wide, twice the items), **large fuel tank** and **large water tank** (twice as much), **greenhouse plot**
  (waters itself), **turbo engine** (twice the strength, half again as fast, burns fuel faster), **large wind
  turbine** (charges twice as fast), **comfy bed** (heals twice as fast while sleeping, respawn at 75%), **magnet
  hook** (pulls in half again as fast, gathers twice as fast), **titanium rod** (fish bite twice as fast), **swift
  flippers** (twice the flippers' extra swimming speed) and **large air tank** (air lasts twice as long as with the
  bottle). Also **rapid charger** (twice the charge per fuel), **fast recycler** (twice as fast), **bright lantern** and
  **floodlight** head light (light twice as far), **telescope** (twice the zoom), **titanium greatsword** and
  **titanium spear** (hit half again as hard) and **long paddle** (pushes twice as hard). Hand tools, weapons and worn
  things last twice as long as their base; every upgrade has a colour of its own (worn ones while worn). Also **steel
  pot** and **fast juicer** (cook twice as fast), **titanium machete** (hits half again as hard), **sturdy scarecrow**
  (never wears out), **master hammer** (twice the repair), **longbow** (draws and shoots half again as far), **bright
  head light** and **bright lamp** (light twice as far) and **lumber axe** (takes pieces apart twice as fast).
- **World option Long voyage:** random custom islands come half as often (after every 6-12 of Raft's own
  islands) and at least 1.6 km apart, so food, water and the raft matter more between stops.
- **World option Iron raft:** the raft's blocks take half damage from shark bites; taking pieces down with the
  hammer or the axe is unchanged.
- **World option Shared EXP:** when a player defeats a monster, every other player within 50 m gets 60 % of its
  EXP too.
- **Fix:** the daily quest's task, the rogue shark's days and the storm days are rolled from a well-mixed number of
  the world's seed and the day: with nearby seeds Raft's random numbers came out nearly the same, so days rolled alike
  (storm days came in runs, and the daily task changed little from day to day).
- **World option Trader raft:** very rarely (about once in 7 km of sailing) a trader's raft comes by, with a stall
  per offer: planks, plastic, rope and scrap for fruit, flower and tree seeds, and now and then a blueprint. The stock
  is shared by all players and saved with the world. Console: `Traders`; the host's `Traders bring` puts one ahead now.
- **World settings window:** the extra options are one list in the right column, about five in view; scroll for
  the rest, so the window stays the same size however many options there are.
- **World option Storm days:** on about one day in ten the sea is rough and it rains all day; the shark comes for
  the raft more often; everyone is warned at nightfall the evening before.
- **World option Silver & golden barrels:** now and then a barrel in the sea is silver (about 1 in 40) or golden
  (about 1 in 150), with a few extra nails, rope and scrap (gold: more, and sometimes a battery or bolts).
- **World option Rogue shark:** now and then a second, rust-red shark comes beside the usual one; it stays until
  it is killed, and then none comes for 5-8 days. Saved with the world; every player sees the same one.
- **World option Daily quest:** each morning a small task for the crew - gather some of a basic resource, catch a
  few fish or defeat a few monsters; done before dark, each player gets a small reward (basic resources or food, 40 EXP
  with levels) once; otherwise it runs out at dark. Shown as a banner and at the top of the journal's quest list.
- **World option Night is dangerous:** at night monsters have x1.3 health and damage and the shark comes for the
  raft more often; by day both are calmer.
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
- **Map types as data** (`.maptype` files you can write yourself), ReRollMapType, and a **Defaults** window for the
  `spawnpool.txt` settings.
- Version notices: plans and worlds remember which mod version wrote them and warn when they come from a newer one;
  players with different mod versions are told, and an update check offers the new version.

### Changed
- **The editor's Islands window opens at once with many islands:** the first rows show straight away and the rest
  fill in over the next frames (1,000 saved islands held it for over 2 s).
- **The host checks what players report:** used things, harvests, quest progress and fired rules count only from a
  player near that island; levels are switched on by the host only, and a level record with more EXP than the host
  credited is put back.
- **Quicker starts:** RML compiled the mod on every start (about a minute), because its own start-up throws away the
  compiled copy it keeps. The mod now keeps that copy (read-only), so only the first start after installing or updating
  compiles; the main menu says so once. Older copies of the mod in RML's cache are removed.
- **The generator gives the same island on every PC:** its list of objects was sorted in the PC's language and could
  include other mods' buildables and follow `placeables.txt`. It now uses Raft's own island objects in a fixed order.
  **A seed now makes a different island than before** (worlds keep the islands already saved; only islands generated
  from now on - new `gen-` islands, a lost `gen-` file made again, map types - follow the new order).
- **No sample plans:** the mod no longer writes the sample world plans Adventure, Island hopping, Growing sea
  and Receiver adventure, and they are gone from **Templates...**. The plans that come with the mod are Random islands
  and No custom islands; full storylines come from the island library (**Get more plans...**). Sample plans written
  by an earlier version stay in your plans folder until you delete them.
- **The first Generate looks like Raft's small islands:** the generator's new layouts **Raft small island** (first in
  the list, and the default) and **Raft large island** take the outline and ground of one of Raft's own islands - the
  seed picks which, and turns, mirrors and wobbles it - with a beach all round to wade onto from the raft. The generator
  opens on one about 40 m across and 12 m high, and **Defaults** gives it; after a Randomize existing or Ready-made
  island the Normal tab no longer opens on that island's big, round settings.
- **Level up is a world option only:** the editor's Island tab and the generator (all three tabs) have no Level up
  choice any more; the level up system is switched in World settings or with `Levels on`. Older islands and presets
  with the choice still load (it is ignored).
- **World plan Check and Progression follow the play order:** Raft's story chain with your islands in their places,
  other islands by what they wait for. Raft's story islands give their blueprints when the story reaches them.
- **Check warns when a main quest needs a tool** (the machete for vines, the metal detector...) that no island before
  it gives. Optional finds (buried treasure, caches) needing a later tool are only listed in Progression.
- **World plans: the bottom buttons are two rows:** working on the plan (+ Add a rule, Check, Progression, Preview
  notebook, Test this plan) above, its files (Export..., Import..., Save, Close) below. In one row Close stuck out past
  the window's edge.
- **Island lists open at once:** **+ Add a saved island** and the other island lists in World plans open at
  once with a "Loading islands... (n of N)" line, then list your islands. Their titles are kept in `summarycache.txt`,
  so the next time takes no time (before, the list came only after every island's whole file was read).
- **Upgrades stand out:** stronger tints (also on parts that have only a texture), a few slow glints in the upgrade's
  colour round a built, hand-held or worn upgrade, and hand-held ones a little bigger. Pale tints hardly showed before.
  The titanium spear is a light, almost white metal (its tint was only a little darker than the scrap spear).
- **Check warns about a deleted story item:** a story item that a quest step, a check, a chest or an action
  still uses but that is no longer in the island's Story items list (or was mistyped) is now a quest trap in Check.
  A quest file edited by hand is read up to 99 steps; more are cut. (The editor adds up to 10; a recipe's quest runs
  longer - the library's finales have up to 40.)
- **Different builds of the same version are noticed:** every release says 3.0, so two players with different 3.0
  builds weren't told (a friend didn't see the new research table). Each build now has a stamp from its files; joining a
  host with another build (or one from before the stamps) shows a notice: both should install the same, newest release.
- **"The host didn't answer" says what to do:** when it keeps coming (a player who joined again within seconds of a
  crash can lose the link now and then), leave the world, wait half a minute and join again.
- **Esc → Custom Islands is compact:** the world's settings window fits a 1366 x 768 screen (it was taller than the
  screen): tick boxes for the randomizer's parts, the extra options and the islands while sailing, short scrolling
  lists, a hint line at the bottom, and the whole window scrolls when needed. Each group and option now says what a
  change does in a world already under way; the head start raft is shown (made with the world, can't change). The
  plan is shown but no longer picked there: a world keeps the plan it was made with. Regrow days, the Receiver dots
  and the unload distance left the window (F10 `RegrowDays`; **Defaults...** in the New Game box). The list of the
  world's islands left it too (the quest book shows where each island's quest stands); the extra options list is taller.
- **Two world options retired:** *Scrambled blueprints* and *Story islands in a new order* are no longer in
  World settings for new worlds. A world that has one on keeps it, and the host can still switch it off there.
- **EXP +50 %:** every monster is worth half as much EXP again (Bruce the shark 180, was 120); the EXP each
  level needs is the same, so levels come faster.
- **Editing islands your saved worlds have (R1b).** Every object of an island now has its own number, kept through
  edits (copies get new ones; files saved before keep working, numbered by place). Deleting objects, changing their
  order or making one a chest no longer keeps those worlds on the old version: they get the new one, with what was
  picked, looted, opened or done there kept on the same objects (what belonged to a deleted object goes with it).
  Only removing or reordering rules or quest steps still keeps the old version for them. **Give my worlds this
  version** carries what was used the same way.
- **Two packs, one story item id (T8).** Installing a pack whose story item id another installed pack already uses
  gives the new pack's item an id of its own (`key-<pack id>`), with its quests, chests and behaviours changed to
  match, so each key opens only its own door in a world with both. Updates keep the new id.
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
- **A joined player sometimes got no islands:** when Raft's world-arrived event never reached the mod, the
  joining player never asked the host for its islands. Now it asks anyway 10 s into the game.
- **The credits at a story's end:** no island banner shows faintly behind the top of the credits card any more (the
  story's last banner waits until the credits are closed).
- **Main story quests stay out of the journal's side quests:** a main story island's further quests, and the quest of
  a generated island in the main story (a *type* rule), were listed in the journal under "Other islands with a quest";
  they count with the main story now, whose steps are in Raft's notebook. A copy of a story island in the
  random pool (a library import such as "Thornwood (The Long Voyage)") no longer shows as a side quest still to come. The guide has the rule for new story lines
  (every island the story depends on gets a place in it); all five story plans keep it.
- **Machinery placed as decoration stays decoration:** an island script's (recipe's) generator, radio, cage, hatch or
  lift placed without a name no longer gets the Ready pieces' working settings; one given a name does.
- **Survivor in a cage:** the cage no longer has a loose red rope lying on the ground beside it (the rope Raft's
  shark cage hangs from at Radio tower). In the editor and in a world, for every player.
- **A generated island whose file was gone** is made again when the world loads: in a session where the editor
  had already been opened, remaking it failed with an error (the generator's defaults were set up too early), so the
  island was missing from the world.
- **Flying islands seen from below:** the rocky underside now meets the ground all round the coast. Where the
  coast ran diagonally it hung down too early, and from below the sky showed through zigzag gaps between the rock and
  the ground (Lantern Spire showed it). Every flying island, made by hand or generated, at any height.
- **The island library's picture** no longer stays on the next entry you click: while that entry's picture is
  on its way the space is empty and its number says **(loading)**, or **(can't be loaded)** when it fails. A slow
  download had left one island's picture showing on every entry clicked after it.
- **The generator's reach line** (how far the island reaches from the raft) now measures the island Generate
  makes: on Randomize existing it left out the caves and buildings that are not made there.
- **Presets and map types that ask for too much** (buildings over 25, quest steps over 8, features over 10) are
  kept in range when generating, so the report no longer lists quest steps that "found no place".
- **Randomize existing on Temperance's small islets** no longer offers (and picks) the Rebuild it designs
  of Temperance itself: a design now matches only its own Raft island, not every island whose name starts the same.
- **No more "AN ERROR OCCURED!" while generating with buildings or caves:** Raft's story scripts no longer throw
  while the generator loads Raft's islands for buildings and caves (also the core catalogue's scenes).
- **Test in the editor got stuck:** it sometimes went to the main menu with Load game open and then did nothing on
  later clicks (Raft's Load box had a broken link after the editor). A test now starts behind a cover with Cancel. If it
  can't start, you are back in the editor and the status bar says why. Back to the editor no longer disappears after 4
  minutes in the test world.
- **Hover texts outside the editor:** only the editor's status bar and the in-world window drew a control's hover
  text, so the main menu's windows (the plan picker, the island library...), the stats page and the alpha box never
  showed theirs. Where there is no such bar, the text now comes in a small tip by the mouse after a moment.
- **Private storages after a join:** when storages that were taken down or sank were forgotten at the host's
  save, a player already in the game kept the old list until they joined again. The host now sends the new list
  to everyone at once.
- **World plan help caught up:** the plan editor's Help box still said to click "Custom Islands plan" in the New
  Game box; it now says Choose plan..., and its Help box and ? popups explain Test this plan, Progression, Preview
  notebook and New main story....
- **Carried off an island by the raft:** standing on a custom island's ground or on its Raft blocks (a wreck,
  a house of planks), Raft could take the player for someone on the raft, so the raft's drift and rocking moved them
  along with it - tens of metres in a second when the raft was far off. Island ground now counts as an island, as
  Raft's own islands do.
- **Into the sea after a slow start:** on a busy computer the island a player had saved on could spawn later than
  the 20 seconds the mod held them for, and they fell into the sea. The hold now lasts while that island is still
  coming, up to two minutes.
- **Held by a dead warthog:** a warthog killed while carrying a player on its charge (another player's spear, say)
  never let go: the player hung on the dead animal, unable to move, until they left the world. A dead warthog now
  drops them, as Raft does when the carried player dies.
- **An error picking up a crate:** picking up a crate (or anything that gives several items at once) with an empty
  hotbar slot selected could put a fishing rod or another item that charges up in hand and away again at once, and
  Raft then threw an error (a NullReferenceException) in the middle of the pickup. It no longer does.
- **Joining again on a ghost raft:** a player who left while standing on a ghost raft (a deck just above the sea)
  could come back on the host's raft: the deck wasn't loaded yet on their PC, so Raft took them for a swimmer and
  moved them. They are now held where they stood until it loads, as on any other island.
- **A broken player spot in a world file:** a spot saved as "NaN" or "Infinity" (a damaged or hand-edited file) was
  read as a number, and the player joining again was moved there. Such a line is now left out, like any other broken line.
  The same goes for the distance the raft sailed towards the next ghost raft: "NaN" there kept them away all session.
- **GenerateIsland with "NaN":** typed as a size, height, roughness or object amount it stopped the command with an
  error. Such a value is now left out, as if it wasn't given.
- **Generated radio towers in a world are whole again:** an island the world generated (a large island with
  a radio tower) laid the tower's floors at a third of their size when Raft hadn't loaded those pieces yet, so its walls
  and furniture stood partly in the air. Islands generated from now on get each piece's own size; the editor's
  generator was never affected.
- **Comfy bed missing from the crafting menu:** it copied the wooden bed, a bed *skin* with no crafting tab of its own,
  so no tab listed it. It now sits in the bed's own tab.
- **Trader raft and a bad world file line:** an `@tradersailed` of NaN or Infinity is now read as 0 (it stuck, and the
  trader raft never came again).
- **A quest step done early counted twice:** a zone step reached early and entered again (a player set down inside it
  after a load) was kept as done twice; early work now never counts more than the step needs.
- **Upgrade table taken away while in use:** the items in its slot go back to the player who had its menu open
  (Raft destroyed them with the table).
- **Barrels switched off:** the silver and golden barrels already in the sea turn plain-coloured at once
  (they lost their extra loot already but kept the colour until Raft spawned them again).
- **Boss arena re-arms:** after a lost fight (the stakes sank after two minutes) stepping back into the arena
  shuts the crew in again while the beast lives. Boss islands generated before keep the old one-shot arena.
- **Rogue shark switched off:** a rogue that was lost (not found after a load) no longer brings a new one
  when the Rogue shark option has been switched off meanwhile; it is let go.
- **Editor commands:** `SetElevation`, `ChangeWidth` and `ChangeStrength` with `NaN` show their usage instead of
  setting a value that is not a number.
- **Upgrade research table, two players:** when two players researched the same item at the same moment, the
  second one lost the item; it now goes back to that player. A world file's research lines with unknown items or
  upgrades (edited by hand, or from an upgrade a later version dropped) leave those out.
- **No errors when the editor takes objects from Temperance or Utopia:** two of Raft's story scripts there threw an
  error each time the editor loaded those islands (switched off) to take their objects; they are kept quiet then.
- **Placed objects stay as placed:** a few of Raft's objects (Varuna Point's debris planks) kept Raft's physics and
  tipped a little each time their island was saved and opened again; every placed object now stands still.
- **Things on generated islands lie on the ground:** metal sheets, firewood, tyres and pallets on a slope now lie
  along it instead of being half buried; a set piece (a stranded boat on a beach edge) is never sunk more than half
  its height; a lookout tower's back railing no longer runs off its deck and its side railings have no gaps. Shore
  ice on snowy islands lies at the waterline without the beach's sand rising over it, and only on gentle beaches.
- **The library's story islands stand on the ground:** a check of every island of every world plan found 73 things
  out of place on 28 islands - walls hanging over a slope, a radar screen wider than its desk, crates and chests set
  into banks, paintings, a camera and tarps off their walls and posts, a wind vane wider than its mast, ice pillars
  inside a den's walls. Each is moved, re-seated or given something to stand on.
- **No pause at the first random island of a session:** the first pick read every pool island's whole file for its
  "not before" distance (a fresh host with 130 pool islands: 0.5-0.7 s a pick on average over the first 20); the
  distances are now kept in `notbeforecache.txt`. The world plan's first look through a session's islands and the
  first pick no longer read every island's whole file either (a world of 152 islands: 9.4 s): an island's own rules
  come from `rulescache.txt`, and an island never reached isn't read to see whether it is finished.
- **A damaged downloaded island is fetched again:** a player's kept copy of the host's island that was cut short (a
  crash while it was written) was used as it was, failed to read and the island never appeared; its content is now
  checked when joining and a damaged copy is downloaded again.
- **A world folder renamed by hand:** Raft still loads it, and now the mod does too - the plan, levels and island
  state come from the folder Raft picked instead of being lost.
- **No hitch for a player who joined many worlds:** each random pick checked every kept copy of a host's island
  on disk again (up to 0.7 s a pick with hundreds of copies); the answer is now kept for a few seconds.
- **No hitch when a random island is chosen:** with many saved islands, picking the next one read the islands folder
  again for every line of spawnpool.txt (a quarter of a second with 1,600 islands); now about 20 ms. A 20-hour world
  (150 islands, a 200-page journal) was measured to load, save and show its Receiver dots without trouble.
- **Snow and cream animals are light now:** the world randomizer's light colours only multiplied the animal's
  texture, so a dark animal such as the warthog stayed almost as dark as Raft's own. They now lift the dark parts
  first, then tint.
- **Sea animals left behind no longer count as defeated:** Raft takes its harmless sea animals (dolphins, turtles, stingrays, whales) away after a while, and animals far from every player; the mod counted each one as killed, so an island's animals could be "defeated" - and the animals gone for good - just by sailing past. Those animals now count as taken away, not killed: they come back with the island. One the players kill still counts as defeated, also when Raft removes it at once (a stingray).
- **Dens on Raft's big islands:** the world randomizer's den had stopped fitting on any of Raft's big islands, as the
  ground under its rim had to stay within 1.5 m. The rock outcrop over the den now closes a drop down to the ground, so
  the den fits where the ground falls up to 3 m under its rim, and its rim still meets the ground all round.
- **Daily quest kills in multiplayer:** with the level up system off, monsters another player defeated (and the
  host's own) did not count for a "Defeat monsters" daily quest. They count now, with levels on or off.
- **Frozen over the sea when joining:** a player who joined could be held in place over the open sea (and bitten by
  the shark) while the mod waited for the island they had stood on - it also waited for the randomizer's extras on one
  of Raft's islands, which never load while that island isn't there. It waits only for an island that can be under the
  player, at most 30 seconds, says so on screen, and lets go at once if the player is hurt.
  And a player who joined onto the raft before its blocks came in is no longer held still while the raft drifts
  on without them (they dropped into the sea by the shark): the raft is left to Raft.
- **Caves that floated:** the randomizer's caves (on Raft's big islands and on generated islands) could stand with
  their rim over the ground, so you saw into the rock from outside and through the floor from inside. On a generated
  island the ground now rises round the cave and buries its rim, open only at the mouth; on Raft's islands a cave goes
  only where the ground meets its rim all round and the floor inside is even. And in a world every cave piece - generated,
  the randomizer's and those placed by hand on library islands - now sits inside a rock hill built over its back and
  sides down to the ground, of the island's own rock (snowy rock on a snowy island), open only at its mouth: no more
  thin shells in the air or sky seen through the mountain (nor a hole in the hill over a den's roofless stretch, nor a slot under a den's low edge where it hung over the ground).
  The hill stops where a cave's roof ends, in a face down to the ground: the way in, and the ledges and paths in front
  of the mouth and beside it, stay clear.
  Balboa's vine cave, a whole rock of its own, is left as it is.
- **See-through caves (X7):** from inside a cave you could see the sea or the sky where the island's ground ran
  through a room (Raft draws the ground from above only), and from outside - in the editor, or over a gap - a cave's
  rock wasn't drawn at all. Every cave piece's rock is now drawn from outside too, and the ground in a cave's rooms is
  let down under the room's floor and cut away where it lies wholly inside or runs steeply through (a hole dug down to
  a tunnel's end, as at The Drowned Labyrinth); in the editor the ground keeps the heights you sculpted. The rock hill over a cave no longer rises out of pits and dug stairs well away from the cave (it blocked Ashfall's crypt stair, 25 m from its bear den). `CICaveInside` looks round every cave piece from inside and fails on any look out.
- **See-through rooms (X7):** Selene's room pieces (walls, ceilings, floors, door frames, tunnels) were drawn from
  inside only too: a room's outer wall, or its ceiling seen from above, showed the room through it. They are drawn
  from both sides now. Hollow Mountain's rocks no longer stand inside its entry hall and granary, its high hall has a
  wall over its windows up to the ceiling, its lake roof reaches the islet and the snow no longer shows through the
  entry hall's floor; Ashfall's crypt slab covers the crypt and the ash is banked up to its edges (cut again after the
  crypt's pit, which had left thin slits under them), so the sky no longer shows under them. The Last Kingdom's crown
  vault has a wall with a doorway on its stair side and its sliding doors across that doorway (the pit and the stairs
  showed beside them), and the hill no longer comes in at its back corners. Rooms built from Selene's pieces in the
  recipes (`selene_cell` in `lib_selene`) had a slit of sky along the top of every wall: the walls stop 0.23 m under
  the ceiling, where Selene's own rooms put ceiling edge pieces; the walls (and the doors in their doorways) are drawn
  5 % taller now to meet the ceiling. Where a room opened straight onto Selene's ladder shaft (The Island of Stations,
  Shelter Atoll), the ground showed in slits beside the shaft's mouth and over it; the room has a doorway into the
  shaft there now. `CIRoomLooks` looks round a room from inside and
  `CISeleneBacks` checks the pieces' backs.
- **"Another build" told again after a rejoin:** a player who rejoined with the host's own build after joining
  with another one still got the old notice. A notice is now only about the host the player is with.
- **Animals walking into the sea:** llamas, goats and the other animals of the custom islands could wander into the
  shallows. They now stay on dry land (the beach just at the waterline at most).
- **The crafting menu:** the Weapons list was twice as wide as its recipes and covered the inventory (the upgrades
  not learned yet counted in its width); lists are now as wide as the recipes they show. Switching category now closes
  the last category's recipe box (the stone axe stayed open over Food/Water).
- **A new plan with an old plan's name:** deleting a plan and making another under its name gave the new plan to the
  worlds made with the old one. Each plan now has its own id, and those worlds keep their own copy.
- **EXP of a new alpha:** a monster that was already hit before the randomizer made it an alpha (switched on or
  re-seeded mid-game) kept its old, lower EXP worth. The worth is now worked out again when its max health changes.
- **Randomizer finds on a fresh island:** for a moment after the randomizer chose extras for one of Raft's islands,
  their file wasn't written yet; a look at it then logged "Could not read island 'rnd-…'" and could move the extras
  off the island. Their size is now known before they join the world.
- **The host's distances for other players:** the unload and Receiver distances went to other players in whole
  metres, so a host's 812.5 m was 813 m on theirs. They now go as they are.
- **Nothing ticked in Choose islands:** a world with every island unticked logged "the spawn pool is empty" every
  10 seconds of sailing. It says so once now, and the World settings button says `none`.
- **Two players in one once-only zone:** when both stepped in within a second, the one who came second was told
  nothing. They now see "Someone else got here first".
- **Very high EXP:** an EXP total past about two billion wrapped round to a negative number and broke the level count.
  It stops at the top now.
- **Fireworks give EXP:** Raft counts a firework's hit as the world's, so a monster hit by one gave no EXP. The
  player who lit it now gets the EXP, also in multiplayer.
- **Rig Seventeen's valve test works:** its lever looked for three valves that had no name, so the crew stash never
  dropped open. The aqueduct's three wheels are named too.
- **Crater Lair's roof laser works:** the mirror and the panel had lost their names (two settings on one line), and the
  panel stood just off the turned beam. Turning the mirror now lights the panel and opens the roof safe.
- **A Raft update that removes one of its objects no longer mixes up an island's trees:** the trees and items after the
  missing object keep what your world saved for them (picked, chopped, regrowing) instead of moving one place along.
- **A busy host no longer loses a chest:** when the host's answers to "may I open this?" kept arriving late (busy sending
  island files), player 2 asked again for ever and nobody got the chest. The host now holds it longer for that player.
- **Private storages and the hammer:** another player could take someone's private storage down with the
  hammer (and get what was inside). Only its builder can now (or the host, when the builder isn't in the game).
- **Build cost refunds after raising the cost:** in a world whose blocks were all built at Raft's own cost, raising
  the build cost and loading the world again made the earlier blocks count as built at the new cost, so taking them
  down gave back more than was paid. The world file now notes the cost they were built at.
- **Worlds from a newer version:** an island line with more fields than this version knows was dropped and then
  erased at the next save; the island now loads and its extra fields are written back as they were.
- **Older worlds save the same twice:** a world without a plan owner got one only at its next load, so each save of
  an older world changed the file once more; the owner is now written at the first save.
- **Another author's pack with the same id** was installed as that entry's update, over its files; it is an entry of its
  own now (`<id>-<author>`), and the import says so.
- **Story texts that disagreed with their islands** (Heart of Fire, Drowned Labyrinth, The Safe Room, Hightide Harbor,
  Glacier Station, The Rock Pen, Thornwood, Wreckers' Cove) and notes naming things their chests don't give (Floe Reach,
  Hermit's Table, The Great Fen, Mayor's Wharf, Shelter Atoll, Rookery Cliffs, Keel and Mast). The Abyss's last chapter
  no longer sends you back through every air pocket.
- **An island rule bringing its own island** brought a new copy each time a copy's rule fired (with "on day 2", one
  every second); a world now takes at most 10 copies that way.
- **A plan rule waiting for an island's Quest 2** (or later) was taken by the quest editor as the main quest's and could be
  overwritten on Save; it is left alone now, and World plans have a **quest no.** field for "When a quest is done" (the
  plan check says when the island has no such quest).
- **The New Game box forgot the plan** chosen for the last world: it now offers it again, like World settings (`newworld_plan.txt`).
- **Library buildings no longer float:** floors and decks over the water or a slope on the library islands now
  stand on posts down to the ground or sea floor (recipes stand them at the save; `posts off` leaves a deck
  meant to hang). `CIFloating` also names floors over nothing and chests with nothing under them, and
  `CIFloatingSweep` checks every saved island (the randomizer's extras left out).
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
- **Boss health:** an animal's health can be set up to Ã—12 (was capped at Ã—4), so the lair bosses set to Ã—5-Ã—12
  are as tough as written. Damage stays at most Ã—4.
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
