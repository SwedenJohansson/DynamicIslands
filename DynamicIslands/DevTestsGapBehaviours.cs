using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Dev tests of the 11-04 gap behaviours: the time and weather checks, the weather, random, count and heal
	/// actions, counters, and the drinking spring.</summary>
	public static partial class DevTests
	{
		static List<ObjAction> Acts(string lines) { return ObjAction.ParseLines(lines); }

		[ConsoleCommand(name: "CIGapParse", docs: "Dev, anywhere: the 11-04 gap behaviours read right - time and weather checks, weather/random/count/heal actions, counters, the drinking spring's defaults, the editor's lists")]
		public static void GapParseCommand()
		{
			bool ok = true;
			// Time of day (2)
			Check(ref ok, GapBehaviours.TimeMatches("night", 23f, false) && !GapBehaviours.TimeMatches("night", 12f, true) && GapBehaviours.TimeMatches("day", 12f, true), "time: day and night follow Raft's sun");
			Check(ref ok, GapBehaviours.TimeMatches("dawn", 6f, true) && !GapBehaviours.TimeMatches("dawn", 9f, true) && GapBehaviours.TimeMatches("dusk", 20f, false), "time: dawn 5-8, dusk 18-21");
			Check(ref ok, GapBehaviours.TimeMatches("20-6", 2f, false) && GapBehaviours.TimeMatches("20-6", 21f, false) && !GapBehaviours.TimeMatches("20-6", 12f, true), "time: 20-6 runs past midnight");
			Check(ref ok, GapBehaviours.TimeMatches("12", 12.5f, true) && !GapBehaviours.TimeMatches("12", 13.2f, true) && GapBehaviours.TimeMatches("dawn, 12:30-14", 13f, true), "time: one hour, half hours, several with commas");
			Check(ref ok, !GapBehaviours.TimeMatches("noon", 12f, true) && !GapBehaviours.TimeMatches("", 12f, true) && !GapBehaviours.TimeMatches("12", -1f, true), "time: unknown words, empty and no sky never hold");
			ObjCheck tc = ObjCheck.Parse("time|night");
			Check(ref ok, tc != null && tc.Describe().Contains("night"), "time|night parses (" + (tc != null ? tc.Describe() : "null") + ")");
			Check(ref ok, ObjCheck.Parse("time|20-6").Describe().Contains("20:00-6:00"), "time|20-6 reads as hours (" + ObjCheck.Parse("time|20-6").Describe() + ")");
			// Weather (5)
			Check(ref ok, GapBehaviours.NameOf(UniqueWeatherType.Rain) == "rain" && GapBehaviours.NameOf(UniqueWeatherType.SnowRough) == "storm" && GapBehaviours.NameOf(UniqueWeatherType.Default) == "clear", "weather names of Raft's weathers");
			Check(ref ok, GapBehaviours.WeatherMatches("rain,fog", "fog") && GapBehaviours.WeatherMatches("storm", "rain") && !GapBehaviours.WeatherMatches("rain", "clear") && !GapBehaviours.WeatherMatches("", "clear"), "weather check: any of several, storm = rain too");
			Check(ref ok, GapBehaviours.Minutes("") == GapBehaviours.DefaultWeatherMinutes && GapBehaviours.Minutes("2") == 2 && GapBehaviours.Minutes("9999") == 600 && GapBehaviours.Minutes("x") == GapBehaviours.DefaultWeatherMinutes, "weather minutes: default 10, max 600");
			ObjAction wa = ObjAction.Parse("weather|fog|2");
			Check(ref ok, wa != null && wa.Shared && wa.Describe().Contains("fog for 2 min"), "weather|fog|2 is shared (" + (wa != null ? wa.Describe() : "null") + ")");
			Check(ref ok, ObjAction.Parse("weather|raft|").Describe().Contains("chooses") && ObjAction.Parse("weather|hail|").Describe().Contains("not clear"), "weather|raft hands back, a wrong name is said");
			ObjAction wr = ObjAction.ToLines(new[] { wa }) is string wl ? ObjAction.Parse(wl) : null;
			Check(ref ok, wr != null && wr.Target == "fog" && wr.Arg == "2", "the weather action saves and reads back");
			// Random (6)
			List<ObjAction> list = Acts("message||before\nrandom||3\ncount|A|\ncount|B|\ncount|C|\ncount|Total|");
			var seen = new HashSet<string>();
			bool same = true;
			for (int s = 1; s <= 60; s++)
			{
				List<ObjAction> picked = GapBehaviours.Pick(list, s * 7919);
				if (picked.Count != 3 || picked[0].Verb != "message" || picked[2].Target != "Total") { same = false; break; }
				seen.Add(picked[1].Target);
				same &= GapBehaviours.Pick(list, s * 7919)[1].Target == picked[1].Target && picked[1].Target == "ABC".Substring(GapBehaviours.Choose(s * 7919, 1, 3), 1);
			}
			Check(ref ok, same && seen.Count == 3, "random||3 keeps one of the next 3, the same for the same seed, all 3 come up (" + string.Join(",", seen.ToArray()) + ")");
			List<ObjAction> w = GapBehaviours.Pick(Acts("random||4\ncount|A|\nwait||2\ncount|B|"), 5);
			Check(ref ok, w.Count == 3 && w[0].Target == "A" && w[1].Verb == "wait", "a random group stops at a wait (" + ObjAction.ToLines(w).Replace("\n", " / ") + ")");
			Check(ref ok, GapBehaviours.Options(ObjAction.Parse("random||")) == 2 && GapBehaviours.Options(ObjAction.Parse("random|5|")) == 5 && GapBehaviours.Options(ObjAction.Parse("random||99")) == GapBehaviours.MaxRandom, "random: default 2, max 20");
			Check(ref ok, GapBehaviours.Pick(Acts("count|A|"), 3).Count == 1 && GapBehaviours.Pick(Acts("random||3"), 3).Count == 0, "no random line: as written; a random line alone: nothing");
			// Counters (7)
			Check(ref ok, GapBehaviours.Counted(2, "") == 3 && GapBehaviours.Counted(2, "+5") == 7 && GapBehaviours.Counted(2, "-1") == 1 && GapBehaviours.Counted(2, "=9") == 9 && GapBehaviours.Counted(2, "reset") == 0 && GapBehaviours.Counted(2, "x") == 2, "count: +1, +N, -N, =N, reset");
			Check(ref ok, GapBehaviours.CountMatches(3, ">=3") && !GapBehaviours.CountMatches(2, "3") && GapBehaviours.CountMatches(0, "=0") && GapBehaviours.CountMatches(1, "") && !GapBehaviours.CountMatches(0, "")
				&& GapBehaviours.CountMatches(2, "<3") && !GapBehaviours.CountMatches(3, "!=3") && GapBehaviours.CountMatches(4, "> 3") && GapBehaviours.CountMatches(3, "<= 3"), "count check: >=, bare number, =, <, !=, >, <=, empty = at least 1");
			int k1 = GapBehaviours.CounterKey("Fish"), k2 = GapBehaviours.CounterKey(" fish "), k3 = GapBehaviours.CounterKey("Fish2");
			Check(ref ok, k1 == k2 && k1 != k3 && k1 >= GapBehaviours.CounterBase && k1 <= GapBehaviours.CounterBase + 0xFFFFF, "a counter's key: case and spaces don't matter, in its own range");
			Check(ref ok, GapBehaviours.CounterBase > Behaviours.PendingBase + 0xFFFFF || GapBehaviours.CounterBase + 0xFFFFF < Behaviours.PendingBase, "the counter keys meet no pending keys");
			ObjAction ca = ObjAction.Parse("count|Fish|");
			ObjCheck cc = ObjCheck.Parse("count|Fish|>=3");
			Check(ref ok, ca != null && ca.Shared && ca.Describe().Contains("goes up by 1") && cc != null && cc.Describe().Contains("at least 3"), "count|Fish and count|Fish|>=3 (" + (ca != null ? ca.Describe() : "null") + "; " + (cc != null ? cc.Describe() : "null") + ")");
			// Heal (10)
			Dictionary<string, float> h = GapBehaviours.HealParts("");
			Check(ref ok, h.Count == 3 && h.Values.All(v => v < 0f), "heal: empty = everything full");
			h = GapBehaviours.HealParts("thirst, hp 20");
			Check(ref ok, h.Count == 2 && h["drink"] < 0f && Mathf.Approximately(h["health"], 20f), "heal: thirst = drink, hp 20 = 20 health");
			Check(ref ok, GapBehaviours.HealParts("gold").Count == 0 && ObjAction.Parse("heal||gold").Describe().Contains("not health"), "heal: a wrong word does nothing and is said");
			ObjAction ha = ObjAction.Parse("heal||food");
			Check(ref ok, ha != null && !ha.Shared && ha.Describe().Contains("food"), "heal is the player's own (" + (ha != null ? ha.Describe() : "null") + ")");
			// The spring
			Dictionary<string, string> d = ObjectProps.Defaults(GapBehaviours.SpringName);
			Check(ref ok, ObjectProps.Get(d, "on.use") == "heal||drink" && ObjectProps.Get(d, BehaviourProps.Use).Length > 0, "a placed spring quenches thirst when used");
			IDictionary<string, string> x = PlayerFeatures.Expand(GapBehaviours.SpringName, P("on.use", "message||Fresh"));
			Check(ref ok, ObjectProps.Get(x, "on.use").StartsWith("heal||drink\n") && ObjectProps.Get(x, BehaviourProps.Use).Length > 0, "a spring's own lines keep the drink");
			x = PlayerFeatures.Expand(GapBehaviours.SpringName, P("on.use", "heal||all", BehaviourProps.Use, "Bathe"));
			Check(ref ok, ObjectProps.Get(x, "on.use") == "heal||all" && ObjectProps.Get(x, BehaviourProps.Use) == "Bathe", "a spring that heals already is left as it is");
			Check(ref ok, PlayerFeatures.Expand("Log", P("on.use", "message||x"))["on.use"] == "message||x", "other objects are left as they are");
			Check(ref ok, ContentCatalog.Hint(GapBehaviours.SpringName).Length > 0, "the catalog has the spring");
			// The editor's lists
			Check(ref ok, new[] { "weather", "random", "count", "heal" }.All(v => BehaviourProps.Verbs.Contains(v)) && new[] { "time", "weather", "count" }.All(v => BehaviourProps.CheckKinds.Contains(v)), "the new actions and checks are known");
			Check(ref ok, BehaviourProps.SharedVerbs.Contains("weather") && BehaviourProps.SharedVerbs.Contains("count") && !BehaviourProps.SharedVerbs.Contains("heal") && !BehaviourProps.SharedVerbs.Contains("random"), "weather and count run on the host, heal and random on each player");
			CheckPlayerTables(ref ok);
			if (ok) Log("PASS: gap behaviours parse"); else Fail("gap behaviours parse");
		}

		/// <summary>Fires an object's use and waits past the host's one-second same-moment guard.</summary>
		static IEnumerator GapUse(IslandWorldState.Entry e, int index)
		{
			Behaviours.Fire(e, index, "use", true);
			yield return new WaitForSeconds(1.15f);
		}

		static IEnumerator GapIsland(Action<IslandFile, Func<string, float, float, Dictionary<string, string>, int>> build, Action<IslandWorldState.Entry> got)
		{
			if (!CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); got(null); yield break; }
			yield return EnsureAlive();
			IslandFile f = PlayerTestIsland(build);
			IslandWorldState.Entry e = null;
			yield return SpawnPlayerIsland(f, z => e = z);
			if (e == null || e.Root == null) { Fail("the island did not spawn"); RemovePlayerIsland(e); got(null); yield break; }
			yield return new WaitForSeconds(1f);
			got(e);
		}

		[ConsoleCommand(name: "CIGapTime", docs: "Dev, in a world (host): spawns 'ciplayer' with an object checking time|night: used at 23:00 its on.use runs, at 12:00 its else.use; the real clock read")]
		public static void GapTimeCommand() { StartTest(GapTimeRoutine()); }

		static IEnumerator GapTimeRoutine()
		{
			bool ok = true;
			int lamp = -1;
			IslandWorldState.Entry e = null;
			yield return GapIsland((file, add) => { lamp = add("Log", 0f, 4f, P(BehaviourProps.Use, "Look", "if.use", "time|night", "on.use", "count|nightuse|", "else.use", "count|dayuse|")); }, x => e = x);
			if (e == null) yield break;
			try
			{
				float real = GapBehaviours.Hour;
				Check(ref ok, real >= 0f && real <= 24f, "Raft's clock reads " + real.ToString("F1") + " (" + (GapBehaviours.IsDay ? "day" : "night") + ")");
				GapBehaviours.TestHour = 23f;
				yield return GapUse(e, lamp);
				Check(ref ok, GapBehaviours.Counter(e, "nightuse") == 1 && GapBehaviours.Counter(e, "dayuse") == 0, "at 23:00 the night check passes (night " + GapBehaviours.Counter(e, "nightuse") + ", day " + GapBehaviours.Counter(e, "dayuse") + ")");
				GapBehaviours.TestHour = 12f;
				yield return GapUse(e, lamp);
				Check(ref ok, GapBehaviours.Counter(e, "nightuse") == 1 && GapBehaviours.Counter(e, "dayuse") == 1, "at 12:00 it fails and else.use runs (night " + GapBehaviours.Counter(e, "nightuse") + ", day " + GapBehaviours.Counter(e, "dayuse") + ")");
			}
			finally
			{
				GapBehaviours.TestHour = null;
				RemovePlayerIsland(e);
			}
			if (ok) Log("PASS: gap time check"); else Fail("gap time check");
		}

		[ConsoleCommand(name: "CIGapWeather", docs: "Dev, in a world (host): spawns 'ciplayer': weather|fog|2 makes fog for 2 min, a weather|fog check passes then, weather|raft hands the weather back to Raft")]
		public static void GapWeatherCommand() { StartTest(GapWeatherRoutine()); }

		static IEnumerator GapWeatherRoutine()
		{
			bool ok = true;
			int fog = -1, look = -1, back = -1;
			IslandWorldState.Entry e = null;
			yield return GapIsland((file, add) =>
			{
				fog = add("Log", 0f, 4f, P(BehaviourProps.Use, "Fog", "on.use", "weather|fog|2"));
				look = add("Log", 3f, 4f, P(BehaviourProps.Use, "Look", "if.use", "weather|fog,rain", "on.use", "count|fogseen|", "else.use", "count|clearseen|"));
				back = add("Log", -3f, 4f, P(BehaviourProps.Use, "Back", "on.use", "weather|raft|"));
			}, x => e = x);
			if (e == null) yield break;
			WeatherManager wm = UnityEngine.Object.FindObjectOfType<WeatherManager>();
			try
			{
				Check(ref ok, wm != null, "the world has Raft's weather");
				if (wm != null)
				{
					Log("weather before: " + GapBehaviours.WeatherNow + " (" + wm.GetCurrentWeatherType() + "), timer " + wm.WeatherTimer.ToString("F0"));
					yield return GapUse(e, fog);
					yield return new WaitForSeconds(1f);
					Check(ref ok, wm.GetCurrentWeatherType() == UniqueWeatherType.Fog && GapBehaviours.WeatherNow == "fog", "weather|fog|2 makes fog (" + wm.GetCurrentWeatherType() + ")");
					Check(ref ok, wm.WeatherTimer > 100f && wm.WeatherTimer <= 120f, "for 2 minutes (timer " + wm.WeatherTimer.ToString("F0") + " s)");
					yield return GapUse(e, look);
					Check(ref ok, GapBehaviours.Counter(e, "fogseen") == 1 && GapBehaviours.Counter(e, "clearseen") == 0, "the weather|fog,rain check passes in fog");
					GapBehaviours.TestWeather = "clear";
					yield return GapUse(e, look);
					GapBehaviours.TestWeather = null;
					Check(ref ok, GapBehaviours.Counter(e, "clearseen") == 1, "and fails when clear (else.use)");
					yield return GapUse(e, back);
					Check(ref ok, wm.WeatherTimer <= 5f, "weather|raft: Raft chooses again in 5 s (timer " + wm.WeatherTimer.ToString("F0") + ")");
				}
			}
			finally
			{
				GapBehaviours.TestWeather = null;
				RemovePlayerIsland(e);
			}
			if (ok) Log("PASS: gap weather"); else Fail("gap weather");
		}

		[ConsoleCommand(name: "CIGapRandom", docs: "Dev, in a world (host): spawns 'ciplayer' with random||3 before count A/B/C and count Total: 10 uses count 10 in all, one of A/B/C each time, more than one of them")]
		public static void GapRandomCommand() { StartTest(GapRandomRoutine()); }

		static IEnumerator GapRandomRoutine()
		{
			bool ok = true;
			int dice = -1;
			IslandWorldState.Entry e = null;
			yield return GapIsland((file, add) => { dice = add("Log", 0f, 4f, P(BehaviourProps.Use, "Roll", "on.use", "random||3\ncount|A|\ncount|B|\ncount|C|\ncount|Total|")); }, x => e = x);
			if (e == null) yield break;
			try
			{
				for (int i = 0; i < 10; i++) yield return GapUse(e, dice);
				int a = GapBehaviours.Counter(e, "A"), b = GapBehaviours.Counter(e, "B"), c = GapBehaviours.Counter(e, "C"), t = GapBehaviours.Counter(e, "Total");
				Check(ref ok, t == 10, "the line after the group runs every time (Total " + t + ")");
				Check(ref ok, a + b + c == 10, "one of A/B/C each time (A " + a + ", B " + b + ", C " + c + ")");
				Check(ref ok, new[] { a, b, c }.Count(n => n > 0) >= 2, "not always the same one");
			}
			finally { RemovePlayerIsland(e); }
			if (ok) Log("PASS: gap random"); else Fail("gap random");
		}

		[ConsoleCommand(name: "CIGapCount", docs: "Dev, in a world (host): spawns 'ciplayer': count|Fish +1 three times opens a gate checking count|Fish|>=3 (before: its else.use), -5 stops at 0, =7 sets it, players are sent each change, the counter is kept with the island's state")]
		public static void GapCountCommand() { StartTest(GapCountRoutine()); }

		static IEnumerator GapCountRoutine()
		{
			bool ok = true;
			int fish = -1, gate = -1, minus = -1, set = -1;
			IslandWorldState.Entry e = null;
			yield return GapIsland((file, add) =>
			{
				fish = add("Log", 0f, 4f, P(BehaviourProps.Use, "Catch", "on.use", "count|Fish|"));
				gate = add("Log", 3f, 4f, P(BehaviourProps.Use, "Open", "if.use", "count|Fish|>=3", "on.use", "signal||fish-gate", "else.use", "count|Early|"));
				minus = add("Log", -3f, 4f, P(BehaviourProps.Use, "Lose", "on.use", "count|Fish|-5"));
				set = add("Log", 0f, 7f, P(BehaviourProps.Use, "Set", "on.use", "count|Fish|=7"));
			}, x => e = x);
			if (e == null) yield break;
			var sent = new List<IslandNetMessage>();
			try
			{
				yield return GapUse(e, gate);
				Check(ref ok, !Behaviours.HasSignal(e, "fish-gate") && GapBehaviours.Counter(e, "Early") == 1, "the gate stays shut at 0 fish (else.use ran)");
				IslandNetwork.Loopback = sent.Add;
				try { Behaviours.Fire(e, fish, "use", true); }
				finally { IslandNetwork.Loopback = null; }
				IslandNetMessage m = sent.FirstOrDefault(s => s.Name == "count");
				Check(ref ok, m != null && m.Ids != null && m.Ids[0] == e.Id && m.Index == GapBehaviours.CounterKey("Fish") && m.Count == 1, "players are told the new count (" + (m != null ? m.Index + "=" + m.Count : "no message") + ")");
				yield return new WaitForSeconds(1.15f);
				yield return GapUse(e, fish);
				Check(ref ok, GapBehaviours.Counter(e, "Fish") == 2, "two catches: 2");
				yield return GapUse(e, gate);
				Check(ref ok, !Behaviours.HasSignal(e, "fish-gate"), "still shut at 2");
				yield return GapUse(e, fish);
				yield return GapUse(e, gate);
				Check(ref ok, Behaviours.HasSignal(e, "fish-gate") && GapBehaviours.Counter(e, "Early") == 2, "open at 3");
				Check(ref ok, e.State.ContainsKey(GapBehaviours.CounterKey("Fish")) && e.State[GapBehaviours.CounterKey("Fish")].Yield == 3, "kept in the island's state (saved with the world)");
				yield return GapUse(e, minus);
				Check(ref ok, GapBehaviours.Counter(e, "Fish") == 0, "-5 stops at 0 (" + GapBehaviours.Counter(e, "Fish") + ")");
				yield return GapUse(e, set);
				Check(ref ok, GapBehaviours.Counter(e, "Fish") == 7, "=7 sets it (" + GapBehaviours.Counter(e, "Fish") + ")");
				// A player's copy takes the host's number; a key outside the counters' range is left alone
				var copy = new IslandNetMessage { Kind = IslandNetMessage.Mech, Name = "count", Ids = new[] { e.Id }, Index = GapBehaviours.CounterKey("Fish"), Count = 4 };
				GapBehaviours.OnCountMessage(copy);
				Check(ref ok, GapBehaviours.Counter(e, "Fish") == 7, "the host ignores a count message (it decides)");
			}
			finally
			{
				IslandNetwork.Loopback = null;
				RemovePlayerIsland(e);
			}
			if (ok) Log("PASS: gap count"); else Fail("gap count");
		}

		[ConsoleCommand(name: "CIGapHeal", docs: "Dev, in a world: spawns 'ciplayer' with a drinking spring and a heal||health 20 stone: drinking fills thirst only, the stone gives 20 health; a picture of the spring (shot_from_gap_spring.png)")]
		public static void GapHealCommand() { StartTest(GapHealRoutine()); }

		static IEnumerator GapHealRoutine()
		{
			bool ok = true;
			int spring = -1, stone = -1;
			IslandWorldState.Entry e = null;
			yield return GapIsland((file, add) =>
			{
				spring = add(GapBehaviours.SpringName, 0f, 4f, P(BehaviourProps.Use, "Drink"));
				stone = add("Log", 3f, 4f, P(BehaviourProps.Use, "Rest", "on.use", "heal||health 20"));
			}, x => e = x);
			if (e == null) yield break;
			Network_Player p = RAPI.GetLocalPlayer();
			PlayerStats s = p != null ? p.Stats : null;
			float h0 = 0f, f0 = 0f, d0 = 0f;
			try
			{
				Check(ref ok, s != null, "the player has stats");
				IslandObjectRef r = e.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(o => o.Index == spring);
				Check(ref ok, r != null, "the spring is placed");
				if (s != null && r != null)
				{
					h0 = s.stat_health.Value; f0 = s.stat_hunger.Normal.Value; d0 = s.stat_thirst.Normal.Value;
					Vector3 at = r.transform.position;
					Check(ref ok, r.GetComponentsInChildren<TextMesh>(true).All(t => !t.gameObject.activeInHierarchy), "its editor name tag is hidden in the world");
					RaycastHit hit;
					bool grounded = Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out hit, 10f, 1 << IslandSpawner.TerrainLayer) && Mathf.Abs(hit.point.y - at.y) < 0.4f;
					Check(ref ok, grounded, "it sits on the ground (" + (grounded ? (at.y - hit.point.y).ToString("F2") + " m" : "no ground under it") + ")");
					yield return CameraShot(at + new Vector3(2.4f, 1.6f, -2.4f), at + Vector3.up * 0.2f, "from_gap_spring");
					PutPlayerNear(r.transform, 1.5f);
					yield return new WaitForSeconds(0.5f);
					s.stat_health.Value = 30f; s.stat_hunger.Normal.Value = 10f; s.stat_thirst.Normal.Value = 10f;
					UseInteract u = r.GetComponentInChildren<UseInteract>(true);
					Check(ref ok, u != null, "the spring can be used");
					if (u != null) Traverse.Create(u).Method("Use").GetValue();
					yield return new WaitForSeconds(0.5f);
					Check(ref ok, s.stat_thirst.Normal.Value >= s.stat_thirst.Normal.Max - 0.5f, "drinking fills thirst (" + s.stat_thirst.Normal.Value.ToString("F0") + "/" + s.stat_thirst.Normal.Max.ToString("F0") + ")");
					Check(ref ok, s.stat_hunger.Normal.Value < 15f, "and not food (" + s.stat_hunger.Normal.Value.ToString("F0") + ")");
					yield return new WaitForSeconds(0.7f);
					yield return GapUse(e, stone);
					Check(ref ok, s.stat_health.Value >= 48f && s.stat_health.Value <= 55f, "heal||health 20 gives 20 health (30 -> " + s.stat_health.Value.ToString("F0") + ")");
				}
			}
			finally
			{
				if (s != null && h0 > 0f) { s.stat_health.Value = h0; s.stat_hunger.Normal.Value = Mathf.Max(f0, 50f); s.stat_thirst.Normal.Value = Mathf.Max(d0, 50f); }
				RemovePlayerIsland(e);
			}
			if (ok) Log("PASS: gap heal"); else Fail("gap heal");
		}

		[ConsoleCommand(name: "CIGapMp", docs: "Dev, two players (D:\\Temp\\scratch_8e762ddb\\gapbeh\\mpgapbeh.ps1): CIGapMp host (host: 'ciplayer' with a Fish counter at 3 and a random||3 dice), client (player 2: sees Fish 3, uses the dice 6 times - the host counts the very ones player 2 picked; GAPMP lines), done (host: removes it)")]
		public static void GapMpCommand(string[] args) { StartTest(GapMpRoutine(args != null && args.Length > 0 ? args[0] : "")); }

		static IEnumerator GapMpRoutine(string part)
		{
			bool ok = true;
			if (part == "host")
			{
				int fish = -1, dice = -1;
				IslandWorldState.Entry e = null;
				yield return GapIsland((file, add) =>
				{
					fish = add("Log", 0f, 4f, P(BehaviourProps.Use, "Catch", "on.use", "count|Fish|"));
					dice = add("Log", 3f, 4f, P(BehaviourProps.Use, "Roll", "on.use", "random||3\ncount|R0|\ncount|R1|\ncount|R2|"));
				}, x => e = x);
				if (e == null) yield break;
				for (int i = 0; i < 3; i++) yield return GapUse(e, fish);
				Check(ref ok, GapBehaviours.Counter(e, "Fish") == 3, "the host counts 3 fish");
				if (ok) Log("PASS: gap mp host"); else Fail("gap mp host");
				yield break;
			}
			if (part == "client")
			{
				if (Raft_Network.IsHost) { Fail("run as player 2"); yield break; }
				IslandWorldState.Entry e = LoadedIsland(new[] { PlayerIsland });
				if (e == null) yield break;
				IslandObjectRef dice = e.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(o => o.Props != null && ObjectProps.Get(o.Props, "on.use").Contains("R0"));
				Check(ref ok, GapBehaviours.Counter(e, "Fish") == 3, "player 2 sees the host's counter (Fish " + GapBehaviours.Counter(e, "Fish") + ")");
				Check(ref ok, dice != null, "player 2 has the dice");
				if (dice == null) { Fail("gap mp client"); yield break; }
				PutPlayerNear(dice.transform, 1.5f);
				yield return new WaitForSeconds(1f);
				var picks = new int[3];
				for (int i = 0; i < 6; i++)
				{
					Behaviours.Fire(e, dice.Index, "use", true);
					picks[GapBehaviours.Choose(GapBehaviours.Seed, 0, 3)]++;
					yield return new WaitForSeconds(1.5f);
				}
				yield return new WaitForSeconds(2f);
				int r0 = GapBehaviours.Counter(e, "R0"), r1 = GapBehaviours.Counter(e, "R1"), r2 = GapBehaviours.Counter(e, "R2");
				Log("GAPMP picked " + picks[0] + "/" + picks[1] + "/" + picks[2] + " counted " + r0 + "/" + r1 + "/" + r2);
				Check(ref ok, r0 + r1 + r2 == 6, "the host ran each of player 2's 6 uses once (" + (r0 + r1 + r2) + ")");
				Check(ref ok, r0 == picks[0] && r1 == picks[1] && r2 == picks[2], "the host counted the very ones player 2 picked");
				if (ok) Log("PASS: gap mp client"); else Fail("gap mp client");
				yield break;
			}
			if (part == "done")
			{
				IslandWorldState.Entry e = IslandWorldState.Islands.FirstOrDefault(x => x.HostName == PlayerIsland);
				int r = e != null ? GapBehaviours.Counter(e, "R0") + GapBehaviours.Counter(e, "R1") + GapBehaviours.Counter(e, "R2") : -1;
				Log("GAPMP host counted " + r);
				RemovePlayerIsland(e);
				Log("PASS: gap mp done");
				yield break;
			}
			Fail("CIGapMp host | client | done");
		}
	}
}
