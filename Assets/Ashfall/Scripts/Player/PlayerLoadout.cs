using System;
using System.Collections.Generic;
using UnityEngine;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Fx;
using Ashfall.InputLayer;
using Ashfall.Weapons;

namespace Ashfall.Player
{
    /// <summary>
    /// Holds the player's weapons and turns trigger pulls into damage in the world.
    ///
    /// Hitscan rather than projectiles: at these ranges the difference is invisible and
    /// raycasts stay exact under any frame rate, which matters for a game where a
    /// missed shot at round 11 ends the run.
    /// </summary>
    public class PlayerLoadout : MonoBehaviour
    {
        [Serializable]
        public class WeaponSlot
        {
            public WeaponDefinition definition;
            public GameObject viewModelPrefab;

            [NonSerialized] public WeaponRuntime Runtime;
            [NonSerialized] public WeaponViewModel View;
        }

        [Header("Slots")]
        [SerializeField] private List<WeaponSlot> slots = new();
        [SerializeField] private int startingSlotCount = 1;

        [Header("References")]
        [SerializeField] private PlayerCameraRig cameraRig;
        [SerializeField] private Transform weaponParent;

        [Header("Tuning")]
        [SerializeField] private float meleeRange = 2.4f;
        [SerializeField] private LayerMask hitMask = ~0;

        public event Action<WeaponSlot> WeaponChanged;
        public event Action<WeaponRuntime> AmmoChanged;

        /// <summary>Raised when a shot connects with something damageable. (critical, killed)</summary>
        public event Action<bool, bool> HitConfirmed;

        private int _currentIndex = -1;
        private readonly List<int> _unlockedSlots = new();
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];

        /// <summary>Multiplies all outgoing weapon damage. Overcharge writes to this.</summary>
        public float DamageMultiplier { get; set; } = 1f;

        public WeaponSlot CurrentSlot => _currentIndex >= 0 && _currentIndex < slots.Count ? slots[_currentIndex] : null;
        public WeaponRuntime CurrentWeapon => CurrentSlot?.Runtime;
        public WeaponDefinition CurrentDefinition => CurrentSlot?.definition;
        public bool IsAiming { get; private set; }
        public IReadOnlyList<WeaponSlot> Slots => slots;
        public IReadOnlyList<int> UnlockedSlots => _unlockedSlots;

        private void Awake()
        {
            cameraRig ??= GetComponentInChildren<PlayerCameraRig>();
            weaponParent = weaponParent != null ? weaponParent : cameraRig?.WeaponSocket;

            if (hitMask == ~0)
            {
                hitMask = AshfallLayers.WeaponHitMask;
            }
        }

        private void Start()
        {
            InitialiseSlots();
        }

        private void InitialiseSlots()
        {
            _unlockedSlots.Clear();

            for (int i = 0; i < slots.Count; i++)
            {
                WeaponSlot slot = slots[i];
                if (slot?.definition == null)
                {
                    continue;
                }

                slot.Runtime = new WeaponRuntime(slot.definition);

                if (slot.View == null && slot.viewModelPrefab != null && weaponParent != null)
                {
                    GameObject instance = Instantiate(slot.viewModelPrefab, weaponParent);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.localRotation = Quaternion.identity;
                    slot.View = instance.GetComponent<WeaponViewModel>();
                    instance.SetActive(false);
                }

                if (i < startingSlotCount)
                {
                    _unlockedSlots.Add(i);
                }
            }

            if (_unlockedSlots.Count > 0)
            {
                EquipSlot(_unlockedSlots[0], instant: true);
            }
        }

        public void ResetLoadout()
        {
            DamageMultiplier = 1f;
            IsAiming = false;
            _unlockedSlots.Clear();

            for (int i = 0; i < slots.Count; i++)
            {
                WeaponSlot slot = slots[i];
                if (slot?.definition == null)
                {
                    continue;
                }

                slot.Runtime = new WeaponRuntime(slot.definition);
                if (slot.View != null)
                {
                    slot.View.gameObject.SetActive(false);
                }

                if (i < startingSlotCount)
                {
                    _unlockedSlots.Add(i);
                }
            }

            _currentIndex = -1;
            if (_unlockedSlots.Count > 0)
            {
                EquipSlot(_unlockedSlots[0], instant: true);
            }
        }

        public bool IsUnlocked(int slotIndex) => _unlockedSlots.Contains(slotIndex);

        public int IndexOfDefinition(WeaponDefinition definition)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i]?.definition == definition)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Unlocks a purchased weapon, or -- if it is already owned -- refills it instead.
        /// Returns true when something actually happened.
        /// </summary>
        public bool AcquireWeapon(WeaponDefinition definition, out bool wasRefill)
        {
            wasRefill = false;
            int index = IndexOfDefinition(definition);
            if (index < 0)
            {
                return false;
            }

            if (_unlockedSlots.Contains(index))
            {
                slots[index].Runtime?.Refill();
                wasRefill = true;
                if (index == _currentIndex)
                {
                    AmmoChanged?.Invoke(CurrentWeapon);
                }

                return true;
            }

            _unlockedSlots.Add(index);
            _unlockedSlots.Sort();
            slots[index].Runtime?.Refill();
            EquipSlot(index, instant: false);
            return true;
        }

        public void RefillAllAmmo()
        {
            for (int i = 0; i < _unlockedSlots.Count; i++)
            {
                slots[_unlockedSlots[i]].Runtime?.Refill();
            }

            AmmoChanged?.Invoke(CurrentWeapon);
        }

        public void EquipSlot(int index, bool instant)
        {
            if (index < 0 || index >= slots.Count || !_unlockedSlots.Contains(index) || index == _currentIndex)
            {
                return;
            }

            if (CurrentSlot?.View != null)
            {
                CurrentSlot.View.gameObject.SetActive(false);
            }

            CurrentWeapon?.CancelReload();

            _currentIndex = index;
            WeaponSlot slot = slots[index];

            if (slot.View != null)
            {
                slot.View.gameObject.SetActive(true);
            }

            if (instant)
            {
                slot.Runtime?.ForceReady();
            }
            else
            {
                slot.Runtime?.BeginEquip();
                AudioDirector.Instance?.Play2D(AudioCue.WeaponEquip);
            }

            WeaponChanged?.Invoke(slot);
            AmmoChanged?.Invoke(slot.Runtime);
        }

        public void CycleWeapon(int direction)
        {
            if (_unlockedSlots.Count <= 1)
            {
                return;
            }

            int position = _unlockedSlots.IndexOf(_currentIndex);
            if (position < 0)
            {
                position = 0;
            }

            position = (position + direction + _unlockedSlots.Count) % _unlockedSlots.Count;
            EquipSlot(_unlockedSlots[position], instant: false);
        }

        public void Tick(in InputFrame input, float deltaTime, bool allowCombat, float movementFactor)
        {
            WeaponSlot slot = CurrentSlot;
            if (slot?.Runtime == null)
            {
                return;
            }

            WeaponRuntime weapon = slot.Runtime;

            if (allowCombat)
            {
                HandleSwitching(input);
            }

            IsAiming = allowCombat && input.AimHeld && weapon.State != WeaponState.Reloading;

            weapon.Tick(deltaTime, allowCombat && input.FireHeld, movementFactor, IsAiming);

            if (allowCombat)
            {
                if (input.ReloadPressed)
                {
                    BeginReload(slot);
                }

                bool fired = weapon.TryFire(input.FireHeld, input.FirePressed);
                if (fired)
                {
                    FireShot(slot);
                    AmmoChanged?.Invoke(weapon);
                }
                else if (input.FirePressed && weapon.IsEmpty)
                {
                    // Dry fire: start the reload for the player rather than making them
                    // discover the R key while a sprinter is on top of them.
                    AudioDirector.Instance?.Play2D(AudioCue.WeaponDryFire);
                    BeginReload(slot);
                }
            }

            // Auto-reload once the trigger is released on an empty magazine.
            if (weapon.IsEmpty && weapon.HasReserve && !input.FireHeld && weapon.State == WeaponState.Ready)
            {
                BeginReload(slot);
            }

            if (weapon.State == WeaponState.Reloading && weapon.ReloadProgress >= 0.999f)
            {
                AmmoChanged?.Invoke(weapon);
            }

            if (slot.View != null)
            {
                slot.View.Tick(deltaTime, weapon.ReloadProgress, weapon.State == WeaponState.Reloading);
                slot.View.ApplyEquipPose(weapon.EquipProgress);
            }
        }

        /// <summary>
        /// Starts a reload and sounds it, from whichever of the three routes
        /// asked -- the reload key, a dry trigger, or the automatic top-up when
        /// the trigger is released on an empty magazine.
        /// </summary>
        private bool BeginReload(WeaponSlot slot)
        {
            if (slot?.Runtime == null || !slot.Runtime.TryBeginReload())
            {
                return false;
            }

            if (slot.definition != null)
            {
                AudioDirector.Instance?.Play2D(slot.definition.ReloadCue);
            }

            return true;
        }

        private void HandleSwitching(in InputFrame input)
        {
            if (input.NextWeaponPressed)
            {
                CycleWeapon(1);
            }
            else if (input.PreviousWeaponPressed)
            {
                CycleWeapon(-1);
            }
            else if (input.DirectWeaponSlot >= 0)
            {
                EquipSlot(input.DirectWeaponSlot, instant: false);
            }
        }

        private void FireShot(WeaponSlot slot)
        {
            WeaponDefinition def = slot.definition;
            WeaponRuntime weapon = slot.Runtime;
            Camera cam = cameraRig != null ? cameraRig.ViewCamera : Camera.main;
            if (cam == null)
            {
                return;
            }

            Transform camTransform = cam.transform;
            Vector3 origin = camTransform.position;
            Vector3 forward = camTransform.forward;
            Vector3 right = camTransform.right;
            Vector3 up = camTransform.up;

            Vector3 muzzlePoint = slot.View != null ? slot.View.Muzzle.position : origin + forward * 0.5f;

            int pellets = Mathf.Max(1, def.pelletsPerShot);
            bool anyHit = false;
            bool anyCritical = false;
            bool anyKill = false;

            for (int i = 0; i < pellets; i++)
            {
                Vector3 direction = weapon.SampleSpreadDirection(forward, right, up);
                if (TracePellet(def, origin, direction, muzzlePoint, out bool critical, out bool killed))
                {
                    anyHit = true;
                    anyCritical |= critical;
                    anyKill |= killed;
                }
            }

            // --- feedback -------------------------------------------------------
            cameraRig?.AddRecoil(def.recoilVertical, def.recoilHorizontal);
            cameraRig?.AddTrauma(def.cameraShakeAmount * 0.4f);
            cameraRig?.AddKickBack(def.kickBack);
            slot.View?.PlayFire(def.muzzleFlashScale, def.accentColor);
            GamepadRumble.Pulse(def.lowFrequencyRumble, def.highFrequencyRumble, def.rumbleSeconds);

            // 2D: the gun is welded to the camera, and positioning it costs the
            // low end that makes a shot feel like it has recoil.
            AudioDirector.Instance?.Play2D(def.fireCue, def.fireVolume, def.firePitch);

            if (anyHit)
            {
                HitConfirmed?.Invoke(anyCritical, anyKill);
            }
        }

        private bool TracePellet(
            WeaponDefinition def,
            Vector3 origin,
            Vector3 direction,
            Vector3 muzzlePoint,
            out bool critical,
            out bool killed)
        {
            critical = false;
            killed = false;

            // Triggers are included because enemy hitboxes are triggers -- that is what
            // keeps a head shot from being swallowed by the CharacterController capsule
            // enclosing the body. Non-damageable triggers are filtered out below so a
            // storm-exposure volume never eats a bullet.
            int count = Physics.RaycastNonAlloc(
                origin,
                direction,
                _hitBuffer,
                def.maxRange,
                hitMask,
                QueryTriggerInteraction.Collide);

            if (count <= 0)
            {
                FxDirector.Instance?.SpawnTracer(muzzlePoint, origin + direction * def.maxRange, def.tracerColor);
                return false;
            }

            // Nearest valid hit, skipping the player's own colliders.
            int bestIndex = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = _hitBuffer[i];
                if (candidate.collider == null || candidate.collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (candidate.collider.isTrigger && candidate.collider.GetComponentInParent<IDamageable>() == null)
                {
                    continue;
                }

                if (candidate.distance < bestDistance)
                {
                    bestDistance = candidate.distance;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                FxDirector.Instance?.SpawnTracer(muzzlePoint, origin + direction * def.maxRange, def.tracerColor);
                return false;
            }

            RaycastHit hit = _hitBuffer[bestIndex];
            FxDirector.Instance?.SpawnTracer(muzzlePoint, hit.point, def.tracerColor);

            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable == null || !damageable.IsAlive)
            {
                FxDirector.Instance?.SpawnWorldImpact(hit.point, hit.normal, def.accentColor);
                AudioDirector.Instance?.PlayAt(AudioCue.ImpactWorld, hit.point);
                return false;
            }

            var relay = hit.collider.GetComponent<DamageRelay>();
            critical = relay != null && relay.CountsAsCritical;

            float damage = def.ResolveDamage(hit.distance, critical: false, DamageMultiplier);
            var info = DamageInfo.Ballistic(damage, hit.point, direction, hit.normal, critical, gameObject);

            // The relay applies its own hitbox multiplier and crit flag, so pass the
            // un-crit-scaled amount and let it decide.
            if (relay != null)
            {
                relay.ApplyDamage(info);
            }
            else
            {
                damageable.ApplyDamage(info);
            }

            killed = !damageable.IsAlive;
            FxDirector.Instance?.SpawnFleshImpact(hit.point, hit.normal, critical);

            // Nine shotgun pellets landing on one body in one frame is one
            // impact, not nine: the director's per-cue interval collapses them.
            AudioDirector.Instance?.PlayAt(
                critical ? AudioCue.ImpactCritical : AudioCue.ImpactFlesh, hit.point);
            return true;
        }

        /// <summary>Shove-and-damage sweep used when an enemy is inside minimum range.</summary>
        public void MeleeStrike(float damage)
        {
            Camera cam = cameraRig != null ? cameraRig.ViewCamera : Camera.main;
            if (cam == null)
            {
                return;
            }

            Transform camTransform = cam.transform;
            int count = Physics.SphereCastNonAlloc(
                camTransform.position,
                0.55f,
                camTransform.forward,
                _hitBuffer,
                meleeRange,
                hitMask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var damageable = _hitBuffer[i].collider != null
                    ? _hitBuffer[i].collider.GetComponentInParent<IDamageable>()
                    : null;
                if (damageable == null || damageable is PlayerHealth)
                {
                    continue;
                }

                damageable.ApplyDamage(DamageInfo.Melee(damage, _hitBuffer[i].point, camTransform.forward, gameObject));
            }
        }

        public void Configure(PlayerCameraRig rig, Transform socket)
        {
            cameraRig = rig;
            weaponParent = socket;
        }
    }
}
