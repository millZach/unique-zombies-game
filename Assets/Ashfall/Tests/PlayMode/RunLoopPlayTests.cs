using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Ashfall.Core;
using Ashfall.Enemies;
using Ashfall.Player;
using Ashfall.World;

namespace Ashfall.Tests
{
    /// <summary>
    /// Plays the real Main scene for a few seconds and checks that the loop actually
    /// turns over.
    ///
    /// The edit-mode tests prove the maths; this proves the game runs. It is the only
    /// check that would catch a broken prefab reference, a spawner that never fires, or
    /// an exception thrown on the first frame of combat.
    /// </summary>
    public class RunLoopPlayTests
    {
        private const string ScenePath = "Assets/Ashfall/Scenes/Main.unity";

        private float _restoreTimeScale = 1f;

        [OneTimeSetUp]
        public void DisableAutoLoad()
        {
            // Belt and braces alongside the runner-scene guard in the bootstrapper:
            // this fixture loads Main itself, so nothing else should.
            AshfallBootstrap.AutoLoadMainScene = false;
        }

        [OneTimeTearDown]
        public void RestoreAutoLoad()
        {
            AshfallBootstrap.AutoLoadMainScene = true;
        }

        [TearDown]
        public void RestoreTimeScale()
        {
            // A test that fails after pausing would otherwise leave timeScale at zero,
            // and every later WaitForSeconds would hang forever.
            Time.timeScale = _restoreTimeScale;
        }

        [UnitySetUp]
        public IEnumerator LoadMainScene()
        {
            Time.timeScale = 1f;

            AsyncOperation load = SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            while (load != null && !load.isDone)
            {
                yield return null;
            }

            yield return null;
        }

        private static GameDirector Director => Object.FindFirstObjectByType<GameDirector>();

        [UnityTest]
        public IEnumerator SceneBootsWithEverySystemPresent()
        {
            yield return null;

            Assert.IsNotNull(Director, "No GameDirector in the Main scene.");
            Assert.IsNotNull(Object.FindFirstObjectByType<PlayerRig>(), "No PlayerRig.");
            Assert.IsNotNull(Object.FindFirstObjectByType<EnemyDirector>(), "No EnemyDirector.");
            Assert.IsNotNull(Object.FindFirstObjectByType<MapPhaseController>(), "No MapPhaseController.");
            Assert.IsNotNull(Object.FindFirstObjectByType<SalvageWallet>(), "No SalvageWallet.");
            Assert.IsNotNull(Object.FindFirstObjectByType<Nav.NavGraph>(), "No NavGraph.");
            Assert.IsNotNull(Camera.main, "No main camera.");
        }

        [UnityTest]
        public IEnumerator PlayerStartsAliveArmedAndOnTheGround()
        {
            yield return null;
            yield return new WaitForSeconds(0.5f);

            var player = Object.FindFirstObjectByType<PlayerRig>();
            Assert.IsTrue(player.Health.IsAlive);
            Assert.AreEqual(player.Health.MaxHealth, player.Health.Health, 0.01f);

            Assert.IsNotNull(player.Loadout.CurrentWeapon, "Player has no equipped weapon.");
            Assert.Greater(player.Loadout.CurrentWeapon.Magazine, 0, "Starting weapon has no ammo.");
            Assert.AreEqual(1, player.Loadout.UnlockedSlots.Count, "Only the sidearm should start unlocked.");

            // If the spawn point were floating or buried, the controller would still be
            // falling half a second in.
            Assert.IsTrue(player.Motor.IsGrounded, "Player did not settle on the floor after spawning.");
        }

        [UnityTest]
        public IEnumerator RunReachesCombatAndSpawnsEnemies()
        {
            yield return null;

            GameDirector director = Director;
            float timeout = Time.time + 30f;

            while (director.State != GameState.Combat && Time.time < timeout)
            {
                yield return null;
            }

            Assert.AreEqual(GameState.Combat, director.State, "The run never reached combat.");
            Assert.AreEqual(1, director.Round, "Combat should begin on round 1.");
            Assert.Greater(director.RemainingThisRound, 0, "Round 1 has nothing to fight.");

            // Give the spawner a couple of ticks to release the first bodies.
            timeout = Time.time + 20f;
            while (director.AliveEnemies == 0 && Time.time < timeout)
            {
                yield return null;
            }

            Assert.Greater(director.AliveEnemies, 0, "No enemies were spawned during combat.");
        }

        [UnityTest]
        public IEnumerator EnemiesCloseOnThePlayer()
        {
            yield return null;

            GameDirector director = Director;
            var player = Object.FindFirstObjectByType<PlayerRig>();

            float timeout = Time.time + 40f;
            while (director.AliveEnemies == 0 && Time.time < timeout)
            {
                yield return null;
            }

            Assert.Greater(director.AliveEnemies, 0, "No enemies to observe.");

            EnemyBrain enemy = director.Enemies.NearestLive(player.transform.position);
            Assert.IsNotNull(enemy);

            float startDistance = Vector3.Distance(enemy.transform.position, player.transform.position);
            yield return new WaitForSeconds(4f);

            // The same body may have died; re-query for whatever is closest now.
            EnemyBrain current = director.Enemies.NearestLive(player.transform.position);
            Assert.IsNotNull(current, "Every enemy vanished without the player firing.");

            float endDistance = Vector3.Distance(current.transform.position, player.transform.position);
            Assert.Less(
                endDistance,
                startDistance,
                $"Enemies made no progress toward the player in four seconds ({startDistance:0.0}m -> {endDistance:0.0}m). " +
                "The nav graph is probably not connecting the spawn points to the courtyard.");
        }

        [UnityTest]
        public IEnumerator KillingAnEnemyPaysSalvage()
        {
            yield return null;

            GameDirector director = Director;
            var wallet = Object.FindFirstObjectByType<SalvageWallet>();

            float timeout = Time.time + 40f;
            while (director.AliveEnemies == 0 && Time.time < timeout)
            {
                yield return null;
            }

            EnemyBrain enemy = director.Enemies.NearestLive(Vector3.zero);
            Assert.IsNotNull(enemy, "No enemy to kill.");

            int before = wallet.Balance;
            var health = enemy.GetComponent<EnemyHealth>();
            health.ApplyDamage(new DamageInfo
            {
                Amount = health.MaxHealth * 2f,
                Point = enemy.transform.position,
                Direction = Vector3.forward,
                Normal = Vector3.back,
                Kind = DamageKind.Ballistic
            });

            yield return null;

            Assert.IsFalse(health.IsAlive, "The enemy survived lethal damage.");
            Assert.Greater(wallet.Balance, before, "Killing an enemy paid no salvage.");
            Assert.AreEqual(1, director.KillsThisRun);
        }

        [UnityTest]
        public IEnumerator BuyingARouteOpensItAndSpendsSalvage()
        {
            yield return null;

            var wallet = Object.FindFirstObjectByType<SalvageWallet>();
            RouteDoor door = null;
            foreach (RouteDoor candidate in Object.FindObjectsByType<RouteDoor>(FindObjectsSortMode.None))
            {
                if (candidate.NavGateName == "LabWing")
                {
                    door = candidate;
                    break;
                }
            }

            Assert.IsNotNull(door, "No lab wing route door in the scene.");
            Assert.IsFalse(door.IsOpen, "The lab route should start closed.");

            wallet.AwardFlat(door.Cost);
            int before = wallet.Balance;
            int cost = door.Cost;

            Assert.IsTrue(door.Interact(wallet, null), "Buying an affordable route failed.");

            yield return null;

            Assert.IsTrue(door.IsOpen);
            Assert.AreEqual(before - cost, wallet.Balance, "Route purchase did not deduct the cost.");
            Assert.IsTrue(Nav.NavGraph.Active.IsGateOpen(Nav.NavGraph.Active.GateIdByName("LabWing")),
                "Buying the route did not open its nav gate.");
        }

        [UnityTest]
        public IEnumerator RouteCannotBeBoughtWithoutEnoughSalvage()
        {
            yield return null;

            var wallet = Object.FindFirstObjectByType<SalvageWallet>();
            RouteDoor door = null;
            foreach (RouteDoor candidate in Object.FindObjectsByType<RouteDoor>(FindObjectsSortMode.None))
            {
                if (candidate.Cost > wallet.Balance)
                {
                    door = candidate;
                    break;
                }
            }

            Assert.IsNotNull(door, "Expected at least one route the player cannot yet afford.");
            Assert.IsFalse(door.CanInteract(wallet));
            Assert.IsFalse(door.Interact(wallet, null), "An unaffordable route opened anyway.");
            Assert.IsFalse(door.IsOpen);

            yield return null;
        }

        [UnityTest]
        public IEnumerator PowerUpsApplyAndExpire()
        {
            yield return null;

            var powerUps = Object.FindFirstObjectByType<PowerUpManager>();
            var player = Object.FindFirstObjectByType<PlayerRig>();
            var wallet = Object.FindFirstObjectByType<SalvageWallet>();

            Assert.AreEqual(1f, player.Loadout.DamageMultiplier, 0.001f);
            powerUps.Activate(PowerUpKind.Overcharge);
            Assert.Greater(player.Loadout.DamageMultiplier, 1f, "Overcharge did not raise weapon damage.");
            Assert.IsTrue(powerUps.IsActive(PowerUpKind.Overcharge));

            powerUps.Activate(PowerUpKind.SalvageSurge);
            Assert.Greater(wallet.EarnMultiplier, 1f, "Salvage Surge did not raise income.");

            powerUps.Activate(PowerUpKind.LastStand);
            Assert.IsTrue(player.Health.LastStandActive, "Last Stand did not arm.");

            powerUps.ResetAll();
            yield return null;

            Assert.AreEqual(1f, player.Loadout.DamageMultiplier, 0.001f, "Overcharge did not clear.");
            Assert.AreEqual(1f, wallet.EarnMultiplier, 0.001f, "Salvage Surge did not clear.");
            Assert.IsFalse(player.Health.LastStandActive, "Last Stand did not clear.");
        }

        [UnityTest]
        public IEnumerator LastStandSurvivesAnOtherwiseLethalHit()
        {
            yield return null;

            var powerUps = Object.FindFirstObjectByType<PowerUpManager>();
            var player = Object.FindFirstObjectByType<PlayerRig>();

            powerUps.Activate(PowerUpKind.LastStand);
            player.Health.ApplyDamage(new DamageInfo
            {
                Amount = player.Health.MaxHealth * 10f,
                Point = player.transform.position,
                Direction = Vector3.forward,
                Normal = Vector3.back,
                Kind = DamageKind.Melee
            });

            yield return null;

            Assert.IsTrue(player.Health.IsAlive, "Last Stand failed to absorb a lethal hit.");
            Assert.Greater(player.Health.Health, 0f);
        }

        [UnityTest]
        public IEnumerator MapPhaseChangesTheStation()
        {
            yield return null;

            var phase = Object.FindFirstObjectByType<MapPhaseController>();
            Assert.AreEqual(MapPhase.Standby, phase.CurrentPhase);

            float fogAtStandby = RenderSettings.fogDensity;

            phase.ApplyPhase(MapPhase.Meridian, instant: true);
            yield return null;

            Assert.AreEqual(MapPhase.Meridian, phase.CurrentPhase);
            Assert.Greater(RenderSettings.fogDensity, fogAtStandby,
                "The Meridian phase should be visibly foggier than Standby.");

            // Doors listed as failing open by Meridian must actually be open.
            foreach (RouteDoor door in Object.FindObjectsByType<RouteDoor>(FindObjectsSortMode.None))
            {
                Assert.IsTrue(door.IsOpen, $"'{door.name}' should have failed open by the Meridian phase.");
            }

            phase.ResetToStart();
            yield return null;
            Assert.AreEqual(MapPhase.Standby, phase.CurrentPhase);
        }

        [UnityTest]
        public IEnumerator RestartResetsTheRun()
        {
            yield return null;

            GameDirector director = Director;
            var wallet = Object.FindFirstObjectByType<SalvageWallet>();
            var player = Object.FindFirstObjectByType<PlayerRig>();

            float timeout = Time.time + 30f;
            while (director.State != GameState.Combat && Time.time < timeout)
            {
                yield return null;
            }

            wallet.AwardFlat(5000);
            player.Health.ApplyDamage(new DamageInfo
            {
                Amount = 40f,
                Point = player.transform.position,
                Direction = Vector3.forward,
                Normal = Vector3.back,
                Kind = DamageKind.Melee
            });

            yield return null;
            Assert.Less(player.Health.Health, player.Health.MaxHealth);

            director.RestartRun();
            yield return null;
            yield return null;

            Assert.AreEqual(player.Health.MaxHealth, player.Health.Health, 0.01f, "Restart did not restore health.");
            Assert.AreEqual(0, director.KillsThisRun, "Restart did not reset the kill count.");
            Assert.AreEqual(0, director.AliveEnemies, "Restart left enemies alive.");
            Assert.Less(wallet.Balance, 5000, "Restart did not reset the wallet.");

            foreach (RouteDoor door in Object.FindObjectsByType<RouteDoor>(FindObjectsSortMode.None))
            {
                Assert.IsFalse(door.IsOpen, $"'{door.name}' stayed open across a restart.");
            }
        }

        [UnityTest]
        public IEnumerator PauseStopsTimeAndResumeRestoresIt()
        {
            yield return null;

            GameDirector director = Director;

            director.SetPaused(true);
            Assert.IsTrue(director.IsPaused);
            Assert.AreEqual(0f, Time.timeScale, 0.0001f, "Pausing did not stop time.");

            director.SetPaused(false);
            Assert.IsFalse(director.IsPaused);
            Assert.AreEqual(1f, Time.timeScale, 0.0001f, "Resuming did not restore time.");
        }
    }
}
