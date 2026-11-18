using System.Collections;
using UnityEngine;

namespace HapticWayfinding.Haptics
{
    /// <summary>
    /// Single place where motor intensities leave the app.
    /// Other scripts write a "guidance" frame every Update; short one-shot patterns
    /// (calibration, alerts) temporarily override it. A changed pattern is sent to the bHaptics Player
    /// immediately (rate-limited); an unchanged one is refreshed periodically to keep playing.
    /// Runs late in the frame so it always sends this frame's guidance (no one-frame lag).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class HapticVestOutput : MonoBehaviour
    {
        public VestLayout layout = new VestLayout();

        [Header("bHaptics Player connection")]
        public bool useBHapticsPlayer = true;
        public string bHapticsUrl = "ws://127.0.0.1:15881/v2/feedbacks?app_id=edu.ufl.icic.hapticwayfinding&app_name=HapticWayfinding";

        [Header("Output")]
        [Tooltip("Global on/off switch (key H).")]
        public bool hapticsEnabled = true;
        [Range(0f, 1f)] public float masterIntensity = 1f;
        [Tooltip("Pattern changes are sent at most this often (s). Faster changes are merged (latest wins). " +
                 "Sending faster than the vest's Bluetooth link can take makes the delay pile up.")]
        [Range(0.02f, 0.3f)] public float minChangeInterval = 0.06f;
        [Tooltip("Unchanged patterns are re-sent this often (s) so they keep playing; frames last 3x this long.")]
        [Range(0.3f, 3f)] public float refreshInterval = 1f;

        readonly VestFrame guidanceFrame = new VestFrame();
        readonly VestFrame overrideFrame = new VestFrame();
        readonly VestFrame outFrame = new VestFrame();
        readonly VestFrame lastSent = new VestFrame();
        bool overrideActive;
        bool hasSent;
        float sinceSend, sinceRefresh;
        long msgCountMark;
        float msgRateTimer;

        /// <summary>WebSocket messages per second actually sent to the bHaptics Player (shown on the HUD).</summary>
        public float MessagesPerSecond { get; private set; }
        Coroutine overrideRoutine;
        BHapticsWebSocketBackend bhaptics;

        /// <summary>What the vest is (supposed to be) doing right now. Read by the visualizer and logger.</summary>
        public VestFrame CurrentOutput => outFrame;
        public bool OverrideActive => overrideActive;
        public string OverrideLabel { get; private set; } = "";

        public string BackendStatus
        {
            get
            {
                if (!useBHapticsPlayer) return "bHaptics disabled (visualizer only)";
                return bhaptics == null ? "bHaptics not started" : bhaptics.Status;
            }
        }
        public bool BackendConnected => bhaptics != null && bhaptics.IsConnected;
        public string PlayerMessage => bhaptics == null ? "" : bhaptics.LastPlayerMessage;

        [Tooltip("Send to position \"Vest\" (40 indices) instead of \"VestFront\"/\"VestBack\". Toggle at runtime with F5.")]
        public bool useSingleVestPosition = false;

        // Lab calibration survives restarts of the built exe (PlayerPrefs = registry on Windows).
        const string PrefSingle = "hw_singleVestPosition", PrefFront = "hw_frontIndex0IsLeft", PrefBack = "hw_backIndex0IsLeft";

        void Awake()
        {
            if (PlayerPrefs.HasKey(PrefSingle)) useSingleVestPosition = PlayerPrefs.GetInt(PrefSingle) == 1;
            if (PlayerPrefs.HasKey(PrefFront)) layout.frontIndex0IsWearersLeft = PlayerPrefs.GetInt(PrefFront) == 1;
            if (PlayerPrefs.HasKey(PrefBack)) layout.backIndex0IsWearersLeft = PlayerPrefs.GetInt(PrefBack) == 1;
        }

        public void SaveCalibration()
        {
            PlayerPrefs.SetInt(PrefSingle, useSingleVestPosition ? 1 : 0);
            PlayerPrefs.SetInt(PrefFront, layout.frontIndex0IsWearersLeft ? 1 : 0);
            PlayerPrefs.SetInt(PrefBack, layout.backIndex0IsWearersLeft ? 1 : 0);
            PlayerPrefs.Save();
        }

        public void ResetCalibration()
        {
            PlayerPrefs.DeleteKey(PrefSingle);
            PlayerPrefs.DeleteKey(PrefFront);
            PlayerPrefs.DeleteKey(PrefBack);
            useSingleVestPosition = false;
            layout.frontIndex0IsWearersLeft = false;
            layout.backIndex0IsWearersLeft = true;
        }

        void OnEnable()
        {
            if (useBHapticsPlayer && bhaptics == null)
                bhaptics = new BHapticsWebSocketBackend(bHapticsUrl, layout);
        }

        void OnDisable()
        {
            bhaptics?.Dispose();
            bhaptics = null;
        }

        /// <summary>Called by the guidance script each frame with the desired continuous pattern.</summary>
        public void SetGuidanceFrame(VestFrame frame)
        {
            guidanceFrame.CopyFrom(frame);
        }

        public void ClearGuidance() => guidanceFrame.Clear();

        /// <summary>Plays a sequence of (frame, seconds) steps that temporarily replaces guidance.</summary>
        public void PlayOverride(IEnumerator sequence, string label)
        {
            StopOverride();
            OverrideLabel = label;
            overrideRoutine = StartCoroutine(RunOverride(sequence));
        }

        public void StopOverride()
        {
            if (overrideRoutine != null) StopCoroutine(overrideRoutine);
            overrideRoutine = null;
            overrideActive = false;
            OverrideLabel = "";
            overrideFrame.Clear();
        }

        /// <summary>Used from inside override sequences.</summary>
        public void SetOverrideFrame(VestFrame frame) => overrideFrame.CopyFrom(frame);

        IEnumerator RunOverride(IEnumerator sequence)
        {
            overrideActive = true;
            yield return sequence;
            overrideActive = false;
            overrideFrame.Clear();
            OverrideLabel = "";
            overrideRoutine = null;
        }

        void LateUpdate()
        {
            outFrame.CopyFrom(overrideActive ? overrideFrame : guidanceFrame);
            if (!hapticsEnabled) outFrame.Clear();
            else outFrame.Scale(masterIntensity);

            bool modeChanged = false;
            if (bhaptics != null && bhaptics.singleVestPosition != useSingleVestPosition)
            {
                bhaptics.singleVestPosition = useSingleVestPosition;
                modeChanged = true;
            }

            float dt = Time.unscaledDeltaTime;
            sinceSend += dt;
            sinceRefresh += dt;
            bool changed = !hasSent || !SameQuantized(outFrame, lastSent);
            bool refresh = modeChanged || sinceRefresh >= refreshInterval || (bhaptics != null && bhaptics.NeedsFullRefresh);
            if ((changed && sinceSend >= minChangeInterval) || refresh)
            {
                // Long frame duration: nothing needs re-sending while the pattern stays the same.
                // Changes are still immediate: the backend sends a new frame / turnOff only for keys that changed.
                int durationMs = Mathf.CeilToInt(refreshInterval * 1000f * 3f);
                bhaptics?.Send(outFrame, durationMs, refresh);
                lastSent.CopyFrom(outFrame);
                hasSent = true;
                sinceSend = 0f;
                if (refresh) sinceRefresh = 0f;
            }

            msgRateTimer += dt;
            if (msgRateTimer >= 1f && bhaptics != null)
            {
                long n = bhaptics.MessagesSent;
                MessagesPerSecond = (n - msgCountMark) / msgRateTimer;
                msgCountMark = n;
                msgRateTimer = 0f;
            }
        }

        static bool SameQuantized(VestFrame a, VestFrame b)
        {
            for (int i = 0; i < a.front.Length; i++)
                if (Mathf.RoundToInt(a.front[i] * 100f) != Mathf.RoundToInt(b.front[i] * 100f)) return false;
            for (int i = 0; i < a.back.Length; i++)
                if (Mathf.RoundToInt(a.back[i] * 100f) != Mathf.RoundToInt(b.back[i] * 100f)) return false;
            return true;
        }

        void OnApplicationQuit()
        {
            bhaptics?.StopAll();
        }
    }
}
