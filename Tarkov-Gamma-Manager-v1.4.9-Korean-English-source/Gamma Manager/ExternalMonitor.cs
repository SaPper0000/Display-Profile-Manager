using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Gamma_Manager
{
    internal class ExternalMonitor
    {
        #region DllImport
        [DllImport("dxva2.dll", EntryPoint = "GetMonitorBrightness")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorBrightness(IntPtr handle, ref uint minimumBrightness, ref uint currentBrightness, ref uint maxBrightness);

        [DllImport("dxva2.dll", EntryPoint = "SetMonitorBrightness")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetMonitorBrightness(IntPtr handle, uint newBrightness);

        [DllImport("dxva2.dll", EntryPoint = "GetMonitorContrast")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorContrast(IntPtr handle, ref uint minimumContrast, ref uint currentContrast, ref uint maxContrast);

        [DllImport("dxva2.dll", EntryPoint = "SetMonitorContrast")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetMonitorContrast(IntPtr handle, uint newContrast);
        #endregion

        private static bool TryGetBrightnessOnce(IntPtr handle, out int value)
        {
            value = 0;
            uint min = 0, cur = 0, max = 0;
            if (!GetMonitorBrightness(handle, ref min, ref cur, ref max)) return false;
            if (max <= min || cur < min || cur > max) return false;
            value = (int)Math.Round((cur - min) * 100.0 / (max - min));
            return true;
        }

        private static bool TryGetContrastOnce(IntPtr handle, out int value)
        {
            value = 0;
            uint min = 0, cur = 0, max = 0;
            if (!GetMonitorContrast(handle, ref min, ref cur, ref max)) return false;
            if (max <= min || cur < min || cur > max) return false;
            value = (int)Math.Round((cur - min) * 100.0 / (max - min));
            return true;
        }

        public static bool TryGetBrightness(IntPtr handle, out int value)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (TryGetBrightnessOnce(handle, out value)) return true;
                Thread.Sleep(10);
            }
            value = 0;
            Logger.Warn("DDC/CI brightness read failed after 3 attempts. Handle=" + handle);
            return false;
        }

        public static bool TryGetContrast(IntPtr handle, out int value)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (TryGetContrastOnce(handle, out value)) return true;
                Thread.Sleep(10);
            }
            value = 0;
            Logger.Warn("DDC/CI contrast read failed after 3 attempts. Handle=" + handle);
            return false;
        }

        public static bool SetBrightness(IntPtr hPhysicalMonitor, uint brightness)
        {
            int target = Math.Max(0, Math.Min(100, (int)brightness));

            // DDC/CI monitors can occasionally ignore a VCP write when another
            // command was sent immediately before it. Retry and verify the value
            // so rapid toggle ON/OFF cannot leave one control behind.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                uint min = 0, cur = 0, max = 100;
                uint realNewValue = (uint)target;
                if (GetMonitorBrightness(hPhysicalMonitor, ref min, ref cur, ref max) && max > min)
                    realNewValue = min + (uint)Math.Round((max - min) * target / 100.0);

                if (SetMonitorBrightness(hPhysicalMonitor, realNewValue))
                {
                    Thread.Sleep(25);
                    int readback;
                    if (TryGetBrightnessOnce(hPhysicalMonitor, out readback) && Math.Abs(readback - target) <= 1)
                        return true;
                }
                Thread.Sleep(25);
            }
            Logger.Warn("DDC/CI brightness write/verify failed after 3 attempts. Target=" + target + ", Handle=" + hPhysicalMonitor);
            return false;
        }


        public static bool SetBrightnessAndContrast(IntPtr handle, int brightness, int contrast)
        {
            if (handle == IntPtr.Zero || handle == (IntPtr)(-1)) return false;

            int targetBrightness = Math.Max(0, Math.Min(100, brightness));
            int targetContrast = Math.Max(0, Math.Min(100, contrast));

            // Apply the two VCP values sequentially. Some monitors drop the second
            // command when brightness and contrast are written back-to-back. The
            // individual setters already retry/read-back, and the small gap here
            // gives slower DDC/CI implementations time to finish the first command.
            bool brightnessOk = SetBrightness(handle, (uint)targetBrightness);
            Thread.Sleep(40);
            bool contrastOk = SetContrast(handle, (uint)targetContrast);
            Thread.Sleep(20);

            int actualBrightness;
            int actualContrast;
            bool brightnessVerified = TryGetBrightnessOnce(handle, out actualBrightness) && Math.Abs(actualBrightness - targetBrightness) <= 1;
            bool contrastVerified = TryGetContrastOnce(handle, out actualContrast) && Math.Abs(actualContrast - targetContrast) <= 1;

            // If a monitor accepted one command but dropped the other, retry only the
            // failed control instead of needlessly changing the successful one.
            if (!brightnessVerified)
            {
                Logger.Warn("DDC/CI final brightness verification failed. Retrying target=" + targetBrightness + ", Handle=" + handle);
                brightnessOk = SetBrightness(handle, (uint)targetBrightness);
                Thread.Sleep(40);
                brightnessVerified = TryGetBrightnessOnce(handle, out actualBrightness) && Math.Abs(actualBrightness - targetBrightness) <= 1;
            }

            if (!contrastVerified)
            {
                Logger.Warn("DDC/CI final contrast verification failed. Retrying target=" + targetContrast + ", Handle=" + handle);
                contrastOk = SetContrast(handle, (uint)targetContrast);
                Thread.Sleep(40);
                contrastVerified = TryGetContrastOnce(handle, out actualContrast) && Math.Abs(actualContrast - targetContrast) <= 1;
            }

            if (!brightnessVerified || !contrastVerified)
            {
                Logger.Warn("DDC/CI brightness/contrast final verification failed. Target=" + targetBrightness + "/" + targetContrast +
                    ", Actual=" + (brightnessVerified ? actualBrightness.ToString() : "?") + "/" +
                    (contrastVerified ? actualContrast.ToString() : "?") + ", Handle=" + handle);
            }

            return brightnessVerified && contrastVerified;
        }

        public static int GetBrightness(IntPtr hPhysicalMonitor)
        {
            int value;
            return TryGetBrightness(hPhysicalMonitor, out value) ? value : -1;
        }

        public static bool SetContrast(IntPtr hPhysicalMonitor, uint contrast)
        {
            int target = Math.Max(0, Math.Min(100, (int)contrast));

            // Contrast is especially prone to being dropped when brightness and
            // contrast are written back-to-back. Retry + read back the actual VCP
            // value before reporting success.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                uint min = 0, cur = 0, max = 100;
                uint realNewValue = (uint)target;
                if (GetMonitorContrast(hPhysicalMonitor, ref min, ref cur, ref max) && max > min)
                    realNewValue = min + (uint)Math.Round((max - min) * target / 100.0);

                if (SetMonitorContrast(hPhysicalMonitor, realNewValue))
                {
                    Thread.Sleep(25);
                    int readback;
                    if (TryGetContrastOnce(hPhysicalMonitor, out readback) && Math.Abs(readback - target) <= 1)
                        return true;
                }
                Thread.Sleep(25);
            }
            Logger.Warn("DDC/CI contrast write/verify failed after 3 attempts. Target=" + target + ", Handle=" + hPhysicalMonitor);
            return false;
        }

        public static int GetContrast(IntPtr hPhysicalMonitor)
        {
            int value;
            return TryGetContrast(hPhysicalMonitor, out value) ? value : -1;
        }

    }
}
