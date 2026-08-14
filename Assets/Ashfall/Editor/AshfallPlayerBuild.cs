using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// Produces a standalone player. Batch entry points only -- there is nothing to
    /// configure by hand, which is the point: the same command runs locally and in CI.
    /// </summary>
    public static class AshfallPlayerBuild
    {
        private const string BuildRoot = "Builds";

        [MenuItem("Ashfall/Build Linux Player", priority = 40)]
        public static void BuildLinuxMenu()
        {
            BuildReport report = Build(BuildTarget.StandaloneLinux64, "Linux", "Ashfall.x86_64");
            Debug.Log(Describe(report));
        }

        public static void BuildLinuxFromCommandLine()
        {
            RunBatch(BuildTarget.StandaloneLinux64, "Linux", "Ashfall.x86_64");
        }

        public static void BuildWindowsFromCommandLine()
        {
            RunBatch(BuildTarget.StandaloneWindows64, "Windows", "Ashfall.exe");
        }

        private static void RunBatch(BuildTarget target, string folder, string executable)
        {
            BuildReport report = Build(target, folder, executable);

            if (report == null)
            {
                Debug.LogError("[Ashfall] PLAYER_BUILD_FAILED: no report returned.");
                EditorApplication.Exit(4);
                return;
            }

            Debug.Log(Describe(report));

            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Ashfall] PLAYER_BUILD_OK {report.summary.outputPath}");
                EditorApplication.Exit(0);
                return;
            }

            Debug.LogError($"[Ashfall] PLAYER_BUILD_FAILED {report.summary.result}");
            EditorApplication.Exit(5);
        }

        private static BuildReport Build(BuildTarget target, string folder, string executable)
        {
            string directory = Path.Combine(BuildRoot, folder);
            Directory.CreateDirectory(directory);

            var scenes = new[] { AshfallProjectBuilder.ScenePath };
            if (!File.Exists(AshfallProjectBuilder.ScenePath))
            {
                Debug.LogError(
                    $"[Ashfall] {AshfallProjectBuilder.ScenePath} does not exist. " +
                    "Run Ashfall / Build Playable Scene first.");
                return null;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(directory, executable),
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.None
            };

            return BuildPipeline.BuildPlayer(options);
        }

        private static string Describe(BuildReport report)
        {
            if (report == null)
            {
                return "[Ashfall] Build produced no report.";
            }

            BuildSummary summary = report.summary;
            return $"[Ashfall] Build {summary.result}: {summary.outputPath}\n" +
                   $"  platform : {summary.platform}\n" +
                   $"  size     : {summary.totalSize / (1024f * 1024f):0.0} MiB\n" +
                   $"  duration : {summary.totalTime.TotalSeconds:0.0}s\n" +
                   $"  errors   : {summary.totalErrors}, warnings: {summary.totalWarnings}";
        }
    }
}
