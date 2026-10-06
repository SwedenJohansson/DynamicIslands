using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// "Raft's features" (ROADMAP LM12, generator options): what Raft's story islands have the player do, put on a generated
	/// island - a cache behind vines for the machete, treasure buried for the metal detector and the shovel, a zipline from
	/// the island's top down to its beach, a strongbox behind a code panel (the code on a note elsewhere), a grove of wild
	/// beehives and patches of dirt for the shovel. How many (0-6), different ones first; an explorer's chest by the beach
	/// holds the tools they need (machete, detector, shovel, zipline tool), as players may not have them yet.
	/// </summary>
	public static class GenFeatures
	{
		public const int Max = 6;
		public static readonly string[] Kinds = { "vines", "treasure", "zipline", "code", "hives", "dirt" };

		public static readonly string[] Names = { ContentCatalog.MacheteVines, ContentCatalog.BuriedTreasure, "ZiplinePath_Landmark", ContentCatalog.WildHive,
			"Pickup_Landmark_DirtPickup", "Loot_Chest", "Loot_ChestLarge", "RT_PowerBox", "Note_Papers" };

		/// <summary>Puts s.Features features on the island; what was put (for the report).</summary>
		public static void Make(MapKit k, IslandGenSettings s, List<string> done)
		{
			int want = Mathf.Clamp(s.Features, 0, Max);
			if (want == 0) return;
			System.Random r = k.Rnd;
			List<string> order = Kinds.OrderBy(x => r.Next()).ToList();
			var made = new List<string>();
			var tools = new List<string>();
			for (int i = 0; i < want; i++)
			{
				string kind = order[i % order.Count];
				string what = One(k, s, kind, i, tools);
				if (what != null) made.Add(what);
			}
			// The tools, in a chest near the shore (where players land)
			if (tools.Count > 0)
			{
				Vector2? p = k.Find(k.Mid, s.Radius * 0.9f, (above, slope) => above > 0.6f && above < 4f && slope < 15f, 6f);
				if (p.HasValue) k.Chest("Loot_Chest", p.Value, "Explorer's chest", string.Join(";", tools.Distinct().Select(t => t + "*1").ToArray()) + ";Rope*3",
					"", 0f);
			}
			if (made.Count > 0) done.Add(string.Join(", ", made.ToArray()) + (made.Count < want ? " (" + (want - made.Count) + " more found no place)" : ""));
		}

		static string One(MapKit k, IslandGenSettings s, string kind, int n, List<string> tools)
		{
			System.Random r = k.Rnd;
			Vector2? dry = k.Find(k.Mid, s.Radius * 0.7f, MapKit.Dry, 10f);
			switch (kind)
			{
				case "vines":
				{
					if (!dry.HasValue) return null;
					string chest = "vinecache" + n;
					var cp = new Dictionary<string, string> { { ObjectProps.LootItems, MapKit.Loot("Treasure") }, { ObjectProps.NoteTitle, "Behind the vines" }, { BehaviourProps.Name, chest }, { BehaviourProps.Hidden, "1" } };
					k.Add("Loot_Chest", k.At(dry.Value), (float)r.NextDouble() * 360f, cp, 3f);
					Dictionary<string, string> vp = ObjectProps.Defaults(ContentCatalog.MacheteVines);
					vp[BehaviourProps.EventKey("use")] = "hide|\nshow|" + chest + "\nmessage||The machete bites through the vines. Something was hidden behind them.";
					k.Add(ContentCatalog.MacheteVines, k.At(dry.Value + new Vector2(1.6f, 0f)), (float)r.NextDouble() * 360f, vp, 0f);
					tools.Add(ContentCatalog.MacheteItem);
					return "a cache behind vines";
				}
				case "treasure":
				{
					int put = 0;
					for (int i = 0; i < 2; i++)
					{
						Vector2? p = k.Find(k.Mid, s.Radius * 0.75f, MapKit.Dry, 8f);
						if (!p.HasValue) continue;
						k.Add(ContentCatalog.BuriedTreasure, k.At(p.Value), 0f, new Dictionary<string, string> { { ObjectProps.TreasureKind, "0" } }, 0f);
						put++;
					}
					if (put == 0) return null;
					tools.Add("MetalDetector"); tools.Add("Shovel");
					return put + " buried treasure" + (put == 1 ? "" : "s");
				}
				case "zipline":
				{
					// (from the top down to the beach: at least 25 m long, falling at least 6 m)
					Vector2 top = k.Highest(k.Mid, s.Radius * 0.5f);
					float topY = k.Ground(top);
					Vector2? beach = null;
					for (int i = 0; i < 40 && beach == null; i++)
					{
						Vector2? p = k.Find(top, s.Radius * 1.1f, (above, slope) => above > 0.4f && above < 2.5f && slope < 18f, 0f);
						if (p.HasValue && (p.Value - top).magnitude > 25f && (p.Value - top).magnitude < 90f && topY - k.Ground(p.Value) > 6f) beach = p;
					}
					if (!beach.HasValue) return null;
					Vector3 far = k.At(beach.Value, 0.2f);
					var zp = new Dictionary<string, string> { { ZiplineEnds.ZipTo, ZiplineEnds.Text(far) }, { BehaviourProps.Name, "zipline" + n } };
					k.Add("ZiplinePath_Landmark", k.At(top, 3f), 0f, zp, 4f);
					tools.Add(ContentCatalog.ZiplineItem);
					return "a zipline down to the beach";
				}
				case "code":
				{
					Vector2? other = k.Find(k.Mid, s.Radius * 0.8f, MapKit.Dry, 15f);
					if (!dry.HasValue || !other.HasValue) return null;
					string code = (1000 + r.Next(9000)).ToString();
					string box = "strongbox" + n;
					var bp = new Dictionary<string, string> { { ObjectProps.LootItems, MapKit.Loot("Treasure") + ";" + MapKit.Loot("Metal").Split(';').First() }, { ObjectProps.NoteTitle, "Strongbox" }, { BehaviourProps.Name, box }, { BehaviourProps.Hidden, "1" } };
					k.Add("Loot_ChestLarge", k.At(dry.Value), (float)r.NextDouble() * 360f, bp, 3f);
					var pp = new Dictionary<string, string>
					{
						{ BehaviourProps.Use, "Enter the code" }, { CodeLock.Code, code },
						{ BehaviourProps.EventKey("use"), "show|" + box + "\nmessage||The panel beeps and a hatch in the ground slides open." },
					};
					k.Add("RT_PowerBox", k.At(dry.Value + new Vector2(0f, 1.8f)), (float)r.NextDouble() * 360f, pp, 0f);
					k.Note("Note_Papers", other.Value, "Scribbled numbers", "Don't forget it this time: " + code + ". The panel by the strongbox.");
					return "a strongbox behind a code panel (" + code + ")";
				}
				case "hives":
				{
					int put = 0;
					Vector2 grove = dry ?? k.Mid;
					for (int i = 0; i < 3; i++)
					{
						Vector2? p = k.Find(grove, 12f, MapKit.Dry, 3f);
						if (!p.HasValue) continue;
						var hp = new Dictionary<string, string> { { ObjectProps.LootItems, ContentCatalog.PresetLoot(new[] { ContentCatalog.WildHiveLoot }) }, { ObjectProps.NoteTitle, "Wild beehive" } };
						k.Add(ContentCatalog.WildHive, k.At(p.Value), (float)r.NextDouble() * 360f, hp, 2f);
						put++;
					}
					return put > 0 ? "a grove of " + put + " wild beehives" : null;
				}
				default:
				{
					int put = 0;
					for (int i = 0; i < 5; i++)
					{
						Vector2? p = k.Find(dry ?? k.Mid, 14f, MapKit.Dry, 2.5f);
						if (!p.HasValue) continue;
						k.Add("Pickup_Landmark_DirtPickup", k.At(p.Value), (float)r.NextDouble() * 360f, null, 1f);
						put++;
					}
					if (put == 0) return null;
					tools.Add("Shovel");
					return put + " dirt spots for the shovel";
				}
			}
		}
	}
}
