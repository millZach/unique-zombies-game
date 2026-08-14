using System;
using System.Collections.Generic;
using UnityEngine;
using Ashfall.Core;
using Ashfall.Player;

namespace Ashfall.World
{
    /// <summary>
    /// Drops, collects and expires power-ups, and owns the timers for their effects.
    ///
    /// Effects are applied by writing to a single multiplier on the owning system
    /// (loadout damage, wallet earn rate, health's Last Stand flag) and cleared when the
    /// timer runs out. Nothing else in the game needs to know a power-up exists.
    /// </summary>
    public class PowerUpManager : MonoBehaviour
    {
        [Serializable]
        public class PowerUpTuning
        {
            public PowerUpKind kind;
            public float durationSeconds = 20f;
            public float magnitude = 2f;
        }

        [Header("Content")]
        [SerializeField] private GameObject pickupPrefab;
        [SerializeField] private List<PowerUpTuning> tuning = new();
        [SerializeField] private int poolSize = 6;

        [Header("References")]
        [SerializeField] private PlayerLoadout loadout;
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private SalvageWallet wallet;
        [SerializeField] private Transform playerTransform;

        [Header("Placement")]
        [SerializeField] private float dropHeightProbe = 4f;

        /// <summary>(kind, duration) -- raised the moment a power-up is picked up.</summary>
        public event Action<PowerUpKind, float> PowerUpActivated;

        public event Action<PowerUpKind> PowerUpExpired;
        public event Action<PowerUpKind, Vector3> PowerUpDropped;

        private readonly Queue<PowerUpPickup> _pool = new();
        private readonly List<PowerUpPickup> _live = new(8);
        private readonly Dictionary<PowerUpKind, float> _activeUntil = new();
        private Transform _poolRoot;

        public IReadOnlyDictionary<PowerUpKind, float> ActiveUntil => _activeUntil;

        private void Awake()
        {
            _poolRoot = new GameObject("Power-Up Pool").transform;
            _poolRoot.SetParent(transform, false);

            if (tuning.Count == 0)
            {
                tuning.Add(new PowerUpTuning { kind = PowerUpKind.Overcharge, durationSeconds = 20f, magnitude = 2.25f });
                tuning.Add(new PowerUpTuning { kind = PowerUpKind.SalvageSurge, durationSeconds = 25f, magnitude = 2f });
                tuning.Add(new PowerUpTuning { kind = PowerUpKind.LastStand, durationSeconds = 28f, magnitude = 1f });
            }
        }

        private void Start()
        {
            loadout ??= FindFirstObjectByType<PlayerLoadout>();
            playerHealth ??= FindFirstObjectByType<PlayerHealth>();
            wallet ??= FindFirstObjectByType<SalvageWallet>();
            playerTransform ??= playerHealth != null ? playerHealth.transform : null;

            Prewarm();
        }

        public void Configure(GameObject prefab, PlayerLoadout playerLoadout, PlayerHealth health, SalvageWallet salvageWallet)
        {
            pickupPrefab = prefab;
            loadout = playerLoadout;
            playerHealth = health;
            wallet = salvageWallet;
            playerTransform = health != null ? health.transform : null;
        }

        private void Prewarm()
        {
            if (pickupPrefab == null)
            {
                return;
            }

            for (int i = 0; i < poolSize; i++)
            {
                _pool.Enqueue(CreatePickup());
            }
        }

        private PowerUpPickup CreatePickup()
        {
            GameObject go = Instantiate(pickupPrefab, _poolRoot);
            go.SetActive(false);
            var pickup = go.GetComponent<PowerUpPickup>();
            if (pickup != null)
            {
                pickup.Collected += HandleCollected;
                pickup.Expired += HandleExpired;
            }

            return pickup;
        }

        public PowerUpTuning TuningFor(PowerUpKind kind)
        {
            for (int i = 0; i < tuning.Count; i++)
            {
                if (tuning[i].kind == kind)
                {
                    return tuning[i];
                }
            }

            return new PowerUpTuning { kind = kind, durationSeconds = 20f, magnitude = 2f };
        }

        /// <summary>Drops a random power-up at a world position.</summary>
        public void DropRandom(Vector3 position)
        {
            var kinds = (PowerUpKind[])Enum.GetValues(typeof(PowerUpKind));
            Drop(kinds[UnityEngine.Random.Range(0, kinds.Length)], position);
        }

        public void Drop(PowerUpKind kind, Vector3 position)
        {
            if (pickupPrefab == null)
            {
                return;
            }

            PowerUpPickup pickup = _pool.Count > 0 ? _pool.Dequeue() : CreatePickup();
            if (pickup == null)
            {
                return;
            }

            // Settle onto the floor beneath the drop point so a kill on a catwalk does
            // not leave a canister floating over the courtyard.
            Vector3 grounded = position;
            if (Physics.Raycast(position + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, dropHeightProbe,
                    AshfallLayers.GroundMask, QueryTriggerInteraction.Ignore))
            {
                grounded = hit.point;
            }

            pickup.Arm(kind, grounded, playerTransform);
            _live.Add(pickup);
            Audio.AudioDirector.Instance?.PlayAt(Audio.AudioCue.PowerUpDrop, grounded);
            PowerUpDropped?.Invoke(kind, grounded);
        }

        private void HandleCollected(PowerUpPickup pickup, PowerUpKind kind)
        {
            _live.Remove(pickup);
            _pool.Enqueue(pickup);
            Activate(kind);
        }

        private void HandleExpired(PowerUpPickup pickup)
        {
            _live.Remove(pickup);
            _pool.Enqueue(pickup);
        }

        public void Activate(PowerUpKind kind)
        {
            PowerUpTuning t = TuningFor(kind);

            // Re-collecting refreshes the timer from now rather than stacking.
            _activeUntil[kind] = Time.time + t.durationSeconds;
            ApplyEffect(kind, t, true);
            Audio.AudioDirector.Instance?.Play2D(Audio.AudioCue.PowerUpPickup);
            PowerUpActivated?.Invoke(kind, t.durationSeconds);
        }

        private void Update()
        {
            if (_activeUntil.Count == 0)
            {
                return;
            }

            float now = Time.time;
            List<PowerUpKind> finished = null;

            foreach (KeyValuePair<PowerUpKind, float> pair in _activeUntil)
            {
                if (now >= pair.Value)
                {
                    finished ??= new List<PowerUpKind>(3);
                    finished.Add(pair.Key);
                }
            }

            if (finished == null)
            {
                return;
            }

            for (int i = 0; i < finished.Count; i++)
            {
                PowerUpKind kind = finished[i];
                _activeUntil.Remove(kind);
                ApplyEffect(kind, TuningFor(kind), false);
                PowerUpExpired?.Invoke(kind);
            }
        }

        private void ApplyEffect(PowerUpKind kind, PowerUpTuning t, bool on)
        {
            switch (kind)
            {
                case PowerUpKind.Overcharge:
                    if (loadout != null)
                    {
                        loadout.DamageMultiplier = on ? t.magnitude : 1f;
                    }

                    break;

                case PowerUpKind.SalvageSurge:
                    if (wallet != null)
                    {
                        wallet.EarnMultiplier = on ? t.magnitude : 1f;
                    }

                    break;

                case PowerUpKind.LastStand:
                    if (playerHealth != null)
                    {
                        playerHealth.LastStandActive = on;
                    }

                    break;
            }
        }

        public float RemainingSeconds(PowerUpKind kind)
        {
            return _activeUntil.TryGetValue(kind, out float until) ? Mathf.Max(0f, until - Time.time) : 0f;
        }

        public bool IsActive(PowerUpKind kind) => RemainingSeconds(kind) > 0f;

        /// <summary>Clears every live pickup and active effect. Used by restart.</summary>
        public void ResetAll()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                if (_live[i] != null)
                {
                    _live[i].gameObject.SetActive(false);
                    _pool.Enqueue(_live[i]);
                }
            }

            _live.Clear();

            var kinds = new List<PowerUpKind>(_activeUntil.Keys);
            for (int i = 0; i < kinds.Count; i++)
            {
                ApplyEffect(kinds[i], TuningFor(kinds[i]), false);
                PowerUpExpired?.Invoke(kinds[i]);
            }

            _activeUntil.Clear();
        }
    }
}
