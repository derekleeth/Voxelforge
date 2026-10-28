using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Voxelforge.Editor
{
    /// <summary>
    /// Builds the playable Windows demo into the git-ignored <c>Builds/Windows/</c> folder and writes a short report
    /// next to it. Batch mode: <c>Unity -batchmode -quit -executeMethod Voxelforge.Editor.DemoBuilder.BuildFromCommandLine</c>.
    /// </summary>
    public static class DemoBuilder
    {
        const string OutputFolder = "Builds/Windows";
        const string ExeName = "Voxelforge.exe";

        [MenuItem("Voxelforge/Build Windows Demo", priority = 100)]
        static void BuildRelease() => Build(development: false);

        [MenuItem("Voxelforge/Build Windows Demo (Development)", priority = 101)]
        static void BuildDevelopment() => Build(development: true);

        public static void BuildFromCommandLine()
        {
            bool development = Environment.GetCommandLineArgs().Contains("-development");
            BuildReport report = Build(development);
            EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        public static BuildReport Build(bool development)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[DemoBuilder] Stop Play Mode before building the demo.");
                return null;
            }

            string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[DemoBuilder] No scenes are enabled in Build Settings.");
                return null;
            }

            string folder = Path.GetFullPath(OutputFolder);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(folder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development | BuildOptions.ConnectWithProfiler : BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            string summary = Describe(report, development, folder);
            File.WriteAllText(Path.Combine(folder, "build-report.txt"), summary);

            if (report.summary.result == BuildResult.Succeeded)
                Debug.Log($"[DemoBuilder] {summary}");
            else
                Debug.LogError($"[DemoBuilder] {summary}");
            return report;
        }

        static string Describe(BuildReport report, bool development, string folder)
        {
            BuildSummary summary = report.summary;
            long folderBytes = Directory.Exists(folder)
                ? new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}Screenshots{Path.DirectorySeparatorChar}")
                                && !f.FullName.Contains("_DontShipItWithYourGame")) // Burst debug symbols
                    .Sum(f => f.Length)
                : 0;

            var text = new StringBuilder();
            text.AppendLine($"Voxelforge {PlayerSettings.bundleVersion} {(development ? "development" : "release")} build: {summary.result}");
            text.AppendLine($"Built {DateTime.Now:yyyy-MM-dd HH:mm} in {summary.totalTime.TotalSeconds:F0} s with Unity {Application.unityVersion}");
            text.AppendLine($"Output: {summary.outputPath}");
            text.AppendLine($"Shippable size: {folderBytes / (1024f * 1024f):F1} MB (Unity's player data: {summary.totalSize / (1024f * 1024f):F1} MB)");
            text.AppendLine($"Errors: {summary.totalErrors}, warnings: {summary.totalWarnings}");

            foreach (BuildStep step in report.steps)
                foreach (BuildStepMessage message in step.messages)
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                        text.AppendLine($"  {step.name}: {message.content}");

            return text.ToString();
        }
    }
}
