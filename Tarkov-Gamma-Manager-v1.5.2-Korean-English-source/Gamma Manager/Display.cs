using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Gamma_Manager
{
    internal class Display
    {
        #region Classes
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szPhysicalMonitorDescription;
        }

        public class DisplayInfo
        {
            public int numDisplay;
            public string displayName;
            public string displayLink;
            public WinApi.DisplayAdapterVendor adapterVendor = WinApi.DisplayAdapterVendor.Unknown;
            public bool isExternal;
            public IntPtr PhysicalHandle;
            public IntPtr MonitorHandle;
            public float rGamma = 1.0f;
            public float gGamma = 1.0f;
            public float bGamma = 1.0f;
            public float rContrast = 1.0f;
            public float gContrast = 1.0f;
            public float bContrast = 1.0f;
            public float rBright = 0.0f;
            public float gBright = 0.0f;
            public float bBright = 0.0f;
            public int saturation = 100;
            public int saturationDefault = 100;
            public int saturationMin = 0;
            public int saturationMax = 200;
            public int saturationStep = 1;
            public bool saturationSupported;
            public string monitorFriendlyName;
            public string monitorManufacturerName;
            public int monitorBrightness;
            public int monitorContrast;
        }
        #endregion

        #region DllImport
        [DllImport("dxva2.dll", EntryPoint = "GetNumberOfPhysicalMonitorsFromHMONITOR")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref uint pdwNumberOfPhysicalMonitors);

        [DllImport("dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        [DllImport("dxva2.dll", EntryPoint = "GetMonitorBrightness")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorBrightness(IntPtr handle, ref uint minimumBrightness, ref uint currentBrightness, ref uint maxBrightness);

        [DllImport("dxva2.dll", EntryPoint = "DestroyPhysicalMonitor")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

        [DllImport("dxva2.dll", EntryPoint = "DestroyPhysicalMonitors")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);
        #endregion

        private static bool IsHandleUsable(IntPtr handle)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                uint min = 0, cur = 0, max = 0;
                if (GetMonitorBrightness(handle, ref min, ref cur, ref max) && max > min)
                    return true;
                Thread.Sleep(10);
            }
            return false;
        }

        public static List<DisplayInfo> QueryDisplayDevices()
        {
            Saturation.ClearBindings(); // 디스플레이 새로 검색 시 이전 바인딩 초기화
            List<DisplayInfo> monitors = new List<DisplayInfo>();

            WinApi.MonitorEnumDelegate MonitorEnumProc = new WinApi.MonitorEnumDelegate(
            (IntPtr hMonitor, IntPtr hdcMonitor, ref WinApi.RECT lprcMonitor, IntPtr dwData) =>
            {
                WinApi.MONITORINFOEX monitorInfo = new WinApi.MONITORINFOEX() { Size = Marshal.SizeOf(typeof(WinApi.MONITORINFOEX)) };

                DisplayInfo monitor = new DisplayInfo();
                monitor.MonitorHandle = hMonitor;

                if (WinApi.GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    string cleanDeviceName = monitorInfo.DeviceName ?? string.Empty;
                    int nullIdx = cleanDeviceName.IndexOf('\0');
                    if (nullIdx >= 0) cleanDeviceName = cleanDeviceName.Substring(0, nullIdx);
                    cleanDeviceName = cleanDeviceName.Trim();

                    monitor.displayLink = cleanDeviceName;
                    monitor.adapterVendor = WinApi.GetDisplayAdapterVendor(cleanDeviceName);

                    WinApi.DISPLAY_DEVICE device = new WinApi.DISPLAY_DEVICE();
                    device.Initialize();

                    if (WinApi.EnumDisplayDevices(cleanDeviceName, 0, ref device, 0))
                    {
                        WinApi.DISPLAY_DEVICE monitorDevice = new WinApi.DISPLAY_DEVICE();
                        monitorDevice.Initialize();
                        if (WinApi.EnumDisplayDevices(cleanDeviceName, 1, ref monitorDevice, 0))
                        {
                            string devStr = monitorDevice.DeviceString ?? string.Empty;
                            nullIdx = devStr.IndexOf('\0');
                            if (nullIdx >= 0) devStr = devStr.Substring(0, nullIdx);
                            monitor.monitorFriendlyName = devStr.Trim();

                            string devId = monitorDevice.DeviceID ?? string.Empty;
                            nullIdx = devId.IndexOf('\0');
                            if (nullIdx >= 0) devId = devId.Substring(0, nullIdx);
                            monitor.monitorManufacturerName = devId.Trim();
                        }
                    }

                    string DName = device.DeviceID ?? string.Empty;
                    nullIdx = DName.IndexOf('\0');
                    if (nullIdx >= 0) DName = DName.Substring(0, nullIdx);
                    DName = DName.Trim();

                    if (!string.IsNullOrEmpty(DName))
                    {
                        string[] parts = DName.Split('\\');
                        if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                        {
                            DName = parts[1];
                        }
                    }

                    // 추출 실패 시 모니터 모델명(monitorFriendlyName) 또는 기본 장치명으로 안전하게 대체
                    if (string.IsNullOrWhiteSpace(DName))
                    {
                        DName = !string.IsNullOrWhiteSpace(monitor.monitorFriendlyName)
                            ? monitor.monitorFriendlyName
                            : cleanDeviceName;
                    }
                    monitor.displayName = DName;
                }

                uint physicalMonitorsCount = 0;

                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref physicalMonitorsCount))
                {
                    Logger.Warn("GetNumberOfPhysicalMonitorsFromHMONITOR failed for " + monitor.displayName + ". Win32Error=" + Marshal.GetLastWin32Error());
                    monitors.Add(monitor);
                    return true;
                }

                var physicalMonitors = new PHYSICAL_MONITOR[physicalMonitorsCount];
                if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, physicalMonitorsCount, physicalMonitors))
                {
                    Logger.Warn("GetPhysicalMonitorsFromHMONITOR failed for " + monitor.displayName + ". Win32Error=" + Marshal.GetLastWin32Error());
                    monitors.Add(monitor);
                    return true;
                }

                monitor.isExternal = false;
                monitor.PhysicalHandle = IntPtr.Zero;
                for (int physicalIndex = 0; physicalIndex < physicalMonitors.Length; physicalIndex++)
                {
                    PHYSICAL_MONITOR physicalMonitor = physicalMonitors[physicalIndex];
                    bool usable = IsHandleUsable(physicalMonitor.hPhysicalMonitor);

                    if (!usable)
                    {
                        DestroyPhysicalMonitor(physicalMonitor.hPhysicalMonitor);
                        continue;
                    }

                    if (!monitor.isExternal)
                    {
                        monitor.isExternal = true;
                        monitor.PhysicalHandle = physicalMonitor.hPhysicalMonitor;
                    }
                    else
                    {
                        DestroyPhysicalMonitor(physicalMonitor.hPhysicalMonitor);
                    }
                }

                if (monitor.isExternal)
                {
                    int brightness, contrast;
                    monitor.monitorBrightness = ExternalMonitor.TryGetBrightness(monitor.PhysicalHandle, out brightness) ? brightness : 50;
                    monitor.monitorContrast = ExternalMonitor.TryGetContrast(monitor.PhysicalHandle, out contrast) ? contrast : 50;
                    if (monitor.PhysicalHandle == IntPtr.Zero)
                        Logger.Warn("External monitor was detected but no usable physical handle was retained for " + monitor.displayName);
                }
                else
                {
                    int brightness;
                    monitor.monitorBrightness = InternalMonitor.TryGetBrightness(out brightness) ? brightness : 50;
                    monitor.monitorContrast = -1;
                }

                Saturation.Prepare(monitor);
                monitors.Add(monitor);
                return true;
            });

            WinApi.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumProc, IntPtr.Zero);
            return monitors;

        }

        public static bool RefreshPhysicalMonitorHandle(DisplayInfo monitor)
        {
            if (monitor == null || !monitor.isExternal || monitor.MonitorHandle == IntPtr.Zero)
                return false;

            uint count = 0;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.MonitorHandle, ref count) || count == 0)
            {
                Logger.Warn("RefreshPhysicalMonitorHandle: no physical monitors. Monitor=" + monitor.displayName);
                return false;
            }

            PHYSICAL_MONITOR[] physicalMonitors = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(monitor.MonitorHandle, count, physicalMonitors))
            {
                Logger.Warn("RefreshPhysicalMonitorHandle: GetPhysicalMonitorsFromHMONITOR failed.");
                return false;
            }

            IntPtr fresh = IntPtr.Zero;
            try
            {
                for (int i = 0; i < physicalMonitors.Length; i++)
                {
                    IntPtr candidate = physicalMonitors[i].hPhysicalMonitor;
                    bool usable = IsHandleUsable(candidate);

                    if (usable && fresh == IntPtr.Zero)
                    {
                        fresh = candidate;
                        continue;
                    }

                    if (candidate != IntPtr.Zero && candidate != (IntPtr)(-1))
                        DestroyPhysicalMonitor(candidate);
                }

                if (fresh == IntPtr.Zero)
                {
                    Logger.Warn("RefreshPhysicalMonitorHandle: no usable DDC/CI handle. Monitor=" + monitor.displayName);
                    return false;
                }

                IntPtr old = monitor.PhysicalHandle;
                monitor.PhysicalHandle = fresh;

                if (old != IntPtr.Zero && old != (IntPtr)(-1) && old != fresh)
                {
                    ExternalMonitor.RemoveLockForHandle(old); // 이전 핸들의 캐시 락 제거
                    try { DestroyPhysicalMonitor(old); }
                    catch (Exception ex) { Logger.Warn("Failed to destroy stale physical handle: " + ex.Message); }
                }

                Logger.Info("Refreshed DDC/CI handle. Monitor=" + monitor.displayName + ", Handle=" + fresh);
                return true;
            }
            catch (Exception ex)
            {
                if (fresh != IntPtr.Zero)
                {
                    try { DestroyPhysicalMonitor(fresh); } catch { }
                }
                Logger.Warn("RefreshPhysicalMonitorHandle exception: " + ex.Message);
                return false;
            }
        }

        public static void ReleasePhysicalMonitorHandles(IEnumerable<DisplayInfo> monitors)
        {
            if (monitors == null) return;
            HashSet<IntPtr> released = new HashSet<IntPtr>();
            foreach (DisplayInfo monitor in monitors)
            {
                if (monitor == null) continue;
                IntPtr handle = monitor.PhysicalHandle;
                if (handle == IntPtr.Zero || handle == (IntPtr)(-1) || released.Contains(handle)) continue;
                try
                {
                    if (!DestroyPhysicalMonitor(handle))
                        Logger.Warn("DestroyPhysicalMonitor failed for handle " + handle);
                }
                catch (Exception ex)
                {
                    Logger.Error("Exception while releasing physical monitor handle " + handle, ex);
                }
                released.Add(handle);
                monitor.PhysicalHandle = IntPtr.Zero;
            }
        }
    }
}