using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>Tests of the quest mechanics (MechFeatures, 2026-10-24): push and pulse zones, carry and stack, lure, collapse,
	/// moveto, a boss guarded by checks. MECH_TESTS.md has the in-game blocks.</summary>
	public static partial class DevTests
	{
		const string MechIsland = "cimech";

		[ConsoleCommand(name: "CIMechParse", docs: "Dev, anywhere: the quest mechanics' settings read right (push, pulse, stack, lure item, carry, moveto, state keys, the behaviour window's verbs)")]
		public static void MechParseCommand()
		{
			bool ok = true;
			MechFeatures.PushSpec s = MechFeatures.PushSpec.Parse(P(MechFeatures.PushProp, "0,2,0,10"));
			Check(ref ok, s != null && s.Dir == Vector3.up && Mathf.Approximately(s.Force, 10f) && s.Kick == 0f, "zone.push=0,2,0,10: up at 10, no kick");
			s = MechFeatures.PushSpec.Parse(P(MechFeatures.PushProp, "1,0,0,500,7"));
			Check(ref ok, s != null && Mathf.Approximately(s.Force, MechFeatures.MaxForce) && Mathf.Approximately(s.Kick, 7f), "a force over the cap is kept to " + MechFeatures.MaxForce + ", kick 7");
			Check(ref ok, MechFeatures.PushSpec.Parse(P(MechFeatures.PushProp, "0,0,0,5")) == null && MechFeatures.PushSpec.Parse(P(MechFeatures.PushProp, "fan")) == null
				&& MechFeatures.PushSpec.Parse(P()) == null, "no direction, words or nothing: no push");
			Check(ref ok, MechFeatures.PushSpec.Join(Vector3.right, 6f, 0f) == "1,0,0,6" && MechFeatures.PushSpec.Join(Vector3.up, 8f, 3f) == "0,1,0,8,3", "the inspector writes push values back");
			Dictionary<string, string> pulse = P(MechFeatures.PulseProp, "3,2");
			Check(ref ok, MechFeatures.PulseOn(pulse, 1f) && !MechFeatures.PulseOn(pulse, 4f) && MechFeatures.PulseOn(pulse, 5.5f) && MechFeatures.PulseOn(P(), 4f),
				"zone.pulse=3,2: on at 1 s, off at 4 s, on at 5.5 s; no pulse = always on");
			Check(ref ok, !MechFeatures.PulseOn(P(MechFeatures.PulseProp, "3,2,1"), 2.5f), "a pulse's offset moves it (3,2,1 is off at 2.5 s)");
			string n; int c;
			Check(ref ok, MechFeatures.StackOf(P(MechFeatures.StackProp, "Crate|4"), out n, out c) && n == "Crate" && c == 4 && !MechFeatures.IsStack(P()), "zone.stack=Crate|4");
			Check(ref ok, MechFeatures.StackOf(P(MechFeatures.StackProp, "Stone"), out n, out c) && c == 1, "a stack with no count needs 1");
			string zd = MechFeatures.DescribeZone(P(MechFeatures.PushProp, "0,1,0,10", MechFeatures.PulseProp, "3,2", MechFeatures.StackProp, "Crate|4"));
			Check(ref ok, zd.Contains("pushes up") && zd.Contains("on 3 s") && zd.Contains("4 × 'Crate'"), "the catalog line tells push, pulse and stack (" + zd + ")");

			IDictionary<string, string> lure = MechFeatures.Expand("Log", P(BehaviourProps.Name, "Trough", MechFeatures.LureItem, "Plank|2"));
			Check(ref ok, ObjectProps.Get(lure, "if.use") == "state|Trough|closed\ntake|Plank|2" && ObjectProps.Get(lure, "on.use").StartsWith("open|")
				&& ObjectProps.Get(lure, "else.use").StartsWith("message||") && ObjectProps.Get(lure, BehaviourProps.Use).StartsWith("Put in "), "beh.lureItem makes its use lines (take, open, else a message)");
			Dictionary<string, string> own = P(MechFeatures.LureItem, "Plank", "on.use", "message||mine");
			Check(ref ok, ReferenceEquals(MechFeatures.Expand("Log", own), own), "a lure with its own use lines keeps them");
			IDictionary<string, string> carry = MechFeatures.Expand("Log", P("on.use", "carry|"));
			Check(ref ok, ObjectProps.Get(carry, BehaviourProps.Use) == "Pick up", "a carried piece's use label: Pick up");
			Check(ref ok, ObjectProps.Get(MechFeatures.Expand("Log", P("on.use", "carry|", BehaviourProps.Use, "Lift the stone")), BehaviourProps.Use) == "Lift the stone", "its own label wins");
			Check(ref ok, MechFeatures.DescribeMoveTo("'Cart'", "Ledge,6").Contains("'Ledge'") && MechFeatures.DescribeMoveTo("'Cart'", "Ledge,6").Contains("6 s"), "moveto|Cart|Ledge,6 described");

			List<ObjAction> acts = ObjAction.ParseLines("carry|\ncollapse|Bridge|2\nmoveto|Cart|Ledge,4");
			Check(ref ok, acts.Count == 3 && acts[1].Target == "Bridge" && acts[1].Arg == "2" && acts[2].Arg == "Ledge,4", "the three verbs parse (" + acts.Count + ")");
			Check(ref ok, BehaviourProps.SharedVerbs.Contains("collapse") && BehaviourProps.SharedVerbs.Contains("moveto") && !BehaviourProps.SharedVerbs.Contains("carry"),
				"collapse and moveto are shared (host), carry is the player's");
			var labels = Traverse.Create(typeof(BehaviourWindow)).Field("VerbLabels").GetValue<Dictionary<string, string>>();
			var hints = Traverse.Create(typeof(BehaviourWindow)).Field("VerbHints").GetValue<Dictionary<string, string>>();
			string[] missing = BehaviourProps.Verbs.Where(v => labels == null || hints == null || !labels.ContainsKey(v) || !hints.ContainsKey(v)).ToArray();
			Check(ref ok, missing.Length == 0, "every verb has a label and a hint in the behaviour window" + (missing.Length > 0 ? " (missing " + string.Join(", ", missing) + ")" : ""));
			Check(ref ok, MechFeatures.PlaceBase + 4 * 0xFFFF + 3 < MechFeatures.StackDoneBase && MechFeatures.PlaceBase > Behaviours.SignalKey("zzz") && MechFeatures.PlaceBase >= 0x10000,
				"the saved places' keys don't meet signals or the regrow keys");
			Check(ref ok, PatchHealth.Feature("CarryJumpPatch").Contains("carrying"), "PatchHealth names the carry patches (" + PatchHealth.Feature("CarryJumpPatch") + ")");
			if (ok) Log("PASS: quest mechanics' settings"); else Fail("quest mechanics' settings");
		}

		[ConsoleCommand(name: "CIMechRun", docs: "Dev, in a world (host): spawns 'cimech' and plays its mechanics: an updraft zone, two crates carried onto a stack target (its stack event), a lure fed a plank (the warthog comes; its shieldIf holds), a bridge that collapses, a cart moved to a ledge")]
		public static void MechRunCommand() { StartTest(MechRunRoutine()); }

		static IEnumerator MechRunRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			IslandFile f = SampleIsland();
			f.Name = MechIsland;
			f.Elevation = 0f;
			Vector2 c = IslandSpawner.LandCentre(f);
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1);
			Func<float, float, Vector3> ground = (x, z) => new Vector3(x, f.Heights[Mathf.Clamp(Mathf.RoundToInt(z / step), 0, res - 1), Mathf.Clamp(Mathf.RoundToInt(x / step), 0, res - 1)] * f.TerrainSize.y, z);
			f.Objects.RemoveAll(o => new Vector2(o.Position.x - c.x, o.Position.z - c.y).magnitude < 25f);
			Func<string, Vector3, Dictionary<string, string>, int> add = (name, pos, props) => { f.Objects.Add(new IslandObject { Name = name, Position = pos, Props = props }); return f.Objects.Count - 1; };
			int push = add(ContentCatalog.TriggerZone, ground(c.x + 12f, c.y), P(ObjectProps.ZoneRadius, "2.5", MechFeatures.PushProp, "0,1,0,9"));
			int stack = add(ContentCatalog.TriggerZone, ground(c.x - 10f, c.y), P(ObjectProps.ZoneRadius, "2.5", MechFeatures.StackProp, "Crate|2", BehaviourProps.EventKey(MechFeatures.StackEvent), "signal||mech-stacked"));
			int crate1 = add("Log", ground(c.x - 4f, c.y + 2f), P(BehaviourProps.Name, "Crate", "on.use", "carry|", MechFeatures.CarrySnap, "0.5"));
			int crate2 = add("Log", ground(c.x - 4f, c.y - 2f), P(BehaviourProps.Name, "Crate", "on.use", "carry|"));
			int trough = add("Log", ground(c.x, c.y + 10f), P(BehaviourProps.Name, "Trough", MechFeatures.LureProp, "Goats|2", MechFeatures.LureItem, "Plank|1"));
			int goats = add("Creature_Boar", ground(c.x + 9f, c.y + 16f), P(ObjectProps.CreatureCount, "1", ObjectProps.CreatureDamage, "0", BehaviourProps.Name, "Goats", MechFeatures.ShieldIf, "state|Trough|open"));
			int bridge = add("Log", ground(c.x + 4f, c.y - 9f), P(BehaviourProps.Name, "Bridge"));
			int lever1 = add("Log", ground(c.x + 2f, c.y - 6f), P(BehaviourProps.Use, "Break", "on.use", "collapse|Bridge|1"));
			int cart = add("Log", ground(c.x - 6f, c.y - 9f), P(BehaviourProps.Name, "Cart"));
			add("Log", ground(c.x - 6f, c.y - 15f) + Vector3.up * 2f, P(BehaviourProps.Name, "Ledge", BehaviourProps.Collision, "none"));
			int lever2 = add("Log", ground(c.x - 3f, c.y - 6f), P(BehaviourProps.Use, "Send", "on.use", "moveto|Cart|Ledge,1"));
			f.Save(IslandSpawner.PathFor(MechIsland));
			IslandWorldState.Entry old = IslandWorldState.Islands.FirstOrDefault(x => x.HostName == MechIsland);
			if (old != null) IslandWorldState.RemoveIds(new[] { old.Id }, true);
			Vector3? spot = CustomIslandSpawner.FindClearSpot(raftPos.Value, CustomIslandSpawner.LandRadius(MechIsland), 300f);
			if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
			int before = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile(MechIsland, spot.Value, true);
			IslandWorldState.Entry e = IslandWorldState.Islands.Skip(before).FirstOrDefault();
			if (e == null || e.Root == null) { Fail("the island did not spawn"); yield break; }
			Func<int, IslandObjectRef> obj = i => e.Root.GetComponentsInChildren<IslandObjectRef>(true).FirstOrDefault(r => r.Index == i);
			Network_Player me = RAPI.GetLocalPlayer();
			try
			{
				yield return new WaitForSeconds(1f);
				// Push: the updraft lifts the player
				TriggerZone pz = obj(push) != null ? obj(push).GetComponent<TriggerZone>() : null;
				Check(ref ok, pz != null && pz.Push != null, "the push zone has its push");
				if (pz != null)
				{
					int p0 = MechFeatures.Pushes;
					PlayerMove.To(me, pz.transform.position + Vector3.up * 0.3f);
					float y0 = me.transform.position.y;
					yield return new WaitForSeconds(0.6f);
					Check(ref ok, MechFeatures.Pushes > p0 && me.transform.position.y > y0 + 0.5f, "the updraft lifts the player (" + (me.transform.position.y - y0).ToString("F1", CultureInfo.InvariantCulture) + " m, " + (MechFeatures.Pushes - p0) + " pushes)");
					PlayerMove.To(me, obj(crate1).transform.position + new Vector3(1.5f, 1.2f, 0f));
					yield return new WaitForSeconds(1.5f);
				}

				// Carry: two crates onto the stack target
				TriggerZone sz = obj(stack).GetComponent<TriggerZone>();
				foreach (int crate in new[] { crate1, crate2 })
				{
					IslandObjectRef cr = obj(crate);
					PutPlayerNear(cr.transform);
					yield return new WaitForSeconds(0.5f);
					Behaviours.Fire(e, crate, "use", true);
					yield return new WaitForSeconds(0.3f);
					Check(ref ok, MechFeatures.CarryingLocal && MechFeatures.CarriedName == "Crate", "the player carries crate #" + crate);
					bool jump = Traverse.Create(me.PersonController).Method("AllowJump").GetValue<bool>();
					Check(ref ok, !jump, "no jumping while carrying");
					Check(ref ok, (cr.transform.position - me.transform.position).magnitude < 2f, "the crate goes with the player");
					// (stand 1.3 m behind the zone's middle, facing it: the crate goes down in front)
					Vector3 zc = sz.transform.position + new Vector3(crate == crate1 ? 0.4f : -0.4f, 0f, 0f);
					PlayerMove.To(me, zc + new Vector3(0f, 1.2f, -1.3f));
					me.transform.rotation = Quaternion.LookRotation(Vector3.forward);
					yield return new WaitForSeconds(0.5f);
					MechFeatures.DropMine();
					Vector3 local; float yaw;
					Check(ref ok, !MechFeatures.CarryingLocal && MechFeatures.Saved(e, crate, out local, out yaw), "put down; its place is saved");
					Check(ref ok, (cr.transform.position - sz.transform.position).magnitude <= sz.Radius, "the crate stands in the stack zone (" + (cr.transform.position - sz.transform.position).magnitude.ToString("F1", CultureInfo.InvariantCulture) + " m)");
				}
				float t0 = Time.realtimeSinceStartup;
				while (!e.State.ContainsKey(Behaviours.SignalKey("mech-stacked")) && Time.realtimeSinceStartup - t0 < 6f) yield return new WaitForSeconds(0.25f);
				Check(ref ok, e.State.ContainsKey(Behaviours.SignalKey("mech-stacked")) && e.State.ContainsKey(MechFeatures.StackDoneBase + stack), "two crates stacked: the zone's stack event ran (its signal) once");

				// Lure: a plank in the trough, the warthog comes; the boss's shieldIf holds while the trough is open
				CreatureSpawnPoint sp = obj(goats).GetComponent<CreatureSpawnPoint>();
				Check(ref ok, sp != null && !MechFeatures.ShieldHolds(e, sp), "the warthog isn't guarded while the trough is closed");
				PutPlayerNear(obj(trough).transform);
				yield return new WaitForSeconds(0.5f);
				me.Inventory.AddItem("Plank", 1);
				int planks = me.Inventory.GetItemCount("Plank");
				Behaviours.Fire(e, trough, "use", true);
				bool vis, open;
				Behaviours.StateNow(e, obj(trough), out vis, out open);
				Check(ref ok, open && me.Inventory.GetItemCount("Plank") == planks - 1, "the plank goes in, the trough opens");
				Check(ref ok, sp != null && MechFeatures.ShieldHolds(e, sp), "boss.shieldIf=state|Trough|open holds now");
				t0 = Time.realtimeSinceStartup;
				while ((sp == null || sp.Spawned.Count == 0 || MechFeatures.LuredNow == 0) && Time.realtimeSinceStartup - t0 < 30f) yield return new WaitForSeconds(0.5f);
				AI_State_CirculateSpawn st = sp != null && sp.Spawned.Count > 0 ? sp.Spawned[0].GetComponentInChildren<AI_State_CirculateSpawn>(true) : null;
				Check(ref ok, MechFeatures.LuredNow > 0 && st != null && (st.SpawnPointPosition - obj(trough).transform.position).magnitude < 0.5f, "the warthog's home is the trough now");
				if (sp != null && sp.Spawned.Count > 0)
				{
					t0 = Time.realtimeSinceStartup;
					while ((sp.Spawned[0].transform.position - obj(trough).transform.position).magnitude > 4f && Time.realtimeSinceStartup - t0 < 25f) yield return new WaitForSeconds(0.5f);
					Check(ref ok, (sp.Spawned[0].transform.position - obj(trough).transform.position).magnitude <= 4f, "the warthog walked to the trough (" + (sp.Spawned[0].transform.position - obj(trough).transform.position).magnitude.ToString("F1", CultureInfo.InvariantCulture) + " m)");
				}

				// Collapse and moveto
				int col0 = MechFeatures.Collapses;
				Behaviours.Fire(e, lever1, "use", true);
				Behaviours.StateNow(e, obj(bridge), out vis, out open);
				Check(ref ok, MechFeatures.Collapses == col0 + 1 && !vis, "collapse|Bridge|1: it falls (hidden in the state at once)");
				Vector3 c0 = obj(cart).transform.position;
				Behaviours.Fire(e, lever2, "use", true);
				yield return new WaitForSeconds(1.6f);
				Check(ref ok, !obj(bridge).gameObject.activeSelf, "the bridge is gone after its second");
				Vector3 ledge = Behaviours.TargetsOf(e, lever2, "Ledge").First().transform.position;
				Vector3 cl; float cy;
				Check(ref ok, (obj(cart).transform.position - ledge).magnitude < 0.2f && MechFeatures.Saved(e, cart, out cl, out cy), "moveto|Cart|Ledge,1: the cart is at the ledge (moved " + (obj(cart).transform.position - c0).magnitude.ToString("F1", CultureInfo.InvariantCulture) + " m), saved");
				Screenshot(new[] { "mech_run" });
			}
			finally
			{
				MechFeatures.Reset();
				IslandWorldState.RemoveIds(IslandWorldState.Islands.Skip(before).Select(x => x.Id).ToList(), true);
				if (File.Exists(IslandSpawner.PathFor(MechIsland))) File.Delete(IslandSpawner.PathFor(MechIsland));
			}
			if (ok) Log("PASS: quest mechanics in a world"); else Fail("quest mechanics in a world");
		}
	}
}
