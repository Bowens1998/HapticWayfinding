using HapticWayfinding.Guidance;
using HapticWayfinding.Haptics;
using HapticWayfinding.Logging;
using HapticWayfinding.Rig;
using UnityEngine;
using UnityEngine.XR;

namespace HapticWayfinding.UI
{
    /// <summary>
    /// Experimenter keyboard controls + status panel on the desktop window.
    /// Press F1 to show / hide the key list and the full status (vest mapping etc.).
    /// By default only a compact status block is shown, so the headset mirror stays visible.
    /// </summary>
    public class ExperimenterHUD : MonoBehaviour
    {
        public HapticVestOutput output;
        public DirectionalHapticGuide guide;
        public TargetFigureController figure;
        public ViewController view;
        public VestCalibration calibration;
        public SessionLogger logger;
        public HapticVestVisualizer visualizer;
        public HmdPoseDriver hmd;

        [Tooltip("Show the key list and full status at start (F1 toggles).")]
        public bool showHelpOnStart = false;
        [System.NonSerialized] public bool showHelp;
        GUIStyle box;

        void Awake()
        {
            if (output == null) output = FindObjectOfType<HapticVestOutput>();
            if (guide == null) guide = FindObjectOfType<DirectionalHapticGuide>();
            if (figure == null) figure = FindObjectOfType<TargetFigureController>();
            if (view == null) view = FindObjectOfType<ViewController>();
            if (calibration == null) calibration = FindObjectOfType<VestCalibration>();
            if (logger == null) logger = FindObjectOfType<SessionLogger>();
            if (visualizer == null) visualizer = FindObjectOfType<HapticVestVisualizer>();
            if (hmd == null) hmd = FindObjectOfType<HmdPoseDriver>();
            showHelp = showHelpOnStart;
        }

        void Update()
        {
            if (GUIUtility.keyboardControl != 0) return; // typing in the participant ID field

            if (Input.GetKeyDown(KeyCode.F1)) showHelp = !showHelp;
            if (Input.GetKeyDown(KeyCode.H)) { output.hapticsEnabled = !output.hapticsEnabled; Mark("haptics_" + (output.hapticsEnabled ? "on" : "off")); }
            if (Input.GetKeyDown(KeyCode.M)) { guide.mode = (GuidanceMode)(((int)guide.mode + 1) % 4); Mark("mode_" + guide.mode); }
            if (Input.GetKeyDown(KeyCode.N)) { guide.distanceCue = (DistanceCue)(((int)guide.distanceCue + 1) % 3); Mark("distcue_" + guide.distanceCue); }
            if (Input.GetKeyDown(KeyCode.V)) { view.CycleCondition(); Mark("visual_" + view.condition); }
            if (Input.GetKeyDown(KeyCode.Tab)) view.CycleLayout();
            if (Input.GetKeyDown(KeyCode.R) && hmd != null)
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (shift) { hmd.ClearRecenter(); Mark("recenter_clear"); }
                else { hmd.Recenter(); Mark("recenter"); }
            }
            if (Input.GetKeyDown(KeyCode.G)) { figure.CycleMotion(); Mark("figure_" + figure.motion); }
            if (Input.GetKeyDown(KeyCode.C)) calibration.RunColumnTest();
            if (Input.GetKeyDown(KeyCode.K)) calibration.RunSweep();
            if (Input.GetKeyDown(KeyCode.J)) calibration.RunMotorTest();
            if (Input.GetKeyDown(KeyCode.X)) output.StopOverride();
            if (Input.GetKeyDown(KeyCode.L)) logger.Toggle();
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) output.masterIntensity = Mathf.Clamp01(output.masterIntensity + 0.1f);
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) output.masterIntensity = Mathf.Clamp01(output.masterIntensity - 0.1f);
            if (Input.GetKeyDown(KeyCode.F2)) visualizer.show = !visualizer.show;

            // Lab calibration of the vest mapping (saved immediately, kept for the next launch)
            if (Input.GetKeyDown(KeyCode.F5)) { output.useSingleVestPosition = !output.useSingleVestPosition; output.SaveCalibration(); }
            if (Input.GetKeyDown(KeyCode.F6)) { output.layout.frontIndex0IsWearersLeft = !output.layout.frontIndex0IsWearersLeft; output.SaveCalibration(); }
            if (Input.GetKeyDown(KeyCode.F7)) { output.layout.backIndex0IsWearersLeft = !output.layout.backIndex0IsWearersLeft; output.SaveCalibration(); }
            if (Input.GetKeyDown(KeyCode.F8)) { output.ResetCalibration(); }

            // 1..8: place figure at 0, 45, ... 315 deg relative to where the participant faces now
            for (int k = 0; k < 8; k++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + k) || Input.GetKeyDown(KeyCode.Keypad1 + k))
                {
                    float b = Mathf.DeltaAngle(0f, k * 45f);
                    figure.motion = FigureMotion.Manual;
                    figure.PlaceAtRelativeBearing(b);
                    Mark("place_" + b.ToString("0"));
                }
            }
        }

        void Mark(string evt) => logger?.Mark(evt);

        void OnGUI()
        {
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true, wordWrap = true };
                box.normal.textColor = Color.white;
            }

            string xr = XRSettings.isDeviceActive
                ? (hmd != null && hmd.IsTracking ? "<color=#7f7>HMD tracking</color>" : "<color=#fc6>HMD active, no pose</color>")
                : "<color=#fc6>No HMD - desktop simulator (WASD/QE, right-drag to look)</color>";
            string vest = output.BackendConnected
                ? "<color=#7f7>" + output.BackendStatus + "</color>" + $"  ({output.MessagesPerSecond:0.0} msg/s)"
                : "<color=#f77>" + output.BackendStatus + "</color>";
            string col = guide.ActiveColumns.Length > 0 ? guide.ActiveDirection + "  ->  " + guide.ActiveColumns : "-";
            string rec = logger.IsRecording ? "<color=#f55>REC</color> " + System.IO.Path.GetFileName(logger.CurrentFile) : "not recording";
            string calib = calibration != null && calibration.CurrentStep.Length > 0 ? "\n<b>Calibration:</b> <color=#ff7>" + calibration.CurrentStep + "</color>" : "";

            string status;
            if (showHelp)
                status =
                    "<b>Haptic Wayfinding</b>\n" +
                    "XR: " + xr + "\n" +
                    "Vest: " + vest + "\n" +
                    $"Bearing: <b>{guide.Bearing:+0;-0;0} deg</b>   Distance: {guide.Distance:0.00} m\n" +
                    $"Active: <b>{col}</b>\n" +
                    $"Mode: {guide.mode}   Distance cue: {guide.distanceCue}   Intensity x{output.masterIntensity:0.0}   Haptics: {(output.hapticsEnabled ? "ON" : "OFF")}\n" +
                    $"Visual: {view.condition}   Figure: {figure.motion}   Layout: {view.layout}   Recentered: {(hmd != null && hmd.IsRecentered ? "yes [R / Shift+R]" : "no [R]")}\n" +
                    $"Vest mapping: send as {(output.useSingleVestPosition ? "Vest (40)" : "VestFront+VestBack")} [F5]   " +
                    $"front idx0 = wearer's {(output.layout.frontIndex0IsWearersLeft ? "LEFT" : "RIGHT")} [F6]   " +
                    $"back idx0 = wearer's {(output.layout.backIndex0IsWearersLeft ? "LEFT" : "RIGHT")} [F7]\n" +
                    "Log: " + rec + calib;
            else
                status =
                    "XR: " + xr + "\n" +
                    "Vest: " + vest + "\n" +
                    $"Bearing <b>{guide.Bearing:+0;-0;0}</b>  {guide.Distance:0.0} m   <b>{col}</b>\n" +
                    $"{guide.mode} | {view.condition} | figure {figure.motion} | haptics {(output.hapticsEnabled ? "ON" : "OFF")}\n" +
                    "Log: " + rec + "    <i>F1 keys / details</i>" + calib;
            GUILayout.BeginArea(new Rect(10, 10, showHelp ? 520 : 400, Screen.height - 20));
            GUILayout.Label(status, box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Participant ID", GUILayout.Width(95));
            logger.participantId = GUILayout.TextField(logger.participantId, GUILayout.Width(120));
            if (GUILayout.Button("Done", GUILayout.Width(50))) GUIUtility.keyboardControl = 0;
            GUILayout.EndHorizontal();

            if (showHelp)
            {
                GUILayout.Label(
                    "<b>Keys</b> (F1 hide)\n" +
                    "1-8  put figure at front / front-right / right / back-right / back / back-left / left / front-left\n" +
                    "Arrows / left-drag on map  move figure     G  figure motion: Wander / Manual / Orbit / RandomJump\n" +
                    "M  guidance: AdjacentPair / SingleColumn / Interpolated / Off     N  distance cue: None / PulseRate / Intensity\n" +
                    "V  booth glass: clear / frosted (fades)     Tab  desktop layout: Inset / Split / MapOnly / HeadsetOnly\n" +
                    "R  recenter: participant's current spot = booth centre, current heading = forward (saved)     Shift+R  undo\n" +
                    "H  haptics on/off     + / -  intensity     F2  vest visualizer\n" +
                    "C  column test     K  sweep     J  motor test     X  stop test\n" +
                    "L  start / stop CSV log\n" +
                    "F5 send format   F6 flip front L/R   F7 flip back L/R   F8 reset mapping (saved automatically)", box);
            }
            GUILayout.EndArea();
        }
    }
}
