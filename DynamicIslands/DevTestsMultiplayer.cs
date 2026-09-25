using System;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Helpers for the two-player test (the second Raft runs in Sandboxie, see TEST_PROTOCOL.md §5).</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIAlive", docs: "Dev: fills up the local player's hunger, thirst, health and oxygen, and respawns them if they are down")]
		public static void AliveCommand()
		{
			DynamicIslands.instance.StartCoroutine(AliveRoutine());
		}

		static System.Collections.IEnumerator AliveRoutine()
		{
			yield return EnsureAlive();
			Log("Alive: the player is up and fed");
		}

		/// <summary>
		/// One line per piece of shared state, in a fixed order, so the host's and the second player's logs can be
		/// compared line by line: role, the shared clock, islands (id, name, hash, where), the used objects' state
		/// per island (quest steps included), and the story items and journal pages.
		/// </summary>
		[ConsoleCommand(name: "CIMPState", docs: "Dev, two-player test: logs a summary of the shared state (islands, object state, quests, story items, clock) to compare between host and client")]
		public static void MPStateCommand()
		{
			bool host = Raft_Network.IsHost;
			var net = ComponentManager<Raft_Network>.Value;
			int players = net != null && net.remoteUsers != null ? net.remoteUsers.Count : 0;
			Log("MP role: " + (host ? "host" : "client") + ", players " + players + (Sandboxed ? " (Sandboxie)" : ""));
			Log("MP clock: " + SharedClock.Now.ToString("F1") + (SharedClock.Synced ? " (synced)" : " (local)"));
			Log("MP islands: " + IslandWorldState.Islands.Count);
			foreach (var e in IslandWorldState.Islands.OrderBy(i => i.Id))
			{
				// (harvested trees and picked-up items reach the entry when the island unloads or the world saves: now)
				if (e.Root != null) IslandObjectState.Capture(e);
				Log("MP island " + e.Id + " " + e.HostName + " hash " + e.Hash + " at " + e.Position.ToString("F0") + (e.Label.Length > 0 ? " label '" + e.Label + "'" : "") +
					(e.WaitingForFile ? " (waiting for the file)" : e.Failed ? " (failed)" : e.Root == null ? " (unloaded)" : " (loaded)"));
				string state = string.Join(" ", e.State.OrderBy(kv => kv.Key)
					.Select(kv => kv.Key.ToString("X") + "=" + (kv.Value.Active ? "on" : "off") + "/" + kv.Value.Yield));
				Log("MP state " + e.Id + ": " + (state.Length > 0 ? state : "(none)"));
				IslandQuest q = QuestTracker.QuestOf(e);
				if (q.Steps.Count > 0) Log("MP quest " + e.Id + ": step " + QuestTracker.StepOf(e) + " of " + q.Steps.Count);
				if (e.Root != null)
				{
					// (Raft's own creatures: the host spawns them, Raft's network shows them to everyone)
					Vector3 at = e.Position;
					// (live ones: a body lingers longer on the host than on other players)
					var near = UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>().Where(a => a != null && (a.networkEntity == null || !a.networkEntity.IsDead) && Vector3.Distance(new Vector3(a.transform.position.x, 0f, a.transform.position.z), new Vector3(at.x, 0f, at.z)) < 300f).ToList();
					Log("MP creatures near " + e.Id + ": " + near.Count + (near.Count > 0 ? " (" + string.Join(", ", near.Select(a => a.name.Replace("(Clone)", "")).OrderBy(n => n).ToArray()) + ")" : ""));
				}
			}
			PlayerInventory inv = RAPI.GetLocalPlayer() != null ? RAPI.GetLocalPlayer().Inventory : null;
			// (per player, not shared: e.g. the planks a lever gave the player who pulled it)
			if (inv != null) Log("MP this player: " + string.Join(", ", new[] { "Plank", "Stone", "Rope", "Nail", "Scrap" }.Select(i => i + " x" + inv.GetItemCount(i)).ToArray()));
			Log("MP story items: " + string.Join(", ", StoryBook.Items.Select(h => h.Def.Id + " x" + h.Count).ToArray()));
			Log("MP story pages: " + string.Join(", ", StoryBook.Pages.Select(p => p.Key).ToArray()));
			Log("MP state done");
		}

		/// <summary>Empties the crew's story book here and, on the host, for every player (tests start and end clean).</summary>
		static void ResetStoryEverywhere()
		{
			StoryBook.Reset();
			if (Raft_Network.IsHost) IslandNetwork.SendStory(StoryBook.StateMessage());
		}

		/// <summary>
		/// The host spawns an island for the second player to play on, and keeps it: the sample island (trees and
		/// pickups) with a door and its lever, a chest with a story key, a vault the key opens, and a note.
		/// Its objects: door, lever (switches the door, gives 2 planks), vault (needs story:mp-key), note "MP note".
		/// </summary>
		[ConsoleCommand(name: "CIMPIsland", docs: "Dev, in game (host): spawns and keeps 'cimpworld', an island for the two-player test (door + lever, key chest + vault, a note, trees and pickups)")]
		public static void MPIslandCommand()
		{
			DynamicIslands.instance.StartCoroutine(MPIslandRoutine());
		}

		static System.Collections.IEnumerator MPIslandRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			IslandFile f = IslandFile.Load(IslandSpawner.PathFor("generated_sample"));
			f.Name = "cimpworld";
			f.Elevation = 0f;
			Vector2 c = IslandSpawner.LandCentre(f);
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1);
			Func<float, float, Vector3> ground = (x, z) => new Vector3(x, f.Heights[Mathf.RoundToInt(z / step), Mathf.RoundToInt(x / step)] * f.TerrainSize.y, z);
			f.Objects.RemoveAll(o => new Vector2(o.Position.x - c.x, o.Position.z - c.y).magnitude < 20f);
			f.Props[StoryItems.Key] = "mp-key|Crew key||A key for the two-player test.";
			f.Objects.Add(new IslandObject { Name = "Block_Wall_Thatch", Position = ground(c.x + 6f, c.y), Props = P(BehaviourProps.Name, "door", BehaviourProps.Turn, "90", BehaviourProps.MoveTime, "0.5") });
			f.Objects.Add(new IslandObject { Name = "Log", Position = ground(c.x + 3f, c.y + 3f), Props = P(BehaviourProps.Name, "lever", BehaviourProps.Use, "Pull the lever",
				BehaviourProps.EventKey("use"), "switch|door|\nmessage||The door moves\ngive||" + ContentCatalog.PresetLoot(new[] { "Plank*2" })) });
			f.Objects.Add(new IslandObject { Name = "Loot_ChestSmall", Position = ground(c.x - 4f, c.y - 3f), Props = P(ObjectProps.LootItems, "story:mp-key*1") });
			f.Objects.Add(new IslandObject { Name = "Block_Wall_Thatch", Position = ground(c.x + 6f, c.y + 6f), Props = P(BehaviourProps.Name, "vault", BehaviourProps.Turn, "90", BehaviourProps.MoveTime, "0.5",
				BehaviourProps.Use, "Open the vault", BehaviourProps.CheckKey("use"), "take|story:mp-key|1", BehaviourProps.ElseKey("use"), "message||It's locked.",
				BehaviourProps.EventKey("use"), "open||\nmessage||The key turns.\njournal|The vault|The crew opened the vault.") });
			f.Objects.Add(new IslandObject { Name = "Note_Paper", Position = ground(c.x - 2f, c.y + 4f), Props = P(ObjectProps.NoteTitle, "MP note", ObjectProps.NoteText, "Read by one player, in the crew's journal.") });
			f.Objects.Add(new IslandObject { Name = "Log", Position = ground(c.x - 6f, c.y) + Vector3.up, Props = P(BehaviourProps.Move, "0,2,0", BehaviourProps.MoveTime, "3", BehaviourProps.MoveMode, "loop",
				BehaviourProps.Spin, "40", BehaviourProps.Collision, "none", BehaviourProps.Name, "mover") });
			f.Objects.Add(new IslandObject { Name = ContentCatalog.TriggerZone, Position = ground(c.x, c.y - 8f), Props = P(ObjectProps.ZoneId, "mpzone", ObjectProps.ZoneRadius, "4",
				BehaviourProps.EventKey("enter"), "message||Zone entered\nsignal||mp-zone") });
			f.Objects.Add(new IslandObject { Name = "Creature_Boar", Position = ground(c.x + 10f, c.y + 8f), Props = P(ObjectProps.CreatureDamage, "0") });
			f.Props[IslandQuest.KeyTitle] = "Crew quest";
			f.Props[IslandQuest.KeySteps] = "reach|mpzone|1|\nread|MP note|1|";
			f.Save(IslandSpawner.PathFor("cimpworld"));
			Vector3? spot = CustomIslandSpawner.FindClearSpot(raftPos.Value, CustomIslandSpawner.LandRadius("cimpworld"), 390f);
			if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
			ResetStoryEverywhere();
			yield return DynamicIslands.instance.SpawnIslandFile("cimpworld", spot.Value, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.LastOrDefault(i => i.HostName == "cimpworld");
			if (e == null || e.Root == null) { Fail("cimpworld did not spawn"); yield break; }
			Log("PASS: cimpworld spawned (island " + e.Id + ") at " + spot.Value.ToString("F0") + ": " + e.Root.GetComponentsInChildren<HarvestableTree>().Length + " trees, " +
				e.Root.GetComponentsInChildren<PickupItem_Networked>().Count(p => p.GetComponent<HarvestableTree>() == null) + " pickups");
		}

		/// <summary>Where the "mover" log of cimpworld is now (both players at the same moment: the same pose).</summary>
		[ConsoleCommand(name: "CIMoverPose", docs: "Dev, in game: the pose of the object named 'mover' on an island and the shared clock: CIMoverPose <island>")]
		public static void MoverPoseCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(args);
			if (e == null) return;
			IslandObjectRef r = e.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(o => o.Name == "mover");
			if (r == null) { Fail("no 'mover' on '" + e.HostName + "'"); return; }
			Log("Mover at clock " + SharedClock.Now.ToString("F2") + ": local height " + (r.transform.position.y - e.Root.transform.position.y).ToString("F2") + ", turn " + r.transform.eulerAngles.y.ToString("F0"));
		}

		// What a player does, as either player (the client's goes through the network as a real player's would).
		// Each takes the island (part of its name) first, and puts the local player next to the object.

		static IslandWorldState.Entry LoadedIsland(string[] args)
		{
			string name = args != null && args.Length > 0 ? args[0] : "";
			IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(i => i.Root != null && i.HostName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
			if (e == null) Fail("no loaded island called '" + name + "' here");
			return e;
		}

		static void PutPlayerNear(Transform t, float offset = 1.5f)
		{
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || t == null) return;
			Vector3 target = t.position + new Vector3(offset, 0f, offset);
			RaycastHit hit;
			if (Physics.Raycast(target + Vector3.up * 30f, Vector3.down, out hit, 60f, 1 << IslandSpawner.TerrainLayer)) target.y = hit.point.y + 1.2f;
			CharacterController cc = player.PersonController.controller;
			cc.enabled = false;
			player.transform.position = target;
			player.PersonController.SwitchControllerType(ControllerType.Ground);
			cc.enabled = true;
			KeepAlive(player);
		}

		[ConsoleCommand(name: "CIUse", docs: "Dev, in game (either player): uses an object as a player pressing E would: CIUse <island> <object name or index>")]
		public static void UseCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(args);
			if (e == null) return;
			string what = args.Length > 1 ? args[1] : "";
			IslandObjectRef r = e.Root.GetComponentsInChildren<IslandObjectRef>(true)
				.FirstOrDefault(o => string.Equals(o.Name, what, StringComparison.OrdinalIgnoreCase) || o.Index.ToString() == what);
			if (r == null) { Fail("no object '" + what + "' on '" + e.HostName + "'"); return; }
			PutPlayerNear(r.transform);
			Behaviours.Fire(e, r.Index, "use", true);
			DynamicIslands.instance.StartCoroutine(AfterAction("Used '" + what + "' on '" + e.HostName + "' (" + (Raft_Network.IsHost ? "host" : "client") + ")"));
		}

		[ConsoleCommand(name: "CIGoto", docs: "Dev, in game (either player): puts the local player next to an object of the island (its name, or a trigger zone's id): CIGoto <island> <name>")]
		public static void GotoCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(args);
			if (e == null) return;
			string what = args.Length > 1 ? args[1] : "";
			IslandObjectRef r = e.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(o =>
				string.Equals(o.Name, what, StringComparison.OrdinalIgnoreCase) || string.Equals(ObjectProps.Get(o.Props, ObjectProps.ZoneId), what, StringComparison.OrdinalIgnoreCase));
			if (r == null) { Fail("no object or zone '" + what + "' on '" + e.HostName + "'"); return; }
			PutPlayerNear(r.transform, 0.3f); // (near the middle: a zone measures the distance in 3D, a slope adds to it)
			DynamicIslands.instance.StartCoroutine(AfterAction("Went to '" + what + "' on '" + e.HostName + "' (" + (Raft_Network.IsHost ? "host" : "client") + ")", 2f)); // (zones check once a second)
		}

		[ConsoleCommand(name: "CIOpenChest", docs: "Dev, in game (either player): opens a chest as a player would: CIOpenChest <island> [n-th chest, from 1; default the first not yet opened]")]
		public static void OpenChestCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(args);
			if (e == null) return;
			// (hidden chests - buried treasure - can't be reached until an action shows them)
			LootCrate[] chests = e.Root.GetComponentsInChildren<LootCrate>(false).OrderBy(c => { IslandObjectRef r = c.GetComponentInParent<IslandObjectRef>(); return r != null ? r.Index : 0; }).ToArray();
			int n;
			string title = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()) : "";
			Func<LootCrate, string> titleOf = c => { IslandObjectRef r = c.GetComponentInParent<IslandObjectRef>(); return r != null ? ObjectProps.Get(r.Props, ObjectProps.NoteTitle) : ""; };
			LootCrate chest = title.Length == 0 ? chests.FirstOrDefault(c => !c.Looted)
				: int.TryParse(title, out n) ? chests.ElementAtOrDefault(n - 1)
				: chests.FirstOrDefault(c => titleOf(c).Equals(title, StringComparison.OrdinalIgnoreCase));
			if (chest == null) { Fail("no such chest on '" + e.HostName + "' (" + chests.Length + " chests)"); return; }
			PutPlayerNear(chest.transform);
			List<string> got = chest.Open();
			NoteReader.Close();
			Log("Opened chest: got " + (got.Count > 0 ? string.Join(", ", got.ToArray()) : "nothing") + (chest.Looted ? "" : " (still closed)"));
			DynamicIslands.instance.StartCoroutine(AfterAction("Opened a chest on '" + e.HostName + "' (" + (Raft_Network.IsHost ? "host" : "client") + ")"));
		}

		[ConsoleCommand(name: "CIReadNote", docs: "Dev, in game (either player): reads a note as a player would: CIReadNote <island> [part of its title]")]
		public static void ReadNoteCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(args);
			if (e == null) return;
			string title = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()) : "";
			CustomNote note = e.Root.GetComponentsInChildren<CustomNote>(true).FirstOrDefault(c => c.GetComponent<LootCrate>() == null && (c.Title ?? "").IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0);
			if (note == null) { Fail("no note '" + title + "' on '" + e.HostName + "'"); return; }
			PutPlayerNear(note.transform);
			NoteReader.Open(note);
			NoteReader.Close();
			DynamicIslands.instance.StartCoroutine(AfterAction("Read '" + note.Title + "' on '" + e.HostName + "'"));
		}

		/// <summary>One axe hit on a tree, as Raft's Axe.OnAxeHit does it: the host chops; a client sends Message_AxeHit to the host.</summary>
		[ConsoleCommand(name: "CIChop", docs: "Dev, in game (either player): one axe hit on a tree of the island, as Raft's axe does it: CIChop <island>")]
		public static void ChopCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(args);
			if (e == null) return;
			HarvestableTree tree = e.Root.GetComponentsInChildren<HarvestableTree>().FirstOrDefault(t => !t.Depleted && t.gameObject.activeInHierarchy);
			if (tree == null) { Fail("no tree to chop on '" + e.HostName + "'"); return; }
			Network_Player player = RAPI.GetLocalPlayer();
			PutPlayerNear(tree.transform);
			int ord = (int)(tree.PickupNetwork.ObjectIndex & 0xFFFF);
			var msg = new Message_AxeHit((Messages)102, player, player.steamID);
			msg.treeObjectIndex = (int)tree.PickupNetwork.ObjectIndex;
			msg.HitPoint = tree.transform.position + Vector3.up;
			msg.HitNormal = Vector3.up;
			if (Raft_Network.IsHost)
			{
				tree.Harvest(player.Inventory);
				player.Network.RPC(msg, Target.Other, Steamworks.EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
			}
			else player.SendP2P(msg, Steamworks.EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
			Log("Chopped tree #" + ord + " on '" + e.HostName + "' once (" + (Raft_Network.IsHost ? "host" : "client: sent to the host") + ")");
		}

		/// <summary>Picks up an item of the island through the local player's Pickup script (Raft sends a client's pickup to the host).</summary>
		[ConsoleCommand(name: "CIPick", docs: "Dev, in game (either player): picks up an item (rock, plant...) of the island as a player would: CIPick <island>")]
		public static void PickCommand(string[] args)
		{
			IslandWorldState.Entry e = LoadedIsland(args);
			if (e == null) return;
			PickupItem_Networked pn = e.Root.GetComponentsInChildren<PickupItem_Networked>()
				.FirstOrDefault(p => p.GetComponent<HarvestableTree>() == null && p.gameObject.activeInHierarchy && p.GetComponent<PickupItem>() != null);
			if (pn == null) { Fail("nothing to pick up on '" + e.HostName + "'"); return; }
			Pickup pickup = RAPI.GetLocalPlayer().GetComponentInChildren<Pickup>(true);
			if (pickup == null) { Fail("the player has no Pickup script"); return; }
			PutPlayerNear(pn.transform);
			int ord = (int)(pn.ObjectIndex & 0xFFFF);
			pickup.PickupItemByType(pn.GetComponent<PickupItem>(), true);
			Log("Picked up " + pn.name + " #" + ord + " on '" + e.HostName + "' (" + (Raft_Network.IsHost ? "host" : "client") + ")");
		}

		/// <summary>After an action: what the player was told, and the island's shared state here, half a second later.</summary>
		static System.Collections.IEnumerator AfterAction(string what, float wait = 0.5f)
		{
			yield return new WaitForSeconds(wait);
			Log(what + ": message '" + Behaviours.LastMessage + "', story items " + string.Join(", ", StoryBook.Items.Select(h => h.Def.Id + " x" + h.Count).ToArray()));
		}
	}
}
