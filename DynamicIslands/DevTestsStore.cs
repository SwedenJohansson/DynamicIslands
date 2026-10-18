using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DynamicIslands
{
	public static partial class DevTests
	{
		/// <summary>SL7: what Raft's trading posts are made of (which scenes hold one, the objects around the TradingPost
		/// script, its goods and prices) - to build the store the mod places on custom islands.</summary>
		[ConsoleCommand(name: "CIDumpTradingPost", docs: "Dev, main menu or editor: loads Raft's island scenes (switched off) whose name has the word (default Big) and logs every TradingPost: its path, the objects around it (meshes, bounds, scripts), and the goods Raft's trading posts sell and buy (SO_TradingPost)")]
		public static void DumpTradingPostCommand(string[] args) { StartTest(DumpTradingPostRoutine(args != null && args.Length > 0 ? args[0] : "Big")); }

		static IEnumerator DumpTradingPostRoutine(string word)
		{
			var buyables = Resources.LoadAll<SO_TradingPost_Buyable>("SO_TradingPost").OrderBy(b => b.sortingOrder).ToList();
			foreach (SO_TradingPost_Buyable b in buyables)
				Log("  buy " + b.name + ": tier " + b.tier + ", stock " + b.startStock + ", gives " + CostText(b.reward) + " for " + string.Join(" + ", (b.cost ?? new Cost[0]).Select(CostText).ToArray()));
			foreach (SO_TradingPost_Sellable s in Resources.LoadAll<SO_TradingPost_Sellable>("SO_TradingPost").OrderBy(b => b.sortingOrder))
				Log("  sell " + (s.sellableItem != null ? s.sellableItem.UniqueName : "?") + ": gives " + CostText(s.reward) + ", reputation " + s.reputationReward);
			var scenes = PlaceableCatalog.LandmarkSceneNames().Where(s => s.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
			Log("Scenes: " + string.Join(", ", scenes.ToArray()));
			int found = 0;
			foreach (string scene in scenes)
				yield return PlaceableCatalog.VisitScene(scene, s => DumpPosts(s, scene, n => found += n));
			if (found > 0) Log("PASS: " + found + " trading posts"); else Fail("no trading posts in " + word + " scenes");
		}

		static string CostText(Cost c) { return c == null || c.item == null ? "-" : c.item.UniqueName + "*" + c.amount; }

		static IEnumerator DumpPosts(Scene scene, string name, Action<int> count)
		{
			int n = 0;
			foreach (GameObject root in scene.GetRootGameObjects())
				foreach (TradingPost tp in root.GetComponentsInChildren<TradingPost>(true))
				{
					n++;
					string path = tp.name; for (Transform p = tp.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
					Log(name + ": TradingPost " + path + " at " + tp.transform.position + " rot " + tp.transform.eulerAngles + " scale " + tp.transform.lossyScale);
					Log("  scripts on it: " + string.Join(", ", tp.GetComponents<Component>().Select(c => c.GetType().Name).ToArray()) + "; landmark item " + (tp.GetComponentInParent<LandmarkItem>() != null ? tp.GetComponentInParent<LandmarkItem>().name : "none"));
					Transform top = tp.transform;
					for (int up = 0; up < 2 && top.parent != null; up++) top = top.parent;
					DumpTree(tp.transform, 0, 3);
					Log("  around (2 up: " + top.name + "):");
					DumpTree(top, 0, 2);
				}
			count(n);
			yield break;
		}

		static void DumpTree(Transform t, int depth, int max)
		{
			Renderer r = t.GetComponent<Renderer>();
			var all = t.GetComponentsInChildren<Renderer>(true);
			Bounds b = all.Length > 0 ? all[0].bounds : new Bounds(t.position, Vector3.zero);
			foreach (Renderer x in all) b.Encapsulate(x.bounds);
			Log("  " + new string(' ', depth * 2) + t.name + (t.gameObject.activeSelf ? "" : " (off)") + " local " + t.localPosition.ToString("F2") + " size " + b.size.ToString("F2") + " renderers " + all.Length +
				" [" + string.Join(",", t.GetComponents<Component>().Where(c => !(c is Transform)).Select(c => c.GetType().Name).ToArray()) + "]");
			if (depth >= max) return;
			foreach (Transform c in t) DumpTree(c, depth + 1, max);
		}
	}
}
