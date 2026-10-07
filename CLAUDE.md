# Custom Islands (DynamicIslands)

Read `docs/claude/MAP.md` first. It says where things are and which one
doc to read for a task. Open code only after that. For a new feature, also
read your kind's section of `docs/claude/CHANGE_CHECKLISTS.md`.

- The mod's C# is compiled by RML inside Raft: **C# 7.3, .NET 4.8**. No
  newer language features.
- Check builds with `compile.ps1` (prints `BUILD OK` or grouped errors),
  not raw MSBuild. Run dev tests with `ci.ps1` (prints only that run's
  `[CITEST]` lines); don't read `Player.log` yourself. If `ci.ps1` fails
  for a reason of its own, grep `Player.log` for `[CITEST]` lines from the
  run's start instead, and say what went wrong with the script.
- Leave `DevTests*.cs` out of searches unless the task is about tests
  (Grep glob `!DevTests*`).
- Never read these whole; grep them and read small ranges:
  `docs/GUIDE.md` (~73k tokens), `README.md` (some lines are over 2,000
  characters), the guide PDF, `.recipe` files, `Player.log`, the measured
  data `DynamicIslands/raft_land.txt`, `raft_underwater.txt`,
  `catalog_index.txt`, and the object lists in `content/objects/`.
- Any change a player or island builder sees updates `docs/GUIDE.md` (then
  the PDF), README (Features, the section, file table, console commands)
  and `CHANGELOG.md` in the same commit. `docs/claude/areas/docs.md` says
  where each kind of feature goes.
- When a change makes a doc in `docs/claude/` wrong, fix that doc in the
  same commit.
