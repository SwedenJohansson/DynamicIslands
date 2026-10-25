using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// Islanders (PF2, 2026-10-25): one of Raft's six characters standing on an island as a story character - standing,
	/// sitting, fishing, sleeping or leaning. A player talks to one with the interact key: a dialogue of pages, maybe with
	/// answers to pick. Talking fires the islander's "talk" event (and "choice1".."choice3" for an answer), counts for a
	/// quest's "Talk to" step and is remembered for the "talked" check - for the whole crew, saved with the world.
	///   islander.character  Raft's character (name or number; empty = one picked by the island and the object)
	///   islander.pose       stand, sit, fish, sleep or lean
	///   talk.text           the dialogue: pages split by a line "---"; lines starting with "&gt;" are answers (up to 3)
	/// Its name (obj.name) is who quests and checks talk to.
	/// </summary>
	public static class Islanders
	{
		public const string ObjectName = "Story_Islander";
		public const string CharacterProp = "islander.character", PoseProp = "islander.pose", TextProp = "talk.text";
		public const string TalkEvent = "talk", ChoicePrefix = "choice", QuestType = "talk";
		public static readonly string[] Poses = { "stand", "sit", "fish", "sleep", "lean" };
		/// <summary>Talked to (by anyone of the crew): TalkedBase + object, after Survivors.KeyBase 0xD0000 + 0xFFFF and
		/// BossPhases' 0xE0000 (StableIds moves it with the object).</summary>
		public const int TalkedBase = 0x130000;
		public const int MaxChoices = 3;
		public const string DefaultText = "Hello there, traveller.\n---\nThe sea brought you here, didn't it? It brings everyone, sooner or later.";

		/// <summary>The last dialogue shown on this machine (tests): speaker, page text.</summary>
		public static string LastSpeaker = "", LastPage = "";
		public static int Talks;

		public static bool Is(string objectName) { return objectName == ObjectName; }

		public static string NameOf(IDictionary<string, string> p) { string n = ObjectProps.Get(p, BehaviourProps.Name).Trim(); return n.Length > 0 ? n : "Islander"; }

		public static string PoseOf(IDictionary<string, string> p)
		{
			string pose = ObjectProps.Get(p, PoseProp).Trim().ToLowerInvariant();
			return Poses.Contains(pose) ? pose : "stand";
		}

		/// <summary>One page of a dialogue: its text and the answers under it.</summary>
		public class Page
		{
			public string Text = "";
			public List<string> Choices = new List<string>();
		}

		/// <summary>A dialogue's pages ("\n" written as two characters counts as a new line - an action's text is one line).</summary>
		public static List<Page> Parse(string text)
		{
			var pages = new List<Page>();
			var cur = new Page();
			var lines = new List<string>();
			Action flush = () =>
			{
				cur.Text = string.Join("\n", lines.ToArray()).Trim();
				if (cur.Text.Length > 0 || cur.Choices.Count > 0) pages.Add(cur);
				cur = new Page();
				lines.Clear();
			};
			foreach (string raw in (text ?? "").Replace("\\n", "\n").Replace("\r", "").Split('\n'))
			{
				string l = raw.Trim();
				if (l == "---") { flush(); continue; }
				if (l.StartsWith(">")) { if (cur.Choices.Count < MaxChoices && l.Substring(1).Trim().Length > 0) cur.Choices.Add(l.Substring(1).Trim()); continue; }
				lines.Add(raw.TrimEnd());
			}
			flush();
			return pages;
		}

		/// <summary>All answers of a dialogue, in order (answer n fires "choice n").</summary>
		public static List<string> Choices(string text) { return Parse(text).SelectMany(pg => pg.Choices).Take(MaxChoices).ToList(); }

		public static void Defaults(string name, Dictionary<string, string> props)
		{
			if (!Is(name)) return;
			props[BehaviourProps.Name] = "Islander";
			props[PoseProp] = "stand";
			props[TextProp] = DefaultText;
		}

		public const string Hint = "one of Raft's characters, standing (or sitting, fishing, sleeping, leaning) on the island. Players talk to it with the interact key: its dialogue, maybe with answers. Select it to write what it says";

		/// <summary>An islander's catalog marker: a figure (body, head) about a person's height; Raft's character in a world.</summary>
		public static GameObject BuildMarker()
		{
			var root = new GameObject(ObjectName);
			root.transform.SetParent(PlaceableCatalog.Container.transform, false);
			Transform marker = new GameObject("Marker").transform;
			marker.SetParent(root.transform, false);
			Color c = new Color(0.85f, 0.7f, 0.5f);
			ContentCatalog.Part(PrimitiveType.Capsule, "Body", marker, new Vector3(0f, 0.75f, 0f), Quaternion.identity, new Vector3(0.45f, 0.6f, 0.35f), ContentCatalog.MarkerMaterial(new Color(0.3f, 0.45f, 0.6f)));
			ContentCatalog.Part(PrimitiveType.Sphere, "Head", marker, new Vector3(0f, 1.6f, 0f), Quaternion.identity, Vector3.one * 0.3f, ContentCatalog.MarkerMaterial(c));
			ContentCatalog.Part(PrimitiveType.Cube, "Nose", marker, new Vector3(0f, 1.6f, 0.15f), Quaternion.identity, new Vector3(0.06f, 0.06f, 0.08f), ContentCatalog.MarkerMaterial(c * 0.9f));
			ContentCatalog.AddLabel(root.transform, "Islander", 2.1f);
			return root;
		}

		/// <summary>World: an islander gets its character and its talk (Behaviours.Attach).</summary>
		internal static void Attach(GameObject go, IslandObjectRef r)
		{
			Islander i = CustomNote.InteractHolder(go).AddComponent<Islander>();
			i.Ref = r;
			i.Body = go;
		}

		/// <summary>A player talked to an islander (the dialogue closed; choice 1..3 = the answer picked, 0 = none).</summary>
		internal static void Talked(IslandWorldState.Entry e, Transform at, int index, string name, int choice)
		{
			Talks++;
			if (e == null) return;
			if (!ContentState.IsUsed(e, TalkedBase + index)) ContentState.MarkUsed(at, TalkedBase + index);
			Debug.Log("[CUSTOM ISLANDS] Talked to '" + name + "' on '" + e.HostName + "'" + (choice > 0 ? ", answer " + choice : ""));
			if (choice > 0) Behaviours.Fire(e, index, ChoicePrefix + choice, true);
			Behaviours.Fire(e, index, TalkEvent, true);
			QuestTracker.Event(e, QuestType, name);
		}

		/// <summary>Whether someone of the crew talked to an islander with this name (or the object itself).</summary>
		internal static bool TalkedTo(IslandWorldState.Entry e, int index, string target)
		{
			return Behaviours.TargetsOf(e, index, target).Any(r => ContentState.IsUsed(e, TalkedBase + r.Index));
		}

		/// <summary>The "dialogue" action: a dialogue for the player who set it off, said by the named object (or this one).
		/// Its answers fire "choice1".. on the object that ran the action.</summary>
		internal static void ShowAction(IslandWorldState.Entry e, int index, ObjAction a)
		{
			IslandObjectRef src = Behaviours.TargetsOf(e, index, "").FirstOrDefault();
			IslandObjectRef by = a.Target.Trim().Length > 0 ? Behaviours.TargetsOf(e, index, a.Target).FirstOrDefault() : src;
			Transform at = by != null ? by.transform : src != null ? src.transform : null;
			string speaker = by != null ? NameOf(by.Props) : a.Target.Trim().Length > 0 ? a.Target.Trim() : "";
			DialoguePanel.Open(at, speaker, a.Arg, choice =>
			{
				if (choice > 0 && e != null) Behaviours.Fire(e, index, ChoicePrefix + choice, true);
			});
		}

		/// <summary>Raft's animations for a pose (the third-person controller: a full-body index; fishing holds the rod).</summary>
		internal static void Pose(Animator an, string pose)
		{
			if (an == null || an.runtimeAnimatorController == null) return;
			int full = pose == "sit" ? 1 : pose == "sleep" ? 7 : pose == "lean" ? 13 : 0;
			try
			{
				foreach (AnimatorControllerParameter par in an.parameters)
				{
					if (par.name == "FullBodyIndex" && par.type == AnimatorControllerParameterType.Int) an.SetInteger(par.name, full);
					else if (par.type == AnimatorControllerParameterType.Bool && par.name.IndexOf("FullBody", StringComparison.OrdinalIgnoreCase) >= 0) an.SetBool(par.name, full > 0);
					else if (pose == "fish" && par.name == "ItemID" && par.type == AnimatorControllerParameterType.Int) an.SetInteger(par.name, 3);
					else if (pose == "fish" && par.name == "Switch" && par.type == AnimatorControllerParameterType.Trigger) an.SetTrigger(par.name);
				}
			}
			catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] An islander's pose '" + pose + "': " + ex.Message); }
		}
	}

	/// <summary>An islander in a world: Raft's character model in its pose, and the interact key opens its dialogue.</summary>
	public class Islander : MonoBehaviour, IRaycastable
	{
		public IslandObjectRef Ref;
		public GameObject Body;
		GameObject model;
		float cooldown;

		public bool HasModel { get { return model != null; } }

		void Start()
		{
			if (Body == null || Ref == null) return;
			// (the editor's name tag never shows in a world)
			foreach (TextMesh t in Body.GetComponentsInChildren<TextMesh>(true)) t.gameObject.SetActive(false);
			IslandWorldState.Entry e = ContentState.EntryOf(transform);
			SO_Character c = null;
			try { c = Survivors.CharacterFor(ObjectProps.Get(Ref.Props, Islanders.CharacterProp), (e != null ? e.HostName : "") + "/" + Ref.Index); } catch { }
			GameObject m = c != null ? Survivors.Model(c, "CI_IslanderModel") : null;
			if (m == null) return; // (no characters known: the marker figure stays)
			Transform marker = Body.transform.Find("Marker");
			if (marker != null) marker.gameObject.SetActive(false);
			m.transform.SetParent(Body.transform, false);
			m.transform.localPosition = Vector3.zero;
			m.transform.localRotation = Quaternion.identity;
			m.SetActive(true);
			model = m;
			string pose = Islanders.PoseOf(Ref.Props);
			Animator an = m.GetComponent<Animator>();
			Islanders.Pose(an, pose);
			// (again once the Animator has run: a parameter set before its first update can be lost)
			StartCoroutine(PoseLater(an, pose));
		}

		System.Collections.IEnumerator PoseLater(Animator an, string pose)
		{
			yield return null;
			yield return new WaitForSeconds(0.3f);
			if (an != null) Islanders.Pose(an, pose);
		}

		void IRaycastable.OnIsRayed()
		{
			if (NoteReader.IsOpen || DialoguePanel.IsOpen || Ref == null) return;
			DisplayTextManager hints = CustomNote.Hints;
			string name = Islanders.NameOf(Ref.Props);
			if (hints != null) hints.ShowText("Talk to " + name, CustomNote.InteractKey, 0, 0, true);
			if (CustomNote.InteractPressed() && Time.time > cooldown)
			{
				cooldown = Time.time + 0.5f;
				if (hints != null) hints.HideDisplayTexts();
				Talk();
			}
		}

		/// <summary>Opens the dialogue (tests call it too).</summary>
		public void Talk()
		{
			if (Ref == null) return;
			IslandWorldState.Entry e = ContentState.EntryOf(transform);
			string name = Islanders.NameOf(Ref.Props);
			Transform at = Body != null ? Body.transform : transform;
			int index = Ref.Index;
			DialoguePanel.Open(at, name, ObjectProps.Get(Ref.Props, Islanders.TextProp), choice => Islanders.Talked(e, at, index, name, choice));
		}

		void IRaycastable.OnRayEnter() { }

		void IRaycastable.OnRayExit()
		{
			DisplayTextManager hints = CustomNote.Hints;
			if (hints != null) hints.HideDisplayTexts();
		}
	}

	/// <summary>The dialogue on screen: the speaker, a page, Next or the answers. The interact key goes on; Tab or Esc
	/// closes (also walking 8 m away). When it closes the callback gets the answer picked (0 = none).</summary>
	public class DialoguePanel : MonoBehaviour
	{
		static DialoguePanel instance;
		public static bool IsOpen { get { return instance != null && instance.gameObject.activeSelf; } }
		public static string ShownSpeaker { get { return IsOpen ? instance.speakerText.text : null; } }
		public static string ShownText { get { return IsOpen ? instance.bodyText.text : null; } }
		public static int ShownPage { get { return IsOpen ? instance.page : -1; } }

		Text speakerText, bodyText;
		RectTransform buttons;
		List<Islanders.Page> pages;
		int page;
		Transform at;
		Action<int> done;
		float openedAt;
		bool cursorWasFree;

		public static void Open(Transform at, string speaker, string text, Action<int> done)
		{
			List<Islanders.Page> pages = Islanders.Parse(text);
			if (pages.Count == 0) pages.Add(new Islanders.Page { Text = "..." });
			if (instance == null) Build();
			if (IsOpen) instance.Finish(0);
			instance.Show(at, speaker, pages, done);
		}

		/// <summary>Next page, or closes after the last (tests).</summary>
		public static void Next() { if (IsOpen) instance.Advance(); }

		/// <summary>Picks answer n (1..3) of the page shown (tests).</summary>
		public static void Pick(int n) { if (IsOpen) instance.Finish(n); }

		public static void Close() { if (IsOpen) instance.Finish(0); }

		static void Build()
		{
			Canvas canvas = UIKit.CreateCanvas("CustomIslands_Dialogue", 500);
			DontDestroyOnLoad(canvas.gameObject);
			instance = canvas.gameObject.AddComponent<DialoguePanel>();
			RectTransform panel = UIKit.Panel(canvas.transform, "Panel", new RectOffset(18, 18, 12, 14), 8f, false);
			UIKit.Anchor(panel, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(700, 250));
			instance.speakerText = UIKit.Label(panel, "", 20, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Speaker");
			UIKit.Size(instance.speakerText.gameObject, -1, 28);
			UIKit.Separator(panel);
			instance.bodyText = UIKit.Label(panel, "", 17, UIKit.TextColor, TextAnchor.UpperLeft, FontStyle.Normal, "Body");
			instance.bodyText.supportRichText = false;
			instance.bodyText.lineSpacing = 1.1f;
			UIKit.Size(instance.bodyText.gameObject, -1, -1).flexibleHeight = 1f;
			instance.buttons = UIKit.Row(panel, 34f, 8f, "Buttons");
			canvas.gameObject.SetActive(false);
		}

		void Show(Transform at, string speaker, List<Islanders.Page> pages, Action<int> done)
		{
			this.at = at;
			this.pages = pages;
			this.done = done;
			speakerText.text = speaker ?? "";
			page = 0;
			gameObject.SetActive(true);
			ShowPage();
			openedAt = Time.unscaledTime;
			cursorWasFree = Cursor.visible;
			if (!cursorWasFree) try { RAPI.ToggleCursor(true); } catch { }
		}

		void ShowPage()
		{
			Islanders.Page pg = pages[page];
			bodyText.text = pg.Text;
			Islanders.LastSpeaker = speakerText.text;
			Islanders.LastPage = pg.Text;
			foreach (Transform t in buttons.Cast<Transform>().ToList()) Destroy(t.gameObject);
			UIKit.Label(buttons, pages.Count > 1 ? (page + 1) + " / " + pages.Count : "", 12, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic, "Count");
			if (pg.Choices.Count > 0)
			{
				// (answer numbers count on from the answers of the pages before: answer n fires "choice n")
				int first = pages.Take(page).Sum(x => x.Choices.Count);
				for (int i = 0; i < pg.Choices.Count; i++)
				{
					int n = first + i + 1;
					UIKit.Button(buttons, pg.Choices[i], () => Finish(n), null, -1, 34f, 14);
				}
			}
			else UIKit.Button(buttons, page < pages.Count - 1 ? "Next" : "Goodbye", Advance, "Or the interact key", 130, 34f, 14);
		}

		void Advance()
		{
			if (pages[page].Choices.Count > 0) return; // (an answer must be picked - or Tab/Esc to walk away)
			if (page < pages.Count - 1) { page++; ShowPage(); openedAt = Time.unscaledTime; }
			else Finish(0);
		}

		void Finish(int choice)
		{
			if (!gameObject.activeSelf) return;
			gameObject.SetActive(false);
			if (!cursorWasFree) try { RAPI.ToggleCursor(false); } catch { }
			Action<int> d = done;
			done = null;
			if (d != null) try { d(choice); } catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] After a dialogue: " + ex.Message); }
		}

		void Update()
		{
			if (Time.unscaledTime - openedAt < 0.2f) return;
			bool interact;
			try { interact = MyInput.GetButtonDown("Interact"); } catch { interact = false; }
			if (interact) { Advance(); return; }
			if (Input.GetKeyDown(KeyCode.Tab) || EscKey.Down(this)) { Finish(0); return; }
			Network_Player player = null;
			try { player = ComponentManager<Network_Player>.Value; } catch { }
			if (!LoadSceneManager.IsGameSceneLoaded || (at != null && player != null && Vector3.Distance(player.transform.position, at.position) > 8f)) Finish(0);
		}
	}
}
