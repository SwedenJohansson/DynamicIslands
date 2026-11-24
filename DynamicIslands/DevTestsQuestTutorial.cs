using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>The guide's quest tutorial pictures (GUIDE 6.8): the editor framed on one object of a built recipe, its
	/// windows opened and shot, and a play whose log lines take HUD pictures (TUTSHOT), open an islander's dialogue
	/// (TUTTALK) or count a cooked item (TUTCOOK).</summary>
	public static partial class DevTests
	{
		/// <summary>A placed editor object by its recipe alias (CIRecipe's last one of it) or its obj.name.</summary>
		static EditorGameObject TutObject(string name)
		{
			EditorGameObject eo;
			if (RecipeObjects.TryGetValue(name, out eo) && eo != null) return eo;
			return PlacedEditorObjects().FirstOrDefault(o => string.Equals(ObjectProps.Get(o.Props, BehaviourProps.Name), name, StringComparison.OrdinalIgnoreCase));
		}

		static float TutNum(string[] a, int i, float d)
		{
			float v;
			return a != null && a.Length > i && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : d;
		}

		static Vector3 TutMiddle(GameObject go)
		{
			Renderer[] rs = go.GetComponentsInChildren<Renderer>(false);
			if (rs.Length == 0) return go.transform.position;
			Bounds b = rs[0].bounds;
			foreach (Renderer r in rs) b.Encapsulate(r.bounds);
			return b.center;
		}

		[ConsoleCommand(name: "CITutLook", docs: "Dev, editor: the camera framed on a placed object (recipe alias or obj.name): CITutLook <name> [dist 6] [from bearing 180, 0 = from the north] [up 3] [aim up 0]")]
		public static void TutLookCommand(string[] args)
		{
			if (args == null || args.Length == 0 || Camera.main == null) { Fail("CITutLook <name> [dist] [from] [up] [aimup]"); return; }
			EditorGameObject eo = TutObject(args[0]);
			if (eo == null) { Fail("no object '" + args[0] + "'"); return; }
			float dist = TutNum(args, 1, 6f), from = TutNum(args, 2, 180f) * Mathf.Deg2Rad, up = TutNum(args, 3, 3f), aim = TutNum(args, 4, 0f);
			Vector3 c = TutMiddle(eo.gameObject) + Vector3.up * aim;
			Vector3 at = c + new Vector3(Mathf.Sin(from), 0f, Mathf.Cos(from)) * dist + Vector3.up * up;
			Camera.main.transform.position = at;
			Camera.main.transform.LookAt(c);
			Log("Camera on " + args[0] + " at " + at);
		}

		[ConsoleCommand(name: "CITutSelect", docs: "Dev, editor: selects a placed object (recipe alias or obj.name) as a click does: CITutSelect <name>; CITutSelect - clears the selection")]
		public static void TutSelectCommand(string[] args)
		{
			if (args == null || args.Length == 0 || args[0] == "-") { DynamicIslands.EditorGizmoHandler.ClearTargets(false); Log("Selection cleared"); return; }
			EditorGameObject eo = TutObject(args[0]);
			if (eo == null) { Fail("no object '" + args[0] + "'"); return; }
			EditorUI.SetTab(TAB.ObjectPlace);
			TransformGizmoSelect(eo.transform);
			Log("Selected " + args[0]);
		}

		[ConsoleCommand(name: "CITutWindow", docs: "Dev, editor: opens a window, takes shot_<file>.png, closes it: CITutWindow <file> <behaviour|note|quest0|quest1|quest2|storyitems> [object]")]
		public static void TutWindowCommand(string[] args)
		{
			if (args == null || args.Length < 2) { Fail("CITutWindow <file> <window> [object]"); return; }
			StartTest(TutWindowRoutine(args[0], args[1], args.Length > 2 ? args[2] : null));
		}

		static IEnumerator TutWindowRoutine(string file, string window, string obj)
		{
			EditorGameObject eo = obj != null ? TutObject(obj) : null;
			if (obj != null && eo == null) { Fail("no object '" + obj + "'"); yield break; }
			Action close;
			switch (window)
			{
				case "behaviour": BehaviourWindow.Open(eo); close = BehaviourWindow.Close; break;
				case "note": NoteEditorWindow.Open(eo); close = NoteEditorWindow.Close; break;
				case "storyitems": StoryItemsWindow.Open(); close = StoryItemsWindow.Close; break;
				case "quest0": case "quest1": case "quest2":
					QuestEditorWindow.Open(); QuestEditorWindow.ShowTab(window[5] - '0'); close = QuestEditorWindow.Close; break;
				default: Fail("no window '" + window + "'"); yield break;
			}
			yield return new WaitForSecondsRealtime(0.8f);
			Screenshot(new[] { file });
			yield return new WaitForSecondsRealtime(0.6f);
			close();
			Log("PASS: tutorial window " + window + " -> " + file);
		}

		static bool tutGfx;

		[ConsoleCommand(name: "CITutQuality", docs: "Dev: the user's graphics quality for the guide's pictures while CIGfx low is on (the window stays small): CITutQuality on|off")]
		public static void TutQualityCommand(string[] args)
		{
			bool on = args == null || args.Length == 0 || args[0] != "off";
			if (on && !tutGfx) tutGfx = GfxShotBegin();
			else if (!on && tutGfx) { GfxShotEnd(true); tutGfx = false; }
			Log("Tutorial quality " + (tutGfx ? "on" : "off"));
		}

		[ConsoleCommand(name: "CITutPlay", docs: "Dev, world (host, a 'CI ' world): CIPlay <name> whose log lines also act: 'log TUTSHOT <file>' a picture with the HUD (shot_<file>.png), 'log TUTTALK <islander>' opens the dialogue (left open), 'log TUTCLOSE' closes it (and a note), 'log TUTNEAR <dist> <object>' puts the player by it,'log TUTCOOK <item> [n]' counts it as cooked, 'log TUTFACE <object> [pitch]' turns the player to it, 'log TUTSNAP <file> <from> <dist> <up> <object>' a play picture (wait 3 after)")]
		public static void TutPlayCommand(string[] args)
		{
			if (args == null || args.Length == 0) { Fail("CITutPlay <name>"); return; }
			StartTest(TutPlayRoutine(string.Join(" ", args)));
		}

		static IEnumerator TutPlayRoutine(string name)
		{
			Application.logMessageReceived += TutLogLine;
			bool began = GfxShotBegin();
			try { yield return PlayRoutine(name); }
			finally { Application.logMessageReceived -= TutLogLine; GfxShotEnd(began); }
		}

		static void TutLogLine(string message, string stack, LogType type)
		{
			int at = message.IndexOf("  TUT", StringComparison.Ordinal);
			if (!message.StartsWith("[CITEST]") || at < 0) return;
			string[] w = message.Substring(at + 2).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (w.Length == 0) return;
			string rest = string.Join(" ", w.Skip(1).ToArray());
			try
			{
				switch (w[0])
				{
					case "TUTSHOT":
						Log("  (shot " + rest + ": dialogue " + (DialoguePanel.IsOpen ? "open" : "closed") + ", " + Mathf.RoundToInt(1f / Mathf.Max(0.001f, Time.unscaledDeltaTime)) + " fps)");
						Screenshot(new[] { rest });
						break;
					case "TUTCLOSE": DialoguePanel.Close(); NoteReader.Close(); break;
					case "TUTNEAR":
						{
							// (TUTNEAR <dist> <object...>: the player stands about dist m from it, facing it)
							IslandObjectRef r = w.Length > 2 ? ScObjOf(playEntry, string.Join(" ", w.Skip(2).ToArray())) : null;
							if (r == null) { Log("  (TUTNEAR: no object)"); break; }
							PutPlayerNear(r.transform, TutNum(w, 1, 3f) / 1.414f);
							TutFace(r.gameObject, 10f);
							break;
						}
					case "TUTCOOK":
						{
							int n = 1;
							if (w.Length > 2) int.TryParse(w[2], out n);
							QuestSteps.Made(w[1], n);
							Network_Player cook = RAPI.GetLocalPlayer();
							if (cook != null) cook.Inventory.AddItem(w[1], n);
							break;
						}
					case "TUTSNAP":
						{
							// (TUTSNAP <file> <from> <dist> <up> <object...>: like the play's snap, for names with spaces)
							if (w.Length < 6) { Log("  (TUTSNAP <file> <from> <dist> <up> <object>)"); break; }
							string target = string.Join(" ", w.Skip(5).ToArray());
							IslandObjectRef r = ScObjOf(playEntry, target);
							if (r == null) { Log("  (TUTSNAP: no object '" + target + "')"); break; }
							Vector3 c = TutMiddle(r.gameObject);
							float from = TutNum(w, 2, 180f) * Mathf.Deg2Rad;
							Vector3 eye = c + new Vector3(Mathf.Sin(from), 0f, Mathf.Cos(from)) * TutNum(w, 3, 6f) + Vector3.up * TutNum(w, 4, 2f);
							DynamicIslands.instance.StartCoroutine(PlayPicture(w[1], eye, c));
							break;
						}
					case "TUTTALK":
						{
							IslandObjectRef r = ScObjOf(playEntry, rest);
							Islander isl = r != null ? r.GetComponentInChildren<Islander>(true) : null;
							if (isl == null) { Log("  (TUTTALK: no islander '" + rest + "')"); break; }
							PutPlayerNear(r.transform, 2f);
							TutFace(r.gameObject, 5f);
							isl.Talk();
							Log("  (dialogue " + (DialoguePanel.IsOpen ? "open: " + DialoguePanel.ShownSpeaker : "did not open") + ")");
							break;
						}
					case "TUTFACE":
						{
							float pitch;
							string target = rest;
							if (w.Length > 2 && float.TryParse(w[w.Length - 1], NumberStyles.Float, CultureInfo.InvariantCulture, out pitch))
								target = string.Join(" ", w.Skip(1).Take(w.Length - 2).ToArray());
							else pitch = 10f;
							IslandObjectRef r = ScObjOf(playEntry, target);
							if (r == null) { Log("  (TUTFACE: no object '" + target + "')"); break; }
							TutFace(r.gameObject, pitch);
							break;
						}
				}
			}
			catch (Exception ex) { Debug.LogWarning("[CITEST] TUT line failed: " + ex.Message); }
		}

		/// <summary>The local player turned to look at the object's middle (pitch: degrees down).</summary>
		static void TutFace(GameObject go, float pitch)
		{
			Network_Player me = RAPI.GetLocalPlayer();
			if (me == null) return;
			Vector3 d = TutMiddle(go) - me.transform.position;
			Look(me, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, pitch);
		}
	}
}
