using HapticWayfinding.Guidance;
using HapticWayfinding.Haptics;
using UnityEngine;

namespace HapticWayfinding.UI
{
    /// <summary>
    /// Desktop overlay showing what the vest is doing right now:
    ///  - a top-down ring of the 8 columns around the torso (participant faces up) with the target bearing,
    ///  - the front and back 4x5 motor grids (both drawn from the wearer's point of view: left = wearer's left).
    /// Works with or without real hardware, so the logic can be checked at a desk.
    /// </summary>
    public class HapticVestVisualizer : MonoBehaviour
    {
        public HapticVestOutput output;
        public DirectionalHapticGuide guide;
        public bool show = true;
        public float scale = 1f;

        Texture2D circle;
        GUIStyle label;

        void Awake()
        {
            if (output == null) output = FindObjectOfType<HapticVestOutput>();
            if (guide == null) guide = FindObjectOfType<DirectionalHapticGuide>();
            circle = MakeCircle(64);
        }

        static Texture2D MakeCircle(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d)));
                }
            t.Apply();
            return t;
        }

        static Color MotorColor(float v)
        {
            return v <= 0.001f ? new Color(0.25f, 0.25f, 0.25f, 0.9f) : Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(1f, 0.15f, 0.1f), v);
        }

        void OnGUI()
        {
            if (!show || output == null) return;
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(12 * scale) };
                label.normal.textColor = Color.white;
            }

            float s = scale;
            float panelW = 470 * s, panelH = 230 * s;
            var panel = new Rect(Screen.width - panelW - 10, Screen.height - panelH - 10, panelW, panelH);
            GUI.Box(panel, GUIContent.none);

            VestFrame f = output.CurrentOutput;
            var layout = output.layout;

            // --- Ring (top-down, participant faces up) ---
            Vector2 c = new Vector2(panel.x + 105 * s, panel.y + 120 * s);
            float R = 75 * s;
            GUI.Label(new Rect(panel.x, panel.y + 2, 210 * s, 20 * s), "Top view (facing up)", label);
            DrawDot(c, 26 * s, new Color(0.5f, 0.5f, 0.5f, 0.9f));
            DrawDot(c + new Vector2(0, -16 * s), 8 * s, Color.white); // nose
            for (int sd = 0; sd < 2; sd++)
                for (int col = 0; col < VestLayout.Columns; col++)
                {
                    var side = (VestSide)sd;
                    float ang = layout.ColumnAngle(side, col) * Mathf.Deg2Rad;
                    Vector2 p = c + new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang)) * R;
                    float v = 0f;
                    float[] arr = f.Side(side);
                    for (int r = 0; r < VestLayout.Rows; r++) v = Mathf.Max(v, arr[r * VestLayout.Columns + col]);
                    DrawDot(p, 22 * s, MotorColor(v));
                }
            if (guide != null && guide.HasTarget)
            {
                float b = guide.Bearing * Mathf.Deg2Rad;
                Vector2 tp = c + new Vector2(Mathf.Sin(b), -Mathf.Cos(b)) * (R + 22 * s);
                DrawDot(tp, 12 * s, new Color(0.2f, 0.9f, 1f));
                GUI.Label(new Rect(panel.x, panel.y + panelH - 22 * s, 210 * s, 20 * s), $"bearing {guide.Bearing:+0;-0;0} deg   dist {guide.Distance:0.0} m", label);
            }

            // --- Front / back grids ---
            DrawGrid(new Rect(panel.x + 220 * s, panel.y + 25 * s, 115 * s, 165 * s), f.front, "FRONT (wearer view)");
            DrawGrid(new Rect(panel.x + 345 * s, panel.y + 25 * s, 115 * s, 165 * s), f.back, "BACK (wearer view)");
            string state = output.hapticsEnabled ? (output.BackendConnected ? "vest: connected" : "vest: NOT connected") : "haptics OFF";
            if (output.OverrideActive) state += "  |  " + output.OverrideLabel;
            GUI.Label(new Rect(panel.x + 215 * s, panel.y + panelH - 22 * s, 250 * s, 20 * s), state, label);
        }

        void DrawGrid(Rect r, float[] values, string title)
        {
            GUI.Label(new Rect(r.x - 10, r.y - 22 * scale, r.width + 20, 20 * scale), title, label);
            float cw = r.width / VestLayout.Columns, rh = r.height / VestLayout.Rows;
            float d = Mathf.Min(cw, rh) * 0.75f;
            for (int row = 0; row < VestLayout.Rows; row++)
                for (int col = 0; col < VestLayout.Columns; col++)
                {
                    Vector2 p = new Vector2(r.x + (col + 0.5f) * cw, r.y + (row + 0.5f) * rh);
                    DrawDot(p, d, MotorColor(values[row * VestLayout.Columns + col]));
                }
            GUI.Label(new Rect(r.x - 6, r.yMax - 2, 20, 16 * scale), "L", label);
            GUI.Label(new Rect(r.xMax - 14, r.yMax - 2, 20, 16 * scale), "R", label);
        }

        void DrawDot(Vector2 center, float size, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - size / 2, center.y - size / 2, size, size), circle);
            GUI.color = old;
        }
    }
}
