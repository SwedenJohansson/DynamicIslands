using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The world option "Story islands in a new order" (WorldOptions.StoryOrder). Raft has no fixed places for its story
	/// islands: one appears near the raft when the host's Receiver is tuned to a frequency that was unlocked, and each
	/// frequency is unlocked by a note - the Receiver's own (the first), then the one found on each island for the next
	/// (Radio Tower, Vasagatan, Balboa, Caravan Town, Tangaroa, Varuna Point, Temperance, Utopia). Every unlock goes through
	/// NoteBook.UnlockFrequency(type): with the option on, every machine turns the chain's n-th island into the new order's
	/// n-th (from the world's seed; Utopia, the ending, stays last), so the Receiver shows, and the host brings, the islands
	/// in that order. The frequency numbers written on the notes follow (FrequencyTextMeshPro and the notebook's UI).
	/// Each island keeps its own content and needs nothing from another (its keys and parts lie on it), so any order can
	/// be finished. The unlocked list is rebuilt from the notes found whenever the options change (a client gets the
	/// host's after its notebook loaded, and a save's notes are read again on load).
	/// </summary>
	public static class StoryOrder
	{
		/// <summary>Raft's order.</summary>
		public static readonly ChunkPointType[] Chain =
		{
			ChunkPointType.Landmark_RadioTower, ChunkPointType.Landmark_Vasagatan, ChunkPointType.Landmark_Balboa, ChunkPointType.Landmark_CaravanIsland,
			ChunkPointType.Landmark_Tangaroa, ChunkPointType.Landmark_VarunaPoint, ChunkPointType.Landmark_Temperance, ChunkPointType.Landmark_Utopia,
		};

		static int orderSeed = int.MinValue;
		/// <summary>Whether the unlocked list holds this world's order now (it is put back to Raft's when the option goes off).</summary>
		static bool mapped;

		/// <summary>A world is loading: nothing of the last world's order.</summary>
		internal static void Reset() { mapped = false; }
		static ChunkPointType[] order;

		static void Log(string msg) { Debug.Log("[CUSTOM ISLANDS] [story order] " + msg); }

		public static bool Active { get { return WorldOptions.On(WorldOptions.StoryOrder) && WorldOptions.Seed != 0; } }

		/// <summary>The order a seed gives: Raft's first seven shuffled (never Raft's own order), Utopia last.</summary>
		public static ChunkPointType[] OrderFor(int seed)
		{
			var o = Chain.Take(Chain.Length - 1).ToArray();
			var rnd = new System.Random(seed ^ 0x57A2);
			for (int i = o.Length - 1; i > 0; i--) { int j = rnd.Next(i + 1); ChunkPointType t = o[i]; o[i] = o[j]; o[j] = t; }
			if (o.SequenceEqual(Chain.Take(o.Length))) { ChunkPointType first = o[0]; Array.Copy(o, 1, o, 0, o.Length - 1); o[o.Length - 1] = first; }
			return o.Concat(new[] { Chain[Chain.Length - 1] }).ToArray();
		}

		/// <summary>This world's order (Raft's when the option is off).</summary>
		public static ChunkPointType[] Order
		{
			get
			{
				if (!Active) return Chain;
				if (order == null || orderSeed != WorldOptions.Seed) { order = OrderFor(WorldOptions.Seed); orderSeed = WorldOptions.Seed; }
				return order;
			}
		}

		/// <summary>The island that comes where Raft's would (the n-th of the chain becomes the n-th of the order).</summary>
		public static ChunkPointType Map(ChunkPointType t)
		{
			int i = Array.IndexOf(Chain, t);
			return i < 0 ? t : Order[i];
		}

		public static string Describe(ChunkPointType[] o) { return string.Join(" > ", o.Select(Name).ToArray()); }

		public static string Name(ChunkPointType t)
		{
			switch (t)
			{
				case ChunkPointType.Landmark_RadioTower: return "Radio Tower";
				case ChunkPointType.Landmark_CaravanIsland: return "Caravan Town";
				case ChunkPointType.Landmark_VarunaPoint: return "Varuna Point";
				default: return t.ToString().Replace("Landmark_", "");
			}
		}

		#region The notebook's unlocked frequencies

		/// <summary>Raft's notes that unlock a frequency: note index -> the island (as Raft has it).</summary>
		public static Dictionary<int, ChunkPointType> FrequencyNotes()
		{
			var map = new Dictionary<int, ChunkPointType>();
			foreach (NoteBookUI ui in Resources.FindObjectsOfTypeAll<NoteBookUI>())
			{
				if (ui == null) continue;
				foreach (FieldInfo f in typeof(NoteBookUI).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				{
					var list = f.GetValue(ui) as IEnumerable<NoteBookNote>;
					if (list == null) continue;
					foreach (NoteBookNote n in list)
						if (n != null && n.isFrequencyThumbnail && Array.IndexOf(Chain, n.thumbNailLandmarkType) >= 0) map[n.noteIndex] = n.thumbNailLandmarkType;
				}
			}
			return map;
		}

		/// <summary>
		/// Every machine, when the options change or a save's notes were read: the story islands unlocked are the ones the
		/// notes found unlock, in this world's order.
		/// </summary>
		public static void Rebuild()
		{
			try
			{
				List<ChunkPointType> unlocked = NoteBook.unlockedChunkPointType;
				if (unlocked == null || NoteBook.unlockedNoteBookIndexes == null) return;
				// (a world that never had the order is left exactly as Raft made it; one that had it goes back to Raft's islands)
				if (!Active && !mapped) return;
				Dictionary<int, ChunkPointType> notes = FrequencyNotes();
				if (notes.Count == 0) return; // (no notebook yet: its own load unlocks them through the patch)
				mapped = Active;
				unlocked.RemoveAll(t => Array.IndexOf(Chain, t) >= 0);
				foreach (int idx in NoteBook.unlockedNoteBookIndexes)
				{
					ChunkPointType t;
					if (notes.TryGetValue(idx, out t) && !unlocked.Contains(Map(t))) unlocked.Add(Map(t));
				}
				RelabelAll();
				if (Active) Log("Order " + Describe(Order) + "; unlocked: " + string.Join(", ", unlocked.Where(t => Array.IndexOf(Chain, t) >= 0).Select(Name).ToArray()));
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [story order] " + e); }
		}

		#endregion

		#region The frequency numbers on notes

		// Each frequency text Raft set up, and the island it names (as Raft has it)
		static readonly List<KeyValuePair<Component, ChunkPointType>> labels = new List<KeyValuePair<Component, ChunkPointType>>();

		internal static void Register(Component c, ChunkPointType original)
		{
			labels.RemoveAll(l => l.Key == null);
			labels.Add(new KeyValuePair<Component, ChunkPointType>(c, original));
			Relabel(c, original);
		}

		/// <summary>The number shown for this island: its place in the chain's frequency now belongs to the order's island.</summary>
		public static string FrequencyText(ChunkPointType original)
		{
			ChunkPointType t = Map(original);
			RecieverFrequency f = RecieverFrequency.AllFrequencies != null ? RecieverFrequency.AllFrequencies.FirstOrDefault(x => x != null && x.chunkPointType == t) : null;
			return f != null ? f.ToString() : null;
		}

		static void Relabel(Component c, ChunkPointType original)
		{
			string text = FrequencyText(original);
			if (text == null || c == null) return;
			object tmp = Traverse.Create(c).Field("frequencyText").GetValue(); // (a TextMeshPro text: set through its "text" property)
			if (tmp != null) Traverse.Create(tmp).Property("text").SetValue(text);
		}

		static void RelabelAll() { foreach (var l in labels.ToList()) if (l.Key != null) Relabel(l.Key, l.Value); }

		#endregion
	}

	/// <summary>A frequency is unlocked (a note found, on every machine): in this world's order.</summary>
	[HarmonyPatch(typeof(NoteBook), "UnlockFrequency")]
	static class StoryOrderUnlock
	{
		static void Prefix(ref ChunkPointType chunkPointType)
		{
			if (StoryOrder.Active) chunkPointType = StoryOrder.Map(chunkPointType);
		}
	}


	/// <summary>Raft's notebook read its notes (a save loaded, a player joined): the story islands they unlock, in this world's order.</summary>
	[HarmonyPatch(typeof(NoteBookUI), "UpdateAllNotesVisibility")]
	static class StoryOrderNotesRead
	{
		static void Postfix() { StoryOrder.Rebuild(); }
	}
	[HarmonyPatch(typeof(FrequencyTextMeshPro), "Start")]
	static class StoryOrderLabel
	{
		static void Postfix(FrequencyTextMeshPro __instance, ChunkPointType ___frequencyType) { StoryOrder.Register(__instance, ___frequencyType); }
	}

	[HarmonyPatch(typeof(FrequencyTextMeshProUI), "Start")]
	static class StoryOrderLabelUI
	{
		static void Postfix(FrequencyTextMeshProUI __instance) { StoryOrder.Register(__instance, __instance.frequencyType); }
	}
}
