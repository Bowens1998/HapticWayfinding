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
        RandomJump
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

        public FigureMotion motion = FigureMotion.Manual;
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
        [Tooltip("Turn the figure to face its walking direction.")]
        public bool faceMovement = true;

        public float LastJumpTime { get; private set; }
        public float LastPlacedBearing { get; private set; } = float.NaN;

        float orbitAngle;
        float jumpTimer;
        bool dragging;
        Vector3 lastPos;

        void Awake()
        {
            if (participant == null && Camera.main != null) participant = Camera.main.transform;
            if (guide == null) guide = FindObjectOfType<DirectionalHapticGuide>();
            lastPos = transform.position;
        }

        void Update()
        {
            switch (motion)
            {
                case FigureMotion.Manual: UpdateManual(); break;
                case FigureMotion.Orbit: UpdateOrbit(); break;
                case FigureMotion.RandomJump: UpdateRandomJump(); break;
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
            Rect r = experimenterCamera.pixelRect;
            if (Input.GetMouseButtonDown(0) && r.Contains(mp)) dragging = true;
            if (Input.GetMouseButtonUp(0)) dragging = false;
            if (!dragging) return;

            Ray ray = experimenterCamera.ScreenPointToRay(mp);
            var ground = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
            if (ground.Raycast(ray, out float enter))
            {
                Vector3 hit = ray.GetPoint(enter);
                SetPosition(new Vector3(hit.x, transform.position.y, hit.z));
                if (motion != FigureMotion.Manual) motion = FigureMotion.Manual;
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
