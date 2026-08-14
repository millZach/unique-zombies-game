using UnityEngine;

namespace Ashfall.Weapons
{
    public enum WeaponState
    {
        Equipping,
        Ready,
        Reloading
    }

    /// <summary>
    /// The mutable half of a weapon: ammo, cadence, spread bloom and reload progress.
    ///
    /// Deliberately a plain C# class rather than a MonoBehaviour. All of the fiddly
    /// state -- burst counters, incremental shotgun reloads, spread recovery -- is
    /// therefore exercisable in edit-mode tests without a scene, a camera or a frame.
    /// </summary>
    public class WeaponRuntime
    {
        public readonly WeaponDefinition Definition;

        public int Magazine { get; private set; }
        public int Reserve { get; private set; }
        public WeaponState State { get; private set; }
        public float CurrentSpreadDegrees { get; private set; }

        /// <summary>0..1 progress through the current reload step.</summary>
        public float ReloadProgress { get; private set; }

        /// <summary>0..1 progress through the equip animation.</summary>
        public float EquipProgress { get; private set; }

        public int ShotsFiredTotal { get; private set; }

        private float _shotCooldown;
        private float _reloadTimer;
        private float _equipTimer;
        private int _burstRemaining;
        private float _burstTimer;
        private bool _triggerLatched;

        public WeaponRuntime(WeaponDefinition definition, bool startFull = true)
        {
            Definition = definition;
            Magazine = startFull ? definition.magazineSize : 0;
            Reserve = definition.reserveAmmo;
            CurrentSpreadDegrees = definition.baseSpreadDegrees;
            State = WeaponState.Ready;
            EquipProgress = 1f;
        }

        public bool IsEmpty => Magazine <= 0;
        public bool HasReserve => Reserve > 0;
        public bool IsReloading => State == WeaponState.Reloading;
        public bool CanReload => Magazine < Definition.magazineSize && Reserve > 0 && State != WeaponState.Equipping;

        /// <summary>Total rounds carried, magazine included. Used by the HUD and tests.</summary>
        public int TotalAmmo => Magazine + Reserve;

        public void BeginEquip()
        {
            State = WeaponState.Equipping;
            _equipTimer = Definition.equipSeconds;
            EquipProgress = 0f;
            _burstRemaining = 0;
            _triggerLatched = false;
        }

        public void ForceReady()
        {
            State = WeaponState.Ready;
            _equipTimer = 0f;
            EquipProgress = 1f;
            _reloadTimer = 0f;
            ReloadProgress = 0f;
        }

        /// <summary>Called once per frame before any fire attempt.</summary>
        public void Tick(float deltaTime, bool triggerHeld, float movementFactor, bool aiming)
        {
            _shotCooldown = Mathf.Max(0f, _shotCooldown - deltaTime);

            if (!triggerHeld)
            {
                _triggerLatched = false;
            }

            switch (State)
            {
                case WeaponState.Equipping:
                    _equipTimer -= deltaTime;
                    EquipProgress = Definition.equipSeconds <= 0f
                        ? 1f
                        : Mathf.Clamp01(1f - (_equipTimer / Definition.equipSeconds));
                    if (_equipTimer <= 0f)
                    {
                        State = WeaponState.Ready;
                        EquipProgress = 1f;
                    }

                    break;

                case WeaponState.Reloading:
                    TickReload(deltaTime);
                    break;
            }

            TickBurst(deltaTime);
            TickSpread(deltaTime, movementFactor, aiming);
        }

        private void TickReload(float deltaTime)
        {
            _reloadTimer -= deltaTime;
            float step = Mathf.Max(0.01f, Definition.reloadSeconds);
            ReloadProgress = Mathf.Clamp01(1f - (_reloadTimer / step));

            if (_reloadTimer > 0f)
            {
                return;
            }

            if (Definition.incrementalReload)
            {
                int want = Mathf.Min(Definition.shellsPerReloadStep, Definition.magazineSize - Magazine);
                int take = Mathf.Min(want, Reserve);
                Magazine += take;
                Reserve -= take;

                if (Magazine >= Definition.magazineSize || Reserve <= 0)
                {
                    State = WeaponState.Ready;
                    ReloadProgress = 0f;
                }
                else
                {
                    _reloadTimer = Definition.reloadSeconds;
                    ReloadProgress = 0f;
                }
            }
            else
            {
                int want = Definition.magazineSize - Magazine;
                int take = Mathf.Min(want, Reserve);
                Magazine += take;
                Reserve -= take;
                State = WeaponState.Ready;
                ReloadProgress = 0f;
            }
        }

        private void TickBurst(float deltaTime)
        {
            if (_burstRemaining <= 0)
            {
                return;
            }

            _burstTimer -= deltaTime;
        }

        private void TickSpread(float deltaTime, float movementFactor, bool aiming)
        {
            float floor = Definition.baseSpreadDegrees;
            floor *= aiming ? Definition.aimSpreadScale : 1f;
            floor *= Mathf.Lerp(1f, Definition.movementSpreadScale, Mathf.Clamp01(movementFactor));

            float ceiling = Definition.maxSpreadDegrees * (aiming ? Mathf.Max(Definition.aimSpreadScale, 0.35f) : 1f);

            if (CurrentSpreadDegrees > floor)
            {
                CurrentSpreadDegrees = Mathf.Max(
                    floor,
                    CurrentSpreadDegrees - Definition.spreadRecoveryPerSecond * deltaTime);
            }
            else
            {
                CurrentSpreadDegrees = floor;
            }

            CurrentSpreadDegrees = Mathf.Min(CurrentSpreadDegrees, ceiling);
        }

        /// <summary>
        /// Attempts one trigger pull. Returns true when a shot was actually produced,
        /// having already spent ammo, started the cooldown and bloomed the spread.
        /// </summary>
        public bool TryFire(bool triggerHeld, bool triggerPressedThisFrame)
        {
            if (State == WeaponState.Equipping)
            {
                return false;
            }

            if (_shotCooldown > 0f)
            {
                return false;
            }

            bool wantsShot = ResolveTriggerIntent(triggerHeld, triggerPressedThisFrame);
            if (!wantsShot)
            {
                return false;
            }

            if (Magazine < Definition.ammoPerShot)
            {
                _burstRemaining = 0;
                return false;
            }

            // Firing cancels an incremental reload; shells already loaded stay loaded.
            if (State == WeaponState.Reloading)
            {
                if (!Definition.incrementalReload)
                {
                    return false;
                }

                State = WeaponState.Ready;
                ReloadProgress = 0f;
                _reloadTimer = 0f;
            }

            Magazine -= Definition.ammoPerShot;
            ShotsFiredTotal++;
            _shotCooldown = Definition.fireMode == FireMode.Burst && _burstRemaining > 0
                ? Definition.burstSpacing
                : Definition.ShotInterval;

            CurrentSpreadDegrees = Mathf.Min(
                Definition.maxSpreadDegrees,
                CurrentSpreadDegrees + Definition.spreadPerShot);

            if (Definition.fireMode == FireMode.Semi)
            {
                _triggerLatched = true;
            }

            return true;
        }

        private bool ResolveTriggerIntent(bool triggerHeld, bool triggerPressedThisFrame)
        {
            switch (Definition.fireMode)
            {
                case FireMode.Automatic:
                    return triggerHeld;

                case FireMode.Burst:
                    if (_burstRemaining > 0)
                    {
                        if (_burstTimer > 0f)
                        {
                            return false;
                        }

                        _burstRemaining--;
                        _burstTimer = Definition.burstSpacing;
                        return true;
                    }

                    if (triggerPressedThisFrame && !_triggerLatched)
                    {
                        _burstRemaining = Mathf.Max(1, Definition.burstCount) - 1;
                        _burstTimer = Definition.burstSpacing;
                        return true;
                    }

                    return false;

                case FireMode.Semi:
                default:
                    return triggerPressedThisFrame && !_triggerLatched;
            }
        }

        public bool TryBeginReload()
        {
            if (!CanReload || State == WeaponState.Reloading)
            {
                return false;
            }

            State = WeaponState.Reloading;
            _reloadTimer = Definition.reloadSeconds;
            ReloadProgress = 0f;
            _burstRemaining = 0;
            return true;
        }

        public void CancelReload()
        {
            if (State == WeaponState.Reloading)
            {
                State = WeaponState.Ready;
                _reloadTimer = 0f;
                ReloadProgress = 0f;
            }
        }

        /// <summary>Adds reserve ammo, clamped to the definition's carry limit. Returns the amount taken.</summary>
        public int AddReserve(int amount)
        {
            int before = Reserve;
            Reserve = Mathf.Clamp(Reserve + amount, 0, Definition.maxReserveAmmo);
            return Reserve - before;
        }

        /// <summary>Tops the weapon back up to a full magazine and full reserve.</summary>
        public void Refill()
        {
            Magazine = Definition.magazineSize;
            Reserve = Definition.maxReserveAmmo;
            CancelReload();
        }

        /// <summary>A random spread direction within the current cone, around <paramref name="forward"/>.</summary>
        public Vector3 SampleSpreadDirection(Vector3 forward, Vector3 right, Vector3 up)
        {
            float radians = CurrentSpreadDegrees * Mathf.Deg2Rad;
            // Uniform sample over the disc so pellets do not clump at the centre.
            float r = Mathf.Sqrt(Random.value) * Mathf.Tan(radians);
            float theta = Random.value * Mathf.PI * 2f;
            Vector3 offset = right * (r * Mathf.Cos(theta)) + up * (r * Mathf.Sin(theta));
            return (forward + offset).normalized;
        }
    }
}
