# Multiplayer
The host owns the world: its island list, world file and settings. Clients copy them, fetch island files they lack, and send what their player does to the host, which decides and passes it on.

## Files
| File | Role |
|---|---|
| `IslandNetwork.cs` | `IslandNetMessage` (one `[Serializable]` class for every kind) and `IslandNetwork`: send helpers, `OnMessage`, join reply, island list, file transfer, `Resync`, `HashOf` |
| `DynamicIslands.cs` | `OnNetworkMessage` (RML) -> `IslandNetwork.OnMessage`; `HookRaftEvents` re-hooks `Raft_Network.OnWorldReceivedLate`; `Update` -> `IslandNetwork.Tick`; console command `Resync` (`ResyncCommand`) |
| `Claims.cs` | Things only one player can have (a chest's loot, a once-zone) |
| `WorldCopy.cs` | The world file sent to every player, so any of them can host later |
| `WorldRules.cs` | Kind 15, and the host's spawnpool.txt settings every player shares |
| `UpdateCheck.cs` | GitHub release check on the main menu; `MismatchText` for a version difference |

## Main flow
1. Client: `Raft_Network.OnWorldReceivedLate` -> `IslandNetwork.OnWorldReceived`: resets `WorldRules`, `WorldOptions`, `StoryChain`, `WorldCopy`; empties the island list.
2. `IslandNetwork.Tick` sends `SyncRequest` (`Name` = `"version:"` + `LibraryPack.ModVersion`) every `SyncRetrySeconds` (5 s); after `SyncMaxTries` (12) every 30 s, with a notice.
3. Host, `OnMessage` case `SyncRequest`: `CompareVersions`; replies `SyncRequest` with its version and `HostCapabilities` (`"counts,spots,events"`); then `JoinPart` each, in order: `WorldRules.Message`, `IslandsMessage(..., true)`, `StoryBook.StateMessage`, `WorldOptions.Message`, `StoryChain.Message`, `QuestCount.Message`, `CreatureSpawner.SpotsMessage(null)`, `WorldRandomizer.Message`, `PlayerPlaces.PlaceMessage`; then `PlayerLevels.StateFor` -> `SendLevels`, `WorldCopy.Send(to)`, `AskPlayers(to)` when files are wanted.
4. Client `ReceiveIslands`: each island at `FromHost` (x,z from its own raft, the host's y); a known island more than 5 m off is moved. A `FullList` drops islands not in it and sets `synced`. New entries -> `ResolveFile`.
5. `ResolveFile`: the same-name file if its hash matches; else `<name>_<hash>.island`; else a copy of any `*_<hash>.island`; else `FileRequest` and `Entry.WaitingForFile`.
6. Host `SendFile` -> `SendChunks`: `FileChunk`s of `ChunkBytes` (3000) in base64, `ChunksPerFrame` (6) a frame. `ReceiveChunk`: only hashes in `requested`; checks `Hash`; saves `<name>_<hash>.island`; clears `WaitingForFile`, so streaming spawns it.
7. Afterwards: `BroadcastAdded`/`BroadcastRemoved`; `WorldRules.Broadcast` (also from `WorldRules.OnPoolChanged`), `WorldOptions.Broadcast`; `WorldCopy.Send(null)` after each save.
8. Claims: `LootCrate`, `TriggerZone`, `Behaviours` call `Claims.May(entry, key, then)`. Host: `HostGrant` (not `ContentState.IsUsed`, not held by another) holds it `HoldSeconds` (6 s). Client: `SendClaim`, host answers `Count` 1/0. No answer in 3 s: with `HostAnswersClaims` it asks again every 5 s up to 30 s, else (an older host) it goes ahead. A grant older than `FreshAnswerSeconds` is asked again.
9. Retries: `RetryLateFiles` asks again for a file with no new chunk in `FileRetrySeconds` (30 s), keeping parts that came. `Resync` clears `synced`, `requested`, `incoming`; the next `Tick` asks for the whole join reply.
10. Host lacks a file (a world from another host): `WorldCopy.LocalFileFor` -> `WantFromPlayers`; `FileWanted` now, to each joiner and every 30 s; a player who has that content answers with `FileChunk`s (`SendFile(..., quiet)`).
11. Versions: `CompareVersions` on both sides, text from `UpdateCheck.MismatchText`; a `FullList` with no version reply first means an older host (`UpdateCheck.Older`). `UpdateCheck.OnMainMenu` asks GitHub (`ReleasesUrl`) once per start; off with `updatecheck=off` in world_rules.txt or `online = off` in library.txt.

## Data and keys
Fields: `Ids`, `Names`, `Hashes`, `Offsets`, `States`, `Labels`, `Rules`, `FullList`, `Name`, `Hash`, `Index`, `Count`, `Data`. "Via host": a client sends to the host, the host applies it and sends it to everyone.
| Kind | Direction | Carries |
|---|---|---|
| 1 `Islands` | host -> clients | per island `Ids`, `Names`, `Hashes`, `Offsets` (3 floats), `States`, `Labels`, `Rules`; `FullList` |
| 2 `Remove` | host -> clients | `Ids` |
| 3 `SyncRequest` | both | `Name` version; host's reply `Data` = capabilities |
| 4 `FileRequest`, 5 `FileChunk`, 23 `FileWanted` | see steps 5, 6, 10 | `Name`, `Hash`; chunks `Index`/`Count`/`Data` |
| 6 `ObjectUsed` | via host | `Ids[0]` island, `Index` state key, `Count` day (host sets its own day; below 0 = available again, host only) |
| 7 `QuestStep`, 25 `QuestStepMore` | via host | `Ids` island (+ quest number), `Index` step, `Count`; `Name` "add" = a player's amount |
| 8 `Announce`, 9 `ObjectSet` | host -> clients | banner `Ids[0]`/`Name`/`Data`/`Offsets`; object `Ids[0]`, `Index`, `Count` bits |
| 10 `EventFired` | both | `Ids[0]`, `Index` object, `Name` event, `FullList` = from host, `Count` 1 = waits / answer |
| 11 `Story`, 14 `Levels` | both | `Name` = sub-kind (`StoryBook.OnMessage`, `PlayerLevels.OnMessage`), `Data` |
| 12 `PlayerPlace` | host -> joiner | `Ids[0]`, `Offsets` from the island |
| 13 `Randomizer`, 17 `WorldOptions`, 19 `StoryChain`, 20 `QuestCount` | host -> clients | `Data` (and `Name`) from that class's `Message` |
| 15 `WorldRules` | host -> clients | `Index` monsters, `Count` build cost %, `Data` `receiver=;unload=;regrow=;rdist=`, `Name` refunds |
| 16 `Claim` | both | `Ids` island and question number, `Index` key, `Count` 1/0 |
| 18 `WorldCopy`, 24 `WorldCopyNewer` | host -> clients; client -> host | `Name` world guid, `Hash` stamp, parts `Index`/`Count`/`Data`; newer: `Data` `savecount;savedat` |
| 21 `CreatureSpots`, 22 `ObjectHarvest` | host -> clients; via host | `Data` `objectIndex:islandId:spot;...`; `Ids` island, active, yield, `Index` ordinal, `Count` day (host sets its own) |

## Where to change X
- New kind: a const in `IslandNetMessage` (next free: 26), a send helper in `IslandNetwork`, a `case` in `IslandNetwork.OnMessage`.
- State a joining player needs: the `JoinPart` list in `OnMessage` case `SyncRequest`, plus a broadcast on change.
- Island list fields: `IslandsMessage` and `ReceiveIslands`. Files: `ResolveFile`, `SendFile`, `ReceiveChunk`.
- One-player-only things: `Claims.May`, `Claims.HostGrant`. Shared settings: `WorldRules.HostSettingsData`, `HostSettingsFrom`.

## Traps
- The host decides. Copy the pattern `if (Raft_Network.IsHost) SendToClients(msg); else if (InMultiplayerGame || Loopback != null) SendToHost(msg);`; the host checks, applies, then passes on. A client's day or "available again" is not trusted (`ObjectUsed`, `ObjectHarvest`).
- Older versions ignore an unknown `Kind`. Don't change what an existing kind means: add a kind (as `QuestStepMore` did). A host behaviour clients rely on goes in `HostCapabilities`; clients reset those flags at the first `SyncRequest`.
- The version reply uses only fields every version has, so an older player can read it; it goes before every other join part.
- Island lists and banners carry x,z from the sender's raft (`CustomIslandSpawner.RaftPosition`) plus absolute y, never world coordinates. Clients drop `Islands` before `worldReceived`.
- `Entry.Id` is the host's. A client may have the island unloaded (`Root == null`): keep state in `Entry.State`, apply on load.
- A new island is announced only once its file exists: `IslandsMessage` skips failed entries and ones with no hash; `TrySpawn` adds generated islands with broadcast off and `GenerateAndSpawn` calls `BroadcastAdded` after the file's `Save`.
- Shared settings: read `WorldRules.UnloadDistance`, `RegrowDays`, `ShowOnReceiver`, `ReceiverDistance`, not the `CustomIslandSpawner` fields.
- Client copies of host state must be reset in `IslandNetwork.OnWorldReceived` or the world-load `Reset`: the next host differs.
- Names and hashes from another PC become file paths: check `IslandNetwork.ReceivedProblem` / `IsHash` (`FileNames.ReceivedProblem`).
- Mod messages and Raft's RPCs (`NetworkChannel.Channel_Game`) keep no order between them: `CreatureSpots` goes before the animal's `Message_CreateAINetworkBehaviour`.
- Big data goes in parts (3000 a part), a few a frame. Each join part is sent on its own (`JoinPart`) so one failure doesn't drop the rest.
- Tests: `IslandNetwork.Loopback` catches sent messages in single-PC dev tests (`DevTests*.cs`); the two-player test helpers (`CIMPState`...) are in `DevTestsMultiplayer.cs`.
