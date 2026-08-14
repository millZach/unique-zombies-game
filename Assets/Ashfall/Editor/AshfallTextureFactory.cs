using System.IO;
using UnityEditor;
using UnityEngine;
using Ashfall.Core;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// Generates the project's surface textures from code.
    ///
    /// Nothing is downloaded and nothing is hand-painted: concrete, panelled steel,
    /// hazard chevrons and tread plate are all synthesised from value noise and simple
    /// analytic patterns, written out as PNGs, and imported normally. That keeps the
    /// repository buildable from a clean clone while still giving surfaces enough
    /// variation to stop reading as flat colour.
    /// </summary>
    public static class AshfallTextureFactory
    {
        public const string TextureFolder = "Assets/Ashfall/Art/Generated/Textures";

        private const int Size = 512;

        public static Texture2D Concrete { get; private set; }
        public static Texture2D ConcreteNormal { get; private set; }
        public static Texture2D WetFloor { get; private set; }
        public static Texture2D SteelPanel { get; private set; }
        public static Texture2D SteelPanelNormal { get; private set; }
        public static Texture2D HazardChevron { get; private set; }
        public static Texture2D TreadPlate { get; private set; }
        public static Texture2D RustWash { get; private set; }
        public static Texture2D GridLines { get; private set; }

        public static void GenerateAll()
        {
            AshfallAssetUtility.EnsureFolder(TextureFolder);

            Concrete = Write("T_Concrete", BuildConcrete());
            ConcreteNormal = WriteNormal("T_Concrete_N", BuildConcreteHeight());
            WetFloor = Write("T_WetFloor", BuildWetFloor());
            SteelPanel = Write("T_SteelPanel", BuildSteelPanel());
            SteelPanelNormal = WriteNormal("T_SteelPanel_N", BuildSteelPanelHeight());
            HazardChevron = Write("T_HazardChevron", BuildHazardChevron());
            TreadPlate = Write("T_TreadPlate", BuildTreadPlate());
            RustWash = Write("T_RustWash", BuildRustWash());
            GridLines = Write("T_GridLines", BuildGridLines());
        }

        // ------------------------------------------------------------------
        // Noise
        // ------------------------------------------------------------------

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int n = x * 374761393 + y * 668265263 + seed * 69069;
                n = (n ^ (n >> 13)) * 1274126177;
                n ^= n >> 16;
                return (n & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        /// <summary>Tiling value noise: wraps cleanly at <paramref name="period"/>.</summary>
        private static float ValueNoise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = x - xi;
            float yf = y - yi;

            int x0 = ((xi % period) + period) % period;
            int y0 = ((yi % period) + period) % period;
            int x1 = (x0 + 1) % period;
            int y1 = (y0 + 1) % period;

            float u = xf * xf * (3f - 2f * xf);
            float v = yf * yf * (3f - 2f * yf);

            float a = Hash(x0, y0, seed);
            float b = Hash(x1, y0, seed);
            float c = Hash(x0, y1, seed);
            float d = Hash(x1, y1, seed);

            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        private static float Fbm(float x, float y, int basePeriod, int octaves, int seed, float gain = 0.5f)
        {
            float sum = 0f;
            float amplitude = 1f;
            float total = 0f;
            int period = basePeriod;

            for (int o = 0; o < octaves; o++)
            {
                sum += ValueNoise(x * period, y * period, period, seed + o * 17) * amplitude;
                total += amplitude;
                amplitude *= gain;
                period *= 2;
            }

            return sum / Mathf.Max(0.0001f, total);
        }

        // ------------------------------------------------------------------
        // Patterns
        // ------------------------------------------------------------------

        private static Color[] BuildConcrete()
        {
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size;
                    float v = y / (float)Size;

                    float grain = Fbm(u, v, 8, 5, 101);
                    float blotch = Fbm(u, v, 3, 3, 233);
                    float speckle = Fbm(u, v, 64, 2, 71) > 0.78f ? 0.10f : 0f;

                    // Faint horizontal form-work seams every quarter of the tile.
                    float seam = Mathf.Abs(Mathf.Sin(v * Mathf.PI * 4f));
                    float seamDark = Mathf.SmoothStep(1f, 0.94f, seam) * 0.09f;

                    float value = 0.72f + (grain - 0.5f) * 0.28f + (blotch - 0.5f) * 0.20f + speckle - seamDark;
                    value = Mathf.Clamp01(value);

                    Color c = AshfallPalette.ConcreteMid * value * 1.55f;
                    c.a = Mathf.Clamp01(0.16f + (1f - grain) * 0.18f);
                    pixels[y * Size + x] = c;
                }
            }

            return pixels;
        }

        private static float[] BuildConcreteHeight()
        {
            var height = new float[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size;
                    float v = y / (float)Size;
                    float grain = Fbm(u, v, 16, 4, 101);
                    float pit = Fbm(u, v, 48, 2, 311) > 0.80f ? -0.35f : 0f;
                    height[y * Size + x] = grain * 0.35f + pit;
                }
            }

            return height;
        }

        private static Color[] BuildWetFloor()
        {
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size;
                    float v = y / (float)Size;

                    float grain = Fbm(u, v, 12, 4, 907);
                    float puddle = Fbm(u, v, 4, 3, 1301);
                    float wet = Mathf.SmoothStep(0.46f, 0.66f, puddle);

                    Color dry = AshfallPalette.ConcreteDark * (0.85f + (grain - 0.5f) * 0.4f) * 1.6f;
                    Color soaked = AshfallPalette.WetFloor * 1.1f;
                    Color c = Color.Lerp(dry, soaked, wet);

                    // Alpha carries smoothness: puddles are glossy, dry slab is not.
                    c.a = Mathf.Lerp(0.10f, 0.92f, wet);
                    pixels[y * Size + x] = c;
                }
            }

            return pixels;
        }

        private static Color[] BuildSteelPanel()
        {
            var pixels = new Color[Size * Size];
            const int panels = 4;
            int panelSize = Size / panels;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size;
                    float v = y / (float)Size;

                    int px = x % panelSize;
                    int py = y % panelSize;
                    int panelIndex = (x / panelSize) + (y / panelSize) * panels;

                    float edge = Mathf.Min(Mathf.Min(px, panelSize - 1 - px), Mathf.Min(py, panelSize - 1 - py));
                    float seam = Mathf.SmoothStep(0f, 3f, edge);

                    // Rivets in each panel corner.
                    float rivet = 0f;
                    float rivetInset = panelSize * 0.11f;
                    Vector2[] corners =
                    {
                        new(rivetInset, rivetInset),
                        new(panelSize - rivetInset, rivetInset),
                        new(rivetInset, panelSize - rivetInset),
                        new(panelSize - rivetInset, panelSize - rivetInset)
                    };
                    for (int i = 0; i < corners.Length; i++)
                    {
                        float d = Vector2.Distance(new Vector2(px, py), corners[i]);
                        rivet = Mathf.Max(rivet, Mathf.SmoothStep(3.2f, 1.2f, d));
                    }

                    float grain = Fbm(u, v, 32, 3, 431 + panelIndex);
                    float rust = Mathf.SmoothStep(0.58f, 0.86f, Fbm(u, v, 6, 4, 617));

                    float value = 0.68f + (grain - 0.5f) * 0.22f;
                    value *= Mathf.Lerp(0.55f, 1f, seam);
                    value += rivet * 0.22f;

                    Color steel = AshfallPalette.MetalPainted * value * 1.7f;
                    Color c = Color.Lerp(steel, AshfallPalette.RustDeep * 1.25f, rust * 0.72f);
                    c.a = Mathf.Clamp01(Mathf.Lerp(0.55f, 0.14f, rust) + rivet * 0.2f);
                    pixels[y * Size + x] = c;
                }
            }

            return pixels;
        }

        private static float[] BuildSteelPanelHeight()
        {
            var height = new float[Size * Size];
            const int panels = 4;
            int panelSize = Size / panels;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int px = x % panelSize;
                    int py = y % panelSize;
                    float edge = Mathf.Min(Mathf.Min(px, panelSize - 1 - px), Mathf.Min(py, panelSize - 1 - py));
                    float seam = Mathf.SmoothStep(0f, 3f, edge);

                    float rivet = 0f;
                    float rivetInset = panelSize * 0.11f;
                    Vector2[] corners =
                    {
                        new(rivetInset, rivetInset),
                        new(panelSize - rivetInset, rivetInset),
                        new(rivetInset, panelSize - rivetInset),
                        new(panelSize - rivetInset, panelSize - rivetInset)
                    };
                    for (int i = 0; i < corners.Length; i++)
                    {
                        float d = Vector2.Distance(new Vector2(px, py), corners[i]);
                        rivet = Mathf.Max(rivet, Mathf.SmoothStep(3.4f, 1.0f, d));
                    }

                    height[y * Size + x] = seam * 0.55f + rivet * 0.45f;
                }
            }

            return height;
        }

        private static Color[] BuildHazardChevron()
        {
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size;
                    float v = y / (float)Size;

                    // Diagonal bands, 8 per tile, with a soft anti-aliased edge.
                    float band = Mathf.Repeat((u + v) * 8f, 1f);
                    float mask = Mathf.SmoothStep(0.48f, 0.52f, band);

                    float wear = Fbm(u, v, 24, 4, 811);
                    float scuff = Mathf.SmoothStep(0.35f, 0.72f, wear);

                    Color c = Color.Lerp(AshfallPalette.HazardStripe, AshfallPalette.HazardYellow, mask);
                    c = Color.Lerp(c * 0.55f, c, scuff);
                    c.a = 0.22f;
                    pixels[y * Size + x] = c;
                }
            }

            return pixels;
        }

        private static Color[] BuildTreadPlate()
        {
            var pixels = new Color[Size * Size];
            const int cell = 64;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size;
                    float v = y / (float)Size;

                    int cx = x % cell;
                    int cy = y % cell;
                    bool alternate = ((x / cell) + (y / cell)) % 2 == 0;

                    // A short diagonal bar in each cell, flipping direction like real
                    // diamond plate.
                    float d = alternate
                        ? Mathf.Abs((cx - cy) / 1.4142f)
                        : Mathf.Abs((cx + cy - cell) / 1.4142f);
                    float along = alternate ? (cx + cy) / 2f : (cx - cy + cell) / 2f;
                    bool insideLength = along > cell * 0.22f && along < cell * 0.78f;
                    float bar = insideLength ? Mathf.SmoothStep(7f, 2.5f, d) : 0f;

                    float grain = Fbm(u, v, 24, 3, 1511);
                    float value = 0.40f + (grain - 0.5f) * 0.18f + bar * 0.42f;

                    Color c = AshfallPalette.MetalOxidised * value * 1.5f;
                    c.a = Mathf.Clamp01(0.30f + bar * 0.35f);
                    pixels[y * Size + x] = c;
                }
            }

            return pixels;
        }

        private static Color[] BuildRustWash()
        {
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = x / (float)Size;
                    float v = y / (float)Size;

                    // Streaks pulled downward, as though water has run over the panel.
                    float streak = Fbm(u * 4f, v * 0.55f, 8, 4, 2027);
                    float blotch = Fbm(u, v, 5, 3, 733);
                    float amount = Mathf.Clamp01(streak * 0.65f + blotch * 0.5f);

                    Color c = Color.Lerp(AshfallPalette.MetalOxidised, AshfallPalette.RustDeep, amount) * 1.35f;
                    c.a = Mathf.Lerp(0.30f, 0.06f, amount);
                    pixels[y * Size + x] = c;
                }
            }

            return pixels;
        }

        private static Color[] BuildGridLines()
        {
            var pixels = new Color[Size * Size];
            const int cell = 128;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int cx = x % cell;
                    int cy = y % cell;
                    float edge = Mathf.Min(Mathf.Min(cx, cell - 1 - cx), Mathf.Min(cy, cell - 1 - cy));
                    float line = Mathf.SmoothStep(2.5f, 0.5f, edge);

                    float u = x / (float)Size;
                    float v = y / (float)Size;
                    float grain = Fbm(u, v, 16, 3, 191);

                    Color baseColor = AshfallPalette.ConcreteDark * (0.85f + (grain - 0.5f) * 0.3f) * 1.5f;
                    Color c = Color.Lerp(baseColor, AshfallPalette.ConcreteLight * 1.4f, line * 0.75f);
                    c.a = 0.24f;
                    pixels[y * Size + x] = c;
                }
            }

            return pixels;
        }

        // ------------------------------------------------------------------
        // Writing
        // ------------------------------------------------------------------

        private static Texture2D Write(string name, Color[] pixels)
        {
            string path = $"{TextureFolder}/{name}.png";
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true, false);
            texture.SetPixels(pixels);
            texture.Apply();

            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.mipmapEnabled = true;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.sRGBTexture = true;
                importer.maxTextureSize = Size;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Converts a height field to a tangent-space normal map and imports it as one.</summary>
        private static Texture2D WriteNormal(string name, float[] height)
        {
            string path = $"{TextureFolder}/{name}.png";
            var pixels = new Color[Size * Size];
            const float strength = 2.4f;

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int xm = (x - 1 + Size) % Size;
                    int xp = (x + 1) % Size;
                    int ym = (y - 1 + Size) % Size;
                    int yp = (y + 1) % Size;

                    float dx = (height[y * Size + xp] - height[y * Size + xm]) * strength;
                    float dy = (height[yp * Size + x] - height[ym * Size + x]) * strength;

                    Vector3 n = new Vector3(-dx, -dy, 1f).normalized;
                    pixels[y * Size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true, true);
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = Size;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
