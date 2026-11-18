using System;
using System.Globalization;
using System.IO;
using System.Text;
using HapticWayfinding.Guidance;
using HapticWayfinding.Haptics;
using HapticWayfinding.Rig;
using UnityEngine;

namespace HapticWayfinding.Logging
{
    /// <summary>
    /// Writes a CSV per recording (key L toggles): participant pose, figure position, bearing,
    /// active column and all 40 motor intensities, sampled at a fixed rate.
    /// Files go to &lt;project or build folder&gt;/Logs/.
    /// </summary>
    public class SessionLogger : MonoBehaviour
    {
        public DirectionalHapticGuide guide;
        public HapticVestOutput output;
        public TargetFigureController figure;
        public ViewController view;
        public string participantId = "P00";
        [Range(1f, 90f)] public float sampleRateHz = 30f;

        public bool IsRecording => writer != null;
        public string CurrentFile { get; private set; } = "";

        StreamWriter writer;
        float startTime, timer;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        void Awake()
        {
            if (guide == null) guide = FindObjectOfType<DirectionalHapticGuide>();
            if (output == null) output = FindObjectOfType<HapticVestOutput>();
            if (figure == null) figure = FindObjectOfType<TargetFigureController>();
            if (view == null) view = FindObjectOfType<ViewController>();
        }

        public static string LogFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));

        public void Toggle()
        {
            if (IsRecording) Stop(); else Begin();
        }

        public void Begin()
        {
            Stop();
            Directory.CreateDirectory(LogFolder);
            string safeId = string.Join("_", participantId.Split(Path.GetInvalidFileNameChars()));
            CurrentFile = Path.Combine(LogFolder, $"{safeId}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            writer = new StreamWriter(CurrentFile, false, new UTF8Encoding(false));
            var sb = new StringBuilder("time_s,event,head_x,head_y,head_z,head_yaw_deg,figure_x,figure_z,bearing_deg,distance_m,active_direction,active_columns,guidance_mode,distance_cue,visual_condition,figure_motion,haptics_on");
            for (int i = 0; i < 20; i++) sb.Append(",F_r").Append(i / 4 + 1).Append("c").Append(i % 4 + 1);
            for (int i = 0; i < 20; i++) sb.Append(",B_r").Append(i / 4 + 1).Append("c").Append(i % 4 + 1);
            writer.WriteLine(sb.ToString());
            startTime = Time.time;
            timer = 0f;
            WriteRow("start");
        }

        public void Stop()
        {
            if (writer == null) return;
            WriteRow("stop");
            writer.Flush();
            writer.Dispose();
            writer = null;
        }

        /// <summary>Adds a row tagged with an event name (e.g. a figure jump, a key press).</summary>
        public void Mark(string evt)
        {
            if (writer != null) WriteRow(evt);
        }

        void Update()
        {
            if (writer == null) return;
            timer += Time.deltaTime;
            if (timer >= 1f / sampleRateHz)
            {
                timer = 0f;
                WriteRow("");
            }
        }

        void WriteRow(string evt)
        {
            var sb = new StringBuilder(512);
            sb.Append((Time.time - startTime).ToString("0.000", Inv)).Append(',').Append(evt);
            Transform head = guide != null ? guide.body : null;
            if (head != null)
            {
                Vector3 p = head.position;
                sb.Append(',').Append(p.x.ToString("0.000", Inv)).Append(',').Append(p.y.ToString("0.000", Inv)).Append(',').Append(p.z.ToString("0.000", Inv));
                sb.Append(',').Append(head.eulerAngles.y.ToString("0.0", Inv));
            }
            else sb.Append(",,,,");
            if (figure != null)
            {
                Vector3 fp = figure.transform.position;
                sb.Append(',').Append(fp.x.ToString("0.000", Inv)).Append(',').Append(fp.z.ToString("0.000", Inv));
            }
            else sb.Append(",,");
            if (guide != null)
            {
                sb.Append(',').Append(guide.Bearing.ToString("0.0", Inv)).Append(',').Append(guide.Distance.ToString("0.000", Inv));
                sb.Append(',').Append(Csv(guide.ActiveDirection));
                sb.Append(',').Append(Csv(guide.ActiveColumns));
                sb.Append(',').Append(guide.mode).Append(',').Append(guide.distanceCue);
            }
            else sb.Append(",,,,,,");
            sb.Append(',').Append(view != null ? view.condition.ToString() : "");
            sb.Append(',').Append(figure != null ? figure.motion.ToString() : "");
            sb.Append(',').Append(output != null && output.hapticsEnabled ? "1" : "0");
            if (output != null)
            {
                foreach (float v in output.CurrentOutput.front) sb.Append(',').Append(Mathf.RoundToInt(v * 100).ToString(Inv));
                foreach (float v in output.CurrentOutput.back) sb.Append(',').Append(Mathf.RoundToInt(v * 100).ToString(Inv));
            }
            writer.WriteLine(sb.ToString());
        }

        static string Csv(string s) => string.IsNullOrEmpty(s) ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";

        void OnDisable() => Stop();
        void OnApplicationQuit() => Stop();
    }
}
