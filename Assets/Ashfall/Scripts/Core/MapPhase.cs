using UnityEngine;

namespace Ashfall.Core
{
    /// <summary>
    /// The authored states the Black Meridian station moves through. Each phase is a
    /// visible, physical change to the map -- not a stat multiplier.
    /// </summary>
    public enum MapPhase
    {
        /// <summary>Rounds 1-2. Courtyard only, emergency amber lighting, shutters down.</summary>
        Standby = 0,

        /// <summary>Rounds 3-5. Lab wing shutter fails open, first storm lamps ignite.</summary>
        Breach = 1,

        /// <summary>Rounds 6-8. Generator room floods with power, hazard strobes, rain intensifies.</summary>
        Surge = 2,

        /// <summary>Rounds 9-11. Mains fail: amber dies, teal storm light takes the station.</summary>
        Blackout = 3,

        /// <summary>Round 12+. Rooftop lane fully exposed, storm column overhead, everything lit teal.</summary>
        Meridian = 4
    }

    public static class MapPhases
    {
        /// <summary>Round at which each phase becomes active. Index matches the enum.</summary>
        public static readonly int[] StartRounds = { 1, 3, 6, 9, 12 };

        public const int Count = 5;

        public static MapPhase ForRound(int round)
        {
            MapPhase phase = MapPhase.Standby;
            for (int i = 0; i < StartRounds.Length; i++)
            {
                if (round >= StartRounds[i])
                {
                    phase = (MapPhase)i;
                }
            }

            return phase;
        }

        /// <summary>True when entering <paramref name="round"/> crosses a phase boundary.</summary>
        public static bool IsTransitionRound(int round)
        {
            for (int i = 0; i < StartRounds.Length; i++)
            {
                if (StartRounds[i] == round)
                {
                    return true;
                }
            }

            return false;
        }

        public static string DisplayName(MapPhase phase)
        {
            switch (phase)
            {
                case MapPhase.Standby: return "STANDBY";
                case MapPhase.Breach: return "BREACH";
                case MapPhase.Surge: return "SURGE";
                case MapPhase.Blackout: return "BLACKOUT";
                case MapPhase.Meridian: return "BLACK MERIDIAN";
                default: return phase.ToString().ToUpperInvariant();
            }
        }

        /// <summary>Short line shown in the HUD objective slot when a phase begins.</summary>
        public static string Headline(MapPhase phase)
        {
            switch (phase)
            {
                case MapPhase.Standby:
                    return "Hold the courtyard. Board the breaches for salvage.";
                case MapPhase.Breach:
                    return "Lab wing shutter has failed open. Storm lamps are live.";
                case MapPhase.Surge:
                    return "Generator room is hot. Something heavy came in with the tide.";
                case MapPhase.Blackout:
                    return "Mains are gone. Only the storm is lighting the station now.";
                case MapPhase.Meridian:
                    return "The meridian is open. Rooftop is fully exposed. Survive it.";
                default:
                    return string.Empty;
            }
        }

        public static Color Tint(MapPhase phase)
        {
            switch (phase)
            {
                case MapPhase.Standby: return AshfallPalette.EmergencyAmber;
                case MapPhase.Breach: return Color.Lerp(AshfallPalette.EmergencyAmber, AshfallPalette.StormTeal, 0.35f);
                case MapPhase.Surge: return Color.Lerp(AshfallPalette.EmergencyAmber, AshfallPalette.StormTeal, 0.6f);
                case MapPhase.Blackout: return Color.Lerp(AshfallPalette.EmergencyAmber, AshfallPalette.StormTeal, 0.85f);
                case MapPhase.Meridian: return AshfallPalette.StormTeal;
                default: return AshfallPalette.StormTeal;
            }
        }
    }
}
