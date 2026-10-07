using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Gamma_Manager
{
    /// <summary>
    /// Windows Core Audio API를 활용한 마스터 볼륨 및 프로세스별 오디오 세션 볼륨 컨트롤러
    /// </summary>
    internal static class AudioManager
    {
        #region Win32 API for Active Window

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public static int GetForegroundProcessId()
        {
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd == IntPtr.Zero) return -1;
                GetWindowThreadProcessId(hWnd, out uint pid);
                return (int)pid;
            }
            catch
            {
                return -1;
            }
        }

        public static string GetProcessNameById(int pid)
        {
            if (pid <= 0) return string.Empty;

            // 1. 최소 권한(PROCESS_QUERY_LIMITED_INFORMATION)으로 안전하게 이미지 경로 조회
            // (BattlEye, EasyAntiCheat 등 안티치트 적용 게임이나 관리자 권한 프로세스에서도 Access Denied 없이 안정적 동작)
            try
            {
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        uint capacity = 1024;
                        var sb = new System.Text.StringBuilder((int)capacity);
                        if (QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                        {
                            string fileName = System.IO.Path.GetFileNameWithoutExtension(sb.ToString());
                            if (!string.IsNullOrEmpty(fileName))
                                return fileName;
                        }
                    }
                    finally
                    {
                        CloseHandle(hProcess);
                    }
                }
            }
            catch { }

            // 2. Fallback: .NET Process API
            try
            {
                using (Process proc = Process.GetProcessById(pid))
                {
                    return proc.ProcessName;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion

        #region COM Interfaces & Constants

        private const int CLSCTX_ALL = 23;
        private const int eRender = 0;
        private const int eMultimedia = 1;

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig]
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
            int GetDevice(string pwstrId, out IMMDevice device);
            int RegisterEndpointNotificationCallback(IntPtr pClient);
            int UnregisterEndpointNotificationCallback(IntPtr pClient);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate([In] ref Guid id, [In] int clsCtx, [In] IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object interfacePointer);
            int OpenPropertyStore(int stgmAccess, out IntPtr properties);
            int GetId(out IntPtr strId);
            int GetState(out int state);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out uint pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            [PreserveSig]
            int SetMasterVolumeLevelScalar(float fLevel, [In] ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            [PreserveSig]
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            [PreserveSig]
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
            [PreserveSig]
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
            int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
            int VolumeStepUp(ref Guid pguidEventContext);
            int VolumeStepDown(ref Guid pguidEventContext);
            int QueryHardwareSupport(out uint pdwHardwareSupportMask);
            int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
        }

        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionManager2
        {
            int GetAudioSessionControl(ref Guid AudioSessionGuid, uint StreamFlags, out IntPtr SessionControl);
            int GetSimpleAudioVolume(ref Guid AudioSessionGuid, uint StreamFlags, out ISimpleAudioVolume AudioVolume);
            [PreserveSig]
            int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
            int RegisterSessionNotification(IntPtr NewSessionNotification);
            int UnregisterSessionNotification(IntPtr NewSessionNotification);
            int RegisterDuckNotification(string sessionID, IntPtr duckNotification);
            int UnregisterDuckNotification(IntPtr duckNotification);
        }

        [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionEnumerator
        {
            [PreserveSig]
            int GetCount(out int SessionCount);
            [PreserveSig]
            int GetSession(int SessionIndex, out IAudioSessionControl Session);
        }

        [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl
        {
            int GetState(out int pRetVal);
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
            int GetGroupingParam(out Guid pRetVal);
            int SetGroupingParam(ref Guid Override, ref Guid EventContext);
            int RegisterAudioSessionNotification(IntPtr NewNotifications);
            int UnregisterAudioSessionNotification(IntPtr NewNotifications);
        }

        [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioSessionControl2
        {
            int GetState(out int pRetVal);
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
            int GetGroupingParam(out Guid pRetVal);
            int SetGroupingParam(ref Guid Override, ref Guid EventContext);
            int RegisterAudioSessionNotification(IntPtr NewNotifications);
            int UnregisterAudioSessionNotification(IntPtr NewNotifications);

            int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
            [PreserveSig]
            int GetProcessId(out uint pRetVal);
            [PreserveSig]
            int IsSystemSoundsSession();
            int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }

        [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ISimpleAudioVolume
        {
            [PreserveSig]
            int SetMasterVolume(float fLevel, [In] ref Guid EventContext);
            [PreserveSig]
            int GetMasterVolume(out float pfLevel);
            [PreserveSig]
            int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, [In] ref Guid EventContext);
            [PreserveSig]
            int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        }

        private static readonly Guid IID_IAudioEndpointVolume = typeof(IAudioEndpointVolume).GUID;
        private static readonly Guid IID_IAudioSessionManager = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

        #endregion

        #region Master Volume Methods

        private static IAudioEndpointVolume GetMasterVolumeEndpoint(out IMMDeviceEnumerator enumerator, out IMMDevice device)
        {
            enumerator = null;
            device = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                int hr = enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out device);
                if (hr != 0 || device == null) return null;

                Guid guid = IID_IAudioEndpointVolume;
                hr = device.Activate(ref guid, CLSCTX_ALL, IntPtr.Zero, out object epvObj);
                if (hr != 0 || epvObj == null) return null;

                return (IAudioEndpointVolume)epvObj;
            }
            catch (Exception ex)
            {
                Logger.Warn("GetMasterVolumeEndpoint error: " + ex.Message);
                return null;
            }
        }

        private static void SafeRelease(object comObject)
        {
            if (comObject != null)
            {
                try
                {
                    Marshal.ReleaseComObject(comObject);
                }
                catch { }
            }
        }

        public static int GetMasterVolume()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioEndpointVolume epv = null;
            try
            {
                epv = GetMasterVolumeEndpoint(out enumerator, out device);
                if (epv == null) return -1;

                int hr = epv.GetMasterVolumeLevelScalar(out float level);
                if (hr == 0)
                {
                    return (int)Math.Round(level * 100f);
                }
                return -1;
            }
            catch (Exception ex)
            {
                Logger.Warn("AudioManager.GetMasterVolume failed: " + ex.Message);
                return -1;
            }
            finally
            {
                SafeRelease(epv);
                SafeRelease(device);
                SafeRelease(enumerator);
            }
        }

        public static bool SetMasterVolume(int level)
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioEndpointVolume epv = null;
            try
            {
                epv = GetMasterVolumeEndpoint(out enumerator, out device);
                if (epv == null) return false;

                float scalar = Math.Max(0f, Math.Min(100f, level)) / 100f;
                Guid ctx = Guid.Empty;
                int hr = epv.SetMasterVolumeLevelScalar(scalar, ref ctx);
                return hr == 0;
            }
            catch (Exception ex)
            {
                Logger.Warn("AudioManager.SetMasterVolume failed: " + ex.Message);
                return false;
            }
            finally
            {
                SafeRelease(epv);
                SafeRelease(device);
                SafeRelease(enumerator);
            }
        }

        public static bool IsMuted()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioEndpointVolume epv = null;
            try
            {
                epv = GetMasterVolumeEndpoint(out enumerator, out device);
                if (epv == null) return false;

                int hr = epv.GetMute(out bool muted);
                return hr == 0 && muted;
            }
            catch
            {
                return false;
            }
            finally
            {
                SafeRelease(epv);
                SafeRelease(device);
                SafeRelease(enumerator);
            }
        }

        public static bool SetMute(bool mute)
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioEndpointVolume epv = null;
            try
            {
                epv = GetMasterVolumeEndpoint(out enumerator, out device);
                if (epv == null) return false;

                Guid ctx = Guid.Empty;
                int hr = epv.SetMute(mute, ref ctx);
                return hr == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                SafeRelease(epv);
                SafeRelease(device);
                SafeRelease(enumerator);
            }
        }

        #endregion

        #region Per-Process Audio Session Volume Methods

        private static IAudioSessionManager2 GetAudioSessionManager(out IMMDeviceEnumerator enumerator, out IMMDevice device)
        {
            enumerator = null;
            device = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                int hr = enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out device);
                if (hr != 0 || device == null) return null;

                Guid guid = IID_IAudioSessionManager;
                hr = device.Activate(ref guid, CLSCTX_ALL, IntPtr.Zero, out object sessionMgrObj);
                if (hr != 0 || sessionMgrObj == null) return null;

                return (IAudioSessionManager2)sessionMgrObj;
            }
            catch (Exception ex)
            {
                Logger.Warn("GetAudioSessionManager error: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 특정 프로세스(PID)의 오디오 볼륨(0~100)을 가져옵니다. 세션이 없으면 -1 반환.
        /// </summary>
        public static int GetProcessVolume(int targetPid)
        {
            if (targetPid <= 0) return -1;

            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioSessionManager2 mgr = null;
            IAudioSessionEnumerator sessionEnum = null;
            try
            {
                mgr = GetAudioSessionManager(out enumerator, out device);
                if (mgr == null) return -1;

                if (mgr.GetSessionEnumerator(out sessionEnum) != 0 || sessionEnum == null) return -1;
                if (sessionEnum.GetCount(out int count) != 0) return -1;

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl sessionControl = null;
                    try
                    {
                        if (sessionEnum.GetSession(i, out sessionControl) == 0 && sessionControl != null)
                        {
                            if (sessionControl is IAudioSessionControl2 ctl2)
                            {
                                if (ctl2.GetProcessId(out uint pid) == 0 && pid == (uint)targetPid)
                                {
                                    if (sessionControl is ISimpleAudioVolume simpleVol)
                                    {
                                        if (simpleVol.GetMasterVolume(out float level) == 0)
                                        {
                                            return (int)Math.Round(level * 100f);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        SafeRelease(sessionControl);
                    }
                }
                return -1;
            }
            catch (Exception ex)
            {
                Logger.Warn($"GetProcessVolume(PID:{targetPid}) error: " + ex.Message);
                return -1;
            }
            finally
            {
                SafeRelease(sessionEnum);
                SafeRelease(mgr);
                SafeRelease(device);
                SafeRelease(enumerator);
            }
        }

        /// <summary>
        /// 특정 프로세스(PID)의 오디오 볼륨(0~100)을 설정합니다.
        /// </summary>
        public static bool SetProcessVolume(int targetPid, int level)
        {
            if (targetPid <= 0) return false;

            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioSessionManager2 mgr = null;
            IAudioSessionEnumerator sessionEnum = null;
            bool success = false;
            try
            {
                mgr = GetAudioSessionManager(out enumerator, out device);
                if (mgr == null) return false;

                if (mgr.GetSessionEnumerator(out sessionEnum) != 0 || sessionEnum == null) return false;
                if (sessionEnum.GetCount(out int count) != 0) return false;

                float scalar = Math.Max(0f, Math.Min(100f, level)) / 100f;
                Guid ctx = Guid.Empty;

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl sessionControl = null;
                    try
                    {
                        if (sessionEnum.GetSession(i, out sessionControl) == 0 && sessionControl != null)
                        {
                            if (sessionControl is IAudioSessionControl2 ctl2)
                            {
                                if (ctl2.GetProcessId(out uint pid) == 0 && pid == (uint)targetPid)
                                {
                                    if (sessionControl is ISimpleAudioVolume simpleVol)
                                    {
                                        if (simpleVol.SetMasterVolume(scalar, ref ctx) == 0)
                                        {
                                            success = true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        SafeRelease(sessionControl);
                    }
                }
                return success;
            }
            catch (Exception ex)
            {
                Logger.Warn($"SetProcessVolume(PID:{targetPid}) error: " + ex.Message);
                return false;
            }
            finally
            {
                SafeRelease(sessionEnum);
                SafeRelease(mgr);
                SafeRelease(device);
                SafeRelease(enumerator);
            }
        }

        /// <summary>
        /// 특정 프로세스 이름(예: "EscapeFromTarkov", "Discord")의 볼륨을 설정합니다. (동일 이름의 모든 인스턴스에 적용)
        /// </summary>
        public static bool SetProcessVolumeByName(string processName, int level)
        {
            if (string.IsNullOrWhiteSpace(processName)) return false;

            string cleanName = processName.Trim();
            if (cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleanName = cleanName.Substring(0, cleanName.Length - 4);

            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioSessionManager2 mgr = null;
            IAudioSessionEnumerator sessionEnum = null;
            bool success = false;
            try
            {
                mgr = GetAudioSessionManager(out enumerator, out device);
                if (mgr == null) return false;

                if (mgr.GetSessionEnumerator(out sessionEnum) != 0 || sessionEnum == null) return false;
                if (sessionEnum.GetCount(out int count) != 0) return false;

                float scalar = Math.Max(0f, Math.Min(100f, level)) / 100f;
                Guid ctx = Guid.Empty;

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl sessionControl = null;
                    try
                    {
                        if (sessionEnum.GetSession(i, out sessionControl) == 0 && sessionControl != null)
                        {
                            if (sessionControl is IAudioSessionControl2 ctl2)
                            {
                                if (ctl2.GetProcessId(out uint pid) == 0 && pid > 0)
                                {
                                    string pName = GetProcessNameById((int)pid);
                                    if (string.Equals(pName, cleanName, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(pName, processName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (sessionControl is ISimpleAudioVolume simpleVol)
                                        {
                                            if (simpleVol.SetMasterVolume(scalar, ref ctx) == 0)
                                            {
                                                success = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        SafeRelease(sessionControl);
                    }
                }
                return success;
            }
            catch (Exception ex)
            {
                Logger.Warn($"SetProcessVolumeByName({processName}) error: " + ex.Message);
                return false;
            }
            finally
            {
                SafeRelease(sessionEnum);
                SafeRelease(mgr);
                SafeRelease(device);
                SafeRelease(enumerator);
            }
        }

        /// <summary>
        /// 특정 프로세스 이름의 현재 볼륨을 가져옵니다.
        /// </summary>
        public static int GetProcessVolumeByName(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return -1;

            string cleanName = processName.Trim();
            if (cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                cleanName = cleanName.Substring(0, cleanName.Length - 4);

            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioSessionManager2 mgr = null;
            IAudioSessionEnumerator sessionEnum = null;
            try
            {
                mgr = GetAudioSessionManager(out enumerator, out device);
                if (mgr == null) return -1;

                if (mgr.GetSessionEnumerator(out sessionEnum) != 0 || sessionEnum == null) return -1;
                if (sessionEnum.GetCount(out int count) != 0) return -1;

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl sessionControl = null;
                    try
                    {
                        if (sessionEnum.GetSession(i, out sessionControl) == 0 && sessionControl != null)
                        {
                            if (sessionControl is IAudioSessionControl2 ctl2)
                            {
                                if (ctl2.GetProcessId(out uint pid) == 0 && pid > 0)
                                {
                                    string pName = GetProcessNameById((int)pid);
                                    if (string.Equals(pName, cleanName, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(pName, processName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (sessionControl is ISimpleAudioVolume simpleVol)
                                        {
                                            if (simpleVol.GetMasterVolume(out float level) == 0)
                                            {
                                                return (int)Math.Round(level * 100f);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        SafeRelease(sessionControl);
                    }
                }
                return -1;
            }
            catch
            {
                return -1;
            }
            finally
            {
                SafeRelease(sessionEnum);
                SafeRelease(mgr);
                SafeRelease(device);
                SafeRelease(enumerator);
            }
        }

        /// <summary>
        /// 현재 오디오 세션을 가지고 있는 실행 중인 프로세스 이름 목록을 반환합니다.
        /// </summary>
        public static List<string> GetActiveAudioProcessNames()
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            IAudioSessionManager2 mgr = null;
            IAudioSessionEnumerator sessionEnum = null;
            try
            {
                mgr = GetAudioSessionManager(out enumerator, out device);
                if (mgr == null) return result;

                if (mgr.GetSessionEnumerator(out sessionEnum) != 0 || sessionEnum == null) return result;
                if (sessionEnum.GetCount(out int count) != 0) return result;

                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl sessionControl = null;
                    try
                    {
                        if (sessionEnum.GetSession(i, out sessionControl) == 0 && sessionControl != null)
                        {
                            if (sessionControl is IAudioSessionControl2 ctl2)
                            {
                                if (ctl2.GetProcessId(out uint pid) == 0 && pid > 0)
                                {
                                    string pName = GetProcessNameById((int)pid);
                                    if (!string.IsNullOrEmpty(pName) && !seen.Contains(pName))
                                    {
                                        seen.Add(pName);
                                        result.Add(pName);
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        SafeRelease(sessionControl);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("GetActiveAudioProcessNames error: " + ex.Message);
            }
            finally
            {
                SafeRelease(sessionEnum);
                SafeRelease(mgr);
                SafeRelease(device);
                SafeRelease(enumerator);
            }

            result.Sort();
            return result;
        }

        #endregion
    }
}
