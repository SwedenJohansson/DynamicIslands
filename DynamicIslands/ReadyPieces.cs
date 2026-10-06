using System.Collections.Generic;
using System.Linq;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Raft's story-island doors, hatches, crank wheels and levers as ready pieces (TODO "Raft's quest items used in the
	/// plans" / ROADMAP LM12): placed from the object list they already work in a world, with Raft's own quest item as the
	/// key where Raft used one - a keycard door opens (it goes) for a player whose crew holds Raft's keycard (the story
	/// item raft-&lt;type&gt; a quest item pickup gives, kept for later doors: a "has" check, not "take"); a hatch opens when
	/// used; a crank wheel or a lever sends the signal "crank" or "lever" that the island's other objects, its quest and
	/// the world plan can wait for. Every setting can be changed in the behaviour window like any other.
	/// </summary>
	public static class ReadyPieces
	{
		public class Piece
		{
			public string Name, Kind, Item, Use, Locked;
		}

		public const string Door = "door", Hatch = "hatch", Crank = "crank", Lever = "lever";

		public static readonly Piece[] All =
		{
			new Piece { Name = "PlantationDoor", Kind = Door, Item = "Tangaroa_KeyCard", Use = "Swipe the keycard", Locked = "A keycard reader blinks red. It wants a Tangaroa keycard." },
			new Piece { Name = "TP_Selene_Int_DoorFrame_WithDoorReactorKey", Kind = Door, Item = "Temperance_ReactorKey", Use = "Turn the reactor key", Locked = "The door needs the reactor key." },
			new Piece { Name = "UT_MainEntranceDoor", Kind = Door, Item = "Utopia_KeyEntrance", Use = "Unlock the entrance", Locked = "The entrance is locked. Somewhere there is a key for it." },
			new Piece { Name = "UT_Hut_LockedDoor_DoorL", Kind = Door, Item = "Utopia_KeyPhase1", Use = "Unlock the door", Locked = "Locked. It needs a key." },
			new Piece { Name = "UT_Hut_LockedDoor_DoorR", Kind = Door, Item = "Utopia_KeyPhase1", Use = "Unlock the door", Locked = "Locked. It needs a key." },
			new Piece { Name = "UT_Skyscraber01PadlockDoor", Kind = Door, Item = "Vasagatan_BoltCutter", Use = "Cut the padlock", Locked = "A heavy padlock. Bolt cutters would get through it." },
			new Piece { Name = "UT_DettoDoor", Kind = Door, Item = "Utopia_DettoCode", Use = "Enter the code", Locked = "A code lock. Someone must have written the code down." },
			new Piece { Name = "VP_GarageDoor", Kind = Door, Item = "Varuna_MotherlodeKey", Use = "Turn the key", Locked = "The garage door's keyhole is empty. It needs a key." },
			new Piece { Name = "TangaroaHatchRoom_Hatch", Kind = Hatch, Use = "Open the hatch" },
			new Piece { Name = "Hatch", Kind = Hatch, Use = "Open the hatch" },
			new Piece { Name = "TP_CoolingStation_Monitor_Hatch01", Kind = Hatch, Use = "Open the hatch" },
			new Piece { Name = "TP_CoolingStation_Monitor_Hatch02", Kind = Hatch, Use = "Open the hatch" },
			new Piece { Name = "TP_Selene_DoorCrankWheel", Kind = Crank, Use = "Turn the crank wheel" },
			new Piece { Name = "UT_DrawBridgeMechanism_Cogwheel01", Kind = Crank, Use = "Turn the cogwheel" },
			new Piece { Name = "TP_CoolingStation_Monitor_Lever", Kind = Lever, Use = "Pull the lever" },
			new Piece { Name = "TP_LaserControlPanel_lever", Kind = Lever, Use = "Pull the lever" },
			new Piece { Name = "TradingPost_Register_Lever", Kind = Lever, Use = "Pull the lever" },
		};

		public static Piece Of(string name) { return All.FirstOrDefault(p => p.Name == name); }

		/// <summary>The piece's working settings (ObjectProps.Defaults).</summary>
		public static void Defaults(string name, Dictionary<string, string> props)
		{
			Piece p = Of(name);
			if (p == null) return;
			props[BehaviourProps.Use] = p.Use;
			switch (p.Kind)
			{
				case Door:
					// (it goes when opened: Raft's doors swing on hinges this piece doesn't have)
					props[BehaviourProps.CheckKey("use")] = "has|" + StoryItems.Prefix + QuestItemPickups.IdPrefix + p.Item + "|1";
					props[BehaviourProps.ElseKey("use")] = "message||" + p.Locked;
					props[BehaviourProps.EventKey("use")] = "hide|";
					break;
				case Hatch:
					props[BehaviourProps.EventKey("use")] = "hide|";
					break;
				case Crank:
					props[BehaviourProps.EventKey("use")] = "message||The wheel turns with a long groan." + "\n" + "signal||crank";
					break;
				case Lever:
					props[BehaviourProps.EventKey("use")] = "message||Clunk. Something moved." + "\n" + "signal||lever";
					break;
			}
		}

		/// <summary>The story item a piece wants (raft-&lt;type&gt;), or null.</summary>
		public static string ItemOf(string name) { Piece p = Of(name); return p != null && p.Item != null ? QuestItemPickups.IdPrefix + p.Item : null; }
	}
}
