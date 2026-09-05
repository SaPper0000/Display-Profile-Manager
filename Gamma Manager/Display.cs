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
            public string displayName = string.Empty;
            // UI 표시명에 붙는 (#1)/(#2)와 무관한 원본 모델명. 저장/매칭에는 사용하지 않는다.
            public string baseDisplayName = string.Empty;
            public string hardwareId = string.Empty;
            public string displayLink = string.Empty;
            // Stable physical monitor identity. Prefer EDID serial; fall back to PNP ID.
            public string monitorKey = string.Empty;
            public string edidManufacturer = string.Empty;
            public string edidProductCode = string.Empty;
            public string edidSerial = string.Empty;
            public WinApi.DisplayAdapterVendor adapterVendor = WinApi.DisplayAdapterVendor.Unknown;
            public bool isExternal = true;
            public IntPtr PhysicalHandle = IntPtr.Zero;
            public IntPtr MonitorHandle = IntPtr.Zero;
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
            public int monitorBrightness = 50;
            public int monitorContrast = 50;
            public int shadowBoost = 0;
            public int shadowBoostMode = 0;
        }
        #endregion

        #region DllImport
        [DllImport("dxva2.dll", EntryPoint = "GetNumberOfPhysicalMonitorsFromHMONITOR", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, ref uint pdwNumberOfPhysicalMonitors);

        [DllImport("dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

        [DllImport("dxva2.dll", EntryPoint = "GetMonitorBrightness", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorBrightness(IntPtr handle, ref uint minimumBrightness, ref uint currentBrightness, ref uint maxBrightness);

        [DllImport("dxva2.dll", EntryPoint = "DestroyPhysicalMonitor", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

        [DllImport("dxva2.dll", EntryPoint = "DestroyPhysicalMonitors", SetLastError = true)]
        public static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);
        #endregion

        public static List<DisplayInfo> QueryDisplayDevices()
        {
            InternalMonitor.Cleanup();
            Saturation.ClearBindings();
            List<DisplayInfo> monitors = new List<DisplayInfo>();

            // 1단계: 디스플레이 어댑터 및 HMONITOR 목록 수집
            WinApi.MonitorEnumDelegate MonitorEnumProc = new WinApi.MonitorEnumDelegate(
            (IntPtr hMonitor, IntPtr hdcMonitor, ref WinApi.RECT lprcMonitor, IntPtr dwData) =>
            {
                try
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
                            for (uint devNum = 0; ; devNum++)
                            {
                                WinApi.DISPLAY_DEVICE monitorDevice = new WinApi.DISPLAY_DEVICE();
                                monitorDevice.Initialize();
                                if (!WinApi.EnumDisplayDevices(cleanDeviceName, devNum, ref monitorDevice, 0))
                                    break;

                                string devStr = monitorDevice.DeviceString ?? string.Empty;
                                nullIdx = devStr.IndexOf('\0');
                                if (nullIdx >= 0) devStr = devStr.Substring(0, nullIdx);
                                devStr = devStr.Trim();

                                if (!string.IsNullOrEmpty(devStr))
                                {
                                    monitor.monitorFriendlyName = devStr;

                                    string devId = monitorDevice.DeviceID ?? string.Empty;
                                    nullIdx = devId.IndexOf('\0');
                                    if (nullIdx >= 0) devId = devId.Substring(0, nullIdx);
                                    monitor.monitorManufacturerName = devId.Trim();

                                    if (!string.IsNullOrEmpty(devId))
                                    {
                                        monitor.hardwareId = devId.Trim();
                                    }
                                    break;
                                }
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

                        if (string.IsNullOrWhiteSpace(DName))
                        {
                            DName = !string.IsNullOrWhiteSpace(monitor.monitorFriendlyName)
                                ? monitor.monitorFriendlyName
                                : cleanDeviceName;
                        }
                        monitor.baseDisplayName = DName;
                        monitor.displayName = DName;

                        if (string.IsNullOrEmpty(monitor.hardwareId))
                        {
                            monitor.hardwareId = !string.IsNullOrEmpty(device.DeviceID) ? device.DeviceID.Trim() : monitor.displayName;
                        }

                        MonitorIdentity.IdentityInfo identity = MonitorIdentity.Read(monitor.hardwareId);
                        monitor.edidManufacturer = identity.Manufacturer;
                        monitor.edidProductCode = identity.ProductCode;
                        monitor.edidSerial = identity.Serial;
                        monitor.monitorKey = MonitorIdentity.BuildBaseKey(identity, monitor.displayLink);
                    }

                    monitors.Add(monitor);
                }
                catch (Exception ex)
                {
                    Logger.Error("Error enumerating individual monitor device.", ex);
                }

                return true;
            });

            WinApi.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumProc, IntPtr.Zero);

            // 2단계: 모니터별 DDC/CI 물리 핸들 순차 조회 (GPU I2C 버스 안정화 간격 적용)
            foreach (var monitor in monitors)
            {
                if (monitor == null || monitor.MonitorHandle == IntPtr.Zero) continue;

                Thread.Sleep(60); // 모니터 간 I2C 버스 해제 대기

                uint physicalMonitorsCount = 0;
                monitor.isExternal = false;
                monitor.PhysicalHandle = IntPtr.Zero;

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    if (GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.MonitorHandle, ref physicalMonitorsCount) && physicalMonitorsCount > 0)
                        break;
                    Thread.Sleep(40);
                }

                if (physicalMonitorsCount > 0)
                {
                    var physicalMonitors = new PHYSICAL_MONITOR[physicalMonitorsCount];
                    if (GetPhysicalMonitorsFromHMONITOR(monitor.MonitorHandle, physicalMonitorsCount, physicalMonitors))
                    {
                        for (int physicalIndex = 0; physicalIndex < physicalMonitors.Length; physicalIndex++)
                        {
                            PHYSICAL_MONITOR physicalMonitor = physicalMonitors[physicalIndex];
                            IntPtr candidate = physicalMonitor.hPhysicalMonitor;

                            if (candidate == IntPtr.Zero || candidate == (IntPtr)(-1))
                                continue;

                            if (!monitor.isExternal)
                            {
                                monitor.isExternal = true;
                                monitor.PhysicalHandle = candidate;
                            }
                            else
                            {
                                DestroyPhysicalMonitor(candidate);
                            }
                        }
                    }
                    else
                    {
                        Logger.Warn("GetPhysicalMonitorsFromHMONITOR failed for " + monitor.displayName);
                    }
                }

                if (monitor.isExternal && monitor.PhysicalHandle != IntPtr.Zero)
                {
                    int brightness, contrast;
                    monitor.monitorBrightness = ExternalMonitor.TryGetBrightness(monitor.PhysicalHandle, out brightness) ? brightness : 50;
                    Thread.Sleep(30); // 밝기/대비 조회 사이 I2C 간격 확보
                    monitor.monitorContrast = ExternalMonitor.TryGetContrast(monitor.PhysicalHandle, out contrast) ? contrast : 50;
                }
                else
                {
                    // DDC/CI 지연 시 WMI 조회 시도 (노트북 내장 패널 판별)
                    int brightness;
                    if (InternalMonitor.TryGetBrightness(monitor.hardwareId, out brightness))
                    {
                        monitor.isExternal = false;
                        monitor.monitorBrightness = brightness;
                        monitor.monitorContrast = -1;
                    }
                    else
                    {
                        // 데스크톱 외장 모니터 기본 상태 유지 (대비 UI 유지)
                        monitor.isExternal = true;
                        monitor.monitorBrightness = 50;
                        monitor.monitorContrast = 50;
                    }
                }

                Saturation.Prepare(monitor);
            }

            // 같은 모델/동일 PNP ID가 여러 개일 경우에도 런타임 MonitorKey 충돌 방지
            MonitorIdentity.EnsureUniqueKeys(monitors);

            return monitors;
        }

        public static bool RefreshPhysicalMonitorHandle(DisplayInfo monitor)
        {
            if (monitor == null || monitor.MonitorHandle == IntPtr.Zero)
                return false;

            lock (monitor)
            {
                uint count = 0;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    if (GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.MonitorHandle, ref count) && count > 0)
                        break;
                    Thread.Sleep(40);
                }

                if (count == 0)
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
                        if (candidate == IntPtr.Zero || candidate == (IntPtr)(-1)) continue;

                        if (fresh == IntPtr.Zero)
                        {
                            fresh = candidate;
                            continue;
                        }

                        DestroyPhysicalMonitor(candidate);
                    }

                    if (fresh == IntPtr.Zero)
                    {
                        Logger.Warn("RefreshPhysicalMonitorHandle: no usable DDC/CI handle. Monitor=" + monitor.displayName);
                        return false;
                    }

                    IntPtr old = monitor.PhysicalHandle;
                    monitor.PhysicalHandle = fresh;
                    monitor.isExternal = true;

                    if (old != IntPtr.Zero && old != (IntPtr)(-1) && old != fresh)
                    {
                        lock (ExternalMonitor.GetLockForHandle(old))
                        {
                            try { DestroyPhysicalMonitor(old); }
                            catch (Exception ex) { Logger.Warn("Failed to destroy stale physical handle: " + ex.Message); }
                        }
                        ExternalMonitor.RemoveLockForHandle(old);
                    }

                    Logger.Info("Refreshed DDC/CI handle. Monitor=" + monitor.displayName + ", Handle=" + fresh);
                    return true;
                }
                catch (Exception ex)
                {
                    for (int i = 0; i < physicalMonitors.Length; i++)
                    {
                        IntPtr h = physicalMonitors[i].hPhysicalMonitor;
                        if (h != IntPtr.Zero && h != (IntPtr)(-1) && h != monitor.PhysicalHandle)
                        {
                            try { DestroyPhysicalMonitor(h); } catch { }
                        }
                    }

                    Logger.Warn("RefreshPhysicalMonitorHandle exception: " + ex.Message);
                    return false;
                }
            }
        }

        public static void ReleasePhysicalMonitorHandles(IEnumerable<DisplayInfo> monitors)
        {
            if (monitors == null) return;
            HashSet<IntPtr> released = new HashSet<IntPtr>();

            foreach (DisplayInfo monitor in monitors)
            {
                if (monitor == null) continue;

                lock (monitor)
                {
                    IntPtr handle = monitor.PhysicalHandle;
                    if (handle == IntPtr.Zero || handle == (IntPtr)(-1)) continue;

                    if (released.Contains(handle))
                    {
                        monitor.PhysicalHandle = IntPtr.Zero;
                        continue;
                    }

                    try
                    {
                        lock (ExternalMonitor.GetLockForHandle(handle))
                        {
                            if (!DestroyPhysicalMonitor(handle))
                                Logger.Warn("DestroyPhysicalMonitor failed for handle " + handle);
                        }

                        ExternalMonitor.RemoveLockForHandle(handle);
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
}