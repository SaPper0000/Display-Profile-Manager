using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace Gamma_Manager
{
    internal class ExternalMonitor
    {
        // 다중 모니터 병렬 제어를 위해, 모니터 핸들별로 독립적인 락(Lock)을 생성하여 관리합니다.
        private static readonly ConcurrentDictionary<IntPtr, object> ddcLocks = new ConcurrentDictionary<IntPtr, object>();

        private static object GetLockForHandle(IntPtr handle)
        {
            return ddcLocks.GetOrAdd(handle, _ => new object());
        }

        #region DllImport
        [DllImport("dxva2.dll", EntryPoint = "GetMonitorBrightness", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorBrightness(IntPtr handle, ref uint minimumBrightness, ref uint currentBrightness, ref uint maxBrightness);

        [DllImport("dxva2.dll", EntryPoint = "SetMonitorBrightness", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetMonitorBrightness(IntPtr handle, uint newBrightness);

        [DllImport("dxva2.dll", EntryPoint = "GetMonitorContrast", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorContrast(IntPtr handle, ref uint minimumContrast, ref uint currentContrast, ref uint maxContrast);

        [DllImport("dxva2.dll", EntryPoint = "SetMonitorContrast", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetMonitorContrast(IntPtr handle, uint newContrast);
        #endregion

        private static bool TryGetBrightnessOnce(IntPtr handle, out int value)
        {
            value = 0;
            uint min = 0, cur = 0, max = 0;
            if (!GetMonitorBrightness(handle, ref min, ref cur, ref max))
            {
                Logger.Warn($"GetMonitorBrightness API call failed. Win32Error: {Marshal.GetLastWin32Error()}, Handle: {handle}");
                return false;
            }
            if (max <= min || cur < min || cur > max) return false;
            value = (int)Math.Round((cur - min) * 100.0 / (max - min));
            return true;
        }

        private static bool TryGetContrastOnce(IntPtr handle, out int value)
        {
            value = 0;
            uint min = 0, cur = 0, max = 0;
            if (!GetMonitorContrast(handle, ref min, ref cur, ref max))
            {
                Logger.Warn($"GetMonitorContrast API call failed. Win32Error: {Marshal.GetLastWin32Error()}, Handle: {handle}");
                return false;
            }
            if (max <= min || cur < min || cur > max) return false;
            value = (int)Math.Round((cur - min) * 100.0 / (max - min));
            return true;
        }

        public static bool TryGetBrightness(IntPtr handle, out int value)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (TryGetBrightnessOnce(handle, out value)) return true;
                Thread.Sleep(20); // 안정성을 위해 대기 시간 소폭 증가
            }
            value = 0;
            Logger.Warn($"DDC/CI brightness read failed after 3 attempts. Handle: {handle}");
            return false;
        }

        public static bool TryGetContrast(IntPtr handle, out int value)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (TryGetContrastOnce(handle, out value)) return true;
                Thread.Sleep(20);
            }
            value = 0;
            Logger.Warn($"DDC/CI contrast read failed after 3 attempts. Handle: {handle}");
            return false;
        }

        public static bool SetBrightness(IntPtr hPhysicalMonitor, uint brightness)
        {
            int target = Math.Max(0, Math.Min(100, (int)brightness));

            for (int attempt = 0; attempt < 3; attempt++)
            {
                uint min = 0, cur = 0, max = 100;
                uint realNewValue = (uint)target;
                if (GetMonitorBrightness(hPhysicalMonitor, ref min, ref cur, ref max) && max > min)
                    realNewValue = min + (uint)Math.Round((max - min) * target / 100.0);

                if (SetMonitorBrightness(hPhysicalMonitor, realNewValue))
                {
                    Thread.Sleep(20); // VCP 처리 시간 보장
                    int readback;
                    if (TryGetBrightnessOnce(hPhysicalMonitor, out readback) && Math.Abs(readback - target) <= 2) // 허용 오차 2로 상향
                        return true;
                }
                else
                {
                    Logger.Warn($"SetMonitorBrightness API failed on attempt {attempt + 1}. Win32Error: {Marshal.GetLastWin32Error()}");
                }
                Thread.Sleep(20);
            }
            Logger.Warn($"DDC/CI brightness write/verify failed after 3 attempts. Target: {target}, Handle: {hPhysicalMonitor}");
            return false;
        }

        public static bool SetBrightnessAndContrast(IntPtr handle, int brightness, int contrast)
        {
            if (handle == IntPtr.Zero || handle == (IntPtr)(-1)) return false;

            // 모니터 핸들별로 생성된 락을 사용하여, 서로 다른 모니터는 병렬로 제어될 수 있게 합니다.
            lock (GetLockForHandle(handle))
            {
                int targetBrightness = Math.Max(0, Math.Min(100, brightness));
                int targetContrast = Math.Max(0, Math.Min(100, contrast));

                bool brightnessOk = SetBrightness(handle, (uint)targetBrightness);
                Thread.Sleep(30); // 밝기/대비 동시 변경 시 모니터 칩셋 과부하 방지
                bool contrastOk = SetContrast(handle, (uint)targetContrast);

                int actualBrightness;
                int actualContrast;
                bool brightnessVerified = TryGetBrightnessOnce(handle, out actualBrightness) && Math.Abs(actualBrightness - targetBrightness) <= 2;
                bool contrastVerified = TryGetContrastOnce(handle, out actualContrast) && Math.Abs(actualContrast - targetContrast) <= 2;

                if (!brightnessVerified)
                {
                    brightnessOk = SetBrightness(handle, (uint)targetBrightness);
                    brightnessVerified = TryGetBrightnessOnce(handle, out actualBrightness) && Math.Abs(actualBrightness - targetBrightness) <= 2;
                }
                if (!contrastVerified)
                {
                    contrastOk = SetContrast(handle, (uint)targetContrast);
                    contrastVerified = TryGetContrastOnce(handle, out actualContrast) && Math.Abs(actualContrast - targetContrast) <= 2;
                }

                if (!brightnessVerified || !contrastVerified)
                {
                    Logger.Warn($"DDC/CI B/C verify failed. Target: {targetBrightness}/{targetContrast}, Actual: {(brightnessVerified ? actualBrightness.ToString() : "?")}/{(contrastVerified ? actualContrast.ToString() : "?")}, Handle: {handle}");
                }

                return brightnessVerified && contrastVerified;
            }
        }

        public static int GetBrightness(IntPtr hPhysicalMonitor)
        {
            int value;
            return TryGetBrightness(hPhysicalMonitor, out value) ? value : -1;
        }

        public static bool SetContrast(IntPtr hPhysicalMonitor, uint contrast)
        {
            int target = Math.Max(0, Math.Min(100, (int)contrast));

            for (int attempt = 0; attempt < 3; attempt++)
            {
                uint min = 0, cur = 0, max = 100;
                uint realNewValue = (uint)target;
                if (GetMonitorContrast(hPhysicalMonitor, ref min, ref cur, ref max) && max > min)
                    realNewValue = min + (uint)Math.Round((max - min) * target / 100.0);

                if (SetMonitorContrast(hPhysicalMonitor, realNewValue))
                {
                    Thread.Sleep(20);
                    int readback;
                    if (TryGetContrastOnce(hPhysicalMonitor, out readback) && Math.Abs(readback - target) <= 2)
                        return true;
                }
                else
                {
                    Logger.Warn($"SetMonitorContrast API failed on attempt {attempt + 1}. Win32Error: {Marshal.GetLastWin32Error()}");
                }
                Thread.Sleep(20);
            }
            Logger.Warn($"DDC/CI contrast write/verify failed after 3 attempts. Target: {target}, Handle: {hPhysicalMonitor}");
            return false;
        }

        public static int GetContrast(IntPtr hPhysicalMonitor)
        {
            int value;
            return TryGetContrast(hPhysicalMonitor, out value) ? value : -1;
        }
    }
}