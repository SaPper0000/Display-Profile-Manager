using System;
using System.Runtime.InteropServices;

namespace Gamma_Manager
{
    internal static class WinApi
    {
        #region DISPLAY_DEVICE struct
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public DisplayDeviceStateFlags StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;

            public void Initialize()
            {
                cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                DeviceName = string.Empty;
                DeviceString = string.Empty;
                DeviceID = string.Empty;
                DeviceKey = string.Empty;
            }
        }
        #endregion

        #region RECT struct
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
        #endregion

        #region MONITORINFOEX struct
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int Size;
            public RECT Monitor;
            public RECT WorkArea;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }
        #endregion

        #region DisplayDeviceStateFlags enum
        [Flags()]
        public enum DisplayDeviceStateFlags : int
        {
            AttachedToDesktop = 0x1,
            MultiDriver = 0x2,
            PrimaryDevice = 0x4,
            MirroringDriver = 0x8,
            VGACompatible = 0x10,
            Removable = 0x20,
            ModesPruned = 0x8000000,
            Remote = 0x4000000,
            Disconnect = 0x2000000,
        }
        #endregion


        public delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        // EnumDisplayDevicesW 유니코드 전용 API
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
        internal static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, int dwFlags);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        internal const int OBJID_WINDOW = 0x00000000;

        internal delegate void WinEventDelegate(
            IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint idEventThread, uint msEventTime);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWinEventHook(
            uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        public enum DisplayAdapterVendor
        {
            Unknown,
            Nvidia,
            Amd
        }

        public static DisplayAdapterVendor GetDisplayAdapterVendor(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return DisplayAdapterVendor.Unknown;

            string queryName = displayName;
            int nullIdx = queryName.IndexOf('\0');
            if (nullIdx >= 0) queryName = queryName.Substring(0, nullIdx);
            queryName = queryName.Trim();

            for (uint i = 0; i < 32; i++)
            {
                DISPLAY_DEVICE device = new DISPLAY_DEVICE();
                device.Initialize();
                if (!EnumDisplayDevices(null, i, ref device, 0)) break;

                string devName = device.DeviceName ?? string.Empty;
                nullIdx = devName.IndexOf('\0');
                if (nullIdx >= 0) devName = devName.Substring(0, nullIdx);
                devName = devName.Trim();

                if (!string.Equals(devName, queryName, StringComparison.OrdinalIgnoreCase)) continue;

                string id = (device.DeviceID ?? string.Empty).ToUpperInvariant();
                if (id.Contains("VEN_10DE")) return DisplayAdapterVendor.Nvidia;
                if (id.Contains("VEN_1002") || id.Contains("ATI")) return DisplayAdapterVendor.Amd;

                string devStr = (device.DeviceString ?? string.Empty).ToUpperInvariant();
                string text = devStr + " " + id;
                if (text.Contains("NVIDIA")) return DisplayAdapterVendor.Nvidia;
                if (text.Contains("AMD") || text.Contains("RADEON") || text.Contains("ATI") || text.Contains("ADVANCED MICRO")) return DisplayAdapterVendor.Amd;
            }
            return DisplayAdapterVendor.Unknown;
        }
    }
}