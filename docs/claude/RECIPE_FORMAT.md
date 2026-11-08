# Recipe format (.recipe)

A recipe builds an island, or a world plan, through the island editor's own operations, one step a line. Most steps are
undo steps, as when a person works in the editor. The player is `DevTests.RecipeRoutine` in `DevTestsRecipe.cs`
(dev builds only).

## Running one

- `CIRecipe <name>` (RML console or `dev_commands.txt`). It reads `Mods\DynamicIslands\recipes\<name>.recipe`
  (`DevTests.RecipeFolder`). The name may contain spaces.
- It opens the editor when needed (`WaitForEditor`), so it runs from the main menu or the editor.
- Sources are in the repo at `content\recipes\`. The game reads only `Mods\DynamicIslands\recipes\`, and nothing in the
  repo copies them there.
- Pass: `[CITEST] PASS: recipe <name> (<n> steps, <m> objects placed, [<k> left out under the ground, ]<t> s[, saved '<island>'])`.
- Fail: `[CITEST] FAIL: recipe <name>, <file>:<line> (<line text>): <error>`. The first error stops the recipe.
  An expand error is `[CITEST] FAIL: recipe <name>: <file>:<line> (<line>): <error>`.
- After a run, `DevTests.RecipeSaved` holds the saved island or plan name, and `DevTests.RecipeObjects` the last object
  placed under each alias (other tests read them).

## File structure

- Blank lines and lines whose first non-space character is `#` are skipped. A `#` later in a line is not a comment.
- Words are split at spaces. `"double quotes"` keep spaces together and are dropped (`Tokens`).
- Text arguments run to the end of the line (`Rest`); there the quotes stay. In text, `\n` is a new line (`Unescape`).
- Numbers use `.` as the decimal point. Step names ignore case.
- The whole file is first expanded (`ExpandRecipe` -> `ExpandLines`), then the steps run in order.

An island recipe (from `bell_stack.recipe`, shortened):

```
include lib_huts
new
gen Seed=9114 Radius=45 Height=22 Shape=3 Style=1
gen SeaFloor=0 Shelf=0.45
generate
brush flatten at 0 -26 r=10 s=20 frames=80 h=5
push at -6 -29 h=5
call floors w=2 d=2 y=0.1
place Block_Wall_Wood - at 0.75 0 y=0.1
roof 2 2 at 0 0 y=0.1 wood
place Note_Papers page2 at 1.2 1.2 y=0.2 drop yaw=30
pop
note page2 Chant, second page | II.\nThe pilgrims slept here and went home.
info title=The Bell Stack
prop bell obj.name=bell
quest title=The Torn Chant
step reach|landing|1|Land at the foot of the Bell Stack
save The Bell Stack
```

A world-plan recipe has only `plan` lines (from `plan_citest.recipe`):

```
plan new CI Plan Recipe
plan description A test plan made by the recipe player.
plan random off
plan story on
plan leaveout Balboa
plan rule rock | island:Signal Rock | start | receiver:500 | A weak signal crackles. | Signal Rock | after:RadioTower | quest
plan save
```

## Preprocessor (`ExpandLines`)

| Keyword | Meaning |
|---|---|
| `set <name> <value>` | A variable. The value is stored as a number when it works out as arithmetic, else as text. Names ignore case. |
| `$name` | Replaced by the variable (letters, digits, `_`). An unknown `$name` stays as it is. |
| `{expr}` | Worked out after `$` replacement, innermost braces first. |
| `for <v> <from> <to> [step]` ... `next` | A loop, `to` included. Step may be negative. Step 0 or more than 5000 rounds is an error. |
| `macro <name> <p1> <p2=default> ...` ... `end` | Defines a macro. Its body is expanded at each `call`. |
| `call <name> <args>` | Args by position or `param=value`. A missing param with no default, or one arg too many, is an error. |
| `include <file>` | Reads `<file>` from the recipes folder; `.recipe` is added unless it ends in `.recipe` or `.play`. It shares variables and macros with the file that includes it. |

- Arithmetic (`ExprParser`): `+ - * / %`, `^` (power), unary `-`, `( )`, `pi`, `sin cos tan` (degrees),
  `atan2(y,x)` (degrees), `sqrt abs min max floor ceil round`, `rnd(a,b)`.
- `rnd` is seeded from the top file's name, so a recipe gives the same numbers every run. `scatter` without `seed=` draws
  from the same generator.
- A `set` inside a `for` body or a macro call stays inside it. Nesting deeper than 40 is an error.
- Shared macro files: `lib_huts`, `lib_rt`, `lib_sea`, `lib_relay`, `lib_selene`, `lib_features`, `lib_caves`.
- `lib_caves` (G6 cave kits, look-checked once in Raft: inside and out by day and night, the X7 probes, host + box):
  `call kit_st x z yaw len [slope] [end=room|open] [size]` a straight cave (cave tool); `call kit_uw x z yaw len [slope]`
  an underwater straight cave climbing to a dry room; `call kit_dig x z yaw len [flat] [r] [slope] [room]` a dug tunnel
  (Dig brush); `call kit_sys x z yaw [side] ...` a joined system of three dug tunnels with two mouths. Each lays
  `caveloot` (dirt mush stone vines titanium light) and, with `gate=1`, `cavegate`; `as=` names the group. The macro
  file's header lists every option.
- `tools\content.ps1 cavecheck <recipe> [-machete]` (offline, `content\cavecheck.py`) checks where a recipe's caves go:
  a cave or dug volume or mouth 6 m from quest objects, islanders, signs, chests, ladders, spawns and dens; no cave over or
  under a quest object; no mouth on the main walk line (3 m from the line through the quest steps' targets); a vine-gated
  cave off the quest path before the machete step (`-machete` or `# cavecheck machete`: an earlier island gives it).
  `# cavecheck inside <alias> ...` marks objects meant to be in a cave. Exit 1 on FAIL.
- `prop <cave piece alias> cave.seacut=1` (`ObjectProps.CaveSeaCut`): the cave hill over that island leaves out its skin over
  deep sea and the shore ring (`CaveMountain.SeaCut`), so a mound raised from the sea floor closes no pool under it
  (Old Mine Islet). Off by default: on Shelter Atoll's islet the cut would take the whole sea side of the hill.
- `prop <cave piece alias> cave.rock=den` (`ObjectProps.CaveRock`): the cave hill's rock texture is multiplied by that piece's
  lit shell colour over the hill rock's (`CaveMountain.RockAsDen`); sand on gentle slopes stays. Ignored when a cave of the hill is tinted.
  `cave.rock=plain`: Raft's plain big rock, one rock for hill and den wall (the standard since 11-08; Shelter Atoll uses it).
- `prop <cave piece alias> cave.foot=dry` (`ObjectProps.CaveFoot`): the default for a bear den (`CaveTool.Room`) with no
  `cave.foot` of its own (`CaveMountain.DryFootFor`, 11-08); `cave.foot=wet` keeps the old foot. Dry walled mouths also get
  `CaveGround.Apron` (ground rounded 4 m out down to the den floor). `CaveGround.CutIn` lets
  ground down only where `Walled()` (rock on 5 of 8 sides within 25 m), `CaveMountain` drops loose skin cells (`DropLoose`),
  puts the sunk rim on dry sea-cut cells (ground > `DryBy` 0.3) and caps every way in at `DryWay` 12 m past its roof; a
  cell counts as past a mouth only within `DryWay` and `DryFront` 2 m of the mouth's width (a far tunnel's line no longer skips
  the rim), then `FootPasses` 3 smoothing passes over the skin (rim and corridor cells kept, roof cells held at half-way to the
  highest roof beside them + Over).
  The falloff also goes on past that piece's own mouth plane beside the opening (more than HalfWidth + `DryFront` off its
  centre line, on dry ground only), capped at ground + `ShoulderSlope` 0.9 x distance from the opening: a rounded shoulder, not a sheer face. 11-08: on dry land
  the hill's last `FlareHigh` 3 m over the ground curve out ever flatter from the hill's slope and dip `FlareSink` 0.3 m under the sand (a flared foot; a straight flare showed flat wedges), `SoftMin` (`SeamSoft` 2.5 m) rounding where the shoulder caps meet the hill, and `ShoulderRamps` (before the
  rim and again after `FootPasses`) caps every skin cell out past a dry mouth (to `DryWay` + 4 m) at ground - 0.6 +
  `ShoulderSlope` x its distance past the opening's HalfWidth (cut cells stay cut): the den's other pieces' roofs had
  kept the shoulders' inner faces sheer. 11-08 c: that cap curves (x (1 + d / `ShoulderCurve` 4)), reaches `BackBand` 6 m
  behind the opening's plane rising `BackSlope` 1.5 a m there (no seam where it stopped), and the capped cells get
  `RoundPasses` 3 lower-only passes toward their neighbours' mean (no crisp crest). Cells over a roof (`cover`) stay as they are.
  Without the prop all of it is skipped: other islands build byte for byte as before (Old Mine Islet checked).
- In Raft, `tools\cavecheckrun.ps1 <island> [...] [-player 2|3]` checks a built island's caves in one world load:
  CICaveGapsLook (pictures out/in by day and night, walked in and out, flooded parts), CICaveInside shots (X7) and
  CIReach (traps). One summary line per island plus the pictures; exit 1 on FAIL.

## Coordinates and units

- Metres and degrees. A point `x z` is measured from the recipe's origin, in the current frame.
- With no frame turned, `x` is the editor's world x and `z` its world z.
- The origin is (500, 500) at the start and after `new`. `generate` and `randomize` move it to the land centre
  (`EditorLandCentre`). `origin x z` sets it to an editor world point; `origin` alone sets it to the land centre.
- Heights (`HeightAt`): `h=` metres above the sea. `y=` metres above the frame's floor, or above the ground when the
  frame has no floor or `ground` is given. Neither: on the frame's floor if it has one, else on the ground.
- Frames: `push at x z [y=|h=] [yaw=] [ground]` ... `pop`. Points inside are turned with the frame and its yaw adds to
  the parent's. The frame has a floor when `y=` or `h=` is given, or when its parent has one (unless `ground`).

## Steps (`RecipeRoutine`)

| Step | Arguments | What it does |
|---|---|---|
| `new` | | A new island (`DynamicIslands.NewIsland`); origin reset, pending quests dropped. |
| `gen` | `Name=value ...` | Collects generator settings for the next `generate`/`randomize`. Several lines add up. |
| `generate` | | `IslandGenerator.GenerateInEditor` with the collected settings; origin = land centre. |
| `randomize` | `<part of a Raft island scene name> [like] [Name=value ...]` | `RaftIslands.VariationOf`, or `RaftIslands.LikeIt` with `like`, of the first `RaftIslands.Offered` island whose scene contains the text; then the sliders given. |
| `origin` | `[x z]` | See above. Clears the frames. |
| `push` / `pop` | see above | A frame for a building. |
| `style` | `Tropical`, `Snowy`, `Desert`, `Forest`, `Volcanic` or a number | The island's style (`StyleOf`). |
| `elevation` | `<m>` | Height above the sea, clamped to `IslandSpawner.MinElevation`..`MaxElevation`. |
| `island` | `key=value` | An island setting in `DynamicIslands.currentIslandProps`. Empty value removes it. |
| `info` | `key=value` | Same, with `info.` before the key: `info title=`, `info author=`, `info description=`. |
| `brush` | `<tool> at x z` or `<tool> from x1 z1 to x2 z2`, `[r=15] [s=4] [frames=30] [rot=] [h=] [line]` | A terrain stroke (`Brush`). Tools: `raise lower flatten smooth sample sand grass rock seabed auto stamp:<i>`. r 2-80, s 0.5-20, frames 1-600. `h=`: flatten to that height above the sea. `rot=`: the stamp's turn (plus the frame's yaw). `line`: the brush's own reach (pre-10-25), so a 2 m stroke along a row of ground points (every 1.953 m) moves that row only; without it a brush under 3.9 m reaches up to 1.4 points and pulls the next row too. |
| `cave` | `from x1 z1 to x2 z2 [slope=] [end=room\|open] [hill=1\|0] [size=] [as=group]` | The editor's cave tool (`CaveTool`, PF-D2): Raft's cave tunnels from the mouth (x1 z1) towards x2 z2, a bear-den room at the end or an open end, the ground raised over them; `slope=` degrees, below 0 down; `size=` 0.7-1.5 times Raft's own (default 1). |
| `dig` | `<x,z \| x,y,z \| x,y,z,r> ... [\| <next stroke> ...] [r=1.75] [slope=0] [fill] [as=group]` | The editor's Dig brush (`DigTunnel`): each stroke pressed at its first point and dug on through the others, as the mouse steers it. A point is x,z (on the ground), x,y,z (y m over the ground, below 0 under it) or x,y,z,r (the radius to there, 1.4-5 m). A stroke whose first point x,z is over a dug passage branches from it; a stroke ending in another tunnel joins it. `slope=` degrees up (-25..25; 0 level, which on flat ground first goes down until there is rock overhead); `fill`: Shift, fills dug tunnels back in at the points. |
| `caveloot` | `<group> [dirt=3] [mush=3] [stone=2] [vines=2] [titanium=0] [light=0]` | Raft's cave loot on the dry floor of the cave or dug tunnel laid `as=<group>` (`CaveLootStep`, DevTestsCaveGaps.cs, G6): dirt spots, mushrooms, stones, titanium ore and a metal lantern, from 5 m in (rock over the spot, no sky within 5 m, floor 0.3 m over the sea), to the passage's sides in turn; `vines` cave vines hung from the roof, scaled to leave 1.95 m to walk under (a vine with no room is hidden). Laid as `<group>_loot`. Error when no dry spot is found (it counts the points near open sky, without roof or floor, and wet). |
| `cavegate` | `<group> [scale=1] [at=x,z] [as=alias] [yaw=0]` | Raft's machete vines (`ChoppableVines`) across the mouth of the cave laid `as=<group>`, alias `<group>_vines`: the player cuts them with the machete to get in. `at=x,z`: across the group's way nearest that point instead (6 m at most from it) - a gated side gallery inside a dug system, whose branch strokes all start at the system's first mouth. |
| `clear` | `at x z [r=5]` or `from x1 z1 to x2 z2 [r=5]`, `[all] [body] [only=<regex>] [below=] [above=] [on=sand\|grass\|rock\|seabed]` | Hides the editor objects in the circle or along the line, as one undo step. Without `all` the recipe's own objects stay. `body`: by how near each object's body (its renderers' box) comes, not its pivot (big look pieces whose pivot stands metres off them). `below`/`above`: height above the sea. |
| `store` | `[alias\|-] at x z [options]` | Raft's trading post as a working store (`place TradingPost ...`, SL7 `RaftStore.cs`): Raft's goods and prices, plus the story blueprints the player lacks (Trashcube*6, one each). Use `ground`. |
| `vending` | `[alias\|-] at x z [options]` | Tangaroa's vending machine, working (`place VendingMachine ...`, SL7b `RaftVending.cs`): Raft's goods and prices (Tangaroa tokens; place `QuestItemPickup_Tangaroa_Token` pickups near it). Use `ground`. |
| `place` | `<Object> <alias\|-> at x z [options]` | One object (`Place`). Object `none` places nothing (an empty macro slot). A Ready piece gets its working settings (`ReadyPieces.Defaults`, CB1) only with an alias (or `as=`); with `-` it stays decoration. |
| `scatter` | `<Object> <count> at x z r= [options]` | Random spots in the circle: dry land by default, `wet` under the sea, `any` both. `deep=` max depth, `free=` metres clear of editor objects, `seed=`, `scale=a-b`, `yaw=` fixed (else random), `as=<alias>`. |
| `line` | `<Object> from x1 z1 to x2 z2 [step=1.5] [ends] [yaw=] [y=\|h=] [sit] [follow]` | A row, each object's x axis along the line. `ends`: pieces at both ends. `follow`: end to end over the ground (`LineFollow`). |
| `ramp` | `<Object> from x1 z1 h1 to x2 z2 h2 [len=6] [top=1] [rot=]` | Pieces end to end between two heights (above the sea, or the frame's floor), tilted and stretched. `len`: piece length along its x; `top`: walking surface above its pivot. |
| `roof` | `<w> <d> at x z [y=\|h=] [wood]` | A hipped roof (`RaftRoof.Hip`) over w x d cells of the 1.5 m grid, its -x -z corner at x z. `at` must be the fourth word. |
| `beside` | `<Object> <alias\|-> <of-alias> [d=2] [yaw=] [turn=]` | On the ground d m in front of the first object of `of-alias` (its facing turned by `yaw`), facing it (+`turn`). |
| `prop` / `beh` | `<alias> key=value` | An object setting on every object of the alias (`PropsCommand.Change`). Empty value removes it. The alias is not the object's name: actions and `state` checks find objects by `obj.name` only. |
| `zipto` | `<alias> x z` | A zipline's far end on the ground there (the `zip.to` setting, `ZiplineEnds.ZipTo`). |
| `loot` | `<alias> <items>` | Chest loot (`ObjectProps.LootItems`), e.g. `Wool*4;Rope*4`. |
| `note` | `<alias> <title> \| <text>` | The note editor's Apply (`NoteEditorWindow.Apply`). |
| `story` | `id\|name\|icon\|description[\|raft=<Raft item>]` | A story item added, or replaced by id (`StoryItemDef.Parse`). |
| `quest` | `title=`, `intro=`, `done=` or `reward=` (one a line) | The island's quest. `quest2`..`quest9`: further quests. |
| `step` | `type\|target\|count\|text` | A quest step. Types are `IslandQuest.Types`: `reach read open kill catch collect pages blueprint ... signal bring craft` (`blueprint\|Blueprint_X\|1\|text`: one of Raft's blueprints carried or learned while at the island; empty target = any story blueprint; `signal\|lever` or `signal\|Gate=open`; `bring\|Plank@Mara\|3`; `craft\|Rope\|2`). Up to 99 steps. `step2`..`step9` for further quests. |
| `questbring` | `<rule line>` | `QuestEditorWindow.SetQuestBringRule`. |
| `rule` | `id \| what \| when \| where \| message \| label` | An island rule (`IntroRule.Parse`); the same id replaces. |
| `posts` | `[off]` | Posts (`RT_Pillar`, stretched) under each floor or deck corner nothing holds, down into the ground or sea floor (`StandPosts`, the check `FloorsOverNothing`, ROADMAP_DONE CA30). `save` does it too; `posts off` leaves them out at the save (a deck meant to hang). |
| `reseat` | `trees` | Opt-in, at the save: trees and palms whose trunk foot is more than 1 m under the land (the ground check's "sunk trunk": a generator tree the recipe's strokes raised the ground round) set back on the ground round their pivot, the foot kept within 1 m under the land; one whose foot would be inside a placed object's collider bounds stays (`ReseatSunkTrees`, 2026-10-31). Without it the save is as before. |
| `save` | `<island name>` | Stands posts under floors over nothing (unless `posts off`), then saves the island (`DynamicIslands.SaveIsland`); the name must pass `FileNames.IslandProblem`. Fails first when an action or `state` check names an object no object is named, or an `obj.name` holds a second key=value from a joined `prop` line (`UnnamedTargets`), or when a `step kill\|<kind>` / `step catch\|<kind>`, or a `kill <kind>` under `island <this island>` in any `.play` of the folder, names a creature label the island doesn't place (`UnplacedCreatures`). |
| `plan` | see below | World plans window. |
| `log` | `<text>` | Writes the text to the log. |
| `where` | `<alias>` | Logs where the alias's objects ended up (x z from the first origin, `h=` above the sea, turn). |
| `wait` | `[n]` | Waits n frames (default 1). |

`place` options (also passed on by `scatter`, `line`): `y=`, `h=`, `ground`, `yaw=`, `tilt=`, `roll=`, `rot=x,y,z` (own
turn instead of the straightened spawn turn), `keep` (keep the spawned turn), `scale=` (0.05-20), `sx=` `sy=` `sz=`
(stretch one axis, 0.05-20), `centred` (visible middle at the point), `sit` (bottom at the height, not the pivot),
`drop` (then set down on whatever is under it).

## World-plan steps

`plan new <name>` (a valid name, not a built-in plan; opens World plans via `WorldPlanWindow.RecipeNew`),
`plan description <text>`, `plan ending <text>`, `plan credits <title> | <name> | <name>...` (the credits' title and
the makers' names, one per line; `WorldPlanWindow.RecipeCredits`), `plan random on|off`, `plan story on|off`,
`plan leaveout <Raft story island>`, `plan rule <id | what | when | where | message | label [| story place | done when]>`,
`plan save`. `plan save` runs Check (`WorldPlanWindow.RecipeSave`), logs each finding as `check: ...`, fails on any
problem, needs the file at `WorldPlan.PathFor(name)`, then closes the window. Every `plan` line except `new` needs a
plan already open.

## Traps

- `wait` counts frames here; in a `.play` test it counts seconds.
- Every `{...}` on any line, note and message text too, is worked out as arithmetic; text that isn't fails the expand.
  A `$word` that names a variable is replaced in text too.
- `gen` names are the public field names of `IslandGenSettings`, case-sensitive. Unknown names and unreadable values are
  silently ignored (`IslandGenSettings.FromText`).
- Before the first step, objects named in `place`, `scatter`, `line` and `ramp` lines are loaded
  (`PlaceableCatalog.EnsureLoaded`). One the editor doesn't have fails the recipe before it starts.
- `prop`, `beh`, `loot`, `note`, `zipto` and `beside`'s `of-alias` fail unless the alias was placed earlier in the same
  run (by `place`, `beside` or `scatter as=`); `where` with an unknown alias logs nothing. Aliases ignore case.
- Quest fields are written only once the quest has a step. `save` fails when the main quest has a title and no steps.
- An object placed with alias `-` at a set height is left out when it is under the ground whole; so are `line` and
  `ramp` pieces. Objects on the ground with no height go down to the lowest ground under their base; a tree, palm or bamboo (not over the sea) goes down by its trunk foot, the lowest ground within 0.5 m of its pivot, not under its whole crown.
- `save` hides Raft's sea finds that the recipe's strokes lifted out of the water, and plants, rocks and finds buried
  whole.
- A strokes line after a `place` can leave that object floating or buried: shape the land first, then place. Check
  each island with `CIFloating` (editor) and `CIGroundIslands <name>` (world) - CHANGE_CHECKLISTS "Everything stands on
  the ground".
- `save` logs `play tests of '<island>' need 'offset X Z'` when the land centre moved from the origin. Put that
  `offset` line in the island's `.play` tests (see `PLAY_FORMAT.md`) whenever the recipe's land changes.

## Helpers for writing recipes

`CIMeasureObjects` (writes `recipes\objects_<category>.txt`), `CIHeightMap`, `CISeaSpots`, `CIProfile`, `CIBoundsOf`,
`CIColliders`, `CIFloating`, `CIView` (writes `recipes\view_<name>_<n>.jpg`), `CIViewAt`, `CIProtoInfo`,
`CIQuestIcons` (`questicons.txt`), `CISounds` (`sounds.txt`), `CIScene`, `CIInstances`, `CIChestReach` (every chest
has a floor spot with room to stand and a clear line from the eye), `CILadderSide` (every ladder's climb side is
open; a ladder takes hold only from its climb trigger's side). All are in `DevTestsRecipe.cs`.
Object and item lists for writing recipes are in the repo at `content\objects\`.
