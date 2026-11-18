using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace HapticWayfinding.Rig
{
    /// <summary>
    /// Applies the headset pose (HTC VIVE Pro through SteamVR / OpenXR) to this camera.
    /// Uses the generic XR InputDevices API so it does not depend on Input System action setup.
    /// Also requests floor-level tracking so y = 0 is the real floor.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class HmdPoseDriver : MonoBehaviour
    {
        public bool IsTracking { get; private set; }

        static readonly List<XRInputSubsystem> subsystems = new List<XRInputSubsystem>();
        bool originSet;

        void OnEnable() => Application.onBeforeRender += OnBeforeRender;
        void OnDisable() => Application.onBeforeRender -= OnBeforeRender;

        void Update()
        {
            if (!originSet) TrySetFloorOrigin();
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
