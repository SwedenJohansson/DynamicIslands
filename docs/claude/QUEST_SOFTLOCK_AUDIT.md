# Quest softlock audit (2026-11-23)

Summary: every way found that a library storyline or quest could become impossible to finish, from a code and
recipe review (SL1-SL14, the 3.5.1 library) plus Raft tests through the queue (TEST_CATALOGUE QSL1-QSL12).
- 3 High/Medium softlocks fixed in the engine: stale `note:N` done-whens (2 plans), journal pages cut by the
  recipe tool (3 islands), a refused multiplayer event eating the crew's story item.
- 2 checker gaps closed: beside rules' done-whens are now checked (PlanChecker, questcheck), and questcheck warns on
  a story item with `raft=` that a has/take check or cage key needs.
- Checked and sound: kill counting (any killer, early kills, strays, puffer self-kills), blueprint steps (every
  step blueprint teaches a recipe), story items found only in chests (WARN only).
- T8 engine guard (user decision 11-23): a story item a has/take check or cage key still needs can't be taken out
  of the journal (QSL6). M3-M9 all fixed (user 11-23 "finish the mod"; QSL7-QSL12, each FAIL before, PASS after).
- M8 closed fully (user 11-23): QuestSkip also replays open, set and light-up steps (QSL10b `CISkipReplaysObjects`);
  steps without an object event (kill, collect, blueprint, has...) just count as done.
- Rows 8, M3, M4, M5, M8 also played by two real players (host + Sandboxie box Raft2, `CIQslMp` + qslmp.ps1): 23/23 PASS
  (host 20261123112820_qslmp2_15467 + box 20261123112819_qslmp2_19933).
- 11-24 (branch `skipbtn`, user "make sure that button always works"): the skip could not reach a side quest behind
  an open main quest, a quest stuck at its first step out of the panel's reach (after a reload), or a client's quest
  far from the host. `QuestSkip.Candidates` + "Other quest" fix all three; Spawn to raft sits beside Skip in
  Esc > Custom Islands for every player (TEST_CATALOGUE SB1-SB6). Still by design: a client can't skip (asks the host);
  kill, walk, tame, pages and counter steps replay nothing.
- Rebuild needed: Camp Blackwater, The Dead Letter Office, Wreckers' Cove (journal pages); Stillwater Terraces,
  Static Shoals, Altitude Market, Caisson Yard, Hadal Commons, Shuttlewreck Reef, The Stalker's Hollow (loot names).
- Branch `quest-softlock` (base content-1104 250ccb39). The 3.5.1 plans were hot-fixed by main (note titles).

| # | Storyline / island | Step | How it softlocks | Severity | Proof | Status |
|---|---|---|---|---|---|---|
| 1 | The Last Post (plan `note:2495`), The Lullaby Frequency (`note:3`) | story chapter done-when | `note:N` names a raw object index; a rebuild moved it, so the chapter never shows done | High | QSL1 `CIStoryNoteStale`: FAIL before, PASS after | Fixed: notes by title, stale number falls back to the island's only note, else its quest; plans use titles |
| 2 | Any plan with `beside` rules | beside done-when | PlanChecker/questcheck never checked a beside rule's done-when, so a stale one slipped through | Medium (checker gap) | QSL1 plan-check rows | Fixed (PlanChecker, questcheck BESIDE) |
| 3 | Camp Blackwater, The Dead Letter Office, Wreckers' Cove | islander journal page | recipe `\\n` turned into a real line break, cutting the journal action after its first line (later paragraphs ran as broken actions) | Medium | QSL2 `CIRecipeJournalBreak`: FAIL before, PASS after | Fixed in the recipe tool and the 3 recipes; needs rebuild |
| 4 | 7 islands (see summary) | loot | loot names Raft doesn't know: the item never drops | Low (not a softlock) | questcheck ERROR 7 | Recipes fixed (dcd98370); needs rebuild |
| 5 | Engine (only The Tide Engine's `relic` has `raft=`; no check needs it) | has/take check, cage key | a story item taken out as its Raft item lowers Count for good: a check needing it stays shut | Low (latent) | code review; questcheck injected test; QSL6 `CIStoryTakeOutGuard`: FAIL before, PASS after | Fixed: `StoryNeeds` guard (Take out greyed out, "Needed on <island>"; host refuses with the reason); questcheck now INFO |
| 6 | All `blueprint` steps (35 blueprints, 346 steps) | blueprint step | a blueprint that teaches nothing counts only while carried | none found | QSL3 `CIBlueprintTeaches` PASS | Sound; test kept as a guard |
| 7 | All kill steps | kill | deaths counted whatever the killer; early kills remembered; strays respawn or return | none found | code review (CreatureSpawner.Account) + questcheck kill counts | Sound |
| 8 | Any keycard door/zone/note in multiplayer | a story `take` check | the player's machine takes the story item before asking; a host refusal (not loaded, or the last one used at the same moment) did nothing and the item was gone | Medium | QSL4 `CIRefusedTakeBack`: FAIL before, PASS after; two players (qslmp): zone and chest take refused, card back, PASS | Fixed: host gives the takes back (`Behaviours.GiveBackTakes`) |
| M3 | MP | zone/chest | a zone or chest refused while not loaded stays marked used (`FreeRefused` needs `Root`) | Low | QSL7 `CIRefusedFreesFar`: FAIL before, PASS after; two players (qslmp): zone and chest ready again on both, PASS | Fixed: `FreeRefused` reads the zones/chests from the island file when not loaded |
| M4 | MP | pickup | a pickup's claim key stays used after a refusal | Low | QSL7 `CIRefusedFreesFar`; two players (qslmp): pickup free on both, nothing given, PASS | Fixed: the story pickup's claim key is freed too |
| M5 | MP | any event | the not-loaded queue drops entries after 64 events or 600 s | Low | QSL8 `CIQueuedSurvivesSave`: FAIL before, PASS after; two players (qslmp): player 2's lever + zone queued, done after the host's save, quit and reload, PASS | Fixed: kept in the island's saved state (`QueuedBase`), no time limit, 256 per island, duplicates dropped, warning when full |
| M6 | Any | dig step | a dug story treasure with regrow 0 can't be dug again if the item is lost | Low | QSL9 `CIStoryTreasureRegrows`: FAIL before, PASS after | Fixed: on load, a story treasure whose item the crew lacks while a check/cage needs it can be dug again (`RegrowForStory`) |
| M7 | Any | `wait` in an event | a non-resumable event with a wait loses its later part on unload/quit | Low | QSL12 `CIFix3Pending` PASS (old test asserted arrive!/quest! not kept) | Fixed: `ExtraPendingBase` keeps phase!, stack!, arrive!, quest!, solved/wrong, crowd/crowdleft, trade, choice4 |
| M8 | Any | QuestSkip | skipping a step doesn't replay its object events (a door it opened stays shut) | Low | QSL10 `CISkipReplays`, QSL10b `CISkipReplaysObjects`: FAIL before (20261123102142_qslmp_21333), PASS after; two players (qslmp): read/reach/talk/open skipped while not loaded, done after the reload, PASS | Fixed: read/reach/talk (note, zone, islander), open (the chest opened: checks, used mark, loot, open event), set (signal + each piece's set) and light-up (the group's lights on: lit, alllit, signal; only when the step asks for the whole group); nothing twice; not loaded: queued |
| M9 | Any | has/take of consumables | a check on an item the player can eat/use up elsewhere | Low | QSL11 inject_m9: FAIL before, PASS after; 3.5.1 library: none | Fixed in questcheck: blueprint WARN, food/drink INFO |
| 10 | Several | collect | story items found only in chests | none (WARN) | questcheck WARN | Sound |
