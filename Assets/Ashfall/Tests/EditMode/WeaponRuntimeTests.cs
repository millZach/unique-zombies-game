using NUnit.Framework;
using UnityEngine;
using Ashfall.Weapons;

namespace Ashfall.Tests
{
    /// <summary>
    /// Ammo, cadence and damage maths for all three weapons.
    ///
    /// <see cref="WeaponRuntime"/> is a plain class precisely so this can be driven with
    /// a fake clock: every test below advances time by hand and asserts on exact counts.
    /// </summary>
    public class WeaponRuntimeTests
    {
        private WeaponDefinition _sidearm;
        private WeaponDefinition _shotgun;
        private WeaponDefinition _rifle;

        [SetUp]
        public void SetUp()
        {
            _sidearm = ScriptableObject.CreateInstance<WeaponDefinition>();
            WeaponDefinition.ApplyMeridianSidearm(_sidearm);

            _shotgun = ScriptableObject.CreateInstance<WeaponDefinition>();
            WeaponDefinition.ApplyBreakwaterShotgun(_shotgun);

            _rifle = ScriptableObject.CreateInstance<WeaponDefinition>();
            WeaponDefinition.ApplyArc9Rifle(_rifle);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_sidearm);
            Object.DestroyImmediate(_shotgun);
            Object.DestroyImmediate(_rifle);
        }

        /// <summary>Advances the weapon by a number of fixed steps.</summary>
        private static void Advance(WeaponRuntime weapon, float seconds, bool triggerHeld = false, float step = 1f / 60f)
        {
            int steps = Mathf.CeilToInt(seconds / step);
            for (int i = 0; i < steps; i++)
            {
                weapon.Tick(step, triggerHeld, 0f, false);
            }
        }

        // ------------------------------------------------------------------
        // Ammo
        // ------------------------------------------------------------------

        [Test]
        public void WeaponStartsWithAFullMagazine()
        {
            var weapon = new WeaponRuntime(_sidearm);
            Assert.AreEqual(_sidearm.magazineSize, weapon.Magazine);
            Assert.AreEqual(_sidearm.reserveAmmo, weapon.Reserve);
            Assert.AreEqual(WeaponState.Ready, weapon.State);
        }

        [Test]
        public void FiringSpendsExactlyOneRound()
        {
            var weapon = new WeaponRuntime(_sidearm);
            weapon.Tick(0.016f, true, 0f, false);

            Assert.IsTrue(weapon.TryFire(true, true));
            Assert.AreEqual(_sidearm.magazineSize - 1, weapon.Magazine);
            Assert.AreEqual(1, weapon.ShotsFiredTotal);
        }

        [Test]
        public void SemiAutoRequiresAFreshTriggerPullPerShot()
        {
            var weapon = new WeaponRuntime(_sidearm);
            weapon.Tick(0.016f, true, 0f, false);
            Assert.IsTrue(weapon.TryFire(true, true), "First pull should fire.");

            // Holding the trigger past the cooldown must not fire a semi-automatic again.
            Advance(weapon, _sidearm.ShotInterval * 3f, triggerHeld: true);
            Assert.IsFalse(weapon.TryFire(true, false), "Holding must not repeat on a semi-auto.");
            Assert.AreEqual(_sidearm.magazineSize - 1, weapon.Magazine);

            // Releasing and pulling again should.
            weapon.Tick(0.016f, false, 0f, false);
            Assert.IsTrue(weapon.TryFire(true, true));
            Assert.AreEqual(_sidearm.magazineSize - 2, weapon.Magazine);
        }

        [Test]
        public void AutomaticFiresWhileHeldAtItsStatedRate()
        {
            var weapon = new WeaponRuntime(_rifle);
            int shots = 0;
            const float duration = 1f;
            const float step = 1f / 240f;

            for (int i = 0; i < Mathf.RoundToInt(duration / step); i++)
            {
                weapon.Tick(step, true, 0f, false);
                if (weapon.TryFire(true, i == 0))
                {
                    shots++;
                }
            }

            float expected = _rifle.roundsPerMinute / 60f;
            Assert.AreEqual(expected, shots, expected * 0.15f,
                $"Arc-9 fired {shots} rounds in a second; expected about {expected:0.0}.");
        }

        [Test]
        public void CannotFireFasterThanTheCooldown()
        {
            var weapon = new WeaponRuntime(_rifle);
            weapon.Tick(0.001f, true, 0f, false);
            Assert.IsTrue(weapon.TryFire(true, true));
            Assert.IsFalse(weapon.TryFire(true, true), "A second shot on the same frame must be refused.");
        }

        [Test]
        public void FiringAnEmptyMagazineDoesNothing()
        {
            var weapon = new WeaponRuntime(_rifle);
            for (int i = 0; i < _rifle.magazineSize; i++)
            {
                Advance(weapon, _rifle.ShotInterval, triggerHeld: true);
                weapon.TryFire(true, false);
            }

            Assert.AreEqual(0, weapon.Magazine);
            Advance(weapon, 1f, triggerHeld: true);
            Assert.IsFalse(weapon.TryFire(true, true));
            Assert.AreEqual(0, weapon.Magazine);
        }

        // ------------------------------------------------------------------
        // Reloading
        // ------------------------------------------------------------------

        [Test]
        public void ReloadMovesRoundsFromReserveIntoTheMagazine()
        {
            var weapon = new WeaponRuntime(_rifle);
            Advance(weapon, 0.02f);
            weapon.TryFire(true, true);
            Advance(weapon, _rifle.ShotInterval);
            weapon.TryFire(true, true);

            int spent = _rifle.magazineSize - weapon.Magazine;
            Assert.AreEqual(2, spent);

            Assert.IsTrue(weapon.TryBeginReload());
            Assert.AreEqual(WeaponState.Reloading, weapon.State);

            Advance(weapon, _rifle.reloadSeconds + 0.1f);

            Assert.AreEqual(WeaponState.Ready, weapon.State);
            Assert.AreEqual(_rifle.magazineSize, weapon.Magazine);
            Assert.AreEqual(_rifle.reserveAmmo - spent, weapon.Reserve);
        }

        [Test]
        public void ReloadingAFullMagazineIsRefused()
        {
            var weapon = new WeaponRuntime(_sidearm);
            Assert.IsFalse(weapon.CanReload);
            Assert.IsFalse(weapon.TryBeginReload());
        }

        [Test]
        public void ReloadWithoutReserveIsRefused()
        {
            var weapon = new WeaponRuntime(_sidearm);
            weapon.Tick(0.016f, false, 0f, false);
            weapon.TryFire(true, true);

            // Drain the reserve down to nothing.
            while (weapon.Reserve > 0)
            {
                weapon.TryBeginReload();
                Advance(weapon, _sidearm.reloadSeconds + 0.05f);
                Advance(weapon, 0.02f);
                weapon.TryFire(true, true);
            }

            Assert.AreEqual(0, weapon.Reserve);
            Assert.IsFalse(weapon.CanReload);
        }

        [Test]
        public void ShotgunReloadsOneShellAtATime()
        {
            var weapon = new WeaponRuntime(_shotgun);
            Assert.IsTrue(_shotgun.incrementalReload);

            // Empty it.
            for (int i = 0; i < _shotgun.magazineSize; i++)
            {
                Advance(weapon, _shotgun.ShotInterval + 0.02f);
                weapon.Tick(0.016f, false, 0f, false);
                weapon.TryFire(true, true);
            }

            Assert.AreEqual(0, weapon.Magazine);

            Assert.IsTrue(weapon.TryBeginReload());
            Advance(weapon, _shotgun.reloadSeconds + 0.02f);
            Assert.AreEqual(1, weapon.Magazine, "One shell per reload step.");
            Assert.AreEqual(WeaponState.Reloading, weapon.State, "The shotgun keeps loading until full.");

            Advance(weapon, _shotgun.reloadSeconds + 0.02f);
            Assert.AreEqual(2, weapon.Magazine);
        }

        [Test]
        public void FiringInterruptsAShotgunReloadAndKeepsLoadedShells()
        {
            var weapon = new WeaponRuntime(_shotgun);
            for (int i = 0; i < _shotgun.magazineSize; i++)
            {
                Advance(weapon, _shotgun.ShotInterval + 0.02f);
                weapon.Tick(0.016f, false, 0f, false);
                weapon.TryFire(true, true);
            }

            weapon.TryBeginReload();
            Advance(weapon, _shotgun.reloadSeconds + 0.02f);
            Advance(weapon, _shotgun.reloadSeconds + 0.02f);
            Assert.AreEqual(2, weapon.Magazine);

            // Wait out the shot cooldown. The reload keeps ticking while we do -- the
            // shotgun loads faster than it fires -- so read the magazine immediately
            // before pulling the trigger rather than assuming a fixed count.
            Advance(weapon, _shotgun.ShotInterval + 0.02f);
            Assert.AreEqual(WeaponState.Reloading, weapon.State, "The reload should still be running.");

            int loadedBeforeShot = weapon.Magazine;
            Assert.GreaterOrEqual(loadedBeforeShot, 2, "Shells loaded so far must be kept.");

            weapon.Tick(0.016f, false, 0f, false);
            Assert.IsTrue(weapon.TryFire(true, true), "Firing should interrupt a shell-by-shell reload.");
            Assert.AreEqual(loadedBeforeShot - 1, weapon.Magazine, "Firing spends exactly one loaded shell.");
            Assert.AreEqual(WeaponState.Ready, weapon.State, "Firing must cancel the remaining reload.");
        }

        [Test]
        public void EquippingBlocksFiringUntilTheAnimationFinishes()
        {
            var weapon = new WeaponRuntime(_rifle);
            weapon.BeginEquip();
            Assert.AreEqual(WeaponState.Equipping, weapon.State);
            Assert.IsFalse(weapon.TryFire(true, true), "Cannot fire mid-equip.");

            Advance(weapon, _rifle.equipSeconds + 0.05f, triggerHeld: true);
            Assert.AreEqual(WeaponState.Ready, weapon.State);
            Assert.IsTrue(weapon.TryFire(true, true));
        }

        // ------------------------------------------------------------------
        // Damage
        // ------------------------------------------------------------------

        [Test]
        public void DamageIsUnreducedInsideTheFalloffStart()
        {
            Assert.AreEqual(1f, _rifle.FalloffAt(0f), 0.0001f);
            Assert.AreEqual(1f, _rifle.FalloffAt(_rifle.falloffStart), 0.0001f);
        }

        [Test]
        public void DamageBottomsOutAtTheFalloffFloor()
        {
            Assert.AreEqual(_rifle.falloffFloor, _rifle.FalloffAt(_rifle.falloffEnd), 0.0001f);
            Assert.AreEqual(_rifle.falloffFloor, _rifle.FalloffAt(_rifle.falloffEnd + 500f), 0.0001f);
        }

        [Test]
        public void DamageDecaysMonotonicallyAcrossTheFalloffBand()
        {
            float previous = float.MaxValue;
            for (float d = 0f; d <= _rifle.falloffEnd + 10f; d += 2f)
            {
                float current = _rifle.FalloffAt(d);
                Assert.LessOrEqual(current, previous + 0.0001f, $"Falloff rose at {d}m.");
                previous = current;
            }
        }

        [Test]
        public void CriticalHitsApplyTheDefinitionMultiplier()
        {
            float body = _rifle.ResolveDamage(5f, critical: false);
            float head = _rifle.ResolveDamage(5f, critical: true);
            Assert.AreEqual(body * _rifle.criticalMultiplier, head, 0.0001f);
        }

        [Test]
        public void OverchargeScalesDamageLinearly()
        {
            float normal = _rifle.ResolveDamage(5f, critical: false);
            float boosted = _rifle.ResolveDamage(5f, critical: false, damageMultiplier: 2.25f);
            Assert.AreEqual(normal * 2.25f, boosted, 0.0001f);
        }

        [Test]
        public void ShotgunOutDamagesTheRifleUpCloseAndLosesAtRange()
        {
            float closeShotgun = _shotgun.damagePerPellet * _shotgun.pelletsPerShot * _shotgun.FalloffAt(3f);
            float closeRifle = _rifle.damagePerPellet * _rifle.FalloffAt(3f);
            Assert.Greater(closeShotgun, closeRifle * 3f, "Breakwater should dominate at point blank.");

            float farShotgun = _shotgun.damagePerPellet * _shotgun.pelletsPerShot * _shotgun.FalloffAt(40f);
            float farRifleDps = _rifle.damagePerPellet * _rifle.FalloffAt(40f) * (_rifle.roundsPerMinute / 60f);
            float farShotgunDps = farShotgun * (_shotgun.roundsPerMinute / 60f);
            Assert.Less(farShotgunDps, farRifleDps, "Arc-9 should win the long-range trade.");
        }

        [Test]
        public void EachWeaponHasADistinctHandlingProfile()
        {
            Assert.AreNotEqual(_sidearm.fireMode, _rifle.fireMode, "Sidearm and rifle should differ in fire mode.");
            Assert.AreEqual(1, _sidearm.pelletsPerShot);
            Assert.Greater(_shotgun.pelletsPerShot, 1, "The Breakwater must be a spread weapon.");
            Assert.Greater(_shotgun.baseSpreadDegrees, _rifle.baseSpreadDegrees);
            Assert.Greater(_rifle.roundsPerMinute, _sidearm.roundsPerMinute);
            Assert.Greater(_sidearm.roundsPerMinute, _shotgun.roundsPerMinute);
            Assert.Greater(_shotgun.recoilVertical, _rifle.recoilVertical);
        }

        // ------------------------------------------------------------------
        // Spread and reserve
        // ------------------------------------------------------------------

        [Test]
        public void SpreadBloomsWhenFiringAndRecoversWhenIdle()
        {
            var weapon = new WeaponRuntime(_rifle);
            weapon.Tick(0.016f, false, 0f, false);
            float rest = weapon.CurrentSpreadDegrees;

            for (int i = 0; i < 5; i++)
            {
                Advance(weapon, _rifle.ShotInterval, triggerHeld: true);
                weapon.TryFire(true, false);
            }

            Assert.Greater(weapon.CurrentSpreadDegrees, rest, "Spread should bloom while firing.");

            Advance(weapon, 4f);
            Assert.AreEqual(rest, weapon.CurrentSpreadDegrees, 0.05f, "Spread should settle back to rest.");
        }

        [Test]
        public void SpreadNeverExceedsTheDefinitionMaximum()
        {
            var weapon = new WeaponRuntime(_rifle);
            for (int i = 0; i < 200; i++)
            {
                Advance(weapon, _rifle.ShotInterval, triggerHeld: true);
                weapon.TryFire(true, false);
                weapon.AddReserve(10);
                if (weapon.IsEmpty)
                {
                    weapon.TryBeginReload();
                    Advance(weapon, _rifle.reloadSeconds + 0.05f);
                }

                Assert.LessOrEqual(weapon.CurrentSpreadDegrees, _rifle.maxSpreadDegrees + 0.001f);
            }
        }

        [Test]
        public void AimingTightensTheSpreadFloor()
        {
            var hip = new WeaponRuntime(_rifle);
            var aimed = new WeaponRuntime(_rifle);

            Advance(hip, 2f);
            for (int i = 0; i < 120; i++)
            {
                aimed.Tick(1f / 60f, false, 0f, true);
            }

            Assert.Less(aimed.CurrentSpreadDegrees, hip.CurrentSpreadDegrees, "Aiming should tighten the cone.");
        }

        [Test]
        public void ReserveIsClampedToTheCarryLimit()
        {
            var weapon = new WeaponRuntime(_sidearm);
            weapon.AddReserve(100000);
            Assert.AreEqual(_sidearm.maxReserveAmmo, weapon.Reserve);
        }

        [Test]
        public void RefillTopsUpBothMagazineAndReserve()
        {
            var weapon = new WeaponRuntime(_rifle);
            Advance(weapon, 0.02f);
            weapon.TryFire(true, true);
            weapon.Refill();

            Assert.AreEqual(_rifle.magazineSize, weapon.Magazine);
            Assert.AreEqual(_rifle.maxReserveAmmo, weapon.Reserve);
            Assert.AreEqual(_rifle.magazineSize + _rifle.maxReserveAmmo, weapon.TotalAmmo);
        }

        [Test]
        public void SpreadDirectionStaysInsideTheCone()
        {
            var weapon = new WeaponRuntime(_shotgun);
            weapon.Tick(0.016f, false, 0f, false);
            float maxAngle = weapon.CurrentSpreadDegrees + 0.01f;

            for (int i = 0; i < 400; i++)
            {
                Vector3 direction = weapon.SampleSpreadDirection(Vector3.forward, Vector3.right, Vector3.up);
                float angle = Vector3.Angle(Vector3.forward, direction);
                Assert.LessOrEqual(angle, maxAngle, $"Pellet {i} left the cone at {angle:0.00} degrees.");
                Assert.AreEqual(1f, direction.magnitude, 0.001f, "Direction must be normalised.");
            }
        }
    }
}
