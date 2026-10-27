using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ShapeMerge.EditorTools
{
    /// <summary>Creates the Shape Merge scene and registers it as the first scene in the build.</summary>
    public static class ShapeMergeSceneBuilder
    {
        public const string ScenePath = "Assets/ShapeMerge/Scenes/ShapeMerge.unity";

        [MenuItem("Tools/Shape Merge/Create Game Scene")]
        public static void CreateScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) BuildScene();
        }

        /// <summary>Batch-mode entry point (-executeMethod).</summary>
        public static void CreateSceneBatch() => BuildScene();

        static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(0f, 5.2f, -10f);
            var cam = cameraObject.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 6.7f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.078f, 0.082f, 0.2f);
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;

            new GameObject("Shape Merge").AddComponent<ShapeMergeGame>();

            if (!AssetDatabase.IsValidFolder("Assets/ShapeMerge/Scenes"))
                AssetDatabase.CreateFolder("Assets/ShapeMerge", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log("Shape Merge: scene created at " + ScenePath + " and added to Build Settings.");
        }

        /// <summary>
        /// Batch-mode Windows build of the Shape Merge scene into Builds/ShapeMerge, or into the
        /// folder given after -shapemerge-buildpath.
        /// </summary>
        public static void BuildWindows()
        {
            string folder = "Builds/ShapeMerge";
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-shapemerge-buildpath") folder = args[i + 1];

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = System.IO.Path.Combine(folder, "ShapeMerge.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("Shape Merge build: " + report.summary.result + " (" + report.summary.totalErrors + " errors)");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }
    }
}
