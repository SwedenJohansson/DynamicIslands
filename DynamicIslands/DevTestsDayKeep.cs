using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using RaftModLoader;
using UnityEngine;
using DynamicIslands.Editor;

namespace DynamicIslands
{
	/// <summary>
	/// Day bug 11-11 (the calendar back at day 0 after a restart; DayRepair.cs). TEST_CATALOGUE DAY rows.
	/// CIDay shows/sets the day, CIDayRepair on|off switches the repair (off = 3.4), CIDayRepairUnit checks the backup scan.
	/// The in-game repro: CINewWorld, CIDay 7, CISave, CIInvFix guard off, CIInvFix exit save (Raft quits as before 3.4),
	/// restart Raft, CILoadWorld, CIDay.
	/// </summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIDay", docs: "Dev: logs the day (DAY <n>), the world id and the last day repair; CIDay <n> sets the day (host, a 'CI ...' world)")]
		public static void DayCommand(string[] args)
		{
			int day;
			if (args != null && args.Length > 0 && int.TryParse(args[0], out day))
			{
				if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("CIDay <n>: host only, in a 'CI ...' world"); return; }
				DayRepair.SetDay(day);
			}
			Log("DAY " + WorldManager.DayCounter + " world=\"" + SaveAndLoad.CurrentGameFileName + "\" id=" + SaveAndLoad.WorldGuid
				+ " repair=" + (DayRepair.TestOff ? "off" : "on") + " last=" + (DayRepair.Last ?? "none") + " idrepair=" + (WorldIdRepair.Last ?? "none"));
			Log("PASS: day " + WorldManager.DayCounter);
		}

		[ConsoleCommand(name: "CIDayRepair", docs: "Dev: CIDayRepair on|off - the day repair on world load (off = as 3.4)")]
		public static void DayRepairCommand(string[] args)
		{
			DayRepair.TestOff = args != null && args.Length > 0 && args[0].ToLowerInvariant() == "off";
			Log("PASS: day repair " + (DayRepair.TestOff ? "off" : "on"));
		}

		[ConsoleCommand(name: "CIDayRepairUnit", docs: "Dev, in game (host, a saved 'CI ...' world): DayRepair's backup scan on made-up backup folders - a save with the id then one without (the quit save before 3.4) gives the day back; other orders don't")]
		public static void DayRepairUnitCommand() { StartTest(DayRepairUnitRoutine()); }

		static IEnumerator DayRepairUnitRoutine()
		{
			string world = WorldCopy.RaftWorldFolder;
			string newest = world == null ? null : Directory.GetDirectories(world).Where(d => d.EndsWith("-Latest")).Select(d => Directory.GetFiles(d, "*.rgd").FirstOrDefault()).FirstOrDefault(f => f != null);
			if (!Raft_Network.IsHost || newest == null || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("day repair unit: needs the host in a saved 'CI ...' world"); yield break; }
			RGD_Game game = SaveAndLoad.Load<RGD_Game>(newest);
			Guid id = Guid.NewGuid(), other = Guid.NewGuid();
			string tmp = Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), "ci_dayrepair");
			bool ok = true;
			Func<string, Guid, int, int> Make = (folder, g, day) =>
			{
				string d = Path.Combine(tmp, folder);
				Directory.CreateDirectory(d);
				game.world.worldGuid = g.ToString();
				game.world.dayCounter = day;
				using (var fs = File.Create(Path.Combine(d, "ci.rgd"))) new BinaryFormatter().Serialize(fs, game);
				return day;
			};
			Action Clear = () => { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { } };
			string from;
			try
			{
				// the 3.3 quit: good save (day 7) -> quit save without id and day 0
				Clear();
				Make("2026.01.01-10.00", id, 6); Make("2026.01.01-10.05", id, 7); Make("2026.01.01-10.05-Latest", Guid.Empty, 0);
				int d1 = DayRepair.DayBeforeReset(tmp, id, out from);
				Check(ref ok, d1 == 7 && from == "2026.01.01-10.05", "good save then a quit save without id: day " + d1 + " from " + from + " (want 7)");
				// 3.4 played on after its id repair: two more saves with the id, days 1 and 2 since the reset
				Make("2026.01.01-10.20", id, 1); Directory.Move(Path.Combine(tmp, "2026.01.01-10.05-Latest"), Path.Combine(tmp, "2026.01.01-10.06")); Make("2026.01.01-10.30-Latest", id, 2);
				int d2 = DayRepair.DayBeforeReset(tmp, id, out from);
				Check(ref ok, d2 == 7, "saves with the id after the reset: still day " + d2 + " (want 7; the repair makes it 7 + 2)");
				// another world's id: nothing
				int d3 = DayRepair.DayBeforeReset(tmp, other, out from);
				Check(ref ok, d3 == -1, "another id: " + d3 + " (want -1)");
				// a world that never lost its day (all saves with the id): nothing
				Clear();
				Make("2026.01.01-10.00", id, 6); Make("2026.01.01-10.05-Latest", id, 7);
				int d4 = DayRepair.DayBeforeReset(tmp, id, out from);
				Check(ref ok, d4 == -1, "no save without id: " + d4 + " (want -1)");
				// an old world that never had an id: nothing
				Clear();
				Make("2026.01.01-10.00", Guid.Empty, 6); Make("2026.01.01-10.05-Latest", Guid.Empty, 7);
				int d5 = DayRepair.DayBeforeReset(tmp, id, out from);
				Check(ref ok, d5 == -1, "no save with the id: " + d5 + " (want -1)");
			}
			finally { Clear(); }
			yield return null;
			if (ok) Log("PASS: day repair unit"); else Fail("day repair unit");
		}
	}
}
