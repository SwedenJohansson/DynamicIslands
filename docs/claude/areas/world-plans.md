# World plans and Raft's story

Which custom islands a world gets, when and where (world plans and island rules), how plan islands join Raft's Receiver story, and the main story tabs in Raft's notebook.

## Files
| File | Role |
|---|---|
| `WorldDirector.cs` | `IntroRule` (one rule line), `WorldPlan` (a .plan file), `IslandCache` (settings of unspawned island files; `rulescache.txt`), `WorldDirector` (host: checks rules, brings and announces islands), `ChunkPointFitPatch`, `ReturningIslands` (left-behind islands come back) |
| `StoryChain.cs` | Plan islands in Raft's Receiver chain, mod frequencies (types from `ModTypeBase` = 100), chain state sent to players |
| `StoryOrder.cs` | Raft's story islands in order (`StoryOrder.Chain`, `Key`, `Parse`); world option `WorldOptions.StoryOrder` (story islands in a new order) |
| `QuestBook.cs` | Main story tabs and pages in Raft's notebook, built on every machine |
| `QuestBookPreview.cs` | World Plans' Preview notebook: a plan's story as `Moment`s, stepped with Back / Next |
| `MainStoryHelper.cs` | New main story helper; `Finish` turns its islands into rule cards |
| `WorldPlanWindow.cs` | Plan editor (an island's own rules: `OpenIsland`); `WorldPlanTemplates` |
| `ChoiceWindow.cs` | Modal pick-one list with search (islands, map types, plans) |
| `PlanChecker.cs`, `PlanCheckWindow.cs` | Check: `Finding`s (`Level.Problem` / `Warning` / `Tip`) and the report window |
| `PlanProgressWindow.cs` | Progression panel: `PlanChecker.Progression(plan)` - Raft's story blueprints and story items rule by rule, kept, never given (CIPlanBlueprints) |
| `WorldIslands.cs`, `IslandPickerWindow.cs` | Spawn-pool entries left out of a world, gap between random islands; the tick-box list in the New Game box |

## Main flow
1. New Game box (`NewWorldOptions.cs`) sets `WorldDirector.PendingPlan`; `WorldIslands.Chosen` holds the entries left out (unticked).
2. Load: `IslandWorldState.OnWorldLoaded` calls the state classes' `Reset`. Host only: it reads the world file (`WorldCopy.Choose`), passes each `@key=value` line along a chain of `ReadLine`s, then calls `StoryChain.OnWorldRead`, `WorldRules.OnWorldRead`, `WorldDirector.OnWorldLoaded`. With no lines (clients) only the last two run. A brand-new world is caught by `WorldDirector.CheckNewWorld` (Raft raises `SaveAndLoad.LoadComplete` only for saved worlds).
3. `WorldDirector.OnWorldLoaded` (returns on clients): a new world gets `SetPlan(PendingPlan ?? DefaultPlan, true)` and `StoryChain.FromPlan`; a saved world plays its stored copy (`@planrule=` lines) unless `PlanFileToPlay` returns the .plan file.
4. `WorldDirector.Tick` (each second, host): `UpdateVisits`, `Evaluate`, `StoryChain.Tick`, `ReturningIslands.Tick`, `QuestMilestones.Tick`.
5. `Evaluate` -> `TryRule` -> `Met` / `Happened` -> `Bring` (`FileFor`, `Place`, `IslandWorldState.Add`, spawn) -> `Announce` (`IslandNetwork.SendAnnounce`, `Show`). A plan rule then goes into `Done`; an island rule sets state key `RuleKeyBase + i` on its island.
6. `IntroRule.Special` rules (`InStory` = place `first` / `after:` / `instead:`, or WHERE `receiver` / `sailing`) are skipped by `Evaluate`. `StoryChain.Tick` waits until an `InStory` step is unlocked, then `Fire`s it when `Met`, brings it (`TryBring`; a `receiver` rule only from `OnTuned`), and calls `MarkDone` when `IsDone`; `MarkDone` unlocks the next step (`Unlock`).
7. `StoryChain.Changed` -> `Rebuild`, `Broadcast`, `ChainChanged`. `QuestBook.Tick` (every 0.5 s, every machine) runs `Apply` when `Signature()` changed.
8. After Raft's `SaveAndLoad.SaveWorld`, `IslandWorldState.Save` (host) writes `WorldDirector.WriteLines`, `WorldIslands.WriteLines`, `StoryChain.WriteLines` with the rest.

## Data and keys
- Plan file `Mods\DynamicIslands\plans\<name>.plan` (`WorldPlan.Folder`, `PathFor`, `Load`, `Parse`, `ToText`, `Save`). Keys: `description`, `random`, `story`, `storyleaveout`, `storyending`, `rule`, `modversion`; `#` = comment; unknown lines and unreadable rules go to `Kept` and are written back. Built-ins `WorldPlan.RandomName`, `NoneName` have no file. `plans\samples.txt` lists samples already written (`WorldPlanWindow.EnsureSamples`).
- Rule line (`IntroRule.ToLine` / `Parse`): `id | what | when | where | message | label | story place | done when | tab title | tab colour | tab intro`. `WhatKinds` island, type, pool, oneof; `WhenKinds` start, km, day, quest, step, zone, visit, rule, signal; `WhereKinds` ahead, near, receiver, sailing; `DoneKinds` quest, visit, step, zone, signal, note. Story place `first`, `after:X`, `instead:X`, `beside`. An island's own rules: setting `bring.rules` (`WorldDirector.IslandRulesKey`).
- World file `Mods\DynamicIslands\worlds\<WorldGuid>.txt`. Director: `@plan=`, `@sailed=`, `@done=`, `@planfrom=`, `@planowner=`, `@planhash=`, `@planrandom=`, `@plandesc=`, `@planrule=` (`@savedby=` is read through `WorldCopy`). Chain: `@storychain=`, `@storyrule=`, `@storyending=`, `@storybeside=`, `@storyfreq=`, `@storyunlocked=`, `@storydone=`, `@storyfired=`, `@storybrought=`, `@storydue=`. Islands: `@islandsoff=` (`|`-separated), `@raftgap=`, `@raftgapcount=`, `@playtime=`. Island line `name|x|y|z|state|rule|label|hash` (`rule` = `Entry.Rule`).
- Island state keys: `WorldDirector.VisitKey` 0x50000, `RuleKeyBase` 0x50001 + rule index, `ReturningIslands.ReturnsKey` 0x50F00.
- Network (`IslandNetMessage`): `Announce` 8, `StoryChain` 19 (also carries banners in `Name`), `QuestCount` 20; clients read the host's plan from the host's world file (`WorldCopy` 18, `WorldCopy.HostLines`).
- `spawnpool.txt`: `defaultPlan`, `returnMinutes`. `world_rules.txt` keeps the last New Game choice: `islandsoff=`, `raftgap=`.
- Commands: `WorldPlan [name]` (`DynamicIslands.WorldPlanCommand`), `StoryChain`, `WorldIslands`, `WorldIslandsGap`.

## Where to change X
| Task | Edit |
|---|---|
| New WHEN kind | `IntroRule.WhenKinds`, `Parse`, `ToLine`, `DescribeWhen`; `WorldDirector.Met` / `Happened`; `PlanChecker.CheckWhen`; `WorldPlan.Help` |
| New WHERE | `IntroRule.WhereKinds`, `Parse`, `ToLine`; `WorldDirector.Place`; `PlanChecker.CheckWhere` |
| Chain done when | `IntroRule.DoneKinds`, `NormalDone`; `StoryChain.IsDone` |
| Chain order | `StoryChain.BuildSteps` |
| Notebook tabs and pages | `QuestBook.Describe`, `BuildIsland`, `MakeTab`, `Apply`, and `Signature` |
| Check findings | `PlanChecker.Check` and its `Check*` methods |
| New plan file key | `WorldPlan.Parse`, `ToText`, `Help`; the world's copy in `WorldDirector.ReadLine` / `WriteLines` |
| Random-island pool | `WorldIslands.TakesPart`; `CustomIslandSpawner.PlanIslandNames` |

## Traps
- Host only: `WorldDirector.Tick` returns on clients after `CheckNewWorld`; `StoryChain.FromPlan`, `Tick`, `OnTuned` act only on the host. Clients get the chain in `StoryChain.OnMessage` (main story rules in `clientBook`, read through `BookRules`).
- `WorldDirector.SetPlan` does not touch the chain: call `StoryChain.FromPlan` too (as `WorldPlanCommand` and `OnWorldLoaded` do).
- `PlanFileToPlay` returns the .plan file only when it differs from the stored copy and is the same plan: same library/import source (`PlanFrom`), or this PC is `PlanOwner`. Plan islands are found by content hash (`PlanHashes`, `FileFor`), not only by name.
- `Done`, `StoryChain.Fired` and `Brought` remember rule ids. `ForgetChangedRules` re-arms a done id that now brings another island; `WorldPlanWindow.NewRuleId` makes fresh ids; `IntroRule.CleanId` turns `| : , ;` into `-`.
- `WorldDirector.Done` also holds `QuestMilestones.NearMark` / `AllMark` (`milestone:90`, `milestone:100`).
- An island rule's fired flag is `RuleKeyBase` + its index in `bring.rules`: inserting or reordering rules shifts it (`QuestEditorWindow.SetQuestBringRule` inserts at 0).
- World file reading: the first `ReadLine` that returns true takes the key; an `@` line nobody takes is kept and written back. `WorldRandomizer`, `WorldOptions`, `WorldIslands`, `StoryChain`, `WorldDirector` get the key lowercased, the others as written. A new state class needs `Reset`, `ReadLine`, `WriteLines`, and its `HasState` in the nothing-to-save check of `IslandWorldState.Save`.
- Harmony: `NoteBook.UnlockFrequency` has two prefixes, `StoryChainUnlock` (skips Raft's call while `StoryChain.Active`; the chain's own calls set `StoryChain.Bypass`) and `StoryOrderUnlock` (`StoryOrder.Active` is false while the chain is active). `NoteBookUI.UpdateAllNotesVisibility` has postfixes from `StoryOrder` and `QuestBook`. Others: `ChunkManager.AddChunkPointForcibly` (`StoryChainReceiver`), `RecieverFrequency.InitializeAllFrequencies`, `RGD_RecieverFrequencies.RestoreFrequencies`, `NoteBookUI.FlipToPageLocally`, `FrequencyTextMeshPro.Start`, `FrequencyTextMeshProUI.Start`, `ChunkManager.DoesPointFit`.
- `QuestBook` redraws only when `Signature()` changes. Its notes use indexes from 20000 and never enter Raft's unlock list. Page numbers must match on every machine so Raft's page flips land on the same page.
- C# 7.3: no switch expressions, `??=`, ranges or `using var`.
- Docs: a format change goes into `WorldPlan.Help` (written at the top of each .plan) and docs/GUIDE.md `7.7 The plan file`; island rules are GUIDE `6.4`, the Receiver story `6.5`; a new file gets a line in README.md `### Source overview`.
