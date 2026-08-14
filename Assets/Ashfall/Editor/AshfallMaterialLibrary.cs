using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Ashfall.Core;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// The station's material set, built once and reused by every piece of geometry.
    ///
    /// A small, shared palette of materials is what makes procedural primitives read as
    /// a designed space: the same six surfaces recurring everywhere is how real
    /// industrial architecture looks, and it keeps draw calls batching.
    /// </summary>
    public static class AshfallMaterialLibrary
    {
        public static Material ConcreteWall { get; private set; }
        public static Material ConcreteFloor { get; private set; }
        public static Material WetFloor { get; private set; }
        public static Material SteelPanel { get; private set; }
        public static Material SteelDark { get; private set; }
        public static Material RustedMetal { get; private set; }
        public static Material HazardPaint { get; private set; }
        public static Material TreadPlate { get; private set; }
        public static Material Timber { get; private set; }
        public static Material GlassDirty { get; private set; }
        public static Material EmissiveTeal { get; private set; }
        public static Material EmissiveAmber { get; private set; }
        public static Material EmissiveRed { get; private set; }
        public static Material EmissiveGreen { get; private set; }
        public static Material EnemyFlesh { get; private set; }
        public static Material ViewmodelSkin { get; private set; }
        public static Material EnemyCorrupt { get; private set; }
        public static Material BruteArmour { get; private set; }
        public static Material GunBody { get; private set; }
        public static Material GunAccent { get; private set; }
        public static Material FxAdditive { get; private set; }
        public static Material Skybox { get; private set; }

        private static readonly List<Material> All = new();

        private static Shader _lit;
        private static Shader _unlit;

        public static IReadOnlyList<Material> AllMaterials => All;

        public static void BuildAll()
        {
            All.Clear();
            AshfallAssetUtility.EnsureFolder(AshfallAssetUtility.MaterialFolder);

            _lit = Shader.Find("Universal Render Pipeline/Lit");
            _unlit = Shader.Find("Universal Render Pipeline/Unlit");

            if (_lit == null)
            {
                // Falls back to the built-in standard shader so the builder still runs if
                // URP has not finished importing for some reason.
                _lit = Shader.Find("Standard");
                Debug.LogWarning("[Ashfall] URP Lit shader not found; falling back to Standard.");
            }

            ConcreteWall = Make("M_ConcreteWall", AshfallPalette.ConcreteMid, 0f, 0.16f,
                AshfallTextureFactory.Concrete, AshfallTextureFactory.ConcreteNormal, 0.7f);

            ConcreteFloor = Make("M_ConcreteFloor", AshfallPalette.ConcreteDark, 0f, 0.24f,
                AshfallTextureFactory.GridLines, AshfallTextureFactory.ConcreteNormal, 0.5f);

            WetFloor = Make("M_WetFloor", Color.white, 0.05f, 0.62f,
                AshfallTextureFactory.WetFloor, AshfallTextureFactory.ConcreteNormal, 0.35f);
            UseAlbedoAlphaSmoothness(WetFloor);

            SteelPanel = Make("M_SteelPanel", Color.white, 0.72f, 0.42f,
                AshfallTextureFactory.SteelPanel, AshfallTextureFactory.SteelPanelNormal, 1.1f);
            UseAlbedoAlphaSmoothness(SteelPanel);

            SteelDark = Make("M_SteelDark", AshfallPalette.ConcreteDark * 1.1f, 0.85f, 0.30f,
                AshfallTextureFactory.SteelPanel, AshfallTextureFactory.SteelPanelNormal, 0.8f);

            RustedMetal = Make("M_RustedMetal", Color.white, 0.35f, 0.22f,
                AshfallTextureFactory.RustWash, AshfallTextureFactory.SteelPanelNormal, 0.9f);
            UseAlbedoAlphaSmoothness(RustedMetal);

            HazardPaint = Make("M_HazardPaint", Color.white, 0.10f, 0.34f,
                AshfallTextureFactory.HazardChevron, null, 0f);

            TreadPlate = Make("M_TreadPlate", Color.white, 0.68f, 0.36f,
                AshfallTextureFactory.TreadPlate, null, 0f);
            UseAlbedoAlphaSmoothness(TreadPlate);

            Timber = Make("M_Timber", new Color(0.290f, 0.212f, 0.145f), 0f, 0.14f,
                AshfallTextureFactory.Concrete, AshfallTextureFactory.ConcreteNormal, 0.45f);

            GlassDirty = MakeTransparent("M_GlassDirty", new Color(0.36f, 0.45f, 0.47f, 0.28f), 0.15f, 0.86f);

            EmissiveTeal = MakeEmissive("M_EmissiveTeal", AshfallPalette.StormTeal, 3.4f);
            EmissiveAmber = MakeEmissive("M_EmissiveAmber", AshfallPalette.EmergencyAmber, 3.0f);
            EmissiveRed = MakeEmissive("M_EmissiveRed", AshfallPalette.WarningRed, 2.6f);
            EmissiveGreen = MakeEmissive("M_EmissiveGreen", AshfallPalette.SalvageGreen, 2.6f);

            EnemyFlesh = Make("M_EnemyFlesh", AshfallPalette.EnemyFlesh, 0.02f, 0.20f,
                AshfallTextureFactory.Concrete, AshfallTextureFactory.ConcreteNormal, 0.35f);
            // Barely-there rim of corruption. Anything stronger and the body reads as
            // emissive too, which erases the contrast against the glowing veins.
            EnableEmission(EnemyFlesh, AshfallPalette.EnemyCorrupt * 0.06f);

            ViewmodelSkin = Make("M_ViewmodelSkin", AshfallPalette.ViewmodelSkin, 0.0f, 0.28f,
                AshfallTextureFactory.Concrete, AshfallTextureFactory.ConcreteNormal, 0.20f);

            EnemyCorrupt = MakeEmissive("M_EnemyCorrupt", AshfallPalette.EnemyCorrupt, 2.4f);
            EnemyCorrupt.SetColor(BaseColorId, AshfallPalette.EnemyCorrupt * 0.35f);

            BruteArmour = Make("M_BruteArmour", AshfallPalette.BruteArmour, 0.55f, 0.30f,
                AshfallTextureFactory.SteelPanel, AshfallTextureFactory.SteelPanelNormal, 1.0f);
            EnableEmission(BruteArmour, AshfallPalette.StormTeal * 0.05f);

            GunBody = Make("M_GunBody", new Color(0.106f, 0.114f, 0.122f), 0.78f, 0.52f, null, null, 0f);
            GunAccent = MakeEmissive("M_GunAccent", AshfallPalette.StormTeal, 2.0f);

            FxAdditive = MakeAdditive("M_FxAdditive", Color.white);
            Skybox = MakeSkybox("M_StormSky");

            AssetDatabase.SaveAssets();
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int BumpScaleId = Shader.PropertyToID("_BumpScale");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int BlendId = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int SmoothnessSourceId = Shader.PropertyToID("_SmoothnessTextureChannel");

        private static Material Persist(string name, Material material)
        {
            string path = $"{AshfallAssetUtility.MaterialFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = material.shader;
                existing.CopyPropertiesFromMaterial(material);
                existing.shaderKeywords = material.shaderKeywords;
                existing.globalIlluminationFlags = material.globalIlluminationFlags;
                existing.renderQueue = material.renderQueue;
                Object.DestroyImmediate(material);
                EditorUtility.SetDirty(existing);
                All.Add(existing);
                return existing;
            }

            material.name = name;
            AssetDatabase.CreateAsset(material, path);
            All.Add(material);
            return material;
        }

        private static Material Make(
            string name, Color baseColor, float metallic, float smoothness,
            Texture2D albedo, Texture2D normal, float normalStrength)
        {
            var m = new Material(_lit);
            m.SetColor(BaseColorId, baseColor);
            m.SetFloat(MetallicId, metallic);
            m.SetFloat(SmoothnessId, smoothness);

            if (albedo != null)
            {
                m.SetTexture(BaseMapId, albedo);
                m.mainTexture = albedo;
            }

            if (normal != null && normalStrength > 0f)
            {
                m.SetTexture(BumpMapId, normal);
                m.SetFloat(BumpScaleId, normalStrength);
                m.EnableKeyword("_NORMALMAP");
            }

            return Persist(name, m);
        }

        private static Material MakeEmissive(string name, Color color, float intensity)
        {
            var m = new Material(_lit);
            m.SetColor(BaseColorId, color * 0.25f);
            m.SetFloat(MetallicId, 0f);
            m.SetFloat(SmoothnessId, 0.55f);
            EnableEmission(m, color * intensity);
            return Persist(name, m);
        }

        private static Material MakeTransparent(string name, Color color, float metallic, float smoothness)
        {
            var m = new Material(_lit);
            m.SetColor(BaseColorId, color);
            m.SetFloat(MetallicId, metallic);
            m.SetFloat(SmoothnessId, smoothness);
            m.SetFloat(SurfaceId, 1f);
            m.SetFloat(BlendId, 0f);
            m.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat(ZWriteId, 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return Persist(name, m);
        }

        private static Material MakeAdditive(string name, Color color)
        {
            Shader shader = _unlit != null ? _unlit : _lit;
            var m = new Material(shader);
            m.SetColor(BaseColorId, color);
            m.SetFloat(SurfaceId, 1f);
            m.SetFloat(BlendId, 1f);
            m.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat(ZWriteId, 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 50;
            return Persist(name, m);
        }

        private static Material MakeSkybox(string name)
        {
            Shader shader = Shader.Find("Skybox/Procedural");
            if (shader == null)
            {
                return null;
            }

            var m = new Material(shader);
            m.SetFloat("_SunSize", 0.02f);
            m.SetFloat("_SunSizeConvergence", 12f);
            m.SetFloat("_AtmosphereThickness", 1.55f);
            m.SetColor("_SkyTint", AshfallPalette.SkyHorizon);
            m.SetColor("_GroundColor", AshfallPalette.SkyZenith);
            m.SetFloat("_Exposure", 0.34f);
            return Persist(name, m);
        }

        private static void EnableEmission(Material m, Color emission)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor(EmissionColorId, emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        /// <summary>URP Lit: read smoothness from the albedo alpha channel rather than a scalar.</summary>
        private static void UseAlbedoAlphaSmoothness(Material m)
        {
            if (m.HasProperty(SmoothnessSourceId))
            {
                m.SetFloat(SmoothnessSourceId, 0f);
            }

            m.SetFloat(SmoothnessId, 1f);
        }
    }
}
