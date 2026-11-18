using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace HapticWayfinding.Rig
{
    /// <summary>
    /// Applies the headset pose (HTC VIVE Pro through SteamVR / OpenXR) to this camera.
    /// Uses the generic XR InputDevices API so it does not depend on Input System action setup.
    /// Also requests floor-level tracking so y = 0 is the real floor.
    /// Recenter (key R): moves / turns the parent rig so the headset's current position becomes the
    /// glass booth centre (world origin) and its current heading becomes +Z. Saved per machine
    /// (PlayerPrefs), so it survives restarts as long as the SteamVR room setup is unchanged.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class HmdPoseDriver : MonoBehaviour
    {
        public bool IsTracking { get; private set; }

        /// <summary>True when a saved recenter offset is applied to the rig.</summary>
        public bool IsRecentered { get; private set; }

        static readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
        bool originSet;
        const string PrefX = "HW_RecenterX", PrefZ = "HW_RecenterZ", PrefYaw = "HW_RecenterYaw";

        bool savedOffsetChecked;

        // The saved offset belongs to the headset's tracking space, so it is only applied once an HMD is
        // active (the desktop simulator keeps the rig at the origin).
        void LoadSavedOffset()
        {
            savedOffsetChecked = true;
            if (transform.parent != null && PlayerPrefs.HasKey(PrefYaw))
            {
                ApplyRig(PlayerPrefs.GetFloat(PrefX), PlayerPrefs.GetFloat(PrefZ), PlayerPrefs.GetFloat(PrefYaw));
                IsRecentered = true;
            }
        }

        /// <summary>Make the headset's current spot the booth centre and its current heading "forward" (+Z).</summary>
        public void Recenter()
        {
            if (transform.parent == null) return;
            Vector3 lp = transform.localPosition;
            Vector3 f = transform.localRotation * Vector3.forward;
            float yaw = (f.x * f.x + f.z * f.z) > 1e-6f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
            Vector3 offset = -(Quaternion.Euler(0f, -yaw, 0f) * new Vector3(lp.x, 0f, lp.z));
            ApplyRig(offset.x, offset.z, -yaw);
            PlayerPrefs.SetFloat(PrefX, offset.x);
            PlayerPrefs.SetFloat(PrefZ, offset.z);
            PlayerPrefs.SetFloat(PrefYaw, -yaw);
            PlayerPrefs.Save();
            IsRecentered = true;
        }

        /// <summary>Back to the SteamVR room-setup origin.</summary>
        public void ClearRecenter()
        {
            if (transform.parent != null) ApplyRig(0f, 0f, 0f);
            PlayerPrefs.DeleteKey(PrefX);
            PlayerPrefs.DeleteKey(PrefZ);
            PlayerPrefs.DeleteKey(PrefYaw);
            PlayerPrefs.Save();
            IsRecentered = false;
        }

        void ApplyRig(float x, float z, float yaw)
        {
            var rig = transform.parent;
            rig.SetPositionAndRotation(new Vector3(x, rig.position.y, z), Quaternion.Euler(0f, yaw, 0f));
        }

        void OnEnable() => Application.onBeforeRender += OnBeforeRender;
        void OnDisable() => Application.onBeforeRender -= OnBeforeRender;

        void Update()
        {
            if (!originSet) TrySetFloorOrigin();
            if (!savedOffsetChecked && XRSettings.isDeviceActive) LoadSavedOffset();
            UpdatePose();
        }

        void OnBeforeRender() => UpdatePose();

        void TrySetFloorOrigin()
        {
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var s in subsystems)
            {
                if (s.running && s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor))
                    originSet = true;
            }
        }

        void UpdatePose()
        {
            if (!XRSettings.isDeviceActive)
            {
                IsTracking = false;
                return;
            }
            var dev = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!dev.isValid) dev = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!dev.isValid)
            {
                IsTracking = false;
                return;
            }

            bool gotPos = dev.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 pos)
                          || dev.TryGetFeatureValue(CommonUsages.devicePosition, out pos);
            bool gotRot = dev.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rot)
                          || dev.TryGetFeatureValue(CommonUsages.deviceRotation, out rot);
            if (gotPos) transform.localPosition = pos;
            if (gotRot) transform.localRotation = rot;
            IsTracking = gotPos && gotRot;
        }
    }
}
