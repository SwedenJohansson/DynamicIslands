using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DynamicIslands.Editor;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>11-12 player report: the long paddle, titanium greatsword, titanium machete and titanium spear did nothing and
	/// the magnet hook never threw. Each hand-held upgrade is used here the way a player uses it: taken in hand, the left
	/// mouse button faked (Raft's CustomInputConfig and InputAction.ReadValue calls in the tools' own code swapped for
	/// test ones), and what Raft's animation events reach and do is checked - next to the base item as a control.</summary>
	public static partial class DevTests
	{
		const string CuHarmonyId = "ci.craftuse";

		// Fake left mouse button: held or not, and the frame its last change shows as pressed/released this frame
		static bool cuFake, cuDown;
		static int cuChangeFrame = -10;
		static void CuSet(bool down) { cuDown = down; cuChangeFrame = Time.frameCount + 1; }

		static MethodInfo cuIsPressed, cuWasPressed, cuWasReleased;
		static readonly Dictionary<MethodInfo, MethodInfo> cuReadValue = new Dictionary<MethodInfo, MethodInfo>();
		static MethodInfo CuInput(string name) => typeof(CustomInputConfig).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
			.FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(string));

		// the ids Raft asked for while faking, and the held item's own use button (UsableTool asks for that one, not "LMB")
		static readonly HashSet<string> cuIds = new HashSet<string>();
		static string cuUseId;
		static bool CuIsUse(string id) { if (cuFake && id != null) cuIds.Add(id); return id == "LMB" || (cuUseId != null && id == cuUseId); }
		static bool CuIsPressedShim(object cfg, object action, string id)
		{
			if (cuFake && CuIsUse(id)) return cuDown;
			if (cuFake && id == "RMB") return false;
			return (bool)cuIsPressed.Invoke(cfg, new[] { action, id });
		}
		static bool CuWasPressedShim(object cfg, object action, string id)
		{
			if (cuFake && CuIsUse(id)) { bool r = cuDown && Time.frameCount == cuChangeFrame; if (r) cuPressTrue++; return r; }
			if (cuFake && id == "RMB") return false;
			return (bool)cuWasPressed.Invoke(cfg, new[] { action, id });
		}
		static bool CuWasReleasedShim(object cfg, object action, string id)
		{
			if (cuFake && CuIsUse(id)) return !cuDown && Time.frameCount == cuChangeFrame;
			if (cuFake && id == "RMB") return false;
			return (bool)cuWasReleased.Invoke(cfg, new[] { action, id });
		}
		// (diagnostics: Update calls on the watched tool, and pressed-this-frame answers given)
		static int cuPressTrue, cuUpdCalls; static object cuWatch;
		static void CuUpdProbe(object __instance) { if (cuFake && __instance != null && ReferenceEquals(__instance, cuWatch)) cuUpdCalls++; }
		static MethodInfo cuReadValueOriginal;
		static float CuReadValueShim(object action)
		{
			if (cuFake) return cuDown ? 1f : 0f;
			return cuReadValueOriginal != null ? (float)cuReadValueOriginal.Invoke(action, null) : 0f;
		}

		static bool CuIsReadValue(object operand) => operand is MethodInfo mi && mi.Name == "ReadValue" && mi.ReturnType == typeof(float) && mi.DeclaringType != null && mi.DeclaringType.Name == "InputAction";

		static IEnumerable<CodeInstruction> CuInputTranspiler(IEnumerable<CodeInstruction> code)
		{
			foreach (CodeInstruction c in code)
			{
				if (c.opcode == OpCodes.Callvirt || c.opcode == OpCodes.Call)
				{
					MethodInfo shim = null;
					if (Equals(c.operand, cuIsPressed)) shim = AccessTools.Method(typeof(DevTests), "CuIsPressedShim");
					else if (Equals(c.operand, cuWasPressed)) shim = AccessTools.Method(typeof(DevTests), "CuWasPressedShim");
					else if (Equals(c.operand, cuWasReleased)) shim = AccessTools.Method(typeof(DevTests), "CuWasReleasedShim");
					else if (CuIsReadValue(c.operand)) { cuReadValueOriginal = (MethodInfo)c.operand; shim = AccessTools.Method(typeof(DevTests), "CuReadValueShim"); }
					if (shim != null) { c.opcode = OpCodes.Call; c.operand = shim; }
				}
				yield return c;
			}
		}

		// What Raft's animation events (and the paddle's push) reached: the method and the component it ran on
		static readonly List<KeyValuePair<string, Component>> cuHits = new List<KeyValuePair<string, Component>>();
		static readonly List<float> cuPaddleForces = new List<float>();
		static void CuProbe(object __instance, MethodBase __originalMethod)
		{
			if (__instance is Component c) cuHits.Add(new KeyValuePair<string, Component>(__originalMethod.Name, c));
		}
		static void CuPaddleProbe(object __instance, object[] __args)
		{
			if (__args != null && __args.Length >= 3 && __args[2] is float f) cuPaddleForces.Add(f);
			CuProbe(__instance, AccessTools.Method(typeof(Paddle), "PaddlePaddle"));
		}

		static readonly Type[] CuToolTypes = { typeof(UsableTool), typeof(Throwable), typeof(Hook), typeof(FishingRod), typeof(Axe), typeof(Hammer),
			typeof(ThrowableComponent), typeof(ThrowableComponent_Bow), typeof(Binoculars), typeof(Paddle), typeof(MeleeWeapon), typeof(Machete) };
		static readonly string[] CuEventMethods = { "MeleeWeapon.OnMeleeStart", "MeleeWeapon.OnMeleeHit", "Paddle.OnPaddle", "Throwable.Animation_HookThrow",
			"Axe.OnAxeStart", "Axe.OnAxeHit", "Hammer.OnHammerStart", "ThrowableComponent.CallStartChargeEvent", "ThrowableComponent.CallThrowEvent",
			"ThrowableComponent.Throw", "FishingRod.OnThrow", "Hook.OnThrow" };

		static List<MethodBase> CuPatch(Harmony harmony)
		{
			var done = new List<MethodBase>();
			cuIsPressed = CuInput("IsPressed"); cuWasPressed = CuInput("WasPressedThisFrame"); cuWasReleased = CuInput("WasReleasedThisFrame");
			if (cuIsPressed == null || cuWasPressed == null || cuWasReleased == null) { Log("  (CustomInputConfig input methods not found)"); return done; }
			var tr = new HarmonyMethod(AccessTools.Method(typeof(DevTests), "CuInputTranspiler"));
			foreach (Type t in CuToolTypes)
				foreach (MethodInfo m in AccessTools.GetDeclaredMethods(t))
				{
					if (m.IsAbstract || m.ContainsGenericParameters) continue;
					bool uses;
					try { uses = PatchProcessor.ReadMethodBody(m).Any(kv => kv.Value != null && (Equals(kv.Value, cuIsPressed) || Equals(kv.Value, cuWasPressed) || Equals(kv.Value, cuWasReleased) || CuIsReadValue(kv.Value))); }
					catch { uses = false; }
					if (!uses) continue;
					try { harmony.Patch(m, transpiler: tr); done.Add(m); } catch (Exception e) { Log("  (input patch on " + t.Name + "." + m.Name + ": " + e.Message + ")"); }
				}
			var probe = new HarmonyMethod(AccessTools.Method(typeof(DevTests), "CuProbe"));
			foreach (string em in CuEventMethods)
			{
				string[] p = em.Split('.');
				Type t = AccessTools.TypeByName(p[0]);
				List<MethodInfo> ms = t != null ? AccessTools.GetDeclaredMethods(t).Where(x => x.Name == p[1] && !x.IsAbstract).ToList() : new List<MethodInfo>();
				if (ms.Count == 0) { Log("  (no " + em + ")"); continue; }
				foreach (MethodInfo m in ms)
					try { harmony.Patch(m, postfix: probe); done.Add(m); } catch (Exception e) { Log("  (probe on " + em + ": " + e.Message + ")"); }
			}
			MethodInfo ut = AccessTools.Method(typeof(UsableTool), "Update");
			if (ut != null) { harmony.Patch(ut, postfix: new HarmonyMethod(AccessTools.Method(typeof(DevTests), "CuUpdProbe"))); done.Add(ut); }
			MethodInfo pp = AccessTools.Method(typeof(Paddle), "PaddlePaddle");
			if (pp != null) { harmony.Patch(pp, postfix: new HarmonyMethod(AccessTools.Method(typeof(DevTests), "CuPaddleProbe"))); done.Add(pp); }
			Log("  (patched " + done.Count + " methods: " + string.Join(", ", done.Select(m => m.DeclaringType.Name + "." + m.Name).ToArray()) + ")");
			return done;
		}

		/// <summary>The hand model in the local player's UseItemController for an item.</summary>
		static GameObject CuHandModel(Network_Player p, string item)
		{
			UseItemController c = p.GetComponentInChildren<UseItemController>(true);
			var list = c != null ? Traverse.Create(c).Field("allConnections").GetValue<List<ItemConnection>>() : null;
			ItemConnection k = list?.FirstOrDefault(x => x != null && x.inventoryItem != null && x.obj != null && x.inventoryItem.UniqueName == item);
			return k?.obj;
		}

		/// <summary>Animation event connections in the player whose receiver is the hand model or under it.</summary>
		static List<AnimationConnection> CuEvents(Network_Player p, GameObject model)
		{
			var r = new List<AnimationConnection>();
			if (model == null) return r;
			foreach (AnimationEventCaller caller in p.GetComponentsInChildren<AnimationEventCaller>(true))
			{
				var cons = Traverse.Create(caller).Field("connections").GetValue<List<AnimationConnection>>();
				if (cons != null) r.AddRange(cons.Where(a => a != null && a.objectReciever != null && (a.objectReciever == model || a.objectReciever.transform.IsChildOf(model.transform))));
			}
			return r;
		}

		static List<string> CuHitsOn(GameObject model) => cuHits.Where(h => h.Value != null && model != null && h.Value.transform.IsChildOf(model.transform)).Select(h => h.Key).ToList();

		static int CuInventoryTotal(Network_Player p) => p.Inventory.allSlots.Where(s => s != null && !s.IsEmpty).Sum(s => s.itemInstance.Amount);

		static void CuUse(string item)
		{
			Item_Base it = ItemManager.GetItemByName(item);
			cuUseId = it != null && it.settings_usable != null ? it.settings_usable.GetUseButtonName() : null;
		}

		// the state a swing meets: item in hand, Raft's menu, the animator's ItemHit and clips (why a swing makes no hit events)
		static string CuDiag(Network_Player p)
		{
			try
			{
				Animator a = p.Animator != null ? p.Animator.anim : null;
				Item_Base inHand = p.PlayerItemManager.useItemController.GetCurrentItemInHand();
				string st = "";
				if (a != null) for (int l = 0; l < a.layerCount; l++) { AnimatorClipInfo[] ci = a.GetCurrentAnimatorClipInfo(l); if (ci.Length > 0) st += l + ":" + ci[0].clip.name + " "; }
				return "in hand " + (inHand != null ? inHand.UniqueName : "none") + ", menu " + CanvasHelper.ActiveMenu + ", ItemHit " + (a != null && a.GetBool("ItemHit")) + ", culling " + (a != null ? a.cullingMode.ToString() : "-")
					+ ", animator on " + (a != null && a.isActiveAndEnabled) + ", speed " + (a != null ? a.speed.ToString("F1") : "-") + ", clips " + st;
			}
			catch (Exception e) { return "diag: " + e.Message; }
		}

		static IEnumerator CuClick(float hold, Action each = null)
		{
			CuSet(true);
			for (float t = 0; t < hold; t += Time.deltaTime) { each?.Invoke(); yield return null; }
			CuSet(false);
			yield return null;
		}

		static IEnumerator CuWait(float seconds, Action each = null)
		{
			for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < seconds; ) { each?.Invoke(); yield return null; }
		}

		[ConsoleCommand(name: "CICraftUse", docs: "Dev, in a 'CI ...' world on a raft, host: every hand-held extra upgrade (and its base item as a control) used as a player uses it, the left mouse button faked: (1) its hand model has the base item's animation events; (2) weapons (greatsword, spear, machete) swung at a warthog held in front of the view must hit it and take health (x1.5 the base's; the machete note says if Raft's machete hurts creatures); (3) the long paddle's stroke pushes the raft with twice the force; (4) the magnet hook thrown out lands in the water and pulls floating debris in; (5) the rod, longbow, hammer and axe get their throw/swing events. Optional arg: one item name")]
		public static void CraftUseCommand(string[] args) { StartTest(CraftUseRoutine(args != null && args.Length > 0 ? args[0] : null)); }

		static IEnumerator CraftUseRoutine(string only)
		{
			bool ok = true;
			Network_Player player = RAPI.GetLocalPlayer();
			string world = SaveAndLoad.CurrentGameFileName;
			if (player == null || !Raft_Network.IsHost || world == null || !world.StartsWith("CI ")) { Fail("craft use: run in a 'CI ...' world, as the host"); yield break; }
			yield return EnsureAlive();
			Log("  " + ExtraUpgrades.MacheteNote);

			var harmony = new Harmony(CuHarmonyId);
			List<MethodBase> patched = null;
			AI_NetworkBehaviour boar = null;
			var held = ExtraUpgrades.Registered.Where(u => u.Held != null && (only == null || u.Name == only || u.BaseName == only)).ToList();
			try
			{
				patched = CuPatch(harmony);
				cuFake = true; cuDown = false;

				// (1) animation events copied: every held upgrade's hand model gets as many as its base item's
				foreach (ExtraUpgrades.Upgrade u in held)
				{
					GameObject mine = CuHandModel(player, u.Name), theirs = CuHandModel(player, u.BaseName);
					List<AnimationConnection> em = CuEvents(player, mine), eb = CuEvents(player, theirs);
					string fm = string.Join(",", em.Select(a => a.functionName).OrderBy(x => x).ToArray()), fb = string.Join(",", eb.Select(a => a.functionName).OrderBy(x => x).ToArray());
					Check(ref ok, mine != null && em.Count == eb.Count && fm == fb, u.Name + ": hand model " + (mine != null ? mine.name : "MISSING") + " has " + em.Count + " animation event(s) [" + fm + "], " + u.BaseName + " " + eb.Count + " [" + fb + "]");
				}

				// (2) weapons on a warthog held in front of the view
				string[] weapons = { "DI_TitaniumGreatsword", "DI_TitaniumSpear", "DI_TitaniumMachete" };
				var weaponRuns = held.Where(u => weapons.Contains(u.Name)).ToList();
				if (weaponRuns.Count > 0)
				{
					Network_Host_Entities ents = ComponentManager<Network_Host_Entities>.Value;
					try { boar = ents.CreateAINetworkBehaviour(AI_NetworkBehaviourType.Boar, player.transform.position + player.transform.forward * 2f + Vector3.up * 0.5f, null); }
					catch (Exception e) { Log("  warthog not made: " + (e.InnerException ?? e).Message); }
					if (boar == null || boar.networkEntity == null) { Check(ref ok, false, "a warthog to hit"); }
					else
					{
						foreach (Rigidbody rb in boar.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
						boar.enabled = false;
						foreach (ExtraUpgrades.Upgrade u in weaponRuns)
						{
							float baseDrop = -1f, mineDrop = -1f;
							foreach (string item in new[] { u.BaseName, u.Name })
							{
								GrHold(player, item); CuUse(item);
								yield return new WaitForSeconds(0.8f);
								GameObject model = CuHandModel(player, item);
								MeleeWeapon w = model != null ? model.GetComponentInChildren<MeleeWeapon>(true) : null;
								float range = w != null ? Traverse.Create(w).Field("attackRange").GetValue<float>() : 2f;
								int dmg = w != null ? Traverse.Create(w).Field("damage").GetValue<int>() : -1;
								Action hold = () =>
								{
									KeepAlive(player);
									if (boar == null) return;
									Transform cam = player.CameraTransform;
									Vector3 fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
									if (fwd.sqrMagnitude < 0.01f) fwd = player.transform.forward;
									Collider col = boar.GetComponentsInChildren<Collider>().FirstOrDefault(x => x.enabled && !x.isTrigger);
									Vector3 off = col != null ? col.bounds.center - boar.transform.position : Vector3.up * 0.4f;
									Vector3 target = cam.position + fwd * Mathf.Clamp(range * 0.6f, 0.9f, 1.8f) + Vector3.down * 0.4f;
									boar.transform.position = target - off;
									LookAt(player, target);
								};
								cuWatch = w; cuUpdCalls = 0; cuPressTrue = 0;
								string wstate = w == null ? "no MeleeWeapon" : "setItemHit " + Traverse.Create(w).Field("setItemHitAnimation").GetValue<bool>() + ", enabled " + w.enabled + ", active " + w.gameObject.activeInHierarchy + ", thisItem " + (Traverse.Create(w).Field("thisItem").GetValue<Item_Base>() != null ? Traverse.Create(w).Field("thisItem").GetValue<Item_Base>().UniqueName : "null") + ", playerNetwork " + (Traverse.Create(w).Field("playerNetwork").GetValue<Network_Player>() != null);
								int hits0 = cuHits.Count;
								float best = 0f;
								string diag = null; int downFrames = 0;
								Action hold0 = hold;
								hold = () => { hold0(); if (cuDown && ++downFrames == 8 && diag == null) diag = CuDiag(player); };
								for (int swing = 0; swing < 3 && best <= 0f && boar != null && boar.networkEntity != null; swing++)
								{
									boar.networkEntity.stat_health.Value = boar.networkEntity.stat_health.Max;
									yield return CuWait(0.3f, hold);
									float hp0 = boar.networkEntity.stat_health.Value;
									yield return CuClick(0.4f, hold);
									yield return CuWait(1.6f, hold);
									if (boar != null && boar.networkEntity != null) best = Mathf.Max(best, hp0 - boar.networkEntity.stat_health.Value);
								}
								List<string> ev = cuHits.Skip(hits0).Where(h => h.Value != null && model != null && h.Value.transform.IsChildOf(model.transform)).Select(h => h.Key).ToList();
								Log("    weapon: " + wstate + "; its Update ran " + cuUpdCalls + "x, pressed-this-frame answered yes " + cuPressTrue + "x");
								Log("    swing: " + (diag ?? "no frame with the button down") + "; all events: " + string.Join(",", cuHits.Skip(hits0).Select(h => h.Key + "@" + (h.Value != null ? h.Value.name : "?")).Distinct().ToArray()));
								Log("  " + item + " (use button '" + cuUseId + "', damage " + dmg + ", range " + range.ToString("F1") + "): events " + string.Join(",", ev.ToArray()) + "; warthog lost " + best.ToString("F1") + " health");
								if (item == u.Name) mineDrop = best; else baseDrop = best;
								if (item == u.Name) Check(ref ok, ev.Contains("OnMeleeHit") && best > 0f, u.Name + " hits the warthog (OnMeleeHit on its own hand model) and takes " + best.ToString("F1") + " health");
							}
							if (u.Name == "DI_TitaniumMachete")
								Log("  CRAFTUSE MACHETE: Raft's machete took " + baseDrop.ToString("F1") + " health from the warthog (" + (baseDrop > 0f ? "it hurts creatures" : "it does not hurt creatures") + "); titanium " + mineDrop.ToString("F1"));
							else if (baseDrop > 0f && mineDrop > 0f)
								Check(ref ok, mineDrop >= baseDrop * 1.3f, u.Name + " hits harder than " + u.BaseName + ": " + mineDrop.ToString("F1") + " vs " + baseDrop.ToString("F1"));
						}
					}
				}

				// (3) the long paddle: its stroke reaches its own Paddle and pushes the raft with twice the force
				ExtraUpgrades.Upgrade pad = held.FirstOrDefault(u => u.Name == "DI_LongPaddle");
				if (pad != null)
				{
					var forces = new Dictionary<string, float>();
					foreach (string item in new[] { pad.BaseName, pad.Name })
					{
						GrHold(player, item); CuUse(item);
						yield return new WaitForSeconds(0.8f);
						GameObject model = CuHandModel(player, item);
						Vector3 fwd = Vector3.ProjectOnPlane(player.CameraTransform.forward, Vector3.up).normalized;
						Action look = () => { KeepAlive(player); Look(player, player.transform.localEulerAngles.y, 55f); };
						int h0 = cuHits.Count, f0 = cuPaddleForces.Count;
						yield return CuWait(0.3f, look);
						yield return CuClick(0.4f, look);
						yield return CuWait(1.8f, look);
						List<string> ev = cuHits.Skip(h0).Where(h => h.Value != null && model != null && h.Value.transform.IsChildOf(model.transform)).Select(h => h.Key).ToList();
						float f = cuPaddleForces.Skip(f0).DefaultIfEmpty(0f).Max();
						forces[item] = f;
						Log("  " + item + ": events " + string.Join(",", ev.ToArray()) + "; paddle force " + f.ToString("F1"));
						if (item == pad.Name) Check(ref ok, ev.Contains("OnPaddle") && ev.Contains("PaddlePaddle") && f > 0f, pad.Name + " strokes (OnPaddle + PaddlePaddle on its own hand model)");
					}
					if (forces.TryGetValue(pad.BaseName, out float fb) && forces.TryGetValue(pad.Name, out float fm) && fb > 0f)
						Check(ref ok, Mathf.Abs(fm - fb * ExtraUpgrades.PaddleFactor) < 0.05f * fb + 0.01f, pad.Name + " pushes with " + fm.ToString("F1") + ", " + pad.BaseName + " " + fb.ToString("F1"));
				}

				// (4) the magnet hook: thrown out into the water, it pulls floating debris in
				ExtraUpgrades.Upgrade hk = held.FirstOrDefault(u => u.Name == "DI_MagnetHook");
				if (hk != null)
					foreach (string item in new[] { hk.BaseName, hk.Name })
					{
						var res = new bool[2];
						yield return CuHookRun(player, item, res);
						if (item == hk.Name) { Check(ref ok, res[0], hk.Name + " is thrown and lands in the water"); Check(ref ok, res[1], hk.Name + " pulls floating debris in"); }
						else Log("  control " + item + ": thrown " + res[0] + ", debris " + res[1]);
					}

				// (5) the rest: their throw / swing events reach their own hand model as they reach the base item's
				foreach (ExtraUpgrades.Upgrade u in held.Where(x => x.Name == "DI_TitaniumRod" || x.Name == "DI_Longbow" || x.Name == "DI_MasterHammer" || x.Name == "DI_LumberAxe"))
				{
					var got = new Dictionary<string, List<string>>();
					foreach (string item in new[] { u.BaseName, u.Name })
					{
						if (item.Contains("bow") || item == "Bow") player.Inventory.AddItem("Arrow_Stone", 5);
						GrHold(player, item); CuUse(item);
						yield return new WaitForSeconds(0.8f);
						GameObject model = CuHandModel(player, item);
						bool throws = u.Name == "DI_TitaniumRod" || u.Name == "DI_Longbow";
						Action look = () => { KeepAlive(player); Look(player, player.transform.localEulerAngles.y, throws ? -10f : 30f); };
						int h0 = cuHits.Count;
						yield return CuWait(0.3f, look);
						yield return CuClick(throws ? 1.2f : 0.6f, look);
						yield return CuWait(2.5f, look);
						got[item] = cuHits.Skip(h0).Where(h => h.Value != null && model != null && h.Value.transform.IsChildOf(model.transform)).Select(h => h.Key).Distinct().ToList();
						Log("  " + item + ": events " + string.Join(",", got[item].ToArray()));
						if (throws) { CuSet(true); yield return CuWait(4f, look); CuSet(false); yield return CuWait(0.5f, look); }
					}
					List<string> b = got[u.BaseName], m = got[u.Name];
					Check(ref ok, m.Count > 0 ? b.All(m.Contains) : b.Count == 0, u.Name + " gets the events " + u.BaseName + " gets: [" + string.Join(",", m.ToArray()) + "] vs [" + string.Join(",", b.ToArray()) + "]");
				}
			}
			finally
			{
				cuFake = false; cuDown = false;
				if (patched != null) foreach (MethodBase m in patched) try { harmony.Unpatch(m, HarmonyPatchType.All, CuHarmonyId); } catch (Exception e) { Log("  (unpatching " + m.Name + ": " + e.Message + ")"); }
				if (boar != null && boar.networkEntity != null) try { boar.networkEntity.stat_health.Value = 0f; UnityEngine.Object.Destroy(boar.gameObject); } catch { }
			}
			Log("  input ids asked while faking: " + string.Join(", ", cuIds.ToArray()));
			if (ok) Log("PASS: craft use"); else Fail("craft use");
		}

		/// <summary>Throws the hook in hand out over the water (each way round in turn until it lands in the water), puts
		/// floating debris where it lies and holds the button to pull it in. res[0] = in the water, res[1] = debris pulled in.</summary>
		static IEnumerator CuHookRun(Network_Player player, string item, bool[] res)
		{
			GrHold(player, item); CuUse(item);
			yield return new WaitForSeconds(0.8f);
			GameObject model = CuHandModel(player, item);
			Hook hook = model != null ? model.GetComponentInChildren<Hook>(true) : null;
			Throwable th = model != null ? model.GetComponentInChildren<Throwable>(true) : null;
			if (hook == null || th == null) { Log("  " + item + ": no Hook/Throwable on its hand model"); yield break; }
			float yaw0 = player.transform.localEulerAngles.y;
			for (int turn = 0; turn < 4 && !res[0]; turn++)
			{
				float yaw = yaw0 + 90f * turn;
				Action look = () => { KeepAlive(player); Look(player, yaw, -12f); };
				int h0 = cuHits.Count;
				yield return CuWait(0.4f, look);
				yield return CuClick(1.3f, look);
				for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 5f && !th.InWater; ) { look(); yield return null; }
				List<string> ev = cuHits.Skip(h0).Where(h => h.Value != null && h.Value.transform.IsChildOf(model.transform)).Select(h => h.Key).Distinct().ToList();
				Log("  " + item + " throw " + turn + ": events " + string.Join(",", ev.ToArray()) + ", in water " + th.InWater + ", hook at " + (th.throwableObject != null ? (th.throwableObject.position - player.transform.position).magnitude.ToString("F1") + " m" : "?"));
				if (th.InWater) res[0] = true;
				else { CuSet(true); yield return CuWait(3f, look); CuSet(false); yield return CuWait(1f, look); }
			}
			if (!res[0]) yield break;

			// debris on the water where the hook lies, pulled in
			ObjectSpawner spawner = UnityEngine.Object.FindObjectOfType<ObjectSpawner_RaftDirection>();
			var debris = new List<PickupItem_Networked>();
			if (spawner == null) { Log("  no ObjectSpawner_RaftDirection"); yield break; }
			Vector3 at = th.throwableObject.position;
			for (int i = 0; i < 3; i++)
			{
				try { PickupItem_Networked d = spawner.SpawnItemNetwork(at + new Vector3((i - 1) * 0.3f, 0.1f, 0.2f), -1, -1); if (d != null) debris.Add(d); }
				catch (Exception e) { Log("  debris not made: " + (e.InnerException ?? e).Message); }
			}
			int inv0 = CuInventoryTotal(player);
			float yawP = player.transform.localEulerAngles.y;
			Action hold = () => { KeepAlive(player); };
			CuSet(true);
			int collected = 0;
			for (float t0 = Time.realtimeSinceStartup; Time.realtimeSinceStartup - t0 < 20f && th.InWater; )
			{
				hold();
				collected = Mathf.Max(collected, hook.hookItemCollector != null ? hook.hookItemCollector.collectedItems.Count : 0);
				yield return null;
			}
			CuSet(false);
			yield return CuWait(1.5f, hold);
			int gained = CuInventoryTotal(player) - inv0;
			int gone = debris.Count(d => d == null || !d.gameObject.activeInHierarchy);
			Log("  " + item + ": " + debris.Count + " debris made, hook collected " + collected + ", " + gone + " gone from the water, inventory +" + gained + ", hook back in hand " + !th.InWater);
			res[1] = debris.Count > 0 && collected > 0 && gained > 0;
		}
	}
}
