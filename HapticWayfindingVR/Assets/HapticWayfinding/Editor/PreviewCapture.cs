using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HapticWayfinding.EditorTools
{
    /// <summary>
    /// Renders preview stills of the demo scene to PNG (for checking the look without a headset).
    /// Output folder: env var HW_PREVIEW_DIR, else &lt;project&gt;/Previews.
    ///   Unity.exe -batchmode -projectPath . -executeMethod HapticWayfinding.EditorTools.PreviewCapture.FastBuildAndCapture -quit
    /// </summary>
    public static class PreviewCapture
    {
        public static void FastBuildAndCapture()
        {
            ProjectSetup.ConfigureAll();
            RenderSetup.Configure();
            SceneBuilder.Build(bakeLighting: false);
            CaptureAll();
        }

        public static void FullBuildAndCapture()
        {
            ProjectSetup.BuildAll();
            CaptureAll();
        }

        [MenuItem("Haptic Wayfinding/Capture Preview Stills")]
        public static void CaptureAll()
        {
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath);
            string dir = Environment.GetEnvironmentVariable("HW_PREVIEW_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.GetFullPath("Previews");
            Directory.CreateDirectory(dir);

            // Participant eye view from inside the booth, looking towards the figure and around the room.
            Shot(dir, "view_front_right", new Vector3(0, 1.65f, 0), new Vector3(5f, 0, 45f));
            Shot(dir, "view_east_windows", new Vector3(0, 1.65f, 0), new Vector3(5f, 90f, 0));
            Shot(dir, "view_west_desks", new Vector3(0, 1.65f, 0), new Vector3(8f, -90f, 0));
            Shot(dir, "view_north_shelf", new Vector3(0, 1.65f, 0), new Vector3(6f, 0f, 0));
            Shot(dir, "view_south_door", new Vector3(0, 1.65f, 0), new Vector3(5f, 180f, 0));
            // Third-person look at the mannequin, and an overview from a corner.
            Shot(dir, "figure_closeup", new Vector3(4.6f, 1.45f, 0.9f), Quaternion.LookRotation(new Vector3(2.5f, 1.0f, 2.5f) - new Vector3(4.6f, 1.45f, 0.9f)).eulerAngles, 55f);
            Shot(dir, "overview_corner", new Vector3(-6.3f, 2.9f, -6.3f), Quaternion.LookRotation(new Vector3(1.5f, 0.6f, 1.5f) - new Vector3(-6.3f, 2.9f, -6.3f)).eulerAngles, 75f);
            ShotTopDown(dir);
            Debug.Log("[HapticWayfinding] Preview stills written to " + dir);
        }

        static void Shot(string dir, string name, Vector3 pos, Vector3 euler, float fov = 90f)
        {
            var go = new GameObject("PreviewCam");
            try
            {
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler));
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 300f;
                int fig = LayerMask.NameToLayer(ProjectSetup.ExperimenterLayerName);
                cam.cullingMask = fig >= 0 ? ~(1 << fig) : ~0;
                cam.allowMSAA = true;
                go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
                Render(cam, Path.Combine(dir, name + ".png"), 1280, 720);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static void ShotTopDown(string dir)
        {
            var go = new GameObject("PreviewTop");
            try
            {
                go.transform.SetPositionAndRotation(new Vector3(0, 25f, 0), Quaternion.Euler(90f, 0, 0));
                var cam = go.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 7.8f;
                cam.farClipPlane = 60f;
                int ceil = LayerMask.NameToLayer(ProjectSetup.CeilingLayerName);
                cam.cullingMask = ceil >= 0 ? ~(1 << ceil) : ~0;
                go.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
                Render(cam, Path.Combine(dir, "map_topdown.png"), 900, 900);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        static void Render(Camera cam, string path, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
