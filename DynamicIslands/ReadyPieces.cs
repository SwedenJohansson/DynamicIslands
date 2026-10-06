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
	/// More of Raft's story machinery (LM12, 2026-10-06): lifts that carry the player up and down when used (Tangaroa's
	/// elevator, Varuna Point's skylifts), cages cut open with Raft's bolt cutters, security cameras that sweep, generators
	/// that start with Raft's generator part and send "power", radios that work with power (a started generator or Raft's
	/// battery charger part) and send "radio", Vasagatan's engine that starts with Raft's gas tank and sends "engine", and
	/// Temperance's turning mirrors (a puzzle piece: each use turns it, sending "mirror").
	/// </summary>
	public static class ReadyPieces
	{
		public class Piece
		{
			public string Name, Kind, Item, Use, Locked;
		}

		public const string Door = "door", Hatch = "hatch", Crank = "crank", Lever = "lever";
		public const string Lift = "lift", Cage = "cage", Camera = "camera", Generator = "generator", Radio = "radio", Engine = "engine", Mirror = "mirror";
		/// <summary>The signals the new kinds send (a quest, the world plan or other objects can wait for them).</summary>
		public const string PowerSignal = "power", RadioSignal = "radio", EngineSignal = "engine", MirrorSignal = "mirror", CageSignal = "cage";

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
			// (LM12, 2026-10-06)
			new Piece { Name = "Elevator", Kind = Lift, Use = "Ride the lift" },
			new Piece { Name = "VP_Skylift", Kind = Lift, Use = "Ride the lift" },
			new Piece { Name = "VP_Skylift_Extended", Kind = Lift, Use = "Ride the lift" },
			new Piece { Name = "UT_DogCage_Medium02", Kind = Cage, Item = "Vasagatan_BoltCutter", Use = "Cut the cage open", Locked = "The cage is chained shut. Bolt cutters would get through the chain." },
			new Piece { Name = "RT_SharkCage", Kind = Cage, Item = "Vasagatan_BoltCutter", Use = "Cut the cage open", Locked = "The cage is chained shut. Bolt cutters would get through the chain." },
			new Piece { Name = "RT_Camera", Kind = Camera },
			new Piece { Name = "EmergencyGenerator", Kind = Generator, Item = "Tangaroa_GeneratorPart", Use = "Start the generator", Locked = "It won't start. A part is missing - a generator part." },
			new Piece { Name = "Generator", Kind = Generator, Item = "Tangaroa_GeneratorPart", Use = "Start the generator", Locked = "It won't start. A part is missing - a generator part." },
			new Piece { Name = "UT_Generator01", Kind = Generator, Item = "Tangaroa_GeneratorPart", Use = "Start the generator", Locked = "It won't start. A part is missing - a generator part." },
			new Piece { Name = "VG_DecorationPrefabBase_EmergencyGenerator Variant", Kind = Generator, Item = "Tangaroa_GeneratorPart", Use = "Start the generator", Locked = "It won't start. A part is missing - a generator part." },
			new Piece { Name = "RT_CommRadio", Kind = Radio, Item = "Caravan_BatteryChargerPart", Use = "Use the radio", Locked = "The radio is dead. It needs power - a running generator, or a battery." },
			new Piece { Name = "Model_Radio", Kind = Radio, Item = "Caravan_BatteryChargerPart", Use = "Use the radio", Locked = "The radio is dead. It needs power - a running generator, or a battery." },
			new Piece { Name = "Balboa_DecorationPrefabBase_RadioDevice", Kind = Radio, Item = "Caravan_BatteryChargerPart", Use = "Use the radio", Locked = "The radio is dead. It needs power - a running generator, or a battery." },
			new Piece { Name = "VG_DecorationPrefabBase_Engine Variant", Kind = Engine, Item = "Vasagatan_GasTank", Use = "Start the engine", Locked = "The tank is dry. It needs fuel - a gas tank." },
			new Piece { Name = "TP_MirrorHousing_Rotating", Kind = Mirror, Use = "Turn the mirror" },
			new Piece { Name = "TP_MirrorHousing_InteractableMirror", Kind = Mirror, Use = "Turn the mirror" },
		};

		/// <summary>The pieces added for LM12's rest (2026-10-06): lifts, cages, cameras, generators, radios, the engine, mirrors.</summary>
		public static IEnumerable<Piece> Machinery { get { return All.Where(p => p.Kind == Lift || p.Kind == Cage || p.Kind == Camera || p.Kind == Generator || p.Kind == Radio || p.Kind == Engine || p.Kind == Mirror); } }


		public static Piece Of(string name) { return All.FirstOrDefault(p => p.Name == name); }

		/// <summary>The piece's working settings (ObjectProps.Defaults).</summary>
		public static void Defaults(string name, Dictionary<string, string> props)
		{
			Piece p = Of(name);
			if (p == null) return;
			if (p.Use != null) props[BehaviourProps.Use] = p.Use;
			string item = p.Item != null ? StoryItems.Prefix + QuestItemPickups.IdPrefix + p.Item : null;
			switch (p.Kind)
			{
				case Lift:
					// (used, it goes up 6 m and down again on the next use, carrying the player standing on it)
					props[BehaviourProps.Move] = "0,6,0";
					props[BehaviourProps.MoveTime] = "5";
					props[BehaviourProps.Carry] = "1";
					break;
				case Cage:
					props[BehaviourProps.CheckKey("use")] = "has|" + item + "|1";
					props[BehaviourProps.ElseKey("use")] = "message||" + p.Locked;
					props[BehaviourProps.EventKey("use")] = "message||The chain snaps and the cage swings open." + "\n" + "hide|" + "\n" + "signal||" + CageSignal;
					break;
				case Camera:
					// (sweeps 70 degrees and back, for ever)
					props[BehaviourProps.Turn] = "70";
					props[BehaviourProps.MoveMode] = "loop";
					props[BehaviourProps.MoveTime] = "4";
					break;
				case Generator:
				case Engine:
					// (started once it runs for good: a later use finds the signal and runs again without wanting the part)
					string sig = p.Kind == Generator ? PowerSignal : EngineSignal;
					props[BehaviourProps.CheckKey("use")] = ObjCheck.AnyLine + "\nsignal|" + sig + "|\ntake|" + item + "|1";
					props[BehaviourProps.ElseKey("use")] = "message||" + p.Locked;
					props[BehaviourProps.EventKey("use")] = "message||" + (p.Kind == Generator ? "The generator coughs, then hums. The power is on." : "The engine sputters, then roars.") + "\n" + "signal||" + sig;
					break;
				case Radio:
					props[BehaviourProps.CheckKey("use")] = ObjCheck.AnyLine + "\nsignal|" + PowerSignal + "|\nhas|" + item + "|1";
					props[BehaviourProps.ElseKey("use")] = "message||" + p.Locked;
					props[BehaviourProps.EventKey("use")] = "message||Static crackles... then a voice, far away, repeating a frequency." + "\n" + "signal||" + RadioSignal;
					break;
				case Mirror:
					props[BehaviourProps.Turn] = "90";
					props[BehaviourProps.MoveTime] = "0.8";
					props[BehaviourProps.EventKey("use")] = "switch|" + "\n" + "signal||" + MirrorSignal;
					break;
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
