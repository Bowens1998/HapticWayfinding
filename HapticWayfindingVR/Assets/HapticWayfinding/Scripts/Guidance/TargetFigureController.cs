using HapticWayfinding.Rig;
using UnityEngine;

namespace HapticWayfinding.Guidance
{
    public enum FigureMotion
    {
        /// <summary>Experimenter drags the figure in the top-down view, or moves it with the arrow keys.</summary>
        Manual,
        /// <summary>Figure walks in a circle around the participant.</summary>
        Orbit,
        /// <summary>Figure jumps to a random direction around the participant every few seconds.</summary>
        RandomJump,
        /// <summary>Figure walks around the glass booth to random spots, pauses, then walks on (default).</summary>
        Wander
    }

    /// <summary>
    /// Moves the "little person" (the target point) around the participant.
    /// Number keys 1-8 put it at a fixed direction RELATIVE to where the participant currently faces:
    /// 1 = front, 2 = front-right, 3 = right, 4 = back-right, 5 = back, 6 = back-left, 7 = left, 8 = front-left.
    /// </summary>
    public class TargetFigureController : MonoBehaviour
    {
        public Transform participant;
        public DirectionalHapticGuide guide;
        [Tooltip("Top-down camera used for mouse dragging.")]
        public Camera experimenterCamera;

        [Tooltip("Motion at start. G cycles Wander -> Manual -> Orbit -> RandomJump.")]
        public FigureMotion startMotion = FigureMotion.Wander;
        /// <summary>Current motion (runtime state; the start value comes from startMotion).</summary>
        [System.NonSerialized] public FigureMotion motion;
        [Tooltip("Distance from the participant for Orbit / RandomJump / number-key placement (m).")]
        public float radius = 2.5f;
        [Tooltip("Keep the figure inside this square (half size, m) around the world origin (clear of furniture along the walls).")]
        public float areaHalfSize = 5.6f;
        [Tooltip("Keep the figure OUT of this square (half size, m) around the origin: the glass booth the participant stands in. 0 = off.")]
        public float keepOutHalfSize = 1.75f;
        public float manualSpeed = 1.5f;
        public float orbitSpeedDegPerSec = 20f;
        public float randomJumpInterval = 5f;
        [Tooltip("RandomJump: snap random directions to multiples of 45 deg (column centres).")]
        public bool randomJumpSnapTo45 = false;
        [Header("Wander")]
        [Tooltip("Walking speed (m/s).")]
        public float wanderSpeed = 1.0f;
        [Tooltip("Distance from the booth centre the figure walks at (m). Inner edge clears the booth corners, outer edge stays inside the two structural columns.")]
        public Vector2 wanderRadius = new Vector2(2.6f, 3.6f);
        [Tooltip("How far around the booth (deg) each walk goes before the next stop.")]
        public Vector2 wanderTurnDeg = new Vector2(50f, 200f);
        [Tooltip("Pause at each stop (s). While standing the figure turns to face the participant.")]
        public Vector2 wanderPause = new Vector2(0.5f, 3f);
        [Tooltip("Turn the figure to face its walking direction.")]
        public bool faceMovement = true;

        public float LastJumpTime { get; private set; }
        public float LastPlacedBearing { get; private set; } = float.NaN;

        ViewController view;
        float orbitAngle;
        float wanderAngle, wanderR, wanderWait;
        bool wanderHasTarget;
        float jumpTimer;
        bool dragging;
        Vector3 lastPos;

        void Awake()
        {
            if (participant == null && Camera.main != null) participant = Camera.main.transform;
            if (guide == null) guide = FindObjectOfType<DirectionalHapticGuide>();
            motion = startMotion;
            view = FindObjectOfType<ViewController>();
            lastPos = transform.position;
        }

        public void CycleMotion()
        {
            // Wander -> Manual -> Orbit -> RandomJump -> Wander
            motion = motion == FigureMotion.Wander ? FigureMotion.Manual
                : motion == FigureMotion.RandomJump ? FigureMotion.Wander
                : (FigureMotion)((int)motion + 1);
            wanderHasTarget = false;
        }

        void Update()
        {
            switch (motion)
            {
                case FigureMotion.Manual: UpdateManual(); break;
                case FigureMotion.Orbit: UpdateOrbit(); break;
                case FigureMotion.RandomJump: UpdateRandomJump(); break;
                case FigureMotion.Wander: UpdateWander(); break;
            }
            HandleMouseDrag();
            ClampToArea();
            UpdateFacing();
        }

        void UpdateManual()
        {
            if (GUIUtility.keyboardControl != 0) return;
            Vector3 v = Vector3.zero;
            if (Input.GetKey(KeyCode.UpArrow)) v += Vector3.forward;
            if (Input.GetKey(KeyCode.DownArrow)) v += Vector3.back;
            if (Input.GetKey(KeyCode.RightArrow)) v += Vector3.right;
            if (Input.GetKey(KeyCode.LeftArrow)) v += Vector3.left;
            transform.position += v.normalized * manualSpeed * Time.deltaTime;
        }

        void UpdateOrbit()
        {
            if (participant == null) return;
            orbitAngle += orbitSpeedDegPerSec * Time.deltaTime;
            Vector3 c = participant.position;
            Vector3 offset = Quaternion.Euler(0f, orbitAngle, 0f) * Vector3.forward * radius;
            SetPosition(new Vector3(c.x + offset.x, transform.position.y, c.z + offset.z));
        }

        void UpdateRandomJump()
        {
            jumpTimer += Time.deltaTime;
            if (jumpTimer < randomJumpInterval) return;
            jumpTimer = 0f;
            float b = Random.Range(-180f, 180f);
            if (randomJumpSnapTo45) b = Mathf.Round(b / 45f) * 45f;
            PlaceAtRelativeBearing(b);
        }

        /// <summary>
        /// Walk in polar coordinates around the booth centre (world origin): angle and radius are blended
        /// together, so the path curves around the glass instead of cutting through it.
        /// </summary>
        void UpdateWander()
        {
            Vector3 p = transform.position;
            float a = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
            float r = new Vector2(p.x, p.z).magnitude;

            if (!wanderHasTarget)
            {
                float turn = Random.Range(wanderTurnDeg.x, wanderTurnDeg.y) * (Random.value < 0.5f ? -1f : 1f);
                wanderAngle = a + turn;
                wanderR = Random.Range(wanderRadius.x, wanderRadius.y);
                wanderHasTarget = true;
            }
            if (wanderWait > 0f) { wanderWait -= Time.deltaTime; return; }

            float da = Mathf.DeltaAngle(a, wanderAngle);
            float dr = wanderR - r;
            float arc = Mathf.Abs(da) * Mathf.Deg2Rad * Mathf.Max(r, wanderRadius.x);
            float remaining = Mathf.Sqrt(arc * arc + dr * dr);
            if (remaining < 0.05f)
            {
                wanderWait = Random.Range(wanderPause.x, wanderPause.y);
                wanderHasTarget = false;
                return;
            }
            float f = Mathf.Clamp01(wanderSpeed * Time.deltaTime / remaining);
            float na = (a + da * f) * Mathf.Deg2Rad;
            float nr = r + dr * f;
            SetPosition(new Vector3(Mathf.Sin(na) * nr, p.y, Mathf.Cos(na) * nr));
        }

        /// <summary>Place the figure at a bearing relative to the participant's current facing direction.</summary>
        public void PlaceAtRelativeBearing(float bearingDeg)
        {
            if (participant == null) return;
            Vector3 fwd = participant.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 dir = Quaternion.Euler(0f, bearingDeg, 0f) * fwd;
            Vector3 p = participant.position + dir * radius;
            SetPosition(new Vector3(p.x, transform.position.y, p.z));
            LastPlacedBearing = bearingDeg;
            LastJumpTime = Time.time;
            orbitAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }

        void SetPosition(Vector3 p) => transform.position = p;

        void HandleMouseDrag()
        {
            if (experimenterCamera == null || !experimenterCamera.isActiveAndEnabled) return;
            Vector3 mp = Input.mousePosition;
            bool inset = view != null && view.IsInset;
            Vector2 vp = default;
            bool over = inset ? view.TryMouseToMapViewport(mp, out vp) : experimenterCamera.pixelRect.Contains(mp);
            if (Input.GetMouseButtonDown(0) && over) dragging = true;
            if (Input.GetMouseButtonUp(0)) dragging = false;
            if (!dragging || (inset && !over)) return;

            Ray ray = inset ? experimenterCamera.ViewportPointToRay(new Vector3(vp.x, vp.y, 0f)) : experimenterCamera.ScreenPointToRay(mp);
            var ground = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
            if (ground.Raycast(ray, out float enter))
            {
                Vector3 hit = ray.GetPoint(enter);
                SetPosition(new Vector3(hit.x, transform.position.y, hit.z));
                if (motion != FigureMotion.Manual) { motion = FigureMotion.Manual; wanderHasTarget = false; }
            }
        }

        void ClampToArea()
        {
            Vector3 p = transform.position;
            p.x = Mathf.Clamp(p.x, -areaHalfSize, areaHalfSize);
            p.z = Mathf.Clamp(p.z, -areaHalfSize, areaHalfSize);
            if (keepOutHalfSize > 0f && Mathf.Abs(p.x) < keepOutHalfSize && Mathf.Abs(p.z) < keepOutHalfSize)
            {
                // push out through the nearest side of the booth
                if (Mathf.Abs(p.x) > Mathf.Abs(p.z)) p.x = Mathf.Sign(p.x == 0f ? 1f : p.x) * keepOutHalfSize;
                else p.z = Mathf.Sign(p.z == 0f ? 1f : p.z) * keepOutHalfSize;
            }
            transform.position = p;
        }

        void UpdateFacing()
        {
            Vector3 delta = transform.position - lastPos;
            delta.y = 0f;
            if (faceMovement && delta.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(delta), 10f * Time.deltaTime);
            lastPos = transform.position;
        }
    }
}
