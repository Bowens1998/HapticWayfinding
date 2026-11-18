using UnityEngine;

namespace HapticWayfinding.Rig
{
    /// <summary>
    /// Red marker on the floor that follows the participant's head (position + yaw).
    /// Lives on the ExperimenterOnly layer, so it is shown in the top-down view but not in the headset.
    /// </summary>
    public class ParticipantMarker : MonoBehaviour
    {
        public Transform head;
        public float floorY = 0.03f; // above the VR floor mat

        void Awake()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
        }

        void LateUpdate()
        {
            if (head == null) return;
            Vector3 p = head.position;
            transform.position = new Vector3(p.x, floorY, p.z);
            Vector3 f = head.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
        }
    }
}
