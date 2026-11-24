using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DynamicIslands.Editor
{
	/// <summary>The inspector's rows for the 11-04 gap features (GapWorld): a trade stand with the island maker's own offer
	/// (GW1) and a trigger zone's co-op count (GW8).</summary>
	public static partial class ObjectInspector
	{
		#region Trade stand (GW1)

		/// <summary>An object with trade.give and trade.get: what players pay, what they get, how many times.</summary>
		static void TradeGroup(EditorGameObject target)
		{
			Dictionary<string, string> p = target.Props;
			RectTransform g = UIKit.Group(root, "Trade stand");
			UIKit.Label(g, "Players look at it and trade with the interact key (E): they pay the price and get the goods.", 12, UIKit.TextMuted);
			UIKit.Label(g, "Players pay (Item*count; more with ;)", 11, UIKit.TextMuted);
			InputField give = UIKit.Field(g, "Plank*12;Rope*2", ObjectProps.Get(p, TradeStand.GiveProp), UIKit.RowHeight,
				"Raft's unique item names with a count, e.g. Plank*12;Rope*2. A story item: story:<id>*1 (the crew's - it is used up)");
			give.onEndEdit.AddListener(v => { Set(target, TradeStand.GiveProp, Clean(v)); Refresh(); });
			amountFields.Add(give);
			UIKit.Label(g, "They get (Item*count; story:<id>*1 for a story item)", 11, UIKit.TextMuted);
			InputField get = UIKit.Field(g, "Seed_Mango*1", ObjectProps.Get(p, TradeStand.GetProp), UIKit.RowHeight,
				"What the player gets for it, e.g. Seed_Mango*1 or story:old-map*1 (a quest's story item)");
			get.onEndEdit.AddListener(v => { Set(target, TradeStand.GetProp, Clean(v)); Refresh(); });
			amountFields.Add(get);
			int stock = TradeStand.StockIn(p);
			UIKit.Slider(g, "Stock", 0f, TradeStand.MaxStock, stock, v => v < 0.5f ? "no limit" : v.ToString("0", CultureInfo.InvariantCulture) + " trades",
				v => Set(target, TradeStand.StockProp, Mathf.RoundToInt(v).ToString(CultureInfo.InvariantCulture), null, false),
				"How many times the stand trades in a world, for the whole crew (each trade goes to one player). No limit: it never sells out", true);
			string bad = string.Join(", ", Unknown(p, TradeStand.GiveProp).Concat(Unknown(p, TradeStand.GetProp)).Distinct().ToArray());
			Text t = UIKit.Label(g, "In a world: \"Trade " + GapWorld.TradeText(p) + "\"" + (bad.Length > 0 ? "\n<color=#e06040>Unknown item(s): " + bad + " - left out in a world</color>" : ""), 12, UIKit.TextColor);
			t.lineSpacing = 1.05f;
			UIKit.Label(g, "Each trade runs its \"trade\" event (Behaviour & events): on.trade=signal||paid for a quest.", 11, UIKit.TextMuted);
			UIKit.Button(g, "Not a trade stand", () =>
			{
				Dictionary<string, string> c = ObjectProps.With(ObjectProps.With(ObjectProps.With(target.Props, TradeStand.GiveProp, null), TradeStand.GetProp, null), TradeStand.StockProp, null);
				PropsCommand.Change(target, c);
				Refresh();
			}, "Remove the offer (the object stays)");
		}

		/// <summary>A new trade stand: an example offer (10 planks for a rope), 3 in stock.</summary>
		static void MakeTradeStand(EditorGameObject target)
		{
			Dictionary<string, string> c = ObjectProps.With(ObjectProps.With(ObjectProps.With(target.Props, TradeStand.GiveProp, "Plank*10"), TradeStand.GetProp, "Rope*1"), TradeStand.StockProp, "3");
			PropsCommand.Change(target, c);
			Refresh();
		}

		/// <summary>"Plank * 12 ; Rope*2;" -> "Plank*12;Rope*2" (a count of 1 when none).</summary>
		internal static string Clean(string v)
		{
			var parts = new List<string>();
			foreach (string raw in (v ?? "").Split(';'))
			{
				string[] x = raw.Split('*');
				string name = x[0].Trim();
				if (name.Length == 0) continue;
				int n;
				if (x.Length < 2 || !int.TryParse(x[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 1) n = 1;
				parts.Add(name + "*" + n.ToString(CultureInfo.InvariantCulture));
			}
			return string.Join(";", parts.ToArray());
		}

		/// <summary>The item names in one of the offer's settings that neither Raft nor the island's story items know.</summary>
		static IEnumerable<string> Unknown(IDictionary<string, string> p, string key)
		{
			foreach (KeyValuePair<string, int> l in ObjectProps.Loot(p, key))
			{
				bool known;
				if (StoryItems.IsStory(l.Key)) known = StoryItems.Find(l.Key) != null;
				else { try { known = ItemManager.GetItemByName(l.Key) != null; } catch { known = true; } }
				if (!known) yield return l.Key;
			}
		}

		#endregion

		#region Co-op zone (GW8)

		/// <summary>A trigger zone's player count: off, or 1-8 players (scaled down to the players online), once or while they stand.</summary>
		static void CoopZoneGroup(EditorGameObject target)
		{
			Dictionary<string, string> p = target.Props;
			int n; bool hold;
			if (!GapWorld.CoopOf(p, out n, out hold)) n = 0;
			RectTransform g = UIKit.Group(root, "Co-op zone (players standing in it)");
			UIKit.Slider(g, "Players", 0f, GapWorld.MaxPlayers, n, v => v < 0.5f ? "off" : v.ToString("0", CultureInfo.InvariantCulture),
				v => { int k = Mathf.RoundToInt(v); Set(target, GapWorld.PlayersProp, GapWorld.Join(k, hold), null, false); },
				"How many players must stand in the zone together; then its \"crowd\" event runs (the host counts)", true);
			if (n > 0)
			{
				RectTransform row = UIKit.Row(g, 26f, 3f, "CoopMode");
				Button once = UIKit.Button(row, "Once", () => { Set(target, GapWorld.PlayersProp, GapWorld.Join(n, false)); Refresh(); },
					"\"crowd\" runs once ever, the first time enough players stand in it (a gate that stays open)", -1, 26f, 12);
				Button stand = UIKit.Button(row, "While they stand", () => { Set(target, GapWorld.PlayersProp, GapWorld.Join(n, true)); Refresh(); },
					"\"crowd\" runs each time it fills, \"crowdleft\" when someone steps off (a door held open by players on plates)", -1, 26f, 12);
				UIKit.SetActive(once, !hold);
				UIKit.SetActive(stand, hold);
			}
			UIKit.Label(g, "How it counts: the number needed is the number set, but never more than the players online - one player alone is always enough; " +
				"with 2 online a 2 or 3 zone needs both. Living players only. The check \"players in zone\" asks the same on any object.", 11, UIKit.TextMuted);
		}

		#endregion
	}
}
