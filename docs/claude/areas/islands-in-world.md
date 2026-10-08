# Islands in the world
How a saved `.island` file becomes an island in a Raft world, streams in and out by distance, and is saved with the world.

## Files
| File | Role |
|---|---|
| `IslandFile.cs` | `IslandFile`: the `.island` format; `Save`, `Load`, `LoadOffThread`, `FromBytes`, `Capture` (private `Read` parses) |
| `IslandSpawner.cs` | Builds an island in the scene: `SpawnInWorldSliced`, `MakeRoot` (cropped terrain, paint, `MakeFlying`), `SpawnObjectsSliced`/`SpawnBody`, `RegisterNetworkIds`, `Despawn`, `PathFor`, `SpawnedRoots` |
| `CustomIslandSpawner.cs` | `Tick` every 2 s: `StreamIslands` (every machine), random islands while sailing (`TrySpawn`, host); spawnpool.txt (`LoadPool`, `SetValue`) |
| `DynamicIslands.cs` | `SpawnIslandFile` coroutine (read, spawn, wire up); `OnLoadComplete`; `Update` calls each class's `Tick` |
| `IslandWorldState.cs` | The world's island list (`Entry`), `Save`/`OnWorldLoaded` of the world file, `OnWorldShift`; `SaveWorldPatch`, `CreateRGDGamePatch`, `RestoreRGDGamePatch` |
| `WorldCopy.cs` | Copies of the world file (Raft's world and save folders, every player); `Choose` picks the copy to read; `LocalFileFor` finds a saved island's file here |
| `IslandObjectState.cs` | Used trees/pickups by ordinal (`ObjectState`): `Capture`, `Apply` (regrow), `Tick` (sends `ObjectHarvest`), `Encode`/`Decode` |
| `CreatureSpawner.cs` | Host: NavMesh, then animals through `Network_Host_Entities`; `Watch`/`Account` record kills and catches; client tint and health; `Patch` (Harmony by hand) |
| `PlayerHold.cs` | `PlayerHold` keeps a player in place until the island under them loads; `PlayerPlaces` (host) records places; `Players.All`; `SetToValidSpawnPointPatch` |
| `PlayerMove.cs` | `PlayerMove.To`: moves the local player at once, off the raft and ladders first |
| `IslandRadar.cs` | Postfix on `Reciever.HandleUI`: a green dot per entry (`Draw`) |
| `IslandRename.cs` | `IslandRename.Rename`: file, `_<hash>` copies, spawnpool.txt, installed.json, plans and island rules, every world file copy |
| `MyIslandsWindow.cs` | My islands: `SourceOf`, worlds using each island, pool weight, Open/Rename/Delete/Give/Tidy up |
| `Housekeeping.cs` | Tidy up (`Look`, `TidyUp`), `AllWorldCopies`, `WorldsByIsland`, `NamesIn`, `SavedByLine` |
| `FileNames.cs` | `FileNames` name checks (`Problem`, `IslandProblem`, `ReceivedProblem`); `SafeFile` (write aside, `Commit`, `Recover`, `RecoverAll`, `WriteAllLines`) |
| `PatchHealth.cs` | `PatchAll` applies each `[HarmonyPatch]` class alone; `Feature` names a failed one; `ShowIfFailed` on the main menu |

## Main flow
1. `SaveAndLoad.LoadComplete` -> `DynamicIslands.OnLoadComplete` -> `IslandWorldState.OnWorldLoaded`: resets every world-state class; the host reads `WorldCopy.Choose(FilePath)` and makes an `Entry` per island line, `Name = WorldCopy.LocalFileFor(name, hash)`, `HostName` = the name in the line. Clients get the list from the host (see multiplayer.md).
2. `DynamicIslands.Update` -> `CustomIslandSpawner.Tick` -> `StreamIslands`: distance to the land's edge from the raft and the players (host: all players; client: its own). Beyond `WorldRules.UnloadDistance`: `IslandObjectState.Capture`, `IslandSpawner.Despawn`, `Root = null`. Inside the reload distance: `DynamicIslands.SpawnIslandFile`.
3. Host only, with `Enabled`, `ChancePerKm` > 0 and `QuietMinutes` played: once `WorldIslands.Target` of Raft's islands are met (`CountRaftIslands`), `TrySpawn` every 10 s: `PickFromPool`, a spot `Rejects` allows, `IslandWorldState.Add`. A pool island the world already has comes back through `ReturningIslands.BringAgain`. `<generated>` and `type:` entries go through `GenerateAndSpawn`.
4. `SpawnIslandFile`: `SafeFile.Recover`, `IslandFile.LoadOffThread` on a worker task, `PlaceableCatalog.EnsureLoaded`, `IslandSpawner.SpawnInWorldSliced` (land at once, objects over frames under an inactive parent). A missing generated file is remade (`CustomIslandSpawner.RemakeOf`, `RemakeAndSpawn`).
5. With an entry: `RegisterNetworkIds`, `IslandObjectState.Apply`, `BuriedTreasure.OnIslandReady`, `Behaviours.OnIslandReady`, `ContentState.OnIslandReady`, `CreatureSpawner.OnIslandReady`, in that order.
6. Raft saves: `SaveWorldPatch` (postfix on `SaveAndLoad.SaveWorld`) -> `IslandWorldState.Save` (host) -> `SafeFile.WriteAllLines` -> `WorldCopy.AfterSave` (Raft's folders, then `WorldCopy.Send`).
7. World shift: `IslandWorldState.OnWorldShift` moves `IslandSpawner.SpawnedRoots` and every `Entry.Position`, adds to `ShiftedBy`, and tells `CustomIslandSpawner`, `PlayerHold`, `PlayerPlaces`, `WorldRandomizer`.

## Data and keys
- Folder `DynamicIslands.assetpath` (`<Raft>\Mods\DynamicIslands\`); `IslandSpawner.PathFor(name)` = `<name>` + `IslandFile.Extension` (`.island`).
- `.island`: magic "CISL", int32 format, deflate body: name, water level, size, uint16 heights, objects (name, pos, euler, scale). v2 paint and mask; v3 `Elevation`, `Style`; v4 `Props`, per-object props, then `Tail` blocks (tag, length, bytes, ended by an empty tag; `MixTag` "mix", "uid" stable object ids from `StableIds`). `FormatVersion` 4, `MaxObjects` 100000.
- `<name>_<12 hex>.island`: a copy from a host or kept for a saved world (`IslandNetwork.DownloadName`, `IsDownloadName`). `gen-<style>-<seed>`: generated (`GeneratedPrefix`, `FreeName`).
- World file `worlds\<world guid>.txt` (`IslandWorldState.WorldFilePath`). Island line `name|x|y|z|state|rule|label|hash`. `@auto=`, `@modversion=`; `WorldCopy.StampLines`: `@savedat=`, `@savecount=`, `@raftsave=` or `@between=`; `@savedby=` (`Housekeeping.SavedByLine`); `@place=` (`PlayerPlaces`); other classes add their own `@key=` lines.
- `CustomIslands.txt` (`WorldCopy.FileName`) in Raft's `World\<name>` folder and, on a Raft save, in its newest save folder.
- Object state `ordinal,active,yield,day;...`. Keys: pickups up to 0xFFFF; `CreatureSpawner.StateKeyBase` 0x10000; `ContentState.LootKeyBase` 0x20000; `TriggerZone.KeyBase` 0x30000.
- Pickup network index `0x40000000 | (islandId & 0x3FFF) << 16 | n` (`RegisterNetworkIds`).
- `spawnpool.txt` (`PoolFileName`): keys in `SetValue` (`chanceperkm`, `quietminutes`, `unloaddistance`, `regrowdays`, `showonreceiver`, `receiverdistance`...); pool lines with weights, `<generated>`, `type:<name>`.

## Where to change X
- File format: `IslandFile.Save` and `IslandFile.Read`. Terrain, paint, flying: `IslandSpawner.MakeRoot`, `MakeFlying`. Objects: `SpawnBody`.
- When and where random islands appear: `CustomIslandSpawner.Tick`, `TrySpawn`, `Rejects`, `PickFromPool`. Streaming: `StreamIslands`.
- Run something when an island is ready: the block after `SpawnInWorldSliced` in `DynamicIslands.SpawnIslandFile`. On unload: `IslandSpawner.Despawn`.
- A new per-world value: `WriteLines`/`ReadLine`/`Reset`/`HasState` in its class, wired into `IslandWorldState.Save` and `OnWorldLoaded`.

## Traps
- C# 7.3 (`LangVersion` in `DynamicIslands.csproj`); RML compiles the `.cs` files in game.
- `Save` writes the oldest format that holds the data (`NeedsFormat3`, `NeedsFormat4`); `Read` refuses a format above `FormatVersion`. New data goes in a new `Tail` tag (unknown tags are kept on save), not a new format number.
- Write files through `SafeFile`. `LoadOffThread` doesn't call `SafeFile.Recover` (`Load` does): the caller does, on the main thread. `IslandFile.Save` calls `IslandCache.ForgetFile`; other code that writes or copies an island file calls it too.
- `Entry.Name` is this PC's file (maybe `<name>_<hash>`); `Entry.HostName` is the island's name, written to the world file and sent to players.
- `Entry.Id` comes from `IslandNetwork.NewId()` each session and isn't saved; ordinals are. Network indexes need the same object order on every machine and the top bit clear.
- In spawn coroutines, after each `yield` check `IslandWorldState.Contains(entry)` and `entry.Root`, and use `entry.Position` (it follows shifts) or `ShiftedBy`. `Entry.Loading` stops a second spawn.
- Host only: `TrySpawn`, `IslandWorldState.Save`, creature spawning, `PlayerPlaces`. The host's `Tick` waits while `IslandWorldState.ForThisWorld` is false; `Save` does nothing before `OnWorldLoaded` ran for this world or after its read failed.
- `Save` deletes the world file when no class `HasState` (and no islands, no kept lines): a new state class must join that check. Unknown `@` lines stay in `keptLines`. Some `ReadLine` calls get the key lower-cased, some don't. A setting (not progress) key goes in `WorldCopy.SettingKeys`.
- Typed names: `FileNames.IslandProblem`. Names from another PC: `FileNames.ReceivedProblem`.
- `Housekeeping.NamesIn` decides which islands a world uses (island lines, `@planrule=`, `@planhash=`): a new world-file key that names an island goes there and in `IslandRename.Rename`.
- `[HarmonyPatch]` classes are found by `PatchHealth.PatchAll`; `PatchHealth.Feature` maps a class-name keyword to a feature name. `CreatureSpawner.Patch` patches by hand; undo a guard in a Finalizer, not a Postfix.
- Raft events go through `DynamicIslands.RaftEvent`, ticks through `TickError`: nothing may throw into Raft.
- Docs: README "Source overview" (one line per file) and its Installing file table; player-visible changes also in `docs/GUIDE.md` (the PDF is made from it).
