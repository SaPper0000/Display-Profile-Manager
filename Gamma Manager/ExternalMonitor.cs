using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;

namespace Gamma_Manager
{
    internal class ExternalMonitor
    {
        // 다중 모니터 병렬 제어를 위해 모니터 핸들별 독립적인 Lock 관리
        private static readonly ConcurrentDictionary<IntPtr, object> handleLocks =
            new ConcurrentDictionary<IntPtr, object>();

        private static readonly object fallbackLock = new object();

        public static object GetLockForHandle(IntPtr handle)
        {
            if (handle == IntPtr.Zero || handle == (IntPtr)(-1))
            {
                return fallbackLock;
            }

            return handleLocks.GetOrAdd(handle, _ => new object());
        }

        public static void RemoveLockForHandle(IntPtr handle)
        {
            if (handle != IntPtr.Zero && handle != (IntPtr)(-1))
            {
                handleLocks.TryRemove(handle, out _);
            }
        }

        public static void ClearAllLocks()
        {
            handleLocks.Clear();
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
            if (handle == IntPtr.Zero || handle == (IntPtr)(-1)) return false;

            try
            {
                uint min = 0, cur = 0, max = 0;
                if (!GetMonitorBrightness(handle, ref min, ref cur, ref max))
                {
                    Logger.Warn($"GetMonitorBrightness API failed. Win32Error: {Marshal.GetLastWin32Error()}, Handle: {handle}");
                    return false;
                }
                if (max <= min) return false;

                cur = Math.Max(min, Math.Min(max, cur));
                value = (int)Math.Round((cur - min) * 100.0 / (max - min));
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"GetMonitorBrightness exception: {ex.Message}");
                return false;
            }
        }

        private static bool TryGetContrastOnce(IntPtr handle, out int value)
        {
            value = 0;
            if (handle == IntPtr.Zero || handle == (IntPtr)(-1)) return false;

            try
            {
                uint min = 0, cur = 0, max = 0;
                if (!GetMonitorContrast(handle, ref min, ref cur, ref max))
                {
                    Logger.Warn($"GetMonitorContrast API failed. Win32Error: {Marshal.GetLastWin32Error()}, Handle: {handle}");
                    return false;
                }
                if (max <= min) return false;

                cur = Math.Max(min, Math.Min(max, cur));
                value = (int)Math.Round((cur - min) * 100.0 / (max - min));
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"GetMonitorContrast exception: {ex.Message}");
                return false;
            }
        }

        public static bool TryGetBrightness(IntPtr handle, out int value)
        {
            value = 0;
            if (handle == IntPtr.Zero || handle == (IntPtr)(-1)) return false;

            lock (GetLockForHandle(handle))
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (TryGetBrightnessOnce(handle, out value)) return true;
                    Thread.Sleep(30);
                }
                Logger.Warn($"DDC/CI brightness read failed after 3 attempts. Handle: {handle}");
                return false;
            }
        }

        public static bool TryGetContrast(IntPtr handle, out int value)
        {
            value = 0;
            if (handle == IntPtr.Zero || handle == (IntPtr)(-1)) return false;

            lock (GetLockForHandle(handle))
            {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (TryGetContrastOnce(handle, out value)) return true;
                    Thread.Sleep(30);
                }
                Logger.Warn($"DDC/CI contrast read failed after 3 attempts. Handle: {handle}");
                return false;
            }
        }

        public static bool SetBrightness(IntPtr hPhysicalMonitor, uint brightness)
        {
            if (hPhysicalMonitor == IntPtr.Zero || hPhysicalMonitor == (IntPtr)(-1)) return false;

            lock (GetLockForHandle(hPhysicalMonitor))
            {
                int target = Math.Max(0, Math.Min(100, (int)brightness));

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        uint min = 0, cur = 0, max = 100;
                        uint realNewValue = (uint)target;
                        if (GetMonitorBrightness(hPhysicalMonitor, ref min, ref cur, ref max) && max > min)
                            realNewValue = min + (uint)Math.Round((max - min) * target / 100.0);

                        if (SetMonitorBrightness(hPhysicalMonitor, realNewValue))
                        {
                            return true;
                        }

                        Logger.Warn($"SetMonitorBrightness API failed on attempt {attempt + 1}. Win32Error: {Marshal.GetLastWin32Error()}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"SetMonitorBrightness exception on attempt {attempt + 1}: {ex.Message}");
                    }

                    Thread.Sleep(30 * (attempt + 1));
                }

                Logger.Warn($"DDC/CI brightness write failed after 3 attempts. Target: {target}, Handle: {hPhysicalMonitor}");
                return false;
            }
        }

        public static bool SetContrast(IntPtr hPhysicalMonitor, uint contrast)
        {
            if (hPhysicalMonitor == IntPtr.Zero || hPhysicalMonitor == (IntPtr)(-1)) return false;

            lock (GetLockForHandle(hPhysicalMonitor))
            {
                int target = Math.Max(0, Math.Min(100, (int)contrast));

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        uint min = 0, cur = 0, max = 100;
                        uint realNewValue = (uint)target;
                        if (GetMonitorContrast(hPhysicalMonitor, ref min, ref cur, ref max) && max > min)
                            realNewValue = min + (uint)Math.Round((max - min) * target / 100.0);

                        if (SetMonitorContrast(hPhysicalMonitor, realNewValue))
                        {
                            return true;
                        }

                        Logger.Warn($"SetMonitorContrast API failed on attempt {attempt + 1}. Win32Error: {Marshal.GetLastWin32Error()}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"SetMonitorContrast exception on attempt {attempt + 1}: {ex.Message}");
                    }

                    Thread.Sleep(30 * (attempt + 1));
                }

                Logger.Warn($"DDC/CI contrast write failed after 3 attempts. Target: {target}, Handle: {hPhysicalMonitor}");
                return false;
            }
        }

        public static bool SetBrightnessAndContrast(IntPtr handle, int brightness, int contrast)
        {
            if (handle == IntPtr.Zero || handle == (IntPtr)(-1)) return false;

            lock (GetLockForHandle(handle))
            {
                int targetBrightness = Math.Max(0, Math.Min(100, brightness));
                int targetContrast = Math.Max(0, Math.Min(100, contrast));

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        uint bMin = 0, bCur = 0, bMax = 100;
                        uint realBrightness = (uint)targetBrightness;
                        if (GetMonitorBrightness(handle, ref bMin, ref bCur, ref bMax) && bMax > bMin)
                            realBrightness = bMin + (uint)Math.Round((bMax - bMin) * targetBrightness / 100.0);

                        uint cMin = 0, cCur = 0, cMax = 100;
                        uint realContrast = (uint)targetContrast;
                        if (GetMonitorContrast(handle, ref cMin, ref cCur, ref cMax) && cMax > cMin)
                            realContrast = cMin + (uint)Math.Round((cMax - cMin) * targetContrast / 100.0);

                        // 1. 밝기 쓰기
                        bool bSetOk = SetMonitorBrightness(handle, realBrightness);

                        // VESA DDC/CI 규격 준수를 위한 40ms 대기 (I2C 버스 릴리즈)
                        Thread.Sleep(40);

                        // 2. 대비 쓰기
                        bool cSetOk = SetMonitorContrast(handle, realContrast);

                        if (bSetOk && cSetOk)
                        {
                            return true;
                        }

                        Logger.Warn($"SetBrightnessAndContrast failed on attempt {attempt + 1}. Win32Error: {Marshal.GetLastWin32Error()}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"SetBrightnessAndContrast exception on attempt {attempt + 1}: {ex.Message}");
                    }

                    Thread.Sleep(30 * (attempt + 1));
                }

                Logger.Warn($"DDC/CI combined write failed after 3 attempts. Handle: {handle}");
                return false;
            }
        }

        public static int GetBrightness(IntPtr hPhysicalMonitor)
        {
            int value;
            return TryGetBrightness(hPhysicalMonitor, out value) ? value : -1;
        }

        public static int GetContrast(IntPtr hPhysicalMonitor)
        {
            int value;
            return TryGetContrast(hPhysicalMonitor, out value) ? value : -1;
        }
    }
}