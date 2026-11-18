using UnityEngine;
using UnityEngine.XR;

namespace HapticWayfinding.Rig
{
    /// <summary>
    /// When no headset is active, lets you play the participant with keyboard + mouse
    /// so everything (including the real vest) can be tested at a desk:
    ///   W/A/S/D move, Q/E turn, hold right mouse button and move the mouse to look around.
    /// </summary>
    public class DesktopSimulator : MonoBehaviour
    {
        public Transform head;
        public float eyeHeight = 1.65f;
        public float moveSpeed = 1.4f;
        public float turnSpeed = 90f;
        public float mouseSensitivity = 3f;

        float yaw, pitch;
        bool initialised;

        public bool Active => !XRSettings.isDeviceActive;

        void Awake()
        {
            if (head == null && Camera.main != null) head = Camera.main.transform;
        }

        void Update()
        {
            if (!Active || head == null) return;
            if (!initialised)
            {
                yaw = head.eulerAngles.y;
                var p = head.localPosition;
                if (p.y < 0.5f) head.localPosition = new Vector3(p.x, eyeHeight, p.z);
                initialised = true;
            }
            if (GUIUtility.keyboardControl != 0) return;

            if (Input.GetKey(KeyCode.Q)) yaw -= turnSpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.E)) yaw += turnSpeed * Time.deltaTime;
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -80f, 80f);
            }
            head.rotation = Quaternion.Euler(pitch, yaw, 0f);

            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            Vector3 v = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) v += fwd;
            if (Input.GetKey(KeyCode.S)) v -= fwd;
            if (Input.GetKey(KeyCode.D)) v += right;
            if (Input.GetKey(KeyCode.A)) v -= right;
            head.position += v.normalized * moveSpeed * Time.deltaTime;
        }
    }
}
