using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The mod's own keys at the hotbar, like Raft's: Raft shows its inventory and notebook keys as small tabs at the ends
	/// of the hotbar (_CanvasGame_New/.../Hotbar/KeyboardLayout: an icon on a brown tab with the key under it). The
	/// journal (J) and, while the level up system is on, the stats page (K) get the same tabs beside the notebook's -
	/// copies of Raft's tab, so they look and hide exactly as Raft's do (with the keyboard layout).
	/// </summary>
	public static class HotkeyHints
	{
		public const string JournalName = "CustomIslands_Hotkey_Journal", StatsName = "CustomIslands_Hotkey_Stats";
		const string LayoutPath = "_CanvasGame_New/InventoryParent/Hotbar/KeyboardLayout";
		static RectTransform journal, stats;
		static float nextLook;
		static Sprite bookIcon;

		/// <summary>Every frame in a world (cheap: looks twice a second).</summary>
		public static void Tick()
		{
			if (Time.unscaledTime < nextLook) return;
			nextLook = Time.unscaledTime + 0.5f;
			try
			{
				if (journal == null || stats == null) Build();
				if (stats != null && stats.gameObject.activeSelf != PlayerLevels.On) stats.gameObject.SetActive(PlayerLevels.On);
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] The hotbar's key tabs: " + e.Message); nextLook = Time.unscaledTime + 30f; }
		}

		static void Build()
		{
			GameObject layout = GameObject.Find(LayoutPath);
			if (layout == null) return;
			Transform note = layout.transform.Find("UI_Hotkey_Element_NoteBook"), inv = layout.transform.Find("UI_Hotkey_Element_Inventory");
			if (note == null) return;
			RectTransform noteRect = (RectTransform)note;
			// (one tab width and a gap further along, away from the inventory's tab: the notebook's is at the hotbar's right end)
			float dir = inv != null && ((RectTransform)inv).anchoredPosition.x > noteRect.anchoredPosition.x ? -1f : 1f;
			float step = (noteRect.rect.width + 6f) * dir;
			if (journal == null) journal = Make(layout.transform, noteRect, JournalName, BookIcon(), JournalWindow.Key.ToString(), step);
			if (stats == null) stats = Make(layout.transform, noteRect, StatsName, StarTabIcon(), PlayerLevels.Key.ToString(), step * 2f);
			Debug.Log("[CUSTOM ISLANDS] Hotbar key tabs: journal (" + JournalWindow.Key + ") and stats (" + PlayerLevels.Key + ") beside Raft's notebook tab");
		}

		static RectTransform Make(Transform parent, RectTransform from, string name, Sprite icon, string key, float dx)
		{
			Transform old = parent.Find(name);
			if (old != null) return (RectTransform)old;
			var copy = (RectTransform)UnityEngine.Object.Instantiate(from.gameObject, parent, false).transform;
			copy.name = name;
			copy.anchoredPosition = from.anchoredPosition + new Vector2(dx, 0f);
			// (only the look: nothing of Raft's that could set the copy's text or react to it)
			foreach (MonoBehaviour b in copy.GetComponentsInChildren<MonoBehaviour>(true))
				if (!(b is Graphic) && !(b is LayoutElement) && !(b is Shadow) && !b.GetType().Name.StartsWith("TextMeshPro")) UnityEngine.Object.Destroy(b);
			Image img = copy.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.transform != copy);
			if (img != null && icon != null) { img.sprite = icon; img.preserveAspect = true; }
			Component tmp = copy.GetComponentsInChildren<Component>(true).FirstOrDefault(c => c != null && c.GetType().Name == "TextMeshProUGUI");
			if (tmp != null) Traverse.Create(tmp).Property("text").SetValue(key);
			else { Text t = copy.GetComponentInChildren<Text>(true); if (t != null) t.text = key; }
			return copy;
		}

		static Sprite starTab;

		/// <summary>The level bar's star as a light silhouette, so the tab tints it as Raft tints its own tab icons (the bar's
		/// dark star came out darker than the others).</summary>
		public static Sprite StarTabIcon()
		{
			if (starTab != null) return starTab;
			// (drawn here: the level bar's star texture can't be read back to make a light copy)
			const int W = 72, H = 68;
			var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "CustomIslands_StarTabIcon" };
			Vector2 c = new Vector2(W / 2f, H / 2f - 2f);
			float R = 32f, r = R * 0.48f;
			var pts = new Vector2[10];
			for (int i = 0; i < 10; i++) { float ang = Mathf.PI / 2f + i * Mathf.PI / 5f; float rad = i % 2 == 0 ? R : r; pts[i] = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad; }
			var px = new Color32[W * H];
			for (int y = 0; y < H; y++)
				for (int x = 0; x < W; x++)
				{
					// (4 samples a pixel for smooth edges)
					int inside = 0;
					for (int sy = 0; sy < 2; sy++) for (int sx = 0; sx < 2; sx++) if (Inside(pts, new Vector2(x + 0.25f + sx * 0.5f, y + 0.25f + sy * 0.5f))) inside++;
					px[y * W + x] = new Color32(255, 255, 255, (byte)(inside * 255 / 4));
				}
			tex.SetPixels32(px);
			tex.Apply();
			starTab = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
			return starTab;
		}

		static bool Inside(Vector2[] poly, Vector2 p)
		{
			bool inside = false;
			for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
				if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
			return inside;
		}

		/// <summary>An open book, drawn as Raft's tab icons are: one light silhouette the tab tints (80 x 64).</summary>
		public static Sprite BookIcon()
		{
			if (bookIcon != null) return bookIcon;
			const int W = 80, H = 64;
			var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "CustomIslands_BookIcon" };
			var px = new Color32[W * H];
			for (int y = 0; y < H; y++)
				for (int x = 0; x < W; x++)
				{
					float fx = x + 0.5f, fy = y + 0.5f;
					float cx = W / 2f;
					float d = Mathf.Abs(fx - cx);             // from the spine
					float top = 52f + 6f * (d / cx) - 5f * Mathf.Pow(d / cx, 2f) * 2f; // pages curve up from the spine, down at the edges
					float bottom = 12f + 5f * (d / cx);
					bool page = d < cx - 3f && fy > bottom && fy < top;
					bool spineGap = d < 1.4f && fy > bottom + 3f;  // the fold between the pages
					bool lines = page && d > 8f && d < cx - 9f && ((int)(fy - bottom) % 8 == 4) && fy < top - 6f; // lines of writing
					bool cover = d < cx - 1f && fy > bottom - 5f && fy <= bottom + 1f; // the cover under the pages
					float a = (page && !spineGap && !lines) || cover ? 1f : 0f;
					px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255));
				}
			tex.SetPixels32(px);
			tex.Apply();
			bookIcon = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
			bookIcon.name = "CustomIslands_BookIcon";
			return bookIcon;
		}
	}
}
