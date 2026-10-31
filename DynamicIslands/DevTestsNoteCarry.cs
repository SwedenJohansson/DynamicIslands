using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Review 2026-10-30, Files #2: journal pages keyed by an object's place move with a carried world state.</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CINoteCarry", docs: "Dev: Files #2 - an island edit carried to a saved world moves its journal pages (note:/act: keys) to the same objects, per island copy; a removed object's page is kept under gone<n>")]
		public static void NoteCarryCommand()
		{
			bool ok = true;
			string isl = "ci_notecarry_" + DateTime.Now.Ticks;
			string folder = Path.Combine(DynamicIslands.assetpath, "worlds");
			string world = Path.Combine(folder, isl + ".txt");
			var cleanup = new List<string> { IslandSpawner.PathFor(isl), world };
			try
			{
				if (!PlaceableCatalog.IsBuilt) { Fail("note carry: the object catalog isn't loaded yet"); return; }
				Func<string, float, IslandObject> obj = (n, x) => new IslandObject { Name = n, Position = new Vector3(x, 5, 0), EulerRotation = Vector3.zero, Scale = Vector3.one };

				// Version 1: four objects (numbers 1-4), kept as the copy the world plays
				var f1 = new IslandFile { Name = isl, TerrainSize = new Vector3(10, 10, 10), HeightmapResolution = 33, Heights = new float[33, 33] };
				f1.Objects.AddRange(new[] { obj("Loot_Chest", 0), obj("Loot_Chest", 1), obj("Loot_Chest", 2), obj("Loot_Chest", 3) });
				f1.Save(IslandSpawner.PathFor(isl));
				IslandFile a = IslandFile.Load(IslandSpawner.PathFor(isl));
				string h0 = IslandNetwork.HashOf(isl);
				string copy = IslandSpawner.PathFor(IslandNetwork.DownloadName(isl, h0));
				File.Copy(IslandSpawner.PathFor(isl), copy, true);
				cleanup.Add(copy);
				cleanup.Add(Path.Combine(Path.Combine(Path.Combine(DynamicIslands.assetpath, IslandFilesWindow.DeletedFolderName), LibraryPack.KeptVersionsFolder), Path.GetFileName(copy)));

				// Version 2: two objects put first, the second one removed: places 0 -> 2, 1 -> gone, 2 -> 3, 3 -> 4
				IslandFile b = IslandFile.Load(IslandSpawner.PathFor(isl));
				b.Objects.RemoveAt(1);
				b.Objects.Insert(0, obj("Loot_Chest", 8));
				b.Objects.Insert(0, obj("Loot_Chest", 9));
				StableIds.Prepare(b, a);
				b.Save(IslandSpawner.PathFor(isl));
				File.SetLastWriteTimeUtc(IslandSpawner.PathFor(isl), DateTime.UtcNow.AddSeconds(2)); // (HashOf caches by write time)
				string h1 = IslandNetwork.HashOf(isl);
				int[] map = StableIds.IndexMap(a, IslandFile.Load(IslandSpawner.PathFor(isl)));
				Check(ref ok, h1 != h0 && map.SequenceEqual(new[] { 2, -1, 3, 4 }), "the objects' places map 0->2, 1 gone, 2->3, 3->4 (" + string.Join(",", map.Select(x => x.ToString()).ToArray()) + ")");

				// The world: two copies of the island, both on version 1, and the pages read there
				Func<string, string, string> page = (key, title) => "@story.page=" + string.Join("|", new[] { key, "3", title, "Island", "Text of " + title }.Select(Uri.EscapeDataString).ToArray());
				string c2 = isl + "|2";
				var lines = new List<string>
				{
					"# Custom islands in world 'CI note carry': name|x|y|z|used objects|rule|receiver label|island file hash",
					"@auto=on",
					page("note:" + isl + ":0", "first"),
					page("note:" + isl + ":1", "removed"),
					page("note:" + isl + ":gone1", "an earlier removed one"),
					page("act:" + isl + ":3:Door: open", "door"),
					page("act:" + isl + ":" + Behaviours.IslandIndex + ":Arrived", "arrived"),
					page("note:" + c2 + ":2", "second copy"),
					page("note:Other:0", "other island"),
					page("msg:" + isl + ":0000abcd", "message"),
					isl + "|0|0|0||||" + h0,
					isl + "|100|0|0||||" + h0,
				};
				File.WriteAllLines(world, lines.ToArray());

				List<string> moved;
				int carried;
				LibraryPack.GiveWorldsThisVersion(isl, out moved, true, out carried);
				string[] back = File.ReadAllLines(world);
				var keys = back.Select(StoryBook.PageKeyOfLine).Where(k => k != null).ToList();
				var want = new List<string>
				{
					"note:" + isl + ":2", "note:" + isl + ":gone1-2", "note:" + isl + ":gone1", "act:" + isl + ":4:Door: open",
					"act:" + isl + ":" + Behaviours.IslandIndex + ":Arrived", "note:" + c2 + ":3", "note:Other:0", "msg:" + isl + ":0000abcd",
				};
				Check(ref ok, carried == 2, "both copies' state was carried (" + carried + ")");
				Check(ref ok, back.Count(l => l.StartsWith(isl + "|") && l.EndsWith("|" + h1)) == 2, "both copies play the new file");
				Check(ref ok, keys.SequenceEqual(want), "the pages follow their objects\n    got    " + string.Join(", ", keys.ToArray()) + "\n    wanted " + string.Join(", ", want.ToArray()));
				// (the other fields as they were)
				string door = back.FirstOrDefault(l => StoryBook.PageKeyOfLine(l) == "act:" + isl + ":4:Door: open");
				Check(ref ok, door != null && door.EndsWith("|3|door|Island|" + Uri.EscapeDataString("Text of door")), "a moved page keeps its day, title, island and text: " + door);

				// Keys MovePageKey leaves alone
				int[] m = { 1, -1 };
				Check(ref ok, StableIds.MovePageKey("note:Cove:x", "Cove", m, null) == "note:Cove:x" && StableIds.MovePageKey("act:Cove:0", "Cove", m, null) == "act:Cove:0" &&
					StableIds.MovePageKey("note:Cove:5", "Cove", m, null) == "note:Cove:5" && StableIds.MovePageKey("note:Cove|2:0", "Cove", m, null) == "note:Cove|2:0" &&
					StableIds.MovePageKey("note:Cove:0", "Cove", m, null) == "note:Cove:1" && StableIds.MovePageKey("storyfreq:1", "Cove", m, null) == "storyfreq:1",
					"keys of other islands or copies, past the objects, or without a number stay as they are");
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally { foreach (string p in cleanup) try { if (File.Exists(p)) File.Delete(p); } catch { } IslandCache.Forget(); }
			if (ok) Log("PASS: note carry (Files #2)"); else Fail("note carry (Files #2)");
		}
	}
}
