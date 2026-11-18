using System.Collections;
using UnityEngine;

namespace HapticWayfinding.Haptics
{
    /// <summary>
    /// Hardware check routines, run from the experimenter keyboard:
    ///  C = column test: buzz each of the 8 columns in turn (front L->R, then back L->R) and show its name on screen.
    ///      Ask the wearer where they feel it. If front/back left-right is swapped, flip the
    ///      frontIndex0IsWearersLeft / backIndex0IsWearersLeft checkboxes on HapticVestOutput.layout.
    ///  K = sweep: the active column rotates once around the torso (clockwise seen from above).
    ///  J = motor test: every motor one by one (device index order) to find dead motors.
    /// </summary>
    public class VestCalibration : MonoBehaviour
    {
        public HapticVestOutput output;
        [Range(0f, 1f)] public float testIntensity = 0.8f;
        public float columnOnSeconds = 0.8f;
        public float gapSeconds = 0.4f;

        public string CurrentStep { get; private set; } = "";

        readonly VestFrame frame = new VestFrame();

        void Awake()
        {
            if (output == null) output = FindObjectOfType<HapticVestOutput>();
        }

        public void RunColumnTest() => output.PlayOverride(ColumnTest(), "Column test");
        public void RunSweep() => output.PlayOverride(Sweep(), "Sweep");
        public void RunMotorTest() => output.PlayOverride(MotorTest(), "Motor test");

        IEnumerator ColumnTest()
        {
            for (int s = 0; s < 2; s++)
            {
                for (int c = 0; c < VestLayout.Columns; c++)
                {
                    var side = (VestSide)s;
                    CurrentStep = $"{VestLayout.ColumnName(side, c)}  ({output.layout.ColumnAngle(side, c):0.#} deg)";
                    frame.Clear();
                    frame.SetColumn(side, c, testIntensity);
                    output.SetOverrideFrame(frame);
                    yield return new WaitForSeconds(columnOnSeconds);
                    frame.Clear();
                    output.SetOverrideFrame(frame);
                    yield return new WaitForSeconds(gapSeconds);
                }
            }
            CurrentStep = "";
        }

        IEnumerator Sweep()
        {
            // Visit columns in clockwise order (seen from above): front L->R, back R->L.
            int[][] order =
            {
                new[] {0, 0}, new[] {0, 1}, new[] {0, 2}, new[] {0, 3},
                new[] {1, 3}, new[] {1, 2}, new[] {1, 1}, new[] {1, 0}
            };
            for (int lap = 0; lap < 2; lap++)
            {
                foreach (var oc in order)
                {
                    var side = (VestSide)oc[0];
                    CurrentStep = "Sweep: " + VestLayout.ColumnName(side, oc[1]);
                    frame.Clear();
                    frame.SetColumn(side, oc[1], testIntensity);
                    output.SetOverrideFrame(frame);
                    yield return new WaitForSeconds(0.35f);
                }
            }
            frame.Clear();
            output.SetOverrideFrame(frame);
            CurrentStep = "";
        }

        IEnumerator MotorTest()
        {
            for (int s = 0; s < 2; s++)
            {
                var side = (VestSide)s;
                for (int r = 0; r < VestLayout.Rows; r++)
                {
                    for (int c = 0; c < VestLayout.Columns; c++)
                    {
                        CurrentStep = $"{side} row {r + 1} {VestLayout.ColumnName(side, c)} -> device index {output.layout.DeviceIndex(side, r, c)}";
                        frame.Clear();
                        frame.Side(side)[r * VestLayout.Columns + c] = testIntensity;
                        output.SetOverrideFrame(frame);
                        yield return new WaitForSeconds(0.3f);
                    }
                }
            }
            frame.Clear();
            output.SetOverrideFrame(frame);
            CurrentStep = "";
        }
    }
}
