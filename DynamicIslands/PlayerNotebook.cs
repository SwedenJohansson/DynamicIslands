using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RaftModLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// PF1: the player's own notebook - free-text pages with titles, typed in a world. Each player has their own, per world
	/// save, in a file on their own computer next to that world's save (Raft's world folder, CustomIslands-notebook.txt);
	/// it is never sent over the network. Before a world has a folder of its own (a new world not saved yet, a received
	/// world not found) it is kept in Mods\DynamicIslands\worlds\&lt;world id&gt;-notebook.txt and moved over later.
	/// Opened with its own key (U, Defaults' Keys) or from the journal; notes and journal pages can be copied into it.
	/// </summary>
	public static class PlayerNotebook
	{
		public const string FileName = "CustomIslands-notebook.txt";
		const string PageMark = "=== ";

		public class Page { public string Title = "", Text = ""; }

		public static readonly List<Page> Pages = new List<Page>();
		/// <summary>The world (its id) the pages belong to, or null outside a world.</summary>
		public static string LoadedFor { get; private set; }
		/// <summary>Tests: where the pages were last read from or written to.</summary>
		public static string LastPath { get; private set; }
		/// <summary>The pages changed (the window refills its list).</summary>
		public static event Action Changed;

		static bool dirty;
		static float saveAt;
		static string path;

		/// <summary>The world being played (its id), or null at the menu, in the editor or before Raft knows the world's id.</summary>
		static string WorldKey
		{
			get
			{
				if (!LoadSceneManager.IsGameSceneLoaded || DynamicIslands.InEditor()) return null;
				Guid g = SaveAndLoad.WorldGuid;
				return g == Guid.Empty ? null : g.ToString();
			}
		}

		/// <summary>Every frame: loads the pages of the world just entered (saving the last world's), saves a few seconds after typing.</summary>
		public static void Tick()
		{
			string key = WorldKey;
			if (key != LoadedFor)
			{
				if (LoadedFor != null) { PlayerNotebookWindow.Close(); Save(); }
				Load(key);
			}
			if (dirty && Time.unscaledTime >= saveAt) Save();
		}

		/// <summary>Where this world's notebook is kept on this computer.</summary>
		public static string PathFor(string worldKey)
		{
			string folder = null;
			try
			{
				// (the host's own save folder is the world being played: another world's key looks for its marked folder)
				if (Raft_Network.IsHost && worldKey == WorldKey) folder = WorldCopy.RaftWorldFolder;
				else folder = ReceivedFolder(worldKey);
			}
			catch { }
			return folder != null ? Path.Combine(folder, FileName) : FallbackPath(worldKey);
		}

		static string FallbackPath(string worldKey) { return Path.Combine(Path.Combine(DynamicIslands.assetpath, "worlds"), worldKey + "-notebook.txt"); }

		/// <summary>A player who joined: the world folder the host's save was filed in (WorldSaveShare marks it with the world's id).</summary>
		static string ReceivedFolder(string worldKey)
		{
			string root = SaveAndLoad.WorldPath;
			if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return null;
			foreach (string dir in Directory.GetDirectories(root))
			{
				string marker = Path.Combine(dir, WorldSaveShare.MarkerFile);
				try { if (File.Exists(marker) && File.ReadAllLines(marker).Any(l => l.Trim().Equals(worldKey, StringComparison.OrdinalIgnoreCase))) return dir; }
				catch { }
			}
			return null;
		}

		static void Load(string key)
		{
			Pages.Clear();
			dirty = false;
			LoadedFor = key;
			path = null;
			if (key != null)
			{
				path = PathFor(key);
				string from = File.Exists(path) ? path : File.Exists(FallbackPath(key)) ? FallbackPath(key) : null;
				if (from != null)
					try
					{
						Pages.AddRange(Parse(File.ReadAllLines(from)));
						LastPath = from;
						// (kept in the fallback while the world had no folder: moved next to the save now)
						if (from != path) dirty = true;
						Debug.Log("[CUSTOM ISLANDS] Your notebook: " + Pages.Count + " page(s) from " + from);
					}
					catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not read your notebook (" + from + "): " + e.Message); }
			}
			if (Changed != null) Changed();
		}

		/// <summary>Writes the pages now (the window calls it on closing and on switching pages).</summary>
		public static void Save()
		{
			if (!dirty || LoadedFor == null) { dirty = false; return; }
			dirty = false;
			// (still in that world: its folder may have come since - a new world's first save)
			string to = WorldKey == LoadedFor ? PathFor(LoadedFor) : path;
			if (to == null) return;
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(to));
				SafeFile.WriteAllLines(to, Serialize(Pages));
				LastPath = to;
				path = to;
				string fallback = FallbackPath(LoadedFor);
				if (to != fallback && File.Exists(fallback)) File.Delete(fallback);
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not save your notebook (" + to + "): " + e.Message); dirty = true; saveAt = Time.unscaledTime + 30f; }
		}

		/// <summary>Something was typed: saved a few seconds later (and on closing).</summary>
		public static void MarkChanged()
		{
			dirty = true;
			saveAt = Time.unscaledTime + 4f;
		}

		/// <summary>A new page at the end; its index.</summary>
		public static int AddPage(string title, string text)
		{
			Pages.Add(new Page { Title = OneLine(title), Text = (text ?? "").Replace("\r", "") });
			MarkChanged();
			if (Changed != null) Changed();
			return Pages.Count - 1;
		}

		public static void RemovePage(int index)
		{
			if (index < 0 || index >= Pages.Count) return;
			Pages.RemoveAt(index);
			MarkChanged();
			if (Changed != null) Changed();
		}

		/// <summary>A note's or a journal page's text copied into the notebook as a new page (saved at once).</summary>
		public static bool CopyIn(string title, string text)
		{
			if (LoadedFor == null) { IslandInfo.ShowMessage("Your notebook is kept per world: open a world first."); return false; }
			AddPage(string.IsNullOrEmpty(title) ? "A copied note" : title, text);
			Save();
			IslandInfo.ShowMessage("Copied into your notebook (" + ModKeys.Name(ModKeys.NotebookKey.Key) + "): '" + Pages.Last().Title + "'");
			return true;
		}

		public static string OneLine(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim(); }

		/// <summary>The file's lines: a comment, then each page as "=== title" and its text (a text line that starts with === or \ gets a \ in front).</summary>
		public static string[] Serialize(IEnumerable<Page> pages)
		{
			var lines = new List<string> { "# Custom Islands: your own notebook for this world. Only on this computer, never sent to other players. Pages start at \"" + PageMark.Trim() + " title\"." };
			foreach (Page p in pages)
			{
				lines.Add(PageMark + OneLine(p.Title));
				if (p.Text.Length == 0) continue;
				foreach (string l in p.Text.Replace("\r", "").Split('\n'))
					lines.Add(l.StartsWith("===") || l.StartsWith("\\") ? "\\" + l : l);
			}
			return lines.ToArray();
		}

		public static List<Page> Parse(string[] lines)
		{
			var pages = new List<Page>();
			Page p = null;
			var text = new List<string>();
			Action close = () => { if (p != null) { p.Text = string.Join("\n", text.ToArray()); pages.Add(p); } text.Clear(); };
			foreach (string raw in lines)
			{
				if (raw.StartsWith(PageMark.Trim())) { close(); p = new Page { Title = raw.Substring(Math.Min(raw.Length, PageMark.Length)).Trim() }; continue; }
				if (p == null) continue; // (the comment before the first page)
				text.Add(raw.StartsWith("\\") ? raw.Substring(1) : raw);
			}
			close();
			return pages;
		}
	}

	/// <summary>PF1: the player notebook's window - the pages on the left, the shown page's title and text on Raft's tan sheet.</summary>
	public class PlayerNotebookWindow : MonoBehaviour
	{
		static PlayerNotebookWindow instance;
		public static bool IsOpen { get { return instance != null && instance.gameObject.activeSelf; } }
		public static KeyCode Key { get { return ModKeys.NotebookKey.Key; } }

		RectTransform pageList;
		Text countText, emptyText;
		InputField titleField, textField;
		Button deleteButton;
		int shown = -1;
		float deleteArmedUntil;
		bool cursorWasFree, dirtyList;
		readonly List<Button> pageButtons = new List<Button>();
		readonly List<Behaviour> held = new List<Behaviour>();

		/// <summary>Every frame from the mod: the notebook key opens and closes it in a world.</summary>
		public static void Tick()
		{
			PlayerNotebook.Tick();
			if (!LoadSceneManager.IsGameSceneLoaded || DynamicIslands.InEditor()) { if (IsOpen) instance.Hide(); return; }
			if (!Input.GetKeyDown(Key) || ModKeys.Listening || NoteReader.IsOpen) return;
			if (IsOpen) { if (!instance.Typing) instance.Hide(); return; }
			if (OtherTyping()) return;
			Open();
		}

		/// <summary>A text field outside the notebook (chat, console) has the keyboard.</summary>
		static bool OtherTyping()
		{
			GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
			InputField f = sel != null ? sel.GetComponent<InputField>() : null;
			return f != null && f.isFocused;
		}

		bool Typing { get { return (titleField != null && titleField.isFocused) || (textField != null && textField.isFocused); } }

		/// <summary>While open Raft's own controls (walking, hotbar, notebook keys) do nothing: the keys are for typing (NotebookInputBlock).</summary>
		public static bool HoldsInput { get { return IsOpen; } }

		public static void Open()
		{
			if (PlayerNotebook.LoadedFor == null) { IslandInfo.ShowMessage("Your notebook is kept per world: it opens in a world."); return; }
			if (instance == null) Build();
			JournalWindow.Close();
			instance.Show();
		}

		public static void Close() { if (instance != null) instance.Hide(); }

		static void Build()
		{
			Canvas canvas = UIKit.CreateCanvas("CustomIslands_PlayerNotebook", 491);
			DontDestroyOnLoad(canvas.gameObject);
			instance = canvas.gameObject.AddComponent<PlayerNotebookWindow>();
			PlayerNotebook.Changed += () => { if (instance != null) instance.dirtyList = true; };

			RectTransform panel = UIKit.Panel(canvas.transform, "Panel", new RectOffset(18, 18, 14, 16), 10f, false);
			UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980, 620));
			RectTransform head = UIKit.Row(panel, 34f, 8f, "Head");
			UIKit.Label(head, "MY NOTEBOOK", 22, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
			instance.countText = UIKit.Label(head, "", 13, UIKit.TextMuted, TextAnchor.MiddleRight);
			UIKit.Separator(panel);

			RectTransform body = UIKit.Row(panel, 490f, 14f, "Body");
			RectTransform left = UIKit.Group(body, "Pages", "Group_Pages");
			UIKit.Size(left.gameObject, 280);
			RectTransform listBox = UIKit.Rect("ListBox", left);
			UIKit.Size(listBox.gameObject, -1, 410);
			ScrollRect s1;
			instance.pageList = UIKit.ScrollList(listBox, out s1, 4f);
			UIKit.Stretch((RectTransform)s1.transform);
			instance.emptyText = UIKit.Label(instance.pageList, "<i>No pages yet. New page starts one; notes and journal pages can be copied in.</i>", 13, UIKit.TextMuted);
			RectTransform listButtons = UIKit.Row(left, 30f, 6f, "ListButtons");
			UIKit.Button(listButtons, "New page", () => instance.NewPage(), "A new empty page at the end", -1, 30f, 13);
			instance.deleteButton = UIKit.Button(listButtons, "Delete", () => instance.DeleteShown(), "Removes the shown page (click twice)", -1, 30f, 13);

			RectTransform sheet = UIKit.Rect("Sheet", body);
			UIKit.Size(sheet.gameObject, -1, -1).flexibleWidth = 1f;
			UIKit.ParchmentSheet(sheet);
			UIKit.Vertical(sheet.gameObject, 10f, new RectOffset(24, 24, 20, 18));
			instance.titleField = UIKit.Field(sheet, "Title", "", 40f, "The page's title");
			instance.textField = UIKit.TextArea(sheet, "Write here: what you found, where to go, what to bring...", 380f, "Your own notes: only on this computer, saved with this world");
			UIKit.Size(instance.textField.gameObject, -1, -1).flexibleHeight = 1f;
			instance.titleField.characterLimit = 80;
			instance.textField.characterLimit = 20000;
			instance.titleField.onValueChanged.AddListener(v => instance.Edited(true, v));
			instance.textField.onValueChanged.AddListener(v => instance.Edited(false, v));
			instance.titleField.onEndEdit.AddListener(v => instance.RefreshList());

			RectTransform bottom = UIKit.Row(panel, 34f, 8f, "Bottom");
			UIKit.Label(bottom, "", 13, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic, "Hint").text =
				"Saved on this computer with this world, never sent to others. " + ModKeys.Name(Key) + " (not while typing) or Esc to close";
			UIKit.Button(bottom, "Journal", () => { Close(); JournalWindow.Open(); }, "The crew's journal (" + ModKeys.Name(JournalWindow.Key) + ")", 130, 34);
			UIKit.Button(bottom, "Close", Close, null, 120, 34);
			canvas.gameObject.SetActive(false);
		}

		void Show()
		{
			gameObject.SetActive(true);
			cursorWasFree = Cursor.visible;
			if (!cursorWasFree) try { RAPI.ToggleCursor(true); } catch { }
			Hold(true);
			if (shown < 0 || shown >= PlayerNotebook.Pages.Count) shown = PlayerNotebook.Pages.Count - 1;
			RefreshList();
			ShowPage(shown);
		}

		void Hide()
		{
			if (!gameObject.activeSelf) return;
			if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
			gameObject.SetActive(false);
			Hold(false);
			if (!cursorWasFree) try { RAPI.ToggleCursor(false); } catch { }
			PlayerNotebook.Save();
		}

		/// <summary>The player stands still while the notebook is open (as in a camera moment).</summary>
		void Hold(bool on)
		{
			if (!on) { foreach (Behaviour b in held) if (b != null) b.enabled = true; held.Clear(); return; }
			try
			{
				Network_Player p = ComponentManager<Network_Player>.Value;
				if (p == null) return;
				foreach (MouseLook ml in p.GetComponentsInChildren<MouseLook>(true).Where(x => x.enabled)) { ml.enabled = false; held.Add(ml); }
				if (p.PersonController != null && p.PersonController.enabled) { p.PersonController.enabled = false; held.Add(p.PersonController); }
			}
			catch { }
		}

		void OnDisable() { Hold(false); }

		void Update()
		{
			if (EscKey.Down(this)) { Hide(); return; }
			if (dirtyList) RefreshList();
			// (Esc in a field puts back its text from before typing - the page keeps what was typed)
			if (shown >= 0 && shown < PlayerNotebook.Pages.Count)
			{
				PlayerNotebook.Page p = PlayerNotebook.Pages[shown];
				if (!titleField.isFocused && titleField.text != p.Title) titleField.text = p.Title;
				if (!textField.isFocused && textField.text != p.Text) textField.text = p.Text;
			}
			if (deleteArmedUntil > 0 && Time.unscaledTime > deleteArmedUntil) { deleteArmedUntil = 0; UIKit.LabelOf(deleteButton).text = "Delete"; }
		}

		void Edited(bool title, string value)
		{
			// (only what the player types: a text set by the window, or put back on Esc, is not an edit)
			InputField f = title ? titleField : textField;
			if (!f.isFocused && !typingForTest) return;
			if (shown < 0 || shown >= PlayerNotebook.Pages.Count)
			{
				// (typing with no page: the first page starts)
				shown = PlayerNotebook.AddPage("", "");
			}
			PlayerNotebook.Page p = PlayerNotebook.Pages[shown];
			if (title) p.Title = PlayerNotebook.OneLine(value); else p.Text = (value ?? "").Replace("\r", "");
			PlayerNotebook.MarkChanged();
		}

		void NewPage()
		{
			PlayerNotebook.Save();
			ShowPage(PlayerNotebook.AddPage("Page " + (PlayerNotebook.Pages.Count + 1), ""));
			RefreshList();
		}

		void DeleteShown()
		{
			if (shown < 0 || shown >= PlayerNotebook.Pages.Count) return;
			if (deleteArmedUntil <= 0) { deleteArmedUntil = Time.unscaledTime + 3f; UIKit.LabelOf(deleteButton).text = "Sure?"; return; }
			deleteArmedUntil = 0;
			UIKit.LabelOf(deleteButton).text = "Delete";
			PlayerNotebook.RemovePage(shown);
			PlayerNotebook.Save();
			ShowPage(Mathf.Min(shown, PlayerNotebook.Pages.Count - 1));
			RefreshList();
		}

		void ShowPage(int index)
		{
			if (index != shown) PlayerNotebook.Save();
			shown = index;
			bool has = index >= 0 && index < PlayerNotebook.Pages.Count;
			titleField.text = has ? PlayerNotebook.Pages[index].Title : "";
			textField.text = has ? PlayerNotebook.Pages[index].Text : "";
			for (int i = 0; i < pageButtons.Count; i++) UIKit.SetLook(pageButtons[i], i == shown ? UIKit.Look.Chosen : UIKit.Look.Plain);
		}

		void RefreshList()
		{
			dirtyList = false;
			foreach (Button b in pageButtons) if (b != null) Destroy(b.gameObject);
			pageButtons.Clear();
			for (int i = 0; i < PlayerNotebook.Pages.Count; i++)
			{
				int index = i;
				string t = PlayerNotebook.Pages[i].Title;
				Button b = UIKit.Button(pageList, t.Length > 0 ? t : "(no title)", () => ShowPage(index), null, -1, 30f, 13);
				UIKit.SetLook(b, i == shown ? UIKit.Look.Chosen : UIKit.Look.Plain);
				pageButtons.Add(b);
			}
			emptyText.gameObject.SetActive(PlayerNotebook.Pages.Count == 0);
			emptyText.transform.SetAsFirstSibling();
			countText.text = PlayerNotebook.Pages.Count + " page(s) · only on this computer";
			if (shown >= PlayerNotebook.Pages.Count) ShowPage(PlayerNotebook.Pages.Count - 1);
		}

		#region Tests

		static bool typingForTest;

		/// <summary>Tests: types a title and text into the shown page as the player would (a new page when none is shown).</summary>
		internal static void TypeForTest(string title, string text)
		{
			if (!IsOpen) return;
			typingForTest = true;
			try
			{
				if (title != null) { instance.titleField.text = title; instance.Edited(true, title); }
				if (text != null) { instance.textField.text = text; instance.Edited(false, text); }
			}
			finally { typingForTest = false; }
			instance.RefreshList();
		}

		internal static void NewPageForTest() { if (IsOpen) instance.NewPage(); }
		internal static int ShownIndex { get { return IsOpen ? instance.shown : -1; } }
		internal static string ShownTitle { get { return IsOpen ? instance.titleField.text : null; } }
		internal static string ShownText { get { return IsOpen ? instance.textField.text : null; } }
		internal static int ListedPages { get { return IsOpen ? instance.pageButtons.Count : -1; } }
		/// <summary>Tests: whether the player's walking is held.</summary>
		internal static bool PlayerHeld { get { return IsOpen && instance.held.Count > 0; } }

		#endregion
	}

	/// <summary>PF1: while the notebook is open Raft's own controls (MyInput) read nothing - typing doesn't walk, switch the hotbar or open menus.</summary>
	[HarmonyPatch(typeof(MyInput), "GetButtonDown", new Type[] { typeof(string) })]
	static class NotebookInputBlockDown
	{
		static bool Prefix(ref bool __result) { if (!PlayerNotebookWindow.HoldsInput) return true; __result = false; return false; }
	}

	[HarmonyPatch(typeof(MyInput), "GetButton", new Type[] { typeof(string) })]
	static class NotebookInputBlockHeld
	{
		static bool Prefix(ref bool __result) { if (!PlayerNotebookWindow.HoldsInput) return true; __result = false; return false; }
	}

	[HarmonyPatch(typeof(MyInput), "GetAxis", new Type[] { typeof(string) })]
	static class NotebookInputBlockAxis
	{
		static bool Prefix(ref float __result) { if (!PlayerNotebookWindow.HoldsInput) return true; __result = 0f; return false; }
	}
}
