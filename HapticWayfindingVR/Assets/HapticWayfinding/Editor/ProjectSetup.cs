using System.IO;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace HapticWayfinding.EditorTools
{
    /// <summary>
    /// One-click project setup (menu: Haptic Wayfinding > ...), also runnable headless:
    ///   Unity.exe -batchmode -projectPath . -executeMethod HapticWayfinding.EditorTools.ProjectSetup.RunBatch -quit
    /// </summary>
    public static class ProjectSetup
    {
        const string ScenePath = SceneBuilder.ScenePath;
        public const string FigureLayerName = "TargetFigure";
        public const string ExperimenterLayerName = "ExperimenterOnly";
        public const string CeilingLayerName = "Ceiling";

        public static void RunBatch()
        {
            SetupLayers();
            ConfigurePlayer();
            ConfigureXR();
            RenderSetup.Configure();
            BuildScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[HapticWayfinding] Project setup finished.");
        }

        /// <summary>Regenerate the scene (picks up new script defaults) and build Builds/HapticWayfinding.exe.</summary>
        [MenuItem("Haptic Wayfinding/3. Rebuild Scene + Windows Build")]
        public static void BuildAll()
        {
            RunBatch();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/HapticWayfinding.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("[HapticWayfinding] Build result: " + report.summary.result + ", errors: " + report.summary.totalErrors);
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }

        [MenuItem("Haptic Wayfinding/1. Configure Project (layers, input, OpenXR)")]
        public static void ConfigureAll()
        {
            SetupLayers();
            ConfigurePlayer();
            ConfigureXR();
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ layers / player
        static void SetupLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            EnsureLayer(layers, FigureLayerName, 8);
            EnsureLayer(layers, ExperimenterLayerName, 9);
            EnsureLayer(layers, CeilingLayerName, 10);
            tagManager.ApplyModifiedProperties();
        }

        static void EnsureLayer(SerializedProperty layers, string name, int preferred)
        {
            for (int i = 8; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name) return;
            if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(preferred).stringValue))
            {
                layers.GetArrayElementAtIndex(preferred).stringValue = name;
                return;
            }
            for (int i = 8; i < layers.arraySize; i++)
            {
                if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                {
                    layers.GetArrayElementAtIndex(i).stringValue = name;
                    return;
                }
            }
            Debug.LogError("No free layer slot for " + name);
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "UF ICIC Lab";
            PlayerSettings.productName = "HapticWayfinding";
            PlayerSettings.runInBackground = true; // keep streaming to the vest when the window loses focus
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Active Input Handling = Both (legacy Input for the experimenter keys, Input System for OpenXR).
            var playerSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0];
            var so = new SerializedObject(playerSettings);
            var prop = so.FindProperty("activeInputHandler");
            if (prop != null && prop.intValue != 2)
            {
                prop.intValue = 2;
                so.ApplyModifiedProperties();
                Debug.Log("[HapticWayfinding] Active Input Handling set to Both (takes effect after editor restart).");
            }
        }

        // ------------------------------------------------------------------ XR
        static void ConfigureXR()
        {
            const BuildTargetGroup group = BuildTargetGroup.Standalone;

            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perBuild);
            if (perBuild == null)
            {
                Directory.CreateDirectory("Assets/XR");
                AssetDatabase.Refresh();
                perBuild = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perBuild, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBuild, true);
            }
            if (!perBuild.HasSettingsForBuildTarget(group)) perBuild.CreateDefaultSettingsForBuildTarget(group);
            if (!perBuild.HasManagerSettingsForBuildTarget(group)) perBuild.CreateDefaultManagerSettingsForBuildTarget(group);

            var general = perBuild.SettingsForBuildTarget(group);
            general.InitManagerOnStart = true;
            XRPackageMetadataStore.AssignLoader(general.AssignedSettings, "UnityEngine.XR.OpenXR.OpenXRLoader", group);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(perBuild);

            FeatureHelpers.RefreshFeatures(group);
            var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (openxr != null)
            {
                var vive = openxr.GetFeature<HTCViveControllerProfile>();
                if (vive != null) vive.enabled = true;
                EditorUtility.SetDirty(openxr);
            }
            else
            {
                Debug.LogWarning("[HapticWayfinding] OpenXR settings not found; enable OpenXR manually in Project Settings > XR Plug-in Management.");
            }
        }

        // ------------------------------------------------------------------ scene
        /// <summary>Realistic scene with baked lighting (takes a few minutes).</summary>
        [MenuItem("Haptic Wayfinding/2. Build Demo Scene (realistic, bake lighting)")]
        public static void BuildScene() => SceneBuilder.Build(bakeLighting: true);

        /// <summary>Same scene without baking (fast, for layout iteration).</summary>
        [MenuItem("Haptic Wayfinding/2b. Build Demo Scene (no bake, fast)")]
        public static void BuildSceneNoBake() => SceneBuilder.Build(bakeLighting: false);
    }
}
