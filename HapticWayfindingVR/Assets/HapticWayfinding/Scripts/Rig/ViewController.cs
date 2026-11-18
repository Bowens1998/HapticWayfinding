using UnityEngine;

namespace HapticWayfinding.Rig
{
    public enum VisualCondition
    {
        /// <summary>Booth glass is clear: the participant sees the room and the walking figure.</summary>
        ClearGlass,
        /// <summary>Booth glass is frosted (opaque): the figure is still there but cannot be seen; direction only from the vest (default, as in the video).</summary>
        FrostedGlass
    }

    public enum ExperimenterLayout
    {
        /// <summary>Desktop window: headset mirror on the left, top-down map on the right.</summary>
        Split,
        /// <summary>Desktop window: full-screen top-down map.</summary>
        MapOnly,
        /// <summary>Desktop window: headset mirror only.</summary>
        HeadsetOnly,
        /// <summary>Desktop window: headset mirror full size, small top-down map + vest panel in the bottom-right corner (default).</summary>
        Inset
    }

    /// <summary>
    /// Controls what the participant sees (clear vs frosted booth glass) and how the experimenter's
    /// top-down camera is laid out on the desktop window.
    /// The figure is never culled from the headset: hiding it is done by frosting the glass booth,
    /// with a short fade, so it does not vanish abruptly.
    /// Layer "ExperimenterOnly" (participant marker, direction line, etc.) is never shown in the headset.
    /// </summary>
    public class ViewController : MonoBehaviour
    {
        public Camera headsetCamera;
        public Camera experimenterCamera;
        public VisualCondition condition = VisualCondition.FrostedGlass;

        [Header("Booth glass")]
        [Tooltip("Booth glass renderers. Empty = all renderers under the 'GlassBooth' object whose material is the glass material.")]
        public Renderer[] boothGlass;
        [Tooltip("Frosted (opaque) glass colour; alpha 1 = nothing visible through it.")]
        public Color frostedColor = new Color(0.86f, 0.89f, 0.9f, 1f);
        [Range(0f, 1f)] public float frostedSmoothness = 0.35f;
        [Tooltip("Clear <-> frosted fade time (s).")]
        public float glassFadeSeconds = 0.8f;
        [Tooltip("Desktop layout at start. Tab cycles Inset -> Split -> MapOnly -> HeadsetOnly.")]
        public ExperimenterLayout startLayout = ExperimenterLayout.Inset;
        [Tooltip("Inset layout: width of the bottom-right map + vest panel, as a fraction of the window width.")]
        [Range(0.15f, 0.5f)] public float insetWidthFraction = 0.3f;

        /// <summary>Current desktop layout (runtime state; the start value comes from startLayout).</summary>
        [System.NonSerialized] public ExperimenterLayout layout;

        // Vest panel native size in HapticVestVisualizer units (scaled to the inset width).
        public const float VestPanelW = 470f, VestPanelH = 230f;
        const float Margin = 10f, Gap = 6f;

        int experimenterLayer;
        int baseMask;
        Material glassMat;               // runtime copy shared by all booth glass panels (the .mat asset is never modified)
        Color clearColor;
        float clearSmoothness, frost;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), SmoothnessId = Shader.PropertyToID("_Smoothness");

        void Awake()
        {
            if (headsetCamera == null) headsetCamera = Camera.main;
            experimenterLayer = LayerMask.NameToLayer("ExperimenterOnly");
            baseMask = ~0;
            if (experimenterLayer >= 0) baseMask &= ~(1 << experimenterLayer);
            SetupGlass();
            frost = condition == VisualCondition.FrostedGlass ? 1f : 0f;
            ApplyFrost();
            layout = startLayout;
            Apply();
        }

        void SetupGlass()
        {
            if (boothGlass == null || boothGlass.Length == 0)
            {
                var booth = GameObject.Find("GlassBooth");
                var list = new System.Collections.Generic.List<Renderer>();
                if (booth != null)
                    foreach (var r in booth.GetComponentsInChildren<Renderer>())
                        if (r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("Glass")) list.Add(r);
                boothGlass = list.ToArray();
            }
            if (boothGlass.Length == 0 || boothGlass[0] == null) { Debug.LogWarning("[HapticWayfinding] Booth glass not found; V will have no visible effect."); return; }
            glassMat = new Material(boothGlass[0].sharedMaterial) { name = "Glass (runtime)" };
            clearColor = glassMat.GetColor(BaseColorId);
            clearSmoothness = glassMat.GetFloat(SmoothnessId);
            foreach (var r in boothGlass) if (r != null) r.sharedMaterial = glassMat;
        }

        void Update()
        {
            float target = condition == VisualCondition.FrostedGlass ? 1f : 0f;
            if (Mathf.Approximately(frost, target)) return;
            frost = Mathf.MoveTowards(frost, target, Time.deltaTime / Mathf.Max(0.01f, glassFadeSeconds));
            ApplyFrost();
        }

        void ApplyFrost()
        {
            if (glassMat == null) return;
            float t = Mathf.SmoothStep(0f, 1f, frost);
            glassMat.SetColor(BaseColorId, Color.Lerp(clearColor, frostedColor, t));
            glassMat.SetFloat(SmoothnessId, Mathf.Lerp(clearSmoothness, frostedSmoothness, t));
        }

        // Inset map: the top-down camera renders into a RenderTexture that OnGUI draws right above the
        // vest panel. A camera viewport rect cannot be used here: with an HMD active the Game view shows
        // the eye mirror, and screen-space camera rects no longer line up with GUI coordinates.
        RenderTexture mapTexture;
        Rect mapGuiRect;           // where the map is drawn, GUI pixels (top-left origin)
        float guiScreenHeight = 1; // Screen.height as seen by OnGUI (differs from Update when XR is active)

        void OnGUI()
        {
            if (layout != ExperimenterLayout.Inset || experimenterCamera == null) return;
            GetInsetRects(out _, out mapGuiRect, out _);
            guiScreenHeight = Screen.height;
            int tw = Mathf.Max(16, Mathf.RoundToInt(mapGuiRect.width)), th = Mathf.Max(16, Mathf.RoundToInt(mapGuiRect.height));
            if (mapTexture == null || mapTexture.width != tw || mapTexture.height != th)
            {
                if (mapTexture != null) { experimenterCamera.targetTexture = null; mapTexture.Release(); Destroy(mapTexture); }
                mapTexture = new RenderTexture(tw, th, 24) { name = "ExperimenterMap", antiAliasing = 2 };
            }
            experimenterCamera.targetTexture = mapTexture;
            experimenterCamera.rect = new Rect(0f, 0f, 1f, 1f);
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(mapGuiRect, mapTexture, ScaleMode.StretchToFill, false);
        }

        /// <summary>
        /// Inset layout: convert a mouse position (Input.mousePosition) to a viewport point of the
        /// experimenter camera. Returns false when the mouse is not over the inset map.
        /// </summary>
        public bool TryMouseToMapViewport(Vector3 mouse, out Vector2 viewport)
        {
            viewport = default;
            if (layout != ExperimenterLayout.Inset || mapGuiRect.width < 1f || mapGuiRect.height < 1f) return false;
            var gui = new Vector2(mouse.x, guiScreenHeight - mouse.y);
            if (!mapGuiRect.Contains(gui)) return false;
            viewport = new Vector2((gui.x - mapGuiRect.x) / mapGuiRect.width, 1f - (gui.y - mapGuiRect.y) / mapGuiRect.height);
            return true;
        }

        void OnDestroy()
        {
            if (mapTexture != null) mapTexture.Release();
            if (glassMat != null) Destroy(glassMat);
        }

        public bool IsInset => layout == ExperimenterLayout.Inset;

        /// <summary>
        /// Inset layout, in GUI pixels (origin top-left): the vest panel sits in the bottom-right corner,
        /// the top-down map directly above it, both <see cref="insetWidthFraction"/> of the window wide.
        /// </summary>
        public void GetInsetRects(out Rect vestPanel, out Rect map, out float vestScale)
        {
            float w = Screen.width, h = Screen.height;
            float insetW = Mathf.Clamp(w * insetWidthFraction, Mathf.Min(260f, w - 2 * Margin), w - 2 * Margin);
            vestScale = insetW / VestPanelW;
            float panelH = VestPanelH * vestScale;
            vestPanel = new Rect(w - insetW - Margin, h - panelH - Margin, insetW, panelH);
            float mapH = Mathf.Max(40f, Mathf.Min(insetW * 0.75f, vestPanel.y - Gap - Margin));
            map = new Rect(vestPanel.x, vestPanel.y - Gap - mapH, insetW, mapH);
        }

        public void CycleCondition()
        {
            condition = condition == VisualCondition.FrostedGlass ? VisualCondition.ClearGlass : VisualCondition.FrostedGlass;
        }

        public void CycleLayout()
        {
            // Inset -> Split -> MapOnly -> HeadsetOnly -> Inset
            layout = layout == ExperimenterLayout.Inset ? ExperimenterLayout.Split
                : layout == ExperimenterLayout.HeadsetOnly ? ExperimenterLayout.Inset
                : (ExperimenterLayout)((int)layout + 1);
            Apply();
        }

        public void Apply()
        {
            if (headsetCamera != null) headsetCamera.cullingMask = baseMask;

            if (experimenterCamera != null)
            {
                experimenterCamera.enabled = layout != ExperimenterLayout.HeadsetOnly;
                if (layout != ExperimenterLayout.Inset) experimenterCamera.targetTexture = null; // Inset assigns its texture in OnGUI
                experimenterCamera.rect = layout == ExperimenterLayout.Split ? new Rect(0.5f, 0f, 0.5f, 1f)
                    : layout == ExperimenterLayout.Inset ? new Rect(0f, 0f, 1f, 1f)
                    : new Rect(0f, 0f, 1f, 1f);
            }
        }
    }
}
