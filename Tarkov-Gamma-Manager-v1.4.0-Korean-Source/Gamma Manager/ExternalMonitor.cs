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
            return false;
        }

        public static void SetBrightness(IntPtr hPhysicalMonitor, uint brightness)
        {
            uint min = 0, cur = 0, max = 100;
            if (GetMonitorBrightness(hPhysicalMonitor, ref min, ref cur, ref max) && max > min)
            {
                uint realNewValue = min + (uint)Math.Round((max - min) * Math.Max(0, Math.Min(100, (int)brightness)) / 100.0);
                SetMonitorBrightness(hPhysicalMonitor, realNewValue);
            }
            else
            {
                SetMonitorBrightness(hPhysicalMonitor, Math.Min(100, brightness));
            }
        }

        public static int GetBrightness(IntPtr hPhysicalMonitor)
        {
            int value;
            return TryGetBrightness(hPhysicalMonitor, out value) ? value : -1;
        }

        public static void SetContrast(IntPtr hPhysicalMonitor, uint contrast)
        {
            uint min = 0, cur = 0, max = 100;
            if (GetMonitorContrast(hPhysicalMonitor, ref min, ref cur, ref max) && max > min)
            {
                uint realNewValue = min + (uint)Math.Round((max - min) * Math.Max(0, Math.Min(100, (int)contrast)) / 100.0);
                SetMonitorContrast(hPhysicalMonitor, realNewValue);
            }
            else
            {
                SetMonitorContrast(hPhysicalMonitor, Math.Min(100, contrast));
            }
        }

        public static int GetContrast(IntPtr hPhysicalMonitor)
        {
            int value;
            return TryGetContrast(hPhysicalMonitor, out value) ? value : -1;
        }
    }
}
