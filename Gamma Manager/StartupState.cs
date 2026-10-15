using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Gamma_Manager
{
    [DataContract]
    internal sealed class StartupDisplayState
    {
        [DataMember] public string DisplayLink;
        [DataMember] public string MonitorKey;
        [DataMember] public string HardwareId;
        [DataMember] public bool IsExternal;
        [DataMember] public int MonitorBrightness;
        [DataMember] public int MonitorContrast;
        [DataMember] public int Saturation = -1; // 시작 시점 원본 채도 보관 (-1: 구버전 미기록, 0 이상: 유효 수치)
        [DataMember] public ushort[] GammaRamp;

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            Saturation = -1;
        }
    }

    [DataContract]
    internal sealed class StartupState
    {
        [DataMember] public List<StartupDisplayState> Displays = new List<StartupDisplayState>();
    }

    internal static class StartupStateManager
    {
        private static readonly object _stateLock = new object();
        private static StartupState _cachedState = null;

        private static string BackupPath
        {
            get { return AppPaths.StateFile("GammaManager.StartupBackup.json"); }
        }

        private static ushort[] Flatten(ushort[,] ramp)
        {
            if (ramp == null) return null;
            ushort[] data = new ushort[768];
            int n = 0;
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < 256; i++)
                    data[n++] = ramp[c, i];
            return data;
        }

        private static ushort[,] Expand(ushort[] data)
        {
            if (data == null || data.Length != 768) return null;
            ushort[,] ramp = new ushort[3, 256];
            int n = 0;
            for (int c = 0; c < 3; c++)
                for (int i = 0; i < 256; i++)
                    ramp[c, i] = data[n++];
            return ramp;
        }

        private static Display.DisplayInfo FindMatchingDisplay(List<Display.DisplayInfo> displays, StartupDisplayState saved)
        {
            if (displays == null || saved == null) return null;

            // 1순위: 안정 식별자(MonitorKey) 매칭 (포트/링크 번호 변경에도 안전)
            if (!string.IsNullOrEmpty(saved.MonitorKey))
            {
                foreach (Display.DisplayInfo d in displays)
                {
                    if (d != null && string.Equals(DisplayService.GetMonitorKey(d), saved.MonitorKey, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
            }

            // 2순위: DisplayLink 매칭
            foreach (Display.DisplayInfo d in displays)
            {
                if (d != null && string.Equals(d.displayLink, saved.DisplayLink, StringComparison.OrdinalIgnoreCase))
                    return d;
            }

            return null;
        }

        private static StartupDisplayState FindMatchingSavedState(StartupState state, Display.DisplayInfo display)
        {
            if (state == null || state.Displays == null || display == null) return null;

            string monitorKey = DisplayService.GetMonitorKey(display);
            if (!string.IsNullOrEmpty(monitorKey))
            {
                foreach (StartupDisplayState saved in state.Displays)
                {
                    if (saved != null && !string.IsNullOrEmpty(saved.MonitorKey) &&
                        string.Equals(saved.MonitorKey, monitorKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return saved;
                    }
                }
            }

            foreach (StartupDisplayState saved in state.Displays)
            {
                if (saved != null && string.Equals(display.displayLink, saved.DisplayLink, StringComparison.OrdinalIgnoreCase))
                    return saved;
            }

            return null;
        }

        private static int ResolveOriginalSaturation(StartupDisplayState saved, Display.DisplayInfo display)
        {
            if (saved != null && saved.Saturation >= 0)
                return saved.Saturation;
            if (display != null && display.saturationDefault >= 0)
                return display.saturationDefault;
            return 100;
        }

        public static bool HasPendingBackup()
        {
            return File.Exists(BackupPath);
        }

        public static bool RestorePending(List<Display.DisplayInfo> displays)
        {
            if (!File.Exists(BackupPath)) return false;
            try
            {
                StartupState state;
                using (FileStream fs = File.OpenRead(BackupPath))
                {
                    state = (StartupState)new DataContractJsonSerializer(typeof(StartupState)).ReadObject(fs);
                }
                if (state == null || state.Displays == null || state.Displays.Count == 0) return false;

                bool allRestoredSuccessfully = true;
                bool matchedAny = false;

                foreach (StartupDisplayState saved in state.Displays)
                {
                    Display.DisplayInfo display = FindMatchingDisplay(displays, saved);
                    if (display == null)
                    {
                        allRestoredSuccessfully = false;
                        continue;
                    }
                    matchedAny = true;
                    bool gammaOk = false;
                    bool hardwareOk = false;

                    // 1. GPU 감마 복원
                    ushort[,] ramp = Expand(saved.GammaRamp);
                    if (ramp != null)
                        gammaOk = Gamma.SetRawGammaRamp(display.displayLink, ramp);
                    else
                        gammaOk = Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f));

                    // 2. GPU 채도(Digital Vibrance/ADL) 강제 준비 및 복원 (0% 흑백 채도도 정상 복원)
                    int origSat = ResolveOriginalSaturation(saved, display);

                    Saturation.Prepare(display);
                    if (display.saturationSupported)
                    {
                        display.saturation = origSat;
                        display.saturationDefault = origSat;
                        Saturation.Apply(display, origSat);
                        Saturation.SetOriginalValue(display.displayLink, origSat);
                    }

                    // 3. 모니터 하드웨어(DDC/CI, WMI) 복원 검증
                    if (saved.IsExternal && display.isExternal)
                    {
                        if (Display.RefreshPhysicalMonitorHandle(display) &&
                            display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
                        {
                            int targetB = Math.Max(0, Math.Min(100, saved.MonitorBrightness));
                            int targetC = saved.MonitorContrast >= 0 ? Math.Max(0, Math.Min(100, saved.MonitorContrast)) : 50;

                            hardwareOk = ExternalMonitor.SetBrightnessAndContrast(display.PhysicalHandle, targetB, targetC);
                            if (hardwareOk)
                            {
                                display.monitorBrightness = targetB;
                                display.monitorContrast = targetC;
                            }
                        }
                    }
                    else if (!saved.IsExternal && !display.isExternal)
                    {
                        int targetBrightness = Math.Max(0, Math.Min(100, saved.MonitorBrightness));
                        string wmiId = !string.IsNullOrEmpty(display.pnpDeviceId) ? display.pnpDeviceId : display.hardwareId;
                        hardwareOk = InternalMonitor.SetBrightness(wmiId, (byte)targetBrightness);
                        if (hardwareOk)
                        {
                            display.monitorBrightness = targetBrightness;
                        }
                    }

                    // 감마 복구 성공 여부를 핵심 기준으로 기록
                    if (!gammaOk)
                    {
                        allRestoredSuccessfully = false;
                    }
                }

                // 최소 1개 이상 모니터가 매칭되고 소프트웨어 감마가 모두 정상 복구되었으면 백업 정리
                if (matchedAny && allRestoredSuccessfully)
                {
                    ClearBackup();
                    return true;
                }

                Logger.Warn("RestorePending: Gamma restore failed for some displays. Retaining backup file for safety.");
                return matchedAny;
            }
            catch (Exception ex)
            {
                Logger.Error("Startup state restore failed.", ex);
                return false;
            }
        }

        public static bool Capture(List<Display.DisplayInfo> displays)
        {
            try
            {
                StartupState state = new StartupState();
                foreach (Display.DisplayInfo display in displays)
                {
                    StartupDisplayState saved = new StartupDisplayState();
                    saved.DisplayLink = display.displayLink;
                    saved.MonitorKey = DisplayService.GetMonitorKey(display);
                    saved.HardwareId = display.hardwareId;
                    saved.IsExternal = display.isExternal;
                    saved.Saturation = display.saturation;
                    if (display.saturationSupported)
                    {
                        Saturation.SetOriginalValue(display.displayLink, display.saturation);
                    }
                    int brightness;
                    int contrast;
                    if (display.isExternal)
                    {
                        bool brightnessOk = false;
                        bool contrastOk = false;

                        for (int attempt = 0; attempt < 3 && !brightnessOk; attempt++)
                        {
                            if (ExternalMonitor.TryGetBrightness(display.PhysicalHandle, out brightness))
                            {
                                saved.MonitorBrightness = brightness;
                                brightnessOk = true;
                            }
                            else
                            {
                                Thread.Sleep(100);

                                if (Display.RefreshPhysicalMonitorHandle(display))
                                {
                                    Thread.Sleep(100);
                                }
                            }
                        }

                        for (int attempt = 0; attempt < 3 && !contrastOk; attempt++)
                        {
                            if (ExternalMonitor.TryGetContrast(display.PhysicalHandle, out contrast))
                            {
                                saved.MonitorContrast = contrast;
                                contrastOk = true;
                            }
                            else
                            {
                                Thread.Sleep(100);

                                if (Display.RefreshPhysicalMonitorHandle(display))
                                {
                                    Thread.Sleep(100);
                                }
                            }
                        }

                        if (!brightnessOk)
                            saved.MonitorBrightness = display.monitorBrightness;

                        if (!contrastOk)
                            saved.MonitorContrast = display.monitorContrast;
                    }
                    else
                    {
                        string wmiId = !string.IsNullOrEmpty(display.pnpDeviceId) ? display.pnpDeviceId : display.hardwareId;
                        saved.MonitorBrightness =
                            InternalMonitor.TryGetBrightness(
                                wmiId,
                                out brightness)
                                ? brightness
                                : display.monitorBrightness;

                        saved.MonitorContrast = -1;
                    }
                    saved.GammaRamp = Flatten(Gamma.GetGammaRamp(display.displayLink));
                    state.Displays.Add(saved);
                }

                // 메모리 캐시 및 파일 저장 전체를 락(Lock)으로 보호하여 충돌 방지
                lock (_stateLock)
                {
                    _cachedState = state;
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(StartupState));
                    using (FileStream fs = File.Create(BackupPath))
                    {
                        serializer.WriteObject(fs, state);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Startup state capture failed.", ex);
                return false;
            }
        }

        private static StartupState GetOrLoadState()
        {
            StartupState state = null;
            lock (_stateLock)
            {
                state = _cachedState;
            }

            if (state == null && File.Exists(BackupPath))
            {
                using (FileStream fs = File.OpenRead(BackupPath))
                {
                    state = (StartupState)new DataContractJsonSerializer(typeof(StartupState)).ReadObject(fs);
                }
                lock (_stateLock)
                {
                    if (_cachedState == null)
                        _cachedState = state;
                }
            }

            return state;
        }

        public static bool RestoreOriginalMonitor(Display.DisplayInfo display, Func<bool> isValid = null)
        {
            if (display == null) return false;
            try
            {
                StartupState state = GetOrLoadState();
                StartupDisplayState saved = FindMatchingSavedState(state, display);
                if (saved == null) return false;

                if (isValid != null && !isValid()) return false;

                // 1. GPU 감마 복원 검증
                bool gammaOk = false;
                ushort[,] ramp = Expand(saved.GammaRamp);
                if (ramp != null)
                    gammaOk = Gamma.SetRawGammaRamp(display.displayLink, ramp);
                else
                    gammaOk = Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f));

                if (isValid != null && !isValid()) return false;

                // 1-1. GPU 채도(Digital Vibrance/ADL) 원본 복원 및 캐시 동기화
                int origSat = ResolveOriginalSaturation(saved, display);
                Saturation.Prepare(display);
                if (display.saturationSupported)
                {
                    display.saturation = origSat;
                    Saturation.Apply(display, origSat);
                    Saturation.SetOriginalValue(display.displayLink, origSat);
                }

                // 2. 모니터 하드웨어 복원 검증
                bool hardwareOk = false;
                if (saved.IsExternal && display.isExternal)
                {
                    Display.RefreshPhysicalMonitorHandle(display);
                    if (display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
                    {
                        if (isValid != null && !isValid()) return false;
                        bool bOk = ExternalMonitor.SetBrightness(display.PhysicalHandle, (uint)Math.Max(0, Math.Min(100, saved.MonitorBrightness)));

                        if (isValid != null && !isValid()) return false;
                        bool cOk = true;
                        if (saved.MonitorContrast >= 0)
                            cOk = ExternalMonitor.SetContrast(display.PhysicalHandle, (uint)Math.Max(0, Math.Min(100, saved.MonitorContrast)));

                        hardwareOk = bOk && cOk;
                    }
                }
                else if (!saved.IsExternal && !display.isExternal)
                {
                    if (isValid != null && !isValid()) return false;
                    int target = Math.Max(0, Math.Min(100, saved.MonitorBrightness));
                    string wmiId = !string.IsNullOrEmpty(display.pnpDeviceId) ? display.pnpDeviceId : display.hardwareId;
                    hardwareOk = InternalMonitor.SetBrightness(wmiId, (byte)target);
                }

                // 감마와 하드웨어가 모두 성공해야만 성공(true) 반환
                return gammaOk && hardwareOk;
            }
            catch (Exception ex)
            {
                Logger.Error("Hard reset monitor restore failed.", ex);
                return false;
            }
        }

        public static bool RestoreOriginalMonitors(List<Display.DisplayInfo> displays, Func<bool> isValid = null)
        {
            if (displays == null || displays.Count == 0) return false;
            try
            {
                StartupState state = GetOrLoadState();
                if (state == null || state.Displays == null) return false;

                bool allSuccess = true;
                bool matchedAny = false;

                foreach (Display.DisplayInfo display in displays)
                {
                    if (display == null) continue;
                    StartupDisplayState saved = FindMatchingSavedState(state, display);
                    if (saved == null) continue;

                    matchedAny = true;

                    if (isValid != null && !isValid()) return false;

                    bool gammaOk = false;
                    ushort[,] ramp = Expand(saved.GammaRamp);
                    if (ramp != null)
                        gammaOk = Gamma.SetRawGammaRamp(display.displayLink, ramp);
                    else
                        gammaOk = Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f));

                    if (isValid != null && !isValid()) return false;

                    int origSat = ResolveOriginalSaturation(saved, display);
                    Saturation.Prepare(display);
                    if (display.saturationSupported)
                    {
                        display.saturation = origSat;
                        Saturation.Apply(display, origSat);
                        Saturation.SetOriginalValue(display.displayLink, origSat);
                    }

                    bool hardwareOk = false;
                    if (saved.IsExternal && display.isExternal)
                    {
                        Display.RefreshPhysicalMonitorHandle(display);
                        if (display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
                        {
                            if (isValid != null && !isValid()) return false;
                            bool bOk = ExternalMonitor.SetBrightness(display.PhysicalHandle, (uint)Math.Max(0, Math.Min(100, saved.MonitorBrightness)));

                            if (isValid != null && !isValid()) return false;
                            bool cOk = true;
                            if (saved.MonitorContrast >= 0)
                                cOk = ExternalMonitor.SetContrast(display.PhysicalHandle, (uint)Math.Max(0, Math.Min(100, saved.MonitorContrast)));

                            hardwareOk = bOk && cOk;
                        }
                    }
                    else if (!saved.IsExternal && !display.isExternal)
                    {
                        if (isValid != null && !isValid()) return false;
                        int target = Math.Max(0, Math.Min(100, saved.MonitorBrightness));
                        string wmiId = !string.IsNullOrEmpty(display.pnpDeviceId) ? display.pnpDeviceId : display.hardwareId;
                        hardwareOk = InternalMonitor.SetBrightness(wmiId, (byte)target);
                    }

                    // 하드웨어 DDC/CI 실패로 인한 무한 루프 방지를 위해 감마 성공 여부 우선
                    if (!gammaOk)
                        allSuccess = false;
                }
                return matchedAny && allSuccess;
            }
            catch (Exception ex)
            {
                Logger.Error("Hard reset all monitors restore failed.", ex);
                return false;
            }
        }

        public static void ClearBackup()
        {
            try
            {
                lock (_stateLock)
                {
                    _cachedState = null; // 메모리 캐시 정리
                }

                if (File.Exists(BackupPath))
                    File.Delete(BackupPath);
            }
            catch (Exception ex)
            {
                Logger.Warn("ClearBackup failed: " + ex.Message);
            }
        }

        public static bool TryGetOriginalValues(string displayLink, out int brightness, out int contrast, string monitorKey = null)
        {
            brightness = 50;
            contrast = 50;
            if (string.IsNullOrEmpty(displayLink) && string.IsNullOrEmpty(monitorKey)) return false;

            try
            {
                StartupState state = GetOrLoadState();
                if (state == null || state.Displays == null) return false;

                if (!string.IsNullOrEmpty(monitorKey))
                {
                    foreach (var saved in state.Displays)
                    {
                        if (saved != null && !string.IsNullOrEmpty(saved.MonitorKey) &&
                            string.Equals(saved.MonitorKey, monitorKey, StringComparison.OrdinalIgnoreCase))
                        {
                            brightness = saved.MonitorBrightness;
                            contrast = saved.MonitorContrast;
                            return true;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(displayLink))
                {
                    foreach (var saved in state.Displays)
                    {
                        if (saved != null && string.Equals(saved.DisplayLink, displayLink, StringComparison.OrdinalIgnoreCase))
                        {
                            brightness = saved.MonitorBrightness;
                            contrast = saved.MonitorContrast;
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static bool TryGetOriginalSaturation(string displayLink, out int saturation, string monitorKey = null)
        {
            saturation = 100;
            if (string.IsNullOrEmpty(displayLink) && string.IsNullOrEmpty(monitorKey)) return false;

            try
            {
                StartupState state = GetOrLoadState();
                if (state == null || state.Displays == null) return false;

                if (!string.IsNullOrEmpty(monitorKey))
                {
                    foreach (var saved in state.Displays)
                    {
                        if (saved != null && !string.IsNullOrEmpty(saved.MonitorKey) &&
                            string.Equals(saved.MonitorKey, monitorKey, StringComparison.OrdinalIgnoreCase) &&
                            saved.Saturation >= 0)
                        {
                            saturation = saved.Saturation;
                            return true;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(displayLink))
                {
                    foreach (var saved in state.Displays)
                    {
                        if (saved != null && string.Equals(saved.DisplayLink, displayLink, StringComparison.OrdinalIgnoreCase) &&
                            saved.Saturation >= 0)
                        {
                            saturation = saved.Saturation;
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static void RestoreAndClear(List<Display.DisplayInfo> displays)
        {
            RestorePending(displays);
        }
    }
}