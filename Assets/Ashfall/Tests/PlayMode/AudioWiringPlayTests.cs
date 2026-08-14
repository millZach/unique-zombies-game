using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.Enemies;
using Ashfall.InputLayer;
using Ashfall.Player;
using Ashfall.World;

namespace Ashfall.Tests
{
    /// <summary>
    /// Proves the audio is actually connected to the game, in the real scene.
    ///
    /// The edit-mode suite proves the director behaves; this proves something
    /// calls it. Every assertion here drives a gameplay path -- pull a trigger,
    /// take a hit, kill something, fail a purchase -- and then checks the
    /// director's counter moved. That is the difference between "an audio
    /// system exists" and "the game makes a noise when you shoot".
    ///
    /// The counters are used rather than <c>AudioSource.isPlaying</c> on
    /// purpose: batch-mode Unity runs with no audio device, and asserting on
    /// playback state would make this suite untrustworthy exactly where it
    /// runs.
    /// </summary>
    public class AudioWiringPlayTests
    {
        private const string ScenePath = "Assets/Ashfall/Scenes/Main.unity";

        [OneTimeSetUp]
        public void DisableAutoLoad()
        {
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
            Time.timeScale = 1f;
            AudioListener.pause = false;
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

        private static AudioDirector Director => AudioDirector.Instance;

        [UnityTest]
        public IEnumerator TheSceneShipsAnAudioDirectorWithEveryCueLoaded()
        {
            yield return null;

            Assert.IsNotNull(Director, "The Main scene has no AudioDirector.");

            foreach (AudioCue cue in System.Enum.GetValues(typeof(AudioCue)))
            {
                if (cue == AudioCue.None)
                {
                    continue;
                }

                Assert.IsTrue(Director.HasClip(cue), $"{cue} has no clip in the built scene.");
            }

            Assert.IsNotNull(Director.StormAmbience, "No storm ambience bed was wired into the scene.");
        }

        [UnityTest]
        public IEnumerator PullingTheTriggerFiresTheWeaponsOwnSound()
        {
            yield return null;
            yield return new WaitForSeconds(0.6f);

            var player = Object.FindFirstObjectByType<PlayerRig>();
            AudioCue fireCue = player.Loadout.CurrentDefinition.fireCue;

            int before = Director.PlayCount(fireCue);

            var input = InputFrame.Empty;
            input.FireHeld = true;
            input.FirePressed = true;
            player.Loadout.Tick(input, Time.deltaTime, allowCombat: true, movementFactor: 0f);

            Assert.AreEqual(before + 1, Director.PlayCount(fireCue),
                "Firing the sidearm did not play its shot sound.");
        }

        [UnityTest]
        public IEnumerator AnEmptyMagazineClicksInsteadOfFiring()
        {
            yield return null;
            yield return new WaitForSeconds(0.6f);

            var player = Object.FindFirstObjectByType<PlayerRig>();
            Weapons.WeaponRuntime weapon = player.Loadout.CurrentWeapon;

            // Empty the magazine the way the game does: one trigger pull at a
            // time, waiting out the cyclic rate.
            var fire = InputFrame.Empty;
            float timeout = Time.time + 20f;
            while (!weapon.IsEmpty && Time.time < timeout)
            {
                fire.FireHeld = true;
                fire.FirePressed = true;
                player.Loadout.Tick(fire, Time.deltaTime, true, 0f);

                fire.FireHeld = false;
                fire.FirePressed = false;
                player.Loadout.Tick(fire, Time.deltaTime, true, 0f);
                yield return null;
            }

            Assert.IsTrue(weapon.IsEmpty, "Could not empty the magazine.");
            weapon.CancelReload();
            // The loop above intentionally exercises the player's auto-reload
            // path while it empties the magazine. Restore reserve for the actual
            // dry-fire assertion so it tests a reloadable empty weapon rather
            // than the exhausted-ammo game-over edge case.
            weapon.AddReserve(weapon.Definition.maxReserveAmmo);

            // The audio library deliberately suppresses repeated magazine cues for
            // 250 ms. Give the previous auto-reload cue time to clear before
            // asserting that this dry trigger starts and sounds a new reload.
            yield return new WaitForSeconds(0.3f);
            // The scene's normal Update loop may have started its own reload
            // while the test waited. Cancel that automatic state immediately
            // before the dry trigger so this assertion owns the transition.
            weapon.CancelReload();

            int dryBefore = Director.PlayCount(AudioCue.WeaponDryFire);
            int reloadBefore = Director.PlayCount(player.Loadout.CurrentDefinition.ReloadCue);
            Assert.IsTrue(weapon.CanReload,
                $"Dry-fire fixture is not reloadable: magazine={weapon.Magazine}, reserve={weapon.Reserve}, state={weapon.State}.");

            var dry = InputFrame.Empty;
            dry.FireHeld = true;
            dry.FirePressed = true;
            player.Loadout.Tick(dry, Time.deltaTime, true, 0f);

            Assert.AreEqual(dryBefore + 1, Director.PlayCount(AudioCue.WeaponDryFire),
                "An empty trigger pull was silent.");
            Assert.AreEqual(reloadBefore + 1, Director.PlayCount(player.Loadout.CurrentDefinition.ReloadCue),
                "The automatic reload that follows a dry fire made no sound.");
        }

        [UnityTest]
        public IEnumerator TakingDamageIsAudible()
        {
            yield return null;
            yield return new WaitForSeconds(0.5f);

            var player = Object.FindFirstObjectByType<PlayerRig>();
            int before = Director.PlayCount(AudioCue.PlayerHurt);

            player.Health.ApplyDamage(DamageInfo.Melee(12f, player.transform.position, Vector3.forward, null));

            Assert.AreEqual(before + 1, Director.PlayCount(AudioCue.PlayerHurt),
                "The player took a hit in silence.");
            Assert.IsTrue(player.Health.IsAlive, "A 12-damage hit should not have been fatal.");
        }

        [UnityTest]
        public IEnumerator KillingAnEnemyPlaysThatArchetypesDeath()
        {
            yield return null;

            GameDirector director = Object.FindFirstObjectByType<GameDirector>();
            var enemies = Object.FindFirstObjectByType<EnemyDirector>();

            float timeout = Time.time + 40f;
            while (enemies.AliveCount == 0 && Time.time < timeout)
            {
                yield return null;
            }

            Assert.Greater(enemies.AliveCount, 0, "No enemy ever spawned, so death audio could not be tested.");

            EnemyBrain victim = enemies.Live[0];
            AudioCue expected = AudioCues.DeathFor(victim.Definition.archetype);
            int before = Director.PlayCount(expected);

            var health = victim.GetComponent<EnemyHealth>();
            health.ApplyDamage(DamageInfo.Ballistic(
                health.MaxHealth * 4f, victim.transform.position, Vector3.forward, Vector3.up, false, null));

            yield return null;

            Assert.AreEqual(before + 1, Director.PlayCount(expected),
                $"A {victim.Definition.displayName} died without a sound.");
            Assert.Greater(director.KillsThisRun, 0, "The kill did not register with the run.");
        }

        [UnityTest]
        public IEnumerator FailingAPurchaseIsAudible()
        {
            yield return null;
            yield return new WaitForSeconds(0.3f);

            var wallet = Object.FindFirstObjectByType<SalvageWallet>();
            int before = Director.PlayCount(AudioCue.PurchaseDenied);

            Assert.IsFalse(wallet.TrySpend(wallet.Balance + 10_000), "That purchase should not have been affordable.");
            Assert.AreEqual(before + 1, Director.PlayCount(AudioCue.PurchaseDenied),
                "A denied purchase gave no feedback.");
        }

        [UnityTest]
        public IEnumerator BuyingARouteIsAudible()
        {
            yield return null;
            yield return new WaitForSeconds(0.3f);

            var door = Object.FindFirstObjectByType<RouteDoor>();
            Assert.IsNotNull(door, "No route doors in the scene.");

            int before = Director.PlayCount(AudioCue.PurchaseRoute);
            door.ForceOpen(instant: true);

            Assert.AreEqual(before + 1, Director.PlayCount(AudioCue.PurchaseRoute),
                "A route opened silently.");
        }

        [UnityTest]
        public IEnumerator CollectingAPowerUpIsAudible()
        {
            yield return null;
            yield return new WaitForSeconds(0.3f);

            var powerUps = Object.FindFirstObjectByType<PowerUpManager>();
            int before = Director.PlayCount(AudioCue.PowerUpPickup);

            powerUps.Activate(PowerUpKind.Overcharge);

            Assert.AreEqual(before + 1, Director.PlayCount(AudioCue.PowerUpPickup),
                "Picking up a power-up was silent.");
        }

        [UnityTest]
        public IEnumerator TheRoundStartFanfarePlaysAndTheStormBedIsRunning()
        {
            yield return null;

            GameDirector director = Object.FindFirstObjectByType<GameDirector>();
            float timeout = Time.time + 30f;

            while (director.Round < 1 && Time.time < timeout)
            {
                yield return null;
            }

            Assert.GreaterOrEqual(director.Round, 1, "The run never reached round 1.");
            Assert.GreaterOrEqual(Director.PlayCount(AudioCue.RoundStart), 1,
                "Round 1 began without its klaxon.");

            // Standby is the calmest phase, but the storm bed still has to be up.
            Assert.Greater(Director.StormIntensity, 0f,
                "The storm ambience was never given an intensity by the phase controller.");
            Assert.LessOrEqual(Director.StormIntensity, 0.35f,
                "Round 1 should not already be at Black Meridian weather.");
        }

        [UnityTest]
        public IEnumerator AdvancingThePhaseMakesTheStormLouder()
        {
            yield return null;
            yield return new WaitForSeconds(0.3f);

            var phases = Object.FindFirstObjectByType<MapPhaseController>();
            phases.ApplyPhase(MapPhase.Standby, instant: true);
            float calm = Director.StormIntensity;

            phases.ApplyPhase(MapPhase.Meridian, instant: true);
            float peak = Director.StormIntensity;

            Assert.Greater(peak, calm, "Black Meridian is not stormier than Standby.");
            Assert.AreEqual(1f, peak, 0.01f, "The final phase should be full intensity.");
        }

        [UnityTest]
        public IEnumerator PausingSilencesTheGame()
        {
            yield return null;
            yield return new WaitForSeconds(0.3f);

            GameDirector director = Object.FindFirstObjectByType<GameDirector>();

            director.SetPaused(true);
            Assert.IsTrue(AudioListener.pause, "Pausing did not stop the audio.");

            director.SetPaused(false);
            Assert.IsFalse(AudioListener.pause, "Unpausing left the audio muted.");
        }

        [UnityTest]
        public IEnumerator PlayingSoundsDoesNotCreateObjects()
        {
            yield return null;
            yield return new WaitForSeconds(0.3f);

            int before = Object.FindObjectsByType<AudioSource>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            for (int i = 0; i < 120; i++)
            {
                Director.PlayAt(AudioCue.ImpactWorld, Vector3.one * i);
                Director.Play2D(AudioCue.WeaponFireRifle);
            }

            yield return null;

            int after = Object.FindObjectsByType<AudioSource>(
                FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            Assert.AreEqual(before, after,
                "The audio director allocated AudioSources at play time; the pool should be fixed.");
        }
    }
}
