# Testing (dev builds)

Automated tests and content tools, run as `CI*` console commands. Players never see them: release builds leave them out.

## Files

- `DevTests.cs` -> `Log`, `Fail`, `Check`, `Init`, the command file (`PollCommandFile`, `RunCommand`), editor and world drivers.
- `DevTests*.cs` (about 60 files) -> one `public static partial class DevTests`; each file adds one area's commands.
- `DevTestsRecipe.cs` -> `CIRecipe` and recipe-writing helpers (see `RECIPE_FORMAT.md`).
- `DevTestsPlay.cs` -> `CIPlay` play tests (see `PLAY_FORMAT.md`).
- `DevTestsScenarios.cs` -> `Sc*` helpers to bring, use and remove islands in a world; many `CISc*` scenario tests.
- `DevTestsMultiplayer.cs`, `DevTestsMultiplayerFull.cs` -> two-player helpers (`PutPlayerNear`, `OnRaftCommand`, `AnimalsOf`).
- `DynamicIslands.cs` -> calls `DevTests.Init` through reflection at start-up, if the type exists.
- `compile.ps1`, `pack.ps1` -> `-Release` leaves `DevTests*.cs` out.

## Main flow

1. Start-up in `DynamicIslands.cs` finds `DynamicIslands.DevTests.Init` by reflection and calls it.
2. `DevTests.Init` logs `[CITEST] Command file: <path>`, sets `Application.runInBackground`, starts `PollCommandFile`.
3. `DevTests.PollCommandFile` checks `CommandFile` every second (real time). It reads all lines, deletes the file, and runs
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
| Editor tests | `CITest`, `CIUndo`, `CIGenTest`, `CIButtons`, `CIQuestTest`, `CITerrainBrushes` | `DevTests.cs`, `DevTestsAll.cs`, `DevTestsContent.cs`, `DevTestsTerrainBrushes.cs` |
| World tests (host) | `CITestWorld`, `CIQuestWorld`, `CILootWorld`, `CIZoneWorld`, `CIBehaviourWorld` | `DevTests.cs`, `DevTestsContent.cs`, `DevTestsBehaviour.cs` |
| `...Unit` (rules, mostly anywhere), `...Check` (state, mostly in game) | `CIMonsterUnit`, `CILibraryUnit`, `CISafetyUnit`, `CIRulesCheck`, `CIPlanCheck`, `CIStoryChainCheck` | per area file |
| Scenarios | `CIScRam`, `CIScDeath`, `CIScCatch` | `DevTestsScenario*.cs` and others |
| Two players | `CIMPFull`, `CIMPState`, `CIJoinHost`, `CIGoto`, `CIUse`, `CIOpenChest`, `CIReadNote` | `DevTestsMultiplayer*.cs`, `DevTestsUI.cs` |
| Library content | `CIRecipe`, `CIPlay`, `CIView`, `CIViewAt`, `CIFloating`, `CIFeatureCheck` | `DevTestsRecipe.cs`, `DevTestsPlay.cs`, `DevTestsFeatureCheck.cs` |

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
  `DynamicIslands.csproj`, or `compile.ps1` never compiles it. `pack.ps1` packs every file in `DynamicIslands\` anyway.
- All `DevTests*.cs` files are one class: helper names (`Log`, `Fail`, `Check`, `F`, `Tokens`, `Options`, `Rest`, `Num`)
  are shared, and a second one with the same signature breaks the build.
- C# 7.3 only (`LangVersion` in the csproj; RML compiles with it).
- All lines of one command file run in the same poll. A command that starts a coroutine returns at once, so the next
  line starts before it finishes. Send one long command, wait for its PASS/FAIL line, then send the next.
- A test that stops with `Fail` and `yield break` prints no PASS line and may leave islands or state behind.
- A command run from the file sets `HelpLinks.Automated` (no browser or Explorer windows).

## Running dev commands: ci.ps1

Use `ci.ps1` in the repo root to run this mod's console commands (the CI* test commands and the others) in a running Raft. It prints only that run's `[CITEST]` lines, so do not read `Player.log` yourself.

- Needs Raft running with a dev build of the mod (`pack.ps1 -Install`, not `-Release`; `-Release` leaves out `DevTests*.cs`). At startup the mod calls `DevTests.Init` through reflection. It logs `[CITEST] Command file: <path>` and starts `PollCommandFile`.
- `PollCommandFile` reads `dev_commands.txt` in `DynamicIslands.assetpath` (`Mods\DynamicIslands\` beside `Raft_Data`) every second, deletes it and runs each line. Blank lines and `#` lines are skipped. Each line is logged as `[CITEST] > <line>`, then `RunCommand` runs the `ConsoleCommand` method with that name. Words after the name are its arguments. An unknown name logs `FAIL: no console command called <name>`.
- Run: `powershell -ExecutionPolicy Bypass -File ci.ps1 -Command "CIEditor" -Until '^Editor ready:'`. Separate several commands with `;`.
- All lines of one run start in the same frame. A command that needs an earlier one finished goes in its own run: `CIEditor`, then `CIDemo`.
- Every test command runs its coroutine through `DevTests.StartTest`, which logs `[CITEST] IDLE (all tests done)` when the last running test ends (also after an exception, logged as `FAIL: exception: ...`). Without `-Until` a run ends there, so most runs need no `-Until`. `CINewWorld` and `CILoadWorld` end only once the world is in (`World ready`), so the next run can be a test. Commands that start no coroutine (`CIMainMenu`, `CIQuit`) still end on `-Quiet`. A new test command must start its routine with `StartTest(...)`, not `DynamicIslands.instance.StartCoroutine`.
- Usual loop: `CIMainMenu`, then `CINewWorld CI <name>` (a fresh world: some rewards come once per world), then the test; `CIQuit -Until Quitting` at the end. Only name test worlds "CI ...".
- Within one test there is no common end line: `Check` logs `PASS:`/`FAIL:` for every sub-check too. `-Until` is a regex matched against the text after `[CITEST] `, on lines after the last command's echo. Use the test's final verdict text, e.g. `'editor save/load round trip'` for `CITest`. Without `-Until`, the run ends after `-Quiet` (20) s with no new `[CITEST]` line. With `-Until`, that quiet rule applies only once a `FAIL:` line has come. Long tests: raise `-Timeout` (300).
- The script writes to the command file path from the log's last `Command file:` line (else `<RaftDir>\Mods\DynamicIslands\`). It stops at once (exit 2) when the log shows the mod loaded without that line, which means a release build. A file the mod does not read in 60 s is deleted again (exit 3).
- Output: `FAIL:` lines, then at most 40 more lines: mod exceptions (`EXC`) first, then the last other `[CITEST]` lines. Lines are cut at 300 characters. `-Full` shows everything. The last line is `PASS n, FAIL n, <seconds> s`.
- Exit 0 no FAIL, 1 FAIL, 2 setup problem (one line says which), 3 timeout.
- Second Raft under Sandboxie: `-Player 2 -LogPath <its Player.log>` uses `dev_commands_2.txt`. The box must open that file to the real folder (`OpenFilePath`).
