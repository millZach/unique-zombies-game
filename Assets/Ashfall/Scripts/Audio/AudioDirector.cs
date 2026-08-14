using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ashfall.Audio
{
    /// <summary>
    /// Every sound in the game routes through here, the same way every
    /// transient visual routes through the FX director.
    ///
    /// Three things this exists to guarantee:
    ///
    /// *One pool.* A fixed set of <see cref="AudioSource"/> voices is created
    /// once and reused. Nothing instantiates an object to make a noise, so a
    /// round-twelve firefight allocates exactly as much as an empty courtyard:
    /// nothing.
    ///
    /// *No spam.* Nine shotgun pellets landing on the same body in the same
    /// frame is one impact sound, not nine. Each cue carries a minimum
    /// interval, and a cue asked for again inside that window is dropped
    /// rather than layered into mud.
    ///
    /// *Silence is legal.* A cue with no clip assigned does nothing and
    /// returns false. The game is fully playable with the audio folder empty,
    /// which is what lets the scene builder, the tests and a headless machine
    /// with no audio device all take the same code path.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class AudioDirector : MonoBehaviour
    {
        [Serializable]
        public class CueEntry
        {
            public AudioCue cue;
            public AudioClip clip;

            [Range(0f, 1.5f)] public float volume = 0.8f;

            [Tooltip("Random pitch spread, +/- this fraction. Stops repeats sounding mechanical.")]
            [Range(0f, 0.4f)] public float pitchJitter = 0.05f;

            [Tooltip("Seconds before this cue may sound again. The anti-spam guard.")]
            public float minInterval = 0.04f;

            [Tooltip("Metres of full volume around a 3D source.")]
            public float minDistance = 3.5f;

            [Tooltip("Metres at which a 3D source is inaudible.")]
            public float maxDistance = 45f;
        }

        [Header("Content (assigned by the scene builder)")]
        [SerializeField] private List<CueEntry> cues = new();
        [SerializeField] private AudioClip stormAmbience;

        [Header("Mix")]
        [Range(0f, 1f)] [SerializeField] private float masterVolume = 0.85f;
        [Range(0f, 1f)] [SerializeField] private float ambienceVolume = 0.34f;
        [SerializeField] private int voiceCount = 24;

        [Header("Storm")]
        [Tooltip("Seconds between thunder at the calmest and the fiercest phase.")]
        [SerializeField] private Vector2 thunderInterval = new Vector2(34f, 9f);
        [Range(0f, 1f)] [SerializeField] private float thunderVolume = 0.55f;

        public static AudioDirector Instance { get; private set; }

        private readonly CueEntry[] _byCue = new CueEntry[AudioCues.Count];
        private readonly float[] _nextAllowedAt = new float[AudioCues.Count];
        private readonly int[] _playCounts = new int[AudioCues.Count];

        private AudioSource[] _voices;
        private float[] _voiceFreeAt;
        private int _nextVoice;

        private AudioSource _ambienceSource;
        private float _stormIntensity;
        private float _ambienceTarget;
        private float _thunderTimer;

        /// <summary>Total cues played this session. Cheap, and the hook the tests assert on.</summary>
        public int TotalPlays { get; private set; }

        public float StormIntensity => _stormIntensity;

        public int PlayCount(AudioCue cue)
        {
            int index = (int)cue;
            return index > 0 && index < _playCounts.Length ? _playCounts[index] : 0;
        }

        public AudioClip ClipFor(AudioCue cue)
        {
            int index = (int)cue;
            return index > 0 && index < _byCue.Length ? _byCue[index]?.clip : null;
        }

        /// <summary>True when a cue has a clip and would actually make a sound.</summary>
        public bool HasClip(AudioCue cue) => ClipFor(cue) != null;

        public IReadOnlyList<CueEntry> Cues => cues;
        public AudioClip StormAmbience => stormAmbience;

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            EnsureRuntime();
        }

        /// <summary>
        /// Builds the voice pool and the ambience source on first need.
        ///
        /// Lazy rather than eager because <see cref="Configure"/> is called by
        /// the scene builder at edit time: creating two dozen child objects
        /// there would serialise them into Main.unity and Awake would then make
        /// two dozen more. Nothing plays a sound during a scene build, so
        /// deferring to the first play keeps the saved scene clean and still
        /// makes the director usable from a test that never enters play mode.
        /// </summary>
        private void EnsureRuntime()
        {
            BuildLookup();

            if (_voices == null)
            {
                BuildVoices();
            }

            BuildAmbience();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Called by the scene builder. Safe to call before or after Awake.</summary>
        public void Configure(List<CueEntry> entries, AudioClip ambience)
        {
            cues = entries ?? new List<CueEntry>();
            stormAmbience = ambience;

            BuildLookup();
            if (_voices != null)
            {
                BuildAmbience();
            }
        }

        private void BuildLookup()
        {
            Array.Clear(_byCue, 0, _byCue.Length);

            for (int i = 0; i < cues.Count; i++)
            {
                CueEntry entry = cues[i];
                if (entry == null)
                {
                    continue;
                }

                int index = (int)entry.cue;
                if (index > 0 && index < _byCue.Length)
                {
                    _byCue[index] = entry;
                }
            }
        }

        private void BuildVoices()
        {
            int count = Mathf.Max(4, voiceCount);
            _voices = new AudioSource[count];
            _voiceFreeAt = new float[count];

            var root = new GameObject("Voice Pool").transform;
            root.SetParent(transform, false);

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject($"Voice {i:00}");
                go.transform.SetParent(root, false);

                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                // Enemies move quickly and the map is small; doppler on a
                // sprinter's shriek sounds like a fault, not like speed.
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.spatialBlend = 0f;

                _voices[i] = source;
            }
        }

        private void BuildAmbience()
        {
            if (_ambienceSource == null)
            {
                var go = new GameObject("Storm Ambience");
                go.transform.SetParent(transform, false);
                _ambienceSource = go.AddComponent<AudioSource>();
                _ambienceSource.playOnAwake = false;
                _ambienceSource.loop = true;
                _ambienceSource.spatialBlend = 0f;
                _ambienceSource.dopplerLevel = 0f;
            }

            _ambienceSource.clip = stormAmbience;
            _ambienceSource.volume = 0f;

            if (stormAmbience != null && !_ambienceSource.isPlaying)
            {
                _ambienceSource.Play();
            }
        }

        // ------------------------------------------------------------------
        // Playback
        // ------------------------------------------------------------------

        /// <summary>
        /// A sound with no position: the player's own weapon, the HUD, the run.
        ///
        /// First-person weapons are deliberately 2D. Positioning a gun that is
        /// welded to the camera buys nothing and costs the low end that makes
        /// it feel like it has recoil.
        /// </summary>
        public bool Play2D(AudioCue cue, float volumeScale = 1f, float pitchScale = 1f)
        {
            return PlayInternal(cue, Vector3.zero, false, volumeScale, pitchScale);
        }

        /// <summary>A sound with a place in the world: enemies, doors, impacts, pickups.</summary>
        public bool PlayAt(AudioCue cue, Vector3 position, float volumeScale = 1f, float pitchScale = 1f)
        {
            return PlayInternal(cue, position, true, volumeScale, pitchScale);
        }

        private bool PlayInternal(AudioCue cue, Vector3 position, bool spatial, float volumeScale, float pitchScale)
        {
            int index = (int)cue;
            if (index <= 0 || index >= _byCue.Length)
            {
                return false;
            }

            CueEntry entry = _byCue[index];
            if (entry?.clip == null)
            {
                return false;
            }

            if (_voices == null)
            {
                EnsureRuntime();
            }

            // Unscaled: a paused or slowed game must not change how often a
            // cue is allowed to retrigger.
            float now = Time.unscaledTime;
            if (now < _nextAllowedAt[index])
            {
                return false;
            }

            AudioSource source = RentVoice(now);
            if (source == null)
            {
                return false;
            }

            float pitch = 1f;
            if (entry.pitchJitter > 0f)
            {
                pitch += UnityEngine.Random.Range(-entry.pitchJitter, entry.pitchJitter);
            }

            pitch = Mathf.Clamp(pitch * Mathf.Max(0.01f, pitchScale), 0.05f, 3f);

            source.clip = entry.clip;
            source.pitch = pitch;
            source.volume = Mathf.Clamp01(entry.volume * volumeScale * masterVolume);
            source.spatialBlend = spatial ? 1f : 0f;
            source.minDistance = entry.minDistance;
            source.maxDistance = entry.maxDistance;
            source.transform.position = spatial ? position : Vector3.zero;
            source.Play();

            _voiceFreeAt[_nextVoice] = now + entry.clip.length / pitch;
            _nextVoice = (_nextVoice + 1) % _voices.Length;

            _nextAllowedAt[index] = now + Mathf.Max(0f, entry.minInterval);
            _playCounts[index]++;
            TotalPlays++;
            return true;
        }

        /// <summary>
        /// The next free voice, or the one that has been busy longest.
        ///
        /// Bookkeeping rather than <c>isPlaying</c>: batch-mode Unity runs with
        /// a null audio device, and a voice allocator that believes nothing is
        /// ever playing would hand out the same source every time and make the
        /// pool untestable.
        /// </summary>
        private AudioSource RentVoice(float now)
        {
            if (_voices == null || _voices.Length == 0)
            {
                return null;
            }

            for (int step = 0; step < _voices.Length; step++)
            {
                int i = (_nextVoice + step) % _voices.Length;
                if (now >= _voiceFreeAt[i])
                {
                    _nextVoice = i;
                    return _voices[i];
                }
            }

            int oldest = 0;
            for (int i = 1; i < _voices.Length; i++)
            {
                if (_voiceFreeAt[i] < _voiceFreeAt[oldest])
                {
                    oldest = i;
                }
            }

            _nextVoice = oldest;
            return _voices[oldest];
        }

        // ------------------------------------------------------------------
        // Storm bed
        // ------------------------------------------------------------------

        /// <summary>
        /// Drives the weather bed. 0 is the calm opening, 1 is Black Meridian.
        ///
        /// The map phases already move the rain, the lights and the fog
        /// together; the ambience joining them is what stops the storm from
        /// being something you only see.
        /// </summary>
        public void SetStormIntensity(float intensity01)
        {
            _stormIntensity = Mathf.Clamp01(intensity01);
            _ambienceTarget = ambienceVolume * Mathf.Lerp(0.42f, 1f, _stormIntensity) * masterVolume;

            if (_thunderTimer <= 0f)
            {
                _thunderTimer = NextThunderDelay();
            }
        }

        public void SetPaused(bool paused)
        {
            AudioListener.pause = paused;
        }

        /// <summary>Silences everything in flight. Used on defeat and restart.</summary>
        public void StopAll()
        {
            if (_voices == null)
            {
                return;
            }

            for (int i = 0; i < _voices.Length; i++)
            {
                if (_voices[i] != null)
                {
                    _voices[i].Stop();
                }

                _voiceFreeAt[i] = 0f;
            }

            Array.Clear(_nextAllowedAt, 0, _nextAllowedAt.Length);
        }

        private float NextThunderDelay()
        {
            float mean = Mathf.Lerp(thunderInterval.x, thunderInterval.y, _stormIntensity);
            return mean * UnityEngine.Random.Range(0.65f, 1.45f);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (_ambienceSource != null && _ambienceSource.clip != null)
            {
                // A phase change should feel like the weather turning, not like
                // someone moving a fader, so the bed takes a couple of seconds.
                _ambienceSource.volume = Mathf.MoveTowards(_ambienceSource.volume, _ambienceTarget, dt * 0.35f);

                if (!_ambienceSource.isPlaying && _ambienceTarget > 0f)
                {
                    _ambienceSource.Play();
                }
            }

            if (_stormIntensity <= 0.02f || !HasClip(AudioCue.StormThunder))
            {
                return;
            }

            _thunderTimer -= dt;
            if (_thunderTimer > 0f)
            {
                return;
            }

            _thunderTimer = NextThunderDelay();
            Play2D(AudioCue.StormThunder, thunderVolume * Mathf.Lerp(0.55f, 1f, _stormIntensity));
        }
    }
}
