using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		/// <summary>The features of Raft the library's islands use (the user, 2026-10-05: every Raft feature on at least one island).</summary>
		static readonly KeyValuePair<string, Func<IslandObject, bool>>[] RaftFeatures =
		{
			new KeyValuePair<string, Func<IslandObject, bool>>("zipline", o => o.Name.StartsWith("ZiplinePath", StringComparison.Ordinal)),
			new KeyValuePair<string, Func<IslandObject, bool>>("machete vines", o => o.Name == ContentCatalog.MacheteVines),
			new KeyValuePair<string, Func<IslandObject, bool>>("buried treasure", o => o.Name == ContentCatalog.BuriedTreasure),
			new KeyValuePair<string, Func<IslandObject, bool>>("dirt", o => o.Name == "Pickup_Landmark_DirtPickup"),
			new KeyValuePair<string, Func<IslandObject, bool>>("wild beehive", o => o.Name == ContentCatalog.WildHive),
		};

		[ConsoleCommand(name: "CIFeatureCheck", docs: "Dev, in game (host): spawns saved islands by name and checks their Raft features work there - zipline ends on the ground, vines with their hidden chest, buried treasure made, dirt, wild hives. CIFeatureCheck <island>[,<island>...]")]
		public static void FeatureCheckCommand(string[] args)
		{
			string[] names = string.Join(" ", args ?? new string[0]).Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToArray();
			DynamicIslands.instance.StartCoroutine(FeatureCheckRoutine(names));
		}

		static IEnumerator FeatureCheckRoutine(string[] names)
		{
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (!raft.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			bool ok = true;
			Check(ref ok, new[] { ContentCatalog.MacheteItem, ContentCatalog.ZiplineItem, "MetalDetector", "Shovel" }.All(ContentCatalog.ItemExists), "Raft's tools exist: machete, zipline tool, metal detector, shovel");
			TreasurePointManager tm = UnityEngine.Object.FindObjectOfType<TreasurePointManager>();
			FieldInfo pa = typeof(MeshPath_Zipline_Landmark).GetField("pointA", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			foreach (string name in names)
			{
				IslandFile f = IslandFile.Load(IslandSpawner.PathFor(name));
				if (f == null) { Check(ref ok, false, "'" + name + "': no saved island"); continue; }
				string found = string.Join(", ", RaftFeatures.Where(ft => f.Objects.Any(ft.Value)).Select(ft => ft.Key + " " + f.Objects.Count(ft.Value)).ToArray());
				IslandWorldState.Remove(name);
				Vector3? spot = CustomIslandSpawner.FindClearSpot(raft.Value, CustomIslandSpawner.LandRadius(name), 900f);
				if (!spot.HasValue) { Check(ref ok, false, "'" + name + "': no open sea"); continue; }
				yield return DynamicIslands.instance.SpawnIslandFile(name, spot.Value, true);
				IslandWorldState.Entry e = IslandWorldState.Islands.LastOrDefault(i => i.HostName == name);
				if (e == null || e.Root == null) { Check(ref ok, false, "'" + name + "' did not spawn"); continue; }
				yield return new WaitForSeconds(1.5f);
				var said = new List<string>();
				// Ziplines: both floors on the ground (or a deck), the line made
				foreach (MeshPath_Zipline_Landmark line in e.Root.GetComponentsInChildren<MeshPath_Zipline_Landmark>(true))
				{
					Transform a = pa.GetValue(line) as Transform;
					var floors = line.transform.root.GetComponentsInChildren<Transform>(true).Where(t => t.IsChildOf(line.transform) && t.name.IndexOf("ZiplineBase", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
					foreach (Transform fl in floors)
					{
						RaycastHit hit;
						float gap = Physics.Raycast(fl.position + Vector3.up * 0.5f, Vector3.down, out hit, 30f, LayerMasks.MASK_GroundMask, QueryTriggerInteraction.Ignore) ? hit.distance - 0.5f : 99f;
						Check(ref ok, gap < 1.2f && gap > -1.5f, "'" + name + "': zipline floor " + fl.name + " on the ground (" + gap.ToString("F2") + " m over it)");
					}
					said.Add("zipline " + (a != null ? Vector3.Distance(line.transform.position, a.position).ToString("F0") + " m" : "?"));
				}
				// Vines: each with a chest it shows
				foreach (IslandObjectRef v in e.Root.GetComponentsInChildren<IslandObjectRef>(true).Where(r => r.ObjectName == ContentCatalog.MacheteVines || (r.Props != null && ObjectProps.Get(r.Props, BehaviourProps.Use).Contains("machete"))))
				{
					string onUse = ObjectProps.Get(v.Props, BehaviourProps.EventKey("use"));
					string target = onUse.Split(new[] { "\\n", "\n" }, StringSplitOptions.None).Where(l => l.StartsWith("show|")).Select(l => l.Substring(5)).FirstOrDefault();
					IslandObjectRef chest = target != null ? ScObjOf(e, target) : null;
					Check(ref ok, ObjectProps.Get(v.Props, BehaviourProps.CheckKey("use")).Contains(ContentCatalog.MacheteItem) && (target == null || (chest != null && !chest.gameObject.activeInHierarchy)),
						"'" + name + "': vines need the machete" + (target != null ? ", their chest '" + target + "' hidden until cut (" + (chest == null ? "missing" : chest.gameObject.activeInHierarchy ? "shown!" : "hidden") + ")" : ""));
				}
				int buried = e.Root.GetComponentsInChildren<BuriedTreasure>(true).Length;
				if (buried > 0) Check(ref ok, tm != null && BuriedTreasure.PointsOf(tm, e.Root.transform).Count == buried, "'" + name + "': " + buried + " buried treasure(s) made as Raft's treasure points");
				Log("  '" + name + "': " + (found.Length > 0 ? found : "no Raft features") + (said.Count > 0 ? " (" + string.Join(", ", said.ToArray()) + ")" : ""));
				IslandWorldState.Remove(name);
				yield return new WaitForSeconds(0.5f);
			}
			if (ok) Log("PASS: feature check"); else Fail("feature check");
		}

		[ConsoleCommand(name: "CIFeatureCoverage", docs: "Dev, anywhere: every Raft feature (zipline, machete vines, buried treasure, dirt, wild beehive) is on at least one of the library's islands (installed island files): the table")]
		public static void FeatureCoverageCommand(string[] args)
		{
			bool ok = true;
			// (one file at a time: all of them at once ran out of memory)
			var on = RaftFeatures.ToDictionary(ft => ft.Key, ft => new List<string>());
			foreach (string path in System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(IslandSpawner.PathFor("x")), "*" + IslandFile.Extension))
			{
				IslandFile f = IslandFile.Load(path);
				if (f == null) continue;
				foreach (var ft in RaftFeatures) if (f.Objects.Any(ft.Value)) on[ft.Key].Add(f.Name);
				f = null;
				GC.Collect();
			}
			foreach (var ft in RaftFeatures)
			{
				string[] names = on[ft.Key].Distinct().OrderBy(n => n).ToArray();
				Check(ref ok, names.Length > 0, ft.Key + ": " + names.Length + " island(s) - " + string.Join(", ", names.Take(12).ToArray()));
			}
			if (ok) Log("PASS: feature coverage"); else Fail("feature coverage");
		}
	}
}
