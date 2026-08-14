using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Ashfall.Core;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// Renders model sheets and in-scene frames to PNG, headlessly.
    ///
    /// This project was built without a display, and "verified by construction"
    /// only goes so far for art: dimension checks cannot tell you that a
    /// silhouette reads, and a nav bake cannot tell you a weapon looks like a
    /// weapon. Under a virtual X server this renders the real prefabs through
    /// the real render pipeline, so a geometry change can be looked at rather
    /// than argued about.
    ///
    /// Batch usage (needs a display -- run under xvfb-run, and do NOT pass
    /// -nographics, which disables the graphics device entirely):
    ///
    ///   xvfb-run -a Unity -batchmode -projectPath . \
    ///     -executeMethod Ashfall.EditorTools.AshfallCapture.CaptureFromCommandLine \
    ///     -captureOut /tmp/ashfall-capture -logFile /tmp/capture.log
    /// </summary>
    public static class AshfallCapture
    {
        private const int SheetWidth = 420;
        private const int SheetHeight = 560;
        private const int SceneWidth = 1280;
        private const int SceneHeight = 720;

        private static readonly string[] EnemyPrefabs =
        {
            "Assets/Ashfall/Prefabs/Enemy_Shambler.prefab",
            "Assets/Ashfall/Prefabs/Enemy_Sprinter.prefab",
            "Assets/Ashfall/Prefabs/Enemy_StormBrute.prefab"
        };

        private static readonly string[] WeaponPrefabs =
        {
            "Assets/Ashfall/Prefabs/VM_MeridianSidearm.prefab",
            "Assets/Ashfall/Prefabs/VM_Breakwater.prefab",
            "Assets/Ashfall/Prefabs/VM_Arc9.prefab"
        };

        [MenuItem("Ashfall/Capture Model Sheets", priority = 40)]
        public static void CaptureMenu()
        {
            string outDir = Path.Combine(Path.GetTempPath(), "ashfall-capture");
            Capture(outDir);
            EditorUtility.RevealInFinder(outDir);
        }

        public static void CaptureFromCommandLine()
        {
            string outDir = ArgumentValue("-captureOut") ?? Path.Combine(Path.GetTempPath(), "ashfall-capture");

            try
            {
                int written = Capture(outDir);
                Debug.Log($"[Ashfall] CAPTURE_OK {written} images into {outDir}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Ashfall] CAPTURE_FAILED: {e}");
                EditorApplication.Exit(2);
                return;
            }

            EditorApplication.Exit(0);
        }

        private static string ArgumentValue(string flag)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == flag)
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        public static int Capture(string outDir)
        {
            Directory.CreateDirectory(outDir);

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                throw new System.InvalidOperationException(
                    "No graphics device. Run under a display (xvfb-run) and without -nographics.");
            }

            int written = 0;
            written += CaptureModelSheets(outDir);
            written += CaptureSceneFrames(outDir);
            return written;
        }

        // ------------------------------------------------------------------
        // Model sheets
        // ------------------------------------------------------------------

        private static int CaptureModelSheets(string outDir)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            int written = 0;

            try
            {
                ApplyStudioLighting();
                Camera camera = CreateCamera("Sheet Camera", SheetWidth, SheetHeight);

                foreach (string path in EnemyPrefabs)
                {
                    written += CaptureTurntable(camera, path, outDir, new[] { 205f, 270f, 330f }, 1.20f);
                }

                foreach (string path in WeaponPrefabs)
                {
                    written += CaptureTurntable(camera, path, outDir, new[] { 215f, 270f }, 1.35f);
                }
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, false);
            }

            return written;
        }

        private static int CaptureTurntable(Camera camera, string prefabPath, string outDir, float[] yaws, float margin)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[Ashfall] Capture skipped missing prefab {prefabPath}");
                return 0;
            }

            GameObject instance = Object.Instantiate(prefab);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // Viewmodels live on the FX layer and disable shadow casting; for a
            // model sheet both of those choices work against the picture.
            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
            {
                r.gameObject.layer = 0;
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }

            int written = 0;
            try
            {
                Bounds bounds = RendererBounds(instance);
                if (bounds.size.sqrMagnitude < 1e-6f)
                {
                    Debug.LogWarning($"[Ashfall] Capture found no renderer bounds on {prefabPath}");
                    return 0;
                }

                string name = Path.GetFileNameWithoutExtension(prefabPath);
                foreach (float yaw in yaws)
                {
                    FrameCamera(camera, bounds, yaw, margin);
                    written += WritePng(camera, Path.Combine(outDir, $"{name}_{Mathf.RoundToInt(yaw):000}.png")) ? 1 : 0;
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            return written;
        }

        private static Bounds RendererBounds(GameObject root)
        {
            var renderers = new List<Renderer>();
            root.GetComponentsInChildren(true, renderers);

            var bounds = new Bounds();
            bool started = false;
            for (int i = 0; i < renderers.Count; i++)
            {
                if (renderers[i] == null || renderers[i] is ParticleSystemRenderer || renderers[i] is LineRenderer)
                {
                    continue;
                }

                if (!started)
                {
                    bounds = renderers[i].bounds;
                    started = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }

            return bounds;
        }

        private static void FrameCamera(Camera camera, Bounds bounds, float yaw, float margin)
        {
            float radius = bounds.extents.magnitude * margin;
            float vertical = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            // A slight downward tilt reads more like a character sheet than a
            // dead-level orthographic-looking shot.
            Quaternion orbit = Quaternion.Euler(11f, yaw, 0f);
            Vector3 offset = orbit * Vector3.back * vertical;

            camera.transform.SetPositionAndRotation(bounds.center + offset, orbit);
            camera.nearClipPlane = Mathf.Max(0.01f, vertical - radius * 2f);
            camera.farClipPlane = vertical + radius * 4f;
        }

        // ------------------------------------------------------------------
        // In-scene frames
        // ------------------------------------------------------------------

        private static int CaptureSceneFrames(string outDir)
        {
            Scene scene = EditorSceneManager.OpenScene(AshfallProjectBuilder.ScenePath, OpenSceneMode.Single);
            int written = 0;

            try
            {
                Camera camera = CreateCamera("Capture Camera", SceneWidth, SceneHeight);

                // The player's own camera holds the framing the game actually
                // ships; copying it means the shot is the shot the player sees.
                Camera player = FindPlayerCamera();
                if (player != null)
                {
                    camera.fieldOfView = player.fieldOfView;
                    camera.nearClipPlane = player.nearClipPlane;
                    camera.farClipPlane = player.farClipPlane;
                    camera.transform.SetPositionAndRotation(player.transform.position, player.transform.rotation);
                }

                GameObject staged = StageEnemiesInFrontOf(camera);
                try
                {
                    written += WritePng(camera, Path.Combine(outDir, "Scene_Courtyard.png")) ? 1 : 0;

                    camera.transform.Rotate(0f, 42f, 0f, Space.World);
                    written += WritePng(camera, Path.Combine(outDir, "Scene_Courtyard_Right.png")) ? 1 : 0;
                }
                finally
                {
                    if (staged != null)
                    {
                        Object.DestroyImmediate(staged);
                    }
                }
            }
            finally
            {
                // Never save: the staged enemies are for the photograph only.
                EditorSceneManager.CloseScene(scene, false);
            }

            return written;
        }

        private static Camera FindPlayerCamera()
        {
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c.CompareTag("MainCamera"))
                {
                    return c;
                }
            }

            return null;
        }

        private static GameObject StageEnemiesInFrontOf(Camera camera)
        {
            var root = new GameObject("Capture Staging");
            Vector3 origin = camera.transform.position;
            Vector3 forward = camera.transform.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            var placements = new (string path, float ahead, float across)[]
            {
                (EnemyPrefabs[0], 5.0f, -1.6f),
                (EnemyPrefabs[1], 6.4f, 1.5f),
                (EnemyPrefabs[2], 8.6f, -0.2f)
            };

            foreach ((string path, float ahead, float across) in placements)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                GameObject instance = Object.Instantiate(prefab, root.transform);
                Vector3 position = origin + forward * ahead + right * across;
                position.y = origin.y - 1.66f;   // camera sits at eye height
                instance.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-forward, Vector3.up));

                // A CharacterController would fight the placement in edit mode.
                foreach (CharacterController controller in instance.GetComponentsInChildren<CharacterController>(true))
                {
                    controller.enabled = false;
                }
            }

            return root;
        }

        // ------------------------------------------------------------------
        // Plumbing
        // ------------------------------------------------------------------

        private static Camera CreateCamera(string name, int width, int height)
        {
            var go = new GameObject(name);
            var camera = go.AddComponent<Camera>();
            camera.fieldOfView = 38f;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 400f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.062f, 0.075f, 1f);
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.cullingMask = ~0;
            camera.targetTexture = null;

            var data = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.renderShadows = true;

            CameraSize[camera] = new Vector2Int(width, height);
            return camera;
        }

        private static readonly Dictionary<Camera, Vector2Int> CameraSize = new();

        private static void ApplyStudioLighting()
        {
            // Three-point: a cold key from the front left, a warm bounce from
            // the right, and a storm-teal rim behind. The same three colours the
            // station is lit with, so a model sheet predicts the in-game read.
            AddLight("Key", new Vector3(38f, 32f, 0f), new Color(0.78f, 0.84f, 1f), 2.6f);
            AddLight("Fill", new Vector3(14f, -58f, 0f), AshfallPalette.EmergencyAmber, 0.85f);
            AddLight("Rim", new Vector3(-16f, 196f, 0f), AshfallPalette.StormTeal, 2.2f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.10f, 0.115f, 0.145f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;
        }

        private static void AddLight(string name, Vector3 euler, Color color, float intensity)
        {
            var go = new GameObject($"Light {name}");
            go.transform.rotation = Quaternion.Euler(euler);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = name == "Key" ? LightShadows.Soft : LightShadows.None;
        }

        private static bool WritePng(Camera camera, string path)
        {
            Vector2Int size = CameraSize.TryGetValue(camera, out Vector2Int s) ? s : new Vector2Int(1280, 720);

            var descriptor = new RenderTextureDescriptor(size.x, size.y, RenderTextureFormat.ARGB32, 24)
            {
                sRGB = true,
                msaaSamples = 1,
                useMipMap = false
            };

            RenderTexture rt = RenderTexture.GetTemporary(descriptor);
            Texture2D readback = null;

            try
            {
                camera.targetTexture = rt;

                // Unity 6 routes offscreen renders through render requests; the
                // direct Render() call is the fallback for anything that does
                // not advertise support.
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(camera, request))
                {
                    RenderPipeline.SubmitRenderRequest(camera, request);
                }
                else
                {
                    camera.Render();
                }

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                readback = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                readback.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                readback.Apply();
                RenderTexture.active = previous;

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, readback.EncodeToPNG());
                return true;
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                if (readback != null)
                {
                    Object.DestroyImmediate(readback);
                }
            }
        }
    }
}
