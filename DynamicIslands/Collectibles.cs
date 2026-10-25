using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Hidden collectible sets (PF3, 2026-10-25): any object with a set name (collect.set; the editor's "Collectible
	/// (set piece)" has one) is a piece of that set. Picking one up is one player's (Claims), counts for the whole crew
	/// (KeyBase + object in the island's state: saved, sent to everyone) and shows "Relics: 2 of 4". When the island's
	/// last piece of a set is found, the host sends the signal "set:name" and runs every piece's "set" event (the reward).
	/// The check "set|name|N" and the quest step "Find N of set" count the pieces found on that island.
	/// </summary>
	public static class Collectibles
	{
		public const string ObjectName = "Story_Collectible", SetProp = "collect.set", QuestType = "set", CheckKind = "set", SetEvent = "set";
		/// <summary>A piece picked up: KeyBase + object (after Islanders.TalkedBase 0x130000 + 0xFFFF).</summary>
		public const int KeyBase = 0x140000;
		public const string Hint = "a hidden piece of a collectible set: players pick it up, the HUD counts \"2 of 4\", the whole set runs the pieces' \"set\" event";

		public static bool Is(string name) { return name == ObjectName; }

		/// <summary>The set an object belongs to ("" = none).</summary>
		public static string SetOf(IDictionary<string, string> p) { return ObjectProps.Get(p, SetProp).Trim(); }
		public static bool IsPiece(IDictionary<string, string> p) { return SetOf(p).Length > 0; }

		/// <summary>The signal a finished set sends (world plans and checks can wait for it).</summary>
		public static string SignalOf(string set) { return "set:" + (set ?? "").Trim().ToLowerInvariant(); }

		public static void Defaults(string name, Dictionary<string, string> props)
		{
			if (!Is(name)) return;
			props[SetProp] = "Relics";
			props[BehaviourProps.Use] = "Pick up the relic";
			props[BehaviourProps.EventKey("use")] = "hide|";
		}

		/// <summary>A piece's use lines as if the builder had written them: it is picked up (hidden for good) when used.</summary>
		public static IDictionary<string, string> Expand(string objectName, IDictionary<string, string> props)
		{
			if (props == null || !IsPiece(props)) return props;
			bool hides = ObjAction.ParseLines(ObjectProps.Get(props, "on.use")).Any(a => a.Verb == "hide" && (a.Target.Trim().Length == 0 || a.Target.Trim().Equals("self", StringComparison.OrdinalIgnoreCase)));
			bool label = ObjectProps.Get(props, BehaviourProps.Use).Length > 0;
			if (hides && label) return props;
			var d = new Dictionary<string, string>(props);
			if (!hides) { string use = ObjectProps.Get(props, "on.use"); d["on.use"] = "hide|" + (use.Length > 0 ? "\n" + use : ""); }
			if (!label) d[BehaviourProps.Use] = "Pick up";
			return d;
		}

		public static GameObject BuildMarker()
		{
			var root = new GameObject(ObjectName);
			root.transform.SetParent(PlaceableCatalog.Container.transform, false);
			Transform marker = new GameObject("Marker").transform;
			marker.SetParent(root.transform, false);
			Material gold = ContentCatalog.MarkerMaterial(new Color(0.95f, 0.75f, 0.25f));
			ContentCatalog.Part(PrimitiveType.Cylinder, "Coin", marker, new Vector3(0f, 0.25f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.35f, 0.03f, 0.35f), gold);
			ContentCatalog.Part(PrimitiveType.Sphere, "Gem", marker, new Vector3(0f, 0.25f, 0.035f), Quaternion.identity, Vector3.one * 0.09f, ContentCatalog.MarkerMaterial(new Color(0.3f, 0.8f, 0.9f)));
			ContentCatalog.AddLabel(root.transform, "Collectible", 0.6f);
			return root;
		}

		/// <summary>World: a piece's editor name tag goes.</summary>
		internal static void Attach(GameObject go, IslandObjectRef r)
		{
			foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
				if (t.name == ContentCatalog.MarkerOnly && t.GetComponent<TextMesh>() != null) t.gameObject.SetActive(false);
		}

		/// <summary>The key a piece's pick-up is claimed and kept under (-1: not a piece).</summary>
		internal static int ClaimKey(IslandObjectRef r) { return r != null && IsPiece(r.Props) ? KeyBase + r.Index : -1; }

		static readonly Dictionary<GameObject, List<IslandObjectRef>> pieces = new Dictionary<GameObject, List<IslandObjectRef>>();

		/// <summary>The island's pieces (all sets; cached per loaded island).</summary>
		internal static List<IslandObjectRef> PiecesOf(IslandWorldState.Entry e)
		{
			if (e == null || e.Root == null) return new List<IslandObjectRef>();
			List<IslandObjectRef> list;
			if (pieces.TryGetValue(e.Root, out list) && list.All(x => x != null)) return list;
			if (pieces.Count > 64) foreach (GameObject gone in pieces.Keys.Where(k => k == null).ToList()) pieces.Remove(gone);
			list = Behaviours.RefsOf(e).Where(x => IsPiece(x.Props)).ToList();
			pieces[e.Root] = list;
			return list;
		}

		static IEnumerable<IslandObjectRef> Of(IslandWorldState.Entry e, string set)
		{
			return PiecesOf(e).Where(x => string.Equals(SetOf(x.Props), (set ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
		}

		public static int Total(IslandWorldState.Entry e, string set) { return Of(e, set).Count(); }
		public static int Found(IslandWorldState.Entry e, string set) { return Of(e, set).Count(x => ContentState.IsUsed(e, KeyBase + x.Index)); }

		/// <summary>This player picked a piece up: "Relics: 2 of 4" (at once; the others see it from Tick).</summary>
		internal static void PickedUp(IslandWorldState.Entry e, IslandObjectRef r)
		{
			string set = SetOf(r.Props);
			ShowCount(e, set, Found(e, set));
		}

		/// <summary>The count each set of a loaded island was last seen at here (island id + set), to show its rises.</summary>
		static readonly Dictionary<string, int> seen = new Dictionary<string, int>();

		static string SeenKey(IslandWorldState.Entry e, string set) { return e.Id + ":" + (set ?? "").Trim().ToLowerInvariant(); }

		static void ShowCount(IslandWorldState.Entry e, string set, int found)
		{
			seen[SeenKey(e, set)] = found;
			int total = Total(e, set);
			LastShown = set + ": " + found + " of " + total + (found >= total ? " - the whole set!" : "");
			IslandInfo.ShowMessage(LastShown);
		}

		/// <summary>Every player: a piece another player picked up (its key came from the host) shows the count here too.
		/// A set first seen (the island loaded) is only noted.</summary>
		static void WatchCounts(IslandWorldState.Entry e, List<IslandObjectRef> all)
		{
			foreach (IGrouping<string, IslandObjectRef> g in all.GroupBy(x => SetOf(x.Props).ToLowerInvariant()))
			{
				int found = g.Count(x => ContentState.IsUsed(e, KeyBase + x.Index)), was;
				string key = SeenKey(e, g.Key);
				if (!seen.TryGetValue(key, out was)) { seen[key] = found; continue; }
				if (found > was) ShowCount(e, SetOf(g.First().Props), found);
				else if (found < was) seen[key] = found;
			}
		}

		/// <summary>The last count shown here (tests).</summary>
		public static string LastShown { get; private set; }

		/// <summary>The check "set|name|N": N pieces of the set found on this island (empty N = all of them).</summary>
		internal static bool Holds(IslandWorldState.Entry e, string set, string arg)
		{
			int need, total = Total(e, set);
			if (!int.TryParse((arg ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out need) || need <= 0) need = total;
			return need > 0 && Found(e, set) >= need;
		}

		static float nextTick;

		/// <summary>Every player: counts picked up elsewhere shown here. Host: a set whose pieces are all found sends its
		/// signal and runs its pieces' "set" event, once per world.</summary>
		internal static void Tick()
		{
			if (Time.time < nextTick) return;
			nextTick = Time.time + 0.5f;
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				if (e.Root == null) continue;
				List<IslandObjectRef> all = PiecesOf(e);
				if (all.Count == 0) continue;
				WatchCounts(e, all);
				if (!Raft_Network.IsHost) continue;
				foreach (IGrouping<string, IslandObjectRef> g in all.GroupBy(x => SetOf(x.Props).ToLowerInvariant()))
				{
					if (Behaviours.HasSignal(e, SignalOf(g.Key)) || !g.All(x => ContentState.IsUsed(e, KeyBase + x.Index))) continue;
					Behaviours.SendSignal(e, SignalOf(g.Key));
					foreach (IslandObjectRef x in g.ToList()) Behaviours.FireFromHost(e, x.Index, SetEvent);
				}
			}
		}
	}
}
