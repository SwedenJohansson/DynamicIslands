using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace DynamicIslands.Editor
{
	/// <summary>Where a creature of an island lives in a world: its type, its settings and the live animals from it.</summary>
	public class CreatureSpawnPoint : MonoBehaviour
	{
		public ContentCatalog.CreatureKind Kind;
		public Dictionary<string, string> Props = new Dictionary<string, string>();
		/// <summary>Place among the island's creatures (file order: the same on every machine).</summary>
		public int Ordinal;
		/// <summary>Host: the animals spawned here that are still wild (not caught), dead ones included until removed.</summary>
		public readonly List<AI_NetworkBehaviour> Spawned = new List<AI_NetworkBehaviour>();
		/// <summary>Host: how many are alive and wild, as last recorded in the island's state.</summary>
		public int RecordedAlive = -1;

		public static CreatureSpawnPoint Create(Transform parent, IslandObject o, int ordinal)
		{
			var go = new GameObject("CreatureSpawn_" + ordinal + "_" + o.Name);
			go.transform.SetParent(parent, false);
			go.transform.position = parent.position + o.Position;
			go.transform.rotation = Quaternion.Euler(o.EulerRotation);
			var p = go.AddComponent<CreatureSpawnPoint>();
			p.Kind = ContentCatalog.CreatureOf(o.Name);
			p.Props = o.Props != null ? new Dictionary<string, string>(o.Props) : new Dictionary<string, string>();
			p.Ordinal = ordinal;
			return p;
		}

		/// <summary>Key of this spawn point in the island's object state (above the 16 bits pickups use).</summary>
		public int StateKey { get { return CreatureSpawner.StateKeyBase + Ordinal; } }
	}

	/// <summary>
	/// Brings the creatures of custom islands to life in a world. The host spawns Raft's real, networked animals at
	/// the island's creature spawn points through Raft's own Network_Host_Entities (clients get them from Raft's
	/// networking, like any animal), with the builder's health, damage, speed, size and colour.
	///
	/// How they fit into Raft:
	///   - Each animal is tied to a switched-off LandmarkEntitySpawner at its spawn point, like the animals of Raft's own
	///     islands: it roams around that point, Raft does not put it in the world save (the island brings it back),
	///     and a domestic animal that is caught and carried off is let go by the spawner and becomes a normal raft animal.
	///   - Raft's animals walk on a NavMesh. Raft's islands come with one baked in; ours is built when the island loads
	///     (NavMeshSurface, from the island's terrain and objects) before any land animal is spawned.
	///   - When the island unloads (far away) its animals are removed the way Raft removes a landmark's animals.
	///   - Killed and caught animals are remembered per spawn point in the island's state (saved with the world);
	///     they come back after CustomIslandSpawner.RegrowDays in-game days unless the builder turned respawning off.
	/// </summary>
	public static class CreatureSpawner
	{
		public const int StateKeyBase = 0x10000;
		/// <summary>Farthest a spawned animal is placed from its spawn point when several share it (m).</summary>
		const float HerdRadius = 3f;

		/// <summary>Animals spawned by custom islands that are still tied to their spawn point.</summary>
		static readonly HashSet<AI_NetworkBehaviour> ours = new HashSet<AI_NetworkBehaviour>();
		/// <summary>Speed multiplier per animal's movement component (read by the movement patches).</summary>
		static readonly Dictionary<AI_Movement, float> speed = new Dictionary<AI_Movement, float>();
		static float nextTick;

		public static bool IsOurs(AI_NetworkBehaviour ai) { return ai != null && ours.Contains(ai); }

		public static int LiveCount { get { return ours.Count(a => a != null); } }

		static Network_Host_Entities HostEntities { get { try { return ComponentManager<Network_Host_Entities>.Value; } catch { return null; } } }

		static int Today { get { try { return WorldManager.DayCounter; } catch { return 0; } } }

		/// <summary>A world finished loading: nothing of the last world is ours any more.</summary>
		public static void OnWorldLoaded()
		{
			ours.Clear();
			speed.Clear();
			HealthBefore.Clear();
			clientTinted.Clear();
			Network_Host_Entities h = HostEntities;
			if (h != null) ContentCatalog.CacheModels(h.AINetworkBehaviourPrefabs);
		}

		#region Spawning (host)

		/// <summary>Host: an island was spawned or reloaded; brings its creatures once the ground is ready for them.</summary>
		public static void OnIslandReady(IslandWorldState.Entry entry)
		{
			if (entry == null || entry.Root == null || !Raft_Network.IsHost) return;
			if (entry.Root.GetComponentInChildren<CreatureSpawnPoint>(true) == null) return;
			DynamicIslands.instance.StartCoroutine(SpawnRoutine(entry, entry.Root));
		}

		static IEnumerator SpawnRoutine(IslandWorldState.Entry entry, GameObject root)
		{
			Network_Host_Entities host = null;
			for (float t = 0; t < 30f && (host = HostEntities) == null; t += 0.5f) yield return new WaitForSeconds(0.5f);
			if (host == null) { Debug.LogWarning("[CUSTOM ISLANDS] Raft's creature manager is missing: no creatures on '" + entry.HostName + "'"); yield break; }
			ContentCatalog.CacheModels(host.AINetworkBehaviourPrefabs);

			// Spawn points not handled yet (this runs again when a zone fires), except those still waiting for their zone
			List<CreatureSpawnPoint> points = root.GetComponentsInChildren<CreatureSpawnPoint>(true).Where(p => p.Kind != null && p.RecordedAlive < 0 && !WaitsForZone(entry, root, p)).ToList();
			var wanted = new Dictionary<CreatureSpawnPoint, int>();
			foreach (CreatureSpawnPoint p in points)
			{
				int n = HowManyNow(entry, p);
				if (n > 0) wanted[p] = n;
				p.RecordedAlive = n;
			}
			if (wanted.Count == 0) yield break;

			// Land animals need a NavMesh for each kind of agent Raft gives them
			var agentTypes = new HashSet<int>();
			foreach (CreatureSpawnPoint p in wanted.Keys)
			{
				AI_NetworkBehaviour prefab = Prefab(host, p.Kind.Type);
				NavMeshAgent agent = prefab != null ? prefab.GetComponentInChildren<NavMeshAgent>(true) : null;
				if (agent != null) agentTypes.Add(agent.agentTypeID);
			}
			// (built already for animals spawned earlier)
			agentTypes.RemoveWhere(t => root.GetComponents<NavMeshSurface>().Any(s => s.agentTypeID == t));
			if (agentTypes.Count > 0)
			{
				float started = Time.realtimeSinceStartup;
				yield return BuildNavMesh(root, agentTypes);
				if (root == null) yield break;
				List<CreatureSpawnPoint> walkers = wanted.Keys.Where(p => { AI_NetworkBehaviour pf = Prefab(host, p.Kind.Type); return pf != null && pf.GetComponentInChildren<NavMeshAgent>(true) != null; }).ToList();
				NavMeshHit probe;
				if (walkers.Count > 0 && !walkers.Any(p => NavMesh.SamplePosition(p.transform.position, out probe, 8f, NavMesh.AllAreas)))
				{
					Debug.LogWarning("[CUSTOM ISLANDS] '" + entry.HostName + "': no NavMesh at the animals' spots after building it; building it again");
					foreach (NavMeshSurface s in root.GetComponents<NavMeshSurface>()) UnityEngine.Object.Destroy(s);
					yield return null;
					if (root == null) yield break;
					yield return BuildNavMesh(root, agentTypes);
					if (root == null) yield break;
				}
				Debug.Log("[CUSTOM ISLANDS] '" + entry.HostName + "': NavMesh for " + agentTypes.Count + " kind(s) of animal built in " + (Time.realtimeSinceStartup - started).ToString("F1") + " s");
			}
			if (root == null || entry.Root != root || !Raft_Network.IsHost) yield break;

			int spawned = 0;
			foreach (var w in wanted)
				for (int i = 0; i < w.Value; i++)
					if (Spawn(host, w.Key, i, w.Value) != null) spawned++;
			Debug.Log("[CUSTOM ISLANDS] '" + entry.HostName + "': " + spawned + " creature(s) spawned at " + wanted.Count + " spawn point(s)");
		}

		/// <summary>An ambush: the creature waits until a player sets off its trigger zone (a zone that isn't on the island doesn't hold it back).</summary>
		static bool WaitsForZone(IslandWorldState.Entry entry, GameObject root, CreatureSpawnPoint p)
		{
			string id = ObjectProps.Get(p.Props, ObjectProps.CreatureZone);
			if (id.Length == 0) return false;
			TriggerZone zone = root.GetComponentsInChildren<TriggerZone>(true).FirstOrDefault(z => z.Id == id);
			return zone != null && !ContentState.IsUsed(entry, zone.StateKey);
		}

		/// <summary>How many animals a spawn point should have now: all of them, or what is left of them unless they have grown back.</summary>
		static int HowManyNow(IslandWorldState.Entry entry, CreatureSpawnPoint p)
		{
			int count = ObjectProps.Count(p.Props);
			ObjectState s;
			if (!entry.State.TryGetValue(p.StateKey, out s)) return count;
			int days = IslandRules.RegrowDays(entry);
			if (ObjectProps.Respawns(p.Props) && days > 0 && Today - s.Day >= days)
			{
				entry.State.Remove(p.StateKey);
				return count;
			}
			return Mathf.Clamp(s.Yield, 0, count);
		}

		static AI_NetworkBehaviour Prefab(Network_Host_Entities host, AI_NetworkBehaviourType type)
		{
			AI_NetworkBehaviour[] list = host.AINetworkBehaviourPrefabs;
			return list != null ? list.FirstOrDefault(b => b != null && b.behaviourType == type) : null;
		}

		/// <summary>Spawns one live animal at a spawn point (index i of n: a herd stands in a ring around the point).</summary>
		public static AI_NetworkBehaviour Spawn(Network_Host_Entities host, CreatureSpawnPoint p, int i, int n)
		{
			try
			{
				AI_NetworkBehaviour prefab = Prefab(host, p.Kind.Type);
				if (prefab == null) { Debug.LogWarning("[CUSTOM ISLANDS] Raft has no " + p.Kind.Type + " to spawn"); return null; }

				Vector3 pos = p.transform.position;
				if (n > 1)
				{
					float a = i * Mathf.PI * 2f / n, r = HerdRadius * Mathf.Max(1f, ObjectProps.Size(p.Props));
					pos += new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r;
				}
				if (prefab.GetComponentInChildren<NavMeshAgent>(true) != null)
				{
					NavMeshHit hit;
					if (NavMesh.SamplePosition(pos, out hit, 8f, NavMesh.AllAreas)) pos = hit.position;
					else if (NavMesh.SamplePosition(p.transform.position, out hit, 8f, NavMesh.AllAreas)) pos = hit.position;
					else Debug.LogWarning("[CUSTOM ISLANDS] No walkable ground near the " + p.Kind.Label + " spawn point at " + p.transform.position);
				}

				// The animal's home: a switched-off spawner at its spawn point (see the class comment)
				var home = new GameObject("Home_" + i);
				home.SetActive(false);
				home.transform.SetParent(p.transform, false);
				home.transform.position = pos;
				home.transform.rotation = p.transform.rotation;
				LandmarkEntitySpawner spawner = home.AddComponent<LandmarkEntitySpawner>();
				spawner.entityType = p.Kind.Type;
				spawner.setStartRotation = true;

				float scale = -1f;
				try { scale = prefab.localScaleInterval.GetRandomValue(); } catch { }
				if (scale <= 0f) scale = 1f;
				scale *= ObjectProps.Size(p.Props);

				AI_NetworkBehaviour ai = host.CreateAINetworkBehaviour(p.Kind.Type, pos, scale, SaveAndLoad.GetUniqueObjectIndex(), SaveAndLoad.GetUniqueObjectIndex(), spawner);
				if (ai == null) return null;
				spawner.spawnedEntityBehaviour = ai;
				ours.Add(ai);
				p.Spawned.Add(ai);
				ApplyStats(ai, p.Props);
				ObjectProps.ApplyTint(ai.gameObject, p.Props);

				// Tell the other players, as Raft's own CreateAINetworkBehaviour does (without the spawner: it isn't a landmark's)
				Raft_Network network = ComponentManager<Raft_Network>.Value;
				if (network != null)
				{
					var msg = new Message_CreateAINetworkBehaviour(Messages.CreateAINetworkBehaviour, network.NetworkIDManager, host.ObjectIndex, pos, ai, null);
					network.RPC(msg, Target.Other, Steamworks.EP2PSend.k_EP2PSendReliable, NetworkChannel.Channel_Game);
				}
				return ai;
			}
			catch (Exception e)
			{
				Debug.LogError("[CUSTOM ISLANDS] Spawning a " + (p.Kind != null ? p.Kind.Label : "creature") + " failed: " + e);
				return null;
			}
		}

		#endregion

		#region Stats

		/// <summary>Host: the builder's health, damage and speed for a freshly spawned animal (Raft's values times the multipliers).</summary>
		public static void ApplyStats(AI_NetworkBehaviour ai, IDictionary<string, string> props)
		{
			float hp = ObjectProps.Health(props), dmg = ObjectProps.Damage(props), spd = ObjectProps.Speed(props);
			if (!Mathf.Approximately(hp, 1f)) DynamicIslands.instance.StartCoroutine(ApplyHealth(ai, hp));
			if (!Mathf.Approximately(dmg, 1f)) ScaleDamage(ai.gameObject, dmg);
			if (!Mathf.Approximately(spd, 1f))
				foreach (AI_Movement m in ai.GetComponentsInChildren<AI_Movement>(true)) speed[m] = spd;
		}

		/// <summary>Raft's own maximum health of animals whose health was changed (the automated tests compare with it).</summary>
		public static readonly Dictionary<AI_NetworkBehaviour, float> HealthBefore = new Dictionary<AI_NetworkBehaviour, float>();

		/// <summary>Sets the health now and again after the animal's first frames (in case its own start-up sets it again).</summary>
		static IEnumerator ApplyHealth(AI_NetworkBehaviour ai, float multiplier)
		{
			float applied = -1f;
			for (int frame = 0; frame < 3; frame++)
			{
				if (ai == null || ai.networkEntity == null || ai.networkEntity.stat_health == null) yield break;
				Stat_Health h = ai.networkEntity.stat_health;
				if (!Mathf.Approximately(h.Max, applied) && h.Max > 0f)
				{
					HealthBefore[ai] = h.Max;
					applied = h.Max * multiplier;
					h.SetMaxValue(applied);
					h.SetToMaxValue();
				}
				yield return null;
			}
		}

		static readonly string[] NotDamage = { "taken", "threshold", "treshold", "range", "radius", "frequency", "time", "particle", "state", "reached", "sound", "box", "event", "cooldown", "delay" };

		/// <summary>
		/// Multiplies the damage an animal deals: Raft keeps it in the animal's own attack states and damage boxes
		/// (fields like attackDamage, chargeDamage, explosionDamage, damage). Only this animal's copies are changed.
		/// Returns the names of the fields that were scaled.
		/// </summary>
		public static List<string> ScaleDamage(GameObject root, float multiplier)
		{
			var scaled = new List<string>();
			foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
			{
				if (mb == null) continue;
				Type t = mb.GetType();
				if (!(t.Name.StartsWith("AI_State") || typeof(DamageBox).IsAssignableFrom(t))) continue;
				for (Type c = t; c != null && c != typeof(MonoBehaviour); c = c.BaseType)
					foreach (FieldInfo f in c.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
					{
						string n = f.Name.ToLowerInvariant();
						if (!n.Contains("damage") || NotDamage.Any(n.Contains)) continue;
						if (f.FieldType == typeof(float)) f.SetValue(mb, (float)f.GetValue(mb) * multiplier);
						else if (f.FieldType == typeof(int)) f.SetValue(mb, Mathf.RoundToInt((int)f.GetValue(mb) * multiplier));
						else continue;
						scaled.Add(t.Name + "." + f.Name);
					}
			}
			return scaled;
		}

		public static float SpeedOf(AI_Movement m)
		{
			float s;
			return m != null && speed.TryGetValue(m, out s) ? s : 1f;
		}

		#endregion

		#region NavMesh

		/// <summary>
		/// Builds a NavMesh per kind of agent from the island's colliders (terrain and objects), in the background.
		/// Raft's object meshes can't be read at runtime, so those colliders count as their bounding boxes. The data
		/// is registered through a NavMeshSurface on the island, which moves it along with Raft's world shifts.
		/// </summary>
		static IEnumerator BuildNavMesh(GameObject root, IEnumerable<int> agentTypes)
		{
			// The island was made this frame: let physics catch up with where its colliders were moved to
			yield return new WaitForFixedUpdate();
			if (root == null) yield break;
			var sources = new List<NavMeshBuildSource>();
			Bounds local = new Bounds();
			try
			{
				Physics.SyncTransforms();
				NavMeshBuilder.CollectSources(root.transform, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
				for (int i = 0; i < sources.Count; i++)
				{
					NavMeshBuildSource s = sources[i];
					Mesh mesh = s.shape == NavMeshBuildSourceShape.Mesh ? s.sourceObject as Mesh : null;
					if (mesh == null || mesh.isReadable) continue;
					s.shape = NavMeshBuildSourceShape.Box;
					s.size = mesh.bounds.size;
					s.transform = s.transform * Matrix4x4.Translate(mesh.bounds.center);
					s.sourceObject = null;
					sources[i] = s;
				}
				// Everything with a collider, relative to the island (whose root is never turned)
				bool any = false;
				foreach (Collider c in root.GetComponentsInChildren<Collider>())
				{
					if (c.isTrigger) continue;
					Bounds b = c.bounds;
					b.center -= root.transform.position;
					if (!any) { local = b; any = true; } else local.Encapsulate(b);
				}
				local.Expand(4f);
				Debug.Log("[CUSTOM ISLANDS] NavMesh sources: " + sources.Count + " (" + sources.Count(s => s.shape == NavMeshBuildSourceShape.Terrain) + " terrain), area " + local.min.ToString("F0") + " to " + local.max.ToString("F0") + " around the island's corner");
			}
			catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Collecting the island's ground for its NavMesh failed: " + e); yield break; }

			foreach (int agentType in agentTypes)
			{
				if (root == null) yield break;
				AsyncOperation op = null;
				try
				{
					NavMeshBuildSettings settings = NavMesh.GetSettingsByID(agentType);
					// A little coarser than Unity's default: much quicker for a whole island, still fine for animals
					settings.overrideVoxelSize = true;
					settings.voxelSize = Mathf.Max(0.2f, settings.agentRadius / 2f);
					var data = new NavMeshData(agentType) { position = root.transform.position, rotation = root.transform.rotation };
					NavMeshSurface surface = root.AddComponent<NavMeshSurface>();
					surface.agentTypeID = agentType;
					surface.navMeshData = data;
					surface.AddData();
					op = NavMeshBuilder.UpdateNavMeshDataAsync(data, settings, sources, local);
				}
				catch (Exception e) { Debug.LogError("[CUSTOM ISLANDS] Building the island's NavMesh failed: " + e); }
				float until = Time.realtimeSinceStartup + 60f;
				while (op != null && !op.isDone && root != null)
				{
					if (Time.realtimeSinceStartup > until) { Debug.LogWarning("[CUSTOM ISLANDS] The island's NavMesh is taking more than a minute; spawning anyway"); break; }
					yield return null;
				}
			}
		}

		#endregion

		#region Watching the animals (host) and colouring them (clients)

		/// <summary>Every frame from the mod: once a second, records killed and caught animals.</summary>
		public static void Tick()
		{
			if (Time.unscaledTime < nextTick) return;
			nextTick = Time.unscaledTime + 1f;
			if (!LoadSceneManager.IsGameSceneLoaded) return;
			if (Raft_Network.IsHost) Watch();
			else TintRemote();
		}

		static void Watch()
		{
			foreach (IslandWorldState.Entry e in IslandWorldState.Islands)
			{
				if (e.Root == null) continue;
				foreach (CreatureSpawnPoint p in e.Root.GetComponentsInChildren<CreatureSpawnPoint>(true))
				{
					if (p.RecordedAlive < 0) continue; // not spawned yet
					for (int i = p.Spawned.Count - 1; i >= 0; i--)
					{
						AI_NetworkBehaviour ai = p.Spawned[i];
						if (ai == null) { p.Spawned.RemoveAt(i); continue; }
						// Caught (Raft lets go of it when it is carried): it belongs to the players now
						if (ai.connectedSpawner == null)
						{
							p.Spawned.RemoveAt(i);
							ours.Remove(ai);
							Debug.Log("[CUSTOM ISLANDS] A " + p.Kind.Label + " of '" + e.HostName + "' was caught");
						}
					}
					int alive = p.Spawned.Count(ai => ai != null && ai.networkEntity != null && !ai.networkEntity.IsDead);
					if (alive != p.RecordedAlive) Record(e, p, alive);
				}
			}
		}

		static void Record(IslandWorldState.Entry e, CreatureSpawnPoint p, int alive)
		{
			p.RecordedAlive = alive;
			if (alive >= ObjectProps.Count(p.Props)) e.State.Remove(p.StateKey);
			else e.State[p.StateKey] = new ObjectState { Active = alive > 0, Yield = alive, Day = Today };
		}

		/// <summary>Host: an island is being unloaded or removed; its wild animals (and their bodies) go with it.</summary>
		public static void OnIslandDespawned(GameObject root)
		{
			if (root == null || !Raft_Network.IsHost) return;
			int removed = 0;
			foreach (CreatureSpawnPoint p in root.GetComponentsInChildren<CreatureSpawnPoint>(true))
			{
				foreach (AI_NetworkBehaviour ai in p.Spawned)
				{
					if (ai == null || ai.connectedSpawner == null) continue;
					ours.Remove(ai);
					foreach (AI_Movement m in ai.GetComponentsInChildren<AI_Movement>(true)) speed.Remove(m);
					try { NetworkIDManager.SendIDBehaviourDead(ai.ObjectIndex, typeof(AI_NetworkBehaviour), true); removed++; }
					catch (Exception ex) { Debug.LogWarning("[CUSTOM ISLANDS] Removing a " + p.Kind.Label + ": " + ex.Message); UnityEngine.Object.Destroy(ai.gameObject); }
				}
				p.Spawned.Clear();
			}
			if (removed > 0) Debug.Log("[CUSTOM ISLANDS] Removed " + removed + " creature(s) with their island");
		}

		static readonly HashSet<AI_NetworkBehaviour> clientTinted = new HashSet<AI_NetworkBehaviour>();

		/// <summary>
		/// Clients get the animals from Raft's networking without their colour: each tinted spawn point of a loaded
		/// island colours the nearest untinted animal of its kind near it (animals stay near their spawn point).
		/// </summary>
		static void TintRemote()
		{
			List<CreatureSpawnPoint> tinted = IslandSpawner.SpawnedRoots.Where(r => r != null)
				.SelectMany(r => r.GetComponentsInChildren<CreatureSpawnPoint>(true)).Where(p => p.Kind != null && ObjectProps.HasTint(p.Props)).ToList();
			if (tinted.Count == 0) return;
			AI_NetworkBehaviour[] all = UnityEngine.Object.FindObjectsOfType<AI_NetworkBehaviour>();
			foreach (AI_NetworkBehaviour ai in all)
			{
				if (ai == null || clientTinted.Contains(ai) || ai.connectedSpawner != null) continue;
				CreatureSpawnPoint best = null;
				float bestDist = 40f;
				foreach (CreatureSpawnPoint p in tinted)
				{
					if (p.Kind.Type != ai.behaviourType) continue;
					float d = Vector3.Distance(p.transform.position, ai.transform.position);
					if (d < bestDist) { bestDist = d; best = p; }
				}
				if (best == null) continue;
				ObjectProps.ApplyTint(ai.gameObject, best.Props);
				clientTinted.Add(ai);
			}
			clientTinted.RemoveWhere(a => a == null);
		}

		#endregion

		#region Harmony patches (applied by hand, so a Raft update that renames something only disables that part)

		public static void Patch(Harmony harmony)
		{
			Try(harmony, typeof(AI_Movement), "SetMovementSpeed", "SetSpeedPrefix", null);
			Try(harmony, typeof(AI_Movement), "ChangeMovementSpeedTowards", "TargetPrefix", "GuardPostfix");
			Try(harmony, typeof(AI_Movement), "LerpMovementSpeedTowards", "TargetPrefix", "GuardPostfix");
			Try(harmony, typeof(AI_Movement), "HandleLerpData", "GuardPrefix", "GuardPostfix");
			Try(harmony, typeof(AI_NetworkBehaviour_Animal), "Serialize_CreateFromIDManager", null, "CreateForJoinersPostfix");
		}

		static void Try(Harmony harmony, Type type, string method, string prefix, string postfix)
		{
			try
			{
				MethodInfo original = AccessTools.Method(type, method);
				if (original == null) { Debug.LogWarning("[CUSTOM ISLANDS] " + type.Name + "." + method + " not found: creature settings may not fully apply"); return; }
				harmony.Patch(original,
					prefix != null ? new HarmonyMethod(typeof(CreatureSpawner).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic)) : null,
					postfix != null ? new HarmonyMethod(typeof(CreatureSpawner).GetMethod(postfix, BindingFlags.Static | BindingFlags.NonPublic)) : null);
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Could not patch " + type.Name + "." + method + ": " + e.Message); }
		}

		// Speed: every speed an animal's states ask for is multiplied once. Moving towards a target speed (or lerping)
		// multiplies the target, and the steps it takes on the way are left alone.
		[ThreadStatic] static int guard;

		static void SetSpeedPrefix(AI_Movement __instance, ref float value) { if (guard == 0) value *= SpeedOf(__instance); }
		static void TargetPrefix(AI_Movement __instance, ref float target) { target *= SpeedOf(__instance); guard++; }
		static void GuardPrefix() { guard++; }
		static void GuardPostfix() { if (guard > 0) guard--; }

		/// <summary>
		/// Raft sends the animals that exist to a player who joins, except those tied to a landmark's spawner (the
		/// landmark brings them). Ours are tied to our own spawner, so they are sent here instead.
		/// </summary>
		static void CreateForJoinersPostfix(AI_NetworkBehaviour_Animal __instance, List<Message_NetworkBehaviour> msgs)
		{
			try
			{
				if (!IsOurs(__instance) || !__instance.ConnectedToSpawner || msgs == null) return;
				Network_Host_Entities host = HostEntities;
				Raft_Network network = ComponentManager<Raft_Network>.Value;
				if (host == null || network == null) return;
				msgs.Add(new Message_CreateAINetworkBehaviour(Messages.CreateAINetworkBehaviour, network.NetworkIDManager, host.ObjectIndex, __instance.transform.position, __instance, null));
			}
			catch (Exception e) { Debug.LogWarning("[CUSTOM ISLANDS] Sending a creature to a joining player: " + e.Message); }
		}

		#endregion
	}
}
