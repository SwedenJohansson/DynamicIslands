using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace DynamicIslands.Editor
{
	/// <summary>
	/// The extra option "upgrades" (WorldOptions.Upgrades, the user 2026-10-09): better versions of Raft's own things to
	/// craft. So far the large battery - a blue copy of Raft's battery with twice its charge, at twice its cost.
	///
	/// How: the item is a copy of Raft's Battery (Item_Base) with its own fixed index (LargeBatteryIndex - saves and
	/// messages name items by index, so it must never change), registered with RAPI.RegisterItem when the mod starts, before
	/// a world builds its crafting menu (CraftingMenu.InitializeRecipes reads ItemManager.GetAllItems once). Its charge is
	/// the item's MaxUses (Battery drains one use per 10 s, the charger adds uses until full), so twice Raft's MaxUses holds
	/// twice as long. Machines take a battery only when their slot lists it (ItemObjectEnabler.itemConnections, by index):
	/// UpgradeBatterySlotPatch adds the large one to every Battery slot as it wakes, with a blue copy of the battery model.
	/// The recipe has no entry of its own in the research table; it is learned while the world has the option on and the
	/// battery is learned (Tick), so it appears in the crafting menu next to the battery. Every player needs the mod (the
	/// mod is needed to join anyway); without it a placed large battery is gone from its slot.
	/// </summary>
	public static class ExtraUpgrades
	{
		/// <summary>The large battery's item index: fixed for ever (worlds save it). Below RAPI's limit of 32767, far from
		/// Raft's own indexes.</summary>
		public const int LargeBatteryIndex = 29411;
		public const string LargeBatteryName = "DI_LargeBattery";
		/// <summary>How many times Raft's battery the large one holds and costs.</summary>
		public const int LargeBatteryFactor = 2;
		public static readonly Color LargeBatteryTint = new Color(0.45f, 0.65f, 1f);

		public static Item_Base LargeBattery { get; private set; }
		static Item_Base battery;

		static void Log(string msg) { Debug.Log("[CUSTOM ISLANDS] [upgrades] " + msg); }

		/// <summary>Makes and registers the large battery (the mod's Start). Safe to call again.</summary>
		public static void Register()
		{
			if (LargeBattery != null) return;
			battery = ItemManager.GetItemByName("Battery");
			if (battery == null) { Debug.LogWarning("[CUSTOM ISLANDS] [upgrades] Raft has no item 'Battery': no large battery"); return; }
			Item_Base existing = ItemManager.GetItemByIndex(LargeBatteryIndex);
			if (existing != null) { Debug.LogWarning("[CUSTOM ISLANDS] [upgrades] Item index " + LargeBatteryIndex + " is taken by '" + existing.UniqueName + "' (another mod): no large battery"); return; }

			Item_Base item = ScriptableObject.Instantiate(battery);
			item.name = LargeBatteryName;
			item.Initialize(LargeBatteryIndex, LargeBatteryName, battery.MaxUses * LargeBatteryFactor);

			ItemInstance_Inventory inv = battery.settings_Inventory.Clone();
			inv.LocalizationTerm = "";
			inv.DisplayName = "Large battery";
			inv.Description = "Holds twice the charge of a battery: machines run twice as long on it. Charged in the battery charger like a battery (twice as long, twice the fuel).";
			inv.Sprite = TintedSprite(battery.settings_Inventory.Sprite, LargeBatteryTint) ?? battery.settings_Inventory.Sprite;
			item.settings_Inventory = inv;

			ItemInstance_Recipe recipe = battery.settings_recipe.Clone();
			recipe.NewCost = battery.settings_recipe.NewCost.Select(c => new CostMultiple(c.items, c.amount * LargeBatteryFactor)).ToArray();
			Traverse t = Traverse.Create(recipe);
			t.Field("_hiddenInResearchTable").SetValue(true);
			t.Field("learnedFromBeginning").SetValue(false);
			t.Field("blueprintItem").SetValue(null);
			t.Field("extraBlueprintItems").SetValue(null);
			recipe.Learned = false;
			item.settings_recipe = recipe;

			RAPI.RegisterItem(item);
			LargeBattery = ItemManager.GetItemByIndex(LargeBatteryIndex);
			if (LargeBattery == null) { Debug.LogWarning("[CUSTOM ISLANDS] [upgrades] The large battery couldn't be registered"); return; }
			Log("Large battery registered (index " + LargeBatteryIndex + ", charge " + LargeBattery.MaxUses + " = " + LargeBatteryFactor + " x " + battery.MaxUses
				+ ", cost " + CostText(recipe.NewCost) + ")");
		}

		/// <summary>The mod's unload: the item off again.</summary>
		public static void Unregister()
		{
			if (LargeBattery == null) return;
			RAPI.UnregisterItem(LargeBattery);
			LargeBattery = null;
		}

		public static string CostText(CostMultiple[] cost)
		{
			return cost == null ? "-" : string.Join(", ", cost.Select(c => c.amount + " " + string.Join("/", (c.items ?? new Item_Base[0]).Where(i => i != null).Select(i => i.UniqueName).ToArray())).ToArray());
		}

		/// <summary>Whether the large battery can be crafted in this world now: the option on and the battery learned.</summary>
		public static bool LargeBatteryCraftable
		{
			get { return LargeBattery != null && battery != null && WorldOptions.On(WorldOptions.Upgrades) && battery.settings_recipe.Learned; }
		}

		/// <summary>Every frame in a world: the large battery's recipe learned exactly while LargeBatteryCraftable (a world
		/// whose option is switched off hides it again; Raft's research table never shows it).</summary>
		public static void Tick()
		{
			if (LargeBattery == null || !LoadSceneManager.IsGameSceneLoaded) return;
			bool on = LargeBatteryCraftable;
			if (LargeBattery.settings_recipe.Learned != on) { LargeBattery.settings_recipe.Learned = on; Log("Large battery " + (on ? "craftable" : "hidden")); }
		}

		/// <summary>Lets a machine's battery slot take the large battery: a connection for it, with a blue copy of the
		/// battery's model beside Raft's one. Returns false when the slot has no battery model to copy.</summary>
		public static bool AddToSlot(Battery slot)
		{
			if (LargeBattery == null || slot == null || slot.itemEnabler == null) return false;
			ItemObjectEnabler en = slot.itemEnabler;
			ItemModelConnection[] cons = en.GetObjectConnections() ?? new ItemModelConnection[0];
			if (cons.Any(c => c != null && c.item != null && c.item.UniqueIndex == LargeBatteryIndex)) return true;
			ItemModelConnection basic = cons.FirstOrDefault(c => c != null && c.item != null && c.model != null && c.item.UniqueName == "Battery");
			if (basic == null) return false;
			GameObject model = UnityEngine.Object.Instantiate(basic.model, basic.model.transform.parent);
			model.name = basic.model.name + "_Large";
			model.SetActive(false);
			ObjectProps.ApplyTint(model, LargeBatteryTint, 1f);
			var con = new ItemModelConnection { item = LargeBattery, model = model };
			Traverse.Create(en).Field("itemConnections").SetValue(cons.Concat(new[] { con }).ToArray());
			return true;
		}

		/// <summary>A copy of a (possibly atlased, unreadable) sprite multiplied by tint.</summary>
		static Sprite TintedSprite(Sprite s, Color tint)
		{
			if (s == null) return null;
			try
			{
				Texture2D src = s.texture;
				Rect r = s.textureRect;
				var mat = new Material(Shader.Find("Sprites/Default")) { color = tint };
				var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
				Graphics.Blit(src, rt, mat);
				RenderTexture previous = RenderTexture.active;
				RenderTexture.active = rt;
				var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false) { name = "LargeBatteryIcon" };
				tex.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
				tex.Apply(false, true);
				RenderTexture.active = previous;
				RenderTexture.ReleaseTemporary(rt);
				UnityEngine.Object.Destroy(mat);
				return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), s.pixelsPerUnit);
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [upgrades] Tinting the battery icon failed: " + e.Message); return null; }
		}
	}

	/// <summary>Every machine's battery slot (grill, smelter, purifier, juicer, recycler, Receiver, sprinkler, charger) takes
	/// the large battery too. Awake runs before a loaded world puts its batteries back (RGD_Battery), so a saved large
	/// battery finds its slot ready.</summary>
	[HarmonyPatch(typeof(Battery), "Awake")]
	static class UpgradeBatterySlotPatch
	{
		static void Postfix(Battery __instance)
		{
			try { ExtraUpgrades.AddToSlot(__instance); }
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] [upgrades] A battery slot: " + e.Message); }
		}
	}
}
