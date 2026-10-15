using System;
using System.Runtime.InteropServices;

namespace Gamma_Manager
{
    /// <summary>
    /// Windows DisplayConfig API를 활용하여 모니터별 HDR(Advanced Color) 활성화 상태를 감지하는 서비스
    /// </summary>
    internal static class HdrService
    {
        private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
        private const uint DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9;
        private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            public bool targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_MODE_INFO
        {
            public uint infoType;
            public uint id;
            public LUID adapterId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] modeDetails;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
        {
            public uint type;
            public uint size;
            public LUID adapterId;
            public uint id;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            public uint value; // bit 0: advancedColorSupported, bit 1: advancedColorEnabled
            public uint colorEncoding;
            public uint bitsPerColorChannel;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string viewGdiDeviceName;
        }

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(
            uint flags,
            ref uint numPathArrayElements,
            [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray,
            ref uint numModeInfoArrayElements,
            [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
            IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO requestPacket);

        [DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

        /// <summary>
        /// 지정된 displayLink(예: \\.\DISPLAY1) 모니터의 Windows HDR 활성화 여부를 조회합니다.
        /// </summary>
        public static bool IsHdrEnabled(string displayLink)
        {
            if (string.IsNullOrEmpty(displayLink)) return false;

            try
            {
                const int ERROR_INSUFFICIENT_BUFFER = 122;
                int err = 0;
                uint pathCount = 0;
                uint modeCount = 0;
                DISPLAYCONFIG_PATH_INFO[] paths = null;
                DISPLAYCONFIG_MODE_INFO[] modes = null;

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    err = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
                    if (err != 0 || pathCount == 0) return false;

                    paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                    modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

                    err = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
                    if (err == 0) break;
                    if (err != ERROR_INSUFFICIENT_BUFFER) return false;
                }
                if (err != 0 || paths == null) return false;

                for (int i = 0; i < pathCount; i++)
                {
                    // Source Name(GDI Device Name) 조회
                    var srcName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                    srcName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                    srcName.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_SOURCE_DEVICE_NAME));
                    srcName.header.adapterId = paths[i].sourceInfo.adapterId;
                    srcName.header.id = paths[i].sourceInfo.id;

                    if (DisplayConfigGetDeviceInfo(ref srcName) == 0)
                    {
                        if (string.Equals(srcName.viewGdiDeviceName, displayLink, StringComparison.OrdinalIgnoreCase))
                        {
                            // Target에 대한 Advanced Color(HDR) 정보 조회
                            var colorInfo = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
                            colorInfo.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
                            colorInfo.header.size = (uint)Marshal.SizeOf(typeof(DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
                            colorInfo.header.adapterId = paths[i].targetInfo.adapterId;
                            colorInfo.header.id = paths[i].targetInfo.id;

                            if (DisplayConfigGetDeviceInfo(ref colorInfo) == 0)
                            {
                                // bit 1: advancedColorEnabled
                                bool isHdrEnabled = (colorInfo.value & 0x2) != 0;
                                return isHdrEnabled;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("IsHdrEnabled check failed for " + displayLink + ": " + ex.Message);
            }

            return false;
        }
    }
}
