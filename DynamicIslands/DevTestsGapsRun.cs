using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// The gap features of 2026-10-24 in a world (_ProjectDocs/plans/storylines10/GAPS_TESTS.md blocks 1-11, single
	/// player): CIGapsRun makes a test island near the raft with a hazard zone, a deep-water zone, a generator, a key
	/// socket, a snow vehicle, a binoculars lookout, a lever, a repel light, a butler bot and a shielded boss with two
	/// barrels, and checks each block. The two-player rows and the editor's screens are not covered here.
	/// </summary>
	public static partial class DevTests
	{
		const string GrIsland = "cigapsrun";

		static IslandObject GrZone(Vector3 at, string id, float r, params string[] kv)
		{
			var l = new List<string> { ObjectProps.ZoneId, id, ObjectProps.ZoneRadius, r.ToString(System.Globalization.CultureInfo.InvariantCulture), BehaviourProps.Name, id };
			l.AddRange(kv);
			return ScObj(ContentCatalog.TriggerZone, at, l.ToArray());
		}

		static float GrHealth(Network_Player p) { return p.Stats.stat_health.Value; }
		static void GrHeal(Network_Player p)
		{
			// (food's bonus health soaks up damage, and an empty belly hurts: both cleared, as CvBite does)
			KeepAlive(p);
			try { p.Stats.stat_BonusHealth.statTarget.Value = 0f; p.Stats.stat_BonusHealth.Value = 0f; } catch { }
		}

		static TriggerZone GrZoneOf(IslandWorldState.Entry e, string id)
		{
			return e.Root.GetComponentsInChildren<TriggerZone>(true).FirstOrDefault(z => string.Equals(z.Id, id, StringComparison.OrdinalIgnoreCase));
		}

		static void GrSetZone(TriggerZone z, params string[] kv)
		{
			foreach (string k in new[] { Hazards.Kind, Hazards.Damage, Hazards.Safe }) z.Props.Remove(k);
			for (int i = 0; i + 1 < kv.Length; i += 2) z.Props[kv[i]] = kv[i + 1];
			z.Hazard = Hazards.KindOf(z.Props);
		}

		/// <summary>Stands the player in the zone for some seconds (put back every half second) and gives the health lost.</summary>
		static IEnumerator GrStand(Network_Player p, Transform at, float seconds, Action<float> lost)
		{
			GrHeal(p);
			float h0 = GrHealth(p);
			for (float t = 0; t < seconds; t += 0.5f) { PutPlayerNear(at, 0.3f); yield return new WaitForSeconds(0.5f); }
			lost(h0 - GrHealth(p));
		}

		/// <summary>Puts a worn item on or off as play does: in (or out of) one of the inventory's equip slots, which fills
		/// PlayerEquipment's worn list that wears| reads.</summary>
		static bool GrWear(Network_Player p, string item, bool on)
		{
			Item_Base it = ItemManager.GetItemByName(item);
			if (it == null) { Log("  (no item " + item + ")"); return false; }
			try
			{
				var slots = (Traverse.Create(p.Inventory).Field("equipSlots").GetValue() as System.Collections.IEnumerable ?? new object[0]).OfType<Slot>().ToList();
				if (slots.Count == 0) { Log("  (no equip slots)"); return false; }
				if (!on)
				{
					foreach (Slot s in slots.Where(s => s.itemInstance != null && s.itemInstance.UniqueName == item)) s.Reset();
					return true;
				}
				if (GapChecks.Wears(item)) return true;
				foreach (Slot s in slots.Where(s => s.IsEmpty))
				{
					s.SetItem(new ItemInstance(it, 1, it.MaxUses));
					if (GapChecks.Wears(item)) return true;
					s.Reset();
				}
				Log("  (" + item + " would not go into any of " + slots.Count + " equip slots)");
			}
			catch (Exception ex) { Log("  (wearing " + item + ": " + (ex.InnerException ?? ex).Message + ")"); }
			return false;
		}

		/// <summary>Puts an item in the inventory if it is not there and takes it in hand (Raft's Hotbar.SelectItem).</summary>
		static bool GrHold(Network_Player p, string item)
		{
			if (item == null) return true;
			try
			{
				Item_Base it = ItemManager.GetItemByName(item);
				if (it == null) { Log("  (no item " + item + ")"); return false; }
				if (p.Inventory.GetItemCount(item) == 0) p.Inventory.AddItem(item, 1);
				Hotbar hotbar = Traverse.Create(p.Inventory).Field("hotbar").GetValue<Hotbar>();
				if (hotbar == null) { Log("  (no hotbar)"); return false; }
				hotbar.SelectItem(it);
				if (!GapChecks.Holds(item)) Log("  (took " + item + " in hand, but holds " + (p.Inventory.GetSelectedHotbarItem() != null ? p.Inventory.GetSelectedHotbarItem().UniqueName : "nothing") + ")");
				return true;
			}
			catch (Exception ex) { Log("  (holding " + item + ": " + (ex.InnerException ?? ex).Message + ")"); return false; }
		}
		static int GrCount(Network_Player p, string item) { try { return p.Inventory.GetItemCount(item); } catch { return -1; } }

		static void GrSetCount(Network_Player p, string item, int n)
		{
			int have = GrCount(p, item);
			if (have > n) p.Inventory.RemoveItem(item, have - n);
			else if (have < n) p.Inventory.AddItem(item, n - have);
		}

		static bool GrState(IslandWorldState.Entry e, string name, string state)
		{
			IslandObjectRef r = ScObjOf(e, name);
			ObjCheck c = ObjCheck.Parse("state|" + name + "|" + state);
			return r != null && c != null && Behaviours.CheckNow(e, r.Index, c);
		}

		[ConsoleCommand(name: "CIGapsRun", docs: "Dev, in game (host): the 10-24 gap features in a world (GAPS_TESTS.md, single player): hazard zones (damage, suit, safe=none, a lever hiding the zone, damage 0, a bad kind), darkness and the headlight, deep water and the oxygen tank, the 3-part generator, the key socket, the snow vehicle, binoculars/holds/wears, butler bot zaps, a repel light, a shielded boss hit by barrels, and the editor defaults")]
		public static void GapsRunCommand(string[] args) { StartTest(GapsRunRoutine()); }

		static IEnumerator GapsRunRoutine()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null || !CustomIslandSpawner.RaftPosition.HasValue || !Raft_Network.IsHost) { Fail("gaps run: in a world, as the host"); yield break; }
			bool ok = true;
			yield return EnsureAlive();
			yield return PlaceableCatalog.EnsureLoaded(new List<string> { "Generator", "TP_CoolingStation_Keysocket", "TP_SnowVehicle01", "TP_CoolingStation_Monitor_Lever", "VP_Spotlight", "VP_ExplosiveBarrel_Pile", "Creature_ButlerBot", "Creature_Boar", "Note_Sign" });

			// 001a46a: codes split or spelled out count as told; a code nowhere in the text does not
			var tells = AccessTools.Method(typeof(PlanChecker), "TellsCode");
			Func<string, string, bool> tc = (t, c) => (bool)tells.Invoke(null, new object[] { t, c });
			Check(ref ok, tc("The panel takes 4-3-1-2.", "4312") && tc("four one nine", "419") && !tc("The keeper never wrote it down.", "4312") && !tc("Dial 4321 for the bar.", "4312"),
				"plan checker codes: split/spelled-out told, untold and wrong codes still warn");

			// Block 11 (props side) and 4: what the editor and the recipes start with
			var ed = ObjectProps.Defaults("Generator");
			GapFeatures.EditorDefaults("Generator", ed);
			var old = ObjectProps.Defaults("Generator");
			Check(ref ok, ReadyPieces.PartsOf(ed) == 3 && ObjectProps.Get(ed, BehaviourProps.CheckKey("use")).Contains("take|story:raft-Tangaroa_GeneratorPart|3"), "editor generator wants 3 parts: " + ObjectProps.Get(ed, BehaviourProps.CheckKey("use")).Replace("\n", " / "));
			Check(ref ok, ReadyPieces.PartsOf(old) == 1, "recipe/old generator keeps 1 part (" + ReadyPieces.PartsOf(old) + ")");
			var one = new Dictionary<string, string>(ed);
			ReadyPieces.SetParts("Generator", one, 1);
			Check(ref ok, ObjectProps.Get(one, BehaviourProps.CheckKey("use")).Contains("|1"), "parts slider to 1 gives |1");
			var bot = ObjectProps.Defaults("Creature_ButlerBot");
			GapFeatures.EditorDefaults("Creature_ButlerBot", bot);
			Check(ref ok, GapFeatures.ZapOf(bot) == 8f, "editor butler bot: creature.zap=" + ObjectProps.Get(bot, GapFeatures.Zap));
			Check(ref ok, Hazards.KindOf(P(Hazards.Kind, "bogus")) == "" && Hazards.DamageOf(P(Hazards.Kind, "acid")) == 4f && Hazards.SafeOf(P(Hazards.Kind, "acid", Hazards.Safe, "none")) == "", "bogus kind = none, acid 4/s, safe=none = nothing");
			Check(ref ok, GapFeatures.RepelOf(ObjectProps.Defaults("VP_Spotlight")) == 8f, "spotlight ready: beh.repel=8");

			// The island
			IslandFile f = ScIsland(GrIsland, "Gaps Run");
			int seed = 1;
			Func<float, float, Vector3> at = (x, z) => ScDry(f, new Vector2(x, z), seed++);
			f.Objects.Add(GrZone(at(-20f, -20f), "HZ", 4f, Hazards.Kind, "acid"));
			Vector3? sea = ScUnderSea(f, 5f);
			if (sea.HasValue) f.Objects.Add(GrZone(sea.Value, "DeepZ", 6f, Hazards.Kind, "deep"));
			f.Objects.Add(GpObj("TP_CoolingStation_Monitor_Lever", at(-8f, -20f), "Lever", BehaviourProps.EventKey("use"), "hide|HZ"));
			var gen = ObjectProps.Defaults("Generator"); GapFeatures.EditorDefaults("Generator", gen); gen[BehaviourProps.Name] = "Gen";
			GameObject gp = PlaceableCatalog.Get("Generator");
			f.Objects.Add(new IslandObject { Name = "Generator", Position = at(4f, -20f), Scale = gp != null ? gp.transform.localScale : Vector3.one, Props = gen });
			f.Objects.Add(GpObj("TP_CoolingStation_Keysocket", at(16f, -20f), "Socket"));
			f.Objects.Add(GpObj("TP_SnowVehicle01", at(-20f, 4f), "Sled"));
			f.Objects.Add(ScObj("Note_Sign", at(28f, -20f), BehaviourProps.Name, "Wreck", BehaviourProps.Hidden, "1"));
			f.Objects.Add(GrZone(at(28f, -8f), "Look", 3f, BehaviourProps.CheckKey("enter"), "holds|Binoculars", BehaviourProps.EventKey("enter"), "show|Wreck", ObjectProps.ZoneRepeat, "1"));
			f.Objects.Add(ScObj("Note_Sign", at(16f, -8f), BehaviourProps.Name, "AxeVines", BehaviourProps.Use, "Cut", BehaviourProps.CheckKey("use"), "holds|Axe*", BehaviourProps.EventKey("use"), "hide|"));
			Vector3 lightAt = at(20f, 20f);
			f.Objects.Add(GpObj("VP_Spotlight", lightAt, "Light"));
			f.Objects.Add(ScObj("Creature_Boar", lightAt + new Vector3(3f, 0f, 0f), ObjectProps.CreatureCount, "2", ObjectProps.CreatureRespawn, "0", BehaviourProps.Name, "Pack"));
			f.Objects.Add(ScObj("Creature_ButlerBot", at(-20f, 25f), ObjectProps.CreatureCount, "1", ObjectProps.CreatureRespawn, "0", GapFeatures.Zap, "8", BehaviourProps.Name, "Bot"));
			Vector3 bossAt = at(-40f, -40f);
			f.Objects.Add(ScObj("Creature_Boar", bossAt, ObjectProps.CreatureCount, "1", ObjectProps.CreatureRespawn, "0", BossPhases.Share, "50", GapFeatures.Shield, "1", BehaviourProps.Name, "boss"));
			f.Objects.Add(GpObj("VP_ExplosiveBarrel_Pile", bossAt + new Vector3(6f, 0f, 0f), "Barrel1"));
			f.Objects.Add(GpObj("VP_ExplosiveBarrel_Pile", bossAt + new Vector3(-6f, 0f, 0f), "Barrel2"));
			IslandWorldState.Entry e = null;
			yield return RfSpawn(f, GrIsland, x => e = x);
			if (e == null) yield break;
			yield return new WaitForSeconds(3f);

			// 1. Hazard zones
			TriggerZone hz = GrZoneOf(e, "HZ");
			float lost = 0f;
			if (hz == null) Check(ref ok, false, "1: the hazard zone is there");
			else
			{
				Hazards.SuitUntil = 0f;
				yield return GrStand(player, hz.transform, 5f, x => lost = x);
				Check(ref ok, lost >= 12f && lost <= 28f, "1: acid 5 s: lost " + lost.ToString("0.#") + " (about 20)");
				Hazards.PutOnSuit("30");
				yield return GrStand(player, hz.transform, 5f, x => lost = x);
				Check(ref ok, lost < 1f && Hazards.SuitUntil > Time.time, "1: with the suit: lost " + lost.ToString("0.#"));
				GrSetZone(hz, Hazards.Kind, "acid", Hazards.Safe, "none");
				yield return GrStand(player, hz.transform, 3f, x => lost = x);
				Check(ref ok, lost >= 6f, "1: safe=none with the suit on: lost " + lost.ToString("0.#"));
				Hazards.SuitUntil = 0f;
				// (Raft's own hazmat suit, put on by the suit action, would keep protecting from the fumes: taken off)
				try { if (player.HazmatSuit != null && player.HazmatSuit.Equipped) player.HazmatSuit.UnEquip(); } catch (Exception ex) { Log("  (hazmat suit off: " + ex.Message + ")"); }
				Check(ref ok, !Hazards.SuitOn(player), "1: the suit is off again");
				GrSetZone(hz, Hazards.Kind, "acid", Hazards.Damage, "0");
				yield return GrStand(player, hz.transform, 3f, x => lost = x);
				Check(ref ok, lost < 1f, "1: damage 0: lost " + lost.ToString("0.#"));
				foreach (string k in new[] { "spikes", "cold", "miasma" })
				{
					GrSetZone(hz, Hazards.Kind, k);
					yield return GrStand(player, hz.transform, 3f, x => lost = x);
					Check(ref ok, lost > 1f, "1: " + k + " 3 s: lost " + lost.ToString("0.#"));
				}
				GrSetZone(hz, Hazards.Kind, "shock");
				yield return GrStand(player, hz.transform, 3f, x => lost = x);
				Check(ref ok, lost >= 6f, "1: shock 3 s: lost " + lost.ToString("0.#"));
				ScUse(e, "Lever");
				yield return new WaitForSeconds(1f);
				yield return GrStand(player, hz.transform, 3f, x => lost = x);
				Check(ref ok, lost < 1f && !hz.gameObject.activeInHierarchy, "1: the lever hid the shock zone: lost " + lost.ToString("0.#"));
				Behaviours.Fire(e, ScObjOf(e, "Lever").Index, "use", true);
				yield return new WaitForSeconds(0.5f);
			}

			// 2. Darkness and the headlight (the zone shown again for it)
			if (hz != null)
			{
				hz.gameObject.SetActive(true);
				GrSetZone(hz, Hazards.Kind, "dark");
				GrWear(player, "HeadLight", false);
				for (int i = 0; i < 4; i++) { PutPlayerNear(hz.transform, 0.3f); yield return new WaitForSeconds(0.5f); }
				Check(ref ok, DarknessOverlay.Dark && !DarknessOverlay.Lit, "2: in the dark without a light: Dark " + DarknessOverlay.Dark);
				bool hl = GrWear(player, "HeadLight", true);
				for (int i = 0; i < 4; i++) { PutPlayerNear(hz.transform, 0.3f); yield return new WaitForSeconds(0.5f); }
				Check(ref ok, hl && GapChecks.Wears("HeadLight*") && DarknessOverlay.Lit && !DarknessOverlay.Dark, "2: with the headlight: wears " + GapChecks.Wears("HeadLight*") + ", Lit " + DarknessOverlay.Lit + ", Dark " + DarknessOverlay.Dark);
				GrWear(player, "HeadLight", false);
				PutPlayerNear(ScObjOf(e, "Socket").transform);
				yield return new WaitForSeconds(0.8f);
				Check(ref ok, !DarknessOverlay.Dark && !DarknessOverlay.Lit, "2: walked out: both off");
				hz.gameObject.SetActive(false);
			}

			// 3. Deep water
			TriggerZone dz = GrZoneOf(e, "DeepZ");
			if (dz == null) Log("  (3: no sea spot deep enough on the test island: skipped)");
			else
			{
				GrWear(player, "OxygenBottle", false);
				yield return PutPlayer(player, dz.transform.position, true);
				player.Stats.stat_oxygen.Value = player.Stats.stat_oxygen.Max;
				float o0 = player.Stats.stat_oxygen.Value;
				for (int i = 0; i < 4; i++) { yield return PutPlayer(player, dz.transform.position, true); yield return new WaitForSeconds(0.5f); }
				float drop = o0 - player.Stats.stat_oxygen.Value;
				Check(ref ok, drop >= 15f, "3: deep water 2 s without a tank: breath " + o0.ToString("0") + " -> " + player.Stats.stat_oxygen.Value.ToString("0"));
				bool ob = GrWear(player, "OxygenBottle", true);
				player.Stats.stat_oxygen.Value = player.Stats.stat_oxygen.Max;
				o0 = player.Stats.stat_oxygen.Value;
				for (int i = 0; i < 4; i++) { yield return PutPlayer(player, dz.transform.position, true); yield return new WaitForSeconds(0.5f); }
				drop = o0 - player.Stats.stat_oxygen.Value;
				Check(ref ok, ob && drop < 8f, "3: with the oxygen tank: breath lost " + drop.ToString("0.#"));
				GrWear(player, "OxygenBottle", false);
				player.Stats.stat_oxygen.Value = player.Stats.stat_oxygen.Max;
			}

			// 4. Generator with 3 parts
			string partId = StoryItems.IdOf("story:raft-Tangaroa_GeneratorPart");
			Action<int> parts = n => { int have = StoryBook.Count(partId); if (have > n) StoryBook.Take(partId, have - n); else if (have < n) StoryBook.Give(partId, n - have); };
			parts(2);
			ScUse(e, "Gen");
			yield return new WaitForSeconds(1f);
			Check(ref ok, !Behaviours.HasSignal(e, "power") && StoryBook.Count(partId) == 2, "4: 2 parts: refused, kept " + StoryBook.Count(partId));
			parts(3);
			ScUse(e, "Gen");
			yield return new WaitForSeconds(1f);
			Check(ref ok, Behaviours.HasSignal(e, "power") && StoryBook.Count(partId) == 0, "4: 3 parts: power " + Behaviours.HasSignal(e, "power") + ", parts left " + StoryBook.Count(partId));

			// 6. Key socket
			GrSetCount(player, "MetalIngot", 0);
			ScUse(e, "Socket");
			yield return new WaitForSeconds(0.5f);
			Check(ref ok, !GrState(e, "Socket", "open"), "6: empty-handed: the socket stays empty");
			GrSetCount(player, "MetalIngot", 2);
			ScUse(e, "Socket");
			yield return new WaitForSeconds(0.5f);
			Check(ref ok, GrState(e, "Socket", "open") && GrCount(player, "MetalIngot") == 1 && Behaviours.HasSignal(e, "socket"), "6: with an ingot: open, ingots left " + GrCount(player, "MetalIngot") + ", socket signal " + Behaviours.HasSignal(e, "socket"));
			ScUse(e, "Socket");
			yield return new WaitForSeconds(0.5f);
			Check(ref ok, GrCount(player, "MetalIngot") == 1, "6: used again: nothing more taken (" + GrCount(player, "MetalIngot") + ")");

			// 10. Snow vehicle
			IslandObjectRef sled = ScObjOf(e, "Sled");
			if (sled == null) Check(ref ok, false, "10: the snow vehicle is there");
			else
			{
				Vector3 s0 = sled.transform.position;
				GrSetCount(player, "Bolt", 2);
				ScUse(e, "Sled");
				yield return new WaitForSeconds(1f);
				Check(ref ok, GrCount(player, "Bolt") == 2 && !Behaviours.HasSignal(e, "vehicle"), "10: 2 bolts: refused");
				GrSetCount(player, "Bolt", 3);
				ScUse(e, "Sled");
				yield return new WaitForSeconds(11f);
				float moved = (sled.transform.position - s0).magnitude;
				Check(ref ok, GrCount(player, "Bolt") == 0 && Behaviours.HasSignal(e, "vehicle") && moved > 20f, "10: 3 bolts: taken, moved " + moved.ToString("0.#") + " m");
				ScUse(e, "Sled");
				yield return new WaitForSeconds(11f);
				Check(ref ok, (sled.transform.position - s0).magnitude < 3f, "10: used again without bolts: back (" + (sled.transform.position - s0).magnitude.ToString("0.#") + " m from the start)");
			}

			// 9. Binoculars, holds, wears
			GrHold(player, null);
			TriggerZone look = GrZoneOf(e, "Look");
			if (look != null)
			{
				PutPlayerNear(ScObjOf(e, "Lever").transform);
				yield return new WaitForSeconds(0.6f);
				for (int i = 0; i < 3; i++) { PutPlayerNear(look.transform, 0.3f); yield return new WaitForSeconds(0.5f); }
				Check(ref ok, !ScObjOf(e, "Wreck").gameObject.activeInHierarchy, "9: empty-handed in the lookout: the wreck stays hidden");
				bool held = GrHold(player, "Binoculars");
				PutPlayerNear(ScObjOf(e, "Lever").transform);
				yield return new WaitForSeconds(0.6f);
				for (int i = 0; i < 3; i++) { PutPlayerNear(look.transform, 0.3f); yield return new WaitForSeconds(0.5f); }
				Check(ref ok, held && GapChecks.Holds("Binoculars") && ScObjOf(e, "Wreck").gameObject.activeInHierarchy, "9: holding binoculars: holds " + GapChecks.Holds("Binoculars") + ", the wreck shows " + ScObjOf(e, "Wreck").gameObject.activeInHierarchy);
			}
			string hook = ItemManager.GetAllItems().Select(i => i.UniqueName).FirstOrDefault(n => n.StartsWith("Hook", StringComparison.OrdinalIgnoreCase));
			GrHold(player, hook);
			ScUse(e, "AxeVines");
			yield return new WaitForSeconds(0.5f);
			Check(ref ok, ScObjOf(e, "AxeVines").gameObject.activeInHierarchy, "9: with a hook (" + hook + ") the axe-only vines stay");
			string axe = ItemManager.GetAllItems().Select(i => i.UniqueName).FirstOrDefault(n => n.StartsWith("Axe", StringComparison.OrdinalIgnoreCase));
			GrHold(player, axe);
			ScUse(e, "AxeVines");
			yield return new WaitForSeconds(0.5f);
			Check(ref ok, !ScObjOf(e, "AxeVines").gameObject.activeInHierarchy, "9: with an axe (" + axe + ") they go");
			List<string> hooks = ItemManager.GetAllItems().Select(i => i.UniqueName).Where(n => n.StartsWith("Hook", StringComparison.OrdinalIgnoreCase) || n == "DI_MagnetHook").ToList();
			List<string> axes = ItemManager.GetAllItems().Select(i => i.UniqueName).Where(n => n.StartsWith("Axe", StringComparison.OrdinalIgnoreCase)).ToList();
			Check(ref ok, hooks.All(n => GapChecks.NameMatches(n, "Hook*")) && axes.Count >= 2 && GapChecks.NameMatches("DI_SwiftFlippers", "Flipper*") && GapChecks.NameMatches("DI_Telescope", "Binoculars") && !GapChecks.NameMatches("Hook_Plastic", "Axe*"),
				"9: name matching: hooks " + string.Join(",", hooks.ToArray()) + "; axes " + string.Join(",", axes.ToArray()) + "; swift flippers = Flipper*, telescope = Binoculars");
			GrHold(player, null);

			// 7. Butler bot zaps
			yield return ScWaitAnimals(e, "Butler bot", 1, 10f);
			CreatureSpawnPoint botSpot = ScObjOf(e, "Bot") != null ? ScObjOf(e, "Bot").GetComponent<CreatureSpawnPoint>() : null;
			AI_NetworkBehaviour b = botSpot != null ? botSpot.Spawned.FirstOrDefault(a => a != null) : null;
			if (b == null) Check(ref ok, false, "7: a butler bot came");
			else
			{
				int z0 = GapFeatures.Zaps;
				GrHeal(player);
				float h0 = GrHealth(player);
				for (float t = 0; t < 5f; t += 0.25f)
				{
					int zn = GapFeatures.Zaps;
					float hb = GrHealth(player);
					PutPlayerNear(b.transform, 1f);
					yield return new WaitForSeconds(0.25f);
					if (GapFeatures.Zaps > zn) Log("  (zap: health " + hb.ToString("0.#") + " -> " + GrHealth(player).ToString("0.#") + ", bonus " + player.Stats.stat_BonusHealth.Value.ToString("0.#") + ", suit " + Hazards.SuitOn(player) + ")");
				}
				int zaps = GapFeatures.Zaps - z0;
				Check(ref ok, zaps >= 2 && zaps <= 3 && h0 - GrHealth(player) >= 14f, "7: 5 s by the bot: " + zaps + " zaps, lost " + (h0 - GrHealth(player)).ToString("0.#"));
			}

			// 8. The repel light
			yield return EnsureAlive();
			CreatureSpawnPoint pack = ScObjOf(e, "Pack") != null ? ScObjOf(e, "Pack").GetComponent<CreatureSpawnPoint>() : null;
			for (float t = 0; t < 10f && (pack == null || pack.Spawned.Count(a => a != null) < 1); t += 0.5f) yield return new WaitForSeconds(0.5f);
			IslandObjectRef light = ScObjOf(e, "Light");
			if (pack == null || light == null) Check(ref ok, false, "8: the light and its boars are there");
			else
			{
				int p0 = GapFeatures.Pushes;
				foreach (AI_NetworkBehaviour a in pack.Spawned.Where(a => a != null)) a.transform.position = light.transform.position + new Vector3(3f, 0.5f, 0f);
				yield return new WaitForSeconds(1.5f);
				float near = pack.Spawned.Where(a => a != null).Select(a => { Vector3 d = a.transform.position - light.transform.position; d.y = 0f; return d.magnitude; }).DefaultIfEmpty(0f).Min();
				Check(ref ok, near >= 7.9f && GapFeatures.Pushes > p0, "8: boars put 3 m from the light: nearest now " + near.ToString("0.#") + " m, pushes +" + (GapFeatures.Pushes - p0));
				light.gameObject.SetActive(false);
				yield return new WaitForSeconds(0.6f);
				int p1 = GapFeatures.Pushes;
				foreach (AI_NetworkBehaviour a in pack.Spawned.Where(a => a != null)) a.transform.position = light.transform.position + new Vector3(3f, 0.5f, 0f);
				yield return new WaitForSeconds(1.5f);
				Check(ref ok, GapFeatures.Pushes == p1, "8: light hidden: no pushes");
			}

			// 5. The shielded boss and the barrels
			yield return EnsureAlive();
			IslandObjectRef bossRef = ScObjOf(e, "boss");
			CreatureSpawnPoint boss = bossRef != null ? bossRef.GetComponent<CreatureSpawnPoint>() : null;
			AI_NetworkBehaviour ba = boss != null ? boss.Spawned.FirstOrDefault(a => a != null) : null;
			Network_Host host = UnityEngine.Object.FindObjectOfType<Network_Host>();
			if (ba == null || host == null) Check(ref ok, false, "5: the boss came");
			else
			{
				Network_Entity be = ba.networkEntity;
				float max = be.stat_health.Max;
				host.DamageEntity(be, ba.transform, max * 0.55f, ba.transform.position + Vector3.up, Vector3.up, EntityType.Player, null);
				yield return new WaitForSeconds(1f);
				Check(ref ok, BossPhases.InPhase(e, boss), "5: hit to 45%: the phase started");
				float h1 = be.stat_health.Value;
				host.DamageEntity(be, ba.transform, max * 0.1f, ba.transform.position + Vector3.up, Vector3.up, EntityType.Player, null);
				yield return new WaitForSeconds(0.5f);
				Check(ref ok, Mathf.Abs(be.stat_health.Value - h1) < 0.01f, "5: a weapon in the phase glances off: " + h1.ToString("0.#") + " -> " + be.stat_health.Value.ToString("0.#"));
				int hits0 = GapFeatures.Hits;
				ScUse(e, "Barrel1");
				yield return new WaitForSeconds(1f);
				Check(ref ok, GapFeatures.Hits == hits0 + 1 && be.stat_health.Value < h1 - max * 0.3f && !ScObjOf(e, "Barrel1").gameObject.activeInHierarchy && Behaviours.HasSignal(e, "blast"),
					"5: a barrel: hits +" + (GapFeatures.Hits - hits0) + ", health " + h1.ToString("0.#") + " -> " + be.stat_health.Value.ToString("0.#") + ", barrel hidden, blast sent");
				ScUse(e, "Barrel2");
				yield return new WaitForSeconds(1f);
				Check(ref ok, be.IsDead || be.stat_health.Value <= 0f, "5: the second barrel kills it (" + be.stat_health.Value.ToString("0.#") + ")");
			}

			yield return EnsureAlive();
			IslandWorldState.Remove(GrIsland);
			if (ok) Log("PASS: gaps run"); else Fail("gaps run");
		}
	}
}
