using UnityEngine;

namespace Ashfall.Core
{
    /// <summary>
    /// The single source of truth for the game's colour language. Runtime FX, the HUD
    /// and the editor scene builder all read from here so the whole slice reads as one
    /// deliberate art direction instead of a pile of default-grey primitives.
    ///
    /// Values are authored in linear-friendly sRGB and kept intentionally desaturated
    /// for the environment so the two signal colours -- teal storm and amber emergency
    /// -- carry all of the gameplay readability.
    /// </summary>
    public static class AshfallPalette
    {
        // --- Environment: wet concrete through oxidised metal ------------------
        public static readonly Color ConcreteDark = new Color(0.106f, 0.118f, 0.133f);
        public static readonly Color ConcreteMid = new Color(0.180f, 0.196f, 0.216f);
        public static readonly Color ConcreteLight = new Color(0.278f, 0.298f, 0.318f);
        public static readonly Color WetFloor = new Color(0.078f, 0.090f, 0.106f);
        public static readonly Color MetalOxidised = new Color(0.243f, 0.196f, 0.161f);
        public static readonly Color MetalPainted = new Color(0.153f, 0.204f, 0.216f);
        public static readonly Color RustDeep = new Color(0.310f, 0.161f, 0.090f);

        // --- Signal colours ----------------------------------------------------
        public static readonly Color StormTeal = new Color(0.239f, 0.878f, 0.855f);
        public static readonly Color StormTealDeep = new Color(0.086f, 0.427f, 0.478f);
        public static readonly Color EmergencyAmber = new Color(1.000f, 0.627f, 0.204f);
        public static readonly Color EmergencyAmberDeep = new Color(0.549f, 0.278f, 0.043f);
        public static readonly Color HazardYellow = new Color(0.898f, 0.749f, 0.153f);
        public static readonly Color HazardStripe = new Color(0.098f, 0.098f, 0.110f);
        public static readonly Color WarningRed = new Color(0.902f, 0.243f, 0.216f);

        // --- Gameplay feedback -------------------------------------------------
        public static readonly Color SalvageGreen = new Color(0.514f, 0.902f, 0.435f);
        public static readonly Color OverchargeViolet = new Color(0.702f, 0.451f, 1.000f);
        public static readonly Color LastStandGold = new Color(1.000f, 0.851f, 0.400f);
        public static readonly Color EnemyFlesh = new Color(0.318f, 0.298f, 0.290f);
        public static readonly Color ViewmodelSkin = new Color(0.360f, 0.225f, 0.165f);
        public static readonly Color EnemyCorrupt = new Color(0.180f, 0.639f, 0.612f);
        public static readonly Color BruteArmour = new Color(0.184f, 0.169f, 0.176f);
        public static readonly Color Blood = new Color(0.361f, 0.078f, 0.098f);

        // --- Atmosphere --------------------------------------------------------
        public static readonly Color FogCalm = new Color(0.075f, 0.098f, 0.118f);
        public static readonly Color FogStorm = new Color(0.055f, 0.106f, 0.125f);
        public static readonly Color SkyHorizon = new Color(0.129f, 0.161f, 0.192f);
        public static readonly Color SkyZenith = new Color(0.035f, 0.047f, 0.067f);
        public static readonly Color MoonKey = new Color(0.596f, 0.729f, 0.851f);

        // --- HUD ---------------------------------------------------------------
        public static readonly Color HudInk = new Color(0.878f, 0.925f, 0.933f);
        public static readonly Color HudInkDim = new Color(0.545f, 0.612f, 0.635f);
        public static readonly Color HudPanel = new Color(0.043f, 0.055f, 0.067f, 0.72f);
        public static readonly Color HudAccent = StormTeal;
        public static readonly Color HudDanger = new Color(0.980f, 0.310f, 0.286f);

        /// <summary>Deterministic small variation so tiled primitives never look stamped.</summary>
        public static Color Jitter(Color baseColor, int seed, float amount = 0.05f)
        {
            var rng = new System.Random(seed);
            float d = ((float)rng.NextDouble() - 0.5f) * 2f * amount;
            return new Color(
                Mathf.Clamp01(baseColor.r + d),
                Mathf.Clamp01(baseColor.g + d),
                Mathf.Clamp01(baseColor.b + d),
                baseColor.a);
        }
    }
}
