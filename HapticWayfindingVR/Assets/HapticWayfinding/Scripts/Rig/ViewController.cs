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
        HeadsetOnly
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
        public ExperimenterLayout layout = ExperimenterLayout.Split;

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
            Apply();
        }

        public void CycleCondition()
        {
            condition = (VisualCondition)(((int)condition + 1) % 3);
            Apply();
        }

        public void CycleLayout()
        {
            layout = (ExperimenterLayout)(((int)layout + 1) % 3);
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
                experimenterCamera.rect = layout == ExperimenterLayout.Split
                    ? new Rect(0.5f, 0f, 0.5f, 1f)
                    : new Rect(0f, 0f, 1f, 1f);
            }
        }
    }
}
