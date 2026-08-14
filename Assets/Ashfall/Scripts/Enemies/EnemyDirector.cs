using System;
using System.Collections.Generic;
using UnityEngine;
using Ashfall.Core;
using Ashfall.Nav;

namespace Ashfall.Enemies
{
    /// <summary>
    /// Owns every enemy body in the run: pooling, spawn pacing and the alive count that
    /// tells the game director when a round is over.
    ///
    /// The pool is per-archetype and never shrinks. Round 12 wants around fifty bodies
    /// across three archetypes, which is small enough to keep resident and large enough
    /// that instantiating mid-round would be visible.
    /// </summary>
    public class EnemyDirector : MonoBehaviour
    {
        [Serializable]
        public class ArchetypePrefab
        {
            public EnemyArchetype archetype;
            public EnemyDefinition definition;
            public GameObject prefab;
        }

        [Header("Content")]
        [SerializeField] private List<ArchetypePrefab> archetypes = new();
        [SerializeField] private List<EnemySpawnPoint> spawnPoints = new();

        [Header("Pooling")]
        [SerializeField] private int prewarmPerArchetype = 8;
        [SerializeField] private Transform poolRoot;

        [Header("Safety")]
        [Tooltip("Absolute cap on simultaneous live enemies, independent of round pacing.")]
        [SerializeField] private int hardConcurrentCap = 24;

        /// <summary>(enemy, killer) -- fired the moment health hits zero.</summary>
        public event Action<EnemyBrain, GameObject> EnemyKilled;

        public event Action<int> AliveCountChanged;

        private readonly Dictionary<EnemyArchetype, Queue<EnemyBrain>> _pools = new();
        private readonly Dictionary<EnemyArchetype, ArchetypePrefab> _byArchetype = new();
        private readonly List<EnemyBrain> _live = new(48);
        private readonly List<EnemySpawnPoint> _usable = new(16);

        private Transform _playerTransform;
        private MapPhase _phase = MapPhase.Standby;

        public int AliveCount => _live.Count;
        public IReadOnlyList<EnemyBrain> Live => _live;
        public IReadOnlyList<EnemySpawnPoint> SpawnPoints => spawnPoints;

        private void Awake()
        {
            if (poolRoot == null)
            {
                poolRoot = new GameObject("Enemy Pool").transform;
                poolRoot.SetParent(transform, false);
            }

            for (int i = 0; i < archetypes.Count; i++)
            {
                ArchetypePrefab entry = archetypes[i];
                if (entry?.prefab == null)
                {
                    continue;
                }

                _byArchetype[entry.archetype] = entry;
                _pools[entry.archetype] = new Queue<EnemyBrain>(prewarmPerArchetype * 2);
            }
        }

        private void Start()
        {
            Prewarm();
            SetPhase(_phase);
        }

        public void Configure(List<ArchetypePrefab> prefabs, List<EnemySpawnPoint> points)
        {
            archetypes = prefabs;
            spawnPoints = points;
        }

        public void SetPlayer(Transform player)
        {
            _playerTransform = player;
            for (int i = 0; i < _live.Count; i++)
            {
                _live[i].SetTarget(player);
            }
        }

        public void SetPhase(MapPhase phase)
        {
            _phase = phase;
            for (int i = 0; i < spawnPoints.Count; i++)
            {
                spawnPoints[i]?.SetPhase(phase);
            }
        }

        public EnemyDefinition DefinitionFor(EnemyArchetype archetype)
        {
            if (_byArchetype.TryGetValue(archetype, out ArchetypePrefab entry))
            {
                return entry.definition;
            }

            // The lookup is built in Awake, so fall back to the serialized list. This is
            // what lets editor tooling inspect the configuration without entering play
            // mode, and it costs nothing at runtime because the dictionary hits first.
            for (int i = 0; i < archetypes.Count; i++)
            {
                if (archetypes[i] != null && archetypes[i].archetype == archetype)
                {
                    return archetypes[i].definition;
                }
            }

            return null;
        }

        private void Prewarm()
        {
            foreach (KeyValuePair<EnemyArchetype, ArchetypePrefab> pair in _byArchetype)
            {
                int count = pair.Key == EnemyArchetype.StormBrute
                    ? Mathf.Max(3, prewarmPerArchetype / 2)
                    : prewarmPerArchetype;

                for (int i = 0; i < count; i++)
                {
                    EnemyBrain brain = CreateInstance(pair.Value);
                    if (brain != null)
                    {
                        Recycle(brain);
                    }
                }
            }
        }

        private EnemyBrain CreateInstance(ArchetypePrefab entry)
        {
            GameObject go = Instantiate(entry.prefab, poolRoot);
            go.SetActive(false);

            var brain = go.GetComponent<EnemyBrain>();
            if (brain == null)
            {
                Destroy(go);
                return null;
            }

            var health = go.GetComponent<EnemyHealth>();
            if (health != null)
            {
                health.Died += HandleDied;
                health.DeathFinished += HandleDeathFinished;
            }

            return brain;
        }

        /// <summary>
        /// Spawns one enemy of the requested archetype. Returns null when the hard cap
        /// is reached or no spawn point is currently usable.
        /// </summary>
        public EnemyBrain Spawn(EnemyArchetype archetype, float healthScale, float speedScale, float damageScale, StationZone? preferredZone = null)
        {
            if (_live.Count >= hardConcurrentCap)
            {
                return null;
            }

            if (!_byArchetype.TryGetValue(archetype, out ArchetypePrefab entry) || entry.definition == null)
            {
                return null;
            }

            EnemySpawnPoint point = PickSpawnPoint(preferredZone);
            if (point == null)
            {
                return null;
            }

            EnemyBrain brain = Rent(archetype, entry);
            if (brain == null)
            {
                return null;
            }

            Vector3 position = point.SamplePosition();
            var agent = brain.GetComponent<SteeringAgent>();

            brain.gameObject.SetActive(true);
            brain.PrepareForReuse();

            if (agent != null)
            {
                agent.Warp(position);
            }
            else
            {
                brain.transform.position = position;
            }

            // Face into the map so the entrance reads as deliberate.
            Vector3 facing = _playerTransform != null
                ? (_playerTransform.position - position)
                : point.transform.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.001f)
            {
                brain.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
            }

            brain.Spawn(entry.definition, _playerTransform, healthScale, speedScale, damageScale);

            _live.Add(brain);
            AliveCountChanged?.Invoke(_live.Count);
            return brain;
        }

        private EnemySpawnPoint PickSpawnPoint(StationZone? preferredZone)
        {
            _usable.Clear();
            Vector3 playerPosition = _playerTransform != null ? _playerTransform.position : Vector3.zero;

            for (int i = 0; i < spawnPoints.Count; i++)
            {
                EnemySpawnPoint point = spawnPoints[i];
                if (point != null && point.IsUsable(playerPosition))
                {
                    _usable.Add(point);
                }
            }

            if (_usable.Count == 0)
            {
                // Relax the distance rule rather than stalling the round entirely.
                for (int i = 0; i < spawnPoints.Count; i++)
                {
                    EnemySpawnPoint point = spawnPoints[i];
                    if (point != null && point.PhaseUnlocked)
                    {
                        _usable.Add(point);
                    }
                }
            }

            if (_usable.Count == 0)
            {
                return null;
            }

            float total = 0f;
            for (int i = 0; i < _usable.Count; i++)
            {
                float w = _usable[i].Weight;
                if (preferredZone.HasValue && _usable[i].Zone == preferredZone.Value)
                {
                    w *= 3f;
                }

                total += w;
            }

            float roll = UnityEngine.Random.value * total;
            for (int i = 0; i < _usable.Count; i++)
            {
                float w = _usable[i].Weight;
                if (preferredZone.HasValue && _usable[i].Zone == preferredZone.Value)
                {
                    w *= 3f;
                }

                roll -= w;
                if (roll <= 0f)
                {
                    return _usable[i];
                }
            }

            return _usable[_usable.Count - 1];
        }

        private EnemyBrain Rent(EnemyArchetype archetype, ArchetypePrefab entry)
        {
            if (!_pools.TryGetValue(archetype, out Queue<EnemyBrain> pool))
            {
                pool = new Queue<EnemyBrain>(8);
                _pools[archetype] = pool;
            }

            while (pool.Count > 0)
            {
                EnemyBrain candidate = pool.Dequeue();
                if (candidate != null)
                {
                    return candidate;
                }
            }

            return CreateInstance(entry);
        }

        private void Recycle(EnemyBrain brain)
        {
            if (brain == null)
            {
                return;
            }

            brain.gameObject.SetActive(false);
            brain.transform.SetParent(poolRoot, false);
            brain.PrepareForReuse();

            EnemyArchetype archetype = brain.Definition != null ? brain.Definition.archetype : EnemyArchetype.Shambler;
            if (!_pools.TryGetValue(archetype, out Queue<EnemyBrain> pool))
            {
                pool = new Queue<EnemyBrain>(8);
                _pools[archetype] = pool;
            }

            pool.Enqueue(brain);
        }

        private void HandleDied(EnemyHealth health, GameObject killer)
        {
            var brain = health.GetComponent<EnemyBrain>();
            if (brain == null)
            {
                return;
            }

            if (_live.Remove(brain))
            {
                AliveCountChanged?.Invoke(_live.Count);
            }

            EnemyKilled?.Invoke(brain, killer);
        }

        private void HandleDeathFinished(EnemyHealth health)
        {
            var brain = health.GetComponent<EnemyBrain>();
            if (brain != null)
            {
                Recycle(brain);
            }
        }

        /// <summary>Clears the field instantly. Used on defeat and restart.</summary>
        public void DespawnAll()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                EnemyBrain brain = _live[i];
                if (brain == null)
                {
                    continue;
                }

                var health = brain.GetComponent<EnemyHealth>();
                health?.KillSilently();
                Recycle(brain);
            }

            _live.Clear();
            AliveCountChanged?.Invoke(0);
        }

        /// <summary>Nearest live enemy to a point, or null. Used for power-up placement.</summary>
        public EnemyBrain NearestLive(Vector3 position)
        {
            EnemyBrain best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < _live.Count; i++)
            {
                if (_live[i] == null)
                {
                    continue;
                }

                float sqr = (_live[i].transform.position - position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = _live[i];
                }
            }

            return best;
        }
    }
}
