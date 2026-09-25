using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>A story item an island defines: an id (what checks and chests use), a name, a picture and a description.</summary>
	public class StoryItemDef
	{
		public string Id = "", Name = "", Icon = "", Description = "";

		/// <summary>"id|name|icon|description"</summary>
		public static StoryItemDef Parse(string line)
		{
			string[] p = (line ?? "").Split('|');
			if (p.Length < 1 || p[0].Trim().Length == 0) return null;
			return new StoryItemDef { Id = p[0].Trim(), Name = p.Length > 1 ? p[1].Trim() : "", Icon = p.Length > 2 ? p[2].Trim() : "", Description = p.Length > 3 ? string.Join("|", p.Skip(3).ToArray()).Trim() : "" };
		}

		public string ToLine() { return Clean(Id) + "|" + Clean(Name) + "|" + Clean(Icon) + "|" + (Description ?? "").Replace("\n", " ").Trim(); }

		static string Clean(string s) { return (s ?? "").Replace("|", "/").Replace("\n", " ").Trim(); }

		public string ShownName { get { return Name.Length > 0 ? Name : Id; } }

		public StoryItemDef Copy() { return new StoryItemDef { Id = Id, Name = Name, Icon = Icon, Description = Description }; }
	}

	/// <summary>
	/// Story items (roadmap 1.3): things of the story that aren't Raft items - a key, a map piece, a captain's log.
	/// An island defines them (setting story.items, one "id|name|icon|description" per line); in a world the crew
	/// holds them together (StoryBook), like Raft's own quest items. Anything that takes items takes them as
	/// "story:&lt;id&gt;": chests, zones, "give" actions and quest rewards give them, "has" and "take" checks look for them.
	/// The picture is one of Raft's quest items ("quest:&lt;type&gt;": keys, key cards, tools...) or any Raft item.
	/// Raft's own quest items can't be used for this: they are a fixed list, and new Raft items would break saves
	/// when the mod is missing.
	/// </summary>
	public static class StoryItems
	{
		public const string Key = "story.items", Prefix = "story:", QuestIcon = "quest:";

		public static bool IsStory(string item) { return item != null && item.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase); }
		public static string IdOf(string item) { return IsStory(item) ? item.Substring(Prefix.Length).Trim() : (item ?? "").Trim(); }
		public static string Ref(string id) { return Prefix + id; }

		public static List<StoryItemDef> Of(IDictionary<string, string> props)
		{
			return ObjectProps.Get(props, Key).Split('\n').Select(StoryItemDef.Parse).Where(d => d != null).ToList();
		}

		public static string Text(IEnumerable<StoryItemDef> defs) { return string.Join("\n", defs.Where(d => d.Id.Length > 0).Select(d => d.ToLine()).ToArray()); }

		/// <summary>A story item's definition: held by the crew, on the island being edited, or on any island of the world.</summary>
		public static StoryItemDef Find(string id)
		{
			id = IdOf(id);
			if (id.Length == 0) return null;
			StoryItemDef d = StoryBook.DefOf(id);
			if (d != null) return d;
			if (DynamicIslands.InEditor() && DynamicIslands.currentIslandProps != null)
			{
				d = Of(DynamicIslands.currentIslandProps).FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
				if (d != null) return d;
			}
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				d = Of(IslandCache.PropsOf(e)).FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
				if (d != null) return d;
			}
			return null;
		}

		public static string Label(string item)
		{
			StoryItemDef d = Find(item);
			return d != null ? d.ShownName : IdOf(item);
		}

		/// <summary>A free id for a new story item, from its name ("Old key" -> "old-key", "old-key-2"...).</summary>
		public static string NewId(IEnumerable<StoryItemDef> existing, string name)
		{
			string b = new string((name ?? "item").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
			if (b.Length == 0) b = "item";
			string id = b;
			for (int i = 2; existing.Any(d => d.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); i++) id = b + "-" + i;
			return id;
		}

		#region Pictures

		static SO_QuestItem[] questItems;

		/// <summary>Raft's quest items (their pictures are story item pictures).</summary>
		public static SO_QuestItem[] QuestItems
		{
			get
			{
				if (questItems == null)
				{
					try { questItems = Resources.LoadAll<SO_QuestItem>("SO_QuestItems").Where(q => q != null && q.itemImage != null).ToArray(); }
					catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Raft's quest items: " + e.Message); questItems = new SO_QuestItem[0]; }
				}
				return questItems;
			}
		}

		public static Sprite IconSprite(string icon)
		{
			if (string.IsNullOrEmpty(icon)) return null;
			if (icon.StartsWith(QuestIcon))
			{
				string type = icon.Substring(QuestIcon.Length);
				SO_QuestItem q = QuestItems.FirstOrDefault(x => x.questItemType.ToString() == type);
				return q != null ? q.itemImage : null;
			}
			return ContentCatalog.ItemSprite(icon);
		}

		#endregion
	}

	/// <summary>
	/// The crew's story items and journal pages in a world. Saved in the world's island list file (@story.item and
	/// @story.page lines), kept by the host and sent to everyone (IslandNetMessage.Story): a client asks the host for
	/// a change and shows it at once; the host's copy wins. Pages come from custom notes (the first time someone
	/// reads one) and from "journal" actions.
	/// </summary>
	public static class StoryBook
	{
		public class Held
		{
			public StoryItemDef Def;
			public int Count;
		}

		public class Page
		{
			public string Key = "", Title = "", Text = "", Island = "";
			public int Day;
		}

		static readonly Dictionary<string, Held> held = new Dictionary<string, Held>(StringComparer.OrdinalIgnoreCase);
		static readonly List<Page> pages = new List<Page>();

		/// <summary>Raised on every machine when the items or pages change (the journal and tests listen).</summary>
		public static event Action Changed;

		public static IEnumerable<Held> Items { get { return held.Values.Where(h => h.Count > 0).OrderBy(h => h.Def.ShownName, StringComparer.OrdinalIgnoreCase); } }
		public static IList<Page> Pages { get { return pages; } }
		public static bool HasState { get { return held.Count > 0 || pages.Count > 0; } }

		public static int Count(string id)
		{
			Held h;
			return held.TryGetValue(StoryItems.IdOf(id), out h) ? h.Count : 0;
		}

		public static StoryItemDef DefOf(string id)
		{
			Held h;
			return held.TryGetValue(StoryItems.IdOf(id), out h) ? h.Def : null;
		}

		static int Today { get { try { return WorldManager.DayCounter; } catch { return 0; } } }

		public static void Reset()
		{
			held.Clear();
			pages.Clear();
			Raise();
		}

		static void Raise() { if (Changed != null) try { Changed(); } catch { } }

		#region Changes

		/// <summary>The crew gets story items (this machine's player found them: they are told).</summary>
		public static void Give(string id, int count)
		{
			id = StoryItems.IdOf(id);
			if (id.Length == 0 || count <= 0) return;
			StoryItemDef d = StoryItems.Find(id) ?? new StoryItemDef { Id = id };
			Change("give", Fields(d.Id, count.ToString(CultureInfo.InvariantCulture), d.Name, d.Icon, d.Description));
			IslandInfo.ShowMessage("Story item: " + d.ShownName + (count > 1 ? " \u00D7" + count : "") + "   (J: journal)");
		}

		/// <summary>The crew's story items are used up (a "take" check passed).</summary>
		public static void Take(string id, int count)
		{
			id = StoryItems.IdOf(id);
			if (id.Length == 0 || count <= 0) return;
			Change("take", Fields(id, count.ToString(CultureInfo.InvariantCulture)));
		}

		/// <summary>A page in the journal (a page with the same key is only added once).</summary>
		public static void AddPage(string key, string title, string text, string island)
		{
			if (pages.Any(p => p.Key == key)) return;
			Change("page", Fields(key, Today.ToString(CultureInfo.InvariantCulture), title ?? "", island ?? "", text ?? ""));
		}

		static void Change(string op, string data)
		{
			bool changed = Apply(op, data);
			var msg = new IslandNetMessage { Name = op, Data = data };
			if (Raft_Network.IsHost) { if (changed) IslandNetwork.SendStory(StateMessage()); }
			else IslandNetwork.SendStory(msg);
			if (changed) Raise();
		}

		static bool Apply(string op, string data)
		{
			string[] f = Split(data);
			int n;
			switch (op)
			{
				case "give":
					if (f.Length < 2 || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return false;
					Held h;
					if (!held.TryGetValue(f[0], out h)) held[f[0]] = h = new Held { Def = new StoryItemDef { Id = f[0] } };
					if (f.Length >= 5)
					{
						// (what the giver knew about it; empty fields keep what is known)
						if (f[2].Length > 0) h.Def.Name = f[2];
						if (f[3].Length > 0) h.Def.Icon = f[3];
						if (f[4].Length > 0) h.Def.Description = f[4];
					}
					h.Count += n;
					Debug.Log("[CUSTOM ISLANDS] Story item '" + h.Def.ShownName + "': " + h.Count);
					return true;
				case "take":
					Held t;
					if (f.Length < 2 || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || !held.TryGetValue(f[0], out t) || t.Count <= 0) return false;
					t.Count = Mathf.Max(0, t.Count - n);
					Debug.Log("[CUSTOM ISLANDS] Story item '" + t.Def.ShownName + "' used: " + t.Count + " left");
					return true;
				case "page":
					if (f.Length < 5 || pages.Any(p => p.Key == f[0])) return false;
					int day;
					int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out day);
					pages.Add(new Page { Key = f[0], Day = day, Title = f[2], Island = f[3], Text = f[4] });
					Debug.Log("[CUSTOM ISLANDS] Journal page '" + f[2] + "'");
					return true;
			}
			return false;
		}

		/// <summary>From the network.</summary>
		public static void OnMessage(IslandNetMessage msg)
		{
			if (msg.Name == "all")
			{
				if (Raft_Network.IsHost) return;
				held.Clear();
				pages.Clear();
				foreach (string line in (msg.Data ?? "").Split('\n'))
				{
					int eq = line.IndexOf('=');
					if (eq > 0) ReadLine(line.Substring(0, eq), line.Substring(eq + 1));
				}
				Raise();
				return;
			}
			if (!Raft_Network.IsHost) return;
			// The host: a client's change; everyone gets the new state
			if (Apply(msg.Name ?? "", msg.Data ?? "")) { IslandNetwork.SendStory(StateMessage()); Raise(); }
		}

		public static IslandNetMessage StateMessage()
		{
			return new IslandNetMessage { Kind = IslandNetMessage.Story, Name = "all", Data = string.Join("\n", Lines().ToArray()) };
		}

		#endregion

		#region Saving (in the world's island list file)

		static IEnumerable<string> Lines()
		{
			foreach (Held h in held.Values)
				yield return "story.item=" + Fields(h.Def.Id, h.Count.ToString(CultureInfo.InvariantCulture), h.Def.Name, h.Def.Icon, h.Def.Description);
			foreach (Page p in pages)
				yield return "story.page=" + Fields(p.Key, p.Day.ToString(CultureInfo.InvariantCulture), p.Title, p.Island, p.Text);
		}

		/// <summary>Lines for the world's file ("@" + key = value).</summary>
		public static IEnumerable<string> WriteLines() { return Lines().Select(l => "@" + l); }

		/// <summary>A line of the world's file; true if it was a story line.</summary>
		public static bool ReadLine(string key, string value)
		{
			string[] f = Split(value);
			int n;
			if (key == "story.item")
			{
				if (f.Length < 2 || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return true;
				held[f[0]] = new Held { Count = n, Def = new StoryItemDef { Id = f[0], Name = f.Length > 2 ? f[2] : "", Icon = f.Length > 3 ? f[3] : "", Description = f.Length > 4 ? f[4] : "" } };
				return true;
			}
			if (key == "story.page")
			{
				if (f.Length < 5) return true;
				int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
				if (!pages.Any(p => p.Key == f[0])) pages.Add(new Page { Key = f[0], Day = n, Title = f[2], Island = f[3], Text = f[4] });
				return true;
			}
			return false;
		}

		/// <summary>Fields joined by '|', each escaped (texts may hold '|', '=' and new lines).</summary>
		static string Fields(params string[] fields) { return string.Join("|", fields.Select(x => Uri.EscapeDataString(x ?? "")).ToArray()); }

		static string[] Split(string data) { return (data ?? "").Split('|').Select(x => { try { return Uri.UnescapeDataString(x); } catch { return x; } }).ToArray(); }

		#endregion
	}

	/// <summary>
	/// The crew's journal in a world (J): the story items with their pictures, and the pages found (custom notes
	/// read, "journal" actions). In the look of Raft's menus, with the page on paper.
	/// </summary>
	public class JournalWindow : MonoBehaviour
	{
		static JournalWindow instance;
		public static bool IsOpen { get { return instance != null && instance.gameObject.activeSelf; } }
		public const KeyCode Key = KeyCode.J;

		static readonly Color Paper = new Color(0.94f, 0.9f, 0.8f, 0.98f), Ink = new Color(0.2f, 0.15f, 0.1f, 1f);

		RectTransform itemGrid, pageList;
		Text countText, readTitle, readText, emptyItems, emptyPages;
		Image readIcon;
		string shownKey;
		bool cursorWasFree, dirty;

		/// <summary>Every frame from the mod: J opens and closes the journal in a world.</summary>
		public static void Tick()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || DynamicIslands.InEditor()) { if (IsOpen) instance.Hide(); return; }
			if (!Input.GetKeyDown(Key) || Typing() || NoteReader.IsOpen) return;
			if (IsOpen) instance.Hide(); else Open();
		}

		/// <summary>A text field (chat, console) has the keyboard.</summary>
		static bool Typing()
		{
			GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
			InputField f = sel != null ? sel.GetComponent<InputField>() : null;
			return f != null && f.isFocused;
		}

		public static void Open()
		{
			if (instance == null) Build();
			instance.Show();
		}

		public static void Close() { if (instance != null) instance.Hide(); }

		static void Build()
		{
			Canvas canvas = UIKit.CreateCanvas("CustomIslands_Journal", 490);
			DontDestroyOnLoad(canvas.gameObject);
			instance = canvas.gameObject.AddComponent<JournalWindow>();
			StoryBook.Changed += () => { if (instance != null) instance.dirty = true; };

			RectTransform panel = UIKit.Panel(canvas.transform, "Panel", new RectOffset(18, 18, 14, 16), 10f, false);
			UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980, 620));
			RectTransform head = UIKit.Row(panel, 34f, 8f, "Head");
			UIKit.Label(head, "JOURNAL", 22, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
			instance.countText = UIKit.Label(head, "", 13, UIKit.TextMuted, TextAnchor.MiddleRight);
			UIKit.Separator(panel);

			RectTransform body = UIKit.Row(panel, 490f, 14f, "Body");
			// Left: items and pages
			RectTransform left = UIKit.Rect("Left", body);
			UIKit.Size(left.gameObject, 400);
			UIKit.Vertical(left.gameObject, 8f, new RectOffset(0, 0, 0, 0));
			RectTransform items = UIKit.Group(left, "Story items");
			UIKit.Size(items.gameObject, -1, 214);
			RectTransform itemBox = UIKit.Rect("ItemBox", items);
			UIKit.Size(itemBox.gameObject, -1, 176);
			ScrollRect s1;
			RectTransform itemContent = UIKit.ScrollList(itemBox, out s1, 4f);
			UIKit.Stretch((RectTransform)s1.transform);
			instance.itemGrid = UIKit.Rect("Grid", itemContent);
			var g = instance.itemGrid.gameObject.AddComponent<GridLayoutGroup>();
			g.cellSize = new Vector2(88f, 84f);
			g.spacing = new Vector2(6f, 6f);
			g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
			g.constraintCount = 4;
			instance.emptyItems = UIKit.Label(itemContent, "<i>No story items yet. Look for them in chests and on the islands.</i>", 13, UIKit.TextMuted);

			RectTransform pagesGroup = UIKit.Group(left, "Pages");
			UIKit.Size(pagesGroup.gameObject, -1, 268);
			RectTransform pageBox = UIKit.Rect("PageBox", pagesGroup);
			UIKit.Size(pageBox.gameObject, -1, 230);
			ScrollRect s2;
			instance.pageList = UIKit.ScrollList(pageBox, out s2, 4f);
			UIKit.Stretch((RectTransform)s2.transform);
			instance.emptyPages = UIKit.Label(instance.pageList, "<i>Notes you read on the islands are written down here.</i>", 13, UIKit.TextMuted);

			// Right: the page (or item) on paper
			RectTransform sheet = UIKit.Rect("Sheet", body);
			UIKit.Background(sheet.gameObject, Paper, 6);
			UIKit.Border(sheet, new Color(0.55f, 0.45f, 0.3f, 1f), 6, 2f);
			UIKit.Vertical(sheet.gameObject, 10f, new RectOffset(28, 28, 22, 18));
			RectTransform titleRow = UIKit.Row(sheet, 44f, 10f, "TitleRow");
			RectTransform iconRect = UIKit.Rect("Icon", titleRow);
			UIKit.Size(iconRect.gameObject, 44, 44);
			instance.readIcon = iconRect.gameObject.AddComponent<Image>();
			instance.readIcon.preserveAspect = true;
			instance.readIcon.raycastTarget = false;
			instance.readTitle = UIKit.Label(titleRow, "", 24, Ink, TextAnchor.MiddleLeft, FontStyle.Normal, "Title");
			ScrollRect s3;
			RectTransform textContent = UIKit.ScrollList(sheet, out s3, 0f);
			UIKit.Size(s3.gameObject, -1, -1).flexibleHeight = 1f;
			instance.readText = UIKit.Label(textContent, "", 17, Ink, TextAnchor.UpperLeft, FontStyle.Normal, "Text");
			instance.readText.lineSpacing = 1.15f;
			instance.readText.supportRichText = false;
			foreach (Text t in new[] { instance.readTitle, instance.readText }) { Shadow sh = t.GetComponent<Shadow>(); if (sh != null) Destroy(sh); }

			RectTransform bottom = UIKit.Row(panel, 34f, 8f, "Bottom");
			UIKit.Label(bottom, "J or Esc to close", 13, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic);
			UIKit.Button(bottom, "Close", Close, null, 120, 34);
			canvas.gameObject.SetActive(false);
		}

		void Show()
		{
			gameObject.SetActive(true);
			cursorWasFree = Cursor.visible;
			if (!cursorWasFree) try { RAPI.ToggleCursor(true); } catch { }
			Fill();
		}

		void Hide()
		{
			if (!gameObject.activeSelf) return;
			gameObject.SetActive(false);
			if (!cursorWasFree) try { RAPI.ToggleCursor(false); } catch { }
		}

		void Update()
		{
			if (Input.GetKeyDown(KeyCode.Escape)) { Hide(); return; }
			if (dirty) Fill();
		}

		/// <summary>Fills the lists from the story book.</summary>
		void Fill()
		{
			dirty = false;
			foreach (Transform c in itemGrid) Destroy(c.gameObject);
			foreach (Transform c in pageList) if (c != emptyPages.transform) Destroy(c.gameObject);
			List<StoryBook.Held> items = StoryBook.Items.ToList();
			foreach (StoryBook.Held h in items) ItemTile(h);
			emptyItems.gameObject.SetActive(items.Count == 0);
			foreach (StoryBook.Page p in StoryBook.Pages.Reverse()) PageButton(p);
			emptyPages.gameObject.SetActive(StoryBook.Pages.Count == 0);
			countText.text = items.Count + " story item(s) \u00B7 " + StoryBook.Pages.Count + " page(s)";
			if (shownKey == null || (!StoryBook.Pages.Any(p => p.Key == shownKey) && !items.Any(h => "item:" + h.Def.Id == shownKey)))
			{
				if (StoryBook.Pages.Count > 0) ShowPage(StoryBook.Pages.Last());
				else if (items.Count > 0) ShowItem(items[0]);
				else { shownKey = null; readIcon.enabled = false; readTitle.text = "Journal"; readText.text = "Nothing here yet. Read notes on the islands and look for story items: they are kept here for the whole crew."; }
			}
		}

		void ItemTile(StoryBook.Held h)
		{
			RectTransform r = UIKit.Rect("Item_" + h.Def.Id, itemGrid);
			Image bg = UIKit.Background(r.gameObject, Color.white, 6);
			var b = r.gameObject.AddComponent<Button>();
			UIKit.Register(b);
			b.targetGraphic = bg;
			UIKit.Slot(b);
			RectTransform pic = UIKit.Rect("Icon", r);
			UIKit.Anchor(pic, new Vector2(0.5f, 1f), new Vector2(0, -5), new Vector2(46, 46));
			Image icon = pic.gameObject.AddComponent<Image>();
			icon.sprite = StoryItems.IconSprite(h.Def.Icon);
			icon.preserveAspect = true; icon.raycastTarget = false;
			if (icon.sprite == null) icon.color = new Color(0.3f, 0.2f, 0.1f, 0.35f);
			Text t = UIKit.Label(r, h.Def.ShownName, 11, UIKit.SlotText, TextAnchor.LowerCenter, FontStyle.Normal, "Label");
			UIKit.Stretch(t.rectTransform, 3, 3, 52, 3);
			t.resizeTextForBestFit = true; t.resizeTextMinSize = 8; t.resizeTextMaxSize = 11;
			if (h.Count > 1)
			{
				Text n = UIKit.Label(r, "\u00D7" + h.Count, 12, UIKit.SlotText, TextAnchor.UpperRight, FontStyle.Bold, "Count");
				UIKit.Stretch(n.rectTransform, 4, 6, 3, 60);
			}
			StoryBook.Held held = h;
			b.onClick.AddListener(() => ShowItem(held));
		}

		void PageButton(StoryBook.Page p)
		{
			Button b = UIKit.Button(pageList, p.Title.Length > 0 ? p.Title : "(a page)", () => ShowPage(p), p.Island.Length > 0 ? "Found on " + p.Island : null, -1, 30f, 13);
			if (p.Key == shownKey) UIKit.SetActive(b, true); else UIKit.Flat(b);
			Text t = UIKit.LabelOf(b);
			t.alignment = TextAnchor.MiddleLeft;
		}

		void ShowPage(StoryBook.Page p)
		{
			shownKey = p.Key;
			readIcon.enabled = false;
			readTitle.text = p.Title.Length > 0 ? p.Title : "A page";
			readText.text = (p.Text.Length > 0 ? p.Text : "(The page is empty.)") + (p.Island.Length > 0 ? "\n\n\u2014 " + p.Island + ", day " + p.Day : "");
			RefreshSelection();
		}

		void ShowItem(StoryBook.Held h)
		{
			shownKey = "item:" + h.Def.Id;
			readIcon.sprite = StoryItems.IconSprite(h.Def.Icon);
			readIcon.enabled = readIcon.sprite != null;
			readTitle.text = h.Def.ShownName + (h.Count > 1 ? "  \u00D7" + h.Count : "");
			readText.text = h.Def.Description.Length > 0 ? h.Def.Description : "A story item. The crew holds it together.";
			RefreshSelection();
		}

		void RefreshSelection()
		{
			foreach (Button b in pageList.GetComponentsInChildren<Button>())
			{
				StoryBook.Page p = StoryBook.Pages.FirstOrDefault(x => ("Button_" + (x.Title.Length > 0 ? x.Title : "(a page)")) == b.name);
				if (p != null && p.Key == shownKey) UIKit.SetActive(b, true); else UIKit.Flat(b);
				UIKit.LabelOf(b).alignment = TextAnchor.MiddleLeft;
			}
		}

		/// <summary>What the journal shows now (tests).</summary>
		public static string ShownTitle { get { return IsOpen ? instance.readTitle.text : null; } }
	}
}
