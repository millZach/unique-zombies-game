using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Ashfall.Audio;
using Ashfall.Core;
using Ashfall.World;

namespace Ashfall.Tests
{
    /// <summary>
    /// The audio director's contract, exercised without a scene or an audio device.
    ///
    /// The properties that matter are not "does it make a noise" -- the build
    /// machine has no output device -- but the ones that stop it hurting the
    /// game: it must never allocate per call, never spam, never throw on a
    /// missing clip, and never hand out a voice that is already busy.
    /// </summary>
    public class AudioDirectorTests
    {
        private GameObject _host;
        private AudioDirector _director;
        private readonly List<AudioClip> _clips = new();

        private AudioClip MakeClip(string name, float seconds = 0.25f)
        {
            var clip = AudioClip.Create(name, Mathf.Max(1, Mathf.RoundToInt(44100 * seconds)), 1, 44100, false);
            _clips.Add(clip);
            return clip;
        }

        private AudioDirector.CueEntry Entry(AudioCue cue, float minInterval, float seconds = 0.25f)
        {
            return new AudioDirector.CueEntry
            {
                cue = cue,
                clip = MakeClip($"Test_{cue}", seconds),
                volume = 0.8f,
                pitchJitter = 0f,
                minInterval = minInterval,
                minDistance = 3f,
                maxDistance = 30f
            };
        }

        [SetUp]
        public void CreateDirector()
        {
            _host = new GameObject("AudioDirector Test Host");
            _director = _host.AddComponent<AudioDirector>();
        }

        [TearDown]
        public void DestroyDirector()
        {
            Object.DestroyImmediate(_host);
            for (int i = 0; i < _clips.Count; i++)
            {
                if (_clips[i] != null)
                {
                    Object.DestroyImmediate(_clips[i]);
                }
            }

            _clips.Clear();
        }

        [Test]
        public void PlayingAConfiguredCueCountsIt()
        {
            _director.Configure(new List<AudioDirector.CueEntry> { Entry(AudioCue.PlayerHurt, 0f) }, null);

            Assert.IsTrue(_director.Play2D(AudioCue.PlayerHurt), "A configured cue should play.");
            Assert.AreEqual(1, _director.PlayCount(AudioCue.PlayerHurt));
            Assert.AreEqual(1, _director.TotalPlays);
        }

        [Test]
        public void AnUnconfiguredCueIsSilentAndDoesNotThrow()
        {
            _director.Configure(new List<AudioDirector.CueEntry>(), null);

            Assert.IsFalse(_director.Play2D(AudioCue.RoundStart));
            Assert.IsFalse(_director.PlayAt(AudioCue.EnemyDeathBrute, Vector3.one * 5f));
            Assert.AreEqual(0, _director.TotalPlays, "Nothing was configured, so nothing should have played.");
        }

        [Test]
        public void ACueWithANullClipIsSilent()
        {
            _director.Configure(new List<AudioDirector.CueEntry>
            {
                new() { cue = AudioCue.ImpactFlesh, clip = null, minInterval = 0f }
            }, null);

            Assert.IsFalse(_director.Play2D(AudioCue.ImpactFlesh));
            Assert.IsFalse(_director.HasClip(AudioCue.ImpactFlesh));
        }

        [Test]
        public void NoneIsNeverPlayable()
        {
            _director.Configure(new List<AudioDirector.CueEntry> { Entry(AudioCue.PlayerHurt, 0f) }, null);

            Assert.IsFalse(_director.Play2D(AudioCue.None));
            Assert.IsFalse(_director.PlayAt(AudioCue.None, Vector3.zero));
        }

        [Test]
        public void RepeatsInsideTheMinimumIntervalAreDropped()
        {
            // Nine shotgun pellets hitting one torso in one frame is one impact.
            _director.Configure(new List<AudioDirector.CueEntry> { Entry(AudioCue.ImpactFlesh, 5f) }, null);

            int played = 0;
            for (int i = 0; i < 9; i++)
            {
                played += _director.PlayAt(AudioCue.ImpactFlesh, Vector3.zero) ? 1 : 0;
            }

            Assert.AreEqual(1, played, "The anti-spam guard should have collapsed the burst to one sound.");
            Assert.AreEqual(1, _director.PlayCount(AudioCue.ImpactFlesh));
        }

        [Test]
        public void AZeroIntervalCueRepeatsFreely()
        {
            _director.Configure(new List<AudioDirector.CueEntry> { Entry(AudioCue.EnemyDeathShambler, 0f) }, null);

            for (int i = 0; i < 6; i++)
            {
                Assert.IsTrue(_director.PlayAt(AudioCue.EnemyDeathShambler, Vector3.zero),
                    $"Repeat {i} was dropped despite a zero interval.");
            }

            Assert.AreEqual(6, _director.PlayCount(AudioCue.EnemyDeathShambler));
        }

        [Test]
        public void TwoDIsUnspatialisedAndThreeDIsPositioned()
        {
            _director.Configure(new List<AudioDirector.CueEntry>
            {
                Entry(AudioCue.WeaponFireSidearm, 0f),
                Entry(AudioCue.EnemyAttackBrute, 0f)
            }, null);

            var position = new Vector3(4f, 1f, -7f);
            _director.Play2D(AudioCue.WeaponFireSidearm);
            _director.PlayAt(AudioCue.EnemyAttackBrute, position);

            AudioSource flat = FindSourcePlaying("Test_WeaponFireSidearm");
            AudioSource placed = FindSourcePlaying("Test_EnemyAttackBrute");

            Assert.IsNotNull(flat, "The 2D cue did not reach a voice.");
            Assert.IsNotNull(placed, "The 3D cue did not reach a voice.");

            Assert.AreEqual(0f, flat.spatialBlend, 0.001f, "A first-person weapon must be 2D.");
            Assert.AreEqual(1f, placed.spatialBlend, 0.001f, "A world event must be 3D.");
            Assert.AreEqual(position, placed.transform.position, "The 3D voice was not moved to the event.");
            Assert.AreEqual(0f, placed.dopplerLevel, 0.001f, "Doppler on fast enemies reads as a fault.");
        }

        [Test]
        public void ConcurrentCuesTakeDifferentVoices()
        {
            var entries = new List<AudioDirector.CueEntry>
            {
                Entry(AudioCue.EnemyAttackShambler, 0f, 2f),
                Entry(AudioCue.EnemyAttackSprinter, 0f, 2f),
                Entry(AudioCue.EnemyAttackBrute, 0f, 2f)
            };
            _director.Configure(entries, null);

            _director.Play2D(AudioCue.EnemyAttackShambler);
            _director.Play2D(AudioCue.EnemyAttackSprinter);
            _director.Play2D(AudioCue.EnemyAttackBrute);

            var used = new HashSet<AudioSource>
            {
                FindSourcePlaying("Test_EnemyAttackShambler"),
                FindSourcePlaying("Test_EnemyAttackSprinter"),
                FindSourcePlaying("Test_EnemyAttackBrute")
            };

            used.Remove(null);
            Assert.AreEqual(3, used.Count, "Three overlapping sounds were stacked onto fewer than three voices.");
        }

        [Test]
        public void TheVoicePoolIsFixedAndNothingIsInstantiatedPerPlay()
        {
            _director.Configure(new List<AudioDirector.CueEntry> { Entry(AudioCue.ImpactWorld, 0f) }, null);

            // The pool is built on first use, not in Configure -- the scene
            // builder calls Configure at edit time and must not serialise two
            // dozen voice objects into Main.unity.
            _director.PlayAt(AudioCue.ImpactWorld, Vector3.zero);

            int before = _host.GetComponentsInChildren<AudioSource>(true).Length;
            Assert.Greater(before, 0, "The first play should have built the voice pool.");

            for (int i = 0; i < 200; i++)
            {
                _director.PlayAt(AudioCue.ImpactWorld, Vector3.one * i);
            }

            int after = _host.GetComponentsInChildren<AudioSource>(true).Length;
            Assert.AreEqual(before, after, "Playing sounds must not create AudioSources.");
        }

        [Test]
        public void StopAllClearsTheAntiSpamGuard()
        {
            _director.Configure(new List<AudioDirector.CueEntry> { Entry(AudioCue.RoundStart, 60f) }, null);

            Assert.IsTrue(_director.Play2D(AudioCue.RoundStart));
            Assert.IsFalse(_director.Play2D(AudioCue.RoundStart), "The guard should still be closed.");

            _director.StopAll();
            Assert.IsTrue(_director.Play2D(AudioCue.RoundStart), "A restart should not inherit the old guard.");
        }

        [Test]
        public void StormIntensityIsClampedAndStored()
        {
            _director.Configure(new List<AudioDirector.CueEntry>(), null);

            _director.SetStormIntensity(0.4f);
            Assert.AreEqual(0.4f, _director.StormIntensity, 0.001f);

            _director.SetStormIntensity(3f);
            Assert.AreEqual(1f, _director.StormIntensity, 0.001f);

            _director.SetStormIntensity(-2f);
            Assert.AreEqual(0f, _director.StormIntensity, 0.001f);
        }

        [Test]
        public void StormIntensityRisesWithEveryMapPhase()
        {
            float previous = -1f;

            for (int i = 0; i < MapPhases.Count; i++)
            {
                MapPhaseController.PhaseAtmosphere atmosphere =
                    MapPhaseController.DefaultAtmosphere((MapPhase)i);
                float intensity = MapPhaseController.StormIntensityFor(atmosphere);

                Assert.Greater(intensity, previous,
                    $"Phase {(MapPhase)i} is not stormier than the phase before it.");
                Assert.That(intensity, Is.InRange(0f, 1.0001f));
                previous = intensity;
            }

            Assert.AreEqual(1f, previous, 0.001f, "Black Meridian should be the full-intensity storm.");
        }

        [Test]
        public void EveryEnemyArchetypeHasItsOwnAttackAndDeathCue()
        {
            var attacks = new HashSet<AudioCue>();
            var deaths = new HashSet<AudioCue>();

            foreach (EnemyArchetype archetype in System.Enum.GetValues(typeof(EnemyArchetype)))
            {
                AudioCue attack = AudioCues.AttackFor(archetype);
                AudioCue death = AudioCues.DeathFor(archetype);

                Assert.AreNotEqual(AudioCue.None, attack);
                Assert.AreNotEqual(AudioCue.None, death);
                Assert.AreNotEqual(attack, death);

                attacks.Add(attack);
                deaths.Add(death);
            }

            int archetypes = System.Enum.GetValues(typeof(EnemyArchetype)).Length;
            Assert.AreEqual(archetypes, attacks.Count, "Two archetypes share an attack sound.");
            Assert.AreEqual(archetypes, deaths.Count, "Two archetypes share a death sound.");
        }

        [Test]
        public void CueIndicesFitTheDirectorsLookupTables()
        {
            foreach (AudioCue cue in System.Enum.GetValues(typeof(AudioCue)))
            {
                Assert.Less((int)cue, AudioCues.Count,
                    $"{cue} is outside AudioCues.Count, so the director would silently ignore it.");
            }
        }

        private AudioSource FindSourcePlaying(string clipName)
        {
            foreach (AudioSource source in _host.GetComponentsInChildren<AudioSource>(true))
            {
                if (source.clip != null && source.clip.name == clipName)
                {
                    return source;
                }
            }

            return null;
        }
    }
}
