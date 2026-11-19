using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DynamicIslands.Editor;
using RaftModLoader;
using UnityEngine;

namespace DynamicIslands
{
	/// <summary>
	/// The recipe cache (speedups 11-19): CIRecipe loads the island the last build saved instead of building it again while
	/// the recipe file, every file it includes, the mod build and Raft's version are what that build had, and the saved files
	/// are as it left them. 347 builds of 85 recipes took 1114 min in a day, 874 of them building the same recipes again
	/// (coral_observatory 16 times). The key is &lt;island&gt;.recipekey next to the saved island (a plan's in plans\).
	/// Only the CIRecipe command uses it: the tests that call RecipeRoutine themselves (CICaveGaps*, CIRecipeDig,
	/// CIPreFaults, CICB1Recipe) check what a build does and always build.
	/// </summary>
	public static partial class DevTests
	{
		public const string RecipeKeyExtension = ".recipekey";
		/// <summary>CIRecipeCache off, or CI_NOCACHE=1 in Raft's environment: every CIRecipe builds.</summary>
		static bool recipeCacheOff = NoCacheEnv();
		/// <summary>The last CIRecipe loaded its island from the cache (no build: RecipeObjects only has the aliases the key kept).</summary>
		public static bool RecipeFromCache;

		/// <summary>The recipe files the last ExpandRecipe read (the recipe, then its includes, in the order read).</summary>
		static readonly List<string> recipeFilesRead = new List<string>();
		/// <summary>What the last RecipeRoutine saved (island and plan files), whether it got to its PASS, and its land-centre line.</summary>
		static readonly List<string> recipeOutputs = new List<string>();
		static bool recipePassed;
		static string recipeLandLine;

		static bool NoCacheEnv()
		{
			try { string v = Environment.GetEnvironmentVariable("CI_NOCACHE"); return !string.IsNullOrEmpty(v) && v != "0"; }
			catch (Exception) { return false; }
		}

		[ConsoleCommand(name: "CIRecipeCache", docs: "Dev: CIRecipe's build cache - off: every CIRecipe builds (as CI_NOCACHE=1 in Raft's environment does), on: CIRecipe loads the saved island while the recipe, its includes and the mod build are unchanged. CIRecipeCache [on|off]")]
		public static void RecipeCacheCommand(string[] args)
		{
			string a = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "";
			if (a == "on") recipeCacheOff = false;
			else if (a == "off") recipeCacheOff = true;
			else if (a.Length > 0) { Fail("CIRecipeCache [on|off]"); return; }
			Log("PASS: recipe cache " + (recipeCacheOff ? "off" : "on"));
		}

		static string Md5Hex(byte[] bytes)
		{
			using (var md5 = System.Security.Cryptography.MD5.Create())
				return BitConverter.ToString(md5.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
		}

		static string FileMd5(string path) { return File.Exists(path) ? Md5Hex(File.ReadAllBytes(path)) : null; }

		static string RecipeKeyPath(string output) { return Path.ChangeExtension(output, RecipeKeyExtension); }

		/// <summary>The file the recipe saves last (its last island save, or its plan), from its expanded lines; null when it saves none.</summary>
		static string RecipeLastOutput(List<RLine> lines)
		{
			string last = null, plan = null;
			foreach (RLine rl in lines)
			{
				string line = rl.Text.Trim();
				string[] t = Tokens(line);
				if (t.Length == 0) continue;
				string verb = t[0].ToLowerInvariant();
				if (verb == "save" && t.Length > 1) last = IslandSpawner.PathFor(Rest(line, 1).Trim());
				else if (verb == "plan" && t.Length > 1 && t[1].ToLowerInvariant() == "new") plan = Rest(line, 2).Trim();
				else if (verb == "plan" && t.Length > 1 && t[1].ToLowerInvariant() == "save" && plan != null) last = WorldPlan.PathFor(plan);
			}
			return last;
		}

		/// <summary>The build's inputs as key lines: the recipe, the mod build, Raft's version, each recipe file read and its hash.</summary>
		static List<string> RecipeInputs(string name)
		{
			var key = new List<string> { "recipe=" + name, "mod=" + IslandNetwork.Build, "raft=" + Application.version };
			foreach (string f in recipeFilesRead.Distinct(StringComparer.OrdinalIgnoreCase))
				key.Add("file " + Path.GetFileName(f) + "=" + FileMd5(f));
			return key;
		}

		/// <summary>Null when the saved key matches (reason: what matched), else why not.</summary>
		static string RecipeKeyMismatch(List<string> inputs, string keyPath, Dictionary<string, string> outputs, out string hitReason)
		{
			hitReason = null;
			if (!File.Exists(keyPath)) return "no key saved";
			string[] saved = File.ReadAllLines(keyPath).Where(l => l.Length > 0 && !l.StartsWith("#")).ToArray();
			var savedIn = saved.Where(l => l.StartsWith("recipe=") || l.StartsWith("mod=") || l.StartsWith("raft=") || l.StartsWith("file ")).ToList();
			string mine = inputs.First(), theirs = savedIn.FirstOrDefault(l => l.StartsWith("recipe="));
			if (theirs != mine) return "made by " + (theirs ?? "no recipe");
			foreach (string pre in new[] { "mod=", "raft=" })
			{
				string a = inputs.First(l => l.StartsWith(pre)), b = savedIn.FirstOrDefault(l => l.StartsWith(pre));
				if (a != b) return (pre == "mod=" ? "mod build " : "Raft ") + (b != null ? b.Substring(pre.Length) : "?") + " -> " + a.Substring(pre.Length);
			}
			var fa = inputs.Where(l => l.StartsWith("file ")).ToList();
			var fb = savedIn.Where(l => l.StartsWith("file ")).ToList();
			string changed = fa.FirstOrDefault(l => !fb.Contains(l));
			if (changed != null) return changed.Substring(5, changed.LastIndexOf('=') - 5) + " changed";
			if (fb.Count != fa.Count) return "includes changed";
			foreach (string l in saved.Where(l => l.StartsWith("out ")))
			{
				int eq = l.LastIndexOf('=');
				string rel = l.Substring(4, eq - 4), path = Path.Combine(DynamicIslands.assetpath, rel), hash = FileMd5(path);
				if (hash == null) return rel + " missing";
				if (hash != l.Substring(eq + 1)) return rel + " changed since the build";
				outputs[path] = hash;
			}
			if (outputs.Count == 0) return "the key names no saved file";
			hitReason = "recipe" + (fa.Count > 1 ? " + " + (fa.Count - 1) + " include(s)" : "") + ", mod build " + IslandNetwork.Build + " unchanged";
			return null;
		}

		static string RelToAssets(string path)
		{
			string root = Path.GetFullPath(DynamicIslands.assetpath).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
			string full = Path.GetFullPath(path);
			return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full.Substring(root.Length) : full;
		}

		/// <summary>CIRecipe: the cache looked at, then the saved island loaded (a hit) or the recipe built and its key written (a miss).</summary>
		static IEnumerator CachedRecipeRoutine(string name, bool fresh)
		{
			RecipeFromCache = false;
			string path = Path.Combine(RecipeFolder, name.EndsWith(".recipe") ? name : name + ".recipe");
			string why = null, hitReason = null, keyPath = null;
			List<string> inputs = null;
			var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (fresh) why = "fresh";
			else if (recipeCacheOff) why = "cache off";
			else if (!File.Exists(path)) why = "no recipe file";
			if (why == null)
			{
				try
				{
					List<RLine> lines = ExpandRecipe(path);
					string last = RecipeLastOutput(lines);
					inputs = RecipeInputs(name);
					if (last == null) why = "it saves nothing";
					else if (IslandNetwork.Build == "?") why = "mod build unknown";
					else { keyPath = RecipeKeyPath(last); why = RecipeKeyMismatch(inputs, keyPath, outputs, out hitReason); }
				}
				catch (Exception e) { why = "key: " + e.Message; }
			}
			if (why != null)
			{
				Log("recipe cache miss " + name + " (" + why + ")");
				// (the old key goes first: a build that fails half way leaves no key to a half-made island)
				if (keyPath != null) try { File.Delete(keyPath); } catch (Exception) { }
				// (the inputs read before the build: its save step expands the island's plays, which ExpandRecipe records instead)
				if (inputs == null && File.Exists(path)) try { ExpandRecipe(path); inputs = RecipeInputs(name); } catch (Exception) { }
				yield return RecipeRoutine(name);
				if (recipePassed && recipeOutputs.Count > 0 && inputs != null) WriteRecipeKey(inputs);
				yield break;
			}
			yield return LoadCachedRecipe(name, keyPath, hitReason, outputs);
		}

		/// <summary>After a build that passed: its key, next to the file it saved last (no key when the mod build is unknown).</summary>
		static void WriteRecipeKey(List<string> key)
		{
			if (key.Any(l => l == "mod=?")) return;
			try
			{
				var lines = new List<string> { "# CIRecipe cache key (speedups 11-19): the build is skipped while every line still holds" };
				lines.AddRange(key);
				foreach (string o in recipeOutputs.Distinct(StringComparer.OrdinalIgnoreCase)) lines.Add("out " + RelToAssets(o) + "=" + FileMd5(o));
				// (the aliases' objects by their saved numbers: TutObject and the like find them after a hit too)
				foreach (var kv in RecipeObjects) if (kv.Value != null && kv.Value.Uid != 0) lines.Add("alias " + kv.Key + "=" + kv.Value.Uid);
				if (recipeLandLine != null) lines.Add("land=" + recipeLandLine);
				File.WriteAllLines(RecipeKeyPath(recipeOutputs.Last()), lines.ToArray());
			}
			catch (Exception e) { Debug.LogWarning("[CITEST] recipe cache: the key wasn't written: " + e.Message); }
		}

		/// <summary>A hit: the editor opened on the saved island, as the build leaves it.</summary>
		static IEnumerator LoadCachedRecipe(string name, string keyPath, string hitReason, Dictionary<string, string> outputs)
		{
			// A hit: the editor opened on the saved island, as the build leaves it
			Log("recipe cache hit " + name + " (" + hitReason + ")");
			float started = Time.realtimeSinceStartup;
			string[] keyLines = File.ReadAllLines(keyPath);
			RecipeSaved = null;
			RecipeObjects.Clear();
			recipeGroups.Clear();
			yield return WaitForEditor(false);
			if (!DynamicIslands.InEditor()) { Fail("recipe " + name + ": the editor didn't open"); yield break; }
			yield return PlaceableCatalog.EnsureBuilt();
			string island = outputs.Keys.Where(o => o.EndsWith(IslandFile.Extension, StringComparison.OrdinalIgnoreCase)).Select(o => Path.GetFileNameWithoutExtension(o)).LastOrDefault();
			if (island != null)
			{
				DynamicIslands.currentIslandName = "";
				bool loaded = false;
				try { loaded = DynamicIslands.LoadIsland(island); } catch (Exception e) { Fail("recipe " + name + ": loading the saved '" + island + "' failed: " + e.Message); yield break; }
				if (!loaded) { Fail("recipe " + name + ": the editor didn't load the saved '" + island + "' (CIRecipe " + name + " fresh builds it)"); yield break; }
				for (float t = 0; t < 120f && DynamicIslands.currentIslandName != island; t += 0.25f) yield return new WaitForSecondsRealtime(0.25f);
				if (DynamicIslands.currentIslandName != island) { Fail("recipe " + name + ": the saved '" + island + "' still not loaded after 2 minutes"); yield break; }
				yield return null; yield return null;
				var byUid = new Dictionary<int, EditorGameObject>();
				foreach (EditorGameObject e in PlacedEditorObjects()) if (e.Uid != 0 && !byUid.ContainsKey(e.Uid)) byUid[e.Uid] = e;
				foreach (string l in keyLines.Where(l => l.StartsWith("alias ")))
				{
					int eq = l.LastIndexOf('='), uid;
					EditorGameObject e;
					if (eq > 6 && int.TryParse(l.Substring(eq + 1), out uid) && byUid.TryGetValue(uid, out e)) RecipeObjects[l.Substring(6, eq - 6)] = e;
				}
			}
			string lastOut = outputs.Keys.Last();
			RecipeSaved = Path.GetFileNameWithoutExtension(lastOut);
			RecipeFromCache = true;
			string land = keyLines.FirstOrDefault(l => l.StartsWith("land="));
			if (land != null) Log(land.Substring(5));
			Log("PASS: recipe " + name + " (cache hit, loaded in " + (Time.realtimeSinceStartup - started).ToString("F0") + " s, saved '" + RecipeSaved + "')");
		}
	}
}
