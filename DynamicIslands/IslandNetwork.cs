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
		/// <summary>A player used something of an island that stays used (a looted chest, a trigger that fires once):
		/// Ids[0] = island, Index = state key, Count = in-game day. Clients send it to the host, the host to everyone.</summary>
		public const int ObjectUsed = 6;
		public int Kind;

		// Islands: one entry per island. Offsets are x,y,z per island relative to the host's raft, so a world shift
		// crossing the message on the wire doesn't matter. FullList: the client drops islands not in the list.
		public int[] Ids;
		public string[] Names;
		public string[] Hashes;
		public float[] Offsets;
		/// <summary>Harvested trees / picked-up items per island (IslandObjectState.Encode), for players who join later.</summary>
		public string[] States;
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
		// Host: content hash per island name (with the file time it was computed for)
		static readonly Dictionary<string, KeyValuePair<DateTime, string>> hashCache = new Dictionary<string, KeyValuePair<DateTime, string>>(StringComparer.OrdinalIgnoreCase);

		static bool InMultiplayerGame { get { return LoadSceneManager.IsGameSceneLoaded && RAPI.GetLocalPlayer() != null; } }

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
		}

		/// <summary>Called every frame. Clients keep asking the host for the island list until it arrives.</summary>
		public static void Tick()
		{
			if (synced || Raft_Network.IsHost || !InMultiplayerGame || Time.unscaledTime < nextSyncTry) return;
			if (syncTries >= SyncMaxTries) { synced = true; Debug.LogWarning("[CUSTOM ISLANDS] [net] The host never sent its island list (does the host have Custom Islands?)"); return; }
			syncTries++;
			nextSyncTry = Time.unscaledTime + SyncRetrySeconds;
			SendToHost(new IslandNetMessage { Kind = IslandNetMessage.SyncRequest });
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

		/// <summary>Host: tell clients about islands (new ones, or the whole list for a client that asked).</summary>
		internal static IslandNetMessage IslandsMessage(IEnumerable<IslandWorldState.Entry> entries, bool fullList)
		{
			Vector3 raft = CustomIslandSpawner.RaftPosition ?? Vector3.zero;
			var list = entries.Where(e => !e.Failed).ToList();
			foreach (var e in list) IslandObjectState.Capture(e);
			var msg = new IslandNetMessage
			{
				Kind = IslandNetMessage.Islands,
				FullList = fullList,
				Ids = list.Select(e => e.Id).ToArray(),
				Names = list.Select(e => e.Name).ToArray(),
				Hashes = list.Select(e => HashOf(e.Name) ?? "").ToArray(),
				States = list.Select(e => IslandObjectState.Encode(e.State)).ToArray(),
				Offsets = new float[list.Count * 3]
			};
			for (int i = 0; i < list.Count; i++)
			{
				Vector3 o = list[i].Position - raft;
				msg.Offsets[i * 3] = o.x; msg.Offsets[i * 3 + 1] = o.y; msg.Offsets[i * 3 + 2] = o.z;
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
						if (Raft_Network.IsHost)
						{
							Log("Sending the island list (" + IslandWorldState.Islands.Count + ") to " + from);
							SendToPlayer(IslandsMessage(IslandWorldState.Islands, true), from);
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
					case IslandNetMessage.ObjectUsed:
						if (msg.Ids != null && msg.Ids.Length > 0)
						{
							ContentState.ApplyUsed(msg.Ids[0], msg.Index, msg.Count);
							if (Raft_Network.IsHost) SendToClients(msg); // everyone else learns it from the host
						}
						break;
				}
			}
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] [net] Handling message " + msg.Kind + " failed: " + e); }
			return true;
		}

		static void ReceiveIslands(IslandNetMessage msg)
		{
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
				if (IslandWorldState.Islands.Any(e => e.Id == msg.Ids[i])) continue;
				var entry = IslandWorldState.AddRemote(msg.Ids[i], msg.Names[i], msg.Hashes[i],
					raft + new Vector3(msg.Offsets[i * 3], msg.Offsets[i * 3 + 1], msg.Offsets[i * 3 + 2]));
				if (msg.States != null && i < msg.States.Length) entry.State = IslandObjectState.Decode(msg.States[i]);
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

			entry.Name = downloaded;
			entry.WaitingForFile = true;
			if (requested.Add(hash))
			{
				Log("Asking the host for island file '" + entry.HostName + "' (" + hash + ")");
				SendToHost(new IslandNetMessage { Kind = IslandNetMessage.FileRequest, Name = entry.HostName, Hash = hash });
			}
		}

		/// <summary>Dev tests: accept chunks for this hash as if we had asked for it.</summary>
		internal static void ExpectFile(string hash) { requested.Add(hash); }

		internal static string DownloadName(string name, string hash) { return name + "_" + hash; }

		/// <summary>True for island names of the form &lt;name&gt;_&lt;12 hex digits&gt; (files downloaded from a host).</summary>
		public static bool IsDownloadName(string name)
		{
			int i = name.LastIndexOf('_');
			return i > 0 && name.Length - i - 1 == 12 && name.Substring(i + 1).All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
		}

		internal static void ReceiveChunk(IslandNetMessage msg)
		{
			if (!requested.Contains(msg.Hash) || msg.Count <= 0 || msg.Index < 0 || msg.Index >= msg.Count) return;
			string[] parts;
			if (!incoming.TryGetValue(msg.Hash, out parts) || parts.Length != msg.Count) incoming[msg.Hash] = parts = new string[msg.Count];
			parts[msg.Index] = msg.Data;
			if (parts.Any(p => p == null)) return;

			incoming.Remove(msg.Hash);
			byte[] bytes = Convert.FromBase64String(string.Concat(parts));
			if (Hash(bytes) != msg.Hash) { Debug.LogWarning("[CUSTOM ISLANDS] [net] Island file '" + msg.Name + "' arrived damaged, asking again"); requested.Remove(msg.Hash); RetryWaiting(msg.Hash); return; }
			string name = DownloadName(msg.Name, msg.Hash);
			Directory.CreateDirectory(DynamicIslands.assetpath);
			File.WriteAllBytes(IslandSpawner.PathFor(name), bytes);
			Log("Received island file '" + msg.Name + "' (" + bytes.Length + " bytes), saved as " + name + IslandFile.Extension);
			foreach (var e in IslandWorldState.Islands.Where(e => e.Hash == msg.Hash)) e.WaitingForFile = false;
		}

		static void RetryWaiting(string hash)
		{
			foreach (var e in IslandWorldState.Islands.Where(e => e.Hash == hash).ToList()) ResolveFile(e);
		}

		internal static void SendFile(string name, string hash, Network_UserId to)
		{
			string path = IslandSpawner.PathFor(name);
			if (!File.Exists(path) || HashOf(name) != hash) { Debug.LogWarning("[CUSTOM ISLANDS] [net] " + to + " asked for island '" + name + "' (" + hash + ") which the host no longer has"); return; }
			byte[] bytes = File.ReadAllBytes(path);
			int count = Mathf.Max(1, (bytes.Length + ChunkBytes - 1) / ChunkBytes);
			for (int i = 0; i < count; i++)
			{
				int len = Mathf.Min(ChunkBytes, bytes.Length - i * ChunkBytes);
				SendToPlayer(new IslandNetMessage { Kind = IslandNetMessage.FileChunk, Name = name, Hash = hash, Index = i, Count = count, Data = Convert.ToBase64String(bytes, i * ChunkBytes, len) }, to);
			}
			Log("Sent island file '" + name + "' (" + bytes.Length + " bytes, " + count + " chunks) to " + to);
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
