using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DynamicIslands
{
	/// <summary>
	/// Set-ups for the real-key tests (11-04, IL20): Raft's sign placed in front of the player and its text field focused
	/// as the E key does, a note opened in front of the player out at sea, and one line of what the keys would see
	/// (CIKeysState). The typing itself still needs real keys (ui.ps1 key / type).
	/// </summary>
	public static partial class DevTests
	{
		const string SignItem = "Placeable_Sign";
		const string NoteHereName = "CI_NoteHere";

		[ConsoleCommand(name: "CIKeysState", docs: "Dev, in game (either player): one line of what the mod's keys see - someone typing (ModKeys.SomeoneTyping), the field that has the keyboard and its text, Raft's sign writer, the stats page, the journal, the note reader, the dialogue, the minimap, CanvasHelper.ActiveMenu")]
		public static void KeysStateCommand()
		{
			Log("Keys state: " + KeysState());
		}

		static string KeysState()
		{
			GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
			InputField f = sel != null ? sel.GetComponent<InputField>() : null;
			TMPro.TMP_InputField t = sel != null ? sel.GetComponent<TMPro.TMP_InputField>() : null;
			string field = sel == null ? "none" : sel.name + (f != null ? " (focused " + f.isFocused + ", text '" + f.text + "')" : t != null ? " (TMP, focused " + t.isFocused + ", text '" + t.text + "')" : "");
			TextWriterManager tw = SignWriterMenu();
			string menu;
			try { menu = CanvasHelper.ActiveMenu.ToString(); } catch { menu = "?"; }
			return "typing " + ModKeys.SomeoneTyping() + ", field " + field + ", sign writing " + (tw != null && tw.IsWriting)
				+ ", stats page " + LevelWindow.IsOpen + ", journal " + JournalWindow.IsOpen + ", notebook " + PlayerNotebookWindow.IsOpen + ", note " + NoteReader.IsOpen + ", dialogue " + DialoguePanel.IsOpen
				+ ", minimap mode " + Minimap.Mode + " shown " + Minimap.Shown + ", ActiveMenu " + menu;
		}

		/// <summary>Raft's sign writing box (one in the game scene).</summary>
		static TextWriterManager SignWriterMenu()
		{
			try { return Resources.FindObjectsOfTypeAll<TextWriterManager>().FirstOrDefault(m => m != null && m.gameObject.scene.IsValid()); }
			catch { return null; }
		}

		[ConsoleCommand(name: "CISign", docs: "Dev, in game (either player; a test world 'CI ...'): Raft's sign (Placeable_Sign) for the real-key tests. CISign give [n] - into the inventory; CISign place - a sign on the raft's floor 1.5 m in front of the player (stand on the raft), the player facing it; CISign write - the nearest sign within 8 m: its text field opened and focused as the E key does (TextWriterManager.StartWriting), then real keys type; CISign stop - closes the box as Esc does (AbortTyping); CISign text - the nearest sign's text")]
		public static void SignCommand(string[] args)
		{
			string what = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";
			int n = 1;
			if (args != null && args.Length > 1) int.TryParse(args[1], out n);
			StartTest(SignRoutine(what, Math.Max(1, n)));
		}

		static IEnumerator SignRoutine(string what, int n)
		{
			Network_Player player = RAPI.GetLocalPlayer();
			Item_Base item = null;
			try { item = ItemManager.GetItemByName(SignItem); } catch { }
			if (player == null || item == null) { Fail("CISign: no player or no item " + SignItem); yield break; }
			if (what == "give")
			{
				player.Inventory.AddItem(SignItem, n);
				Log("PASS: CISign give: " + n + " x " + SignItem + " in the inventory (now " + player.Inventory.GetItemCount(SignItem) + ")");
				yield break;
			}
			if (what == "place")
			{
				Block floor = null;
				RaycastHit hit = default(RaycastHit);
				Vector3 fwd = Flat(player.transform.forward).normalized;
				if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
				// (the raft's floor 1.5 m ahead, else round the player: the first spot with a block under it)
				for (int k = 0; k < 8 && floor == null; k++)
				{
					Vector3 dir = Quaternion.Euler(0f, k * 45f, 0f) * fwd;
					Vector3 p = player.transform.position + dir * 1.5f + Vector3.up * 1.5f;
					foreach (RaycastHit h in Physics.RaycastAll(p, Vector3.down, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore).OrderBy(x => x.distance))
					{
						if (h.collider.transform.IsChildOf(player.transform)) continue;
						floor = h.collider.GetComponentInParent<Block>();
						if (floor != null) { hit = h; fwd = dir; }
						break;
					}
				}
				if (floor == null || floor.transform.parent == null) { Fail("CISign place: no raft floor near the player (stand on the raft)"); yield break; }
				DPS dps = item.settings_buildable != null && item.settings_buildable.GetBlockPrefab(DPS.Floor) != null ? DPS.Floor : DPS.Default;
				Transform pivot = floor.transform.parent;
				Vector3 local = pivot.InverseTransformPoint(hit.point);
				// (the sign's face towards the player)
				Vector3 euler = (Quaternion.Inverse(pivot.rotation) * Quaternion.LookRotation(-fwd)).eulerAngles;
				player.Inventory.AddItem(SignItem, 1);
				if (item.settings_recipe != null && item.settings_recipe.NewCost != null)
					foreach (CostMultiple c in item.settings_recipe.NewCost.Where(c => c != null && c.items != null && c.items.Length > 0 && c.items[0] != null)) player.Inventory.AddItem(c.items[0].UniqueName, c.amount);
				Block made = null;
				try { made = player.BlockCreator.CreateBlockCheat(item, local, euler, dps, 0); }
				catch (Exception ex) { Fail("CISign place: Raft's BlockCreator threw " + ex.Message); yield break; }
				yield return new WaitForSeconds(0.5f);
				if (made == null) { Fail("CISign place: no sign made"); yield break; }
				Vector3 look = made.transform.position + Vector3.up * 1f - Helper.MainCamera.transform.position;
				Look(player, Quaternion.LookRotation(Flat(look)).eulerAngles.y, -Mathf.Atan2(look.y, Flat(look).magnitude) * Mathf.Rad2Deg);
				Log("PASS: CISign place: a sign at " + made.transform.position.ToString("F1") + ", " + Vector3.Distance(player.transform.position, made.transform.position).ToString("F1") + " m from the player (" + dps + ")");
				yield break;
			}
			TextWriterObject sign = NearestSign(player, 8f);
			TextWriterManager menu = SignWriterMenu();
			if (what == "write")
			{
				if (sign == null || menu == null) { Fail("CISign write: " + (sign == null ? "no sign within 8 m (CISign place)" : "no sign writing box")); yield break; }
				Vector3 look = sign.transform.position + Vector3.up * 0.8f - Helper.MainCamera.transform.position;
				Look(player, Quaternion.LookRotation(Flat(look)).eulerAngles.y, -Mathf.Atan2(look.y, Flat(look).magnitude) * Mathf.Rad2Deg);
				menu.StartWriting(sign);
				yield return null;
				yield return new WaitForSeconds(0.3f);
				InputField field = Traverse.Create(menu).Field("inputField").GetValue<InputField>();
				// (the box takes the keyboard itself; if Unity hasn't focused it yet, as a click would)
				if (field != null && !field.isFocused) { field.Select(); field.ActivateInputField(); yield return null; yield return null; }
				bool focused = field != null && field.isFocused;
				if (menu.IsWriting && focused) Log("PASS: CISign write: the sign's text field has the keyboard - " + KeysState());
				else Fail("CISign write: writing " + menu.IsWriting + ", field focused " + focused + " - " + KeysState());
				yield break;
			}
			if (what == "stop")
			{
				if (menu != null && menu.IsWriting) menu.AbortTyping();
				yield return new WaitForSeconds(0.3f);
				if (menu != null && menu.IsWriting) Fail("CISign stop: still writing");
				else Log("PASS: CISign stop: the sign box is closed - " + KeysState());
				yield break;
			}
			if (what == "text")
			{
				Log("PASS: CISign text: " + (sign != null ? "'" + sign.GetText() + "'" : "no sign within 8 m"));
				yield break;
			}
			Log("PASS: CISign status: sign within 8 m " + (sign != null) + ", " + SignItem + " x " + player.Inventory.GetItemCount(SignItem) + " - " + KeysState());
		}

		static TextWriterObject NearestSign(Network_Player player, float within)
		{
			return UnityEngine.Object.FindObjectsOfType<TextWriterObject>().Where(t => t != null && t.GetComponentInParent<SignWriter>() != null)
				.OrderBy(t => Vector3.Distance(t.transform.position, player.transform.position)).FirstOrDefault(t => Vector3.Distance(t.transform.position, player.transform.position) <= within);
		}

		[ConsoleCommand(name: "CINoteHere", docs: "Dev, in game (either player): a custom note 1 m in front of the player, wherever they are (the raft at sea: no island, no creatures near), opened as the E key opens a note (NoteReader.Open); the player is kept alive. CINoteHere [title] / CINoteHere close (closes it and removes the note)")]
		public static void NoteHereCommand(string[] args)
		{
			string title = args != null && args.Length > 0 ? string.Join(" ", args) : "";
			StartTest(NoteHereRoutine(title));
		}

		static IEnumerator NoteHereRoutine(string title)
		{
			GameObject old = GameObject.Find(NoteHereName);
			if (title.Equals("close", StringComparison.OrdinalIgnoreCase))
			{
				NoteReader.Close();
				if (old != null) UnityEngine.Object.Destroy(old);
				yield return null;
				Log("PASS: CINoteHere close: note " + (NoteReader.IsOpen ? "STILL OPEN" : "closed") + " - " + KeysState());
				yield break;
			}
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null) { Fail("CINoteHere: no player"); yield break; }
			yield return EnsureAlive();
			if (old != null) UnityEngine.Object.Destroy(old);
			yield return null;
			Vector3 fwd = Flat(player.transform.forward).normalized;
			if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
			var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
			go.name = NoteHereName;
			UnityEngine.Object.Destroy(go.GetComponent<Collider>());
			go.transform.localScale = new Vector3(0.3f, 0.02f, 0.4f);
			go.transform.position = player.transform.position + fwd * 1f + Vector3.up * 1f;
			// (on the raft the note moves with it: the reader closes when the player is far from the note)
			if (player.transform.parent != null) go.transform.SetParent(player.transform.parent, true);
			CustomNote note = CustomNote.Attach(go, new Dictionary<string, string> {
				{ ObjectProps.NoteTitle, title.Length > 0 ? title : "CI note" },
				{ ObjectProps.NoteText, "A test note for the keys (IL20): K, J and the minimap keys do nothing while it is open." } });
			KeepAlive(player);
			NoteReader.Open(note);
			yield return new WaitForSeconds(0.3f);
			if (NoteReader.IsOpen) Log("PASS: CINoteHere: note '" + NoteReader.ShownTitle + "' open in front of the player - " + KeysState());
			else Fail("CINoteHere: the note reader didn't open");
		}

		#region Keys pressed through the game's own key code (Raft minimised: no real keys)

		static KeyCode fakeKey = KeyCode.None;
		static int fakeFrame = -1;
		static bool fakeInstalled;

		/// <summary>DevTests.Init (start-up, before the mod's key code first runs - Mono may build Input.GetKeyDown into
		/// its callers when they are first compiled): Input.GetKeyDown(KeyCode) also answers true for CIKey's key, for one frame.</summary>
		static void InstallFakeKeys()
		{
			if (fakeInstalled) return;
			fakeInstalled = true;
			try
			{
				var h = new Harmony("DynamicIslands.DevTests.FakeKeys");
				h.Patch(AccessTools.Method(typeof(Input), "GetKeyDown", new[] { typeof(KeyCode) }), postfix: new HarmonyMethod(typeof(DevTests), "FakeKeyPostfix"));
			}
			catch (Exception ex) { Log("Fake keys not installed: " + ex.Message); }
		}

		static void FakeKeyPostfix(KeyCode __0, ref bool __result)
		{
			if (!__result && __0 == fakeKey && Time.frameCount == fakeFrame) __result = true;
		}

		[ConsoleCommand(name: "CIKey", docs: "Dev, in game (either player): presses a key through the game's own key code with Raft minimised - for one frame Input.GetKeyDown(<key>) is true (the mod's K, J and minimap keys read it), and a text field that has the keyboard gets the key's letter as typing gives it (InputField.ProcessEvent); then one CIKeysState line. CIKey <KeyCode> (K, J, M, N...)")]
		public static void KeyCommand(string[] args)
		{
			KeyCode k;
			string name = args != null && args.Length > 0 ? args[0] : "";
			try { k = (KeyCode)Enum.Parse(typeof(KeyCode), name, true); }
			catch { Fail("CIKey: no key called '" + name + "'"); return; }
			StartTest(KeyRoutine(k));
		}

		static IEnumerator KeyRoutine(KeyCode k)
		{
			InstallFakeKeys();
			string before = KeysState();
			GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
			InputField f = sel != null ? sel.GetComponent<InputField>() : null;
			TMPro.TMP_InputField t = sel != null ? sel.GetComponent<TMPro.TMP_InputField>() : null;
			bool typed = false;
			string letter = k >= KeyCode.A && k <= KeyCode.Z ? ((char)('a' + (k - KeyCode.A))).ToString() : null;
			if (letter != null && f != null && f.isFocused) { f.ProcessEvent(Event.KeyboardEvent(letter)); f.ForceLabelUpdate(); typed = true; }
			else if (letter != null && t != null && t.isFocused) { t.ProcessEvent(Event.KeyboardEvent(letter)); t.ForceLabelUpdate(); typed = true; }
			fakeKey = k;
			fakeFrame = Time.frameCount + 1;
			yield return null;
			yield return null;
			yield return new WaitForSeconds(0.2f);
			fakeKey = KeyCode.None;
			Log("PASS: CIKey " + k + (typed ? " (typed into " + sel.name + ")" : "") + " - before: " + before);
			Log("CIKey " + k + " after: " + KeysState());
		}

		#endregion
	}
}
