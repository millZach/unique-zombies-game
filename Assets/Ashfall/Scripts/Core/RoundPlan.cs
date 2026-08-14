using UnityEngine;

namespace Ashfall.Core
{
    public enum EnemyArchetype
    {
        Shambler = 0,
        Sprinter = 1,
        StormBrute = 2
    }

    /// <summary>
    /// Everything the spawner needs to run one round. Produced purely from the round
    /// number so the whole progression curve is testable without a scene.
    /// </summary>
    public struct RoundComposition
    {
        public int Round;
        public MapPhase Phase;

        public int ShamblerCount;
        public int SprinterCount;
        public int BruteCount;

        public int MaxConcurrent;
        public float SpawnInterval;
        public float HealthScale;
        public float SpeedScale;
        public float DamageScale;
        public int SalvagePerKill;

        public int TotalEnemies => ShamblerCount + SprinterCount + BruteCount;
        public bool IsEliteRound => BruteCount > 0;
    }

    /// <summary>
    /// The progression curve for Ashfall's twelve-round vertical slice.
    ///
    /// Design intent:
    ///  - Round 1 is short and teaches the loop without threatening the player.
    ///  - Sprinters arrive with the Breach phase (round 3) so the map change and the
    ///    new threat land together.
    ///  - The first Storm Brute arrives on round 6, the Surge phase.
    ///  - Round 12 is the crescendo, not an unwinnable wall.
    ///
    /// All numbers live here as constants so they can be tuned in one place and
    /// asserted in edit-mode tests.
    /// </summary>
    public static class RoundPlan
    {
        public const int FirstRound = 1;
        public const int FinalRound = 12;

        public const int SprinterFirstRound = 3;
        public const int BruteFirstRound = 6;

        private const int BaseEnemies = 5;
        private const float LinearGrowth = 1.85f;
        private const float QuadraticGrowth = 0.16f;

        public static RoundComposition For(int round)
        {
            round = Mathf.Max(FirstRound, round);

            var c = new RoundComposition
            {
                Round = round,
                Phase = MapPhases.ForRound(round)
            };

            int total = TotalEnemiesFor(round);

            c.BruteCount = BruteCountFor(round);
            // Brutes are worth several bodies of pressure; trade them against the pool
            // so an elite round does not simply stack everything at once.
            int remaining = Mathf.Max(1, total - c.BruteCount * 3);

            float sprinterShare = SprinterShareFor(round);
            c.SprinterCount = Mathf.RoundToInt(remaining * sprinterShare);
            c.ShamblerCount = Mathf.Max(0, remaining - c.SprinterCount);

            c.MaxConcurrent = Mathf.Clamp(4 + Mathf.FloorToInt(round * 1.15f), 5, 18);
            c.SpawnInterval = Mathf.Lerp(2.35f, 0.65f, Mathf.InverseLerp(FirstRound, FinalRound, round));
            c.HealthScale = HealthScaleFor(round);
            c.SpeedScale = Mathf.Min(1.35f, 1f + 0.028f * (round - 1));
            c.DamageScale = Mathf.Min(2.0f, 1f + 0.075f * (round - 1));
            c.SalvagePerKill = 55 + 5 * Mathf.Min(round - 1, 10);

            return c;
        }

        public static int TotalEnemiesFor(int round)
        {
            round = Mathf.Max(FirstRound, round);
            float raw = BaseEnemies + LinearGrowth * (round - 1) + QuadraticGrowth * (round - 1) * (round - 1);
            return Mathf.Max(4, Mathf.RoundToInt(raw));
        }

        public static int BruteCountFor(int round)
        {
            if (round < BruteFirstRound)
            {
                return 0;
            }

            // 6 -> 1, 7/8 -> 1, 9-11 -> 2, 12+ -> 3
            if (round >= 12) return 3;
            if (round >= 9) return 2;
            return 1;
        }

        public static float SprinterShareFor(int round)
        {
            if (round < SprinterFirstRound)
            {
                return 0f;
            }

            // Ramps from a fifth of the wave up to just over half by the final round.
            return Mathf.Clamp(0.20f + 0.035f * (round - SprinterFirstRound), 0f, 0.55f);
        }

        public static float HealthScaleFor(int round)
        {
            round = Mathf.Max(FirstRound, round);

            // Linear while the player is still assembling a loadout, then gently
            // compounding so late rounds demand the shotgun/rifle rather than the pistol.
            if (round <= 9)
            {
                return 1f + 0.16f * (round - 1);
            }

            float atNine = 1f + 0.16f * 8f;
            return atNine * Mathf.Pow(1.11f, round - 9);
        }

        /// <summary>Seconds of breathing room granted before a round's first spawn.</summary>
        public static float IntroDurationFor(int round)
        {
            return MapPhases.IsTransitionRound(round) ? 7.0f : 4.0f;
        }

        /// <summary>Bonus salvage paid out for surviving a round.</summary>
        public static int ClearBonusFor(int round)
        {
            return 60 + 25 * round + (MapPhases.IsTransitionRound(round) ? 150 : 0);
        }

        /// <summary>
        /// Rounds where a power-up drop is guaranteed, on top of the random per-kill roll.
        /// Keeps the slice from ever going dry on power-ups in a short session.
        /// </summary>
        public static bool GuaranteesPowerUp(int round)
        {
            return round == 4 || round == 6 || round == 8 || round == 10 || round == 12;
        }
    }
}
