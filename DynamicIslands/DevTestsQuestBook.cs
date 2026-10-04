using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DynamicIslands.Editor;

namespace DynamicIslands
{
	/// <summary>
	/// The quest book spike (NextTask_QuestBook.md, Q0): what Raft's notebook is made of, and whether the mod can add
	/// tabs and pages to it. CINoteBookDump writes the book's objects to notebook_dump.txt; CINoteBookOpen opens it at a
	/// page (pictures); CINoteBookUnlockAll unlocks every note of Raft's (test worlds only).
	/// </summary>
	public static partial class DevTests
	{
		static NoteBookUI LocalNoteBookUI()
		{
			Network_Player p = null;
			try { p = RAPI.GetLocalPlayer(); } catch { }
			return p != null ? p.NoteBookUI : null;
		}

		static string RectOf(Transform t)
		{
			RectTransform r = t as RectTransform;
			if (r == null) return "pos=" + t.localPosition;
			return "anch=" + r.anchoredPosition + " size=" + r.sizeDelta + " min=" + r.anchorMin + " max=" + r.anchorMax + " piv=" + r.pivot + " rot=" + t.localEulerAngles.z.ToString("0") + " scale=" + t.localScale.x.ToString("0.##");
		}

		static void DumpTree(StringBuilder sb, Transform t, string indent, int depth)
		{
			string comps = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToArray());
			TMP_Text text = t.GetComponent<TMP_Text>();
			Image img = t.GetComponent<Image>();
			sb.Append(indent).Append(t.gameObject.activeSelf ? "" : "(off) ").Append(t.name).Append(" [").Append(comps).Append("] ").Append(RectOf(t));
			if (text != null) sb.Append(" TEXT=\"").Append((text.text ?? "").Replace("\n", "\\n").Substring(0, Math.Min(80, (text.text ?? "").Length))).Append("\" font=").Append(text.font != null ? text.font.name : "-").Append(" fs=").Append(text.fontSize);
			if (img != null) sb.Append(" IMG=").Append(img.sprite != null ? img.sprite.name : "-").Append(" col=").Append(img.color);
			sb.AppendLine();
			if (depth <= 0) { if (t.childCount > 0) sb.Append(indent).Append("  ... ").Append(t.childCount).AppendLine(" children"); return; }
			foreach (Transform c in t) DumpTree(sb, c, indent + "  ", depth - 1);
		}

		static string NbPath(Transform t, Transform root)
		{
			var parts = new List<string>();
			for (Transform x = t; x != null && x != root; x = x.parent) parts.Insert(0, x.name);
			return string.Join("/", parts.ToArray());
		}

		[ConsoleCommand(name: "CINoteBookDump", docs: "Dev, world: writes Raft's notebook (pages, notes, tabs, found items, the whole object tree) to Mods\\DynamicIslands\\notebook_dump.txt (quest book spike)")]
		public static void NoteBookDumpCommand()
		{
			NoteBookUI ui = LocalNoteBookUI();
			if (ui == null) { Fail("NoteBookDump: no local player's notebook (in a world?)"); return; }
			var sb = new StringBuilder();
			Traverse tr = Traverse.Create(ui);
			NoteBookPage[] pages = tr.Field("pageObjs").GetValue<NoteBookPage[]>() ?? new NoteBookPage[0];
			NoteBookNote[] notes = tr.Field("notes").GetValue<NoteBookNote[]>() ?? new NoteBookNote[0];
			Canvas canvas = tr.Field("noteBookCanvas").GetValue<Canvas>();
			Transform root = canvas != null ? canvas.transform : ui.transform;
			sb.AppendLine("NoteBookUI on " + NbPath(ui.transform, null) + "; canvas " + (canvas != null ? canvas.name + " mode=" + canvas.renderMode + " scale=" + canvas.transform.localScale : "-"));
			sb.AppendLine("currentPageIndex=" + tr.Field("currentPageIndex").GetValue() + " highestUnlockedPageIndex=" + tr.Field("highestUnlockedPageIndex").GetValue() + " isDisplayed=" + ui.isDisplayed);
			sb.AppendLine("unlocked note indexes: " + string.Join(",", (NoteBook.unlockedNoteBookIndexes ?? new List<int>()).Select(i => i.ToString()).ToArray()));
			sb.AppendLine("unlocked chunk types: " + string.Join(",", (NoteBook.unlockedChunkPointType ?? new List<ChunkPointType>()).Select(i => i.ToString()).ToArray()));
			sb.AppendLine();
			sb.AppendLine("PAGES " + pages.Length);
			foreach (NoteBookPage pg in pages)
			{
				if (pg == null) { sb.AppendLine("  null"); continue; }
				Traverse pt = Traverse.Create(pg);
				NoteBookNote[] pn = pt.Field("notes").GetValue<NoteBookNote[]>() ?? new NoteBookNote[0];
				sb.AppendLine("  page " + pg.pageIndex + " '" + pg.name + "' type=" + pg.GetType().Name + " title='" + pt.Field("pageTitleString").GetValue() + "' active=" + pg.gameObject.activeSelf + " path=" + NbPath(pg.transform, root) + " notes=" + string.Join(",", pn.Select(n => n == null ? "null" : n.noteIndex.ToString()).ToArray()));
			}
			sb.AppendLine();
			sb.AppendLine("NOTES " + notes.Length);
			foreach (NoteBookNote n in notes)
			{
				if (n == null) { sb.AppendLine("  null"); continue; }
				TMP_Text[] texts = n.GetComponentsInChildren<TMP_Text>(true);
				sb.AppendLine("  note " + n.noteIndex + " '" + n.name + "' land=" + n.landmarkType + " unlocked=" + n.isUnlocked + " thumb=" + n.isFrequencyThumbnail + "/" + n.thumbNailLandmarkType + " path=" + NbPath(n.transform, root)
					+ " comps=" + string.Join(",", n.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).ToArray())
					+ " texts=" + string.Join(" | ", texts.Select(t => t.name + ":" + (t.text ?? "").Replace("\n", "\\n").Substring(0, Math.Min(50, (t.text ?? "").Length))).ToArray()));
			}
			sb.AppendLine();
			Notebook_ThumbnailShortcut[] tabs = ui.GetComponentsInChildren<Notebook_ThumbnailShortcut>(true);
			if (tabs.Length == 0 && canvas != null) tabs = canvas.GetComponentsInChildren<Notebook_ThumbnailShortcut>(true);
			sb.AppendLine("TABS " + tabs.Length);
			foreach (Notebook_ThumbnailShortcut tab in tabs)
			{
				sb.AppendLine("  tab '" + tab.name + "' target=" + (tab.targetPage != null ? tab.targetPage.pageIndex + " " + tab.targetPage.name : "null") + " active=" + tab.gameObject.activeInHierarchy + " path=" + NbPath(tab.transform, root) + " " + RectOf(tab.transform));
				foreach (Transform c in tab.GetComponentsInChildren<Transform>(true)) if (c != tab.transform) sb.AppendLine("     " + NbPath(c, tab.transform) + " [" + string.Join(",", c.GetComponents<Component>().Where(x => x != null && !(x is Transform)).Select(x => x.GetType().Name).ToArray()) + "] " + (c.GetComponent<TMP_Text>() != null ? "TEXT=\"" + c.GetComponent<TMP_Text>().text.Replace("\n", "\\n") + "\"" : ""));
			}
			sb.AppendLine();
			NoteBook_QuestItem[] items = tr.Field("questItemUIs").GetValue<NoteBook_QuestItem[]>() ?? new NoteBook_QuestItem[0];
			sb.AppendLine("QUEST ITEM SLOTS " + items.Length + (items.Length > 0 && items[0] != null ? " first at " + NbPath(items[0].transform, root) : ""));
			sb.AppendLine();
			sb.AppendLine("TREE (from " + root.name + ")");
			DumpTree(sb, root, "", 9);
			string file = Path.GetFullPath(Path.Combine(DynamicIslands.assetpath, "notebook_dump.txt"));
			File.WriteAllText(file, sb.ToString());
			Log("PASS: notebook dumped to " + file + " (" + pages.Length + " pages, " + notes.Length + " notes, " + tabs.Length + " tabs)");
		}

		[ConsoleCommand(name: "CINoteBookOpen", docs: "Dev, world: opens Raft's notebook at a page (CINoteBookOpen [page]); CINoteBookOpen close shuts it")]
		public static void NoteBookOpenCommand(string[] args)
		{
			NoteBookUI ui = LocalNoteBookUI();
			if (ui == null) { Fail("NoteBookOpen: no notebook"); return; }
			if (args != null && args.Length > 0 && args[0] == "close") { ui.SetBookActive(false); Log("PASS: notebook closed"); return; }
			if (!ui.isDisplayed) ui.SetBookActive(true);
			uint page;
			if (args != null && args.Length > 0 && uint.TryParse(args[0], out page)) ui.FlipToPageLocally(page);
			Log("PASS: notebook open at " + Traverse.Create(ui).Field("currentPageIndex").GetValue());
		}

		[ConsoleCommand(name: "CINoteBookUnlockAll", docs: "Dev, world (test worlds only): unlocks every note in Raft's notebook (pictures)")]
		public static void NoteBookUnlockAllCommand()
		{
			if (!(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("NoteBookUnlockAll: only in a test world (named CI ...)"); return; }
			NoteBookUI ui = LocalNoteBookUI();
			NoteBook nb = UnityEngine.Object.FindObjectOfType<NoteBook>();
			if (ui == null || nb == null) { Fail("NoteBookUnlockAll: no notebook"); return; }
			NoteBookNote[] notes = Traverse.Create(ui).Field("notes").GetValue<NoteBookNote[]>() ?? new NoteBookNote[0];
			int n = 0;
			foreach (NoteBookNote note in notes) if (note != null && !note.isUnlocked) { nb.UnlockSpecificNoteWithUniqueNoteIndex(note.noteIndex, true, false); n++; }
			Traverse.Create(ui).Method("UpdatePages").GetValue();
			Log("PASS: " + n + " notes unlocked");
		}

		[ConsoleCommand(name: "CIQuestBookUnit", docs: "Dev, anywhere: the quest book's plan data without a world - a rule's tab title, colour and intro (written and read back, line breaks kept), 'next coordinates when a note is read', the tab's name when none is given, a plan's ending page, older rules and plans unchanged (TEST_CATALOGUE QB1)")]
		public static void QuestBookUnitCommand()
		{
			bool ok = true;
			IntroRule r = IntroRule.Parse("ferry | island:Saltmarsh Ferry | start | receiver:800 | A ferry's band. | Ferry | after:beacon | note:5 | The Ferry | 4 | Line one\\nLine two");
			Check(ref ok, r != null && r.TabTitle == "The Ferry" && r.TabColour == 4 && r.TabIntro == "Line one\nLine two" && r.StoryDone == "note:5" && r.MainStory,
				"a main story rule with a tab title, colour and a two-line intro read: " + (r != null ? r.TabTitle + " / " + r.TabColour + " / " + r.TabIntro.Replace("\n", "\\n") + " / " + r.StoryDone : "null"));
			IntroRule back = r != null ? IntroRule.Parse(r.ToLine()) : null;
			Check(ref ok, back != null && back.ToLine() == r.ToLine() && back.TabIntro == r.TabIntro, "... written and read back the same: " + (r != null ? r.ToLine() : ""));
			Check(ref ok, r != null && r.DescribeDone() == "its note #5 is read" && IntroRule.NormalDone("Note: 7") == "note:7", "next coordinates when a note is read: " + (r != null ? r.DescribeDone() : ""));
			string oldLine = "cove | island:Wreckers' Cove | start | receiver:700 | A lantern code. | Wreckers' Cove | after:RadioTower | quest";
			IntroRule old = IntroRule.Parse(oldLine);
			Check(ref ok, old != null && old.ToLine() == oldLine && old.TabTitle == "" && old.TabColour == 0 && old.TabIntro == "" && old.MainStory, "an older story rule (eight parts) reads and writes as before, and is main story");
			IntroRule side = IntroRule.Parse("trip | island:Signal Rock | km:3 | sailing:300 | | ");
			Check(ref ok, side != null && !side.MainStory, "a rule outside the chain is a side quest");
			IntroRule wild = IntroRule.Parse("w | island:X | start | ahead:300 | | | | | | 15 | ");
			Check(ref ok, wild != null && wild.TabColour == IntroRule.TabColours && !wild.MainStory, "a colour past Raft's ten is cut to 10: " + (wild != null ? wild.TabColour.ToString() : "null"));
			IntroRule bad = IntroRule.Parse("w | island:X | start | ahead:300 | | | first | | | blue | ");
			Check(ref ok, bad != null && bad.TabColour == 0, "a colour that isn't a number = chosen by place");
			Check(ref ok, old != null && old.TabName == "Wreckers' Cove", "no tab title: the Receiver label");
			IntroRule noLabel = IntroRule.Parse("isle | island:Thornwood | start | receiver:900 | | | after:Vasagatan | quest");
			Check(ref ok, noLabel != null && noLabel.TabName == "Thornwood", "no title or label: the island's name");
			IntroRule pipe = new IntroRule { Id = "p", What = "island", WhatArg = "X", StoryPlace = "first", TabTitle = "A|B", TabIntro = "x | y\r\nz" };
			IntroRule pipeBack = IntroRule.Parse(pipe.ToLine());
			Check(ref ok, pipeBack != null && pipeBack.TabTitle == "A/B" && pipeBack.TabIntro == "x / y\nz", "a '|' in a title or intro can't break the line: " + (pipeBack != null ? pipeBack.TabTitle + " / " + pipeBack.TabIntro.Replace("\n", "\\n") : "null"));

			WorldPlan p = WorldPlan.Parse("t", "story = off\nstoryending = The sea gives up its last secret.\\n\\nThe end.\nrule = a | island:X | start | receiver:600 | | | first | quest | Start | 2 | \n");
			Check(ref ok, p.StoryEnding == "The sea gives up its last secret.\n\nThe end." && p.Rules.Count == 1 && p.Rules[0].TabColour == 2, "a plan's ending page read (line breaks kept)");
			WorldPlan p2 = WorldPlan.Parse("t", p.ToText());
			Check(ref ok, p2.StoryEnding == p.StoryEnding && p2.Rules[0].ToLine() == p.Rules[0].ToLine(), "... written and read again the same");
			WorldPlan plain = WorldPlan.Parse("t", "rule = camp | type:camp | start | ahead:350 | | \n");
			Check(ref ok, plain.StoryEnding == "" && !plain.ToText().Contains("storyending ="), "an older plan has no ending page and writes none");
			if (ok) Log("PASS: quest book unit");
		}

		[ConsoleCommand(name: "CIQuestBookState", docs: "Dev, world: the quest book now - its layout, our tabs, Raft's tabs shown, our pages' texts")]
		public static void QuestBookStateCommand()
		{
			QuestBook.Refresh();
			Log("QUESTBOOK layout: " + QuestBook.Layout);
			foreach (string t in QuestBook.Tabs) Log("QUESTBOOK tab: " + t);
			Log("QUESTBOOK items: " + string.Join(", ", QuestBook.Items.ToArray()));
			Log("QUESTBOOK Raft tabs shown: " + string.Join(",", StoryOrder.Chain.Where(QuestBook.RaftTabShown).Select(StoryOrder.Name).ToArray()));
			foreach (KeyValuePair<uint, List<string>> kv in QuestBook.PageTexts.OrderBy(k => k.Key))
				Log("QUESTBOOK page " + kv.Key + ": " + string.Join(" || ", kv.Value.Select(x => x.Replace("\n", "\\n")).ToArray()));
			Log("PASS: quest book state");
		}

		[ConsoleCommand(name: "CIQuestBookOpen", docs: "Dev, world: opens Raft's notebook at one of the quest book's tabs (CIQuestBookOpen <tab title>), or at a page number")]
		public static void QuestBookOpenCommand(string[] args)
		{
			string title = args != null ? string.Join(" ", args) : "";
			NoteBookUI ui = LocalNoteBookUI();
			if (ui == null) { Fail("QuestBookOpen: no notebook"); return; }
			if (!ui.isDisplayed) ui.SetBookActive(true);
			uint page;
			if (uint.TryParse(title, out page)) ui.FlipToPageLocally(page);
			else if (!QuestBook.PressTab(title)) { Fail("QuestBookOpen: no tab '" + title + "'"); return; }
			Log("PASS: notebook open at " + Traverse.Create(ui).Field("currentPageIndex").GetValue());
		}

		static void DropComponents(GameObject go, params string[] typeNames)
		{
			foreach (Component c in go.GetComponentsInChildren<Component>(true))
				if (c != null && typeNames.Contains(c.GetType().Name)) UnityEngine.Object.DestroyImmediate(c);
		}

		[ConsoleCommand(name: "CINoteBookClone", docs: "Dev, world (test worlds only): quest book spike - adds <n> test islands to Raft's notebook (a tab each, cloned from Vasagatan's, and two pages with a note), unlocks them and opens the book at the first; CINoteBookClone <n> scroll also puts the tabs into a scrolling strip")]
		public static void NoteBookCloneCommand(string[] args)
		{
			if (!(SaveAndLoad.CurrentGameFileName ?? "").StartsWith("CI ")) { Fail("NoteBookClone: only in a test world (named CI ...)"); return; }
			int count = 1;
			if (args != null && args.Length > 0) int.TryParse(args[0], out count);
			bool scroll = args != null && args.Contains("scroll");
			NoteBookUI ui = LocalNoteBookUI();
			NoteBook nb = UnityEngine.Object.FindObjectOfType<NoteBook>();
			if (ui == null || nb == null) { Fail("NoteBookClone: no notebook"); return; }
			Traverse tr = Traverse.Create(ui);
			var pages = (tr.Field("pageObjs").GetValue<NoteBookPage[]>() ?? new NoteBookPage[0]).ToList();
			var notes = (tr.Field("notes").GetValue<NoteBookNote[]>() ?? new NoteBookNote[0]).ToList();
			NoteBookPage tplL = pages.FirstOrDefault(p => p.pageIndex == 6), tplR = pages.FirstOrDefault(p => p.pageIndex == 7);
			Notebook_ThumbnailShortcut tplTab = ui.GetComponentsInChildren<Notebook_ThumbnailShortcut>(true).FirstOrDefault(t => t.name.Contains("Vasagatan"));
			if (tplL == null || tplR == null || tplTab == null) { Fail("NoteBookClone: Vasagatan's pages or tab not found"); return; }
			uint next = pages.Max(p => p.pageIndex) + 1;
			if (next % 2 == 1) next++; // a spread starts on an even (left) page
			int firstPage = (int)next;
			Sprite[] tabSprites = Resources.FindObjectsOfTypeAll<Sprite>().Where(s => s.name.StartsWith("NoteBook_Thumbnail_")).OrderBy(s => s.name).ToArray();
			for (int i = 0; i < count; i++)
			{
				int idx = 10000 + i * 100;
				string title = "Test Island " + (i + 1);
				NoteBookPage left = null;
				foreach (NoteBookPage tpl in new[] { tplL, tplR })
				{
					NoteBookPage pg = UnityEngine.Object.Instantiate(tpl, tpl.transform.parent);
					pg.pageIndex = next;
					pg.name = (tpl == tplL ? "NoteBookPageL_" : "NoteBookPageR_") + title + "_PageIndex " + next + "_CI";
					Traverse.Create(pg).Field("pageTitleString").SetValue(title);
					Transform tt = pg.transform.Find("Page Title");
					if (tt != null && tt.GetComponent<TMP_Text>() != null) tt.GetComponent<TMP_Text>().text = title;
					Transform nt = pg.transform.Find("Page Title/Page number");
					if (nt != null && nt.GetComponent<TMP_Text>() != null) nt.GetComponent<TMP_Text>().text = (next + 1).ToString();
					// one note kept (the first), the rest removed
					NoteBookNote[] pn = pg.GetComponentsInChildren<NoteBookNote>(true);
					for (int k = 1; k < pn.Length; k++) UnityEngine.Object.DestroyImmediate(pn[k].gameObject);
					NoteBookNote note = pn[0];
					note.noteIndex = idx + 1 + (tpl == tplL ? 0 : 1);
					note.landmarkType = ChunkPointType.None;
					note.isUnlocked = false;
					note.name = "NoteBookNote_Index" + note.noteIndex + "_CI";
					Traverse.Create(note).Field("voiceActor").SetValue(null);
					Traverse.Create(note).Field("voiceData").SetValue(null);
					DropComponents(note.gameObject, "Localize");
					Transform play = note.transform.Find("Play&Stop Button");
					if (play != null) play.gameObject.SetActive(false);
					TMP_Text[] tx = note.GetComponentsInChildren<TMP_Text>(true);
					if (tx.Length > 0) tx[0].text = tpl == tplL
						? "This is a page of " + title + ", added by Custom Islands while the game runs. If you can read this in Raft's own book, with Raft's paper and handwriting, the quest book can be done.\n\n-The mod"
						: "A second note on the right page. Notes can be as long as Raft's (about 350 characters).";
					for (int k = 1; k < tx.Length; k++) tx[k].text = "";
					Traverse.Create(pg).Field("notes").SetValue(new[] { note });
					pages.Add(pg);
					notes.Add(note);
					if (tpl == tplL) left = pg;
					next++;
				}
				Notebook_ThumbnailShortcut tab = UnityEngine.Object.Instantiate(tplTab, tplTab.transform.parent);
				tab.name = "ThumbNailButton_" + title + "_CI";
				tab.targetPage = left;
				NoteBookNote tn = tab.GetComponent<NoteBookNote>();
				tn.noteIndex = idx;
				tn.isFrequencyThumbnail = false;
				tn.thumbNailLandmarkType = ChunkPointType.None;
				tn.isUnlocked = false;
				DropComponents(tab.gameObject, "FrequencyTextMeshProUI", "Localize");
				foreach (TMP_Text t in tab.GetComponentsInChildren<TMP_Text>(true))
				{
					if (t.name == "DestinationName") t.text = title;
					if (t.name == "DestinationFrequency") { t.text = "#" + (1000 + i * 37).ToString(); t.gameObject.SetActive(true); }
				}
				Image img = tab.GetComponent<Image>();
				if (img != null && tabSprites.Length > 0) img.sprite = tabSprites[i % tabSprites.Length];
				notes.Add(tn);
			}
			tr.Field("pageObjs").SetValue(pages.ToArray());
			tr.Field("notes").SetValue(notes.ToArray());
			if (scroll) MakeTabsScroll(tplTab.transform.parent as RectTransform);
			int unlocked = 0;
			foreach (NoteBookNote n in notes) if (n.noteIndex >= 10000) { nb.UnlockSpecificNoteWithUniqueNoteIndex(n.noteIndex, true, false); unlocked++; }
			tr.Method("UpdatePages").GetValue();
			if (!ui.isDisplayed) ui.SetBookActive(true);
			bool flipped = ui.FlipToPageLocally((uint)firstPage);
			Log("PASS: " + count + " test islands added (pages from " + firstPage + ", " + unlocked + " notes unlocked, flip " + flipped + ", now at " + tr.Field("currentPageIndex").GetValue() + ", highest " + tr.Field("highestUnlockedPageIndex").GetValue() + ", tab sprites " + tabSprites.Length + (scroll ? ", tabs scroll" : "") + ")");
		}

		/// <summary>The tab strip (Raft's ThumbNails, a VerticalLayoutGroup as tall as the book) inside a scrolling viewport.</summary>
		static void MakeTabsScroll(RectTransform strip)
		{
			if (strip == null || strip.parent.name == "CI_TabViewport") return;
			var vp = new GameObject("CI_TabViewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image), typeof(ScrollRect));
			RectTransform v = vp.GetComponent<RectTransform>();
			v.SetParent(strip.parent, false);
			v.SetSiblingIndex(strip.GetSiblingIndex());
			v.anchorMin = strip.anchorMin; v.anchorMax = strip.anchorMax; v.pivot = strip.pivot;
			v.anchoredPosition = strip.anchoredPosition; v.sizeDelta = new Vector2(strip.sizeDelta.x + 30f, strip.sizeDelta.y);
			vp.GetComponent<Image>().color = new Color(1, 1, 1, 0.003f); // catches the wheel between tabs
			strip.SetParent(v, false);
			strip.anchorMin = new Vector2(0.5f, 1f); strip.anchorMax = new Vector2(0.5f, 1f); strip.pivot = new Vector2(0.5f, 1f);
			strip.anchoredPosition = new Vector2(15f, 0f);
			var fit = strip.gameObject.AddComponent<ContentSizeFitter>();
			fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
			ScrollRect sr = vp.GetComponent<ScrollRect>();
			sr.content = strip; sr.horizontal = false; sr.vertical = true; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 20f;
			sr.viewport = v;
			LayoutRebuilder.ForceRebuildLayoutImmediate(strip);
		}

		[ConsoleCommand(name: "CINoteBookScrollTo", docs: "Dev, world: scrolls the quest book spike's tab strip (0 = top, 1 = bottom)")]
		public static void NoteBookScrollToCommand(string[] args)
		{
			ScrollRect sr = UnityEngine.Object.FindObjectsOfType<ScrollRect>().FirstOrDefault(s => s.name == "CI_TabViewport");
			if (sr == null) { Fail("NoteBookScrollTo: no tab strip that scrolls"); return; }
			float f = 0f;
			if (args != null && args.Length > 0) float.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f);
			sr.verticalNormalizedPosition = 1f - f;
			Log("PASS: tabs scrolled to " + f + " (content " + sr.content.rect.height.ToString("0") + " in " + ((RectTransform)sr.transform).rect.height.ToString("0") + ")");
		}

		[ConsoleCommand(name: "CINoteBookTab", docs: "Dev, world: presses a notebook tab by its title (as a player's click does)")]
		public static void NoteBookTabCommand(string[] args)
		{
			string title = args != null ? string.Join(" ", args) : "";
			NoteBookUI ui = LocalNoteBookUI();
			Notebook_ThumbnailShortcut tab = ui != null ? ui.GetComponentsInChildren<Notebook_ThumbnailShortcut>(true).FirstOrDefault(t => t.GetComponentsInChildren<TMP_Text>(true).Any(x => x.name == "DestinationName" && x.text == title)) : null;
			if (tab == null) { Fail("NoteBookTab: no tab '" + title + "'"); return; }
			tab.OnThumbnailButtonPress();
			Log("PASS: tab '" + title + "' pressed, at page " + Traverse.Create(ui).Field("currentPageIndex").GetValue());
		}
	}
}
