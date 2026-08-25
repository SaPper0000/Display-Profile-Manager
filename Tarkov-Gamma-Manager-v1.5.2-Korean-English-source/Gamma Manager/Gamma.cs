using System;
using System.Runtime.InteropServices;

namespace Gamma_Manager
{
    internal class Gamma
    {
        private static T Clamp<T>(T val, T min, T max) where T : IComparable<T>
        {
            if (val.CompareTo(min) < 0) return min;
            else if (val.CompareTo(max) > 0) return max;
            else return val;
        }

        public static ushort[,] CreateGammaRamp(float rGamma, float gGamma, float bGamma, float rContrast, float gContrast, float bContrast, float rBright, float gBright, float bBright)
        {
            //Gamma check
            const float MaxGamma = 4.4f;
            const float MinGamma = 0.3f;
            rGamma = Clamp(rGamma, MinGamma, MaxGamma);
            gGamma = Clamp(gGamma, MinGamma, MaxGamma);
            bGamma = Clamp(bGamma, MinGamma, MaxGamma);

            //Contrast check 
            const float MaxContrast = 100.0f;
            const float MinContrast = 0.1f;
            rContrast = Clamp(rContrast, MinContrast, MaxContrast);
            gContrast = Clamp(gContrast, MinContrast, MaxContrast);
            bContrast = Clamp(bContrast, MinContrast, MaxContrast);

            //Brightness check
            const float MaxBright = 1.0f;
            const float MinBright = -1.0f;
            rBright = Clamp(rBright, MinBright, MaxBright);
            gBright = Clamp(gBright, MinBright, MaxBright);
            bBright = Clamp(bBright, MinBright, MaxBright);

            //Auxiliary parameters
            double rInvgamma = 1.0 / rGamma;
            double gInvgamma = 1.0 / gGamma;
            double bInvgamma = 1.0 / bGamma;
            double rNorm = Math.Pow(255.0, rInvgamma - 1.0);
            double gNorm = Math.Pow(255.0, gInvgamma - 1.0);
            double bNorm = Math.Pow(255.0, bInvgamma - 1.0);

            ushort[,] newGamma = new ushort[3, 256];

            for (int i = 0; i < 256; i++)
            {
                double rVal = i * rContrast - (rContrast - 1.0) * 127.0;
                double gVal = i * gContrast - (gContrast - 1.0) * 127.0;
                double bVal = i * bContrast - (bContrast - 1.0) * 127.0;

                // [Fix] 음수 거듭제곱 시 NaN이 반환되어 화면이 깨지는 현상 방지
                if (rGamma != 1.0f)
                {
                    double signR = Math.Sign(rVal);
                    rVal = signR * Math.Pow(Math.Abs(rVal), rInvgamma) / rNorm;
                }
                if (gGamma != 1.0f)
                {
                    double signG = Math.Sign(gVal);
                    gVal = signG * Math.Pow(Math.Abs(gVal), gInvgamma) / gNorm;
                }
                if (bGamma != 1.0f)
                {
                    double signB = Math.Sign(bVal);
                    bVal = signB * Math.Pow(Math.Abs(bVal), bInvgamma) / bNorm;
                }

                rVal += rBright * 128.0;
                gVal += gBright * 128.0;
                bVal += bBright * 128.0;

                newGamma[0, i] = (ushort)Clamp((int)Math.Round((rVal / 255.0) * 65535.0), 0, 65535); // r
                newGamma[1, i] = (ushort)Clamp((int)Math.Round((gVal / 255.0) * 65535.0), 0, 65535); // g
                newGamma[2, i] = (ushort)Clamp((int)Math.Round((bVal / 255.0) * 65535.0), 0, 65535); // b
            }
            return newGamma;
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[,] ramp);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool GetDeviceGammaRamp(IntPtr hdc, ushort[,] lpRamp);

        public static bool SetGammaRamp(string display_dc, ushort[,] newGammaArray)
        {
            if (string.IsNullOrEmpty(display_dc) || newGammaArray == null) return false;

            IntPtr hDC = CreateDC(null, display_dc, null, IntPtr.Zero);
            if (hDC == IntPtr.Zero)
            {
                Logger.Warn("Gamma Set failed: CreateDC returned NULL for " + display_dc);
                return false;
            }
            try
            {
                bool ok = SetDeviceGammaRamp(hDC, newGammaArray);
                if (!ok) Logger.Warn("Gamma SetDeviceGammaRamp failed for " + display_dc + ". Win32Error=" + Marshal.GetLastWin32Error());
                return ok;
            }
            finally { DeleteDC(hDC); }
        }

        public static ushort[,] GetGammaRamp(string display_dc)
        {
            if (string.IsNullOrEmpty(display_dc)) return null;
            ushort[,] ramp = new ushort[3, 256];
            IntPtr hDC = CreateDC(null, display_dc, null, IntPtr.Zero);
            if (hDC == IntPtr.Zero)
            {
                Logger.Warn("Gamma Get failed: CreateDC returned NULL for " + display_dc);
                return null;
            }
            try
            {
                if (!GetDeviceGammaRamp(hDC, ramp))
                {
                    Logger.Warn("Gamma GetDeviceGammaRamp failed for " + display_dc + ". Win32Error=" + Marshal.GetLastWin32Error());
                    return null;
                }
                return ramp;
            }
            finally { DeleteDC(hDC); }
        }

        public static bool SetRawGammaRamp(string display_dc, ushort[,] ramp)
        {
            if (string.IsNullOrEmpty(display_dc) || ramp == null) return false;
            IntPtr hDC = CreateDC(null, display_dc, null, IntPtr.Zero);
            if (hDC == IntPtr.Zero)
            {
                Logger.Warn("Raw gamma restore failed: CreateDC returned NULL for " + display_dc);
                return false;
            }
            try
            {
                bool ok = SetDeviceGammaRamp(hDC, ramp);
                if (!ok) Logger.Warn("Raw gamma restore failed for " + display_dc + ". Win32Error=" + Marshal.GetLastWin32Error());
                return ok;
            }
            finally { DeleteDC(hDC); }
        }
    }
}