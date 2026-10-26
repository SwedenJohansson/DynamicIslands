using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;
using HMLLibrary;
using HarmonyLib;
using Debug = UnityEngine.Debug;

namespace DynamicIslands
{
	/// <summary>A watch on animals' AI states (why a quest animal goes away).</summary>
	public static partial class DevTests
	{
		[ConsoleCommand(name: "CIStateWatch", docs: "Dev, world: CIStateWatch <name part> [seconds] - in the background, logs every AI state change of the animals whose object name has that part (state, position, health), and when one goes; returns at once")]
		public static void StateWatchCommand(string[] args)
		{
			string part = args != null && args.Length > 0 ? args[0] : "Stingray";
			float secs = args != null && args.Length > 1 ? F(args[1]) : 600f;
			watchPart = part;
			if (!destroyPatched)
			{
				destroyPatched = true;
				try
				{
					new Harmony("ci.statewatch").Patch(AccessTools.Method(typeof(UnityEngine.Object), "Destroy", new[] { typeof(UnityEngine.Object) }),
						new HarmonyMethod(typeof(DevTests).GetMethod("WatchDestroyPrefix", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
				}
				catch (Exception ex) { Log("state watch: no Destroy patch (" + ex.Message + ")"); }
			}
			DynamicIslands.instance.StartCoroutine(StateWatchRoutine(part, secs));
			Log("state watch on '" + part + "' for " + secs + " s");
		}

		static string watchPart;
		static bool destroyPatched;

		/// <summary>Logs who destroys a watched animal (the call stack, before Unity's deferred destroy).</summary>
		static void WatchDestroyPrefix(UnityEngine.Object obj)
		{
			try
			{
				if (watchPart == null || obj == null) return;
				GameObject go = obj as GameObject ?? (obj is Component ? ((Component)obj).gameObject : null);
				if (go == null || go.name.IndexOf(watchPart, StringComparison.OrdinalIgnoreCase) < 0 || go.GetComponentInChildren<AI_NetworkBehaviour>(true) == null) return;
				Log("watch destroy of " + go.name + " (" + obj.GetType().Name + "): " + new System.Diagnostics.StackTrace(1, false).ToString().Replace("\n", " | "));
			}
			catch { }
		}

		static IEnumerator StateWatchRoutine(string part, float secs)
		{
			var last = new Dictionary<AI_NetworkBehaviour, string>();
			var ids = new Dictionary<AI_NetworkBehaviour, string>();
			float end = Time.time + secs;
			while (Time.time < end)
			{
				foreach (AI_NetworkBehaviour a in UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>())
				{
					if (a == null || a.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) continue;
					if (!ids.ContainsKey(a)) ids[a] = a.name + "#" + a.GetInstanceID();
					AI_StateMachine sm = a.GetComponentInChildren<AI_StateMachine>(true);
					string st = sm == null ? "?" : sm.currentState == null ? "none" : sm.currentState.GetType().Name + "(" + sm.currentState.name + ")";
					string s;
					if (!last.TryGetValue(a, out s) || s != st)
					{
						Log("watch " + ids[a] + ": " + st + " at " + a.transform.position.ToString("F0") + ", hp " + (a.networkEntity != null && a.networkEntity.stat_health != null ? a.networkEntity.stat_health.Value.ToString("F0") : "?") +
							", spawner " + (a.connectedSpawner != null ? a.connectedSpawner.name : "none") + ", trackers " + a.GetComponentsInChildren<WorldEvent_Tracker>(true).Length);
						last[a] = st;
					}
				}
				foreach (AI_NetworkBehaviour off in last.Keys.Where(k => k != null && !k.gameObject.activeInHierarchy && last[k] != "inactive").ToList())
				{
					Log("watch " + ids[off] + ": inactive (was " + last[off] + ")");
					last[off] = "inactive";
				}
				foreach (AI_NetworkBehaviour gone in last.Keys.Where(k => k == null).ToList())
				{
					Log("watch " + ids[gone] + ": gone (last " + last[gone] + ")");
					last.Remove(gone);
				}
				yield return new WaitForSeconds(0.5f);
			}
			Log("state watch on '" + part + "' ended");
			watchPart = null;
		}

		[ConsoleCommand(name: "CIQuestStrayBack", docs: "Dev, world (host, 'CI ...'): a test island whose quest asks to kill its stingray; the stingray taken away alive (as Raft does) comes back at its spot at once, not counted as defeated (CreatureSpawner.Account, SL13 Lowstreet)")]
		public static void QuestStrayBackCommand(string[] args) { StartTest(QuestStrayBackRoutine()); }

		static IEnumerator QuestStrayBackRoutine()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || !Raft_Network.IsHost || !(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("quest stray back: host, in a test world 'CI ...'"); yield break; }
			yield return EnsureAlive();
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { "Creature_Stingray" });
			bool ok = true;
			const string name = "CI_StrayBack";
			var gs = new IslandGenSettings { Seed = 6262, Radius = 40f, Height = 6f, Trees = 0f, Bushes = 0f, Rocks = 0f, Harvest = 0f, BeachThings = 0f, Water = 0f, SeaRocks = 0f, SeaFinds = 0f, Sunken = 0f };
			IslandFile f = IslandGenerator.CreateFile(gs, name);
			f.Props[IslandQuest.KeyTitle] = "CI stray quest";
			f.Props[IslandQuest.KeySteps] = "kill|Stingray|1|Drive off the stingray";
			Vector3? sea = ScUnderSea(f, 4f);
			if (!sea.HasValue) { Fail("quest stray back: no sea deep enough by the test island"); yield break; }
			f.Objects.Add(ScObj("Creature_Stingray", sea.Value, ObjectProps.CreatureCount, "1"));
			IslandWorldState.Entry e = null;
			yield return RfSpawn(f, name, x => e = x);
			if (e == null) yield break;
			CreatureSpawnPoint p = null;
			AI_NetworkBehaviour first = null;
			for (float t = 0f; t < 30f && first == null; t += 0.5f)
			{
				p = e.Root != null ? e.Root.GetComponentsInChildren<CreatureSpawnPoint>(true).FirstOrDefault(x => x.Kind != null && x.Kind.Type == AI_NetworkBehaviourType.Stingray) : null;
				first = p != null && p.RecordedAlive > 0 ? p.Spawned.FirstOrDefault(a => a != null) : null;
				if (first == null) yield return new WaitForSeconds(0.5f);
			}
			Check(ref ok, first != null, "the quest's stingray is there");
			if (first == null) { Fail("quest stray back"); yield break; }
			int done = QuestTracker.QuestOf(e, 0) != null ? QuestTracker.StepOf(e, 0) : -1;
			// (taken away alive, as Raft's own sea animals are)
			UnityEngine.Object.Destroy(first.gameObject);
			AI_NetworkBehaviour back = null;
			for (float t = 0f; t < 5f && back == null; t += 0.25f)
			{
				yield return new WaitForSeconds(0.25f);
				back = p.Spawned.FirstOrDefault(a => a != null && a != first && (a.networkEntity == null || !a.networkEntity.IsDead));
			}
			Check(ref ok, back != null && Vector3.Distance(back.transform.position, p.transform.position) < 12f, "taken away alive: a stingray back at its spot within 5 s" + (back != null ? " (" + Vector3.Distance(back.transform.position, p.transform.position).ToString("F1") + " m from it)" : ""));
			Check(ref ok, p.Strayed == 0 && p.RecordedAlive == 1, "not counted as gone or defeated (strayed " + p.Strayed + ", alive " + p.RecordedAlive + ")");
			Check(ref ok, done < 0 || QuestTracker.StepOf(e, 0) == done, "the quest's kill step not done by it");
			Check(ref ok, back != null && back.GetComponentsInChildren<WorldEvent_Tracker_AI>(true).Length == 0, "the new one kept out of Raft's sea events too");
			IslandWorldState.Remove(name);
			if (ok) Log("PASS: quest stray back"); else Fail("quest stray back");
		}
	}
}
