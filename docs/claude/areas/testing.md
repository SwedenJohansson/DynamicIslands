# Testing (dev builds)

Automated tests and content tools, run as `CI*` console commands. Players never see them: release builds leave them out.

## Files

- `DevTests.cs` -> `Log`, `Fail`, `Check`, `Init`, the command file (`PollCommandFile`, `RunCommand`), editor and world drivers.
- `DevTests*.cs` (114 files) -> one `public static partial class DevTests`; each file adds one area's commands.
- `DevTestsRecipe.cs` -> `CIRecipe` and recipe-writing helpers (see `RECIPE_FORMAT.md`).
- `DevTestsT8cb.cs` -> `CIT8Items` (story take-out), `CICB1Recipe` (decorative ready pieces), `CITrust` (two players: host trust, a player's take-out). CI world commands set `PlayerTrust.ForTests`; `CITrust setup` clears it, `CITrust on` sets it again.
- `DevTestsMp1030.cs` -> `CIMp1030` (one machine, host, Loopback: review 10-30 multiplayer fixes - corrections of refused used/harvest, full-list state re-apply, list without names, event on an island the host hasn't loaded, the day before's daily reward, ping sender, join size).
- `DevTestsSaveStateResets.cs` -> `CISaveStateResets` (fix2 10-30: quest rewards follow Raft saves - a reload from before the reward pays again once, a later save refuses; world-load resets of quest counts, collectibles, golden barrels, the head start build).
- `DevTestsBugHunt1030.cs` -> `CIBhArriveAgain` (an arrive whose only-if failed fires again after the player is land radius + 60 m away), `CIBhHiddenKill` (a kill in the second of hiding counts), `CIBhMissingAsk` (the story step is kept while the missing-islands box asks), `CIBhStableKeys` (treasure, trade-stand and vending keys carried on a library update).
- `DevTestsLifecycle1030.cs` -> `CIForcedStoryPoint` (Raft's forced story points pushed clear of custom islands; its clean-up keeps unloaded Raft islands near them).
- `DevTestsFix3.cs` -> `CIFix3RunShared` (a throwing shared action doesn't stop the chain), `CIFix3HostRefused` (a refusal shows its messages), `CIFix3Chest` (on.open gives a story item -> no refill), `CIFix3Pending` (pending keys of events 16-19), `CIFix3Stable` (MechFeatures keys move on a carry), `CIFix3WorldsUsing` (Raft's world folders scanned), `CIFix3LocalFile` (a damaged name_hash copy isn't played).
- `DevTestsUpgrade.cs` -> `CIUpgradeHeal` (worlds from Release 2026-10-23: `StoryUpgrade` re-arms a removed island's rule and adopts a hand-spawned copy; `QuestTracker.HealSpentChests`).
- `DevTestsNoteCarry.cs` -> `CINoteCarry` (journal pages follow their notes through Give my worlds this version; a removed note's page is kept as `gone<n>`).
- `DevTestsTestTools.cs` -> keys, sign and note with Raft minimised: `CIKey`, `CIKeysState`, `CISign`, `CINoteHere` (see "Keys, sign, note, islanders" below).
- `DevTestsMore.cs` -> also `CIFace` (stand in front of an object and check Raft's interaction ray finds it) and `CITalk` (talk to an islander).
- `DevTestsCursor.cs` -> the mouse and `CanvasHelper.ActiveMenu` around the mod's windows (`ModCursor`): `CICursorState` (one line: menu, mouse, focus, RML's ToggleCursor guard, Raft's Esc menu open, the mod's holders, `ModKeys.Blocked`), `CICursorTrace on|off` (logs every `RAPI.ToggleCursor`/`TogglePriorityCursor` and `CanvasHelper.CloseMenu` with its caller), `CICursorFix <island> <islander>` (needs `Levels on`; many PASS lines - run with `-done`).
- `DevTestsPlay.cs` -> `CIPlay` play tests (see `PLAY_FORMAT.md`).
- `DevTestsScenarios.cs` -> `Sc*` helpers to bring, use and remove islands in a world; many `CISc*` scenario tests.
- `DevTestsEditorStart.cs` -> the start window, plan on the main menu, Use in a plan..., Edit island, plan tabs (`CICreateWindow`, `CIPlanFromMenu`, `CIUseInPlan`, `CIEditIslandFromMenu`, `CIPlanTabs`, `CIStoryNewIsland`).
- `DevTestsMultiplayer.cs`, `DevTestsMultiplayerFull.cs` -> two-player helpers (`PutPlayerNear`, `OnRaftCommand`, `AnimalsOf`).
- `DynamicIslands.cs` -> calls `DevTests.Init` through reflection at start-up, if the type exists.
- `compile.ps1`, `pack.ps1` -> `-Release` leaves `DevTests*.cs` out.

## Main flow

1. Start-up in `DynamicIslands.cs` finds `DynamicIslands.DevTests.Init` by reflection and calls it.
2. `DevTests.Init` logs `[CITEST] Command file: <path>`, sets `Application.runInBackground`, starts `PollCommandFile`.
3. `DevTests.PollCommandFile` checks `CommandFile` every 0.2 s (real time; a file written in the last 0.1 s waits a look). It reads all lines, deletes the file, and runs
   each non-empty line not starting with `#`, logging `[CITEST] > <line>`.
4. `DevTests.RunCommand` finds the method with a `ConsoleCommand` attribute of that name (any case) in the mod's assembly
   and calls it with the rest of the line split at spaces (no quoting). Any of the mod's console commands works.
5. The command usually starts a coroutine (`DynamicIslands.instance.StartCoroutine`), logs with `Log`/`Check`, and ends
   with `Log("PASS: ...")` or `Fail(...)`.

## Data and keys

- `Mods\DynamicIslands\dev_commands.txt`: commands for this Raft. `dev_commands_2.txt`: for a second Raft running in
  Sandboxie (`DevTests.Sandboxed`: `SbieDll.dll` is loaded); the box must open that file to the real folder.
- Log lines: `[CITEST] <text>` (`Log`), `[CITEST] PASS: <what>` / `[CITEST] FAIL: <what>` (`Check`, Debug.Log),
  `[CITEST] FAIL: <msg>` (`Fail`, Debug.LogError). They show in the F10 console and in Raft's `Player.log`.
- Command file errors: `[CITEST] FAIL: no console command called <x>`, `[CITEST] FAIL: command '<line>': <exception>`.
- `Mods\DynamicIslands\recipes\` (`DevTests.RecipeFolder`): `.recipe` and `.play` files, and outputs `view_*.jpg`,
  `play_*.jpg`, `objects_<category>.txt`, `questicons.txt`, `sounds.txt`. `CIShot` writes `shot_<name>.png` and `CIItems`
  writes `items.txt` in `Mods\DynamicIslands\`.
- Repo `content\recipes\` and `content\tests\` hold the sources; `content\objects\` holds object and item lists. No mod
  code or repo script copies them; both kinds go into the one `recipes\` folder.
- Test worlds: name starts with `CI ` (`SaveAndLoad.CurrentGameFileName`). Test island: `citest` (`DevTests.TestIsland`);
  scenario tests save islands named `cisc...` and delete them with `ScRemove`.

## Command families

| Family | Examples | Where |
|---|---|---|
| Drivers | `CIEditor`, `CITab`, `CIClick`, `CINewWorld`, `CILoadWorld`, `CISave`, `CIShot`, `CIBackground`, `CIMute`, `CIQuit`, `CIAlive`, `CIScene` | `DevTests.cs`, `DevTestsMultiplayer.cs`, `DevTestsRecipe.cs` |
| Editor tests | `CITest`, `CIUndo`, `CIDigTunnelEditor` / `CIDigTunnelCamera` (the Dig brush; the camera flown into a dug tunnel, God mode), `CICaveToolInside` (the straight cave tool tunnel seen from inside its den: no void, no sky over the den wall), `CIGenTest`, `CIButtons`, `CIButtonsInside` (also the Camera window, "camera"), `CICameraSettings` (the settings, and the controls list: every row, one line each, fits 1280x720), `CIIslandFilesHelp`, `CIChoiceTiming` (`DevTestsChoiceTiming.cs`), `CITestButton` (`DevTestsPolish.cs`), `CIGenBuildingsQuiet`, `CIErrorNote` (`DevTestsErrors.cs`), `CIQuestTest`, `CITerrainBrushes`, `CIGenSweep` (`quick` / `<chunk>` / `all` / `list`: Generate island through its window over ~370 fixed cases), `CIGenCase <n>` | `DevTests.cs`, `DevTestsAll.cs`, `DevTestsUiFit.cs`, `DevTestsContent.cs`, `DevTestsTerrainBrushes.cs`, `DevTestsGenSweep.cs` |
| World tests (host) | `CITestWorld`, `CIQuestWorld`, `CILootWorld`, `CIZoneWorld`, `CIBehaviourWorld`, `CIDropHold` (RK2, dropped items), `CIStandIn` (RK1, another host's island version) | `DevTests.cs`, `DevTestsContent.cs`, `DevTestsBehaviour.cs`, `DevTestsItemHold.cs`, `DevTestsStandIn.cs` |
| `...Unit` (rules, mostly anywhere), `...Check` (state, mostly in game) | `CIMonsterUnit`, `CILibraryUnit`, `CISafetyUnit`, `CIRulesCheck`, `CIPlanCheck`, `CIStoryChainCheck` | per area file |
| Scenarios | `CIScRam`, `CIScDeath`, `CIScCatch` | `DevTestsScenario*.cs` and others |
| Two players | `CIMPFull`, `CIMPState`, `CIJoinHost`, `CIGoto`, `CIUse`, `CIOpenChest`, `CIReadNote`, `CIRk34` (RK3/RK4, `tools\mprk34.ps1`) | `DevTestsMultiplayer*.cs`, `DevTestsUI.cs`, `DevTestsMpAnswer.cs` |
| Library content | `CIRecipe`, `CIPlay`, `CIView`, `CIViewAt`, `CIFloating`, `CIFloatingSweep`, `CIRemoveBuried`, `CISettle`, `CIFeatureCheck` | `DevTestsRecipe.cs`, `DevTestsPlay.cs`, `DevTestsFeatureCheck.cs` |
| On the ground, in a world (`tools\groundall.ps1`) | `CIGroundWorld`, `CIGroundIslands`, `CIGroundTypes`, `CIShackDish` (a generated shack with its dish forced: dish on its pole's axis, 6.0 m up, pole on the ground) | `DevTestsGround.cs` (support chains: a floor, pillar or prop must rest on something that reaches the ground; each banner rope end needs a post or wall; small things on a steep wall fail; a tree, palm or bamboo stem whose foot stands over a drop (3+ of 8 points round it more than 0.8 m above the ground) is "trunk over a drop", one whose foot (pivot) is more than 1 m under the land at it is "sunk trunk" and more than 0.4 m over what is under it "trunk foot floating" (Pickup_ landmark trees too); a lamp or projector under a ceiling, and roof pieces joined edge to edge, count as held; `CIRenderers <object>` lists an object's meshes and colliders) |
| Stale islands (`tools\stale.ps1`, GAP17) | none (git + file times) | `STALE: <island>` for every built island older than its recipe or an included lib_ recipe (author time, HEAD + origin/content, 60 min grace); `BEHIND:` when the worktree lacks recipe commits of origin/content; `-rebuild` builds up to 12 with `contentall.ps1 -parts build -only`. pack.ps1 -Install doesn't rebuild islands, so groundall (editor sweep, plans/lib/all parts) and alltests' content phase run it first; `-player 2` also flags box copies older than Raft 1's (`SHADOWED:`, the box reads its own) |
| Build check (`tools\buildcheck.ps1`, GAP20) | none (reads the installed .rmod) | `REPO:` the worktree the content tools build from (CI_MOD_DIR, else DynamicIslands-tests) and its commit, `INSTALLED:` the .rmod and its pack stamp (`DynamicIslands.build.txt` beside it, written by pack.ps1, copied by `-Install`; never inside the .rmod: IslandNetwork.Build hashes its files); PASS when every file of the .rmod is the repo's, else `MATCH:` the worktrees that are + FAIL; alltests' content phase skips stale/contentall on a FAIL (`CI_BUILD_CHECK=warn` runs them anyway) |

Prefix families (`CIGen*`, `CIRandomizer*`, `CIStory*`, `CIPlan*`, `CIQuestBook*`, `CINoteBook*`, `CIProbe*`, `CIMeasure*`,
`CIDump*`) are spread over several files. Each command's `docs:` text starts with `Dev`, mostly followed by where it runs
(`Dev, editor`, `Dev, in game (host)`, `Dev, world`, `Dev, main menu`, `Dev, anywhere`). Grep `ConsoleCommand(name: "CI<name>"`.

## Where to change X

- New test command -> a `[ConsoleCommand(name: "CI...", docs: "Dev, <where>: ...")]` public static method in a
  `DevTests*.cs` file; long work in a coroutine; report with `Check`, finish with `Log("PASS: ...")` or `Fail`.
- New recipe step -> the switch in `DevTests.RecipeRoutine`; preprocessor keywords -> `DevTests.ExpandLines`.
- New play step -> the switch in `DevTests.PlayRoutine`; new expectation -> `DevTests.PlayExpect`.
- Islands in a world for a test -> `ScBring`, `ScSpot`, `ScOpenChest`, `ScReadNote`, `ScEnterZone`, `ScUse`,
  `ScAnimals`, `ScKill`, `ScRemove` in `DevTestsScenarios.cs`.
- Command file behaviour -> `DevTests.PollCommandFile`, `DevTests.RunCommand`, `DevTests.CommandFile`.

## Traps

- Code outside `DevTests*.cs` must never use `DevTests`: release builds don't have it. Check with `compile.ps1 -Release`
  (it compiles a copy of the csproj without the `Compile Include="DevTests` lines).
- A new test file must be named `DevTests*.cs` (so `-Release` drops it) and get a `<Compile Include>` line in
  `DynamicIslands.csproj`, or `compile.ps1` never compiles it (three test files missed it until
  10-21; Raft compiles them anyway). `pack.ps1` packs every file in `DynamicIslands\` anyway.
- All `DevTests*.cs` files are one class: helper names (`Log`, `Fail`, `Check`, `F`, `Tokens`, `Options`, `Rest`, `Num`)
  are shared, and a second one with the same signature breaks the build.
- C# 7.3 only (`LangVersion` in the csproj; RML compiles with it).
- The lines of one command file run one after another: each waits until the tests the earlier lines started (through
  `StartTest`) have ended. A command that starts a coroutine some other way returns at once, and the next line starts.
- A test that stops with `Fail` and `yield break` prints no PASS line and may leave islands or state behind.
- A command run from the file sets `HelpLinks.Automated` (no browser or Explorer windows).

## Island checks: ground, overlap, one-sided (`DevTestsGround.cs`)

- `CI<Check>World` measures the loaded islands, `CI<Check>Islands <which>` saved islands one by one, `CI<Check>Types [types] [n]` brand-new generated islands (ground and overlap only; host, a `CI ...` test world). Each `IslandCheck` writes `Mods\DynamicIslands\<file>_<world|islands|types>.txt`.
- `CIOverlap*` (`OverlapProblems`): building pieces through furniture/props (`PieceOverlaps`), then `SceneOverlaps`: a themed scene's props, chest and note (`RandomizerIslands.SceneProp` = `set.scene`, the theme's name) against each other by their collider boxes as turned (big mesh colliders too, meshes when none; 3D separating axes), fail over 6 cm, lines `scene <theme>: A ... goes d m into B`. Islands made before 10-21 have no `set.scene`: their set pieces named in `RandomizerIslands.Themes` are measured instead. Dish + pole pairs skipped.
- The generator keeps the same rule (`RandomizerIslands.FootOf`/`Into`, 2D turned boxes, 5 cm gap) in `Dress`, `Sprinkle`, `DressWorld`; `CIIslandsSound` checks it on the files (no Raft objects needed).

## Keys, sign, note, islanders (Raft minimised, 11-04)

Real keys can't reach a minimised Raft, and no test may bring a Raft window up. These go through the game's own code instead:
- `CIKey <KeyCode>` (either player): for one frame `Input.GetKeyDown(<key>)` answers true (a Harmony postfix that
  `DevTests.Init` installs at start-up, before the mod's key code is first compiled), so the mod's K, J and minimap
  key handlers run as for a real press. A letter key also types into the text field that has the keyboard
  (`InputField`/`TMP_InputField.ProcessEvent`). Logs `PASS: CIKey <key> - before: <state>` and `CIKey <key> after: <state>`.
- `CIKeysState`: one line - typing (`ModKeys.SomeoneTyping`), the selected field (focus, text), sign writing, stats page,
  journal, notebook, note, dialogue, minimap, Raft's open menu, the cursor (shown/hidden, lock).
- `CISign give [n] | place | write | stop | text | status`: Raft's sign (`Placeable_Sign`): give it, place it (cheat
  build on the raft floor 1.5 m ahead, facing the player; the first raft block under the spot, through terrain over a beached raft), `write` opens Raft's sign text menu on the nearest sign
  (`TextWriterManager.StartWriting`) and focuses its field, `stop` aborts typing.
- `CINoteHere [title] | close`: a custom note 1 m in front of the player, opened (`NoteReader.Open`); `close` closes and removes it.
- K opens the stats page only with the level up system on: `Levels on` first.
- IL20 recipe: `Levels on; CIOnRaft; CISign give; CISign place; CISign write; CIKey K; CIKey J; CIKeysState; CISign stop` (nothing opens, the
  letters land on the sign), then `CINoteHere; CIKey K; CINoteHere close` (the stats page stays shut).
- `CIFace <island> <name | note title | islander name or its start>` (either player): stand points on solid ground (a
  floor too, at the object's own height) with room to stand and a clear view; an islander is aimed at the chest. No
  point where Raft's ray (`Helper.FindInteractable`) finds it = the player goes back where they were. Raft's ray goes
  through the screen's middle, which a minimised window doesn't have: `AheadInteractable` casts the same ray from the
  camera straight ahead when Raft's misses, and a minimised Raft may not turn the view at all (its mouse look
  waits for a drawn frame), so it then turns the camera itself to the object and casts in the same frame (the Facing
  line says which); the player is held still (controller off) while it looks. A miss logs the first try's distances, angle and the first thing on the line. The player goes back
  to the same spot of the raft (raft-relative: the raft drifts; the old world spot left them under it).
- `CITalk <island> <islander> [read | pick <n>]` (either player): `CIFace`, then `Islander.Talk` (E's path) and
  `DialoguePanel.Close` (Esc's path), which ends in `Islanders.Talked` (talk events, a "Bring items" hand-in, talk steps).
  `PASS: talked to '<name>' on '<island>' (host|client; Islanders.Talked ran[, message ...])`.
  A dialogue that closes by itself within 0.5 s fails with its cause (`DialoguePanel.LastClose`: a key, the scene, the player N m away).
- `CIOnRaft` puts the player on the raft's top block under its middle (`RaycastAll`, island terrain above a beached raft
  skipped); the line says `(a raft block[, under <terrain>])` or `(not the raft)`.
- `CILoadWorld <name>` loads the world straight from its folder in `SaveAndLoad.WorldPath` (`LoadWorldDirect`:
  newest save via `GetLatestRGDGameFromBackupFolders`, then Raft's `Button_LoadGame`; not signed in to PlayFab = hosted solo, as `CINewWorld` does - hosted for friends
  offline the scene loaded but the player never came in), without Raft's Load Game list,
  which reads every world first (a box with ~2000 worlds: "no saved worlds listed"). No name = the list, as before.
- `CIRecipe`'s unnamed-target check skips `order|<puzzle>|n` (its target is a puzzle group, not an object).

## Running dev commands: ci.ps1

Use `ci.ps1` in the repo root to run this mod's console commands (the CI* test commands and the others) in a running Raft. It prints only that run's `[CITEST]` lines, so do not read `Player.log` yourself.

- Needs Raft running with a dev build of the mod (`pack.ps1 -Install`, not `-Release`; `-Release` leaves out `DevTests*.cs`). At startup the mod calls `DevTests.Init` through reflection. It logs `[CITEST] Command file: <path>` and starts `PollCommandFile`.
- `PollCommandFile` reads `dev_commands.txt` in `DynamicIslands.assetpath` (`Mods\DynamicIslands\` beside `Raft_Data`) every 0.2 s, deletes it and runs each line. Blank lines and `#` lines are skipped. Each line is logged as `[CITEST] > <line>`, then `RunCommand` runs the `ConsoleCommand` method with that name. Words after the name are its arguments. An unknown name logs `FAIL: no console command called <name>`.
- Run: `powershell -ExecutionPolicy Bypass -File ci.ps1 -Command "CIEditor" -Until '^Editor ready:'`. Separate several commands with `;`.
- The commands of one run go one after another (see below), so `CIEditor;CIDemo` works in one run.
- Every test command runs its coroutine through `DevTests.StartTest`, which logs `[CITEST] IDLE (all tests done)` when the last running test ends (also after an exception, logged as `FAIL: exception: ...`). Without `-Until` a run ends there, so most runs need no `-Until`. `CINewWorld` and `CILoadWorld` end only once the world is in (`World ready`), so the next run can be a test. Commands that start no coroutine (`CIMainMenu`, `CIQuit`) still end on `-Quiet`. A new test command must start its routine with `StartTest(...)`, not `DynamicIslands.instance.StartCoroutine`. Several commands in one run (`-Command "CIA;CIB"`) run one after another: a line waits until the tests of the earlier lines have ended, and IDLE comes once, after the last (until 10-16 IDLE came after CIA, the run stopped there and the next run's test overlapped CIB - the cause of the "flaky" CIGenRaftFeatures).
- Readiness, not fixed waits (10-20; the menu line is in `DevTestsContent.cs`, `World ready` and spawn now in `DevTests.cs`,
  `Took`, `WaitIslandSettled` and `RemoveIslandsAndWait` in `DevTestsReady.cs`): `CIMainMenu` logs `At the main menu (after n s)` and IDLE once the menu's New Game box is there; `CINewWorld` waits up to 30 s for that box; `World ready` comes once the player is in and `CustomIslandSpawner.StreamTicks > 0` with no island loading (10 s cap); `WaitIslandSettled` (no `CaveMountain.Pending`, rigidbodies asleep, object count still; capped at the old fixed time) and `RemoveIslandsAndWait` replace the ground checks' and `CIPlay`'s sleeps; `CISpawnNow` ends with `PASS: spawn now - ... built in n s`. Scripts wait for these lines instead of `Start-Sleep`.
- `CINewWorld` survives a throw in Raft's Create (twice a NullReferenceException in `Dropdown.Hide`, 10-26: the run hung ~25 min at the menu): `CreateWithRetry` catches it, goes on if a world began loading within 2 s, else closes the box's dropdowns, reopens the box and tries once more; a second throw logs `FAIL: CINewWorld '<name>': Create threw twice (...)`. `CINewWorldRetry` (anywhere) tests that logic with a fake Create.
- Usual loop: `CIMainMenu`, then `CINewWorld CI <name>` (a fresh world: some rewards come once per world), then the test; `CIQuit -Until Quitting` at the end. Only name test worlds "CI ...".
- Within one test there is no common end line: `Check` logs `PASS:`/`FAIL:` for every sub-check too. `-Until` is a regex matched against the text after `[CITEST] `, on lines after the last command's echo. Use the test's final verdict text, e.g. `'editor save/load round trip'` for `CITest`. Without `-Until`, the run ends after `-Quiet` (20) s with no new `[CITEST]` line. With `-Until`, that quiet rule applies only once a `FAIL:` line has come. Long tests: raise `-Timeout` (300).
- The script writes to the command file path from the log's last `Command file:` line (else `<RaftDir>\Mods\DynamicIslands\`). It stops at once (exit 2) when the log shows the mod loaded without that line, which means a release build. A file the mod does not read in 60 s is deleted again (exit 3).
- Output: `FAIL:` lines, then at most 40 more lines: mod exceptions (`EXC`) first, then the last other `[CITEST]` lines. Lines are cut at 300 characters. `-Full` shows everything. The last line is `PASS n, FAIL n, <seconds> s`.
- Exit 0 no FAIL, 1 FAIL, 2 setup problem (one line says which), 3 timeout.
- Second Raft under Sandboxie: `-Player 2 -LogPath <its Player.log>` uses `dev_commands_2.txt`. The box must open that file to the real folder (`OpenFilePath`).

## Reach audit: can a player get to every quest target? (`DevTestsReach.cs`, ROADMAP PQ11)

- `CIReach <island>` (world, host, a world named `CI ...`): brings the island in and, with Raft's own colliders, measures whether every quest target (note, chest, zone, used object, islander) can be got to from the open sea on four sides and left again to the sea; it then walks the targets with the real player (`CharacterController`), uses them, and walks back out. A quest island with a target no player can reach fails.
- `CIReachOn [label]` / `CIReachOff`: every `CIPlay` measures each island it leaves, in the state the quest left it (`CIReachOff` for a play that only needs pictures).
- Speed: `CIReachSpeed <n> [pace]` - n moves a frame (1 = a player's pace, 0 = no walks, the grid alone; 3x to 5x in audits), and `pace` multiplies Raft's walk and swim speed on the ground and in the water (not in the air). Raft moves by swept `CharacterController.Move`, so there is no tunnelling; a guard casts from the old to the new position each frame and marks a tunnelled walk INVALID. `CIReachControls`: the walker also walks straight at walls too high to step or jump over; none may be got through. Every FAIL is walked again at 1x before it counts.
- Precheck: an open target under the sky with a path in from two or more sides and back to the water may pass by the grid alone ("PASS (precheck)"). Caves, shafts, roofs, under-water spots and chests in buildings are always walked.
- Worlds: a storyline gets a fresh world of its own; library islands go four to six to a fresh world (one `CIReach` run per island, each island removed before the next). Walks start 15 m off the shore (a teleport, no sailing); the walk itself never teleports.
- Tools: `CIReachProbe` (the walker alone between two points, logging what is round its feet), `CIReachHeights` (a grid of heights to plan a ramp), `CIReachSwim`, `CIReachWhy` (the player's movement state). The mod logs `[CITEST] HB` every 10 s; a walker that moves under 0.5 m in 10 s is unstuck once, and after 30-90 s without a waypoint the walk fails as "stuck".
- Plans, results and the open list: `_ProjectDocs/plans/REACH_AUDIT.md` (outside git).

## Low graphics for test runs: CIGfx (`DevTestsGfx.cs`)

- `CIGfx low [fps]`: windowed 1280x720, quality level 0, shadows off, lowest water, AO/AA/volumetric light off, vsync off, frame cap (default 30, `0` = none). `CIGfx high`: the user's values back (taken when low was first set). `CIGfx status`.
- `tools\raft.ps1 launch` sends `CIGfx low` to every Raft it starts (both players; `rebuild.ps1`, `mp.ps1` go through it). `$env:RAFT_GFX = "high"` skips it (`docshots.ps1` does, and sends `CIGfx high`).
- Pictures stay at the user's quality: `RenderCam` (play, recipe, library pictures) and `PlayPicture` switch for the shot (`GfxShotBegin`/`GfxShotEnd`). `CIShot` captures the screen as it is: send `CIGfx high` first for full-size shots, `CIGfx low` after.
- Nothing low is kept: `GfxSavePostfix` makes Raft's Settings.rgd save the user's values; quitting restores them; `tools\gfx.ps1 watch <pid>` (started by `raft.ps1`) puts Unity's registry values (`Screenmanager*`, `UnityGraphicsQuality*`) back from the backup once the first player's Raft closes. `gfx.ps1 check` compares with the backup.
