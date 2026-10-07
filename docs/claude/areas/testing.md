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
