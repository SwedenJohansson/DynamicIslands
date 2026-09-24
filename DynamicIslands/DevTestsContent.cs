using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DynamicIslands.Editor;
using HMLLibrary;
using RaftModLoader;
using UnityEngine;
using UnityEngine.AI;

namespace DynamicIslands
{
	/// <summary>Tests for the custom content: object settings (format 4), the inspector, notes and creatures.</summary>
	public static partial class DevTests
	{
		const string PropsIsland = "ciprops", CreatureIsland = "cicreature";

		/// <summary>Long test runs starve the test player (and warthogs bite): fill them up, and respawn them if they're down.</summary>
		static IEnumerator EnsureAlive()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			if (player == null) yield break;
			bool down = player.Stats != null && player.Stats.stat_health.Value <= 0f;
			if (down)
			{
				Player p = player.GetComponentInChildren<Player>(true);
				if (p != null) { Log("The test player was down: respawning them (inventory kept)"); p.RespawnWithoutBed(false); }
				yield return new WaitForSeconds(3f);
			}
			KeepAlive(player);
		}

		#region Editor: settings, inspector, file format 4

		[ConsoleCommand(name: "CIPropsTest", docs: "Dev, editor: creatures, notes and tints - placing, the inspector, undo/redo, duplicate, save and load (format 4)")]
		public static void PropsTest()
		{
			DynamicIslands.instance.StartCoroutine(PropsTestRoutine());
		}

		static EditorGameObject PlaceForTest(string name, Vector3 pos, Transform placed)
		{
			GameObject go = PlaceableCatalog.Spawn(name, placed);
			if (go == null) return null;
			go.transform.position = pos;
			foreach (Collider c in go.GetComponentsInChildren<Collider>()) c.enabled = true;
			return EditorGameObject.Attach(go, name);
		}

		static IEnumerator PropsTestRoutine()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			EditorUI.SetTab(TAB.ObjectPlace);
			Transform placed = GameObject.Find("PlacedObjects").transform;
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			yield return null;
			CommandUndoRedo.UndoRedoManager.Clear();

			var cats = PlaceableCatalog.Browse().ToDictionary(c => c.Key, c => c.Value.Count);
			Check(ref ok, ContentCatalog.Categories.All(cats.ContainsKey), "the object list has the categories " + string.Join(", ", ContentCatalog.Categories.Select(c => c + " (" + (cats.ContainsKey(c) ? cats[c] : 0) + ")").ToArray()));

			Vector3 c0 = terraineditor.terrain.transform.position + new Vector3(500f, IslandFile.DefaultWaterLevel + 1f, 500f);
			EditorGameObject boar = PlaceForTest("Creature_Boar", c0, placed);
			EditorGameObject sign = PlaceForTest("Note_Sign", c0 + new Vector3(4, 0, 0), placed);
			string plainName = PlaceableCatalog.CoreNames.FirstOrDefault(n => PlaceableCatalog.CategoryOf(n) == PlaceableCatalog.NatureCategory && ContentCatalog.CreatureOf(n) == null);
			EditorGameObject plain = PlaceForTest(plainName, c0 + new Vector3(-4, 0, 0), placed);
			if (boar == null || sign == null || plain == null) { Fail("could not place a creature, a sign and " + plainName); yield break; }
			Check(ref ok, boar.transform.Find("Marker") != null || boar.transform.Find("Model") != null, "the warthog shows as " + (boar.transform.Find("Model") != null ? "Raft's model" : "a marker"));
			Check(ref ok, ObjectProps.Get(sign.Props, ObjectProps.NoteTitle) == "Sign", "a sign from the list starts as a note titled \"Sign\"");

			// Settings as undo steps
			PropsCommand.Change(boar, ObjectProps.With(boar.Props, ObjectProps.CreatureCount, "3"));
			PropsCommand.Change(boar, ObjectProps.With(ObjectProps.With(boar.Props, ObjectProps.CreatureHealth, "2"), ObjectProps.CreatureDamage, "1.5"));
			PropsCommand.Change(boar, ObjectProps.With(boar.Props, ObjectProps.TintColor, "#FF2020"));
			NoteEditorWindow.Apply(sign, "Welcome", "Line one\nLine two \u00E5\u00E4\u00F6");
			NoteEditorWindow.Apply(plain, "Hidden treasure", "Look under the palm.");
			Check(ref ok, ObjectProps.Count(boar.Props) == 3 && ObjectProps.Health(boar.Props) == 2f && ObjectProps.HasTint(boar.Props), "creature settings: " + string.Join(", ", boar.Props.Select(kv => kv.Key + "=" + kv.Value).ToArray()));
			CommandUndoRedo.UndoRedoManager.Undo(); // the plain object's note
			CommandUndoRedo.UndoRedoManager.Undo(); // the sign's text
			bool undone = !ObjectProps.IsNote(plain.GameObjectName, plain.Props) && ObjectProps.Get(sign.Props, ObjectProps.NoteTitle) == "Sign";
			CommandUndoRedo.UndoRedoManager.Redo();
			CommandUndoRedo.UndoRedoManager.Redo();
			Check(ref ok, undone && ObjectProps.Get(sign.Props, ObjectProps.NoteText).StartsWith("Line one") && ObjectProps.IsNote(plain.GameObjectName, plain.Props), "undo and redo of note text");

			// Tint shows on the marker; labels show the settings
			Renderer body = boar.GetComponentsInChildren<Renderer>().FirstOrDefault(r => r.name != ContentCatalog.MarkerOnly && !(r is ParticleSystemRenderer));
			var block = new MaterialPropertyBlock();
			if (body != null) body.GetPropertyBlock(block);
			Color shown = body != null && body.sharedMaterial != null && body.sharedMaterial.HasProperty("_Color") ? block.GetColor("_Color") : Color.clear;
			Check(ref ok, shown.r > shown.g * 2f, "the tint shows in the editor (" + shown + ")");
			TextMesh label = boar.GetComponentsInChildren<TextMesh>().FirstOrDefault();
			Check(ref ok, label != null && label.text.Contains("\u00D73"), "the marker's label shows the herd: " + (label != null ? label.text.Replace("\n", " / ") : "none"));
			TextMesh noteTag = plain.GetComponentsInChildren<TextMesh>().FirstOrDefault();
			Check(ref ok, noteTag != null && noteTag.text.Contains("Hidden treasure"), "a readable object shows its title in the editor");

			// Inspector
			TransformGizmoSelect(boar.transform);
			yield return null; yield return null;
			Check(ref ok, ObjectInspector.Visible && ObjectInspector.Target == boar, "selecting the warthog opens the creature editor");
			Screenshot(new[] { "props_creature" });
			yield return new WaitForSecondsRealtime(0.5f);
			TransformGizmoSelect(plain.transform);
			yield return null; yield return null;
			Check(ref ok, ObjectInspector.Visible && ObjectInspector.Target == plain, "selecting a readable object shows its note");
			Screenshot(new[] { "props_note" });
			yield return new WaitForSecondsRealtime(0.5f);
			NoteEditorWindow.Open(plain);
			yield return null;
			Screenshot(new[] { "props_noteeditor" });
			yield return new WaitForSecondsRealtime(0.5f);
			NoteEditorWindow.Close();

			// Duplicate keeps the settings
			TransformGizmoSelect(boar.transform);
			int copies = PlacementOptions.DuplicateSelection();
			EditorGameObject copy = DynamicIslands.EditorGizmoHandler.SelectedRoots.Select(t => t.GetComponent<EditorGameObject>()).FirstOrDefault();
			Check(ref ok, copies == 1 && copy != null && copy != boar && ObjectProps.Count(copy.Props) == 3 && ObjectProps.HasTint(copy.Props), "a copy keeps the creature's settings");
			DynamicIslands.EditorGizmoHandler.ClearTargets(false);

			// Island info, save (format 4), wipe, load
			DynamicIslands.currentIslandProps[IslandProps.Title] = "Test Island";
			DynamicIslands.currentIslandProps[IslandProps.Author] = "CI";
			bool saved = DynamicIslands.SaveIsland(PropsIsland);
			int version;
			using (var r = new BinaryReader(File.OpenRead(IslandSpawner.PathFor(PropsIsland)))) { r.ReadUInt32(); version = r.ReadInt32(); }
			Check(ref ok, saved && version == 4, "saved as format " + version);
			var plainFile = new IslandFile { Name = "x", TerrainSize = new Vector3(10, 10, 10), HeightmapResolution = 33, Heights = new float[33, 33] };
			string tmp = Path.Combine(DynamicIslands.assetpath, "ciprops_plain.island");
			plainFile.Save(tmp);
			using (var r = new BinaryReader(File.OpenRead(tmp))) { r.ReadUInt32(); version = r.ReadInt32(); }
			File.Delete(tmp);
			Check(ref ok, version == 2, "an island without settings is still written as format 2 (older versions read it)");

			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			DynamicIslands.currentIslandProps.Clear();
			yield return null;
			DynamicIslands.LoadIsland(PropsIsland);
			yield return new WaitForSecondsRealtime(1f);
			List<EditorGameObject> back = placed.GetComponentsInChildren<EditorGameObject>().ToList();
			EditorGameObject b2 = back.FirstOrDefault(e => e.GameObjectName == "Creature_Boar");
			EditorGameObject s2 = back.FirstOrDefault(e => e.GameObjectName == "Note_Sign");
			EditorGameObject p2 = back.FirstOrDefault(e => e.GameObjectName == plainName);
			Check(ref ok, back.Count == 4 && b2 != null && ObjectProps.Count(b2.Props) == 3 && Mathf.Approximately(ObjectProps.Damage(b2.Props), 1.5f) && ObjectProps.ColorText(ObjectProps.Tint(b2.Props)) == "#FF2020",
				"the creatures' settings load back (" + back.Count + " objects)");
			Check(ref ok, s2 != null && ObjectProps.Get(s2.Props, ObjectProps.NoteText) == "Line one\nLine two \u00E5\u00E4\u00F6" && p2 != null && ObjectProps.Get(p2.Props, ObjectProps.NoteTitle) == "Hidden treasure",
				"note texts load back (with line breaks and \u00E5\u00E4\u00F6)");
			Check(ref ok, ObjectProps.Get(DynamicIslands.currentIslandProps, IslandProps.Title) == "Test Island" && ObjectProps.Get(DynamicIslands.currentIslandProps, IslandProps.Author) == "CI", "the island's name and author load back");

			// Loading an island while something is selected must not leave its settings on screen
			if (p2 != null)
			{
				TransformGizmoSelect(p2.transform);
				yield return null; yield return null;
				DynamicIslands.LoadIsland(PropsIsland);
				yield return new WaitForSecondsRealtime(0.5f);
				Check(ref ok, !ObjectInspector.Visible, "loading an island closes the inspector of the object that was selected");
			}

			File.Delete(IslandSpawner.PathFor(PropsIsland));
			if (ok) Log("PASS: object settings (creatures, notes, tints)"); else Fail("object settings (creatures, notes, tints)");
		}

		static void TransformGizmoSelect(Transform t)
		{
			var g = DynamicIslands.EditorGizmoHandler;
			g.ClearTargets(false);
			g.AddTarget(t, false);
		}

		#endregion

		#region World: creatures and notes

		[ConsoleCommand(name: "CICreatureProbe", docs: "Dev: what Raft offers for creatures here - its prefab list (in a world), their navigation, shaders; models in memory (editor)")]
		public static void CreatureProbe()
		{
			Network_Host_Entities host = null;
			try { host = ComponentManager<Network_Host_Entities>.Value; } catch { }
			AI_NetworkBehaviour[] prefabs = host != null ? host.AINetworkBehaviourPrefabs : Resources.FindObjectsOfTypeAll<AI_NetworkBehaviour>().Where(b => !b.gameObject.scene.IsValid()).ToArray();
			Log("Creature prefabs " + (host != null ? "in Network_Host_Entities" : "in memory (no world)") + ": " + (prefabs != null ? prefabs.Length : 0));
			if (prefabs != null)
				foreach (AI_NetworkBehaviour p in prefabs)
				{
					if (p == null) continue;
					NavMeshAgent agent = p.GetComponentInChildren<NavMeshAgent>(true);
					Renderer r = p.GetComponentsInChildren<Renderer>(true).FirstOrDefault(x => x is SkinnedMeshRenderer || x is MeshRenderer);
					Material m = r != null ? r.sharedMaterial : null;
					string colorProp = m != null ? new[] { "_Color", "_BaseColor", "_MainColor", "_TintColor", "_Tint", "_AlbedoColor" }.FirstOrDefault(m.HasProperty) : null;
					float hp = -1f;
					try { hp = p.networkEntity != null && p.networkEntity.stat_health != null ? p.networkEntity.stat_health.Max : -1f; } catch { }
					Log(string.Format("  {0} ({1}): agent {2}, shader {3}, colour {4}, health {5}, scale {6}, offered {7}", p.behaviourType, p.GetType().Name,
						agent != null ? NavMesh.GetSettingsNameFromID(agent.agentTypeID) + "/" + agent.agentTypeID + " r=" + agent.radius.ToString("F2") + " speed " + agent.speed : "none",
						m != null ? m.shader.name : "-", colorProp ?? "none", hp, p.localScaleInterval != null ? p.localScaleInterval.minValue + "-" + p.localScaleInterval.maxValue : "?",
						ContentCatalog.CreatureOf("Creature_" + p.behaviourType) != null));
				}
			int mask = LayerMasks.MASK_RaycastInteractable;
			Log("Interactable layers: " + LayerList(mask) + "; rays hit triggers: " + Physics.queriesHitTriggers + "; NavMesh agent types: " + NavMesh.GetSettingsCount());
		}

		[ConsoleCommand(name: "CICreatureTest", docs: "Dev, in game (host): an island with creatures and notes 150 m ahead - spawning, stats, tint, NavMesh, reading a note, the island banner, killing, unloading and reloading. CICreatureTest [keep]")]
		public static void CreatureTest(string[] args)
		{
			DynamicIslands.instance.StartCoroutine(CreatureTestRoutine(args != null && args.Contains("keep")));
		}

		/// <summary>Writes cicreature.island: the sample island with a herd of warthogs, a chicken, a puffer fish, a sign and an island name.</summary>
		static bool MakeCreatureIsland(out string error)
		{
			error = null;
			string source = IslandSpawner.ListSavedIslands().FirstOrDefault(n => n == "generated_sample") ?? IslandSpawner.ListSavedIslands().FirstOrDefault(n => n == TestIsland);
			if (source == null) { error = "no generated_sample or citest island to build on"; return false; }
			IslandFile f = IslandFile.Load(IslandSpawner.PathFor(source));
			f.Name = CreatureIsland;
			f.Elevation = 0f;
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1);
			Vector2 c = IslandSpawner.LandCentre(f);
			Func<float, float, Vector3> ground = (x, z) =>
			{
				int ix = Mathf.Clamp(Mathf.RoundToInt(x / step), 0, res - 1), iz = Mathf.Clamp(Mathf.RoundToInt(z / step), 0, res - 1);
				return new Vector3(x, f.Heights[iz, ix] * f.TerrainSize.y, z);
			};
			// Clear the land centre of other objects so the animals have room
			f.Objects.RemoveAll(o => new Vector2(o.Position.x - c.x, o.Position.z - c.y).magnitude < 20f);
			f.Objects.Add(new IslandObject { Name = "Creature_Boar", Position = ground(c.x, c.y), Props = new Dictionary<string, string> {
				{ ObjectProps.CreatureCount, "2" }, { ObjectProps.CreatureHealth, "2" }, { ObjectProps.CreatureDamage, "1.5" }, { ObjectProps.CreatureSpeed, "1.5" }, { ObjectProps.TintColor, "#FF3030" } } });
			f.Objects.Add(new IslandObject { Name = "Creature_Chicken", Position = ground(c.x + 8f, c.y + 3f), Props = new Dictionary<string, string> { { ObjectProps.CreatureSize, "1.5" } } });
			// A puffer fish in the water off the coast
			Vector3 sea = ground(c.x, c.y);
			for (float d = 10f; d < 300f; d += 5f) { Vector3 p = ground(c.x + d, c.y); if (p.y < f.WaterLevel - 4f) { sea = new Vector3(p.x, f.WaterLevel - 3f, p.z); break; } }
			f.Objects.Add(new IslandObject { Name = "Creature_PufferFish", Position = sea });
			f.Objects.Add(new IslandObject { Name = "Note_Sign", Position = ground(c.x - 5f, c.y), Props = new Dictionary<string, string> { { ObjectProps.NoteTitle, "Warning" }, { ObjectProps.NoteText, "Warthogs live here.\nBring a spear." } } });
			f.Props[IslandProps.Title] = "Warthog Hill";
			f.Props[IslandProps.Author] = "CI";
			f.Props[IslandProps.Description] = "Mind the red warthogs.";
			f.Save(IslandSpawner.PathFor(CreatureIsland));
			return true;
		}

		static IEnumerator CreatureTestRoutine(bool keep)
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			string error;
			if (!MakeCreatureIsland(out error)) { Fail(error); yield break; }
			IslandInfo.ForgetShown();
			bool ok = true;
			int exceptions = 0; string firstException = null;
			Application.LogCallback counter = (msg, trace, type) => { if (type == LogType.Exception || (type == LogType.Error && !msg.StartsWith("[CITEST]"))) { exceptions++; if (firstException == null) firstException = msg + " " + trace.Split('\n').FirstOrDefault(); } };
			Application.logMessageReceived += counter;

			// Clear of Raft's own islands (on top of one, players fall through the ground)
			Vector3? spot = CustomIslandSpawner.FindClearSpot(raftPos.Value, CustomIslandSpawner.LandRadius(CreatureIsland), 390f);
			if (!spot.HasValue) { Application.logMessageReceived -= counter; Fail("no open sea near the raft for the test island"); yield break; }
			Vector3 pos = spot.Value;
			int before = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile(CreatureIsland, pos, true);
			IslandWorldState.Entry entry = IslandWorldState.Islands.Skip(before).FirstOrDefault();
			if (entry == null || entry.Root == null) { Application.logMessageReceived -= counter; Fail("the creature island did not spawn"); yield break; }
			List<CreatureSpawnPoint> points = entry.Root.GetComponentsInChildren<CreatureSpawnPoint>(true).ToList();
			Check(ref ok, points.Count == 3, points.Count + " creature spawn points (no markers in the world: " + (entry.Root.GetComponentsInChildren<Transform>(true).All(t => t.name != "Marker")) + ")");

			float t0 = Time.realtimeSinceStartup;
			while (points.Sum(p => p.Spawned.Count) < 4 && Time.realtimeSinceStartup - t0 < 60f) yield return new WaitForSeconds(0.5f);
			yield return new WaitForSeconds(1f);
			foreach (CreatureSpawnPoint p in points)
				Log("  " + p.Kind.Label + ": " + p.Spawned.Count + " spawned " + string.Join(", ", p.Spawned.Where(a => a != null).Select(a => a.transform.position.ToString() + " hp " + a.networkEntity.stat_health.Value + "/" + a.networkEntity.stat_health.Max).ToArray()));
			CreatureSpawnPoint boars = points.FirstOrDefault(p => p.Kind.Type == AI_NetworkBehaviourType.Boar);
			CreatureSpawnPoint chicken = points.FirstOrDefault(p => p.Kind.Type == AI_NetworkBehaviourType.Chicken);
			CreatureSpawnPoint fish = points.FirstOrDefault(p => p.Kind.Type == AI_NetworkBehaviourType.PufferFish);
			Check(ref ok, boars != null && boars.Spawned.Count == 2, "two warthogs spawned (" + (Time.realtimeSinceStartup - t0).ToString("F1") + " s)");
			Check(ref ok, chicken != null && chicken.Spawned.Count == 1 && chicken.Spawned[0] is AI_NetworkBehaviour_Domestic, "a chicken spawned (catchable)");
			Check(ref ok, fish != null && fish.Spawned.Count == 1, "a puffer fish spawned in the sea");
			NavMeshHit hit;
			Check(ref ok, boars != null && NavMesh.SamplePosition(boars.transform.position, out hit, 3f, NavMesh.AllAreas), "the island has a NavMesh at the warthogs' spot");

			Network_Host_Entities host = ComponentManager<Network_Host_Entities>.Value;
			AI_NetworkBehaviour boar = boars != null ? boars.Spawned.FirstOrDefault(a => a != null) : null;
			if (boar != null)
			{
				AI_NetworkBehaviour prefab = host.AINetworkBehaviourPrefabs.First(b => b != null && b.behaviourType == AI_NetworkBehaviourType.Boar);
				float rafts;
				bool known = CreatureSpawner.HealthBefore.TryGetValue(boar, out rafts);
				Check(ref ok, known && rafts > 0f && Mathf.Abs(boar.networkEntity.stat_health.Max - rafts * 2f) < 0.5f && Mathf.Approximately(boar.networkEntity.stat_health.Value, boar.networkEntity.stat_health.Max),
					"health \u00D72: " + boar.networkEntity.stat_health.Value + "/" + boar.networkEntity.stat_health.Max + " (Raft's warthog: " + (known ? rafts.ToString() : "?") + ")");
				AI_Movement move = boar.GetComponentInChildren<AI_Movement>(true);
				Check(ref ok, move != null && Mathf.Approximately(CreatureSpawner.SpeedOf(move), 1.5f), "speed \u00D71.5 registered (moving at " + (move != null ? move.MovementSpeed.ToString("F2") : "?") + ")");
				List<string> fields = CreatureSpawner.ScaleDamage(prefab.gameObject, 1f); // only lists the fields (x1)
				Check(ref ok, fields.Count > 0, "damage fields scaled \u00D71.5: " + string.Join(", ", fields.ToArray()));
				Renderer r = boar.GetComponentsInChildren<Renderer>().FirstOrDefault(x => x is SkinnedMeshRenderer);
				var block = new MaterialPropertyBlock();
				if (r != null) r.GetPropertyBlock(block);
				string prop = r != null ? new[] { "_Color", "_BaseColor", "_MainColor", "_TintColor", "_Tint", "_AlbedoColor" }.FirstOrDefault(r.sharedMaterial.HasProperty) : null;
				Color col = prop != null ? block.GetColor(prop) : Color.clear;
				Check(ref ok, prop != null && col.r > col.g * 1.5f, "the warthog is tinted red (" + prop + " = " + col + ")");
				Check(ref ok, boar.connectedSpawner != null && boar.Serialize_Save() == null, "the warthog belongs to the island, not to Raft's world save");
			}

			// Read the note
			CustomNote note = entry.Root.GetComponentsInChildren<CustomNote>(true).FirstOrDefault();
			Check(ref ok, note != null && note.gameObject.layer != 0 && note.GetComponent<RaycastInteractable>() != null, "the sign is readable with Raft's interact key (layer " + (note != null ? LayerMask.LayerToName(note.gameObject.layer) : "-") + ")");
			if (note != null)
			{
				NoteReader.Open(note);
				yield return null;
				Check(ref ok, NoteReader.ShownTitle == "Warning" && NoteReader.ShownText == "Warthogs live here.\nBring a spear.", "the note opens: \"" + NoteReader.ShownTitle + "\"");
				Screenshot(new[] { "note_reader" });
				yield return new WaitForSeconds(1f);
				NoteReader.Close();
			}

			// Walk up to the island: its banner shows
			yield return StandRoutine(entry.Root);
			yield return new WaitForSeconds(1.5f);
			Check(ref ok, IslandInfo.LastShown != null && IslandInfo.LastShown.StartsWith("Warthog Hill"), "arriving shows the banner: " + IslandInfo.LastShown);
			Screenshot(new[] { "creatures" });
			yield return new WaitForSeconds(1f);

			// Kill a warthog: remembered
			if (boar != null)
			{
				boar.networkEntity.Damage(100000f, boar.transform.position, Vector3.up, EntityType.Player, true);
				yield return new WaitForSeconds(2.5f);
				ObjectState s;
				Check(ref ok, entry.State.TryGetValue(boars.StateKey, out s) && s.Yield == 1, "a killed warthog is remembered (" + (entry.State.ContainsKey(boars.StateKey) ? IslandObjectState.Encode(new Dictionary<int, ObjectState> { { boars.StateKey, entry.State[boars.StateKey] } }) : "no state") + ")");
			}

			// Unload: the animals go with the island; reload: the dead one stays dead
			List<AI_NetworkBehaviour> all = points.SelectMany(p => p.Spawned).Where(a => a != null).ToList();
			IslandObjectState.Capture(entry);
			IslandSpawner.Despawn(entry.Root);
			entry.Root = null;
			yield return new WaitForSeconds(0.5f);
			Check(ref ok, all.All(a => a == null) && CreatureSpawner.LiveCount == 0, "unloading the island removes its " + all.Count + " animals (" + all.Count(a => a != null) + " left)");
			// The automatic spawner loads it again (it is close to the raft)
			t0 = Time.realtimeSinceStartup;
			while (entry.Root == null && Time.realtimeSinceStartup - t0 < 60f) yield return new WaitForSeconds(0.5f);
			Check(ref ok, entry.Root != null && IslandSpawner.SpawnedRoots.Count(r => r != null && r.name == "CustomIsland_" + CreatureIsland) == 1, "the island comes back once (" + (Time.realtimeSinceStartup - t0).ToString("F1") + " s)");
			t0 = Time.realtimeSinceStartup;
			while (CreatureSpawner.LiveCount < 3 && Time.realtimeSinceStartup - t0 < 60f) yield return new WaitForSeconds(0.5f);
			yield return new WaitForSeconds(1f);
			CreatureSpawnPoint boars2 = entry.Root != null ? entry.Root.GetComponentsInChildren<CreatureSpawnPoint>().FirstOrDefault(p => p.Kind.Type == AI_NetworkBehaviourType.Boar) : null;
			Check(ref ok, boars2 != null && boars2.Spawned.Count == 1, "after reloading, one warthog (the other was killed): " + (boars2 != null ? boars2.Spawned.Count : -1));

			Application.logMessageReceived -= counter;
			Check(ref ok, exceptions == 0, "no errors (" + exceptions + (firstException != null ? ", first: " + firstException : "") + ")");
			if (!keep)
			{
				IslandWorldState.RemoveIds(new[] { entry.Id }, true);
				File.Delete(IslandSpawner.PathFor(CreatureIsland));
			}
			if (ok) Log("PASS: creatures and notes in a world" + (keep ? " (island kept)" : "")); else Fail("creatures and notes in a world");
		}

		#region Loot

		[ConsoleCommand(name: "CIItems", docs: "Dev: writes every item of Raft (unique name | shown name | has a picture) to Mods\\DynamicIslands\\items.txt, and checks the loot presets")]
		public static void ListItems()
		{
			var lines = ItemManager.GetAllItems().Where(i => i != null).OrderBy(i => i.UniqueName)
				.Select(i => i.UniqueName + " | " + ContentCatalog.ItemLabel(i.UniqueName) + " | " + (i.settings_Inventory != null && i.settings_Inventory.Sprite != null)).ToArray();
			File.WriteAllLines(Path.Combine(DynamicIslands.assetpath, "items.txt"), lines);
			Log(lines.Length + " items written to items.txt");
			foreach (var p in ContentCatalog.LootPresets)
			{
				string[] missing = p.Value.Select(x => x.Split('*')[0]).Where(n => !ContentCatalog.ItemExists(n)).ToArray();
				Log("Loot preset " + p.Key + ": " + ContentCatalog.PresetLoot(p.Value) + (missing.Length > 0 ? "   (not in Raft: " + string.Join(", ", missing) + ")" : ""));
			}
		}

		[ConsoleCommand(name: "CILootTest", docs: "Dev, editor: chests - the Loot & chests category, default loot, the item picker, amounts, undo, making any object a chest, save and load")]
		public static void LootTest()
		{
			DynamicIslands.instance.StartCoroutine(LootTestRoutine());
		}

		static IEnumerator LootTestRoutine()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			EditorUI.SetTab(TAB.ObjectPlace);
			Transform placed = GameObject.Find("PlacedObjects").transform;
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			yield return null;
			CommandUndoRedo.UndoRedoManager.Clear();
			var cats = PlaceableCatalog.Browse().ToDictionary(c => c.Key, c => c.Value.Count);
			Check(ref ok, cats.ContainsKey(ContentCatalog.LootCategory) && cats[ContentCatalog.LootCategory] >= 5, "the object list has " + ContentCatalog.LootCategory + " (" + (cats.ContainsKey(ContentCatalog.LootCategory) ? cats[ContentCatalog.LootCategory] : 0) + ")");

			Vector3 c0 = terraineditor.terrain.transform.position + new Vector3(500f, IslandFile.DefaultWaterLevel + 1f, 500f);
			EditorGameObject chest = PlaceForTest("Loot_Chest", c0, placed);
			string plainName = PlaceableCatalog.CoreNames.FirstOrDefault(n => n.StartsWith("TP_Moontown_TarpCrate"));
			EditorGameObject crate = PlaceForTest(plainName ?? PlaceableCatalog.CoreNames.First(), c0 + new Vector3(4, 0, 0), placed);
			if (chest == null || crate == null) { Fail("could not place a chest"); yield break; }
			List<KeyValuePair<string, int>> start = ObjectProps.Loot(chest.Props);
			Check(ref ok, start.Count >= 3 && start.All(l => ContentCatalog.ItemExists(l.Key)), "a new chest starts with Raft items: " + ObjectProps.Get(chest.Props, ObjectProps.LootItems));

			List<string> items = ItemManager.GetAllItems().Where(i => i != null && i.settings_Inventory != null && i.settings_Inventory.Sprite != null).Select(i => i.UniqueName).Take(40).ToList();
			ItemPickerWindow.AddItem(chest, items[20]);
			ItemPickerWindow.AddItem(chest, items[20]);
			List<KeyValuePair<string, int>> after = ObjectProps.Loot(chest.Props);
			Check(ref ok, after.Count == start.Count + 1 && after.Last().Key == items[20] && after.Last().Value == 2, "adding an item twice makes 2 of it (" + ContentCatalog.ItemLabel(items[20]) + ")");
			CommandUndoRedo.UndoRedoManager.Undo(); // both clicks are one step
			Check(ref ok, ObjectProps.Loot(chest.Props).Count == start.Count, "undo takes the added item out again");
			CommandUndoRedo.UndoRedoManager.Redo();

			// Any object can be a chest (with a note too)
			PropsCommand.Change(crate, ObjectProps.With(crate.Props, ObjectProps.LootItems, ContentCatalog.PresetLoot(ContentCatalog.LootPresets[1].Value)));
			NoteEditorWindow.Apply(crate, "Supplies", "Take what you need.");
			PropsCommand.Change(crate, ObjectProps.With(crate.Props, ObjectProps.LootRefill, "0"));
			Check(ref ok, ObjectProps.IsLoot(crate.GameObjectName, crate.Props) && ObjectProps.IsNote(crate.GameObjectName, crate.Props) && !ObjectProps.LootRefills(crate.Props), "a plain crate holds loot and a note, and never refills");
			TextMesh tag = crate.GetComponentsInChildren<TextMesh>().FirstOrDefault();
			Check(ref ok, tag != null && tag.text.Contains("Supplies") && tag.text.Contains("items"), "its name tag shows the note and the loot: " + (tag != null ? tag.text.Replace("\n", " / ") : "none"));

			// Inspector and the item picker
			TransformGizmoSelect(chest.transform);
			yield return null; yield return null;
			Check(ref ok, ObjectInspector.Visible && ObjectInspector.Target == chest, "selecting the chest opens its loot");
			Screenshot(new[] { "loot_inspector" });
			yield return new WaitForSecondsRealtime(0.5f);
			ItemPickerWindow.Open(chest);
			yield return null;
			Check(ref ok, ItemPickerWindow.IsOpen, "the item picker opens");
			Screenshot(new[] { "loot_picker" });
			yield return new WaitForSecondsRealtime(0.5f);
			ItemPickerWindow.Close();
			TransformGizmoSelect(crate.transform);
			yield return null; yield return null;
			Screenshot(new[] { "loot_crate" });
			yield return new WaitForSecondsRealtime(0.5f);
			DynamicIslands.EditorGizmoHandler.ClearTargets(false);

			string chestLoot = ObjectProps.Get(chest.Props, ObjectProps.LootItems), crateLoot = ObjectProps.Get(crate.Props, ObjectProps.LootItems);
			bool saved = DynamicIslands.SaveIsland("ciloot");
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			yield return null;
			DynamicIslands.LoadIsland("ciloot");
			yield return new WaitForSecondsRealtime(1f);
			List<EditorGameObject> back = placed.GetComponentsInChildren<EditorGameObject>().ToList();
			EditorGameObject c2 = back.FirstOrDefault(e => e.GameObjectName == "Loot_Chest"), k2 = back.FirstOrDefault(e => e.GameObjectName == crate.GameObjectName);
			Check(ref ok, saved && c2 != null && k2 != null && ObjectProps.Get(c2.Props, ObjectProps.LootItems) == chestLoot && ObjectProps.Get(k2.Props, ObjectProps.LootItems) == crateLoot && !ObjectProps.LootRefills(k2.Props),
				"the loot saves and loads back");
			File.Delete(IslandSpawner.PathFor("ciloot"));
			if (ok) Log("PASS: loot in the editor"); else Fail("loot in the editor");
		}

		[ConsoleCommand(name: "CILootWorld", docs: "Dev, in game (host): an island with two chests - opening gives the items, it stays empty (also after reloading), other players are told, refilling")]
		public static void LootWorld()
		{
			DynamicIslands.instance.StartCoroutine(LootWorldRoutine());
		}

		static IEnumerator LootWorldRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			string source = IslandSpawner.ListSavedIslands().FirstOrDefault(n => n == "generated_sample");
			if (source == null) { Fail("no generated_sample island"); yield break; }
			IslandFile f = IslandFile.Load(IslandSpawner.PathFor(source));
			f.Name = "ciloot";
			f.Elevation = 0f;
			Vector2 c = IslandSpawner.LandCentre(f);
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1);
			Func<float, float, Vector3> ground = (x, z) => new Vector3(x, f.Heights[Mathf.RoundToInt(z / step), Mathf.RoundToInt(x / step)] * f.TerrainSize.y, z);
			string loot = ContentCatalog.PresetLoot(ContentCatalog.LootPresets[0].Value);
			f.Objects.Add(new IslandObject { Name = "Loot_Chest", Position = ground(c.x, c.y), Props = new Dictionary<string, string> { { ObjectProps.LootItems, loot } } });
			f.Objects.Add(new IslandObject { Name = "Loot_Barrel", Position = ground(c.x + 3f, c.y), Props = new Dictionary<string, string> { { ObjectProps.LootItems, "Plank*3" }, { ObjectProps.LootRefill, "0" }, { ObjectProps.NoteTitle, "Barrel note" } } });
			f.Save(IslandSpawner.PathFor("ciloot"));

			Vector3? spot = CustomIslandSpawner.FindClearSpot(raftPos.Value, CustomIslandSpawner.LandRadius("ciloot"), 390f);
			if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
			int before = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile("ciloot", spot.Value, true);
			IslandWorldState.Entry entry = IslandWorldState.Islands.Skip(before).FirstOrDefault();
			if (entry == null || entry.Root == null) { Fail("the loot island did not spawn"); yield break; }
			List<LootCrate> crates = entry.Root.GetComponentsInChildren<LootCrate>().OrderBy(x => x.Ordinal).ToList();
			Check(ref ok, crates.Count == 2 && crates[0].GetComponent<RaycastInteractable>() != null && crates[1].GetComponent<CustomNote>() != null, crates.Count + " chests, interactable; the barrel also has a note");
			if (crates.Count < 2) yield break;

			// Open the chest: the items arrive in the inventory
			PlayerInventory inv = RAPI.GetLocalPlayer().Inventory;
			List<KeyValuePair<string, int>> want = ObjectProps.Loot(new Dictionary<string, string> { { ObjectProps.LootItems, loot } });
			Dictionary<string, int> had = want.ToDictionary(w => w.Key, w => inv.GetItemCount(w.Key));
			IslandNetMessage sent = null;
			IslandNetwork.Loopback = m => sent = m;
			List<string> given;
			try { given = crates[0].Open(); }
			finally { IslandNetwork.Loopback = null; }
			yield return null;
			// Anything that didn't fit was dropped, so count what arrived in the inventory
			int arrived = want.Count(w => inv.GetItemCount(w.Key) - had[w.Key] == w.Value);
			Check(ref ok, given.Count == want.Count && arrived > 0, "opening gives " + string.Join(", ", given.ToArray()) + " (" + arrived + " of " + want.Count + " kinds in the inventory, the rest dropped)");
			Check(ref ok, crates[0].Looted && crates[0].Open().Count == 0, "then it's empty (a second open gives nothing)");
			Check(ref ok, sent != null && sent.Kind == IslandNetMessage.ObjectUsed && sent.Ids[0] == entry.Id && sent.Index == crates[0].StateKey, "the other players are told (" + (sent != null ? "message " + sent.Kind + ", key " + sent.Index : "nothing sent") + ")");
			// A client receiving that message marks it too
			entry.State.Remove(crates[0].StateKey);
			ContentState.ApplyUsed(entry.Id, crates[0].StateKey, 3);
			Check(ref ok, crates[0].Looted, "a looted message from another player empties it here");

			// The barrel with a note: opens and shows the note
			crates[1].Open();
			yield return null;
			Check(ref ok, NoteReader.IsOpen && NoteReader.ShownTitle == "Barrel note", "a chest with a note shows the note when opened");
			NoteReader.Close();

			// Reload: still empty; after the regrow time the chest refills, the barrel (never) doesn't
			IslandObjectState.Capture(entry);
			IslandSpawner.Despawn(entry.Root);
			entry.Root = null;
			float t0 = Time.realtimeSinceStartup;
			while (entry.Root == null && Time.realtimeSinceStartup - t0 < 60f) yield return new WaitForSeconds(0.5f);
			crates = entry.Root != null ? entry.Root.GetComponentsInChildren<LootCrate>().OrderBy(x => x.Ordinal).ToList() : new List<LootCrate>();
			Check(ref ok, crates.Count == 2 && crates[0].Looted && crates[1].Looted, "after reloading the island both are still empty");
			int days = CustomIslandSpawner.RegrowDays;
			foreach (int key in new[] { ContentState.LootKeyBase, ContentState.LootKeyBase + 1 }) entry.State[key] = new ObjectState { Active = false, Day = -1000 };
			ContentState.OnIslandReady(entry);
			Check(ref ok, crates.Count == 2 && !crates[0].Looted && crates[1].Looted, "after " + days + " days the chest is full again; the barrel (never) stays empty");

			IslandWorldState.RemoveIds(new[] { entry.Id }, true);
			File.Delete(IslandSpawner.PathFor("ciloot"));
			if (ok) Log("PASS: loot in a world"); else Fail("loot in a world");
		}

		#endregion

		#region Trigger zones and island rules

		[ConsoleCommand(name: "CIZoneTest", docs: "Dev, editor: trigger zones - placing, the zone editor, linking a creature (ambush), renaming, the island rule, save and load")]
		public static void ZoneTest()
		{
			DynamicIslands.instance.StartCoroutine(ZoneTestRoutine());
		}

		static IEnumerator ZoneTestRoutine()
		{
			yield return WaitForEditor(false);
			bool ok = true;
			EditorUI.SetTab(TAB.ObjectPlace);
			Transform placed = GameObject.Find("PlacedObjects").transform;
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			yield return null;
			CommandUndoRedo.UndoRedoManager.Clear();
			Vector3 c0 = terraineditor.terrain.transform.position + new Vector3(500f, IslandFile.DefaultWaterLevel + 1f, 500f);
			EditorGameObject zone = PlaceForTest(ContentCatalog.TriggerZone, c0, placed);
			EditorGameObject boar = PlaceForTest("Creature_Boar", c0 + new Vector3(6, 0, 0), placed);
			if (zone == null || boar == null) { Fail("could not place a zone and a warthog"); yield break; }
			string id = ObjectProps.Get(zone.Props, ObjectProps.ZoneId);
			Check(ref ok, id.StartsWith("zone-") && ContentCatalog.ZoneIdsInEditor().Contains(id), "a new zone gets a name (" + id + ")");
			PropsCommand.Change(zone, ObjectProps.With(ObjectProps.With(zone.Props, ObjectProps.ZoneRadius, "10"), ObjectProps.ZoneMessage, "You hear grunting..."));
			PropsCommand.Change(boar, ObjectProps.With(boar.Props, ObjectProps.CreatureZone, id));
			Transform sphere = zone.transform.Cast<Transform>().FirstOrDefault(t => t.name == ContentCatalog.MarkerOnly && t.GetComponent<TextMesh>() == null);
			Check(ref ok, sphere != null && Mathf.Approximately(sphere.localScale.x, 20f), "the sphere shows the 10 m radius");
			TextMesh bl = boar.GetComponentsInChildren<TextMesh>().FirstOrDefault();
			Check(ref ok, bl != null && bl.text.Contains("waits for " + id), "the warthog's tag says it waits: " + (bl != null ? bl.text.Replace("\n", " / ") : "none"));

			// Clicking in the zone's sphere still selects what is inside it
			Camera cam = Camera.main;
			cam.transform.position = c0 + new Vector3(6, 4, -8);
			cam.transform.LookAt(boar.transform.position + Vector3.up * 0.5f);
			Transform picked = PlacementOptions.PickObject(cam.ScreenPointToRay(cam.WorldToScreenPoint(boar.transform.position + Vector3.up * 0.5f)), ~0);
			Check(ref ok, picked == boar.transform, "clicking the warthog inside the zone selects the warthog (got " + (picked != null ? picked.name : "nothing") + ")");

			TransformGizmoSelect(zone.transform);
			yield return null; yield return null;
			Check(ref ok, ObjectInspector.Visible && ObjectInspector.Target == zone, "selecting the zone opens the zone editor");
			Screenshot(new[] { "zone_inspector" });
			yield return new WaitForSecondsRealtime(0.5f);
			TransformGizmoSelect(boar.transform);
			yield return null; yield return null;
			Screenshot(new[] { "zone_creature" });
			yield return new WaitForSecondsRealtime(0.5f);
			DynamicIslands.EditorGizmoHandler.ClearTargets(false);

			// Island rule
			DynamicIslands.currentIslandProps[IslandProps.RegrowDays] = "5";
			bool saved = DynamicIslands.SaveIsland("cizone");
			foreach (Transform child in placed) UnityEngine.Object.Destroy(child.gameObject);
			DynamicIslands.currentIslandProps.Clear();
			yield return null;
			DynamicIslands.LoadIsland("cizone");
			yield return new WaitForSecondsRealtime(1f);
			EditorGameObject z2 = placed.GetComponentsInChildren<EditorGameObject>().FirstOrDefault(e => e.GameObjectName == ContentCatalog.TriggerZone);
			EditorGameObject b2 = placed.GetComponentsInChildren<EditorGameObject>().FirstOrDefault(e => e.GameObjectName == "Creature_Boar");
			Check(ref ok, saved && z2 != null && b2 != null && ObjectProps.Get(z2.Props, ObjectProps.ZoneMessage) == "You hear grunting..." && ObjectProps.Get(b2.Props, ObjectProps.CreatureZone) == id
				&& ObjectProps.Get(DynamicIslands.currentIslandProps, IslandProps.RegrowDays) == "5", "the zone, the link and the island rule load back");
			File.Delete(IslandSpawner.PathFor("cizone"));
			if (ok) Log("PASS: trigger zones in the editor"); else Fail("trigger zones in the editor");
		}

		[ConsoleCommand(name: "CIZoneWorld", docs: "Dev, in game (host): a trigger zone with a message, items and an ambush warthog; the island rule for regrowing")]
		public static void ZoneWorld()
		{
			DynamicIslands.instance.StartCoroutine(ZoneWorldRoutine());
		}

		static IEnumerator ZoneWorldRoutine()
		{
			Vector3? raftPos = CustomIslandSpawner.RaftPosition;
			if (!raftPos.HasValue || !Raft_Network.IsHost) { Fail("run in a world, as the host"); yield break; }
			yield return EnsureAlive();
			bool ok = true;
			IslandFile f = IslandFile.Load(IslandSpawner.PathFor("generated_sample"));
			f.Name = "cizone";
			f.Elevation = 0f;
			Vector2 c = IslandSpawner.LandCentre(f);
			int res = f.HeightmapResolution;
			float step = f.TerrainSize.x / (res - 1);
			Func<float, float, Vector3> ground = (x, z) => new Vector3(x, f.Heights[Mathf.RoundToInt(z / step), Mathf.RoundToInt(x / step)] * f.TerrainSize.y, z);
			f.Objects.RemoveAll(o => new Vector2(o.Position.x - c.x, o.Position.z - c.y).magnitude < 15f);
			f.Objects.Add(new IslandObject { Name = ContentCatalog.TriggerZone, Position = ground(c.x, c.y), Props = new Dictionary<string, string> {
				{ ObjectProps.ZoneId, "ambush" }, { ObjectProps.ZoneRadius, "8" }, { ObjectProps.ZoneMessage, "Something moves in the bushes!" }, { ObjectProps.LootItems, ContentCatalog.PresetLoot(new[] { "Plank*2", "Rope*1" }) } } });
			f.Objects.Add(new IslandObject { Name = "Creature_Boar", Position = ground(c.x + 5f, c.y), Props = new Dictionary<string, string> { { ObjectProps.CreatureZone, "ambush" } } });
			f.Objects.Add(new IslandObject { Name = "Creature_Chicken", Position = ground(c.x - 5f, c.y) });
			f.Props[IslandProps.RegrowDays] = "7";
			f.Save(IslandSpawner.PathFor("cizone"));

			Vector3? spot = CustomIslandSpawner.FindClearSpot(raftPos.Value, CustomIslandSpawner.LandRadius("cizone"), 390f);
			if (!spot.HasValue) { Fail("no open sea near the raft"); yield break; }
			int before = IslandWorldState.Islands.Count;
			yield return DynamicIslands.instance.SpawnIslandFile("cizone", spot.Value, true);
			IslandWorldState.Entry entry = IslandWorldState.Islands.Skip(before).FirstOrDefault();
			if (entry == null || entry.Root == null) { Fail("the zone island did not spawn"); yield break; }
			Check(ref ok, IslandRules.RegrowDays(entry) == 7, "the island's own rule: things come back after " + IslandRules.RegrowDays(entry) + " days");
			TriggerZone zone = entry.Root.GetComponentInChildren<TriggerZone>();
			Check(ref ok, zone != null && zone.Id == "ambush" && zone.GetComponentsInChildren<Renderer>().Length == 0, "the zone exists and is invisible");

			float t0 = Time.realtimeSinceStartup;
			while (CreatureSpawner.LiveCount < 1 && Time.realtimeSinceStartup - t0 < 30f) yield return new WaitForSeconds(0.5f);
			yield return new WaitForSeconds(2f);
			CreatureSpawnPoint boars = entry.Root.GetComponentsInChildren<CreatureSpawnPoint>().First(p => p.Kind.Type == AI_NetworkBehaviourType.Boar);
			CreatureSpawnPoint chicken = entry.Root.GetComponentsInChildren<CreatureSpawnPoint>().First(p => p.Kind.Type == AI_NetworkBehaviourType.Chicken);
			Check(ref ok, chicken.Spawned.Count == 1 && boars.Spawned.Count == 0, "the chicken is there, the ambush warthog waits (" + boars.Spawned.Count + ")");

			// Walk in (the zone checks the local player; the test sets it off directly)
			PlayerInventory inv = RAPI.GetLocalPlayer().Inventory;
			var items = ObjectProps.Loot(new Dictionary<string, string> { { ObjectProps.LootItems, ContentCatalog.PresetLoot(new[] { "Plank*2", "Rope*1" }) } });
			Dictionary<string, int> had = items.ToDictionary(i => i.Key, i => inv.GetItemCount(i.Key));
			zone.Enter();
			yield return null;
			Check(ref ok, IslandInfo.LastMessage == "Something moves in the bushes!", "walking in shows the message");
			Check(ref ok, items.Count > 0 && items.All(i => inv.GetItemCount(i.Key) - had[i.Key] == i.Value), "and gives " + string.Join(", ", items.Select(i => i.Key + "*" + i.Value).ToArray()));
			Screenshot(new[] { "zone_message" });
			t0 = Time.realtimeSinceStartup;
			while (boars.Spawned.Count == 0 && Time.realtimeSinceStartup - t0 < 20f) yield return new WaitForSeconds(0.5f);
			Check(ref ok, boars.Spawned.Count == 1 && zone.HasFired, "the ambush warthog appears (" + (Time.realtimeSinceStartup - t0).ToString("F1") + " s)");
			IslandInfo.ForgetShown();
			zone.Enter();
			Check(ref ok, IslandInfo.LastMessage == null, "a once-zone doesn't fire again");

			// Reload: the zone has fired, so the warthog is there at once
			IslandObjectState.Capture(entry);
			IslandSpawner.Despawn(entry.Root);
			entry.Root = null;
			t0 = Time.realtimeSinceStartup;
			while (entry.Root == null && Time.realtimeSinceStartup - t0 < 60f) yield return new WaitForSeconds(0.5f);
			t0 = Time.realtimeSinceStartup;
			CreatureSpawnPoint b2 = null;
			while (Time.realtimeSinceStartup - t0 < 30f)
			{
				b2 = entry.Root != null ? entry.Root.GetComponentsInChildren<CreatureSpawnPoint>().FirstOrDefault(p => p.Kind.Type == AI_NetworkBehaviourType.Boar) : null;
				if (b2 != null && b2.Spawned.Count > 0) break;
				yield return new WaitForSeconds(0.5f);
			}
			Check(ref ok, b2 != null && b2.Spawned.Count == 1, "after reloading, the ambush warthog is already there");

			IslandWorldState.RemoveIds(new[] { entry.Id }, true);
			File.Delete(IslandSpawner.PathFor("cizone"));
			if (ok) Log("PASS: trigger zones in a world"); else Fail("trigger zones in a world");
		}

		#endregion

		[ConsoleCommand(name: "CINoteLook", docs: "Dev, in game: stands the player in front of the nearest readable note and checks Raft's own interaction ray finds it")]
		public static void NoteLook()
		{
			DynamicIslands.instance.StartCoroutine(NoteLookRoutine());
		}

		static IEnumerator NoteLookRoutine()
		{
			Network_Player player = RAPI.GetLocalPlayer();
			CustomNote note = player != null ? UnityEngine.Object.FindObjectsOfType<CustomNote>().OrderBy(n => (n.transform.position - player.transform.position).sqrMagnitude).FirstOrDefault() : null;
			if (note == null) { Fail("no player or no readable note (run CICreatureTest keep first)"); yield break; }
			Vector3 target = note.GetComponent<Collider>().bounds.center;
			Vector3 away = Vector3.ProjectOnPlane(note.transform.parent.forward, Vector3.up).normalized;
			if (away.sqrMagnitude < 0.01f) away = Vector3.forward;
			// On the ground 1.8 m in front of it
			Vector3 stand = target + away * 1.8f;
			RaycastHit ground;
			if (Physics.Raycast(stand + Vector3.up * 10f, Vector3.down, out ground, 30f, 1 << IslandSpawner.TerrainLayer)) stand.y = ground.point.y + 0.2f;
			CharacterController cc = player.PersonController.controller;
			cc.enabled = false;
			player.transform.position = stand;
			cc.enabled = true;
			yield return new WaitForSeconds(1f);
			Camera cam = Helper.MainCamera;
			cam.transform.LookAt(target);
			RaycastInteractable found = Helper.FindInteractable(Player.UseDistance * 1.1f, QueryTriggerInteraction.Collide);
			bool ok = found != null && found.gameObject == note.gameObject && found.RaycastableObjects.Contains(note);
			if (ok) Log("PASS: Raft's interaction ray finds the note \"" + note.Title + "\" from " + Vector3.Distance(cam.transform.position, target).ToString("F1") + " m");
			else Fail("Raft's interaction ray found " + (found != null ? found.name : "nothing") + " instead of the note (camera at " + cam.transform.position + ", note at " + target + ")");
		}

		[ConsoleCommand(name: "CIMainMenu", docs: "Dev, in game: leaves the world (saving) for the main menu, as Raft's pause menu does")]
		public static void MainMenu()
		{
			Log("Leaving the world");
			ComponentManager<Raft_Network>.Value.LeaveGame((DisconnectReason)2, (SceneName)1, true, false);
		}

		[ConsoleCommand(name: "CIModelCheck", docs: "Dev, editor (after a world): creatures show Raft's real models instead of markers; places a few and takes a screenshot")]
		public static void ModelCheck()
		{
			DynamicIslands.instance.StartCoroutine(ModelCheckRoutine());
		}

		static IEnumerator ModelCheckRoutine()
		{
			yield return WaitForEditor(false);
			int models = ContentCatalog.Creatures.Count(k => PlaceableCatalog.Get(k.Name) != null && PlaceableCatalog.Get(k.Name).transform.Find("Model") != null);
			EditorUI.SetTab(TAB.ObjectPlace);
			Transform placed = GameObject.Find("PlacedObjects").transform;
			Vector3 c0 = terraineditor.terrain.transform.position + new Vector3(500f, IslandFile.DefaultWaterLevel, 500f);
			string[] show = { "Creature_Boar", "Creature_Llama", "Creature_Bear", "Creature_Chicken", "Creature_Hyena" };
			for (int i = 0; i < show.Length; i++)
			{
				EditorGameObject e = PlaceForTest(show[i], c0 + new Vector3(i * 4f - 8f, 0, 0), placed);
				if (e != null && i == 0) PropsCommand.Change(e, ObjectProps.With(e.Props, ObjectProps.TintColor, "#6080FF"));
			}
			Camera.main.transform.position = c0 + new Vector3(0, 5f, -14f);
			Camera.main.transform.rotation = Quaternion.Euler(15f, 0, 0);
			yield return new WaitForSecondsRealtime(0.5f);
			Screenshot(new[] { "creature_models" });
			if (models == ContentCatalog.Creatures.Length) Log("PASS: all " + models + " creatures show Raft's models in the editor");
			else Fail("only " + models + " of " + ContentCatalog.Creatures.Length + " creatures show Raft's models (visit a world first)");
		}

		#endregion
	}
}
