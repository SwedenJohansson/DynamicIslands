# Custom Islands (DynamicIslands)

Read `docs/claude/MAP.md` first. It says where things are and which one
doc to read for a task. Open code only after that.

- The mod's C# is compiled by RML inside Raft: **C# 7.3, .NET 4.8**. No
  newer language features.
- Check builds with `compile.ps1` (prints `BUILD OK` or grouped errors),
  not raw MSBuild.
- Leave `DevTests*.cs` out of searches unless the task is about tests
  (Grep glob `!DevTests*`).
- Never read these whole: `docs/GUIDE.md`, `README.md`, the guide PDF,
  `.recipe` files, `Player.log`. Grep for the heading or lines you need.
- Any change a player or island builder sees updates `docs/GUIDE.md` (then
  the PDF), README (Features, the section, file table, console commands)
  and `CHANGELOG.md` in the same commit.
- When a change makes a doc in `docs/claude/` wrong, fix that doc in the
  same commit.
