using UnityEngine;
using UnityEngine.SceneManagement;
using Ashfall.InputLayer;

namespace Ashfall.Core
{
    /// <summary>
    /// Makes the game playable from whatever scene happens to be open.
    ///
    /// Pressing Play in an empty scene is the most common way to end up staring at a
    /// grey void and assuming the project is broken. This runs before the first frame
    /// of any scene, notices that no <see cref="GameDirector"/> exists, and loads the
    /// generated Main scene instead.
    /// </summary>
    public static class AshfallBootstrap
    {
        public const string MainSceneName = "Main";

        /// <summary>Set to false to debug a bare scene without being redirected.</summary>
        public static bool AutoLoadMainScene = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void OnBeforeSceneLoad()
        {
            // Physics settings the whole game assumes. Set here rather than in the
            // project asset so a fresh clone cannot get them wrong.
            Physics.defaultSolverIterations = 8;
            Application.targetFrameRate = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OnAfterSceneLoad()
        {
            EnsureInput();

            if (!AutoLoadMainScene)
            {
                return;
            }

            if (Object.FindFirstObjectByType<GameDirector>() != null)
            {
                return;
            }

            Scene active = SceneManager.GetActiveScene();

            // Never hijack the test runner's own scene. It bootstraps play mode from a
            // generated "InitTestScene<guid>" that holds the runner's callback objects;
            // loading over it destroys them and the whole run hangs with no output.
            // Play-mode tests load the Main scene themselves.
            if (IsTestRunnerScene(active))
            {
                return;
            }

            if (active.name == MainSceneName)
            {
                Debug.LogWarning(
                    "[Ashfall] The Main scene is open but has no GameDirector. " +
                    "Run 'Ashfall / Build Playable Scene' from the menu bar to regenerate it.");
                return;
            }

            if (!CanLoadMainScene())
            {
                Debug.LogWarning(
                    $"[Ashfall] No GameDirector in '{active.name}' and the Main scene is not in the build settings. " +
                    "Run 'Ashfall / Build Playable Scene' to generate and register it.");
                return;
            }

            Debug.Log($"[Ashfall] No GameDirector in '{active.name}'; loading the Main scene.");
            SceneManager.LoadScene(MainSceneName, LoadSceneMode.Single);
        }

        private static bool IsTestRunnerScene(Scene scene)
        {
            return scene.name.StartsWith("InitTestScene", System.StringComparison.Ordinal);
        }

        private static bool CanLoadMainScene()
        {
            for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            {
                string path = SceneUtility.GetScenePathByBuildIndex(i);
                if (path.EndsWith($"/{MainSceneName}.unity", System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureInput()
        {
            // Touching the property creates the singleton if the scene did not ship one,
            // so nothing downstream ever has to null-check the input layer.
            _ = AshfallInput.Instance;
        }
    }
}
