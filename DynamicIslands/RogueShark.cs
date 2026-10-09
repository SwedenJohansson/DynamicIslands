using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The world option "Rogue shark" (WorldOptions.RogueShark): now and then (a roll per in-game day from the world's seed,
	/// not before day 3) a second shark comes beside Bruce - rust-red. It doesn't leave: it stays until it is killed, and once
	/// killed no other comes for several days (5-8). While it lives, Bruce comes back as usual when killed (Raft only sends a
	/// new shark when the dying one was the last: the rogue isn't counted); the rogue's own body never brings one.
	///   - The host rolls and spawns it with Raft's own CreateAINetworkBehaviour (the other players get it as any shark) and
	///     sends which one it is (network kind 27: Index = its object index, 0 = none; Count = the day before which none comes).
	///   - Raft saves it as one of its sharks; the world file keeps which one ("@rogueshark=index;quietuntil"). If it can't be
	///     found after a load, another shark beyond the first is taken for it, or a new one comes.
	/// </summary>
	public static class RogueShark
	{
		public const float Chance = 0.12f;
		public const int FirstDay = 3, QuietMin = 5, QuietMax = 8;
		public static readonly Color Colour = new Color(1f, 0.5f, 0.38f);

		/// <summary>The living rogue's object index (0 = none).</summary>
		public static uint Index;
		/// <summary>No new rogue before this day (after a kill).</summary>
		public static int QuietUntil;
		// (the last one killed: its body still decays a while after the kill is noticed)
		static uint deadIndex;
		static int rolledDay = -1, tintedId;
		static float nextCheck, missingSince;

		/// <summary>Tests: the day in place of Raft's.</summary>
		internal static int? TestDay;

		public static bool On { get { return WorldOptions.On(WorldOptions.RogueShark); } }

		static void Log(string msg) { Debug.Log("[CUSTOM ISLANDS] [rogue] " + msg); }

		public static int Today
		{
			get
			{
				if (TestDay.HasValue) return TestDay.Value;
				try { return WorldManager.DayCounter; } catch { return 0; }
			}
		}

		internal static void Reset() { Index = 0; deadIndex = 0; QuietUntil = 0; rolledDay = -1; tintedId = 0; nextCheck = 0f; missingSince = 0f; TestDay = null; }

		/// <summary>Whether a rogue comes on this day (the same on every machine and every time).</summary>
		public static bool Rolls(int seed, int day)
		{
			if (day < FirstDay) return false;
			return WorldOptions.Unit(seed, day, 41) < Chance;
		}

		/// <summary>How many days none comes after one killed on this day.</summary>
		public static int QuietDays(int seed, int day)
		{
			return QuietMin + new System.Random(WorldOptions.Mix(seed, day, 5)).Next(QuietMax - QuietMin + 1);
		}

		public static bool IsRogueIndex(uint objectIndex) { return objectIndex != 0 && (objectIndex == Index || objectIndex == deadIndex); }
		public static bool IsRogue(AI_NetworkBehaviour ai) { return ai != null && IsRogueIndex(ai.ObjectIndex); }

		static bool Dead(AI_NetworkBehaviour ai) { return ai == null || (ai.networkEntity != null && ai.networkEntity.IsDead); }

		public static AI_NetworkBehavior_Shark Find()
		{
			if (Index == 0) return null;
			return UnityEngine.Object.FindObjectsOfType<AI_NetworkBehavior_Shark>().FirstOrDefault(s => s != null && s.ObjectIndex == Index);
		}

		public static bool Alive { get { AI_NetworkBehavior_Shark s = Find(); return s != null && !Dead(s); } }

		public static void Tick()
		{
			if (Time.unscaledTime < nextCheck) return;
			nextCheck = Time.unscaledTime + 1f;
			if (!LoadSceneManager.IsGameSceneLoaded) return;
			AI_NetworkBehavior_Shark rogue = Find();
			// (every machine: its colour, again for a new body - a load, a player who joins)
			if (rogue != null && !Dead(rogue) && rogue.GetInstanceID() != tintedId) Tint(rogue);
			if (!Raft_Network.IsHost) return;
			if (Index != 0)
			{
				if (rogue != null) { missingSince = 0f; if (Dead(rogue)) Killed(); return; }
				// Not found: after a load Raft's sharks come a moment later; then another shark is taken for it, or a new one comes
				if (missingSince <= 0f) { missingSince = Time.unscaledTime; return; }
				if (Time.unscaledTime - missingSince < 15f) return;
				missingSince = 0f;
				List<AI_NetworkBehavior_Shark> sharks = UnityEngine.Object.FindObjectsOfType<AI_NetworkBehavior_Shark>().Where(s => s != null && !Dead(s)).OrderBy(s => s.ObjectIndex).ToList();
				if (sharks.Count >= 2) { Index = sharks.Last().ObjectIndex; tintedId = 0; Log("Shark #" + Index + " taken for the rogue (its own wasn't found)"); Changed("state"); }
				else { Log("The rogue wasn't found: a new one"); Index = 0; Spawn(); }
				return;
			}
			if (!On) return;
			int day = Today;
			if (day == rolledDay) return;
			rolledDay = day;
			if (day >= QuietUntil && Rolls(WorldOptions.Seed, day)) Spawn();
		}

		/// <summary>Host: a rogue now, a little way off the raft.</summary>
		internal static AI_NetworkBehavior_Shark Spawn()
		{
			if (!Raft_Network.IsHost) return null;
			Network_Host_Entities host = null;
			try { host = ComponentManager<Network_Host_Entities>.Value; } catch { }
			Vector3? raft = CustomIslandSpawner.RaftPosition;
			if (host == null || !raft.HasValue) return null;
			Vector2 dir = UnityEngine.Random.insideUnitCircle.normalized;
			if (dir == Vector2.zero) dir = Vector2.up;
			Vector3 pos = new Vector3(raft.Value.x + dir.x * 45f, -10f, raft.Value.z + dir.y * 45f);
			AI_NetworkBehavior_Shark shark = host.CreateAINetworkBehaviour(AI_NetworkBehaviourType.Shark, pos, null) as AI_NetworkBehavior_Shark;
			if (shark == null) { Log("Raft made no shark"); return null; }
			Index = shark.ObjectIndex;
			tintedId = 0;
			Tint(shark);
			Log("A rogue shark #" + Index + " on day " + Today);
			Changed("spawn");
			return shark;
		}

		static void Killed()
		{
			Log("The rogue #" + Index + " killed on day " + Today);
			deadIndex = Index;
			Index = 0;
			QuietUntil = Today + QuietDays(WorldOptions.Seed, Today);
			Changed("dead");
		}

		static void Changed(string what)
		{
			IslandNetwork.SendToEveryone(Message(what));
			IslandWorldState.Save();
			Banner(what);
		}

		static void Banner(string what)
		{
			if (what == "spawn") IslandInfo.Show("A rogue shark", "", "A second shark, rust-red, has found the raft. It won't leave on its own - only when it's killed.");
			else if (what == "dead") IslandInfo.Show("The rogue shark is dead", "", "No other will come for a few days.");
		}

		static void Tint(AI_NetworkBehaviour ai)
		{
			int n = 0;
			foreach (Renderer r in WorldRandomizer.BodyOf(ai)) n += WorldRandomizer.TintTextures(r, Colour);
			tintedId = ai.GetInstanceID();
			Log("Shark #" + ai.ObjectIndex + " rust-red (" + n + " texture(s))");
		}

		/// <summary>The rogue's colour on this body (tests: the textures it has now).</summary>
		internal static bool LooksRogue(AI_NetworkBehaviour ai)
		{
			foreach (Renderer r in WorldRandomizer.BodyOf(ai))
			{
				var block = new MaterialPropertyBlock();
				r.GetPropertyBlock(block);
				Texture t = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_Diffuse") ? block.GetTexture("_Diffuse") : block.GetTexture("_MainTex");
				if (t != null && t.name.StartsWith("CI_Tinted_")) return true;
			}
			return false;
		}

		#region Network (kind 27), save

		internal static IslandNetMessage Message(string what)
		{
			return new IslandNetMessage { Kind = IslandNetMessage.RogueShark, Name = what, Index = unchecked((int)Index), Count = QuietUntil };
		}

		internal static void OnMessage(IslandNetMessage msg)
		{
			if (Raft_Network.IsHost) return;
			uint was = Index;
			Index = unchecked((uint)msg.Index);
			QuietUntil = msg.Count;
			if (was != 0 && Index != was) deadIndex = was;
			tintedId = 0;
			nextCheck = 0f;
			Log("The host's rogue: " + (Index == 0 ? "none" : "#" + Index) + ", none before day " + QuietUntil);
			if (msg.Name != "state") Banner(msg.Name);
		}

		internal static bool ReadLine(string key, string value)
		{
			if (key != "rogueshark") return false;
			string[] p = (value ?? "").Split(';');
			uint i; int q;
			Index = p.Length > 0 && uint.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out i) ? i : 0;
			QuietUntil = p.Length > 1 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out q) ? q : 0;
			tintedId = 0;
			missingSince = 0f;
			return true;
		}

		internal static IEnumerable<string> WriteLines()
		{
			if (Index != 0 || QuietUntil > 0) yield return "@rogueshark=" + Index.ToString(CultureInfo.InvariantCulture) + ";" + QuietUntil.ToString(CultureInfo.InvariantCulture);
		}

		#endregion

		[ConsoleCommand(name: "RogueShark", docs: "The world option Rogue shark: RogueShark = where it is; RogueShark spawn = one now (host); RogueShark kill = kill it (host, tests)")]
		public static void RogueSharkCommand(string[] args)
		{
			string a = args != null && args.Length > 0 ? args[0] : "";
			if (a == "spawn" && Raft_Network.IsHost && Index == 0) Spawn();
			AI_NetworkBehavior_Shark s = Find();
			if (a == "kill" && Raft_Network.IsHost && s != null && s.networkEntity != null) s.networkEntity.Damage(100000f, s.transform.position, Vector3.up, EntityType.Player, true);
			int sharks = UnityEngine.Object.FindObjectsOfType<AI_NetworkBehavior_Shark>().Count(x => x != null && !Dead(x));
			Debug.Log("[CUSTOM ISLANDS] Rogue shark: " + (On ? "on" : "off") + ", " + (Index == 0 ? "none" : "#" + Index + (s == null ? " (not here)" : Dead(s) ? " (dead)" : LooksRogue(s) ? " (rust-red)" : " (not coloured yet)")) +
				", none before day " + QuietUntil + " (today " + Today + "), " + sharks + " shark(s) alive");
		}
	}

	/// <summary>
	/// Rogue shark: a dead shark's body decays and Raft sends a new shark only if it was the last one (Network_Host_Entities.SharkCount
	/// 1). The rogue's body brings none; Bruce's does while the rogue lives, as if it weren't there.
	/// </summary>
	[HarmonyPatch(typeof(AI_State_Decay_Shark), "CheckToSpawnOnDecay")]
	static class RogueSharkDecay
	{
		static bool Prefix(AI_State_Decay_Shark __instance, ref bool __result, out int __state)
		{
			__state = 0;
			AI_NetworkBehaviour ai = __instance.GetComponentInParent<AI_NetworkBehaviour>();
			if (RogueShark.IsRogue(ai)) { __result = true; Debug.Log("[CUSTOM ISLANDS] [rogue] Its body decays: no shark comes for it"); return false; }
			if (RogueShark.Index == 0 || !RogueShark.Alive) return true;
			Network_Host_Entities host = null;
			try { host = ComponentManager<Network_Host_Entities>.Value; } catch { }
			if (host != null && host.SharkCount > 1) { host.SharkCount--; __state = 1; Debug.Log("[CUSTOM ISLANDS] [rogue] A shark's body decays while the rogue lives: Raft counts " + host.SharkCount + " shark(s)"); }
			return true;
		}

		static void Postfix(int __state)
		{
			if (__state == 0) return;
			try { ComponentManager<Network_Host_Entities>.Value.SharkCount += __state; } catch { }
		}
	}
}
