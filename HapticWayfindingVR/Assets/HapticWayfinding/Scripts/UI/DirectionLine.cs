using HapticWayfinding.Guidance;
using UnityEngine;

namespace HapticWayfinding.UI
{
    /// <summary>Line on the floor from the participant to the figure (experimenter map only).</summary>
    [RequireComponent(typeof(LineRenderer))]
    public class DirectionLine : MonoBehaviour
    {
        public DirectionalHapticGuide guide;
        public float floorY = 0.03f;
        LineRenderer line;

        void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.positionCount = 2;
            if (guide == null) guide = FindObjectOfType<DirectionalHapticGuide>();
        }

        void LateUpdate()
        {
            bool on = guide != null && guide.HasTarget;
            line.enabled = on;
            if (!on) return;
            Vector3 a = guide.body.position, b = guide.target.position;
            line.SetPosition(0, new Vector3(a.x, floorY, a.z));
            line.SetPosition(1, new Vector3(b.x, floorY, b.z));
        }
    }
}
