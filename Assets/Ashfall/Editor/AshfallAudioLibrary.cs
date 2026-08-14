using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Ashfall.Audio;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// Loads the generated audio and holds the mix.
    ///
    /// The clips themselves come from <c>Tools/Audio/generate_audio.py</c> --
    /// synthesised, not sampled, so the audio has the same provenance story as
    /// the geometry and textures. What lives here is everything a sound
    /// designer would otherwise tweak in an inspector and lose on the next
    /// rebuild: per-cue level, pitch spread, anti-spam interval and 3D falloff.
    ///
    /// A missing WAV is a warning and a silent cue, never a build failure. The
    /// project has to stay buildable from a clean clone whether or not anyone
    /// has run the generator.
    /// </summary>
    public static class AshfallAudioLibrary
    {
        public const string AudioFolder = "Assets/Ashfall/Audio";
        public const string StormAmbienceName = "AMB_Storm_Loop";

        private static readonly List<string> Missing = new();

        public static IReadOnlyList<string> MissingClips => Missing;

        public static AudioClip Load(string clipName)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioFolder}/{clipName}.wav");
            if (clip == null && !Missing.Contains(clipName))
            {
                Missing.Add(clipName);
            }

            return clip;
        }

        public static AudioClip StormAmbience() => Load(StormAmbienceName);

        /// <summary>
        /// One row per cue: which clip, how loud, how much pitch spread, and
        /// how often it is allowed to retrigger.
        ///
        /// The intervals are not arbitrary. A cue's interval has to be shorter
        /// than the fastest legitimate repeat -- the Arc-9 fires every 0.087 s,
        /// so its shot cannot be guarded at 0.1 s or full-auto would stutter --
        /// and long enough to collapse the bursts that are really one event,
        /// like nine shotgun pellets hitting one torso in one frame.
        /// </summary>
        public static List<AudioDirector.CueEntry> BuildCueTable()
        {
            Missing.Clear();

            var table = new List<AudioDirector.CueEntry>
            {
                // --- weapons: 2D, volume supplied by the weapon definition ----
                Entry(AudioCue.WeaponFireSidearm, "SFX_Weapon_Sidearm_Fire", 1.00f, 0.05f, 0.020f),
                Entry(AudioCue.WeaponFireShotgun, "SFX_Weapon_Shotgun_Fire", 1.00f, 0.04f, 0.050f),
                Entry(AudioCue.WeaponFireRifle, "SFX_Weapon_Rifle_Fire", 1.00f, 0.06f, 0.015f),
                Entry(AudioCue.WeaponReloadMagazine, "SFX_Weapon_Reload_Mag", 0.75f, 0.03f, 0.250f),
                Entry(AudioCue.WeaponReloadShell, "SFX_Weapon_Reload_Shell", 0.70f, 0.06f, 0.100f),
                Entry(AudioCue.WeaponDryFire, "SFX_Weapon_DryFire", 0.55f, 0.05f, 0.180f),
                Entry(AudioCue.WeaponEquip, "SFX_Weapon_Equip", 0.60f, 0.05f, 0.150f),

                // --- impacts: 3D, at the point the bullet actually landed -----
                Entry(AudioCue.ImpactFlesh, "SFX_Impact_Flesh", 0.65f, 0.10f, 0.055f, 2.5f, 30f),
                Entry(AudioCue.ImpactCritical, "SFX_Impact_Critical", 0.80f, 0.08f, 0.060f, 3.0f, 36f),
                Entry(AudioCue.ImpactWorld, "SFX_Impact_World", 0.45f, 0.12f, 0.050f, 2.0f, 26f),

                // --- enemies ---------------------------------------------------
                Entry(AudioCue.EnemyAttackShambler, "SFX_Enemy_Attack_Shambler", 0.85f, 0.09f, 0.050f, 3f, 30f),
                Entry(AudioCue.EnemyAttackSprinter, "SFX_Enemy_Attack_Sprinter", 0.85f, 0.12f, 0.040f, 3f, 32f),
                Entry(AudioCue.EnemyAttackBrute, "SFX_Enemy_Attack_Brute", 1.00f, 0.06f, 0.080f, 5f, 48f),
                Entry(AudioCue.EnemyDeathShambler, "SFX_Enemy_Death_Shambler", 0.80f, 0.08f, 0.030f, 4f, 36f),
                Entry(AudioCue.EnemyDeathSprinter, "SFX_Enemy_Death_Sprinter", 0.80f, 0.10f, 0.030f, 4f, 36f),
                Entry(AudioCue.EnemyDeathBrute, "SFX_Enemy_Death_Brute", 1.00f, 0.04f, 0.100f, 6f, 58f),

                // --- player: always 2D, it is happening to you ------------------
                Entry(AudioCue.PlayerHurt, "SFX_Player_Hurt", 0.85f, 0.07f, 0.220f),
                Entry(AudioCue.PlayerDown, "SFX_Player_Down", 1.00f, 0.00f, 1.000f),
                Entry(AudioCue.PlayerLastStand, "SFX_Player_LastStand", 0.90f, 0.00f, 0.500f),

                // --- world ------------------------------------------------------
                Entry(AudioCue.PowerUpPickup, "SFX_PowerUp_Pickup", 0.85f, 0.02f, 0.200f),
                Entry(AudioCue.PowerUpDrop, "SFX_PowerUp_Drop", 0.70f, 0.05f, 0.150f, 4f, 42f),
                Entry(AudioCue.PurchaseRoute, "SFX_Purchase_Route", 0.90f, 0.03f, 0.400f, 6f, 50f),
                Entry(AudioCue.PurchaseWeapon, "SFX_Purchase_Weapon", 0.80f, 0.04f, 0.250f, 4f, 34f),
                Entry(AudioCue.PurchaseDenied, "SFX_Purchase_Denied", 0.60f, 0.03f, 0.350f),
                Entry(AudioCue.BarricadeRepair, "SFX_Barricade_Repair", 0.70f, 0.10f, 0.120f, 3f, 28f),

                // --- run flow ---------------------------------------------------
                Entry(AudioCue.RoundStart, "SFX_Round_Start", 0.85f, 0.00f, 1.000f),
                Entry(AudioCue.RoundComplete, "SFX_Round_Complete", 0.80f, 0.00f, 1.000f),
                Entry(AudioCue.StormThunder, "AMB_Storm_Thunder", 1.00f, 0.10f, 3.000f)
            };

            if (Missing.Count > 0)
            {
                Debug.LogWarning(
                    $"[Ashfall] {Missing.Count} audio clip(s) not found under {AudioFolder}: " +
                    $"{string.Join(", ", Missing)}. Those cues will be silent. " +
                    "Run: /usr/bin/python3 Tools/Audio/generate_audio.py");
            }

            return table;
        }

        private static AudioDirector.CueEntry Entry(
            AudioCue cue, string clipName, float volume, float pitchJitter, float minInterval,
            float minDistance = 3.5f, float maxDistance = 45f)
        {
            return new AudioDirector.CueEntry
            {
                cue = cue,
                clip = Load(clipName),
                volume = volume,
                pitchJitter = pitchJitter,
                minInterval = minInterval,
                minDistance = minDistance,
                maxDistance = maxDistance
            };
        }

        // ------------------------------------------------------------------
        // Import settings
        // ------------------------------------------------------------------

        [MenuItem("Ashfall/Configure Audio Import Settings", priority = 30)]
        public static void ConfigureImportSettingsMenu()
        {
            int changed = ConfigureImportSettings();
            Debug.Log($"[Ashfall] Audio import settings applied to {changed} clip(s).");
        }

        /// <summary>
        /// Short effects decompress on load as PCM; the long storm bed streams
        /// compressed.
        ///
        /// A gunshot must not have decode latency on first play, and at a few
        /// tens of kilobytes there is nothing to save by compressing it. The
        /// ambience is the opposite case: twelve seconds of stereo is the
        /// single biggest asset in the project, it starts once, and nobody can
        /// hear Vorbis on rain.
        /// </summary>
        public static int ConfigureImportSettings()
        {
            if (!AssetDatabase.IsValidFolder(AudioFolder))
            {
                return 0;
            }

            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder });
            int changed = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not AudioImporter importer)
                {
                    continue;
                }

                bool isAmbience = System.IO.Path.GetFileNameWithoutExtension(path) == StormAmbienceName;

                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = isAmbience ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = isAmbience ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
                settings.quality = isAmbience ? 0.70f : 1f;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                // Preload moved onto the per-platform sample settings in Unity 6.
                settings.preloadAudioData = !isAmbience;

                bool dirty = false;
                if (!Mathf.Approximately(importer.defaultSampleSettings.quality, settings.quality)
                    || importer.defaultSampleSettings.loadType != settings.loadType
                    || importer.defaultSampleSettings.compressionFormat != settings.compressionFormat
                    || importer.defaultSampleSettings.preloadAudioData != settings.preloadAudioData
                    || importer.forceToMono != !isAmbience
                    || importer.loadInBackground != isAmbience)
                {
                    dirty = true;
                }

                importer.defaultSampleSettings = settings;
                // Effects are mono at source; forcing it makes the 3D panner
                // exact rather than "mostly centred".
                importer.forceToMono = !isAmbience;
                importer.loadInBackground = isAmbience;
                importer.ambisonic = false;

                if (dirty)
                {
                    importer.SaveAndReimport();
                    changed++;
                }
            }

            return changed;
        }
    }
}
