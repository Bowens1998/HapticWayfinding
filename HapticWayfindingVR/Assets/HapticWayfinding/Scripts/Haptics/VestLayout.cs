using System;
using UnityEngine;

namespace HapticWayfinding.Haptics
{
    public enum VestSide { Front = 0, Back = 1 }

    /// <summary>
    /// Geometry of a 40-motor bHaptics vest (Tactot / TactSuit X40):
    /// 20 motors on the front, 20 on the back, each panel 4 columns x 5 rows.
    ///
    /// Inside this project every column is addressed in WEARER coordinates:
    /// wearerCol 0 = the wearer's left side, wearerCol 3 = the wearer's right side,
    /// on both the front and the back panel. Converting to the device's own motor
    /// index happens only in <see cref="DeviceIndex"/>, so if the motor order on the
    /// real vest turns out to be mirrored, flip one checkbox here.
    ///
    /// The 8 columns are treated as 8 directions around the torso. Bearing convention:
    /// 0 deg = straight ahead, +90 = right, -90 = left, +/-180 = behind.
    /// </summary>
    [Serializable]
    public class VestLayout
    {
        public const int Columns = 4;
        public const int Rows = 5;
        public const int MotorsPerSide = Columns * Rows;

        [Tooltip("Direction (deg) that each FRONT column points to, wearer's left -> right.")]
        public float[] frontColumnAngles = { -67.5f, -22.5f, 22.5f, 67.5f };

        [Tooltip("Direction (deg) that each BACK column points to, wearer's left -> right.")]
        public float[] backColumnAngles = { -112.5f, -157.5f, 157.5f, 112.5f };

        [Tooltip("bHaptics VestFront: is device index 0 on the wearer's LEFT? Verified in the lab 2026-11-18 (default was mirrored).")]
        public bool frontIndex0IsWearersLeft = true;

        [Tooltip("bHaptics VestBack: is device index 0 on the wearer's LEFT? Verified in the lab 2026-11-18 (column test matched).")]
        public bool backIndex0IsWearersLeft = true;

        public float ColumnAngle(VestSide side, int wearerCol)
        {
            return side == VestSide.Front ? frontColumnAngles[wearerCol] : backColumnAngles[wearerCol];
        }

        /// <summary>Device motor index (0..19) for the bHaptics VestFront / VestBack position.</summary>
        public int DeviceIndex(VestSide side, int row, int wearerCol)
        {
            bool leftFirst = side == VestSide.Front ? frontIndex0IsWearersLeft : backIndex0IsWearersLeft;
            int col = leftFirst ? wearerCol : (Columns - 1 - wearerCol);
            return row * Columns + col;
        }

        /// <summary>
        /// The 8 columns in clockwise order seen from above, starting at the front-left:
        /// F-left-outer, F-left-inner, F-right-inner, F-right-outer, B-right-outer, B-right-inner, B-left-inner, B-left-outer.
        /// </summary>
        public static readonly (VestSide side, int col)[] Ring =
        {
            (VestSide.Front, 0), (VestSide.Front, 1), (VestSide.Front, 2), (VestSide.Front, 3),
            (VestSide.Back, 3), (VestSide.Back, 2), (VestSide.Back, 1), (VestSide.Back, 0)
        };

        /// <summary>
        /// 8 direction sectors, 45 deg each, centred on 0 / 45 / 90 / ... Sector s drives the two neighbouring
        /// columns Ring[s] and Ring[s+1]:
        ///   s=0 front-left (-45)  : front left-outer + left-inner
        ///   s=1 front (0)         : front left-inner + right-inner (the middle two)
        ///   s=2 front-right (+45) : front right-inner + right-outer
        ///   s=3 right (+90)       : front right-outer + back right-outer
        ///   s=4 back-right (+135) : back right-outer + right-inner
        ///   s=5 back (180)        : back right-inner + left-inner (the middle two)
        ///   s=6 back-left (-135)  : back left-inner + left-outer
        ///   s=7 left (-90)        : back left-outer + front left-outer
        /// </summary>
        public static readonly string[] SectorNames =
            { "Front-left", "Front", "Front-right", "Right", "Back-right", "Back", "Back-left", "Left" };

        public static float SectorCenter(int sector) => Mathf.DeltaAngle(0f, -45f + 45f * sector);

        /// <summary>Continuous sector coordinate: integer values are sector centres.</summary>
        public static float SectorCoordinate(float bearingDeg) => (Mathf.DeltaAngle(0f, bearingDeg) + 45f) / 45f;

        public static int NearestSector(float bearingDeg)
        {
            int s = Mathf.RoundToInt(SectorCoordinate(bearingDeg));
            return ((s % 8) + 8) % 8;
        }

        /// <summary>Finds the column (of all 8) whose direction is closest to the bearing.</summary>
        public void NearestColumn(float bearingDeg, out VestSide side, out int wearerCol)
        {
            side = VestSide.Front;
            wearerCol = 0;
            float best = float.MaxValue;
            for (int s = 0; s < 2; s++)
            {
                for (int c = 0; c < Columns; c++)
                {
                    float d = Mathf.Abs(Mathf.DeltaAngle(bearingDeg, ColumnAngle((VestSide)s, c)));
                    if (d < best)
                    {
                        best = d;
                        side = (VestSide)s;
                        wearerCol = c;
                    }
                }
            }
        }

        public static string ColumnName(VestSide side, int wearerCol)
        {
            string[] names = { "left-outer", "left-inner", "right-inner", "right-outer" };
            return (side == VestSide.Front ? "Front " : "Back ") + names[wearerCol];
        }
    }

    /// <summary>One frame of motor intensities (0..1), stored in wearer coordinates [row * 4 + wearerCol].</summary>
    public class VestFrame
    {
        public readonly float[] front = new float[VestLayout.MotorsPerSide];
        public readonly float[] back = new float[VestLayout.MotorsPerSide];

        public float[] Side(VestSide side) => side == VestSide.Front ? front : back;

        public void Clear()
        {
            Array.Clear(front, 0, front.Length);
            Array.Clear(back, 0, back.Length);
        }

        public void CopyFrom(VestFrame other)
        {
            Array.Copy(other.front, front, front.Length);
            Array.Copy(other.back, back, back.Length);
        }

        public void Scale(float k)
        {
            for (int i = 0; i < front.Length; i++) front[i] = Mathf.Clamp01(front[i] * k);
            for (int i = 0; i < back.Length; i++) back[i] = Mathf.Clamp01(back[i] * k);
        }

        public void SetColumn(VestSide side, int wearerCol, float intensity, bool[] rowMask = null)
        {
            float[] arr = Side(side);
            for (int r = 0; r < VestLayout.Rows; r++)
            {
                if (rowMask != null && r < rowMask.Length && !rowMask[r]) continue;
                arr[r * VestLayout.Columns + wearerCol] = Mathf.Max(arr[r * VestLayout.Columns + wearerCol], intensity);
            }
        }

        public void Fill(float intensity)
        {
            for (int i = 0; i < front.Length; i++) front[i] = intensity;
            for (int i = 0; i < back.Length; i++) back[i] = intensity;
        }

        public bool IsEmpty()
        {
            for (int i = 0; i < front.Length; i++) if (front[i] > 0.001f) return false;
            for (int i = 0; i < back.Length; i++) if (back[i] > 0.001f) return false;
            return true;
        }
    }
}
