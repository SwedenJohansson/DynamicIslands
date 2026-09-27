using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The level up system on screen: the EXP a hit gave floating up over the monster ("+5"); the level and its EXP
	/// bar just under Raft's own health, thirst and hunger bars (with a Stats button while Raft's inventory is open);
	/// and a box when the system turns on or the player levels up (clicking it opens the stats page).
	/// </summary>
	public class LevelHud : MonoBehaviour
	{
		const float FloatSeconds = 1.4f, FloatRise = 1.1f, AnnounceSeconds = 5f, LevelUpSeconds = 12f, PulseSeconds = 1.2f;
		static readonly Color Gold = new Color(1f, 0.86f, 0.35f, 1f);

		class Floater { public Text Text; public Vector3 At; public float Born; }

		static LevelHud instance;
		readonly List<Floater> floaters = new List<Floater>();
		RectTransform canvasRect;
		CanvasGroup banner;
		Text bannerTitle, bannerText;
		Button bannerButton;
		float bannerShownAt = -100f, bannerSeconds = AnnounceSeconds;

		// The level bar under Raft's stat bars (in Raft's own HUD, so it hides with it)
		RectTransform hud, hudFill;
		Text hudLevel, hudXp;
		Button hudStats;
		Image hudTrack;
		CanvasHelper hudOf;
		float nextPlace, pulseAt = -100f;

		/// <summary>The texts floating now, and the last box shown (tests).</summary>
		public static string[] Floating { get { return instance == null ? new string[0] : instance.floaters.Where(f => f.Text.gameObject.activeSelf).Select(f => f.Text.text).ToArray(); } }
		public static string LastFloat { get; private set; }
		public static string LastAnnounce { get; private set; }
		/// <summary>The level bar is up next to Raft's stat bars.</summary>
		public static bool BarShown { get { return instance != null && instance.hud != null && instance.hud.gameObject.activeInHierarchy; } }
		/// <summary>What the level bar says, and where it is against Raft's stat bars (tests).</summary>
		public static string BarText { get { return BarShown ? instance.hudLevel.text + " " + instance.hudXp.text : null; } }
		public static string BarPlace { get; private set; }
		public static Button StatsButton { get { return instance != null ? instance.hudStats : null; } }
		public static Button BannerButton { get { return instance != null && instance.banner.gameObject.activeSelf ? instance.bannerButton : null; } }

		static LevelHud Get()
		{
			if (instance == null) Build();
			return instance;
		}

		/// <summary>Makes sure the HUD exists (the mod calls it every frame while the system is on).</summary>
		public static void Ensure() { Get(); }

		/// <summary>"+5 EXP" floating up from a place in the world.</summary>
		public static void Float(string text, Vector3 at)
		{
			LevelHud h = Get();
			Floater f = h.floaters.FirstOrDefault(x => !x.Text.gameObject.activeSelf);
			if (f == null)
			{
				Text t = UIKit.Label(h.canvasRect, "", 30, Gold, TextAnchor.MiddleCenter, FontStyle.Bold, "Exp");
				t.horizontalOverflow = HorizontalWrapMode.Overflow;
				UIKit.Anchor(t.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(160, 40));
				Outline o = t.gameObject.AddComponent<Outline>();
				o.effectColor = new Color(0.2f, 0.1f, 0.03f, 0.9f);
				o.effectDistance = new Vector2(1.5f, -1.5f);
				t.raycastTarget = false;
				f = new Floater { Text = t };
				h.floaters.Add(f);
			}
			f.Text.text = text + " EXP";
			f.At = at;
			f.Born = Time.unscaledTime;
			f.Text.gameObject.SetActive(true);
			LastFloat = f.Text.text;
			h.Place(f);
		}

		/// <summary>EXP came in: the level bar lights up for a moment.</summary>
		public static void ShowBar()
		{
			LevelHud h = Get();
			h.pulseAt = Time.unscaledTime;
			h.RefreshBar();
		}

		/// <summary>A box in the middle of the top of the screen; a clickable one opens the stats page (when the mouse is free: in a menu).</summary>
		public static void Announce(string title, string text, bool clickable = false)
		{
			LevelHud h = Get();
			h.bannerTitle.text = title;
			h.bannerText.text = text;
			h.bannerShownAt = Time.unscaledTime;
			h.bannerSeconds = clickable ? LevelUpSeconds : AnnounceSeconds;
			h.bannerButton.interactable = clickable;
			h.banner.blocksRaycasts = clickable;
			h.banner.gameObject.SetActive(true);
			h.banner.alpha = 0f;
			LastAnnounce = title + " | " + text;
			Debug.Log("[CUSTOM ISLANDS] Levels: " + LastAnnounce);
		}

		/// <summary>Hides everything (leaving a world, the system off).</summary>
		public static void Clear()
		{
			if (instance == null) return;
			foreach (Floater f in instance.floaters) f.Text.gameObject.SetActive(false);
			instance.banner.gameObject.SetActive(false);
			if (instance.hud != null) instance.hud.gameObject.SetActive(false);
		}

		static void Build()
		{
			Canvas canvas = UIKit.CreateCanvas("CustomIslands_Levels", 410); // (above the island banners (400): an EXP number behind one would be lost)
			DontDestroyOnLoad(canvas.gameObject);
			instance = canvas.gameObject.AddComponent<LevelHud>();
			instance.canvasRect = (RectTransform)canvas.transform;

			// The box, below where the island banners show; a level up's box opens the stats page when clicked
			RectTransform n = UIKit.Rect("LevelBanner", canvas.transform);
			UIKit.Anchor(n, new Vector2(0.5f, 1f), new Vector2(0, -190), new Vector2(560, 0));
			UIKit.Surface(n);
			UIKit.Vertical(n.gameObject, 4f, new RectOffset(22, 22, 12, 14), true);
			instance.bannerTitle = UIKit.Label(n, "", 28, UIKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold, "Title");
			instance.bannerText = UIKit.Label(n, "", 16, UIKit.TextColor, TextAnchor.MiddleCenter, FontStyle.Normal, "Text");
			foreach (Graphic g in n.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
			Image hit = n.GetComponent<Image>() ?? n.gameObject.AddComponent<Image>();
			hit.raycastTarget = true;
			instance.bannerButton = n.gameObject.AddComponent<Button>();
			instance.bannerButton.targetGraphic = hit;
			var nav = instance.bannerButton.navigation; nav.mode = Navigation.Mode.None; instance.bannerButton.navigation = nav;
			instance.bannerButton.onClick.AddListener(() => { instance.banner.gameObject.SetActive(false); LevelWindow.OpenFromGame(); });
			UIKit.Hint(n.gameObject, "Open the stats page");
			instance.banner = n.gameObject.AddComponent<CanvasGroup>();
			n.gameObject.SetActive(false);
			PlayerLevels.Changed += () => { if (instance != null) instance.RefreshBar(); };
		}

		/// <summary>
		/// Builds the level bar in Raft's HUD, next to its stat bars (CanvasHelper.StatSliderParent: health, thirst and
		/// hunger): as wide as they are, just below them (above them if that would leave the screen).
		/// </summary>
		void PlaceHud()
		{
			CanvasHelper helper = null;
			try { helper = ComponentManager<CanvasHelper>.Value; } catch { }
			if (helper == null) helper = FindObjectOfType<CanvasHelper>();
			if (helper == null || helper.StatSliderParent == null) return;
			RectTransform parent = helper.StatSliderParent.transform as RectTransform;
			if (parent == null) return;
			if (hud == null || hudOf != helper)
			{
				if (hud != null) Destroy(hud.gameObject);
				hudOf = helper;
				BuildHud(parent);
			}
			// Where Raft's bars are, in the parent's space
			var sliders = new[] { helper.healthSlider, helper.thirstSlider, helper.hungerSlider }.Where(s => s != null).Select(s => (RectTransform)s.transform).ToList();
			if (sliders.Count == 0) return;
			float xMin = float.MaxValue, xMax = float.MinValue, yMin = float.MaxValue, yMax = float.MinValue, rowH = 0f;
			var corners = new Vector3[4];
			foreach (RectTransform r in sliders)
			{
				r.GetWorldCorners(corners);
				Vector3 a = parent.InverseTransformPoint(corners[0]), b = parent.InverseTransformPoint(corners[2]);
				xMin = Mathf.Min(xMin, a.x, b.x); xMax = Mathf.Max(xMax, a.x, b.x);
				yMin = Mathf.Min(yMin, a.y, b.y); yMax = Mathf.Max(yMax, a.y, b.y);
				rowH = Mathf.Max(rowH, Mathf.Abs(b.y - a.y));
			}
			// (as tall as one of Raft's bars, a little less; the text follows)
			float h = Mathf.Max(8f, rowH * 0.8f), gap = Mathf.Max(2f, rowH * 0.25f);
			SizeHud(h);
			hud.anchorMin = hud.anchorMax = parent.pivot;
			hud.pivot = new Vector2(0f, 1f);
			hud.sizeDelta = new Vector2(xMax - xMin, h);
			// (local space is around the parent's pivot: the anchor sits there)
			hud.anchoredPosition = new Vector2(xMin, yMin - gap);
			// Below the bars, unless that is off the screen: then above them
			hud.GetWorldCorners(corners);
			Canvas c = parent.GetComponentInParent<Canvas>();
			Camera cam = c != null && c.renderMode != RenderMode.ScreenSpaceOverlay ? c.worldCamera : null;
			float bottom = RectTransformUtility.WorldToScreenPoint(cam, corners[0]).y;
			bool above = bottom < 2f;
			if (above) { hud.pivot = new Vector2(0f, 0f); hud.anchoredPosition = new Vector2(xMin, yMax + gap); }
			hud.SetAsLastSibling();
			BarPlace = (above ? "above" : "below") + " Raft's " + sliders.Count + " stat bars, " + (xMax - xMin).ToString("F0") + " wide";
		}

		void BuildHud(RectTransform parent)
		{
			hud = UIKit.Rect("CustomIslands_LevelBar", parent);
			// (placed by hand next to Raft's bars: a layout group on their parent must leave it alone; its parts are placed
			// by anchors in fractions of it, so they follow Raft's HUD scale)
			UIKit.Ensure<LayoutElement>(hud.gameObject).ignoreLayout = true;
			hudLevel = UIKit.Label(hud, "LV 1", 14, Gold, TextAnchor.MiddleLeft, FontStyle.Bold, "Level");
			hudLevel.horizontalOverflow = HorizontalWrapMode.Overflow;
			Span(hudLevel.rectTransform, 0f, 0.2f, 0f, 1f);
			RectTransform track = UIKit.Rect("Track", hud);
			Span(track, 0.2f, 0.72f, 0.3f, 0.7f);
			hudTrack = UIKit.Background(track.gameObject, new Color(0.1f, 0.06f, 0.02f, 0.75f), 4);
			hudFill = UIKit.Rect("Fill", track);
			hudFill.anchorMin = Vector2.zero; hudFill.anchorMax = new Vector2(0f, 1f);
			hudFill.offsetMin = Vector2.zero; hudFill.offsetMax = Vector2.zero;
			UIKit.Background(hudFill.gameObject, Gold, 4);
			hudXp = UIKit.Label(hud, "", 11, new Color(1f, 0.95f, 0.85f, 0.9f), TextAnchor.MiddleRight, FontStyle.Normal, "Xp");
			hudXp.horizontalOverflow = HorizontalWrapMode.Overflow;
			Span(hudXp.rectTransform, 0.74f, 1f, 0f, 1f);
			// While Raft's inventory is open (the mouse is free): a button for the stats page, right of the bar
			hudStats = UIKit.Button(hud, "Stats", LevelWindow.OpenFromGame, "Your level and stat points (" + PlayerLevels.Key + ")", 64, 22f, 12);
			RectTransform sb = (RectTransform)hudStats.transform;
			sb.anchorMin = new Vector2(1f, 0f); sb.anchorMax = new Vector2(1f, 1f); sb.pivot = new Vector2(0f, 0.5f);
			hudStats.gameObject.SetActive(false);
			foreach (Graphic g in hud.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = g.transform.IsChildOf(hudStats.transform);
			RefreshBar();
		}

		static void Span(RectTransform r, float x0, float x1, float y0, float y1)
		{
			r.anchorMin = new Vector2(x0, y0); r.anchorMax = new Vector2(x1, y1);
			r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
		}

		/// <summary>Text and button sizes that follow the bar's height (Raft's HUD scale).</summary>
		void SizeHud(float h)
		{
			hudLevel.fontSize = Mathf.Max(8, Mathf.RoundToInt(h * 0.62f));
			hudXp.fontSize = Mathf.Max(7, Mathf.RoundToInt(h * 0.5f));
			RectTransform sb = (RectTransform)hudStats.transform;
			sb.sizeDelta = new Vector2(h * 3.2f, h * 0.2f);
			sb.anchoredPosition = new Vector2(h * 0.3f, 0f);
			Text t = UIKit.LabelOf(hudStats);
			if (t != null) { t.resizeTextMaxSize = Mathf.Max(8, Mathf.RoundToInt(h * 0.6f)); t.fontSize = t.resizeTextMaxSize; }
		}

		void RefreshBar()
		{
			if (hud == null) return;
			LevelRecord r = PlayerLevels.Mine;
			if (r == null) return;
			int level = r.Level, start = LevelRules.TotalFor(level), need = LevelRules.XpFor(level);
			hudLevel.text = "LV " + level + (r.Unspent > 0 ? "<color=#ffffff>+</color>" : "");
			hudXp.text = (r.Xp - start) + " / " + need;
			hudFill.anchorMax = new Vector2(Mathf.Clamp01((r.Xp - start) / (float)need), 1f);
		}

		void Place(Floater f)
		{
			Camera cam = Camera.main;
			float t = (Time.unscaledTime - f.Born) / FloatSeconds;
			if (cam == null || t >= 1f) { f.Text.gameObject.SetActive(false); return; }
			Vector3 world = f.At + Vector3.up * (FloatRise * t);
			Vector3 screen = cam.WorldToScreenPoint(world);
			if (screen.z <= 0.1f) { f.Text.enabled = false; return; }
			f.Text.enabled = true;
			Vector2 local;
			RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);
			f.Text.rectTransform.anchoredPosition = local;
			// Pops in, then fades out; a little smaller far away
			float scale = Mathf.Lerp(1.35f, 1f, Mathf.Clamp01(t * 6f)) * Mathf.Clamp(12f / Mathf.Max(1f, screen.z), 0.6f, 1.2f);
			f.Text.rectTransform.localScale = Vector3.one * scale;
			Color c = f.Text.color;
			c.a = Mathf.Clamp01((1f - t) / 0.35f);
			f.Text.color = c;
		}

		void Update()
		{
			bool inWorld = LoadSceneManager.IsGameSceneLoaded && !DynamicIslands.InEditor();
			if (!inWorld || !PlayerLevels.On) { Clear(); return; }
			foreach (Floater f in floaters) if (f.Text.gameObject.activeSelf) Place(f);
			if (banner.gameObject.activeSelf)
			{
				float t = Time.unscaledTime - bannerShownAt;
				banner.alpha = Mathf.Clamp01(t / 0.4f) * Mathf.Clamp01((bannerSeconds - t) / 1f);
				if (t > bannerSeconds) banner.gameObject.SetActive(false);
			}
			// The level bar: placed again now and then (Raft's HUD can change size), lit up for a moment after EXP
			if (Time.unscaledTime >= nextPlace || hud == null) { nextPlace = Time.unscaledTime + 2f; PlaceHud(); }
			if (hud == null) return;
			if (!hud.gameObject.activeSelf) { hud.gameObject.SetActive(true); RefreshBar(); }
			float p = Mathf.Clamp01(1f - (Time.unscaledTime - pulseAt) / PulseSeconds);
			hudTrack.color = Color.Lerp(new Color(0.1f, 0.06f, 0.02f, 0.75f), new Color(0.55f, 0.42f, 0.12f, 0.9f), p);
			bool menu = CanvasHelper.ActiveMenu == MenuType.Inventory;
			if (hudStats.gameObject.activeSelf != menu) hudStats.gameObject.SetActive(menu);
		}
	}

	/// <summary>
	/// Other players' levels, kept discreet: a small "Lv 5" in gold under the name Raft shows over each other player
	/// (Network_Player.playerNameTextMesh), only while the level up system is on. Their own name tag stays Raft's.
	/// </summary>
	public static class LevelTags
	{
		const string TagName = "CustomIslands_Level";
		static readonly Color TagColor = new Color(1f, 0.84f, 0.4f, 0.8f);
		static float nextCheck;

		/// <summary>What each other player's tag says now, by Steam id (tests).</summary>
		public static readonly Dictionary<ulong, string> Shown = new Dictionary<ulong, string>();

		/// <summary>Every frame from the mod (checks twice a second).</summary>
		public static void Tick()
		{
			if (Time.unscaledTime < nextCheck) return;
			nextCheck = Time.unscaledTime + 0.5f;
			Shown.Clear();
			Network_Player local = null;
			try { local = RAPI.GetLocalPlayer(); } catch { }
			foreach (Network_Player p in Object.FindObjectsOfType<Network_Player>())
			{
				if (p == null || p == local || p.playerNameTextMesh == null) continue;
				TextMesh name = p.playerNameTextMesh;
				Transform t = name.transform.Find(TagName);
				int level = PlayerLevels.LevelOf(p.steamID.Id);
				if (level <= 0) { if (t != null) t.gameObject.SetActive(false); continue; }
				TextMesh tag = t != null ? t.GetComponent<TextMesh>() : Make(name);
				tag.gameObject.SetActive(true);
				string text = "Lv " + level;
				if (tag.text != text) tag.text = text;
				Shown[p.steamID.Id] = text;
			}
		}

		/// <summary>A smaller copy of the name's text just under it (same font, facing the camera with it).</summary>
		static TextMesh Make(TextMesh name)
		{
			var go = new GameObject(TagName);
			go.layer = name.gameObject.layer;
			go.transform.SetParent(name.transform, false);
			var tag = go.AddComponent<TextMesh>();
			tag.font = name.font;
			tag.fontSize = name.fontSize;
			tag.fontStyle = name.fontStyle;
			tag.characterSize = name.characterSize * 0.62f;
			tag.anchor = TextAnchor.UpperCenter;
			tag.alignment = TextAlignment.Center;
			tag.color = TagColor;
			MeshRenderer from = name.GetComponent<MeshRenderer>(), to = go.GetComponent<MeshRenderer>();
			if (from != null && to != null) { to.sharedMaterial = from.sharedMaterial; to.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; to.receiveShadows = false; }
			// Just under the name: its lowest point, in its own space (the name faces the camera, so up stays up)
			float below = 0f;
			if (from != null && from.bounds.size.y > 0.001f)
				below = name.transform.InverseTransformPoint(new Vector3(from.bounds.center.x, from.bounds.min.y, from.bounds.center.z)).y;
			else below = -name.characterSize * name.fontSize * 0.1f;
			go.transform.localPosition = new Vector3(0f, below - name.characterSize * 0.5f, 0f);
			return tag;
		}
	}

	/// <summary>
	/// The stats page (K) in a world with the level up system on: the level and EXP, the monsters defeated, and the nine stats to put the
	/// level's points into (+1% a point, at most 10 each). Points put in can be taken back until the page is closed.
	/// </summary>
	public class LevelWindow : MonoBehaviour
	{
		static LevelWindow instance;
		public static bool IsOpen { get { return instance != null && instance.gameObject.activeSelf; } }

		Text levelText, pointsText, xpText, totalText, killsText;
		RectTransform fill;
		readonly Image[][] pips = new Image[LevelRules.StatCount][];
		readonly Text[] values = new Text[LevelRules.StatCount];
		readonly Button[] plus = new Button[LevelRules.StatCount], minus = new Button[LevelRules.StatCount];
		int[] openedWith = new int[LevelRules.StatCount];
		bool cursorWasFree;

		static readonly Color PipOn = new Color(0.97f, 0.8f, 0.42f, 1f), PipOff = new Color(0.16f, 0.09f, 0.04f, 0.85f);

		/// <summary>Every frame from the mod: K opens and closes the page while the level up system is on.</summary>
		public static void Tick()
		{
			if (!LoadSceneManager.IsGameSceneLoaded || DynamicIslands.InEditor() || !PlayerLevels.On) { if (IsOpen) instance.Hide(); return; }
			if (!Input.GetKeyDown(PlayerLevels.Key) || Typing() || NoteReader.IsOpen || JournalWindow.IsOpen) return;
			if (IsOpen) instance.Hide(); else Open();
		}

		static bool Typing()
		{
			GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
			InputField f = sel != null ? sel.GetComponent<InputField>() : null;
			return f != null && f.isFocused;
		}

		public static void Open()
		{
			if (!PlayerLevels.On) return;
			if (instance == null) Build();
			instance.Show();
		}

		public static void Close() { if (instance != null) instance.Hide(); }

		/// <summary>
		/// Opens the page from Raft's screen (the Stats button by Raft's stat bars in the inventory, a level up's box):
		/// Raft's open menu closes first, so the page gets the mouse and gives it back to the game when it closes.
		/// </summary>
		public static void OpenFromGame()
		{
			if (!PlayerLevels.On) return;
			MenuType open = CanvasHelper.ActiveMenu;
			if (open != MenuType.None)
			{
				CanvasHelper helper = null;
				try { helper = ComponentManager<CanvasHelper>.Value; } catch { }
				if (helper == null) helper = FindObjectOfType<CanvasHelper>();
				if (helper != null) try
				{
					helper.CloseMenu(open);
					// (CloseMenu only closes a menu it finds open: anything left, Raft closes all of them)
					if (CanvasHelper.ActiveMenu != MenuType.None) helper.CloseAllMenus();
				}
				catch (System.Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Closing Raft's menu: " + e.Message); }
				if (CanvasHelper.ActiveMenu != MenuType.None) Debug.LogWarning("[CUSTOM ISLANDS] Raft's " + CanvasHelper.ActiveMenu + " menu did not close for the stats page");
			}
			DynamicIslands.instance.StartCoroutine(OpenNextFrame());
		}

		static IEnumerator OpenNextFrame()
		{
			yield return null;
			yield return null;
			Open();
		}

		static void Build()
		{
			Canvas canvas = UIKit.CreateCanvas("CustomIslands_Stats", 492);
			DontDestroyOnLoad(canvas.gameObject);
			instance = canvas.gameObject.AddComponent<LevelWindow>();
			PlayerLevels.Changed += () => { if (IsOpen) instance.Fill(); };

			RectTransform panel = UIKit.Panel(canvas.transform, "Panel", new RectOffset(20, 20, 14, 16), 8f, false);
			UIKit.Anchor(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(640, 648));
			RectTransform head = UIKit.Row(panel, 34f, 8f, "Head");
			instance.levelText = UIKit.Label(head, "LEVEL 1", 22, UIKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold, "Level");
			instance.pointsText = UIKit.Label(head, "", 15, UIKit.TextColor, TextAnchor.MiddleRight, FontStyle.Normal, "Points");

			RectTransform xp = UIKit.Group(panel, "Experience");
			RectTransform track = UIKit.Rect("Track", xp);
			UIKit.Size(track.gameObject, -1, 16);
			UIKit.Background(track.gameObject, new Color(0.15f, 0.08f, 0.03f, 0.9f), 5);
			instance.fill = UIKit.Rect("Fill", track);
			instance.fill.anchorMin = Vector2.zero; instance.fill.anchorMax = new Vector2(0f, 1f);
			instance.fill.offsetMin = Vector2.zero; instance.fill.offsetMax = Vector2.zero;
			UIKit.Background(instance.fill.gameObject, PipOn, 5);
			RectTransform xpRow = UIKit.Row(xp, 20f, 6f, "XpRow");
			instance.xpText = UIKit.Label(xpRow, "", 13, UIKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, "Xp");
			instance.totalText = UIKit.Label(xpRow, "", 12, UIKit.TextMuted, TextAnchor.MiddleRight, FontStyle.Normal, "Total");
			instance.killsText = UIKit.Label(xp, "", 14, UIKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, "Kills");

			RectTransform stats = UIKit.Group(panel, "Stats");
			for (int i = 0; i < LevelRules.StatCount; i++)
			{
				int stat = i;
				RectTransform row = UIKit.Row(stats, 30f, 6f, "Stat_" + LevelRules.StatNames[i]);
				Text name = UIKit.Label(row, LevelRules.StatNames[i], 15, UIKit.TextColor, TextAnchor.MiddleLeft, FontStyle.Normal, "Name");
				UIKit.Size(name.gameObject, 118, -1, 0);
				RectTransform pipRow = UIKit.Rect("Pips", row);
				UIKit.Horizontal(pipRow.gameObject, 3f);
				UIKit.Size(pipRow.gameObject, 10 * 17 + 9 * 3, 16, 0);
				instance.pips[i] = new Image[LevelRules.MaxPoints];
				for (int p = 0; p < LevelRules.MaxPoints; p++)
				{
					RectTransform pip = UIKit.Rect("Pip" + p, pipRow);
					UIKit.Size(pip.gameObject, 17, 16, 0);
					instance.pips[i][p] = UIKit.Background(pip.gameObject, PipOff, 3);
				}
				instance.values[i] = UIKit.Label(row, "", 14, UIKit.Accent, TextAnchor.MiddleRight, FontStyle.Normal, "Value");
				UIKit.Size(instance.values[i].gameObject, 74, -1, 1); // (takes the row's free space: the buttons sit at its right edge)
				instance.minus[i] = UIKit.Button(row, "−", () => PlayerLevels.Spend(stat, -1), "Take back a point put into " + LevelRules.StatNames[i].ToLowerInvariant() + " (until you close this page)", 34, 28f, 16);
				instance.plus[i] = UIKit.Button(row, "+", () => PlayerLevels.Spend(stat, 1), "Put a stat point into " + LevelRules.StatNames[i].ToLowerInvariant() + ": " + LevelRules.StatHints[i].ToLowerInvariant() + " (+1% a point, at most " + LevelRules.MaxPoints + ")", 34, 28f, 16);
				UIKit.Hint(row.gameObject, LevelRules.StatHints[i]);
			}

			Text about = UIKit.Label(panel, "Hit monsters to earn EXP: the tougher the monster and the harder it bites, the more (Bruce the shark is worth " + LevelRules.ReferenceXp +
				"). Level 2 takes about 5 monster kills, level 3 about 10 more, level 4 about 20 more, then 10 more each level. Every level gives " + LevelRules.PointsPerLevel +
				" stat points; each point is +1% (Hunger, Thirst and Oxygen last 1% longer), at most " + LevelRules.MaxPoints + " points in a stat. With every stat full the levels go on, without points.", 12, UIKit.TextMuted, TextAnchor.UpperLeft, FontStyle.Normal, "About");
			about.lineSpacing = 1.05f;
			RectTransform bottom = UIKit.Row(panel, 34f, 8f, "Bottom");
			UIKit.Label(bottom, PlayerLevels.Key + " or Esc to close", 13, UIKit.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic);
			UIKit.Button(bottom, "Close", Close, null, 120, 34);
			canvas.gameObject.SetActive(false);
		}

		void Show()
		{
			gameObject.SetActive(true);
			cursorWasFree = Cursor.visible;
			if (!cursorWasFree) try { RAPI.ToggleCursor(true); } catch { }
			LevelRecord r = PlayerLevels.Mine;
			openedWith = r != null ? (int[])r.Points.Clone() : new int[LevelRules.StatCount];
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
			if (Input.GetKeyDown(KeyCode.Escape)) Hide();
		}

		void Fill()
		{
			LevelRecord r = PlayerLevels.Mine;
			if (r == null) return;
			int level = r.Level, start = LevelRules.TotalFor(level), need = LevelRules.XpFor(level);
			levelText.text = "LEVEL " + level;
			pointsText.text = r.Unspent > 0 ? "<color=#f8cc6b>" + r.Unspent + "</color> stat point(s) to spend" : r.AllFull ? "Every stat is full" : "No stat points to spend";
			xpText.text = (r.Xp - start) + " / " + need + " EXP to level " + (level + 1);
			totalText.text = r.Xp + " EXP in all";
			killsText.text = "Monsters defeated: <color=#f8cc6b>" + r.Kills + "</color>";
			fill.anchorMax = new Vector2(Mathf.Clamp01((r.Xp - start) / (float)need), 1f);
			for (int i = 0; i < LevelRules.StatCount; i++)
			{
				int p = r.Points[i];
				for (int k = 0; k < LevelRules.MaxPoints; k++) pips[i][k].color = k < p ? PipOn : PipOff;
				values[i].text = p == 0 ? "+0%" : "+" + p + "%" + (p >= LevelRules.MaxPoints ? " max" : "");
				plus[i].interactable = r.Unspent > 0 && p < LevelRules.MaxPoints;
				Color plusInk = UIKit.AccentText;
				plusInk.a = plus[i].interactable ? 1f : 0.3f;
				UIKit.LabelOf(plus[i]).color = plusInk;
				// (the button stays in its place, dimmed when there is nothing to take back, so the row doesn't jump)
				minus[i].interactable = p > openedWith[i];
				Color ink = UIKit.AccentText;
				ink.a = p > openedWith[i] ? 1f : 0.3f;
				UIKit.LabelOf(minus[i]).color = ink;
			}
		}

		/// <summary>What the page shows (tests): "LEVEL 3 | 2 stat point(s) to spend | Walk speed +4% ...".</summary>
		public static string Shown
		{
			get
			{
				if (!IsOpen) return null;
				return instance.levelText.text + " | " + instance.pointsText.text + " | " + instance.xpText.text + " | " + instance.killsText.text.Replace("<color=#f8cc6b>", "").Replace("</color>", "") + " | " +
					string.Join(", ", Enumerable.Range(0, LevelRules.StatCount).Select(i => LevelRules.StatNames[i] + " " + instance.values[i].text).ToArray());
			}
		}

		/// <summary>Clicks a stat's + or - button (tests), as a player would.</summary>
		public static bool Click(int stat, bool add)
		{
			if (!IsOpen) return false;
			Button b = add ? instance.plus[stat] : instance.minus[stat];
			if (!b.gameObject.activeInHierarchy || !b.interactable) return false;
			b.onClick.Invoke();
			return true;
		}
	}
}
