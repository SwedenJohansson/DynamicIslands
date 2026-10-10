# Play test format (.play)

A play test brings a saved island into a world and plays it as a player would: walks, climbs, reads, opens, uses,
fights and checks the quest. A plan's test plays a world plan's story the same way. The player is
`DevTests.PlayRoutine` in `DevTestsPlay.cs` (dev builds only).

## Running one

- `CIPlay <name>` reads `Mods\DynamicIslands\recipes\<name>.play`: the same folder as recipes
  (`DevTests.RecipeFolder`), not a tests folder. Sources are in the repo at `content\tests\`; nothing copies them.
- Host only, in a world (`LoadSceneManager.IsGameSceneLoaded`, `Raft_Network.IsHost`).
- Use a test world whose name starts with `CI ` (make one with `CINewWorld <name>` from the main menu). `plan` refuses any
  other world. In a `CI ` world, `island` also empties the player's inventory.
- The file is expanded like a recipe (`ExpandRecipe`): `#` comments, `set`, `$name`, `{expr}`, `for`/`next`,
  `macro`/`call`, `include` (see `RECIPE_FORMAT.md`).
- `include` adds `.recipe` unless the name ends in `.play`, so write `include signal_rock.play`.
- Files: `<island>.play` (the test), `<island>_pics.play` (guide pictures), `plan_<name>.play` (a plan's story),
  `diag_*.play` (aids).

Example (from `bell_stack.play`, shortened):

```
island The Bell Stack
offset -0.7757 -2.2826
expect step 0
expect title The Torn Chant
expect hidden cellbridge
walk -12 -38.5 to -12 -37.5 h=0.6
zone landing
expect step 1
read Chant, first page
walk 0 -19 to 0 -12 to 0 -6 to 0 -4 h=9
use bell
expect message isn't whole
```

## Coordinates

- Metres, as in the island's recipe: `PlayPoint` = the island entry's `Position` + (x - offset x, 0, z - offset z).
- `offset X Z` is the land centre's distance from the recipe's origin. The recipe's `save` step logs the values.
- `h=` is metres above the island entry's `Position.y` (the sea, for an island at sea level). `y=` is metres above what
  is at the point.

## Steps

| Step | Arguments | What it does |
|---|---|---|
| `island` | `<island name> [keep]` | The island (by file name) is brought into open sea within 400 m of the raft (`ScSpot`, `ScBring`); with no room the raft is moved 600 m on, up to 4 times. Copies of it already in the world are removed first; the crew's story items of the island are taken back. In a plan test: the copy the plan brought; the player is moved near it if it is over 300 m off or not loaded. |
| `offset` | `<x> <z>` | See Coordinates. |
| `stand` | | The player on the highest gentle spot near the island terrain's top (`StandRoutine`). |
| `at` | `<x> <z> [y=\|h=]` | The player put at the point, on what is there. |
| `walk` | `<x> <z> to <x> <z> [to <x> <z> ...] [h=] [below=] [blocked]` | The player is put on the first point, then walks the rest with Raft's controller (`PlayWalk`). Each point must be reached within 0.7 m before 2 s stuck. `h=`: must end within 1.6 m of that height. `below=`: start on what is under that height. `blocked`: passes when the walk does not get there. |
| `climb` | `<x> <z> [h=]` | The nearest collider whose name contains `climb`, within 3 m; the player 1 m above its foot. Passes when Raft's controller is climbing within 3 s. |
| `zone` | `<id>` | The trigger zone with that id (any case) is entered (`ScEnterZone`). |
| `air` | `<zone id>` | A shown zone with Air: a player with 10 % breath has over 90 % a moment later. |
| `read` | `<title>` | Reads the note whose title contains the text, any case (or the note of a chest). |
| `open` | `<title>` | Opens the chest whose note title is the text (`ScOpenChest`). |
| `reach` | `<title>` | That chest is within 2.5 m of the player's eye with nothing solid between (`ScReach`). The player is not moved. |
| `openat` | `<x> <z>` | Opens the nearest chest; it must be within 4 m of the point. |
| `use` | `<name> [stay]` | Fires `use` on the object (`Behaviours.Fire`). The player is put next to it (`ScUse`); with `stay`, used from where they stand (a lift carries them). |
| `kill` | `<label>` | Waits up to 15 s for the island's animals of that label, then defeats all of them (`ScKill`). |
| `catch` | `<label> [n]` | Catches n (default 1) of the island's own spawner animals (`ScCarryHome`). |
| `plan` | `<plan name>` or `end` | Clean story slate (`PlanCleanSlate`), then the world gets the plan (`ScSetPlan`); `end` puts the world's story back to Raft's. |
| `note` | `<Raft story island>` | Reads Raft's note that gives that island's frequency (`PlayNote`). |
| `tune` | `<rule>` | The Receiver tuned to a plan island's frequency (`TuneTo`); the island it brings is played next. |
| `arrive` | `<rule>` | Waits up to 90 s for the island a plan rule brings by itself; it is played next. |
| `sail` | `<km>` | Adds that many km to `WorldDirector.Sailed` (for a plan's km rules). |
| `expect` | see below | A check. |
| `wait` | `[s]` | Waits s seconds (default 1). |
| `hour` | `<h>` | Time of day (`AzureSkyHour`). |
| `weather` | `[name]` | Raft's weather whose name contains the text, set at once (`PlayWeather`); with no name or no match, logs the weathers. |
| `log` | `<text>` | Writes the text to the log. |
| `where` | `<name>` | Logs where the island's objects whose name starts with it are, in test coordinates. |
| `picture` | `<file> <x> <h> <z> <lx> <lh> <lz>` | A 1280x720 picture with Raft's camera to `recipes\play_<file>.jpg`. Heights above `Position.y`, or `+h` above what is below. |
| `beam` | `<emitter>` | Traces a laser emitter's beam now and logs it: where it starts, its points, the mirrors, what it ends on, and the emitter's renderers (their middle is the beam's start). Diagnostics only: checks just that the emitter is there |
| `snap` | `<file> <object> [from] [dist] [up]` | A picture like `picture`, framed on the object's middle: seen from compass bearing `from` (0 = from the north, default 180), `dist` m off (default twice its size, at least 4), `up` m higher (default 0.4 x dist). Logs where the object is. |

## Expectations (`PlayExpect`, plus `chain` and `spinsown` in `PlayRoutine`)

| Expect | Passes when |
|---|---|
| `expect step <n>` | The island's quest is at step n (0 = the first). |
| `expect title <text>` | The quest's title is exactly the text (case counts). |
| `expect done` | The quest is done. |
| `expect story <id> <n>` | The crew holds exactly n of that story item. |
| `expect shown <name>` / `expect hidden <name>` | The object is shown / hidden. |
| `expect message <text>` | One of the last messages contains the text (any case). |
| `expect item <item> <n>` | The player has at least n of the item (Raft's unique item name, as in `content\objects\items.txt`). |
| `expect animals <label> <n>` | Exactly n of the island's animals of that label are alive. |
| `expect height <h>` | The player is within 1.2 m of h above `Position.y`. |
| `expect stand <x> <z> h=<h> [below=<m>]` | What is at the point is within 0.6 m of h above `Position.y`. |
| `expect spinsown <name>` | The object turns about its own up axis. |
| `expect chain <rule\|Raft story island> [done\|unlocked\|locked]` | Its place in the story chain (default `done`), allowing up to 15 s. |

## Pass and fail in the log

- Each check logs `[CITEST] PASS: <what>` or `[CITEST] FAIL: <what>`. The test goes on after a failed check.
- An unknown step or expectation is a failed check, not a stop.
- The last line is `[CITEST] PASS: play <name> (<n> checks)`, or `[CITEST] FAIL: play <name>` if any check failed.
- These stop the test at once with `[CITEST] FAIL: play <name>...` and no last line: no file, not host in a world,
  an expand error, a step that needs an island before `island`, the island not loaded, the island didn't come, no
  open sea, `plan` outside a `CI ` world.
- At the end, islands brought by `island` are removed unless the last `island` line had `keep`; after a `plan` without
  `plan end`, the story is put back to Raft's. A test that stopped early leaves them; the next run's `island` removes
  old copies.

## Traps

- Before an island, only `island`, `log`, `wait`, `hour`, `plan`, `note`, `tune`, `arrive` and `expect chain` may run.
- `island`, `tune` and `arrive` reset the offset to 0. Put `offset` after them.
- `wait` is seconds here; in a recipe it is frames.
- `read` matches part of a note title. `open` and `reach` match a chest's `note.title` setting whole (any case).
- `use`, `expect shown`, `expect hidden` and `expect spinsown` match the object's `obj.name` setting
  (`BehaviourProps.Name`, read by `IslandObjectRef.Name`) whole, any case; set it in the recipe with
  `prop <alias> obj.name=<name>`. `where` matches object names.
- `expect shown`, `hidden`, `spinsown`, `story` and `chain` take one word; `use`, `read`, `open`, `zone` and
  `expect title`/`message` take the rest of the line. `chain` and `spinsown` must be written in lower case.
- Animal labels may have spaces; the last word of `catch` and `expect animals` is the count. A label like
  `Warthog 1.4` also asks for that size (`AnimalsOf`).
- `kill` defeats every animal of the label. `catch` takes only animals of the island's own spawners.
- When a recipe's land changes, update `offset` in both `<island>.play` and `<island>_pics.play`.
- A plan test includes each island's test (`include <island>.play`) after its `tune`; in plan mode that test's `island`
  line plays the plan's copy, so the island tests must work there too.
