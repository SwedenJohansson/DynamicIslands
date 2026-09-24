using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using HMLLibrary;
using RaftModLoader;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// A readable note on a custom island in a world. Raft's own interaction is used: the object gets a collider on
	/// Raft's interactable layer with a RaycastInteractable, so looking at it shows Raft's "[E] Read ..." hint and the
	/// interact key opens the note (NoteReader). The text travels inside the island file, so every player sees it.
	/// </summary>
	public class CustomNote : MonoBehaviour, IRaycastable
	{
		public string Title = "", Text = "";

		/// <summary>Makes a spawned island object readable (its collider for Raft's interaction is added as a child).</summary>
		public static CustomNote Attach(GameObject go, IDictionary<string, string> props)
		{
			GameObject holder = InteractHolder(go);
			CustomNote note = holder.AddComponent<CustomNote>();
			note.Title = ObjectProps.Get(props, ObjectProps.NoteTitle, "");
			note.Text = ObjectProps.Get(props, ObjectProps.NoteText, "");
			return note;
		}

		public const string HolderName = "CI_Interact";

		/// <summary>
		/// The child that Raft's "what am I looking at" ray finds for an island object: a collider covering its model
		/// on Raft's interactable layer, with a RaycastInteractable. Notes and chests on the same object share it.
		/// </summary>
		public static GameObject InteractHolder(GameObject go)
		{
			Transform existing = go.transform.Find(HolderName);
			if (existing != null) return existing.gameObject;
			var holder = new GameObject(HolderName);
			holder.transform.SetParent(go.transform, false);
			holder.layer = InteractLayer();
			var box = holder.AddComponent<BoxCollider>();
			// Covers the object's model, a little bigger so small papers are easy to aim at
			Bounds b;
			if (LocalBounds(go, out b))
			{
				Vector3 s = go.transform.lossyScale;
				box.center = b.center;
				box.size = new Vector3(Mathf.Max(b.size.x, 0.3f / Mathf.Max(0.01f, s.x)), Mathf.Max(b.size.y, 0.3f / Mathf.Max(0.01f, s.y)), Mathf.Max(b.size.z, 0.3f / Mathf.Max(0.01f, s.z))) * 1.1f;
			}
			else box.size = Vector3.one * 0.5f;
			box.isTrigger = Physics.queriesHitTriggers; // a trigger doesn't get in the player's way, if Raft's rays see triggers
			holder.AddComponent<RaycastInteractable>();
			return holder;
		}

		/// <summary>The first layer Raft's "what am I looking at" ray looks for interactable things on.</summary>
		static int InteractLayer()
		{
			int named = LayerMask.NameToLayer("RaycastInteractable");
			int mask = LayerMasks.MASK_RaycastInteractable;
			if (named >= 0 && (mask & (1 << named)) != 0) return named;
			for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
			return 0;
		}

		static bool LocalBounds(GameObject go, out Bounds b)
		{
			b = new Bounds();
			bool any = false;
			Matrix4x4 toLocal = go.transform.worldToLocalMatrix;
			foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
			{
				if (mf.sharedMesh == null) continue;
				Bounds mb = mf.sharedMesh.bounds;
				Matrix4x4 m = toLocal * mf.transform.localToWorldMatrix;
				for (int i = 0; i < 8; i++)
				{
					Vector3 p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
					if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
				}
			}
			return any;
		}

		internal static DisplayTextManager Hints { get { try { return ComponentManager<DisplayTextManager>.Value; } catch { return null; } } }

		internal static bool InteractPressed()
		{
			try { return MyInput.GetButtonDown("Interact"); } catch { return Input.GetKeyDown(KeyCode.E); }
		}

		internal static KeyCode InteractKey
		{
			get
			{
				try { Keybind k; return MyInput.Keybinds != null && MyInput.Keybinds.TryGetValue("Interact", out k) ? k.MainKey : KeyCode.E; }
				catch { return KeyCode.E; }
			}
		}

		void IRaycastable.OnIsRayed()
		{
			if (NoteReader.IsOpen) return;
			// A chest with a note: the chest shows the hint and opens first (then shows the note itself)
			LootCrate crate = GetComponent<LootCrate>();
			if (crate != null && !crate.Looted && crate.Items.Count > 0) return;
			DisplayTextManager hints = Hints;
			if (hints != null && crate == null) hints.ShowText("Read " + (Title.Length > 0 ? "\"" + Title + "\"" : "the note"), InteractKey, 0, 0, true);
			if (InteractPressed())
			{
				if (hints != null) hints.HideDisplayTexts();
				NoteReader.Open(this);
			}
		}

		void IRaycastable.OnRayEnter() { }

		void IRaycastable.OnRayExit()
		{
			DisplayTextManager hints = Hints;
			if (hints != null) hints.HideDisplayTexts();
		}
	}

	/// <summary>
	/// Shows a note on screen like a sheet of paper: title, text (scrolls when long), and Close. It closes with the
	/// interact key, Tab, the button, or when the player walks away. The mouse is freed while it is open.
	/// </summary>
	public class NoteReader : MonoBehaviour
	{
		static NoteReader instance;
		public static bool IsOpen { get { return instance != null && instance.gameObject.activeSelf; } }
		/// <summary>The note being read (the automated tests look at it).</summary>
		public static CustomNote Current { get { return IsOpen ? instance.note : null; } }
		public static string ShownTitle { get { return IsOpen ? instance.titleText.text : null; } }
		public static string ShownText { get { return IsOpen ? instance.bodyText.text : null; } }

		static readonly Color Paper = new Color(0.94f, 0.9f, 0.8f, 0.98f), Ink = new Color(0.2f, 0.15f, 0.1f, 1f), PaperEdge = new Color(0.55f, 0.45f, 0.3f, 1f);

		CustomNote note;
		Text titleText, bodyText;
		ScrollRect scroll;
		float openedAt;
		bool cursorWasFree;

		public static void Open(CustomNote n)
		{
			if (n == null) return;
			if (instance == null) Build();
			instance.Show(n);
		}

		public static void Close() { if (instance != null) instance.Hide(); }

		static void Build()
		{
			Canvas canvas = UIKit.CreateCanvas("CustomIslands_NoteReader", 500);
			DontDestroyOnLoad(canvas.gameObject);
			instance = canvas.gameObject.AddComponent<NoteReader>();

			RectTransform sheet = UIKit.Rect("Sheet", canvas.transform);
			UIKit.Anchor(sheet, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 640));
			UIKit.Background(sheet.gameObject, Paper, 6);
			UIKit.Border(sheet, PaperEdge, 6, 2f);
			UIKit.Vertical(sheet.gameObject, 12f, new RectOffset(34, 34, 28, 22));

			instance.titleText = UIKit.Label(sheet, "", 26, Ink, TextAnchor.MiddleCenter, FontStyle.Bold, "Title");
			instance.titleText.font = SerifFont() ?? UIKit.Font;
			UIKit.Size(instance.titleText.gameObject, -1, 40);
			RectTransform line = UIKit.Rect("Line", sheet);
			line.gameObject.AddComponent<Image>().color = new Color(PaperEdge.r, PaperEdge.g, PaperEdge.b, 0.5f);
			UIKit.Size(line.gameObject, -1, 2);

			RectTransform content = UIKit.ScrollList(sheet, out instance.scroll, 0f);
			UIKit.Size(instance.scroll.gameObject, -1, -1).flexibleHeight = 1f;
			instance.bodyText = UIKit.Label(content, "", 18, Ink, TextAnchor.UpperLeft, FontStyle.Normal, "Body");
			instance.bodyText.font = SerifFont() ?? UIKit.Font;
			instance.bodyText.lineSpacing = 1.15f;
			instance.bodyText.supportRichText = false; // (the list's layout sizes it to its text)

			RectTransform bottom = UIKit.Row(sheet, 34f, 8f, "Bottom");
			Text hint = UIKit.Label(bottom, "", 13, new Color(Ink.r, Ink.g, Ink.b, 0.6f), TextAnchor.MiddleLeft, FontStyle.Italic, "Hint");
			hint.text = "Press the interact key or Tab to close";
			UIKit.Button(bottom, "Close", Close, null, 110, 34);
			canvas.gameObject.SetActive(false);
		}

		/// <summary>A book-like font if Windows has one (falls back to the UI font).</summary>
		static Font SerifFont()
		{
			if (serif == null && !serifTried)
			{
				serifTried = true;
				try { serif = Font.CreateDynamicFontFromOSFont(new[] { "Georgia", "Palatino Linotype", "Times New Roman" }, 18); } catch { }
			}
			return serif;
		}
		static Font serif;
		static bool serifTried;

		void Show(CustomNote n)
		{
			note = n;
			titleText.text = n.Title.Length > 0 ? n.Title : "Note";
			bodyText.text = n.Text.Length > 0 ? n.Text : "(The page is empty.)";
			gameObject.SetActive(true);
			Canvas.ForceUpdateCanvases();
			scroll.verticalNormalizedPosition = 1f;
			openedAt = Time.unscaledTime;
			cursorWasFree = Cursor.visible;
			if (!cursorWasFree) try { RAPI.ToggleCursor(true); } catch { }
			Debug.Log("[CUSTOM ISLANDS] Reading note '" + titleText.text + "'");
			QuestTracker.Event(ContentState.EntryOf(n.transform), "read", n.Title);
		}

		void Hide()
		{
			if (!gameObject.activeSelf) return;
			gameObject.SetActive(false);
			note = null;
			if (!cursorWasFree) try { RAPI.ToggleCursor(false); } catch { }
		}

		void Update()
		{
			// Not in the same frame as the key press that opened it
			if (Time.unscaledTime - openedAt < 0.2f) return;
			bool interact;
			try { interact = MyInput.GetButtonDown("Interact"); } catch { interact = false; }
			if (interact || Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Escape)) { Hide(); return; }
			Network_Player player = null;
			try { player = ComponentManager<Network_Player>.Value; } catch { }
			if (note == null || !LoadSceneManager.IsGameSceneLoaded || (player != null && Vector3.Distance(player.transform.position, note.transform.position) > 8f)) Hide();
		}
	}
}
