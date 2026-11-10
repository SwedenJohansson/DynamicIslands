using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// An island note or action unlocks one of Raft's Receiver frequencies (ROADMAP QE8): the behaviour action
	/// "frequency|&lt;story island&gt;" (a note's "read" event holds one when the note editor's "Unlocks" list is set). The host
	/// unlocks Raft's own notebook note for that island (NoteBook.UnlockSpecificNoteNetworked): Raft sends it to every
	/// player, saves it with the world and replays it on load, and the story order and a plan's story chain see it as
	/// they see a note found on Raft's islands - so Raft's own frequencies keep working as before.
	/// </summary>
	public static class ReceiverUnlock
	{
		public const string Verb = "frequency";

		/// <summary>The story islands an action can name, in Raft's order (Radio Tower .. Utopia).</summary>
		public static IEnumerable<ChunkPointType> Stops { get { return StoryOrder.Chain; } }

		/// <summary>The notebook note that unlocks this island in this world (the story order's mapping undone), or -1.</summary>
		public static int NoteFor(ChunkPointType wanted)
		{
			Dictionary<int, ChunkPointType> notes = StoryOrder.FrequencyNotes();
			if (notes.Count == 0) return -1;
			// (with the story order on, the note of Raft's island t unlocks Map(t): pick the one that lands on the wanted island)
			foreach (var kv in notes.OrderBy(k => k.Key))
				if ((StoryOrder.Active ? StoryOrder.Map(kv.Value) : kv.Value) == wanted) return kv.Key;
			foreach (var kv in notes.OrderBy(k => k.Key)) if (kv.Value == wanted) return kv.Key;
			return -1;
		}

		public static bool IsUnlocked(ChunkPointType t)
		{
			try { return NoteBook.unlockedChunkPointType != null && NoteBook.unlockedChunkPointType.Contains(t); }
			catch { return false; }
		}

		public static string Describe(ObjAction a)
		{
			ChunkPointType t = StoryOrder.Parse(a.Target);
			return t == ChunkPointType.None ? "unlock a Receiver frequency (no story island named: '" + a.Target + "')"
				: "unlock the Receiver frequency of " + StoryOrder.Name(t) + " (Raft's note, for every player)";
		}

		/// <summary>Host (a shared action): unlocks the island's note for everyone; nothing when it is already unlocked.</summary>
		public static void HostRun(IslandWorldState.Entry e, ObjAction a)
		{
			ChunkPointType t = StoryOrder.Parse(a.Target);
			if (t == ChunkPointType.None) { Debug.LogWarning("[CUSTOM ISLANDS] frequency: '" + a.Target + "' is not a story island"); return; }
			int note = NoteFor(t);
			if (note < 0) { Debug.LogWarning("[CUSTOM ISLANDS] frequency: no notebook note for " + t); return; }
			if (NoteBook.unlockedNoteBookIndexes != null && NoteBook.unlockedNoteBookIndexes.Contains(note)) return;
			NoteBook book = ComponentManager<NoteBook>.Value;
			if (book == null) { Debug.LogWarning("[CUSTOM ISLANDS] frequency: no notebook"); return; }
			Debug.Log("[CUSTOM ISLANDS] frequency: " + (e != null ? e.HostName : "?") + " unlocks " + StoryOrder.Name(t) + " (note " + note + ")");
			book.UnlockSpecificNoteNetworked(note, true);
		}

		/// <summary>The drop list's choices: none, then the story islands.</summary>
		public static List<DropList.Option> Options(string none)
		{
			var list = new List<DropList.Option> { new DropList.Option("", none, "Reading it unlocks no frequency") };
			foreach (ChunkPointType t in Stops)
				list.Add(new DropList.Option(StoryOrder.Key(t), StoryOrder.Name(t), "The Receiver's frequency of " + StoryOrder.Name(t) + " (Raft's note for it, for every player)"));
			return list;
		}

		/// <summary>The frequency a note's "read" event unlocks, as its key (Vasagatan; "" = none).</summary>
		public static string OfNote(IDictionary<string, string> props)
		{
			ObjAction a = ObjAction.ParseLines(ObjectProps.Get(props, BehaviourProps.EventKey("read"))).FirstOrDefault(x => x.Verb == Verb);
			ChunkPointType t = a != null ? StoryOrder.Parse(a.Target) : ChunkPointType.None;
			return t != ChunkPointType.None ? StoryOrder.Key(t) : "";
		}

		/// <summary>The props with the note's "read" frequency set ("" = removed); its other actions stay.</summary>
		public static Dictionary<string, string> WithNoteFrequency(IDictionary<string, string> props, string stop)
		{
			string key = BehaviourProps.EventKey("read");
			List<ObjAction> list = ObjAction.ParseLines(ObjectProps.Get(props, key));
			list.RemoveAll(x => x.Verb == Verb);
			if (!string.IsNullOrEmpty(stop)) list.Add(new ObjAction { Verb = Verb, Target = stop, Arg = "" });
			return ObjectProps.With(props, key, list.Count > 0 ? ObjAction.ToLines(list) : null);
		}
	}
}
