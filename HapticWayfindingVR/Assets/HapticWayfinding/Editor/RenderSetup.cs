using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HapticWayfinding.EditorTools
{
    /// <summary>
    /// Switches the project to URP with VR-friendly quality settings:
    /// HDR, 4x MSAA, soft shadows, SSAO, ACES tonemapping, mild bloom / vignette.
    /// </summary>
    public static class RenderSetup
    {
        const string Dir = "Assets/HapticWayfinding/Rendering";
        const string PipelinePath = Dir + "/HW_URP.asset";
        const string RendererPath = Dir + "/HW_URP_Renderer.asset";
        public const string VolumeProfilePath = Dir + "/HW_PostProcess.asset";

        [MenuItem("Haptic Wayfinding/Configure URP Rendering")]
        public static void Configure()
        {
            Directory.CreateDirectory(Dir);
            AssetDatabase.Refresh();

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
                ResourceReloader.TryReloadAllNullIn(rendererData, "Packages/com.unity.render-pipelines.universal");
                AssetDatabase.SaveAssets();
            }
            AddSsao(rendererData);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            var so = new SerializedObject(pipeline);
            SetInt(so, "m_MSAA", 4);
            SetBool(so, "m_SupportsHDR", true);
            SetFloat(so, "m_RenderScale", 1f);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetInt(so, "m_MainLightShadowmapResolution", 2048);
            SetFloat(so, "m_ShadowDistance", 18f);
            SetInt(so, "m_ShadowCascadeCount", 2);
            SetInt(so, "m_AdditionalLightsPerObjectLimit", 4);
            SetBool(so, "m_MixedLightingSupported", true);
            SetBool(so, "m_RequireDepthTexture", false);
            SetBool(so, "m_RequireOpaqueTexture", false);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);

            CreateVolumeProfile();
            AssetDatabase.SaveAssets();
            Debug.Log("[HapticWayfinding] URP configured.");
        }

        static void AddSsao(UniversalRendererData data)
        {
            foreach (var f in data.rendererFeatures)
                if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion") return;

            // SSAO is internal in URP 14, so create it by type name.
            Type t = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
            if (t == null) { Debug.LogWarning("SSAO feature type not found; skipping."); return; }
            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(t);
            feature.name = "SSAO";
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.SaveAssets();
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

            var fso = new SerializedObject(feature);
            SetFloat(fso, "m_Settings.Intensity", 1.6f);
            SetFloat(fso, "m_Settings.Radius", 0.25f);
            SetFloat(fso, "m_Settings.DirectLightingStrength", 0.2f);
            SetBool(fso, "m_Settings.Downsample", true);
            fso.ApplyModifiedPropertiesWithoutUndo();

            var dso = new SerializedObject(data);
            var features = dso.FindProperty("m_RendererFeatures");
            var map = dso.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            dso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
        }

        static void CreateVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile != null) AssetDatabase.DeleteAsset(VolumeProfilePath);
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.35f);
            color.contrast.Override(8f);
            color.saturation.Override(4f);

            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(4f);

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.16f);
            vignette.smoothness.Override(0.5f);

            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
        }

        public static VolumeProfile LoadVolumeProfile() => AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);

        static void SetInt(SerializedObject so, string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; else Warn(name); }
        static void SetBool(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; else Warn(name); }
        static void SetFloat(SerializedObject so, string name, float v) { var p = so.FindProperty(name); if (p != null) p.floatValue = v; else Warn(name); }
        static void Warn(string name) => Debug.LogWarning("[HapticWayfinding] URP setting not found: " + name);
    }
}
