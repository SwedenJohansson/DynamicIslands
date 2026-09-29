using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The editor's Save / Load window: a name field, the island's height in the world, a list of saved islands
	/// (click = pick, double-click = load) and Save / Load / Delete / Close buttons. Built in code with UIKit.
	/// Open with the top bar's Open / Save as buttons or Ctrl+O; Ctrl+S saves the current island.
	/// </summary>
	public class IslandFilesWindow : MonoBehaviour
	{
		static IslandFilesWindow instance;

		public static bool IsOpen { get { return instance != null && instance.gameObject.activeSelf; } }

		InputField nameField;
		InputField elevationField;
		RectTransform listContent;
		Text status;
		string pendingOverwrite, pendingDelete;
		string lastClicked;
		float lastClickTime;

		/// <summary>Builds the (hidden) window on the editor canvas. Call once when the editor opens.</summary>
		public static void Create(Transform canvas, GameObject unusedTemplate)
		{
			var blocker = new GameObject("IslandFilesWindow", typeof(RectTransform), typeof(Image));
			blocker.layer = 5;
			blocker.transform.SetParent(canvas, false);
			UIKit.Stretch((RectTransform)blocker.transform);
			blocker.GetComponent<Image>().color = new Color(0, 0, 0, 0.65f); // dims the editor and swallows clicks (modal)
			instance = blocker.AddComponent<IslandFilesWindow>();
			instance.Build();
			blocker.SetActive(false);
		}

		public static void Open()
		{
			if (instance == null) return;
			GeneratorWindow.Close();
			instance.gameObject.SetActive(true);
			instance.transform.SetAsLastSibling();
			instance.nameField.text = DynamicIslands.currentIslandName;
			instance.elevationField.text = DynamicIslands.currentElevation.ToString(System.Globalization.CultureInfo.InvariantCulture);
			instance.pendingOverwrite = instance.pendingDelete = null;
			instance.SetStatus("Type a name and press Save, or pick an island. Double-click an island to open it.", false);
			instance.Refresh();
			instance.nameField.ActivateInputField();
		}

		public static void Close()
		{
			if (instance == null) return;
			instance.gameObject.SetActive(false);
			EditorInput.IsTyping = false;
		}

		/// <summary>Ctrl+S: save straight away if the island already has a file, otherwise ask for a name.</summary>
		public static void QuickSave()
		{
			// (a new island is "myisland" until it has its own name: it saved over a 'myisland' saved before)
			if (!DynamicIslands.IsUnnamed && File.Exists(IslandSpawner.PathFor(DynamicIslands.currentIslandName))) DynamicIslands.SaveIsland(DynamicIslands.currentIslandName);
			else Open();
		}

		void Update()
		{
			EditorInput.IsTyping = (nameField != null && nameField.isFocused) || (elevationField != null && elevationField.isFocused);
			if (Input.GetKeyDown(KeyCode.Escape)) Close();
			else if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) && nameField.text.Trim().Length > 0) OnSave();
		}

		void OnDisable()
		{
			EditorInput.IsTyping = false;
		}

		void Build()
		{
			RectTransform panel = UIKit.Panel(transform, "Panel", new RectOffset(16, 16, 14, 16), 10f);
			UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500, 0));

			RectTransform head = UIKit.Row(panel, 28f, 6f, "Head");
			UIKit.Label(head, "ISLANDS", 18, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
			UIKit.Label(head, "Mods\\DynamicIslands", 11, UIKit.TextMuted, TextAnchor.MiddleRight);

			// Name and height, side by side
			RectTransform island = UIKit.Group(panel, "Island");
			RectTransform labels = UIKit.Row(island, 16f, 8f, "Labels");
			UIKit.Label(labels, "Name", 13, UIKit.TextMuted);
			Text hl = UIKit.Label(labels, "Height in the world (m)", 13, UIKit.TextMuted);
			UIKit.Size(hl.gameObject, 150);
			RectTransform fields = UIKit.Row(island, 32f, 8f, "Fields");
			nameField = UIKit.Field(fields, "e.g. myisland", "", 32f);
			nameField.characterLimit = 64;
			nameField.onValueChanged.AddListener(v => { pendingOverwrite = null; pendingDelete = null; Highlight(); });
			// Elevation: 0 = normal island, above 0 = flying, below 0 = under water (saved with the island)
			elevationField = UIKit.Field(fields, "0", "0", 32f, "60 = a flying island, -30 = under water, 0 = a normal island");
			UIKit.Size(elevationField.gameObject, 150);
			elevationField.contentType = InputField.ContentType.DecimalNumber;
			elevationField.characterLimit = 6;
			elevationField.onEndEdit.AddListener(v =>
			{
				float e;
				DynamicIslands.currentElevation = float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out e)
					? Mathf.Clamp(e, IslandSpawner.MinElevation, IslandSpawner.MaxElevation) : 0f;
				elevationField.text = DynamicIslands.currentElevation.ToString(System.Globalization.CultureInfo.InvariantCulture);
				EditorUI.RefreshIsland();
				SetStatus("In a world this island will be " + IslandSpawner.DescribeElevation(DynamicIslands.currentElevation) + ". Save to keep it.", false);
			});

			// Saved islands
			RectTransform saved = UIKit.Group(panel, "Saved islands");
			RectTransform listBox = UIKit.Rect("ListBox", saved);
			UIKit.Size(listBox.gameObject, -1, 230);
			UIKit.Background(listBox.gameObject, UIKit.FieldBg, 6);
			ScrollRect sr;
			listContent = UIKit.ScrollList(listBox, out sr, 3f);
			UIKit.Stretch((RectTransform)sr.transform, 4, 4, 4, 4);

			// Sharing: the picked island as a pack (with the islands it brings), and packs others made
			RectTransform share = UIKit.Row(panel, 30f, 8f, "Share");
			UIKit.Button(share, "Export...", OnExport, "Share the picked island: a pack (.zip) with the islands its rules bring, to send or to put in the island library", -1, 30, 13);
			UIKit.Button(share, "Import...", () => { Close(); LibraryImportWindow.Open(); }, "Install islands and plans from a pack (.zip) someone made, or remove what you installed", -1, 30, 13);
			UIKit.Button(share, "Library...", () => { Close(); LibraryWindow.Open(1); }, "The island library: islands and plans others made, to download", -1, 30, 13);

			status = UIKit.Label(panel, "", 14, UIKit.TextColor, TextAnchor.MiddleCenter, FontStyle.Italic, "Status");
			UIKit.Size(status.gameObject, -1, 34);

			RectTransform buttons = UIKit.Row(panel, 34f, 8f, "Buttons");
			Button save = UIKit.Button(buttons, "Save", OnSave, "Save the island under this name (Enter)", -1, 34, 15);
			UIKit.Primary(save);
			UIKit.Button(buttons, "Open", OnLoad, "Open the picked island (unsaved changes are lost)", -1, 34, 15);
			Button del = UIKit.Button(buttons, "Delete", OnDelete, "Delete the picked island's file (asks first)", -1, 34, 15);
			UIKit.DangerButton(del);
			UIKit.Button(buttons, "Close", Close, "Close (Esc)", -1, 34, 15);
		}

		void Refresh()
		{
			foreach (Transform child in listContent) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
			string[] names = IslandSpawner.ListSavedIslands().ToArray();
			if (names.Length == 0)
			{
				UIKit.Size(UIKit.Label(listContent, "No saved islands yet.", 14, UIKit.TextMuted, TextAnchor.MiddleCenter, FontStyle.Italic, "Empty").gameObject, -1, 30);
				return;
			}
			foreach (string n in names)
			{
				string islandName = n;
				var info = new FileInfo(IslandSpawner.PathFor(n));
				Button entry = UIKit.Button(listContent, n, () => OnEntryClicked(islandName), null, -1, 30, 14);
				entry.name = "Island_" + n;
				Text label = UIKit.LabelOf(entry);
				label.alignment = TextAnchor.MiddleLeft;
				label.rectTransform.offsetMin = new Vector2(10, 0);
				Text meta = UIKit.Label(entry.transform, info.LastWriteTime.ToString("yyyy-MM-dd HH:mm") + "   " + Math.Max(1, info.Length / 1024) + " KB", 11, UIKit.TextMuted, TextAnchor.MiddleRight, FontStyle.Normal, "Meta");
				UIKit.Stretch(meta.rectTransform, 10, 10, 0, 0);
			}
			Highlight();
		}

		void Highlight()
		{
			string current = nameField != null ? nameField.text.Trim() : "";
			foreach (Button b in listContent.GetComponentsInChildren<Button>(true))
				UIKit.SetActive(b, b.name == "Island_" + current);
		}

		void OnEntryClicked(string islandName)
		{
			bool doubleClick = lastClicked == islandName && Time.unscaledTime - lastClickTime < 0.4f;
			lastClicked = islandName; lastClickTime = Time.unscaledTime;
			nameField.text = islandName;
			if (doubleClick) OnLoad();
		}

		void OnSave()
		{
			float e;
			if (float.TryParse(elevationField.text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out e))
				DynamicIslands.currentElevation = Mathf.Clamp(e, IslandSpawner.MinElevation, IslandSpawner.MaxElevation);
			string n = nameField.text.Trim();
			if (n.Length == 0) { SetStatus("Type a name first.", true); return; }
			if (!FileNames.Valid(n)) { SetStatus(FileNames.Problem(n), true); return; }
			bool exists = File.Exists(IslandSpawner.PathFor(n));
			if (exists && n != DynamicIslands.currentIslandName && pendingOverwrite != n)
			{
				pendingOverwrite = n;
				SetStatus("'" + n + "' already exists. Press Save again to overwrite it.", true);
				return;
			}
			if (DynamicIslands.SaveIsland(n)) Close();
			else SetStatus("Saving failed - see the console (F10).", true);
		}

		void OnExport()
		{
			string n = nameField.text.Trim();
			if (n.Length == 0 || !File.Exists(IslandSpawner.PathFor(n))) { SetStatus("Pick a saved island to share (save the one you're editing first).", true); return; }
			Close();
			LibraryExportWindow.OpenIsland(n);
		}

		void OnLoad()
		{
			string n = nameField.text.Trim();
			if (n.Length == 0) { SetStatus("Pick an island to open.", true); return; }
			if (!File.Exists(IslandSpawner.PathFor(n))) { SetStatus("There is no saved island called '" + n + "'.", true); return; }
			if (DynamicIslands.LoadIsland(n)) Close();
			else SetStatus("Opening failed - see the console (F10).", true);
		}

		void OnDelete()
		{
			string n = nameField.text.Trim();
			string path = IslandSpawner.PathFor(n);
			if (n.Length == 0 || !File.Exists(path)) { SetStatus("Pick an island to delete.", true); return; }
			if (pendingDelete != n)
			{
				pendingDelete = n;
				// (what would miss it: saved worlds that have it, plans that bring it)
				List<string> worlds = LibraryPack.WorldsUsing(n);
				List<string> plans = PlansNaming(n);
				string uses = (worlds.Count > 0 ? " Saved worlds that have it: " + string.Join(", ", worlds.Take(4).ToArray()) + (worlds.Count > 4 ? " and " + (worlds.Count - 4) + " more" : "") + " - it goes missing there." : "") +
					(plans.Count > 0 ? " Plans that bring it: " + string.Join(", ", plans.Take(4).ToArray()) + "." : "");
				SetStatus("Delete '" + n + "'? Press Delete again." + uses + " (It is moved to Mods\\DynamicIslands\\" + DeletedFolderName + ", where you can get it back.)", true);
				return;
			}
			try
			{
				MoveToDeleted(n);
				pendingDelete = null;
				DynamicIslands.Notify("Deleted island '" + n + "' (kept in " + DeletedFolderName + " until you remove it there)");
				SetStatus("Deleted '" + n + "'.", false);
				Refresh();
			}
			catch (Exception ex) { SetStatus("Could not delete: " + ex.Message, true); }
		}

		public const string DeletedFolderName = "deleted";

		/// <summary>
		/// An island deleted in the editor is moved aside, not erased: Mods\DynamicIslands\deleted\&lt;name&gt;.island (an older
		/// deleted one of the same name gets a date). Put it back by moving the file into Mods\DynamicIslands.
		/// </summary>
		public static void MoveToDeleted(string name)
		{
			string path = IslandSpawner.PathFor(name);
			string folder = Path.Combine(DynamicIslands.assetpath, DeletedFolderName);
			Directory.CreateDirectory(folder);
			string to = Path.Combine(folder, Path.GetFileName(path));
			if (File.Exists(to)) File.Move(to, Path.Combine(folder, name + " " + File.GetLastWriteTime(to).ToString("yyyy-MM-dd HHmmss") + IslandFile.Extension));
			File.Move(path, to);
		}

		/// <summary>Saved plans with a rule that brings this island by name (island: or oneof:).</summary>
		public static List<string> PlansNaming(string island)
		{
			var result = new List<string>();
			foreach (string p in WorldPlan.All().Where(x => !WorldPlan.IsBuiltIn(x)))
			{
				WorldPlan plan = WorldPlan.Load(p);
				if (plan != null && plan.Rules.Any(r => (r.What == "island" && r.WhatArg.Trim().Equals(island, StringComparison.OrdinalIgnoreCase)) ||
					(r.What == "oneof" && r.WhatArg.Split(',').Any(x => x.Trim().Equals(island, StringComparison.OrdinalIgnoreCase)))))
					result.Add(p);
			}
			return result;
		}

		void SetStatus(string message, bool warning)
		{
			status.text = message;
			status.color = warning ? new Color(1f, 0.72f, 0.45f) : UIKit.TextColor;
		}
	}
}
