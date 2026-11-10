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
	///   talk.text           the dialogue: pages split by a line "---"; lines starting with "&gt;" are answers (up to 4
	///                       a page). QE10 (3.5) branches: a line "#name" starts a branch of its own pages (left out of the
	///                       pages read in order); an answer "&gt; Who are you? -&gt; who +asked" goes to branch "who" and
	///                       sets the island's flag "asked" (its counter to 1; "-asked" back to 0; "end" closes; "?asked"
	///                       / "?!asked" show the answer only while the flag is set / not set). Answer n (in the text's
	///                       order) fires "choice n" (1..4); flags go through the event "answer n" (Expand: count actions,
	///                       so the host sets them for the crew).
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
		public const int MaxChoices = 4;
		/// <summary>QE10: the event an answer's flags run on (answer1.., any number; not shown in the behaviour window).</summary>
		public const string AnswerPrefix = "answer";
		/// <summary>QE10: an answer's "-&gt; end" closes the dialogue; "start" goes back to the first pages.</summary>
		public const string EndLabel = "end", StartLabel = "start";
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
		/// <summary>QE10: one answer: its words, where it goes (Goto: a branch, "end", or none = the dialogue closes), the
		/// flags it sets and clears, the flags it needs set (IfSet) or not set (IfNot), and its number in the whole text (1..).</summary>
		public class Answer
		{
			public string Text = "", Goto = "";
			public List<string> Sets = new List<string>(), Clears = new List<string>(), IfSet = new List<string>(), IfNot = new List<string>();
			public int Number;
			public bool Branches { get { return Goto.Length > 0 && !Goto.Equals(EndLabel, StringComparison.OrdinalIgnoreCase); } }
		}

		public class Page
		{
			public string Text = "";
			public List<string> Choices = new List<string>();
			/// <summary>QE10: the answers (Choices = their words) and the page's branch ("" = the pages read in order).</summary>
			public List<Answer> Answers = new List<Answer>();
			public string Section = "";
		}

		/// <summary>QE10: a branch's line "#name" (one word).</summary>
		public static bool IsLabel(string line, out string label)
		{
			label = null;
			string l = (line ?? "").Trim();
			if (l.Length < 2 || l[0] != '#') return false;
			for (int i = 1; i < l.Length; i++) if (!char.IsLetterOrDigit(l[i]) && l[i] != '_' && l[i] != '-') return false;
			label = l.Substring(1);
			return true;
		}

		/// <summary>QE10: an answer line's words after "&gt;": "text -&gt; branch +flag -flag ?flag ?!flag".</summary>
		public static Answer ParseAnswer(string body)
		{
			var a = new Answer();
			string b = (body ?? "").Trim();
			int arrow = b.LastIndexOf("->", StringComparison.Ordinal);
			if (arrow >= 0)
			{
				foreach (string w in b.Substring(arrow + 2).Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries))
				{
					if (w.StartsWith("?!") && w.Length > 2) a.IfNot.Add(w.Substring(2));
					else if (w.StartsWith("?") && w.Length > 1) a.IfSet.Add(w.Substring(1));
					else if (w.StartsWith("+") && w.Length > 1) a.Sets.Add(w.Substring(1));
					else if (w.StartsWith("-") && w.Length > 1) a.Clears.Add(w.Substring(1));
					else if (a.Goto.Length == 0) a.Goto = w.TrimStart('#');
				}
				b = b.Substring(0, arrow).Trim();
			}
			a.Text = b;
			return a;
		}

		/// <summary>A dialogue's pages ("\n" written as two characters counts as a new line - an action's text is one line).</summary>
		public static List<Page> Parse(string text)
		{
			var pages = new List<Page>();
			var cur = new Page();
			var lines = new List<string>();
			string section = "";
			int number = 0;
			Action flush = () =>
			{
				cur.Text = string.Join("\n", lines.ToArray()).Trim();
				if (cur.Text.Length > 0 || cur.Choices.Count > 0) pages.Add(cur);
				cur = new Page { Section = section };
				lines.Clear();
			};
			foreach (string raw in (text ?? "").Replace("\\n", "\n").Replace("\r", "").Split('\n'))
			{
				string l = raw.Trim(), label;
				if (l == "---") { flush(); continue; }
				if (IsLabel(l, out label)) { flush(); section = label; cur.Section = label; continue; }
				if (l.StartsWith(">"))
				{
					Answer a = ParseAnswer(l.Substring(1));
					if (cur.Answers.Count < MaxChoices && a.Text.Length > 0) { a.Number = ++number; cur.Answers.Add(a); cur.Choices.Add(a.Text); }
					continue;
				}
				lines.Add(raw.TrimEnd());
			}
			flush();
			return pages;
		}

		/// <summary>All answers of a dialogue, in order (answer n fires "choice n").</summary>
		public static List<string> Choices(string text) { return Parse(text).SelectMany(pg => pg.Choices).Take(MaxChoices).ToList(); }

		/// <summary>QE10: every answer of a dialogue, in order (all branches).</summary>
		public static List<Answer> Answers(string text) { return Parse(text).SelectMany(pg => pg.Answers).ToList(); }

		/// <summary>QE10: the branches a dialogue has ("#name" lines), in order.</summary>
		public static List<string> Sections(string text) { return Parse(text).Select(pg => pg.Section).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); }

		/// <summary>QE10: what is wrong with a dialogue's branches (the editor's line): an answer to a branch it doesn't have,
		/// a branch no answer goes to. Null = nothing.</summary>
		public static string BranchProblem(string text)
		{
			List<Page> pages = Parse(text);
			var sections = new HashSet<string>(pages.Select(p => p.Section).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
			var problems = new List<string>();
			foreach (Answer a in pages.SelectMany(p => p.Answers))
				if (a.Branches && !a.Goto.Equals(StartLabel, StringComparison.OrdinalIgnoreCase) && !sections.Contains(a.Goto))
					problems.Add("answer \"" + a.Text + "\" goes to \"" + a.Goto + "\" but there is no line #" + a.Goto);
			var reached = new HashSet<string>(pages.SelectMany(p => p.Answers).Where(a => a.Branches).Select(a => a.Goto), StringComparer.OrdinalIgnoreCase);
			foreach (string sct in sections) if (!reached.Contains(sct)) problems.Add("no answer goes to #" + sct);
			if (pages.Count > 0 && !pages.Any(p => p.Section.Length == 0)) problems.Add("it starts with a branch: write what it says first above the first #name");
			return problems.Count == 0 ? null : string.Join("; ", problems.ToArray());
		}

		/// <summary>QE10: an islander's answers' flags as actions, as if the builder had written them: on.answer n = "count|flag|=1"
		/// (+flag) and "count|flag|=0" (-flag). Shared actions: the host sets the island's counter for the crew, every player
		/// sees it (GapBehaviours counters).</summary>
		public static IDictionary<string, string> Expand(string objectName, IDictionary<string, string> props)
		{
			if (props == null || !Is(objectName)) return props;
			string text = ObjectProps.Get(props, TextProp);
			if (text.IndexOf("->", StringComparison.Ordinal) < 0) return props;
			Dictionary<string, string> d = null;
			foreach (Answer a in Answers(text))
			{
				if (a.Sets.Count == 0 && a.Clears.Count == 0) continue;
				if (d == null) d = new Dictionary<string, string>(props);
				var acts = a.Sets.Select(f => GapBehaviours.CountVerb + "|" + f + "|=1").Concat(a.Clears.Select(f => GapBehaviours.CountVerb + "|" + f + "|=0"));
				d[BehaviourProps.EventKey(AnswerPrefix + a.Number)] = string.Join("\n", acts.ToArray());
			}
			return d ?? props;
		}

		/// <summary>QE10: whether an answer shows now: its ?flags set and ?!flags not (the island's counters; none known = 0).</summary>
		public static bool Shows(Answer a, Func<string, int> flag)
		{
			foreach (string f in a.IfSet) if ((flag != null ? flag(f) : 0) < 1) return false;
			foreach (string f in a.IfNot) if ((flag != null ? flag(f) : 0) >= 1) return false;
			return true;
		}

		/// <summary>QE10: the flags' values on an island for a dialogue (counters: host-set, sent to every player).</summary>
		internal static Func<string, int> FlagsOf(IslandWorldState.Entry e) { return f => e != null ? GapBehaviours.Counter(e, f) : 0; }

		/// <summary>A player picked answer n (the text's order): its "choice n" event (1..4) and its flags (event "answer n").</summary>
		internal static void Picked(IslandWorldState.Entry e, int index, string name, int n)
		{
			Picks++;
			if (e == null || n <= 0) return;
			Debug.Log("[CUSTOM ISLANDS] '" + name + "' on '" + e.HostName + "': answer " + n);
			if (n <= MaxChoices) Behaviours.Fire(e, index, ChoicePrefix + n, true);
			Behaviours.Fire(e, index, AnswerPrefix + n, true);
		}

		/// <summary>Answers picked on this machine (tests).</summary>
		public static int Picks;

		/// <summary>QE10 (the editor's button): the text with one more answer "&gt; Answer N -&gt; branchN" at the end of the
		/// first lines (before the first branch) and the branch "#branchN" at the end.</summary>
		public static string WithBranch(string text)
		{
			var lines = (text ?? "").Replace("\r", "").Split('\n').ToList();
			if (lines.Count == 1 && lines[0].Trim().Length == 0) lines = new List<string> { "Hello there." };
			var used = new HashSet<string>(Sections(text), StringComparer.OrdinalIgnoreCase);
			int k = 1;
			while (used.Contains("branch" + k)) k++;
			int first = lines.FindIndex(l => { string lb; return IsLabel(l, out lb); });
			int at = first < 0 ? lines.Count : first;
			while (at > 0 && lines[at - 1].Trim().Length == 0) at--;
			lines.Insert(at, "> Answer " + k + " -> branch" + k);
			lines.Add("#branch" + k);
			lines.Add("What it says after answer " + k + ".");
			return string.Join("\n", lines.ToArray());
		}

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

		/// <summary>A player talked to an islander (the dialogue closed; choice = the last answer picked, 0 = none - its
		/// events ran when it was picked: Picked).</summary>
		internal static void Talked(IslandWorldState.Entry e, Transform at, int index, string name, int choice)
		{
			Talks++;
			if (e == null) return;
			if (!ContentState.IsUsed(e, TalkedBase + index)) ContentState.MarkUsed(at, TalkedBase + index);
			Debug.Log("[CUSTOM ISLANDS] Talked to '" + name + "' on '" + e.HostName + "'" + (choice > 0 ? ", answer " + choice : ""));
			Behaviours.Fire(e, index, TalkEvent, true);
			QuestSteps.HandIn(e, name); // (a "Bring items" step for them: the items are handed over - QE4)
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
			// (QE10: its branches work; its flags don't - only an islander's own dialogue has the answer events that set them)
			DialoguePanel.Open(at, speaker, a.Arg, null, n =>
			{
				if (n > 0 && n <= MaxChoices && e != null) Behaviours.Fire(e, index, ChoicePrefix + n, true);
			}, FlagsOf(e));
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
		/// <summary>Raft's character it shows (null: the marker figure).</summary>
		public SO_Character Character { get; private set; }

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
			Character = c;
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
			DialoguePanel.Open(at, name, ObjectProps.Get(Ref.Props, Islanders.TextProp), choice => Islanders.Talked(e, at, index, name, choice),
				n => Islanders.Picked(e, index, name, n), Islanders.FlagsOf(e));
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
		int page, lastPicked;
		Transform at;
		Action<int> done, picked;
		Func<string, int> flag;
		/// <summary>QE10: flags this dialogue set or cleared (a player's own picks show at once; the host's counter follows).</summary>
		readonly Dictionary<string, int> setHere = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		float openedAt;
		/// <summary>What last closed the dialogue on its own: a key, the scene, the distance (tests).</summary>
		public static string LastClose;

		public static void Open(Transform at, string speaker, string text, Action<int> done) { Open(at, speaker, text, done, null, null); }

		/// <summary>Opens a dialogue. picked: an answer was picked (its number in the text, 1..), at once - a branch goes on
		/// after it; done: the dialogue closed (the last answer picked, 0 = none). flag: the island's flags (?flag answers).</summary>
		public static void Open(Transform at, string speaker, string text, Action<int> done, Action<int> picked, Func<string, int> flag)
		{
			List<Islanders.Page> pages = Islanders.Parse(text);
			if (pages.Count == 0) pages.Add(new Islanders.Page { Text = "..." });
			if (instance == null) Build();
			if (IsOpen) instance.Finish(0);
			instance.Show(at, speaker, pages, done, picked, flag);
		}

		/// <summary>Next page, or closes after the last (tests).</summary>
		public static void Next() { if (IsOpen) instance.Advance(); }

		/// <summary>Picks answer n (its number in the text, 1..) on the page shown (tests); not shown there: answer n is
		/// picked and the dialogue closes (as before QE10).</summary>
		public static void Pick(int n)
		{
			if (!IsOpen) return;
			Islanders.Answer a = instance.Visible().FirstOrDefault(x => x.Number == n);
			if (a != null) { instance.Choose(a); return; }
			instance.lastPicked = n;
			if (instance.picked != null) instance.Call(instance.picked, n);
			instance.Finish(n);
		}

		/// <summary>QE10 (tests): the branch shown ("" = the first pages) and the answers shown on this page.</summary>
		public static string ShownSection { get { return IsOpen ? instance.pages[instance.page].Section : null; } }
		public static List<string> ShownAnswers { get { return IsOpen ? instance.Visible().Select(a => a.Text).ToList() : new List<string>(); } }

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

		void Show(Transform at, string speaker, List<Islanders.Page> pages, Action<int> done, Action<int> picked, Func<string, int> flag)
		{
			this.at = at;
			this.pages = pages;
			this.done = done;
			this.picked = picked;
			this.flag = flag;
			setHere.Clear();
			lastPicked = 0;
			speakerText.text = speaker ?? "";
			// (the first page read in order; a text of branches only starts at its first)
			page = Math.Max(0, pages.FindIndex(p => p.Section.Length == 0));
			gameObject.SetActive(true);
			ShowPage();
			openedAt = Time.unscaledTime;
			ModCursor.Take(this);
		}

		void ShowPage()
		{
			Islanders.Page pg = pages[page];
			bodyText.text = pg.Text;
			Islanders.LastSpeaker = speakerText.text;
			Islanders.LastPage = pg.Text;
			foreach (Transform t in buttons.Cast<Transform>().ToList()) Destroy(t.gameObject);
			// (QE10: the page count is its branch's)
			List<Islanders.Page> same = pages.Where(x => string.Equals(x.Section, pg.Section, StringComparison.OrdinalIgnoreCase)).ToList();
			UIKit.Label(buttons, same.Count > 1 ? (same.IndexOf(pg) + 1) + " / " + same.Count : "", 12, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic, "Count");
			List<Islanders.Answer> shown = Visible();
			if (shown.Count > 0)
			{
				// (answer numbers count on through the whole text: answer n fires "choice n"; hidden ones keep theirs)
				foreach (Islanders.Answer a in shown)
				{
					Islanders.Answer chosen = a;
					UIKit.Button(buttons, a.Text, () => Choose(chosen), null, -1, 34f, 14);
				}
			}
			else UIKit.Button(buttons, NextPage() >= 0 ? "Next" : "Goodbye", Advance, "Or the interact key", 130, 34f, 14);
		}

		/// <summary>The page's answers that show now (QE10: ?flag / ?!flag).</summary>
		List<Islanders.Answer> Visible()
		{
			Func<string, int> now = f => { int v; return setHere.TryGetValue(f, out v) ? v : flag != null ? flag(f) : 0; };
			return pages[page].Answers.Where(a => Islanders.Shows(a, now)).ToList();
		}

		/// <summary>The next page of the same branch, or -1.</summary>
		int NextPage()
		{
			string sct = pages[page].Section;
			for (int i = page + 1; i < pages.Count; i++) if (string.Equals(pages[i].Section, sct, StringComparison.OrdinalIgnoreCase)) return i;
			return -1;
		}

		void Advance()
		{
			if (Visible().Count > 0) return; // (an answer must be picked - or Tab/Esc to walk away)
			int next = NextPage();
			if (next >= 0) { page = next; ShowPage(); openedAt = Time.unscaledTime; }
			else Finish(lastPicked);
		}

		/// <summary>QE10: an answer picked: its events at once (picked), then its branch's first page, or the dialogue closes.</summary>
		void Choose(Islanders.Answer a)
		{
			lastPicked = a.Number;
			foreach (string f in a.Sets) setHere[f] = 1;
			foreach (string f in a.Clears) setHere[f] = 0;
			if (picked != null) Call(picked, a.Number);
			if (!gameObject.activeSelf) return; // (its events closed it)
			int to = !a.Branches ? -1 : a.Goto.Equals(Islanders.StartLabel, StringComparison.OrdinalIgnoreCase)
				? pages.FindIndex(p => p.Section.Length == 0)
				: pages.FindIndex(p => string.Equals(p.Section, a.Goto, StringComparison.OrdinalIgnoreCase));
			if (to >= 0) { page = to; ShowPage(); openedAt = Time.unscaledTime; }
			else Finish(a.Number);
		}

		void Call(Action<int> f, int n) { try { f(n); } catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] An answer's events: " + ex.Message); } }

		void Finish(int choice)
		{
			if (!gameObject.activeSelf) return;
			gameObject.SetActive(false);
			ModCursor.Give(this);
			Action<int> d = done;
			done = null;
			picked = null;
			if (d != null) try { d(choice); } catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] After a dialogue: " + ex.Message); }
		}

		void Update()
		{
			if (Time.unscaledTime - openedAt < 0.2f) return;
			bool interact;
			try { interact = MyInput.GetButtonDown("Interact"); } catch { interact = false; }
			if (interact) { LastClose = "the interact key"; Advance(); return; }
			if (Input.GetKeyDown(KeyCode.Tab) || EscKey.Down(this)) { LastClose = "Tab or Esc"; Finish(0); return; }
			Network_Player player = null;
			try { player = ComponentManager<Network_Player>.Value; } catch { }
			if (!LoadSceneManager.IsGameSceneLoaded) { LastClose = "no game scene"; Finish(0); return; }
			if (at != null && player != null && Vector3.Distance(player.transform.position, at.position) > 8f) { LastClose = "the player " + Vector3.Distance(player.transform.position, at.position).ToString("F1") + " m away"; Finish(0); }
		}
	}
}
