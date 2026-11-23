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
- Text arguments run to the end of the line (`Rest`); there the quotes stay. In text, `\n` is a new line (`Unescape`), and `\\n` stays a written `\n` (11-23): inside a `prop` event value (`on.choice1=journal|...`) a real new line starts the next action, so a journal page's or dialogue's own new lines are written `\\n` there and only the line between actions is `\n` (Camp Blackwater's, The Dead Letter Office's and Wreckers' Cove's islander pages were cut after their first line; `CIRecipeJournalBreak`).
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
- `python -I content\questcheck.py [--stage "dir;dir"] [--all] [--json f]` (offline, questdeepB 11-14) proves every shipped
  quest can be finished: reads the installed mod's plans + islands, the library deploy folder (+ staged copies) and every
  recipe's item lists; checks rule lines, story places, steps and their targets, story items/blueprints/titanium in play
  order, Raft item names (`content\objects\items.txt`), action lines cut by a real line break, compass words and
  chest wording; `note:` done-whens by number or title, for rules beside Raft's story too (part `BESIDE`), and story
  items with `raft=` that a has/take check or cage key needs held (T8 take-out, `story-items` INFO; quest softlock
  audit 11-23), and has/take checks of Raft items players use up (M9: a blueprint WARN, a food or drink INFO). `--mods`/`--stage` an empty folder and `--library <release folder>` check one release alone. ERROR = can't finish or silently lost; exit 1 on any ERROR. Its lists mirror IntroRule/IslandQuest/
  BehaviourProps.Verbs - keep them in step with the C#.
- `prop <cave piece alias> cave.seacut=1` (`ObjectProps.CaveSeaCut`): the cave hill over that island leaves out its skin over
  deep sea and the shore ring (`CaveMountain.SeaCut`), so a mound raised from the sea floor closes no pool under it
  (Old Mine Islet). Off by default: on Shelter Atoll's islet the cut would take the whole sea side of the hill.
- `prop <cave piece alias> cave.rock=den` (`ObjectProps.CaveRock`): the cave hill's rock texture is multiplied by that piece's
  lit shell colour over the hill rock's (`CaveMountain.RockAsDen`); sand on gentle slopes stays. Ignored when a cave of the hill is tinted.
  `cave.rock=plain`: Raft's plain big rock, one rock for hill and den wall (the standard since 11-08; Shelter Atoll uses it).
- Cave hill foot band (`CaveMountain.PaintFoot`, 11-21): in a world the island's ground paint round the hill is blended to sand (0.8) and rock (0.2), 0.9 at the skin's edge fading over `FootBand` 2.5 m with a ragged edge; not under the sea, not on the editor's measuring hill. The hill's grid and noise are laid from the island's origin (`anchor`), so the world's hill equals `CaveMountain.Preview`'s.
- `prop <cave piece alias> cave.foot=dry` (`ObjectProps.CaveFoot`): the default for a bear den (`CaveTool.Room`) with no
  `cave.foot` of its own (`CaveMountain.DryFootFor`, 11-08); `cave.foot=wet` keeps the old foot. `cave.dome=<m>` (`ObjectProps.CaveDome`, 11-10) caps a dry
  den's dome reach (side/back/front) at that many m, height kept, its roof cells keeping the hill's own slopes; unset = unchanged. Dry walled mouths also get
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
  11-18 (w15): beside a lone den's arch (a dry den with no `cave.plug`), cells on its plane and up to a cell behind it, past
  `ArchHalf` + `LoneSlot` off its line and over no roof or rim, are capped at floor + `ArchUp` + `LoneCheek`(d) +
  `BackSlope` behind the plane (map char `P`); they had kept the hill's full height, a sheer cut each side of the mouth.   `LoneCheek`(d) is `LoneSide` x d (curved) until its slope reaches `LoneSteep` 1.5, then straight on at that slope (it
  had grown to sheer walls far out); the lone ramp in front and the den-dome cap use it too. 11-18 w15X: the `P` cap also
  takes cells over the den's own roof, no lower than that roof + Over + `LoneRoofOver` 0.5 m (they stood at the dome's
  full height and their cut face showed over the mouth as torn strips). The
  build keeps the tallest step left there (`LoneSheer`, `LoneSheerAt`); `CICaveGapsLook` fails it over `LoneSheerMax` 5 m.
  11-20 (cavelay): a den whose arch top is under the sea (more than `SeaFloorBy` 1 m) takes the lone ramp's foot from
  the seabed under the sea, not `SeaLevel` - `SeaFootBy`: from the sea's top every cell was "within `LoneSink`" and dived
  to just under the seabed, a 9.8 m step beside Gravewell Shaft's diver den. Only the foot changes: the `o` rule still
  holds (letting the ramp out over the seabed raised Weatherglass Deep's bank beside its way from 0.8 to 4.8 m).
  `Band` lights a skin band taller than `BandTallFrom` 1.5 m more and more by its own face (full at +`BandTallOver` 3 m), not
  the ground's up normal: tall walls had shown as pale sheets.
  Without the prop all of it is skipped: other islands build byte for byte as before (Old Mine Islet checked).
- In Raft, `tools\cavecheckrun.ps1 <island> [...] [-player 2|3]` checks a built island's caves in one world load:
  CICaveGapsLook (pictures out/in by day and night, walked in and out, flooded parts), CICaveInside shots (X7) and
  CIReach (traps). One summary line per island plus the pictures; exit 1 on FAIL.
  Story gates (bars an event hides) are opened for the look; CIReach gets its own wait, `-reachTimeout` (5400 s).

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
| `cave` | `from x1 z1 to x2 z2 [slope=] [end=room\|open] [hill=1\|0] [size=] [as=group]` | The editor's cave tool (`CaveTool`, PF-D2): Raft's cave tunnels from the mouth (x1 z1) towards x2 z2, a bear-den room at the end or an open end, the ground raised over them; `slope=` degrees, below 0 down; `size=` 0.7-1.5 times Raft's own (default 1). Plants and loose rocks in its rock, on its way in or on its doorstep are hidden; building pieces there (`CaveTool.IsBuildingPiece`: `Block_*`, the RT_ walls/floors/fences/pillars/beams/stairs, reef-hut and ruin pillars) are kept and logged as `WARNING: cave: N building piece(s) in its way kept`, counted per island in the recipe save's `[CITEST]   WARNING: N building pieces in a cave's way kept` line (11-21: Overpass Shoal's culvert, laid after its deck, had hidden 21 deck floors, 7 rails and 2 pillars) - put the `cave` line before the deck it runs under. |
| `dig` | `<x,z \| x,y,z \| x,y,z,r> ... [\| <next stroke> ...] [r=1.75] [slope=0] [fill \| shaft] [as=group]` | The editor's Dig brush (`DigTunnel`): each stroke pressed at its first point and dug on through the others, as the mouse steers it. A point is x,z (on the ground), x,y,z (y m over the ground, below 0 under it) or x,y,z,r (the radius to there, 1.4-5 m). A stroke whose first point x,z is over a dug passage branches from it; a stroke ending in another tunnel joins it. `slope=` degrees up (-25..25; 0 level, which on flat ground first goes down until there is rock overhead); `fill`: Shift, fills dug tunnels back in at the points. `shaft` (11-08, The Rootcore): the stroke is laid straight through its points at any angle, not steered at most 25 degrees - a ladder shaft: `dig 10,-5 10,0.3,-5 shaft r=1.6` from the passage under 10 -5 straight up to 0.3 m over the ground there (the ground cut over its top); place the ladders and a landing yourself. Each stroke logs `[CITEST] dig: stroke from <first point> ends at x z, floor h= r= (ground h=)` (11-08): the floor height down there, for `h=` of ladders, rats and zones. |
| `caveloot` | `<group> [dirt=3] [mush=3] [stone=2] [vines=2] [titanium=0] [light=0] [wet=0]` | Raft's cave loot on the dry floor of the cave or dug tunnel laid `as=<group>` (`CaveLootStep`, DevTestsCaveGaps.cs, G6): dirt spots, mushrooms, stones, titanium ore and a metal lantern, from 5 m in (rock over the spot, no sky within 5 m, floor 0.3 m over the sea), to the passage's sides in turn; `vines` cave vines hung from the roof, scaled to leave 1.95 m to walk under (a vine with no room is hidden). Each piece is then set down on the lowest floor under its corners (at most half its height: a stone on a ramp stood in the air on its low side, 11-21). Laid as `<group>_loot`. Error when no dry spot is found (it counts the points near open sky, without roof or floor, and wet). `wet=1` lets the flooded floor count too: a cave of an island sunk by `elevation`, a flooded cave with an air pocket (`kit_st`/`kit_uw` pass `wet=`). |
| `cavegate` | `<group> [scale=1] [at=x,z] [as=alias] [yaw=0]` | Raft's machete vines (`ChoppableVines`) across the mouth of the cave laid `as=<group>`, alias `<group>_vines`: the player cuts them with the machete to get in. `at=x,z`: across the group's way nearest that point instead (6 m at most from it) - a gated side gallery inside a dug system, whose branch strokes all start at the system's first mouth. |
| `clear` | `at x z [r=5]` or `from x1 z1 to x2 z2 [r=5]`, `[all] [body] [only=<regex>] [below=] [above=] [on=sand\|grass\|rock\|seabed]` | Hides the editor objects in the circle or along the line, as one undo step. Without `all` the recipe's own objects stay. `body`: by how near each object's body (its renderers' box) comes, not its pivot (big look pieces whose pivot stands metres off them). `below`/`above`: height above the sea. |
| `store` | `[alias\|-] at x z [options]` | Raft's trading post as a working store (`place TradingPost ...`, SL7 `RaftStore.cs`): Raft's goods and prices, plus the story blueprints the player lacks (Trashcube*6, one each). Use `ground`. |
| `vending` | `[alias\|-] at x z [options]` | Tangaroa's vending machine, working (`place VendingMachine ...`, SL7b `RaftVending.cs`): Raft's goods and prices (Tangaroa tokens; place `QuestItemPickup_Tangaroa_Token` pickups near it). Use `ground`. |
| `place` | `<Object> <alias\|-> at x z [options]` | One object (`Place`). Object `none` places nothing (an empty macro slot). A Ready piece gets its working settings (`ReadyPieces.Defaults`, CB1) only with an alias (or `as=`); with `-` it stays decoration. Its height on the ground is read with the caves' ground cut made first (`CaveGround.EditorCatchUp`, IG5: a piece put beside a cave placed a moment before floated once the world cut the ground). |
| `scatter` | `<Object> <count> at x z r= [options]` | Random spots in the circle: dry land by default, `wet` under the sea, `any` both. `deep=` max depth, `free=` metres clear of editor objects, `seed=`, `scale=a-b`, `yaw=` fixed (else random), `as=<alias>`. |
| `line` | `<Object> from x1 z1 to x2 z2 [step=1.5] [ends] [yaw=] [y=\|h=] [sit] [follow]` | A row, each object's x axis along the line. `ends`: pieces at both ends. `follow`: end to end over the ground (`LineFollow`). |
| `ramp` | `<Object> from x1 z1 h1 to x2 z2 h2 [len=6] [top=1] [rot=]` | Pieces end to end between two heights (above the sea, or the frame's floor), tilted and stretched. `len`: piece length along its x; `top`: walking surface above its pivot. |
| `roof` | `<w> <d> at x z [y=\|h=] [wood]` | A hipped roof (`RaftRoof.Hip`) over w x d cells of the 1.5 m grid, its -x -z corner at x z. `at` must be the fourth word. |
| `beside` | `<Object> <alias\|-> <of-alias> [d=2] [yaw=] [turn=]` | On the ground d m in front of the first object of `of-alias` (its facing turned by `yaw`), facing it (+`turn`). |
| `prop` / `beh` | `<alias> key=value` | An object setting on every object of the alias (`PropsCommand.Change`). Empty value removes it. The alias is not the object's name: actions and `state` checks find objects by `obj.name` only. |
| `zipto` | `<alias> x z` | A zipline's far end on the ground there (the `zip.to` setting, `ZiplineEnds.ZipTo`). Since 11-20 it also keeps the line off the ground, at the island's `save` (once every later stroke and kit has shaped the land: Thalassa Mooring's line was clear at its `zipto`, then `kit_st`'s hill rose 6 m round its middle). When the near floor is on the ground (`zipfrom ... ground`) it makes the rope as Raft does in a world (`ZiplineEnds.RopeBetween`: `CreatePath` with the line's slack, so it sags - about 3.4 m on a 20 m line) and raises the near end in steps (`ZipRaise`, at most `ZiplineEnds.MaxRaise` = 8 m) until that rope leaves a rider `RiderClear` (2.6 m: the rider hangs about 2.3 m under it) over the ground - less towards the far end, where the rope comes down to its floor (`ZiplineEnds.RiderWant`: 0.8 of the rope's fall per metre, but the full room again from 5 m past the far end's left-out stretch, so a flat line is raised all the way - 11-21, Thalassa Mooring's 1.6 m fall over 23 m let its rider go 3 m out), leaving out the first 1.5 m and the last 5 m (`RiderFarSkip`); from there to the last 3 m (`FarSkip`) the room wanted never drops under `LineLow` + 0.1 m (0.6 m, 11-21: The Rain Engine's and The Tide Engine's lines came 0.3 m over the slope 3-5 m before their ends, where nothing was wanted). A near floor a later stroke buried more than 0.5 m under the ground (The Rootcore's, 3 m) is lifted onto the ground first (`ZipNearOutOfLand`), and a far end a later step raised the ground over (the `cliffs` talus under Caisson Yard's crane line, c3z61) goes back up onto the ground. A `zip.slack` the recipe set (a `prop` before the save) is the rope it starts from and is never loosened. The ground there is the land or anything solid standing on it in the land's layer (`ZipGround`: a rock, a cliff, a block - Caisson Yard's line ran into a rock pillar the land's height left out), as a world's rider meets it; the line's own pieces don't count. First it removes the generator's objects (not the recipe's, except what a `scatter` without `as=` placed - 11-21, Caisson Yard's `sk_plants` boulder on its ridge) whose top comes within a rider's room under that rope (`ZipClearStrays`, logged `zipto: N of the generator's objects in a rider's way ... removed`; bodies over 10 m across stay, logged `zipto: kept in a rider's way ...`, except boulders - Caisson Yard's scaled-up `SmallBoulder2` on its ridge): a `clear` along the line goes by pivots, and Caisson Yard's 5 m `SmallBoulder2` stood with its pivot more than 3 m off the line. Where Raft's own slack would want a tower, it tries a tighter rope (half, a quarter, a tenth of the slack - Raft's is negative, -6.92: the `zip.slack` setting, `ZiplineEnds.ZipSlack`, which `ZiplineEnds.Apply` sets in a world), then only the rope's own 1 m (`LineClear`). The ground under the rope includes the rock hill a world builds over the island's cave pieces (`CaveMountain.Preview` builds it in the editor for the check, then removes it; 11-21: Caisson Yard's summit rope ran 0.32 m into that hill), and the ground check measures it there too; a far end whose floor would stand under that hill's skin goes up onto it (`ZipFarOnSkin`, logged `zipto: its far end's floor stood ... under the skin ...`; 11-21 c3z13, Caisson Yard's summit line ended in the sand), and the ground check fails a rope that comes into the ground in its last 3 m too (`zipline far end in the ground`, `ZiplineEnds.FarStretchLowest`). Where the ground in those last 3 m comes up over the room the far end has at its own floor (a slope rising before it), the far end goes up till they stand `FarEndClear` (0.3 m) over it, `FarMaxRaise` (3 m) at most, its floor on a `Block_Floor_Wood` deck on posts, ladders down its far side when over 1 m up (`ZipFarClear`, logged `zipto: its rope came ... in its last 3 m ... its far end raised ...`; 11-21 c3z15: Caisson Yard's summit rope, its end on the hill's skin, ran 0.24 m into the dune before it); an end on flat ground or a roof is left as it is, its rope touching its own floor by a few mm too (the ground check's `FarEndLow` is -0.05 m: 11-21 c3z17/18, Thalassa Mooring 0.0096 m and Engine Court 0.0038 m at the floor itself). Over the cave hill's skin the rope's last 3 m want 0.3 m (`FarEndClear`: a world's hill comes out a little off the measuring one - 11-21 c3z21, Caisson Yard's summit floor set on the measuring skin stood 0.2 m in the world's sand), so a far end on the skin always stands on a deck; the raise counts that deck too (the rope must not run through it), and when no raise up to 3 m gives the room the rope is tightened (`zip.slack` at 1/2, 1/4, 1/10 of its own; c3z21: a sagging rope rose into its far end from under, 0.4 m through the deck). The raised floor sits on a wood deck with its own four posts into the ground (`ZipDeckPosts`, 11-21: Engine Court's and Thalassa Mooring's decks stood on their ladders alone; a corner whose post would go through a thing is left to `save`'s posts, which move it in) (its own ladders neither hold its corners nor count as a building under it: `zipDeckLadders`), with `Block_Ladder`s up the deck's back side (away from the line, climb side out) from its top into the ground; the generator's objects on their climbing side are removed as under the rope (`ZipClearLadderSides`, 11-21 c3z13: a `SmallBoulder2` shut Caisson Yard's ridge deck ladder), the recipe's own rocks kept with a `WARNING`. It logs `zipto: the rope came ...`; when nothing up to 8 m clears it, `WARNING: zipto: ... a line through a hill: move an end` and (since 11-21) the raise and rope that leave it the most room are set anyway (`The most room it can get set: raised R m, slack S`), or the line is left as it is when none gives more room; a tower line that no tighter rope clears gets the tightest rope that gives it more room, with its `WARNING`. A near end already in the air (a tower's deck) is not raised: when its rope comes under 1 m, only a tighter rope is tried (logged `its rope tightened to slack ...`, plus `WARNING: zipto: a rider on that rope comes ... short of a rider's room` when a rider would still walk into what is under it - Engine Court's middle tower line and its loot chest), else a `WARNING`; a tower line whose rope is clear is still checked for a rider's room (the same `WARNING`). When neither a raise up to 8 m nor a tighter rope clears a line (a hump between its ends - 15 islands, c3z29 11-21), its far end first moves back along the line toward the near end, 5% of the line at a time and at most half of it, never nearer than 8 m nor less than 1 m under the near end, onto the ground there (`ZipPullIn`; logged `zipto: ... its far end moved back X m along the line to x z ...: set the recipe's zipto there`); only when that fails too do the most-room fallbacks and the `WARNING` above apply. Last (`ZipShaveGround`, c3 round 4 11-21), a rope still under the ground check's room between its first 1.5 and last 3 m (0.5 m, coming down in a straight line to -0.05 m over the 3 m before the last 3 - `ZiplineEnds.LineWant`, c3 round 8: a rope coming down to its floor is low there by design; the land and the world's cave hill, as the check measures) gets the land under it cut to 1 m under the rope, sides sloping 0.7 m a metre from 1.5 m out, at most 6 m deep, never under the sea; the land under placed objects (bounds + 1 m, 2 m till c3 round 8; trees and bushes not, when the save reseats them) and the cave hill's skin are not cut, and the log names the objects that held it, the most first, with whose they are (the recipe's, its scattered ones, the island's) and the part that held (collider or renderer, its size); a lever's or a mechanism's part (`ShaveFarOwner`) more than 12 m from the object does not hold the land (`not held by <object>'s <part> (N points, over 12 m off it)`; c3z70: The Coldhold Vault's floodlever) - move them or `clear` along the line (logged `zipto: its rope came X m over the ground ... the land under it cut down up to D m ...`, a `WARNING ... move an end` when it is still short). Over the cave hill's skin the far-end raise also wants 0.5 m for the 3 m before the last 3 m. |
| `zipfrom` | `<alias> x z [h=\|y=\|ground]` | Moves the zipline so its near end's floor (where players step on) stands there, as the editor's Near end: where I look. The line's pivot is not that floor: placed by its pivot on a tower's deck, the floor hung off the deck in the air (IG5). |
| `loot` | `<alias> <items> [pick=N]` | Chest loot (`ObjectProps.LootItems`), e.g. `Wool*4;Rope*4`. An item with `@<percent>` (`Wool*4@25;Rope*4`) or a `pick=N` at the end makes a random chest (`RandomLoot`, QE14): `loot.mode=random`, `loot.chance`, `loot.pick` (N different items weighted by chance; none = each item rolls its own). Without them the line works as before. |
| `note` | `<alias> <title> \| <text>` | The note editor's Apply (`NoteEditorWindow.Apply`). |
| `story` | `id\|name\|icon\|description[\|raft=<Raft item>]` | A story item added, or replaced by id (`StoryItemDef.Parse`). |
| `quest` | `title=`, `intro=`, `done=` or `reward=` (one a line) | The island's quest. `quest2`..`quest9`: further quests. |
| `step` | `type\|target\|count\|text` | A quest step. Types are `IslandQuest.Types`: `reach read open kill catch collect pages blueprint ... signal bring craft` (`blueprint\|Blueprint_X\|1\|text`: one of Raft's blueprints carried or learned while at the island; empty target = any story blueprint; `signal\|lever` or `signal\|Gate=open`; `bring\|Plank@Mara\|3`; `craft\|Rope\|2`). Up to 99 steps. `step2`..`step9` for further quests. |
| `questbring` | `<rule line>` | `QuestEditorWindow.SetQuestBringRule`. |
| `rule` | `id \| what \| when \| where \| message \| label` | An island rule (`IntroRule.Parse`); the same id replaces. |
| `posts` | `[off]` | Posts (`RT_Pillar`, stretched) under each floor or deck corner nothing holds, down into the ground or sea floor (`StandPosts`, the check `FloorsOverNothing`, ROADMAP_DONE CA30). A building piece under a corner holds it when its top is within 1.5 m of the floor (no post); a lower one takes the post on its top (11-21: Engine Court's 6.6 m ridge deck had all four posts left out). `save` does it too; `posts off` leaves them out at the save (a deck meant to hang). |
| `reseat` | `trees` or `off` | At every save since IG5 (11-18; before it opt-in with `reseat trees`, 2026-10-31): trees and palms whose trunk foot is more than 1 m under the land (the ground check's "sunk trunk": a generator tree the recipe's strokes raised the ground round) or more than 0.4 m over it with nothing placed under it ("trunk foot floating") set back on the ground round their pivot, the foot kept within 1 m under the land; one whose foot would be inside a placed object's collider bounds stays (`ReseatSunkTrees`). Grass tufts and bushes (`CaravanIsland_*Grass`, `Bush`, `Bush2`, `SmallBush_N`, `Monstera_N`, `Banana_Bush_N`) more than 0.4 m over the land are dropped onto it too (IG5: Firedance Isle's pits, Ironreef's peak). A trunk the recipe laid down (tilted more than 60 degrees: `rot=`/`tilt=`) is left where it lies - its pivot is its cut end, not a foot; lay one with a height (`h=`, or `ground y=-0.3` for the cut end in the ground), since plain `ground` rests it on its lowest branch tip with the trunk in the air (Rootcellar Deep, CP4 11-20). `reseat off` keeps them where the recipe put them; `reseat trees` is still read (the default). A tree whose foot is on a steep drop's rim (3 of 8 points round it 0.8 m lower) moves up to 3 m off it (`OffDropRim`). A sunk tree (foot more than 1 m under the land) whose spot on the ground is inside a placed object's bounds is left out (logged `reseat trees: N sunk trees whose foot would be inside a placed object left out`; c3z29 11-21, The General Post Office's pines). |
| `save` | `<island name>` | Stands posts under floors over nothing (unless `posts off`), then saves the island (`DynamicIslands.SaveIsland`); the name must pass `FileNames.IslandProblem`. Fails first when an action or `state` check names an object no object is named, or an `obj.name` holds a second key=value from a joined `prop` line (`UnnamedTargets`), or when a `step kill\|<kind>` / `step catch\|<kind>`, or a `kill <kind>` under `island <this island>` in any `.play` of the folder, names a creature label the island doesn't place (`UnplacedCreatures`). |
| `plan` | see below | World plans window. |
| `log` | `<text>` | Writes the text to the log. |
| `cliffs` | `[rise=7] [step=3] [depth=10] [r=20] [reach=12] [at x z] [rocks=piece] [keep=x,z,r\|x,z,r]` | Slopes the island's bare land cliffs at the waterline (`DevTestsGround.CliffsStep`, c3 round 3 11-21): every face `CISheetIslands` would flag (rising `rise` m or more in one heightmap step from under the sea to over it, nothing standing in front) gets a rock-painted talus raised on its low side, falling `step` points of height per point from the top, never under `depth` m below the sea and never into a placed piece's body standing there (its colliders, or its bounds when it has none; zones, creatures, notes, loot, pickups and markers never hold it back, since c3z52; loot, pickups, notes and creatures it would bury ride up with it by as much as the ground rose under them - one under the sea staying under it unless sitting on the new ground lifts it out - since lib 3.5.1 11-23, when the talus buried Sundown's angler, Cistern Hall's mushrooms and Ironreef's clay; `keep=x,z,r\|x,z,r` (same date): circles in the recipe's coordinates left exactly as they are - no point raised in them, no face in them sloped or given rocks - for what an island was built round at the waterline: a dock pool, a sunk boat's dive site, a fish pool (the talus had filled Crater Lair's dock pool under the hangar door and buried the dive sites' boats; `CICliffsKeepUnit`); a small thing wholly under the sea (4 m wide, 8 m tall at most: coral, kelp, a sea-floor rock) is carried up onto the new ground instead, and a bigger natural rock (a name with Rock, Boulder, Stone, Coral, Kelp, SeaVine or Stack, not a built wall, pillar, arch, cave or tunnel) may sink into the talus up to its middle or to 1 m under its top, whichever is higher, and one wholly under the sea may be buried; the talus stops at the rock's cap rather than at its side (c3z61h: The Abyss's south coast stayed a sheer wall behind a sunken BigSharpRock_5). Air pockets and built pieces (a container) still hold it back - burying an air pocket would take the air out of the dive. Where two walls face each other (a pit, a dive shaft) the points between them are left open, so a narrow shaft keeps its depth; only points in front of a face (within 60 degrees of straight out) count for that, since c3z59 - the talus discs meeting beside faces along every bay and cape had kept hundreds of points as "between". `at x z` (with `r=`) limits it to one spot; `reach` is how far out (points) it may build at least - a taller face's talus goes as far as it needs to reach `depth` (up to 40). Logs the faces sloped, points raised, things carried up, points left under objects (naming the pieces that held most back) or between walls, and the faces still bare with why (between walls, under which objects, at the terrain's edge, on the talus's side, other) and up to 3 of them in the recipe's coordinates. `rocks=piece` (c3z61h; `rocks=A|B|C` takes the pieces in turn by place, c3z64b): after the talus, that rock piece (e.g. `BigBoulder4_Low`, preloaded like a `place`) is stood in the waterline at every face still bare, not at the terrain's edge - a pit's or a dive shaft's wall, or a channel's, that the talus leaves open - from 3 m under the sea (or the face's foot when higher) up to the face's top, stood on its longest side with its wide side along the wall and its thin side out (c3z62: the placer's straight turn laid the _Low boulders down, all under the water), stretched along whichever of its axes points up to at most 3 times its height and then wider along the wall by a quarter of that (1.5 times at 3; not when the wider rock would meet a piece or a rope: c3z65, posts in a row), each rock varied from its place (turned up to 25 degrees off the wall's line, leaned up to 8, 0.8-1.2 times its size, reaching 0-1.2 m over the top, up to 0.9 m off the face's middle along the wall: not a row of identical teeth; where a piece is in or on the wall it is moved 0.7 or 1.4 m out of it before being left out, c3z65; never with dry ground 1.5 m in front of it, c3z65b; each rock must cover its own face as the sheet check sees it, else it is redone turned and leaned less, then plain at the face's middle, then 0.6 and 1.2 m in toward the wall leaned 8 and 12 degrees to it (c3z70), and left plain when none covers it; then a second pass stands a smaller rock (0.55-0.7 times) at each face the sheet check would still find bare, and the log line ends with how many and the faces still bare with what was in the way, c3z67; where a full rock may not stand there - under a zipline's rope or a piece over the water such as a floor or a rope fence - the second pass stands a short one, its top 1.2 m under the rope and 0.3 m under the piece, at the face or up to 2.5 m along the wall or 0.7 m out, never at a tunnel's mouth, logged as `(N kept short under a rope or a piece)`, c3z70), one more wherever a face's ray from the sea (the sheet check's test) meets no rock stood yet, so the gaps between two rocks are filled (c3z64), the rocks overlapping each other and standing in front of logs, natural rocks and bushes, but moved 1.5, 2.5 or 4 m along the wall where it would meet another placed piece (non-trigger colliders: a ladder, a tunnel's mouth - left open) and left out when that does not clear it; within 3.5 m of a zipline's rope (near end to its zip.to) a rock reaches at most 2.5 m under the rope, and is left out where that is under 1 m over the sea ("a zipline's rope"; c3z64: a stretched rock raised Caisson Yard's zipline end 3 m); the log adds how many were stood, how many faces they cover and what blocked the rest ("none where they would meet X n"). One undo step in the editor for the ground (the rocks are placed pieces). |
| `where` | `<alias>` | Logs where the alias's objects ended up (x z from the first origin, `h=` above the sea, turn). |
| `ground` | `[cut] <x> <z> [<x> <z> ...]` | Logs the land's height above the sea at those points as the recipe has shaped it so far (whether a stroke's `h=` reached there); `cut`: the caves' ground cut made first, as a world spawns it. |
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
  whole (not those in a dug passage or in a placed cave's room: cave roof over, floor under).
- `save` then settles what the ground check would fail (`SettleAtSave`, 11-21): a loose rock or boulder in the air
  (`RockFloats`) goes down onto what is under it, or is hidden when that would sink it more than half its height; a prop
  buried whole (the check's `buried` rule) up to 2.5 m down goes up onto the ground. Deeper ones are left for the check. Buried treasure
  (`Treasure_Buried`) is left where it is. The caves' cut is caught up first (`CaveGround.EditorCatchUp`), and a prop in a cave
  piece's room (`CaveGround.EditorRoomTest`: the piece's own floor under it and roof over it) is left on that floor - the land
  under a den is cut in the world (Old Mine Islet's carts were lifted 2 m onto it and floated, c3z40).
- A strokes line after a `place` can leave that object floating or buried: shape the land first, then place. Check
  each island with `CIFloating` (editor) and `CIGroundIslands <name>` (world) - CHANGE_CHECKLISTS "Everything stands on
  the ground".
- `save` logs `play tests of '<island>' need 'offset X Z'` when the land centre moved from the origin (measured on the
  heights as saved, `EditorLandCentre(true)`: dug caves' cut ground counts at its own height, as the world's
  `IslandSpawner.LandCentre` reads the file; Gravewell Shaft's plays stood 2.8 m off before, 11-20). Put that
  `offset` line in the island's `.play` tests (see `PLAY_FORMAT.md`) whenever the recipe's land changes.

## Helpers for writing recipes

`CIMeasureObjects` (writes `recipes\objects_<category>.txt`), `CIHeightMap`, `CISeaSpots`, `CIProfile`, `CIBoundsOf`,
`CIColliders`, `CIFloating`, `CIView` (writes `recipes\view_<name>_<n>.jpg`), `CIViewAt`, `CIProtoInfo`,
`CIQuestIcons` (`questicons.txt`), `CISounds` (`sounds.txt`), `CIScene`, `CIInstances`, `CIChestReach` (every chest
has a floor spot with room to stand and a clear line from the eye), `CILadderSide` (every ladder's climb side is
open; a ladder takes hold only from its climb trigger's side). All are in `DevTestsRecipe.cs`.
Object and item lists for writing recipes are in the repo at `content\objects\`.
