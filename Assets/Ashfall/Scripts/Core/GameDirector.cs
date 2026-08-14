using System;
using System.Collections;
using UnityEngine;
using Ashfall.Enemies;
using Ashfall.InputLayer;
using Ashfall.Player;
using Ashfall.World;

namespace Ashfall.Core
{
    /// <summary>
    /// The run. Owns the state machine, paces every round, moves the station through its
    /// phases, pays out salvage and handles defeat and restart.
    ///
    /// Everything else in the game is a subsystem this thing talks to; nothing else
    /// talks back except through events, which keeps the flow of a run readable in one
    /// file.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class GameDirector : MonoBehaviour
    {
        [Header("Subsystems")]
        [SerializeField] private PlayerRig player;
        [SerializeField] private EnemyDirector enemies;
        [SerializeField] private MapPhaseController mapPhase;
        [SerializeField] private PowerUpManager powerUps;
        [SerializeField] private SalvageWallet wallet;

        [Header("Run rules")]
        [SerializeField] private int startRound = 1;
        [SerializeField] private int finalRound = RoundPlan.FinalRound;
        [SerializeField] private float briefingSeconds = 6f;
        [SerializeField] private float roundClearSeconds = 4.5f;
        [SerializeField] private float defeatHoldSeconds = 2.0f;

        [Header("Spawn pacing")]
        [Tooltip("Enemies released per spawn tick at the start of a round.")]
        [SerializeField] private int spawnBurstSize = 2;

        // --- events the HUD listens to -------------------------------------
        public event Action<GameState> StateChanged;
        public event Action<int, RoundComposition> RoundStarted;
        public event Action<int> RoundCleared;
        public event Action<MapPhase, string> PhaseAnnounced;
        public event Action<string, float> ObjectiveChanged;
        public event Action<int, int> RunEnded;

        public GameState State { get; private set; } = GameState.Boot;
        public int Round { get; private set; }
        public RoundComposition Composition { get; private set; }
        public bool IsPaused { get; private set; }
        public float StateTimer { get; private set; }

        /// <summary>Enemies still to be released this round.</summary>
        public int PendingSpawns { get; private set; }

        public int AliveEnemies => enemies != null ? enemies.AliveCount : 0;

        /// <summary>Total remaining this round: alive plus not yet released.</summary>
        public int RemainingThisRound => AliveEnemies + PendingSpawns;

        public int KillsThisRun { get; private set; }
        public int HighestRoundReached { get; private set; }
        public PlayerRig Player => player;
        public EnemyDirector Enemies => enemies;
        public MapPhaseController MapPhase => mapPhase;
        public PowerUpManager PowerUps => powerUps;
        public SalvageWallet Wallet => wallet;
        public int FinalRound => finalRound;

        private Coroutine _flow;
        private int _shamblersLeft;
        private int _sprintersLeft;
        private int _brutesLeft;
        private bool _powerUpGrantedThisRound;

        private void Awake()
        {
            player ??= FindFirstObjectByType<PlayerRig>();
            enemies ??= FindFirstObjectByType<EnemyDirector>();
            mapPhase ??= FindFirstObjectByType<MapPhaseController>();
            powerUps ??= FindFirstObjectByType<PowerUpManager>();
            wallet ??= FindFirstObjectByType<SalvageWallet>();
        }

        private void OnEnable()
        {
            if (enemies != null)
            {
                enemies.EnemyKilled += HandleEnemyKilled;
            }

            if (player?.Health != null)
            {
                player.Health.Died += HandlePlayerDied;
            }
        }

        private void OnDisable()
        {
            if (enemies != null)
            {
                enemies.EnemyKilled -= HandleEnemyKilled;
            }

            if (player?.Health != null)
            {
                player.Health.Died -= HandlePlayerDied;
            }
        }

        private void Start()
        {
            if (enemies != null && player != null)
            {
                enemies.SetPlayer(player.transform);
            }

            BeginRun();
        }

        public void Configure(
            PlayerRig playerRig,
            EnemyDirector enemyDirector,
            MapPhaseController phaseController,
            PowerUpManager powerUpManager,
            SalvageWallet salvageWallet)
        {
            player = playerRig;
            enemies = enemyDirector;
            mapPhase = phaseController;
            powerUps = powerUpManager;
            wallet = salvageWallet;
        }

        // ------------------------------------------------------------------
        // Run flow
        // ------------------------------------------------------------------

        public void BeginRun()
        {
            if (_flow != null)
            {
                StopCoroutine(_flow);
            }

            KillsThisRun = 0;
            Round = startRound - 1;
            HighestRoundReached = 0;
            _flow = StartCoroutine(RunFlow());
        }

        public void RestartRun()
        {
            if (_flow != null)
            {
                StopCoroutine(_flow);
                _flow = null;
            }

            SetPaused(false);

            Audio.AudioDirector.Instance?.StopAll();
            enemies?.DespawnAll();
            powerUps?.ResetAll();
            wallet?.ResetWallet();
            mapPhase?.ResetToStart();
            player?.RespawnAtStart();

            if (enemies != null && player != null)
            {
                enemies.SetPlayer(player.transform);
            }

            BeginRun();
        }

        private IEnumerator RunFlow()
        {
            SetState(GameState.Briefing);
            mapPhase?.ApplyRound(startRound, instant: true);
            player?.SetCombatEnabled(true);

            ObjectiveChanged?.Invoke("Black Meridian station. Hold until the storm passes.", briefingSeconds);
            PhaseAnnounced?.Invoke(Core.MapPhase.Standby, MapPhases.Headline(Core.MapPhase.Standby));

            yield return WaitPausable(briefingSeconds);

            while (true)
            {
                Round++;
                HighestRoundReached = Mathf.Max(HighestRoundReached, Round);

                Composition = RoundPlan.For(Round);
                _shamblersLeft = Composition.ShamblerCount;
                _sprintersLeft = Composition.SprinterCount;
                _brutesLeft = Composition.BruteCount;
                PendingSpawns = Composition.TotalEnemies;
                _powerUpGrantedThisRound = false;

                bool phaseChanges = MapPhases.IsTransitionRound(Round);
                mapPhase?.ApplyRound(Round, instant: false);

                SetState(GameState.RoundIntro);
                Audio.AudioDirector.Instance?.Play2D(Audio.AudioCue.RoundStart);
                RoundStarted?.Invoke(Round, Composition);

                if (phaseChanges)
                {
                    PhaseAnnounced?.Invoke(Composition.Phase, MapPhases.Headline(Composition.Phase));
                }

                string objective = BuildObjective(Composition);
                float intro = RoundPlan.IntroDurationFor(Round);
                ObjectiveChanged?.Invoke(objective, intro);

                yield return WaitPausable(intro);

                SetState(GameState.Combat);
                yield return RunCombat();

                if (State == GameState.Defeat)
                {
                    yield break;
                }

                SetState(GameState.RoundClear);
                int bonus = RoundPlan.ClearBonusFor(Round);
                wallet?.AwardFlat(bonus);
                Audio.AudioDirector.Instance?.Play2D(Audio.AudioCue.RoundComplete);
                RoundCleared?.Invoke(Round);
                ObjectiveChanged?.Invoke($"Round {Round} clear.  +{bonus} salvage.  Spend it.", roundClearSeconds);

                if (Round >= finalRound)
                {
                    yield return WaitPausable(roundClearSeconds);
                    SetState(GameState.RunComplete);
                    RunEnded?.Invoke(Round, KillsThisRun);
                    yield break;
                }

                yield return WaitPausable(roundClearSeconds);
            }
        }

        private IEnumerator RunCombat()
        {
            float spawnTimer = 0f;

            while (RemainingThisRound > 0)
            {
                if (State == GameState.Defeat)
                {
                    yield break;
                }

                if (!IsPaused)
                {
                    spawnTimer -= Time.deltaTime;

                    if (spawnTimer <= 0f && PendingSpawns > 0 && AliveEnemies < Composition.MaxConcurrent)
                    {
                        int room = Composition.MaxConcurrent - AliveEnemies;
                        int burst = Mathf.Min(spawnBurstSize, room, PendingSpawns);

                        for (int i = 0; i < burst; i++)
                        {
                            if (!SpawnNext())
                            {
                                break;
                            }
                        }

                        spawnTimer = Composition.SpawnInterval;
                    }
                }

                yield return null;
            }
        }

        private bool SpawnNext()
        {
            EnemyArchetype archetype;

            // Brutes come in first so the elite is the thing the player meets, not a
            // straggler that shows up after the wave is already thinned.
            if (_brutesLeft > 0)
            {
                archetype = EnemyArchetype.StormBrute;
            }
            else if (_sprintersLeft > 0 && (_shamblersLeft == 0 || UnityEngine.Random.value < 0.45f))
            {
                archetype = EnemyArchetype.Sprinter;
            }
            else if (_shamblersLeft > 0)
            {
                archetype = EnemyArchetype.Shambler;
            }
            else
            {
                return false;
            }

            EnemyBrain spawned = enemies?.Spawn(
                archetype,
                Composition.HealthScale,
                Composition.SpeedScale,
                Composition.DamageScale,
                PreferredZoneForRound());

            if (spawned == null)
            {
                return false;
            }

            switch (archetype)
            {
                case EnemyArchetype.StormBrute: _brutesLeft--; break;
                case EnemyArchetype.Sprinter: _sprintersLeft--; break;
                default: _shamblersLeft--; break;
            }

            PendingSpawns = Mathf.Max(0, PendingSpawns - 1);
            return true;
        }

        private Nav.StationZone? PreferredZoneForRound()
        {
            // Bias each phase toward the route it just opened so the map change is felt
            // as pressure from a new direction, not only as new scenery.
            switch (Composition.Phase)
            {
                case Core.MapPhase.Breach: return Nav.StationZone.LabWing;
                case Core.MapPhase.Surge: return Nav.StationZone.GeneratorRoom;
                case Core.MapPhase.Blackout: return Nav.StationZone.Catwalk;
                case Core.MapPhase.Meridian: return Nav.StationZone.Rooftop;
                default: return null;
            }
        }

        private static string BuildObjective(RoundComposition c)
        {
            if (c.IsEliteRound)
            {
                return c.BruteCount == 1
                    ? $"Round {c.Round}: a Storm Brute is coming in with the wave. Do not let it corner you."
                    : $"Round {c.Round}: {c.BruteCount} Storm Brutes inbound. Keep moving.";
            }

            if (c.SprinterCount > 0)
            {
                return $"Round {c.Round}: {c.TotalEnemies} contacts, {c.SprinterCount} of them fast.";
            }

            return $"Round {c.Round}: {c.TotalEnemies} contacts inbound.";
        }

        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------

        private void HandleEnemyKilled(EnemyBrain brain, GameObject killer)
        {
            KillsThisRun++;

            if (brain?.Definition != null)
            {
                int reward = Mathf.RoundToInt(brain.Definition.salvageValue * 0.55f) + Composition.SalvagePerKill / 2;
                wallet?.Award(reward);
                TryDropPowerUp(brain);
            }
        }

        private void TryDropPowerUp(EnemyBrain brain)
        {
            if (powerUps == null || brain.Definition == null)
            {
                return;
            }

            bool guaranteed = !_powerUpGrantedThisRound
                              && RoundPlan.GuaranteesPowerUp(Round)
                              && RemainingThisRound <= Mathf.Max(1, Composition.TotalEnemies / 3);

            if (guaranteed || UnityEngine.Random.value < brain.Definition.powerUpDropChance)
            {
                _powerUpGrantedThisRound = true;
                powerUps.DropRandom(brain.transform.position);
            }
        }

        private void HandlePlayerDied()
        {
            if (State == GameState.Defeat)
            {
                return;
            }

            if (_flow != null)
            {
                StopCoroutine(_flow);
                _flow = null;
            }

            StartCoroutine(DefeatFlow());
        }

        private IEnumerator DefeatFlow()
        {
            SetState(GameState.Defeat);
            player?.SetControlEnabled(false);
            GamepadRumble.Pulse(0.9f, 0.6f, 0.6f);
            ObjectiveChanged?.Invoke("Signal lost.", defeatHoldSeconds);

            yield return new WaitForSecondsRealtime(defeatHoldSeconds);

            enemies?.DespawnAll();
            RunEnded?.Invoke(Round, KillsThisRun);
            AshfallInput.Instance.SetCursorLocked(false);
        }

        // ------------------------------------------------------------------
        // Pause
        // ------------------------------------------------------------------

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused)
            {
                return;
            }

            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            Audio.AudioDirector.Instance?.SetPaused(paused);
            player?.SetControlEnabled(!paused && !State.IsRunOver());
            AshfallInput.Instance.SetCursorLocked(!paused && !State.IsRunOver());

            if (paused)
            {
                GamepadRumble.StopAll();
            }
        }

        public void TogglePause()
        {
            if (State.IsRunOver())
            {
                return;
            }

            SetPaused(!IsPaused);
        }

        private void SetState(GameState next)
        {
            if (State == next)
            {
                return;
            }

            State = next;
            StateTimer = 0f;
            StateChanged?.Invoke(next);
        }

        private void Update()
        {
            if (!IsPaused)
            {
                StateTimer += Time.deltaTime;
            }
        }

        /// <summary>Yields for a duration that respects the pause state.</summary>
        private IEnumerator WaitPausable(float seconds)
        {
            float remaining = seconds;
            while (remaining > 0f)
            {
                if (!IsPaused)
                {
                    remaining -= Time.deltaTime;
                }

                yield return null;
            }
        }
    }
}
