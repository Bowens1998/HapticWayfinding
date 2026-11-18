using UnityEngine;

namespace HapticWayfinding.Guidance
{
    /// <summary>
    /// Procedural walk cycle for the jointed mannequin built by the scene builder.
    /// Drives hips / knees / shoulders / elbows from the distance the figure actually moves,
    /// so legs never slide: dragging, arrow keys, orbit all animate correctly. Teleports
    /// (number keys, random jumps) are detected and not animated. When standing still the
    /// figure slowly turns to face the participant and breathes slightly.
    /// </summary>
    public class ProceduralWalker : MonoBehaviour
    {
        [Header("Joints (assigned by the scene builder)")]
        public Transform hips;
        public Transform spine;
        public Transform head;
        public Transform leftHip, rightHip, leftKnee, rightKnee, leftAnkle, rightAnkle;
        public Transform leftShoulder, rightShoulder, leftElbow, rightElbow;

        [Header("Gait")]
        [Tooltip("Distance (m) covered by one full gait cycle (two steps).")]
        public float cycleLength = 1.3f;
        public float hipSwingDeg = 26f;
        public float kneeBendDeg = 50f;
        public float armSwingDeg = 22f;
        public float elbowBendDeg = 18f;
        public float bobHeight = 0.03f;
        [Tooltip("Speed (m/s) at which the walk animation reaches full amplitude.")]
        public float fullWalkSpeed = 1.0f;

        [Header("Idle")]
        public Transform faceTarget;
        public float idleTurnDegPerSec = 70f;

        Vector3 lastPos;
        float phase, walkWeight, hipsBaseY, breath;

        void Start()
        {
            lastPos = transform.position;
            if (hips != null) hipsBaseY = hips.localPosition.y;
            if (faceTarget == null && Camera.main != null) faceTarget = Camera.main.transform;
        }

        void LateUpdate()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 d = transform.position - lastPos;
            d.y = 0f;
            lastPos = transform.position;
            float dist = d.magnitude;
            if (dist > 0.5f) dist = 0f; // teleport, not a step

            float speed = Mathf.Min(dist / dt, 3f);
            float targetWeight = Mathf.Clamp01(speed / fullWalkSpeed);
            walkWeight = Mathf.Lerp(walkWeight, targetWeight, 1f - Mathf.Exp(-8f * dt));
            phase += dist / Mathf.Max(0.1f, cycleLength) * Mathf.PI * 2f;
            if (walkWeight < 0.02f) phase = Mathf.Lerp(phase, Mathf.Round(phase / Mathf.PI) * Mathf.PI, 1f - Mathf.Exp(-5f * dt));

            float w = walkWeight;
            float s = Mathf.Sin(phase), c = Mathf.Cos(phase);

            // Positive X rotation swings a hanging limb backwards; negative swings it forwards.
            Set(leftHip, -s * hipSwingDeg * w, 0f, 0f);
            Set(rightHip, s * hipSwingDeg * w, 0f, 0f);
            Set(leftKnee, (Mathf.Max(0f, c) * kneeBendDeg + 5f) * w, 0f, 0f);
            Set(rightKnee, (Mathf.Max(0f, -c) * kneeBendDeg + 5f) * w, 0f, 0f);
            Set(leftAnkle, -Mathf.Max(0f, c) * 12f * w, 0f, 0f);
            Set(rightAnkle, -Mathf.Max(0f, -c) * 12f * w, 0f, 0f);

            Set(leftShoulder, s * armSwingDeg * w, 0f, -6f);
            Set(rightShoulder, -s * armSwingDeg * w, 0f, 6f);
            Set(leftElbow, -(8f + elbowBendDeg * w * (0.6f + 0.4f * Mathf.Max(0f, -s))), 0f, 0f);
            Set(rightElbow, -(8f + elbowBendDeg * w * (0.6f + 0.4f * Mathf.Max(0f, s))), 0f, 0f);

            breath += dt * Mathf.PI * 2f / 4.5f;
            if (spine != null) spine.localRotation = Quaternion.Euler(3f * w + Mathf.Sin(breath) * 0.6f, s * 7f * w, 0f);
            if (hips != null)
            {
                var p = hips.localPosition;
                p.y = hipsBaseY - bobHeight * w * (0.5f + 0.5f * Mathf.Cos(2f * phase));
                hips.localPosition = p;
                hips.localRotation = Quaternion.Euler(0f, -s * 5f * w, s * 2f * w);
            }

            // Standing: turn to face the participant.
            if (faceTarget != null && w < 0.15f)
            {
                Vector3 to = faceTarget.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f)
                {
                    var want = Quaternion.LookRotation(to.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, want, idleTurnDegPerSec * dt);
                }
            }
        }

        static void Set(Transform t, float x, float y, float z)
        {
            if (t != null) t.localRotation = Quaternion.Euler(x, y, z);
        }
    }
}
