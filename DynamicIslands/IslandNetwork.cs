using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Steamworks;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// One message type for everything the mod sends between host and clients (RML serializes [Serializable] objects;
	/// arrays of primitives and strings are fine). Which fields are used depends on Kind.
	/// </summary>
	[Serializable]
	public class IslandNetMessage
	{
		public const int Islands = 1, Remove = 2, SyncRequest = 3, FileRequest = 4, FileChunk = 5;
		/// <summary>Host -> everyone (and each player who joins): the world's rules (WorldRules). Index = monster level, Count = build cost %.</summary>
		public const int WorldRules = 15;
		/// <summary>A player used something of an island that stays used (a looted chest, a trigger that fires once):
		/// Ids[0] = island, Index = state key, Count = in-game day. Clients send it to the host, the host to everyone.</summary>
		public const int ObjectUsed = 6;
		/// <summary>An island's quest moved on: Ids[0] = island, Index = step, Count = progress in it. Client -> host -> everyone.</summary>
		public const int QuestStep = 7;
		/// <summary>Host -> everyone: a rule brought a new island (WorldDirector). Name = title, Data = message, Offsets = where, relative to the raft.</summary>
		public const int Announce = 8;
		/// <summary>Host -> everyone: an object was shown/hidden or opened/closed (Behaviours). Ids[0] = island, Index = object, Count = bits (1 there, 2 open).</summary>
		public const int ObjectSet = 9;
		/// <summary>An object event (Behaviours): client -> host (do the shared actions), or host -> clients with FullList set
		/// (do the personal ones if near). Ids[0] = island, Index = object (0xFFFF = the island), Name = event.</summary>
		public const int EventFired = 10;
		/// <summary>The crew's story items and journal (StoryBook): client -> host a change (Name = give / take / page, Data =
		/// its fields), host -> everyone the whole state (Name = all, Data = its lines).</summary>
		public const int Story = 11;
		/// <summary>Host -> a player who just joined: where they stood on a custom island (PlayerPlaces). Ids[0] = island,
		/// Offsets = x,y,z from its middle.</summary>
		public const int PlayerPlace = 12;
		/// <summary>Host -> everyone: the world randomizer's settings and seed (WorldRandomizer). Data = RandomizerSettings.Encode().</summary>
		public const int Randomizer = 13;
		/// <summary>A thing only one player can have (Claims): client -> host "may I?" (Ids[0] = island, Index = state key),
		/// host -> that client the answer (Count 1 = granted, 0 = no, someone else has it).</summary>
		public const int Claim = 16;
		/// <summary>The level up system (PlayerLevels): host -> everyone "on"; host -> a joining player "state" (Data = their
		/// record); a player -> host "mine" (Data = their record now).</summary>
		public const int Levels = 14;
		/// <summary>Host -> everyone (and each player who joins): the world's options (WorldOptions). Data = "on=a,b;seed=n",
		/// Name = the private storages' builders (PrivateStorage).</summary>
		public const int WorldOptions = 17;
		/// <summary>Host -> everyone (after each save) and each player who joins: the world file (WorldCopy), so any player can
		/// host the world later. Name = world id, Hash = the copy's stamp, Index/Count = part, Data = text.</summary>
		public const int WorldCopy = 18;
		/// <summary>Host -> everyone (and each player who joins): the story chain (StoryChain) - Data = "on;steps|frequencies|unlocked|fired",
		/// Name = a banner to show ("title\ntext"), if any.</summary>
		public const int StoryChain = 19;
		/// <summary>Host -> everyone (when it changes, and each player who joins): the world's quest count (QuestCount, ROADMAP
		/// CW3) - Data = one line per quest, "group\tname\t1|0", so every player's journal shows the host's count.</summary>
		public const int QuestCount = 20;
		public int Kind;

		// Islands: one entry per island. Offsets are x,z per island relative to the host's raft, so a world shift
		// crossing the message on the wire doesn't matter, and y the island's own height (world shifts are flat; a
		// joining player's raft is still settling - it was 11 m low, and every island arrived 11 m up: the persistence
		// test). FullList: the client drops islands not in the list.
		public int[] Ids;
		public string[] Names;
		public string[] Hashes;
		public float[] Offsets;
		/// <summary>Harvested trees / picked-up items per island (IslandObjectState.Encode), for players who join later.</summary>
		public string[] States;
		/// <summary>Names shown on the Receiver (Entry.Label).</summary>
		public string[] Labels;
		/// <summary>The rule that brought each island (Entry.Rule): players' notebooks find a main story island's quest and
		/// notes by it (QuestBook) - since 2026-10-04.</summary>
		public string[] Rules;
		public bool FullList;

		// FileRequest / FileChunk: island file with this name and content hash, chunk Index of Count (base64)
		public string Name;
		public string Hash;
		public int Index;
		public int Count;
		public string Data;
	}

	/// <summary>
	/// Keeps clients' custom islands in step with the host. The host owns the island list (IslandWorldState):
	/// it sends the whole list when a client asks after joining, each new island as it spawns, and removals.
	/// Clients keep their own copy of the list, stream the islands in and out by distance like the host does,
	/// and fetch island files they don't have from the host (saved as &lt;name&gt;_&lt;hash&gt;.island).
	/// </summary>
	public static class IslandNetwork
	{
		/// <summary>Raw bytes per file chunk (base64 makes it about a third bigger).</summary>
		const int ChunkBytes = 3000;
		const float SyncRetrySeconds = 5f;
		const int SyncMaxTries = 12;

		static int nextId = 1;
		static bool synced;
		static int syncTries;
		static float nextSyncTry;
		// Client: files being received, by hash
		static readonly Dictionary<string, string[]> incoming = new Dictionary<string, string[]>();
		static readonly HashSet<string> requested = new HashSet<string>();
		// Client: when each file was asked for (asked again when it doesn't come), and whether the player was told
		static readonly Dictionary<string, float> requestedAt = new Dictionary<string, float>();
		static readonly HashSet<string> toldWaiting = new HashSet<string>();
		static bool toldNoList;
		static float nextFileCheck;
		const float FileRetrySeconds = 30f, SlowSyncSeconds = 30f;
		// Host: content hash per island name (with the file time it was computed for)
		static readonly Dictionary<string, KeyValuePair<DateTime, string>> hashCache = new Dictionary<string, KeyValuePair<DateTime, string>>(StringComparer.OrdinalIgnoreCase);

		static bool InMultiplayerGame { get { return LoadSceneManager.IsGameSceneLoaded && RAPI.GetLocalPlayer() != null; } }

		/// <summary>In a world with the network: messages go out (or to the tests' loopback).</summary>
		public static bool InGame { get { return InMultiplayerGame || Loopback != null; } }

		// Client: the host's world has arrived (Raft_Network.OnWorldReceivedLate), so the raft is where the host's
		// is. Island offsets are relative to the raft: before this they would land around the scene's origin. Seen in
		// a two-player test: an island spawned while the client was still joining ended up 1 km away.
		static bool worldReceived, wasInGame;

		/// <summary>Whether this machine knows the world's islands: the host always, a client once the host's list came.</summary>
		public static bool HasList { get { return Raft_Network.IsHost || synced; } }

		/// <summary>Client: Raft has received the host's world. Starts the island sync afresh (a message that came
		/// in while joining, or the list from an earlier game, is dropped: the host's full list replaces it).</summary>
		public static void OnWorldReceived()
		{
			if (Raft_Network.IsHost) return;
			worldReceived = true;
			WorldRules.OnWorldReceived();
			WorldOptions.OnWorldReceived();
			global::DynamicIslands.Editor.StoryChain.OnWorldReceived();
			IslandWorldState.RemoveIds(IslandWorldState.Islands.Select(e => e.Id).ToList(), false);
			synced = false;
			syncTries = 0;
			nextSyncTry = 0;
			Log("The host's world arrived: asking for its islands");
		}

		static void Log(string msg) { Debug.Log("[CUSTOM ISLANDS] [net] " + msg); }

		/// <summary>Host: a unique id for a new entry in the island list.</summary>
		public static int NewId() { return nextId++; }

		public static void OnWorldLoaded()
		{
			synced = Raft_Network.IsHost;
			syncTries = 0;
			nextSyncTry = 0;
			incoming.Clear();
			requested.Clear();
			requestedAt.Clear();
			toldWaiting.Clear();
			toldNoList = false;
		}

		/// <summary>Called every frame. Clients keep asking the host for the island list until it arrives.</summary>
		public static void Tick()
		{
			// Left the game: the next one sends its world again. (Only on leaving: Raft raises OnWorldReceivedLate
			// before it counts the game scene as loaded.)
			if (LoadSceneManager.IsGameSceneLoaded) wasInGame = true;
			else if (wasInGame) { wasInGame = false; worldReceived = false; }
			if (!Raft_Network.IsHost && worldReceived && InMultiplayerGame && Time.unscaledTime >= nextFileCheck) { nextFileCheck = Time.unscaledTime + 5f; RetryLateFiles(); }
			if (synced || Raft_Network.IsHost || !worldReceived || !InMultiplayerGame || Time.unscaledTime < nextSyncTry) return;
			// (after the first minute: still asking, slowly - a host busy loading, or a message lost, was left without islands for
			// the whole session before)
			if (syncTries >= SyncMaxTries && !toldNoList)
			{
				toldNoList = true;
				Debug.LogWarning("[CUSTOM ISLANDS] [net] The host hasn't sent its island list yet (does the host have Custom Islands?) - still asking every " + SlowSyncSeconds + " s");
				DynamicIslands.Notify("The host hasn't sent its custom islands yet (does the host have Custom Islands?). Still asking...", true);
			}
			syncTries++;
			nextSyncTry = Time.unscaledTime + (syncTries > SyncMaxTries ? SlowSyncSeconds : SyncRetrySeconds);
			// (with this player's version of the mod: the host says when they differ, and answers with its own)
			if (syncTries == 1) { HostAnswersClaims = false; HostAddsCounts = false; } // (a new host: known again from its answer)
			SendToHost(new IslandNetMessage { Kind = IslandNetMessage.SyncRequest, Name = VersionTag + LibraryPack.ModVersion });
		}

		/// <summary>A player: the host answers claims (it sent its version - claims are older than that - or answered one):
		/// a late answer is waited for, never taken as "yes" (both players looted one chest while the host was busy sending
		/// island files).</summary>
		public static bool HostAnswersClaims { get; internal set; }

		/// <summary>What this host does that older ones don't, told to players with its version ("counts": a player's quest
		/// events go to it as amounts it adds up, also for later steps - an older host took an amount for the total).</summary>
		const string HostCapabilities = "counts";

		/// <summary>A player: the host adds quest counts up (since 2026-10-01); else the player sends its total, as before.</summary>
		public static bool HostAddsCounts { get; internal set; }

		const string VersionTag = "version:";

		/// <summary>The last version difference seen (tests), or "".</summary>
		public static string VersionNotice { get; private set; }

		/// <summary>A player who joined (on the host) or the host (on a player) has another version of the mod: say so, once each.</summary>
		static void CompareVersions(string tag, string who)
		{
			if (string.IsNullOrEmpty(tag) || !tag.StartsWith(VersionTag)) return;
			string theirs = tag.Substring(VersionTag.Length), mine = LibraryPack.ModVersion;
			if (theirs == mine) return;
			string text = Raft_Network.IsHost
				? who + " joined with Custom Islands " + theirs + " - you have " + mine + ". Things may not match between you: both should use the same version."
				: "The host has Custom Islands " + theirs + " - you have " + mine + ". Things may not match between you: both should use the same version.";
			if (VersionNotice == text) return;
			VersionNotice = text;
			Debug.LogWarning("[CUSTOM ISLANDS] [net] " + text);
			global::DynamicIslands.DynamicIslands.Notify(text, true);
		}

		#region Sending

		/// <summary>Dev tests: when set, messages go here instead of over the network.</summary>
		public static Action<IslandNetMessage> Loopback;

		static void SendToClients(IslandNetMessage msg)
		{
			if (Loopback != null) { Loopback(msg); return; }
			if (!Raft_Network.IsHost || !InMultiplayerGame) return;
			try { DynamicIslands.instance.SendNetworkMessage(msg, Target.Other, EP2PSend.k_EP2PSendReliable); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [net] Sending failed: " + e.Message); }
		}

		/// <summary>Host: a message for every other player.</summary>
		public static void SendToEveryone(IslandNetMessage msg) { SendToClients(msg); }

		static void SendToPlayer(IslandNetMessage msg, Network_UserId player)
		{
			if (Loopback != null) { Loopback(msg); return; }
			try { DynamicIslands.instance.SendNetworkMessageToPlayer(msg, player, EP2PSend.k_EP2PSendReliable); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [net] Sending failed: " + e.Message); }
		}

		static void SendToHost(IslandNetMessage msg)
		{
			Raft_Network network = ComponentManager<Raft_Network>.Value;
			if (network == null) return;
			SendToPlayer(msg, network.HostID);
		}

		/// <summary>A story change: a client asks the host; the host sends the whole state to everyone.</summary>
		public static void SendStory(IslandNetMessage msg)
		{
			msg.Kind = IslandNetMessage.Story;
			if (Raft_Network.IsHost) SendToClients(msg);
			else if (InMultiplayerGame || Loopback != null) SendToHost(msg);
		}

		/// <summary>Host: the world's rules, to everyone.</summary>
		public static void SendWorldRules(IslandNetMessage msg)
		{
			if (Raft_Network.IsHost) SendToClients(msg);
		}

		/// <summary>The level up system: the host to one player (to set) or everyone; a player to the host.</summary>
		/// <summary>The world file's copy: the host to one player (who joined) or everyone.</summary>
		public static void SendWorldCopy(IslandNetMessage msg, Network_UserId? to)
		{
			msg.Kind = IslandNetMessage.WorldCopy;
			if (!Raft_Network.IsHost) return;
			if (to.HasValue) SendToPlayer(msg, to.Value); else SendToClients(msg);
		}

		public static void SendLevels(IslandNetMessage msg, Network_UserId? to)
		{
			msg.Kind = IslandNetMessage.Levels;
			if (Raft_Network.IsHost) { if (to.HasValue) SendToPlayer(msg, to.Value); else SendToClients(msg); }
			else if (InMultiplayerGame || Loopback != null) SendToHost(msg);
		}

		/// <summary>Host: tell clients about islands (new ones, or the whole list for a client that asked).</summary>
		internal static IslandNetMessage IslandsMessage(IEnumerable<IslandWorldState.Entry> entries, bool fullList)
		{
			Vector3 raft = CustomIslandSpawner.RaftPosition ?? Vector3.zero;
			// (an island still being made - its file not written yet - is left out: a player who joined in that moment got
			// it without a hash, it failed there, and its announcement later was ignored as known; it comes with that)
			var list = entries.Where(e => !e.Failed && HashOf(e.Name) != null).ToList();
			foreach (var e in list) IslandObjectState.Capture(e);
			var msg = new IslandNetMessage
			{
				Kind = IslandNetMessage.Islands,
				FullList = fullList,
				Ids = list.Select(e => e.Id).ToArray(),
				// (the island's own name: a host playing it from a downloaded copy - name_hash - still calls it by it)
				Names = list.Select(e => e.HostName ?? e.Name).ToArray(),
				Hashes = list.Select(e => HashOf(e.Name) ?? "").ToArray(),
				States = list.Select(e => IslandObjectState.Encode(e.State)).ToArray(),
				Labels = list.Select(e => e.Label ?? "").ToArray(),
				Rules = list.Select(e => e.Rule ?? "").ToArray(),
				Offsets = new float[list.Count * 3]
			};
			for (int i = 0; i < list.Count; i++)
			{
				Vector3 o = list[i].Position - raft;
				msg.Offsets[i * 3] = o.x; msg.Offsets[i * 3 + 1] = list[i].Position.y; msg.Offsets[i * 3 + 2] = o.z;
			}
			return msg;
		}

		public static void BroadcastAdded(IslandWorldState.Entry entry)
		{
			if (!Raft_Network.IsHost) return;
			SendToClients(IslandsMessage(new[] { entry }, false));
		}

		/// <summary>Tells the others that something of an island was used (host: to all clients; client: to the host, who passes it on).</summary>
		public static void SendUsed(int islandId, int key, int day)
		{
			var msg = new IslandNetMessage { Kind = IslandNetMessage.ObjectUsed, Ids = new[] { islandId }, Index = key, Count = day };
			if (Raft_Network.IsHost) SendToClients(msg);
			else if (InMultiplayerGame || Loopback != null) SendToHost(msg);
		}

		/// <summary>A player's quest event: its amount at that step, for the host to add ("add"; an older host takes it as the
		/// total, as before).</summary>
		public static void SendQuestAdd(int islandId, int step, int amount) { SendQuestAdd(islandId, 0, step, amount); }

		/// <summary>Quest n of an island (0 = the main quest): its number goes as a second id (an island's further quests, LM4).</summary>
		public static void SendQuestAdd(int islandId, int n, int step, int amount)
		{
			if (Raft_Network.IsHost) return;
			if (InMultiplayerGame || Loopback != null) SendToHost(new IslandNetMessage { Kind = IslandNetMessage.QuestStep, Ids = n == 0 ? new[] { islandId } : new[] { islandId, n }, Index = step, Count = amount, Name = "add" });
		}

		public static void SendQuest(int islandId, int step, int progress) { SendQuest(islandId, 0, step, progress); }

		public static void SendQuest(int islandId, int n, int step, int progress)
		{
			var msg = new IslandNetMessage { Kind = IslandNetMessage.QuestStep, Ids = n == 0 ? new[] { islandId } : new[] { islandId, n }, Index = step, Count = progress };
			if (Raft_Network.IsHost) SendToClients(msg);
			else if (InMultiplayerGame || Loopback != null) SendToHost(msg);
		}

		/// <summary>Host: tells everyone a rule brought this island (the banner with its direction).</summary>
		public static void SendAnnounce(IslandWorldState.Entry e, string title, string message)
		{
			if (!Raft_Network.IsHost) return;
			Vector3 o = e.Position - (CustomIslandSpawner.RaftPosition ?? Vector3.zero);
			SendToClients(new IslandNetMessage { Kind = IslandNetMessage.Announce, Ids = new[] { e.Id }, Name = title, Data = message, Offsets = new[] { o.x, e.Position.y, o.z } });
		}

		/// <summary>Host: an object's shared state changed.</summary>
		public static void SendObjectSet(int islandId, int index, int bits)
		{
			if (!Raft_Network.IsHost) return;
			SendToClients(new IslandNetMessage { Kind = IslandNetMessage.ObjectSet, Ids = new[] { islandId }, Index = index, Count = bits });
		}

		/// <summary>An object event: a client tells the host; the host tells clients (fromHost: they do the personal part).</summary>
		public static void SendEvent(int islandId, int index, string ev, bool fromHost)
		{
			var msg = new IslandNetMessage { Kind = IslandNetMessage.EventFired, Ids = new[] { islandId }, Index = index, Name = ev, FullList = fromHost };
			if (Raft_Network.IsHost) { if (fromHost) SendToClients(msg); }
			else if (InMultiplayerGame || Loopback != null) SendToHost(msg);
		}


		/// <summary>Client: asks the host for a thing only one player can have (Claims).</summary>
		public static void SendClaim(int islandId, int key)
		{
			if (!Raft_Network.IsHost && (InMultiplayerGame || Loopback != null)) SendToHost(new IslandNetMessage { Kind = IslandNetMessage.Claim, Ids = new[] { islandId }, Index = key });
		}
		public static void BroadcastRemoved(IEnumerable<int> ids)
		{
			int[] list = ids.ToArray();
			if (!Raft_Network.IsHost || list.Length == 0) return;
			SendToClients(new IslandNetMessage { Kind = IslandNetMessage.Remove, Ids = list });
		}

		#endregion

		#region Receiving

		/// <summary>Returns true if the message was ours.</summary>
		public static bool OnMessage(object message, Network_UserId from)
		{
			var msg = message as IslandNetMessage;
			if (msg == null) return false;
			try
			{
				switch (msg.Kind)
				{
					case IslandNetMessage.SyncRequest:
						// (a player: the host's answer with its version)
						if (!Raft_Network.IsHost) { if ((msg.Name ?? "").StartsWith(VersionTag)) HostAnswersClaims = true; HostAddsCounts = (msg.Data ?? "").Split(',').Contains("counts"); CompareVersions(msg.Name, "The host"); break; }
						if (Raft_Network.IsHost)
						{
							CompareVersions(msg.Name ?? VersionTag + "an older version", "A player");
							SendToPlayer(new IslandNetMessage { Kind = IslandNetMessage.SyncRequest, Name = VersionTag + LibraryPack.ModVersion, Data = HostCapabilities }, from);
							Log("Sending the island list (" + IslandWorldState.Islands.Count + ") to " + from);
							SendToPlayer(WorldRules.Message(), from);
							SendToPlayer(IslandsMessage(IslandWorldState.Islands, true), from);
							SendToPlayer(StoryBook.StateMessage(), from);
							SendToPlayer(WorldRandomizer.Message(), from);
							SendToPlayer(WorldOptions.Message(), from);
							SendToPlayer(global::DynamicIslands.Editor.StoryChain.Message(), from);
							SendToPlayer(global::DynamicIslands.Editor.QuestCount.Message(), from);
							// (after the list: the island it names is in the player's list then)
							IslandNetMessage place = PlayerPlaces.PlaceMessage(from.Id);
							if (place != null) SendToPlayer(place, from);
							IslandNetMessage levels = PlayerLevels.StateFor(from.Id);
							if (levels != null) SendLevels(levels, from);
							// (their own copy of the world, to host it later)
							global::DynamicIslands.Editor.WorldCopy.Send(from);
						}
						break;
					case IslandNetMessage.WorldRules:
						WorldRules.OnMessage(msg);
						break;
					case IslandNetMessage.PlayerPlace:
						if (!Raft_Network.IsHost && msg.Ids != null && msg.Ids.Length > 0 && msg.Offsets != null && msg.Offsets.Length >= 3)
						{
							IslandWorldState.Entry on = IslandWorldState.Islands.FirstOrDefault(e => e.Id == msg.Ids[0]);
							if (on != null) PlayerHold.GoTo(on.Position + new Vector3(msg.Offsets[0], msg.Offsets[1], msg.Offsets[2]), on.HostName);
						}
						break;
					case IslandNetMessage.FileRequest:
						if (Raft_Network.IsHost) SendFile(msg.Name, msg.Hash, from);
						break;
					case IslandNetMessage.Islands:
						if (!Raft_Network.IsHost) ReceiveIslands(msg);
						break;
					case IslandNetMessage.Remove:
						if (!Raft_Network.IsHost) IslandWorldState.RemoveIds(msg.Ids, false);
						break;
					case IslandNetMessage.FileChunk:
						if (!Raft_Network.IsHost) ReceiveChunk(msg);
						break;
					case IslandNetMessage.QuestStep:
						if (msg.Ids != null && msg.Ids.Length > 0)
						{
							// (a player's amount: the host adds it up and tells everyone the total)
							int questNo = msg.Ids.Length > 1 ? msg.Ids[1] : 0;
							if (Raft_Network.IsHost && msg.Name == "add") { QuestTracker.AddFromPlayer(msg.Ids[0], questNo, msg.Index, msg.Count); break; }
							QuestTracker.Apply(msg.Ids[0], questNo, msg.Index, msg.Count);
							if (Raft_Network.IsHost) SendToClients(msg);
						}
						break;
					case IslandNetMessage.ObjectSet:
						if (!Raft_Network.IsHost && msg.Ids != null && msg.Ids.Length > 0) Behaviours.ApplyRemote(msg.Ids[0], msg.Index, msg.Count);
						break;
					case IslandNetMessage.EventFired:
						if (msg.Ids != null && msg.Ids.Length > 0) Behaviours.OnEventMessage(msg.Ids[0], msg.Index, msg.Name ?? "", msg.FullList);
						break;
					case IslandNetMessage.Story:
						StoryBook.OnMessage(msg);
						break;
					case IslandNetMessage.Randomizer:
						WorldRandomizer.OnMessage(msg);
						break;
					case IslandNetMessage.WorldOptions:
						WorldOptions.OnMessage(msg);
						break;
					case IslandNetMessage.Levels:
						PlayerLevels.OnMessage(msg, from);
						break;
					case IslandNetMessage.WorldCopy:
						global::DynamicIslands.Editor.WorldCopy.OnMessage(msg);
						break;
					case IslandNetMessage.StoryChain:
						global::DynamicIslands.Editor.StoryChain.OnMessage(msg);
						break;
					case IslandNetMessage.QuestCount:
						global::DynamicIslands.Editor.QuestCount.OnMessage(msg);
						break;
					case IslandNetMessage.Announce:
						if (!Raft_Network.IsHost && worldReceived && msg.Offsets != null && msg.Offsets.Length >= 3)
							WorldDirector.Show(msg.Name ?? "", msg.Data ?? "", FromHost(CustomIslandSpawner.RaftPosition ?? Vector3.zero, msg.Offsets, 0));
						break;
					case IslandNetMessage.Claim:
						Claims.OnMessage(msg, from.Id, answer => SendToPlayer(answer, from));
						break;
					case IslandNetMessage.ObjectUsed:
						if (msg.Ids != null && msg.Ids.Length > 0)
						{
							// (a player's day isn't trusted - AU60: only the host makes things available again, and what a player
							// used gets the host's day, the one its regrow counts from - on every machine, as it is passed on so)
							if (Raft_Network.IsHost)
							{
								if (msg.Count < 0) { Log("A player said " + msg.Index.ToString("X") + " on island " + msg.Ids[0] + " is available again: only the host decides that"); break; }
								msg.Count = ContentState.Today;
							}
							ContentState.ApplyUsed(msg.Ids[0], msg.Index, msg.Count);
							if (Raft_Network.IsHost) SendToClients(msg); // everyone else learns it from the host
						}
						break;
				}
			}
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] [net] Handling message " + msg.Kind + " failed: " + e); }
			return true;
		}

		/// <summary>Where the host's island number i is here: beside this machine's raft as it is beside the host's, at its own height.</summary>
		static Vector3 FromHost(Vector3 raft, float[] offsets, int i)
		{
			return new Vector3(raft.x + offsets[i * 3], offsets[i * 3 + 1], raft.z + offsets[i * 3 + 2]);
		}

		static void ReceiveIslands(IslandNetMessage msg)
		{
			// (while joining: the raft isn't where the host's is yet; the full list asked for once the world is here has it)
			if (!worldReceived) { Log("Island message while joining: waiting for the host's world first"); return; }
			synced = true;
			Vector3 raft = CustomIslandSpawner.RaftPosition ?? Vector3.zero;
			int n = msg.Ids != null ? msg.Ids.Length : 0;
			if (msg.FullList)
			{
				var keep = new HashSet<int>(msg.Ids ?? new int[0]);
				IslandWorldState.RemoveIds(IslandWorldState.Islands.Where(e => !keep.Contains(e.Id)).Select(e => e.Id).ToArray(), false);
			}
			int added = 0;
			for (int i = 0; i < n; i++)
			{
				IslandWorldState.Entry known = IslandWorldState.Islands.FirstOrDefault(e => e.Id == msg.Ids[i]);
				if (known != null && msg.Rules != null && i < msg.Rules.Length && !string.IsNullOrEmpty(msg.Rules[i])) known.Rule = msg.Rules[i];
				if (known != null)
				{
					// (moved by the host: an island the players still need came back ahead of the raft - ReturningIslands)
					if (msg.Offsets != null && i * 3 + 2 < msg.Offsets.Length)
					{
						Vector3 at = FromHost(raft, msg.Offsets, i);
						if (new Vector2(at.x - known.Position.x, at.z - known.Position.z).magnitude > 5f)
						{
							if (known.Root != null) { IslandObjectState.Capture(known); IslandSpawner.Despawn(known.Root); known.Root = null; }
							known.Position = at;
							Log("Island " + known.Id + " '" + known.HostName + "' moved by the host to " + at.ToString("F0"));
						}
					}
					// (known without its file - it came while the host was still making it, or its file failed here: the
					// host's hash now lets it come after all; Resync didn't help before)
					string hash = msg.Hashes != null && i < msg.Hashes.Length ? msg.Hashes[i] ?? "" : "";
					if ((known.Failed || string.IsNullOrEmpty(known.Hash)) && hash.Length > 0 && known.Root == null && !known.Loading)
					{
						known.Hash = hash;
						known.Failed = false;
						known.WaitingForFile = false;
						ResolveFile(known);
						Log("Island " + known.Id + " '" + known.HostName + "' had no file here: trying again with the host's");
					}
					continue;
				}
				var entry = IslandWorldState.AddRemote(msg.Ids[i], msg.Names[i], msg.Hashes[i],
					FromHost(raft, msg.Offsets, i));
				if (msg.States != null && i < msg.States.Length) entry.State = IslandObjectState.Decode(msg.States[i]);
				if (msg.Labels != null && i < msg.Labels.Length) entry.Label = msg.Labels[i] ?? "";
				if (msg.Rules != null && i < msg.Rules.Length) entry.Rule = msg.Rules[i] ?? "";
				ResolveFile(entry);
				added++;
			}
			Log("Host sent " + n + " island(s)" + (msg.FullList ? " (full list)" : "") + ", " + added + " new");
		}

		/// <summary>
		/// Client: point the entry at a local file with the host's content. Uses the file of the same name when it
		/// matches, else a previously downloaded copy, else asks the host for it (the entry waits meanwhile).
		/// </summary>
		static void ResolveFile(IslandWorldState.Entry entry)
		{
			string hash = entry.Hash;
			if (string.IsNullOrEmpty(hash)) return; // host couldn't hash it; try the local file
			if (HashOf(entry.HostName) == hash) { entry.Name = entry.HostName; return; }
			string downloaded = DownloadName(entry.HostName, hash);
			if (File.Exists(IslandSpawner.PathFor(downloaded))) { entry.Name = downloaded; return; }
			// (the same content downloaded for another island name - the request already answered: a copy of it, not a wait
			// of 30 s for a second download - review 2026-10-06)
			try
			{
				string same = Directory.GetFiles(DynamicIslands.assetpath, "*_" + hash + IslandFile.Extension).FirstOrDefault();
				if (same != null)
				{
					File.Copy(same, IslandSpawner.PathFor(downloaded), true);
					entry.Name = downloaded;
					Log("Island file '" + entry.HostName + "' is the same as " + Path.GetFileName(same) + ": copied (" + hash + ")");
					return;
				}
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Copying an island file with the same content: " + e.Message); }

			entry.Name = downloaded;
			entry.WaitingForFile = true;
			if (requested.Add(hash))
			{
				requestedAt[hash] = Time.unscaledTime;
				Log("Asking the host for island file '" + entry.HostName + "' (" + hash + ")");
				SendToHost(new IslandNetMessage { Kind = IslandNetMessage.FileRequest, Name = entry.HostName, Hash = hash });
			}
		}

		/// <summary>Dev tests: accept chunks for this hash as if we had asked for it.</summary>
		internal static void ExpectFile(string hash) { requested.Add(hash); }

		internal static string DownloadName(string name, string hash) { return name + "_" + hash; }

		/// <summary>
		/// True for island files downloaded from a host (or kept for a saved world): &lt;name&gt;_&lt;12 hex digits&gt;, where the
		/// digits are the file's own content hash. (A player's island named like "camp_202609281530" - a date and time are
		/// 12 digits too - is theirs: it takes part while sailing and is never cleaned up as a copy.)
		/// </summary>
		public static bool IsDownloadName(string name)
		{
			int i = name.LastIndexOf('_');
			if (i <= 0 || name.Length - i - 1 != 12 || !name.Substring(i + 1).All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
			string h = HashOf(name);
			return h == null || h == name.Substring(i + 1);
		}

		internal static void ReceiveChunk(IslandNetMessage msg)
		{
			if (!requested.Contains(msg.Hash) || msg.Count <= 0 || msg.Index < 0 || msg.Index >= msg.Count) return;
			string[] parts;
			if (!incoming.TryGetValue(msg.Hash, out parts) || parts.Length != msg.Count) incoming[msg.Hash] = parts = new string[msg.Count];
			parts[msg.Index] = msg.Data;
			// (the wait for a retry starts again with every chunk: a big file over a slow line was asked for again every 30 s
			// from the first request and never finished - AU14)
			requestedAt[msg.Hash] = Time.unscaledTime;
			if (parts.Any(p => p == null)) return;

			incoming.Remove(msg.Hash);
			byte[] bytes = Convert.FromBase64String(string.Concat(parts));
			if (Hash(bytes) != msg.Hash) { Debug.LogWarning("[CUSTOM ISLANDS] [net] Island file '" + msg.Name + "' arrived damaged, asking again"); requested.Remove(msg.Hash); RetryWaiting(msg.Hash); return; }
			string name = DownloadName(msg.Name, msg.Hash);
			Directory.CreateDirectory(DynamicIslands.assetpath);
			SafeFile.WriteAllBytes(IslandSpawner.PathFor(name), bytes);
			Log("Received island file '" + msg.Name + "' (" + bytes.Length + " bytes), saved as " + name + IslandFile.Extension);
			foreach (var e in IslandWorldState.Islands.Where(e => e.Hash == msg.Hash))
			{
				// (two of the host's islands with the same content but other names: each entry waits for its own name's copy
				// - only the first was saved and the other never loaded, AU15)
				string want = DownloadName(e.HostName, msg.Hash);
				try { if (want != name && !File.Exists(IslandSpawner.PathFor(want))) File.Copy(IslandSpawner.PathFor(name), IslandSpawner.PathFor(want)); }
				catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] [net] Copying '" + name + "' for '" + e.HostName + "': " + ex.Message); }
				e.WaitingForFile = false;
			}
		}

		/// <summary>
		/// Client: an island file asked for that hasn't come in 30 s (the message lost, the host busy, the file missing on the
		/// host) is asked for again; the player is told once which island is waited for. Before, it waited for good.
		/// </summary>
		static void RetryLateFiles()
		{
			foreach (var e in IslandWorldState.Islands.Where(x => x.WaitingForFile && !string.IsNullOrEmpty(x.Hash)).ToList())
			{
				float at;
				if (!requestedAt.TryGetValue(e.Hash, out at) || Time.unscaledTime - at < FileRetrySeconds) continue;
				if (toldWaiting.Add(e.Hash)) DynamicIslands.Notify("Waiting for the island '" + e.HostName + "' from the host - still asking", true);
				Log("Island file '" + e.HostName + "' (" + e.Hash + ") hasn't come: asking again (keeping the " + (incoming.ContainsKey(e.Hash) ? incoming[e.Hash].Count(x => x != null) : 0) + " parts that came)");
				requested.Remove(e.Hash);
				// (the parts that came are kept: the host's next sending fills in the rest - AU14)
				RetryWaiting(e.Hash);
			}
		}

		/// <summary>Client: asks the host for everything again (the Resync command).</summary>
		public static void Resync()
		{
			synced = false; syncTries = 0; nextSyncTry = 0; toldNoList = false;
			requested.Clear(); requestedAt.Clear(); incoming.Clear(); toldWaiting.Clear();
			Log("Resync: asking the host for its islands again");
		}

		static void RetryWaiting(string hash)
		{
			foreach (var e in IslandWorldState.Islands.Where(e => e.Hash == hash).ToList()) ResolveFile(e);
		}

		internal static void SendFile(string name, string hash, Network_UserId to)
		{
			// (a host playing an island from a copy - name_hash, downloaded as a player or kept for a saved world - sends
			// that copy, under the island's own name: the player saves it as name_hash, the name its entry waits for; sent
			// as name_hash it was saved as name_hash_hash and the island never loaded)
			string file = name;
			if (HashOf(name) != hash && HashOf(DownloadName(name, hash)) == hash) file = DownloadName(name, hash);
			string path = IslandSpawner.PathFor(file);
			if (!File.Exists(path) || HashOf(file) != hash) { Debug.LogWarning("[CUSTOM ISLANDS] [net] " + to + " asked for island '" + name + "' (" + hash + ") which the host no longer has"); return; }
			byte[] bytes = File.ReadAllBytes(path);
			if (DynamicIslands.instance != null) DynamicIslands.instance.StartCoroutine(SendChunks(name, hash, bytes, to));
		}

		/// <summary>Chunks sent a frame (ROADMAP M3: all ~300 chunks of a 900 KB island went out in one loop, a burst that
		/// a slow connection dropped messages of).</summary>
		internal const int ChunksPerFrame = 6;

		static System.Collections.IEnumerator SendChunks(string name, string hash, byte[] bytes, Network_UserId to)
		{
			int count = Mathf.Max(1, (bytes.Length + ChunkBytes - 1) / ChunkBytes);
			float started = Time.realtimeSinceStartup;
			for (int i = 0; i < count; i++)
			{
				int len = Mathf.Min(ChunkBytes, bytes.Length - i * ChunkBytes);
				try { SendToPlayer(new IslandNetMessage { Kind = IslandNetMessage.FileChunk, Name = name, Hash = hash, Index = i, Count = count, Data = Convert.ToBase64String(bytes, i * ChunkBytes, len) }, to); }
				catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [net] Sending '" + name + "' chunk " + i + ": " + e.Message); }
				if ((i + 1) % ChunksPerFrame == 0) yield return null;
			}
			Log("Sent island file '" + name + "' (" + bytes.Length + " bytes, " + count + " chunks in " + (Time.realtimeSinceStartup - started).ToString("F1") + " s) to " + to);
		}

		#endregion

		#region Hashes

		static string Hash(byte[] bytes)
		{
			using (var sha = SHA1.Create())
				return BitConverter.ToString(sha.ComputeHash(bytes), 0, 6).Replace("-", "").ToLowerInvariant();
		}

		/// <summary>Short content hash of a saved island file, or null if there's no such file.</summary>
		public static string HashOf(string name)
		{
			string path = IslandSpawner.PathFor(name);
			if (!File.Exists(path)) return null;
			DateTime t = File.GetLastWriteTimeUtc(path);
			KeyValuePair<DateTime, string> cached;
			if (hashCache.TryGetValue(name, out cached) && cached.Key == t) return cached.Value;
			string h = Hash(File.ReadAllBytes(path));
			hashCache[name] = new KeyValuePair<DateTime, string>(t, h);
			return h;
		}

		#endregion
	}
}
