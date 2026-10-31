using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Fix round 3, 2026-10-30: one test per fix (each failed before its fix).</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIFix3Stable", docs: "Dev: fix 3 - an island edit carries a pushed crate's place and a stack zone's done flag (MechFeatures) to the same objects, and keeps the island's own pending keys")]
		public static void Fix3StableCommand()
		{
			bool ok = true;
			string tmp = Path.Combine(DynamicIslands.assetpath, "ci_fix3stable.island");
			try
			{
				if (!PlaceableCatalog.IsBuilt) { Fail("fix3 stable: the object catalog isn't loaded yet"); return; }
				Func<string, float, IslandObject> obj = (n, x) => new IslandObject { Name = n, Position = new Vector3(x, 5, 0), EulerRotation = Vector3.zero, Scale = Vector3.one };
				var f1 = new IslandFile { Name = "ci_fix3stable", TerrainSize = new Vector3(10, 10, 10), HeightmapResolution = 33, Heights = new float[33, 33] };
				f1.Objects.AddRange(new[] { obj("Loot_Chest", 0), obj("Loot_Chest", 1), obj("Loot_Chest", 2), obj("Loot_Chest", 3) });
				f1.Save(tmp);
				IslandFile a = IslandFile.Load(tmp);
				// The edit: a new object first - every object's place moves by one
				IslandFile b = IslandFile.Load(tmp);
				b.Objects.Insert(0, obj("Loot_Chest", 9));
				StableIds.Prepare(b, a);
				b.Save(tmp);
				IslandFile c = IslandFile.Load(tmp);
				Func<int, ObjectState> st = y => new ObjectState { Active = true, Yield = y, Day = 3 };
				int islandPending = Behaviours.PendingBase + (Behaviours.IslandIndex << 4) + 8;
				var was = new Dictionary<int, ObjectState>
				{
					{ MechFeatures.PlaceBase + 2 * 4 + 0, st(11) }, // a crate at place 2, its x -> place 3
					{ MechFeatures.PlaceBase + 2 * 4 + 1, st(12) },
					{ MechFeatures.PlaceBase + 2 * 4 + 3, st(13) },
					{ MechFeatures.StackDoneBase + 1, st(1) },      // a stack zone at place 1, done -> place 2
					{ islandPending, st(2) },                        // the island's arrive, waiting -> kept as it is
				};
				var expect = new Dictionary<int, ObjectState>
				{
					{ MechFeatures.PlaceBase + 3 * 4 + 0, was[MechFeatures.PlaceBase + 2 * 4 + 0] },
					{ MechFeatures.PlaceBase + 3 * 4 + 1, was[MechFeatures.PlaceBase + 2 * 4 + 1] },
					{ MechFeatures.PlaceBase + 3 * 4 + 3, was[MechFeatures.PlaceBase + 2 * 4 + 3] },
					{ MechFeatures.StackDoneBase + 2, was[MechFeatures.StackDoneBase + 1] },
					{ islandPending, was[islandPending] },
				};
				int dropped;
				string unsure;
				string moved = StableIds.Carry(a, c, IslandObjectState.Encode(was), false, out dropped, out unsure);
				Check(ref ok, moved != null && moved == IslandObjectState.Encode(expect) && dropped == 0,
					"a crate's place, a stack zone's done flag and the island's pending arrive follow (dropped " + dropped + (unsure != null ? ", unsure: " + unsure : "") + ")\n    got    " + moved + "\n    wanted " + IslandObjectState.Encode(expect));
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally { try { File.Delete(tmp); } catch { } }
			if (ok) Log("PASS: fix3 stable"); else Fail("fix3 stable");
		}

		[ConsoleCommand(name: "CIFix3Pending", docs: "Dev: fix 3 - every event whose shared actions are kept over a wait reads back as itself from its key (phase!, stack!, arrive!, quest! ran into the next object's keys)")]
		public static void Fix3PendingCommand()
		{
			bool ok = true;
			try
			{
				const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
				Type t = typeof(Behaviours);
				MethodInfo resumable = t.GetMethod("Resumable", flags), key = t.GetMethod("PendingKey", flags), evOf = t.GetMethod("PendingEventOf", flags);
				string[] events = (string[])t.GetField("PendingEvents", flags).GetValue(null);
				string[] more = (string[])t.GetProperty("MorePendingEvents", flags).GetValue(null, null);
				if (resumable == null || key == null || evOf == null || events == null || more == null) { Fail("fix3 pending: Behaviours' pending methods not found"); return; }
				int kept = 0;
				foreach (string ev in events.Concat(more).SelectMany(x => new[] { x, x + "!" }))
				{
					if (!(bool)resumable.Invoke(null, new object[] { ev })) continue;
					kept++;
					foreach (int index in new[] { 5, Behaviours.IslandIndex })
					{
						int k = (int)key.Invoke(null, new object[] { index, ev });
						string back = (string)evOf.Invoke(null, new object[] { k });
						bool sameObject = ((k - (k >= Behaviours.MorePendingBase ? Behaviours.MorePendingBase : Behaviours.PendingBase)) >> 4) == index;
						Check(ref ok, back == ev && sameObject, "'" + ev + "' at " + index + ": its key " + k.ToString("X") + " reads back as '" + back + "'" + (sameObject ? "" : ", another object's"));
					}
				}
				Check(ref ok, !(bool)resumable.Invoke(null, new object[] { "arrive!" }) && !(bool)resumable.Invoke(null, new object[] { "quest!" }), "arrive! and quest! are not kept (they run this session only)");
				Check(ref ok, (bool)resumable.Invoke(null, new object[] { "use!" }) && (bool)resumable.Invoke(null, new object[] { "arrive" }), "use! and arrive are kept");
				Log("  " + kept + " event(s) kept over a wait");
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			if (ok) Log("PASS: fix3 pending"); else Fail("fix3 pending");
		}

		[ConsoleCommand(name: "CIFix3Chest", docs: "Dev: fix 3 - a chest whose open gives a story item never fills up again (the story item came again with every refill)")]
		public static void Fix3ChestCommand()
		{
			bool ok = true;
			var made = new List<GameObject>();
			try
			{
				Func<string, LootCrate> crate = open =>
				{
					var go = new GameObject("ci_fix3_chest");
					made.Add(go);
					var props = new Dictionary<string, string> { { ObjectProps.LootItems, "Plank*2" }, { BehaviourProps.EventKey("open"), open } };
					return LootCrate.Attach(go, "Loot_Chest", props, 0);
				};
				Check(ref ok, !crate("give||story:ci_fix3*1\nmessage||A key!").Refills, "its open gives a story item: it never fills up again");
				Check(ref ok, crate("give||Plank*1\nmessage||A plank!").Refills, "its open gives a plank: it fills up again");
				Check(ref ok, crate("").Refills, "no open actions: it fills up again");
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally { foreach (GameObject go in made) if (go != null) UnityEngine.Object.Destroy(go); }
			if (ok) Log("PASS: fix3 chest"); else Fail("fix3 chest");
		}

		[ConsoleCommand(name: "CIFix3RunShared", docs: "Dev, in game (host): fix 3 - one shared action that throws doesn't stop the rest of its chain")]
		public static void Fix3RunSharedCommand()
		{
			if (RAPI.GetLocalPlayer() == null || !Raft_Network.IsHost) { Fail("run in a world, as the host"); return; }
			bool ok = true;
			string title = "CI fix3 " + DateTime.Now.Ticks;
			try
			{
				// (no state: the signal throws, as a target gone does)
				var e = new IslandWorldState.Entry { Id = IslandNetwork.NewId(), Name = "ci_fix3_noisland", HostName = "ci_fix3_noisland", State = null };
				List<ObjAction> actions = ObjAction.ParseLines("signal||ci_fix3\njournal|" + title + "|The rest of the chain ran");
				// (by its first three parameters: the merge of 10-31 met a RunShared with more, optional ones - they get their defaults)
				MethodInfo run = typeof(Behaviours).GetMethods(BindingFlags.NonPublic | BindingFlags.Static).FirstOrDefault(m => m.Name == "RunShared" && m.GetParameters().Length >= 3 &&
					m.GetParameters()[0].ParameterType == typeof(IslandWorldState.Entry) && m.GetParameters()[1].ParameterType == typeof(int) && m.GetParameters()[2].ParameterType == typeof(List<ObjAction>) &&
					m.GetParameters().Skip(3).All(x => x.IsOptional));
				if (run == null) { Fail("fix3 run shared: Behaviours.RunShared not found"); return; }
				bool threw = false;
				try { run.Invoke(null, new object[] { e, 0, actions }.Concat(run.GetParameters().Skip(3).Select(x => x.DefaultValue)).ToArray()); } catch (Exception ex) { threw = true; Log("  threw: " + (ex.InnerException ?? ex).Message); }
				Check(ref ok, !threw, "a throwing action doesn't stop its caller");
				Check(ref ok, StoryBook.Pages.Any(p => p.Key.StartsWith("act:") && p.Title == title), "the journal action after it ran (its page is there)");
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally { foreach (StoryBook.Page p in StoryBook.Pages.Where(x => x.Title == title).ToList()) StoryBook.Pages.Remove(p); }
			if (ok) Log("PASS: fix3 run shared"); else Fail("fix3 run shared");
		}

		[ConsoleCommand(name: "CIFix3HostRefused", docs: "Dev, in game (host): fix 3 - the host arriving at an island whose once-claim another player holds gets its messages, not its items (before: nothing)")]
		public static void Fix3HostRefusedCommand()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || !Raft_Network.IsHost) { Fail("run in a world, as the host"); return; }
			bool ok = true;
			string name = "ci_fix3_refused_" + DateTime.Now.Ticks;
			string text = "CI fix3 hello " + DateTime.Now.Ticks;
			PlayerInventory inv = player.Inventory;
			int planks = inv.GetItemCount("Plank");
			try
			{
				var f = new IslandFile { Name = name, TerrainSize = new Vector3(10, 10, 10), HeightmapResolution = 33, Heights = new float[33, 33] };
				f.Props[BehaviourProps.EventKey("arrive")] = "give||Plank*1\nmessage||" + text;
				f.Save(IslandSpawner.PathFor(name));
				var e = new IslandWorldState.Entry { Id = IslandNetwork.NewId(), Name = name, HostName = name };
				int key = Behaviours.DoneBase + Behaviours.IslandIndex;
				Check(ref ok, Claims.HostGrant(e, key, 12345UL), "another player holds the island's arrive");
				Behaviours.Fire(e, -1, "arrive", true);
				Check(ref ok, Behaviours.LastMessage == text, "the host is refused: its message still shows (" + Behaviours.LastMessage + ")");
				Check(ref ok, inv.GetItemCount("Plank") == planks, "... but not its items (planks " + planks + " -> " + inv.GetItemCount("Plank") + ")");
				Check(ref ok, !e.State.ContainsKey(key), "... and it isn't marked done here");
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally
			{
				foreach (StoryBook.Page p in StoryBook.Pages.Where(x => (x.Text ?? "").Contains(text)).ToList()) StoryBook.Pages.Remove(p);
				try { File.Delete(IslandSpawner.PathFor(name)); } catch { }
				int now = inv.GetItemCount("Plank");
				if (now > planks) inv.RemoveItem("Plank", now - planks);
			}
			if (ok) Log("PASS: fix3 host refused"); else Fail("fix3 host refused");
		}

		[ConsoleCommand(name: "CIFix3WorldsUsing", docs: "Dev: fix 3 - a world only in Raft's world folders counts as using its islands (no copy of the old version was kept for it)")]
		public static void Fix3WorldsUsingCommand()
		{
			bool ok = true;
			string folder = null;
			try
			{
				string raftWorlds = SaveAndLoad.WorldPath;
				if (string.IsNullOrEmpty(raftWorlds) || !Directory.Exists(raftWorlds)) { Fail("fix3 worlds using: Raft's world folder isn't there"); return; }
				string world = "CI_fix3_onlyraft_" + DateTime.Now.Ticks;
				folder = Path.Combine(raftWorlds, "zz_ci_fix3_" + DateTime.Now.Ticks);
				Directory.CreateDirectory(folder);
				File.WriteAllLines(Path.Combine(folder, WorldCopy.FileName), new[] { "# Custom islands in world '" + world + "': name|x|y|z", "@auto=on", "ci_fix3_wu|0|0|0||||abcdefabcdef" });
				List<string> using_ = LibraryPack.WorldsUsing("ci_fix3_wu");
				Check(ref ok, using_.Contains(world), "the world only in Raft's folder uses the island (" + string.Join(", ", using_.ToArray()) + ")");
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally { try { if (folder != null) Directory.Delete(folder, true); } catch { } }
			if (ok) Log("PASS: fix3 worlds using"); else Fail("fix3 worlds using");
		}

		[ConsoleCommand(name: "CIFix3LocalFile", docs: "Dev: fix 3 - a world line's name_hash copy is played only when its content has that hash (a damaged copy played as nothing)")]
		public static void Fix3LocalFileCommand()
		{
			bool ok = true;
			string name = "ci_fix3_lf_" + DateTime.Now.Ticks, tmp = name + "_tmp";
			const string badHash = "abcdefabcdef";
			var cleanup = new List<string> { IslandSpawner.PathFor(name), IslandSpawner.PathFor(tmp), IslandSpawner.PathFor(IslandNetwork.DownloadName(name, badHash)) };
			bool wantedBefore = IslandNetwork.Wanted.Contains(badHash);
			try
			{
				Func<int, IslandFile> file = n =>
				{
					var f = new IslandFile { Name = name, TerrainSize = new Vector3(10, 10, 10), HeightmapResolution = 33, Heights = new float[33, 33] };
					f.Props["ci.version"] = n.ToString();
					return f;
				};
				file(1).Save(IslandSpawner.PathFor(name));
				// A damaged copy: its name says one version, its bytes are cut short
				File.WriteAllBytes(IslandSpawner.PathFor(IslandNetwork.DownloadName(name, badHash)), new byte[] { 1, 2, 3, 4, 5 });
				string got = WorldCopy.LocalFileFor(name, badHash);
				Check(ref ok, got == name, "a damaged copy isn't played: this PC's own is (" + got + ")");
				// A good copy of another version
				file(2).Save(IslandSpawner.PathFor(tmp));
				string h2 = IslandNetwork.HashOf(tmp);
				string copy = IslandNetwork.DownloadName(name, h2);
				cleanup.Add(IslandSpawner.PathFor(copy));
				File.Move(IslandSpawner.PathFor(tmp), IslandSpawner.PathFor(copy));
				got = WorldCopy.LocalFileFor(name, h2);
				Check(ref ok, h2 != IslandNetwork.HashOf(name) && got == copy, "a good copy is played by its hash (" + got + ")");
			}
			catch (Exception ex) { Check(ref ok, false, "threw " + ex); }
			finally
			{
				foreach (string path in cleanup) try { if (File.Exists(path)) File.Delete(path); } catch { }
				// (the host asked the players for the bad hash: forgotten again)
				if (!wantedBefore)
					try
					{
						const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
						var wanted = typeof(IslandNetwork).GetField("wanted", flags).GetValue(null) as Dictionary<string, string>;
						var requested = typeof(IslandNetwork).GetField("requested", flags).GetValue(null) as HashSet<string>;
						if (wanted != null) wanted.Remove(badHash);
						if (requested != null) requested.Remove(badHash);
					}
					catch { }
			}
			if (ok) Log("PASS: fix3 local file"); else Fail("fix3 local file");
		}
	}
}
