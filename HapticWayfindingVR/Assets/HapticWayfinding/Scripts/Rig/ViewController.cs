using UnityEngine;

namespace HapticWayfinding.Rig
{
    public enum VisualCondition
    {
        /// <summary>Participant sees the room and the figure.</summary>
        FigureVisible,
        /// <summary>Participant sees the room but NOT the figure: direction only from the vest (default, as in the video).</summary>
        FigureHidden,
        /// <summary>Headset shows black: haptics only.</summary>
        Blindfold
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
    /// Controls what the participant's headset camera renders and how the experimenter's
    /// top-down camera is laid out on the desktop window.
    /// Layers (created by the setup script):
    ///   "TargetFigure"     – the little person; hidden from the headset in FigureHidden / Blindfold.
    ///   "ExperimenterOnly" – participant marker, direction line, etc.; never shown in the headset.
    /// </summary>
    public class ViewController : MonoBehaviour
    {
        public Camera headsetCamera;
        public Camera experimenterCamera;
        public VisualCondition condition = VisualCondition.FigureHidden;
        [Tooltip("Desktop layout at start. Tab cycles Inset -> Split -> MapOnly -> HeadsetOnly.")]
        public ExperimenterLayout startLayout = ExperimenterLayout.Inset;
        [Tooltip("Inset layout: width of the bottom-right map + vest panel, as a fraction of the window width.")]
        [Range(0.15f, 0.5f)] public float insetWidthFraction = 0.3f;

        /// <summary>Current desktop layout (runtime state; the start value comes from startLayout).</summary>
        [System.NonSerialized] public ExperimenterLayout layout;

        // Vest panel native size in HapticVestVisualizer units (scaled to the inset width).
        public const float VestPanelW = 470f, VestPanelH = 230f;
        const float Margin = 10f, Gap = 6f;

        int figureLayer, experimenterLayer;
        int baseMask;
        CameraClearFlags baseClear;
        Color baseBackground;

        void Awake()
        {
            if (headsetCamera == null) headsetCamera = Camera.main;
            figureLayer = LayerMask.NameToLayer("TargetFigure");
            experimenterLayer = LayerMask.NameToLayer("ExperimenterOnly");
            baseMask = ~0;
            if (experimenterLayer >= 0) baseMask &= ~(1 << experimenterLayer);
            baseClear = headsetCamera.clearFlags;
            baseBackground = headsetCamera.backgroundColor;
            layout = startLayout;
            Apply();
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
            condition = (VisualCondition)(((int)condition + 1) % 3);
            Apply();
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
            if (headsetCamera != null)
            {
                switch (condition)
                {
                    case VisualCondition.FigureVisible:
                        headsetCamera.cullingMask = baseMask;
                        headsetCamera.clearFlags = baseClear;
                        headsetCamera.backgroundColor = baseBackground;
                        break;
                    case VisualCondition.FigureHidden:
                        headsetCamera.cullingMask = figureLayer >= 0 ? baseMask & ~(1 << figureLayer) : baseMask;
                        headsetCamera.clearFlags = baseClear;
                        headsetCamera.backgroundColor = baseBackground;
                        break;
                    case VisualCondition.Blindfold:
                        headsetCamera.cullingMask = 0;
                        headsetCamera.clearFlags = CameraClearFlags.SolidColor;
                        headsetCamera.backgroundColor = Color.black;
                        break;
                }
            }

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
