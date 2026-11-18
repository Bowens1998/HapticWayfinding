using HapticWayfinding.Haptics;
using UnityEngine;

namespace HapticWayfinding.Guidance
{
    public enum GuidanceMode
    {
        /// <summary>
        /// Lab spec (default): 8 directions, each drives the TWO neighbouring columns around it.
        /// Front = front middle two columns, front-left = front columns 1+2 (from the wearer's left),
        /// left = front left-most + back left-most, back = back middle two, etc.
        /// </summary>
        AdjacentPair,
        /// <summary>Exactly one of the 8 columns vibrates: the one closest to the target direction.</summary>
        SingleColumn,
        /// <summary>The two neighbouring columns share intensity, giving a smoother "rotating" cue.</summary>
        Interpolated,
        Off
    }

    public enum DistanceCue
    {
        /// <summary>Continuous vibration, distance not encoded.</summary>
        None,
        /// <summary>Vibration pulses faster when the target is closer.</summary>
        PulseRate,
        /// <summary>Vibration is stronger when the target is closer.</summary>
        Intensity
    }

    /// <summary>
    /// Core of the demo:
    ///   1. take the participant's forward vector (HMD or a body tracker), projected on the floor,
    ///   2. take the vector from the participant to the target figure, projected on the floor,
    ///   3. signed angle between them = bearing (0 front, +90 right, -90 left, 180 behind),
    ///   4. drive the vest columns that point in that direction.
    /// </summary>
    public class DirectionalHapticGuide : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Whose forward vector is used. Default: the HMD camera. Use a VIVE Tracker on the chest for true torso direction.")]
        public Transform body;
        [Tooltip("The figure (point) whose direction is rendered on the vest.")]
        public Transform target;
        public HapticVestOutput output;

        [Header("Encoding")]
        public GuidanceMode mode = GuidanceMode.AdjacentPair;
        [Range(0f, 1f)] public float intensity = 0.8f;
        [Tooltip("Rows of each column that vibrate (top row first). Default: all 5.")]
        public bool[] rowMask = { true, true, true, true, true };
        [Tooltip("The active direction only switches once the bearing is this many degrees past the border (prevents flicker).")]
        [Range(0f, 15f)] public float switchHysteresisDeg = 4f;

        [Header("Distance cue (optional)")]
        public DistanceCue distanceCue = DistanceCue.None;
        public float nearDistance = 0.5f;
        public float farDistance = 6f;
        [Tooltip("Pulse period (s) at near / far distance.")]
        public float pulsePeriodNear = 0.2f;
        public float pulsePeriodFar = 1.0f;
        [Range(0.1f, 0.9f)] public float pulseDuty = 0.5f;
        [Range(0f, 1f)] public float minIntensityFar = 0.3f;

        // ---- Read-only state for HUD / logger ----
        public float Bearing { get; private set; }
        public float Distance { get; private set; }
        /// <summary>Human-readable direction, e.g. "Front-left".</summary>
        public string ActiveDirection { get; private set; } = "";
        /// <summary>Which columns are on, e.g. "Front left-outer + Front left-inner".</summary>
        public string ActiveColumns { get; private set; } = "";
        public bool HasTarget => body != null && target != null && target.gameObject.activeInHierarchy;

        readonly VestFrame frame = new VestFrame();
        Vector3 lastForward = Vector3.forward;
        float pulsePhase;
        int currentSector = -1;
        VestSide prevSide;
        int prevCol = -1;

        void Awake()
        {
            if (output == null) output = FindObjectOfType<HapticVestOutput>();
            if (body == null && Camera.main != null) body = Camera.main.transform;
        }

        /// <summary>Signed horizontal angle from the body's forward vector to the point (deg, + = right).</summary>
        public float ComputeBearing(Vector3 point)
        {
            Vector3 fwd = body.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = lastForward; // looking straight up/down
            else lastForward = fwd;
            Vector3 to = point - body.position;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
        }

        // LateUpdate: runs after the figure / simulator / HMD moved this frame, so the cue is never a frame behind.
        void LateUpdate()
        {
            frame.Clear();
            ActiveDirection = "";
            ActiveColumns = "";

            if (!HasTarget || output == null)
            {
                output?.SetGuidanceFrame(frame);
                return;
            }

            Bearing = ComputeBearing(target.position);
            Vector3 d = target.position - body.position;
            d.y = 0f;
            Distance = d.magnitude;

            if (mode == GuidanceMode.Off)
            {
                output.SetGuidanceFrame(frame);
                return;
            }

            float level = intensity * DistanceGain() * PulseGate();
            var layout = output.layout;

            switch (mode)
            {
                case GuidanceMode.AdjacentPair:
                {
                    int s = PairSectorWithHysteresis(Bearing);
                    var a = VestLayout.Ring[s];
                    var b = VestLayout.Ring[(s + 1) % 8];
                    ActiveDirection = VestLayout.SectorNames[s];
                    ActiveColumns = VestLayout.ColumnName(a.side, a.col) + " + " + VestLayout.ColumnName(b.side, b.col);
                    if (level > 0f)
                    {
                        frame.SetColumn(a.side, a.col, level, rowMask);
                        frame.SetColumn(b.side, b.col, level, rowMask);
                    }
                    break;
                }
                case GuidanceMode.SingleColumn:
                {
                    layout.NearestColumn(Bearing, out var side, out int col);
                    if (prevCol >= 0 && (side != prevSide || col != prevCol))
                    {
                        float dNew = Mathf.Abs(Mathf.DeltaAngle(Bearing, layout.ColumnAngle(side, col)));
                        float dOld = Mathf.Abs(Mathf.DeltaAngle(Bearing, layout.ColumnAngle(prevSide, prevCol)));
                        if (dOld - dNew < switchHysteresisDeg) { side = prevSide; col = prevCol; }
                    }
                    prevSide = side;
                    prevCol = col;
                    ActiveDirection = $"{Bearing:+0;-0;0} deg";
                    ActiveColumns = VestLayout.ColumnName(side, col);
                    if (level > 0f) frame.SetColumn(side, col, level, rowMask);
                    break;
                }
                case GuidanceMode.Interpolated:
                {
                    ActiveDirection = $"{Bearing:+0;-0;0} deg";
                    for (int s = 0; s < 2; s++)
                    {
                        for (int c = 0; c < VestLayout.Columns; c++)
                        {
                            float delta = Mathf.Abs(Mathf.DeltaAngle(Bearing, layout.ColumnAngle((VestSide)s, c)));
                            float w = Mathf.Clamp01(1f - delta / 45f); // neighbouring columns are 45 deg apart
                            if (w <= 0.05f) continue;
                            ActiveColumns += (ActiveColumns.Length > 0 ? " + " : "") + VestLayout.ColumnName((VestSide)s, c) + $" {w * 100f:0}%";
                            if (level > 0f) frame.SetColumn((VestSide)s, c, level * Mathf.Sqrt(w), rowMask);
                        }
                    }
                    break;
                }
            }

            output.SetGuidanceFrame(frame);
        }

        int PairSectorWithHysteresis(float bearing)
        {
            float x = VestLayout.SectorCoordinate(bearing);
            int nearest = VestLayout.NearestSector(bearing);
            if (currentSector >= 0)
            {
                // circular distance (in sectors) from the current sector centre
                float dist = Mathf.Abs(Mathf.DeltaAngle(x * 45f, currentSector * 45f)) / 45f;
                if (dist < 0.5f + switchHysteresisDeg / 45f) return currentSector;
            }
            currentSector = nearest;
            return nearest;
        }

        float DistanceGain()
        {
            if (distanceCue != DistanceCue.Intensity) return 1f;
            float t = Mathf.InverseLerp(farDistance, nearDistance, Distance); // 0 far, 1 near
            return Mathf.Lerp(minIntensityFar, 1f, t);
        }

        float PulseGate()
        {
            if (distanceCue != DistanceCue.PulseRate) return 1f;
            float t = Mathf.InverseLerp(nearDistance, farDistance, Distance); // 0 near, 1 far
            float period = Mathf.Lerp(pulsePeriodNear, pulsePeriodFar, t);
            pulsePhase = (pulsePhase + Time.deltaTime / Mathf.Max(0.05f, period)) % 1f;
            return pulsePhase < pulseDuty ? 1f : 0f;
        }
    }
}
