using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

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
            // Stable HMONITOR used to reacquire a fresh DDC/CI physical handle after
            // display sleep, fullscreen transitions, driver resets, or stale handles.
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

        public static List<DisplayInfo> QueryDisplayDevices()
        {
            List<DisplayInfo> monitors = new List<DisplayInfo>();

            WinApi.MonitorEnumDelegate MonitorEnumProc = new WinApi.MonitorEnumDelegate(
            (IntPtr hMonitor, IntPtr hdcMonitor, ref WinApi.RECT lprcMonitor, IntPtr dwData) =>
            {
                WinApi.MONITORINFOEX monitorInfo = new WinApi.MONITORINFOEX() { Size = Marshal.SizeOf(typeof(WinApi.MONITORINFOEX)) };

                DisplayInfo monitor = new DisplayInfo();
                monitor.MonitorHandle = hMonitor;
                if (WinApi.GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    WinApi.DISPLAY_DEVICE device = new WinApi.DISPLAY_DEVICE();
                    device.Initialize();
                    if (WinApi.EnumDisplayDevices(monitorInfo.DeviceName.ToLPTStr(), 0, ref device, 0))
                    {
                        monitor.displayLink = monitorInfo.DeviceName;
                        monitor.adapterVendor = WinApi.GetDisplayAdapterVendor(monitorInfo.DeviceName);

                        WinApi.DISPLAY_DEVICE monitorDevice = new WinApi.DISPLAY_DEVICE();
                        monitorDevice.Initialize();
                        if (WinApi.EnumDisplayDevices(monitorInfo.DeviceName.ToLPTStr(), 1, ref monitorDevice, 0))
                        {
                            monitor.monitorFriendlyName = monitorDevice.DeviceString;
                            monitor.monitorManufacturerName = monitorDevice.DeviceID;
                        }
                    }
                    string DName = device.DeviceID;
                    DName = DName.Substring(DName.IndexOf("\\") + 1);
                    DName = DName.Substring(0, DName.IndexOf("\\"));
                    monitor.displayName = DName;

                    /*Console.WriteLine("Left: " + lprcMonitor.Left);
                    Console.WriteLine("Right: " + lprcMonitor.Right);
                    Console.WriteLine("Top: " + lprcMonitor.Top);
                    Console.WriteLine("Bottom: " + lprcMonitor.Bottom);*/

                }
                for (int i = 0; i < monitors.Count; i++)
                {

                }

                uint physicalMonitorsCount = 0;

                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref physicalMonitorsCount))
                {
                    Logger.Warn("GetNumberOfPhysicalMonitorsFromHMONITOR failed for " + monitor.displayName + ". Win32Error=" + Marshal.GetLastWin32Error());
                    return true;
                }

                var physicalMonitors = new PHYSICAL_MONITOR[physicalMonitorsCount];
                if (!GetPhysicalMonitorsFromHMONITOR(hMonitor, physicalMonitorsCount, physicalMonitors))
                {
                    Logger.Warn("GetPhysicalMonitorsFromHMONITOR failed for " + monitor.displayName + ". Win32Error=" + Marshal.GetLastWin32Error());
                    return true;
                }

                // Keep one usable physical-monitor handle per Windows monitor.
                // The previous implementation overwrote the handle when an HMONITOR
                // exposed multiple physical monitors and leaked the unused handles.
                // We now select the first usable handle and immediately destroy the rest.
                monitor.isExternal = false;
                monitor.PhysicalHandle = IntPtr.Zero;
                for (int physicalIndex = 0; physicalIndex < physicalMonitors.Length; physicalIndex++)
                {
                    PHYSICAL_MONITOR physicalMonitor = physicalMonitors[physicalIndex];
                    uint minValue = 0, currentValue = 0, maxValue = 0;
                    bool usable = GetMonitorBrightness(physicalMonitor.hPhysicalMonitor, ref minValue, ref currentValue, ref maxValue)
                        && maxValue > minValue && currentValue >= minValue && currentValue <= maxValue;

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
                    // The DDC/CI read can occasionally fail during monitor initialization.
                    // Never turn a failed read into a real 0 value; use a neutral UI fallback.
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

                // Resolve the vendor-native saturation control for this specific display.
                // Unsupported GPUs keep the control disabled instead of applying a global color effect.
                Saturation.Prepare(monitor);
                monitors.Add(monitor);
                return true;
            });

            WinApi.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumProc, IntPtr.Zero);
            return monitors;
        }

        /// <summary>
        /// Reacquire the physical monitor handle for a display. DXVA2 physical
        /// monitor handles are not guaranteed to remain valid across monitor
        /// power-state changes, fullscreen transitions, GPU-driver resets, or
        /// display reconfiguration. The old implementation kept one handle for
        /// the entire process lifetime, which made profile/toggle DDC writes fail
        /// silently after such transitions.
        /// </summary>
        public static bool RefreshPhysicalMonitorHandle(DisplayInfo monitor)
        {
            if (monitor == null || !monitor.isExternal || monitor.MonitorHandle == IntPtr.Zero)
                return false;

            uint count = 0;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.MonitorHandle, ref count) || count == 0)
            {
                Logger.Warn("RefreshPhysicalMonitorHandle: no physical monitors. Monitor=" + monitor.displayName +
                    ", Win32Error=" + Marshal.GetLastWin32Error());
                return false;
            }

            PHYSICAL_MONITOR[] physicalMonitors = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(monitor.MonitorHandle, count, physicalMonitors))
            {
                Logger.Warn("RefreshPhysicalMonitorHandle: GetPhysicalMonitorsFromHMONITOR failed. Monitor=" +
                    monitor.displayName + ", Win32Error=" + Marshal.GetLastWin32Error());
                return false;
            }

            IntPtr fresh = IntPtr.Zero;
            try
            {
                for (int i = 0; i < physicalMonitors.Length; i++)
                {
                    IntPtr candidate = physicalMonitors[i].hPhysicalMonitor;
                    uint min = 0, cur = 0, max = 0;
                    bool usable = GetMonitorBrightness(candidate, ref min, ref cur, ref max) &&
                                  max > min && cur >= min && cur <= max;
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
                Logger.Warn("RefreshPhysicalMonitorHandle exception. Monitor=" + monitor.displayName + ": " + ex.Message);
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
                        Logger.Warn("DestroyPhysicalMonitor failed for handle " + handle + ". Win32Error=" + Marshal.GetLastWin32Error());
                }
                catch (Exception ex)
                {
                    Logger.Error("Exception while releasing physical monitor handle " + handle, ex);
                }
                released.Add(handle);
                monitor.PhysicalHandle = IntPtr.Zero;
            }
        }

        /*public static void DisposeMonitors(IEnumerable<DisplayInfo> monitors)
        {
            if (monitors?.Any() == true)
            {
                PHYSICAL_MONITOR[] monitorArray = monitors.Select(m => new PHYSICAL_MONITOR { hPhysicalMonitor = m.PhysicalHandle }).ToArray();
                DestroyPhysicalMonitors((uint)monitorArray.Length, monitorArray);
            }
        }*/

    }
}
