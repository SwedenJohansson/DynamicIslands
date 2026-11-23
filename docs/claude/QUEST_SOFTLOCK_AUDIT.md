# Quest softlock audit (2026-11-23)

Summary: every way found that a library storyline or quest could become impossible to finish, from a code and
recipe review (SL1-SL14, the 3.5.1 library) plus Raft tests through the queue (TEST_CATALOGUE QSL1-QSL4).
- 3 High/Medium softlocks fixed in the engine: stale `note:N` done-whens (2 plans), journal pages cut by the
  recipe tool (3 islands), a refused multiplayer event eating the crew's story item.
- 2 checker gaps closed: beside rules' done-whens are now checked (PlanChecker, questcheck), and questcheck warns on
  a story item with `raft=` that a has/take check or cage key needs.
- Checked and sound: kill counting (any killer, early kills, strays, puffer self-kills), blueprint steps (every
  step blueprint teaches a recipe), story items found only in chests (WARN only).
- Proposed, not fixed (user decision): an engine guard for taken-out story items (T8), and the multiplayer
  edge cases M3-M9 below (code review only, none reached by a library storyline yet).
- Rebuild needed: Camp Blackwater, The Dead Letter Office, Wreckers' Cove (journal pages); Stillwater Terraces,
  Static Shoals, Altitude Market, Caisson Yard, Hadal Commons, Shuttlewreck Reef, The Stalker's Hollow (loot names).
- Branch `quest-softlock` (base content-1104 250ccb39). The 3.5.1 plans were hot-fixed by main (note titles).

| # | Storyline / island | Step | How it softlocks | Severity | Proof | Status |
|---|---|---|---|---|---|---|
| 1 | The Last Post (plan `note:2495`), The Lullaby Frequency (`note:3`) | story chapter done-when | `note:N` names a raw object index; a rebuild moved it, so the chapter never shows done | High | QSL1 `CIStoryNoteStale`: FAIL before, PASS after | Fixed: notes by title, stale number falls back to the island's only note, else its quest; plans use titles |
| 2 | Any plan with `beside` rules | beside done-when | PlanChecker/questcheck never checked a beside rule's done-when, so a stale one slipped through | Medium (checker gap) | QSL1 plan-check rows | Fixed (PlanChecker, questcheck BESIDE) |
| 3 | Camp Blackwater, The Dead Letter Office, Wreckers' Cove | islander journal page | recipe `\\n` turned into a real line break, cutting the journal action after its first line (later paragraphs ran as broken actions) | Medium | QSL2 `CIRecipeJournalBreak`: FAIL before, PASS after | Fixed in the recipe tool and the 3 recipes; needs rebuild |
| 4 | 7 islands (see summary) | loot | loot names Raft doesn't know: the item never drops | Low (not a softlock) | questcheck ERROR 7 | Recipes fixed (dcd98370); needs rebuild |
| 5 | Engine (only The Tide Engine's `relic` has `raft=`; no check needs it) | has/take check, cage key | a story item taken out as its Raft item lowers Count for good: a check needing it stays shut | Low (latent) | code review; questcheck WARN injected test | Lint + guide + editor tip; engine guard proposed |
| 6 | All `blueprint` steps (35 blueprints, 346 steps) | blueprint step | a blueprint that teaches nothing counts only while carried | none found | QSL3 `CIBlueprintTeaches` PASS | Sound; test kept as a guard |
| 7 | All kill steps | kill | deaths counted whatever the killer; early kills remembered; strays respawn or return | none found | code review (CreatureSpawner.Account) + questcheck kill counts | Sound |
| 8 | Any keycard door/zone/note in multiplayer | a story `take` check | the player's machine takes the story item before asking; a host refusal (not loaded, or the last one used at the same moment) did nothing and the item was gone | Medium | QSL4 `CIRefusedTakeBack`: FAIL before, PASS after | Fixed: host gives the takes back (`Behaviours.GiveBackTakes`) |
| M3 | MP | zone/chest | a zone or chest refused while not loaded stays marked used (`FreeRefused` needs `Root`) | Low | code review | Proposed |
| M4 | MP | pickup | a pickup's claim key stays used after a refusal | Low | code review | Proposed |
| M5 | MP | any event | the not-loaded queue drops entries after 64 events or 600 s | Low | code review | Proposed |
| M6 | Any | dig step | a dug story treasure with regrow 0 can't be dug again if the item is lost | Low | code review | Proposed |
| M7 | Any | `wait` in an event | a non-resumable event with a wait loses its later part on unload/quit | Low | code review | Proposed |
| M8 | Any | QuestSkip | skipping a step doesn't replay its object events (a door it opened stays shut) | Low | code review | Proposed |
| M9 | Any | has/take of consumables | a check on an item the player can eat/use up elsewhere | Low | code review | Proposed |
| 10 | Several | collect | story items found only in chests | none (WARN) | questcheck WARN | Sound |
