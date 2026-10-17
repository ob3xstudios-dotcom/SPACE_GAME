using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.EditorTools
{
    public static class SpaceWorldWindowsBuild
    {
        private const string DevelopmentOutput =
            "Builds/Windows-x64/Development/SpaceWorldAlone.exe";
        private const string PlaytestOutput =
            "Builds/Windows-x64/Playtest/SpaceWorldAlone.exe";

        [MenuItem("SpaceWorld/Build/Windows x64/Development")]
        public static void BuildDevelopment()
        {
            Build(DevelopmentOutput, BuildOptions.Development);
        }

        [MenuItem("SpaceWorld/Build/Windows x64/Playtest")]
        public static void BuildPlaytest()
        {
            Build(PlaytestOutput, BuildOptions.None);
        }

        private static void Build(string relativeOutputPath, BuildOptions options)
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new InvalidOperationException(
                    "SpaceWorld build failed: Build Settings contains no enabled scenes.");

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string outputPath = Path.GetFullPath(Path.Combine(projectRoot, relativeOutputPath));
            string outputDirectory = Path.GetDirectoryName(outputPath);

            if (Directory.Exists(outputDirectory) &&
                Directory.EnumerateFileSystemEntries(outputDirectory).Any())
            {
                throw new InvalidOperationException(
                    $"SpaceWorld build refused to overwrite existing files in: {outputDirectory}");
            }

            Directory.CreateDirectory(outputDirectory);

            Debug.Log(
                $"SpaceWorld build starting. Target=StandaloneWindows64 Options={options} " +
                $"Output={outputPath} Scenes={string.Join(", ", scenes)}");

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = options
            });

            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded || summary.totalErrors > 0)
            {
                string message =
                    $"SpaceWorld build failed. Result={summary.result} " +
                    $"Errors={summary.totalErrors} Warnings={summary.totalWarnings} " +
                    $"Output={outputPath}";
                Debug.LogError(message);
                throw new InvalidOperationException(message);
            }

            if (!File.Exists(outputPath))
            {
                string message =
                    $"SpaceWorld build reported success but the executable is missing: {outputPath}";
                Debug.LogError(message);
                throw new FileNotFoundException(message, outputPath);
            }

            Debug.Log(
                $"SpaceWorld build succeeded. Result={summary.result} " +
                $"Errors={summary.totalErrors} Warnings={summary.totalWarnings} " +
                $"SizeBytes={summary.totalSize} Time={summary.totalTime} Output={outputPath}");
        }
    }
}
