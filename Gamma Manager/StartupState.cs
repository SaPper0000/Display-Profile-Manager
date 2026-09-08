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
        [DataMember] public bool IsExternal;
        [DataMember] public int MonitorBrightness;
        [DataMember] public int MonitorContrast;
        [DataMember] public int Saturation; // 👈 시작 시점 원본 채도 보관 필드 추가
        [DataMember] public ushort[] GammaRamp;
    }

    [DataContract]
    internal sealed class StartupState
    {
        [DataMember] public List<StartupDisplayState> Displays = new List<StartupDisplayState>();
    }

    internal static class StartupStateManager
    {
        private static readonly object _stateLock = new object();
        private static StartupState _cachedState = null; // 👈 메모리 캐시 필드 추가

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
                    Display.DisplayInfo display = null;
                    foreach (Display.DisplayInfo d in displays)
                        if (string.Equals(d.displayLink, saved.DisplayLink, StringComparison.OrdinalIgnoreCase)) { display = d; break; }

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

                    // 2. GPU 채도(Digital Vibrance/ADL) 강제 준비 및 복원
                    int origSat = saved.Saturation > 0 ? saved.Saturation : (display.saturationDefault > 0 ? display.saturationDefault : 100);

                    Saturation.Prepare(display);
                    if (display.saturationSupported)
                    {
                        display.saturation = origSat;
                        Saturation.Apply(display, origSat);
                        Saturation.SetOriginalValue(display.displayLink, origSat); // 👈 정상 종료 시 200이 아닌 원래 값으로 리셋되도록 캐시 교체
                    }

                    // 2. 모니터 하드웨어(DDC/CI, WMI) 복원 검증
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
                        hardwareOk = InternalMonitor.SetBrightness(display.hardwareId, (byte)targetBrightness);
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
                    saved.IsExternal = display.isExternal;
                    saved.Saturation = display.saturation;
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
                        saved.MonitorBrightness =
                            InternalMonitor.TryGetBrightness(
                                display.hardwareId,
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

        public static bool RestoreOriginalMonitor(Display.DisplayInfo display, Func<bool> isValid = null)
        {
            if (display == null) return false;
            try
            {
                StartupState state = null;
                lock (_stateLock)
                {
                    state = _cachedState; // 1순위: 메모리 캐시에서 즉시 조회
                }

                // 메모리에 없을 때만 디스크에서 읽기 (Fallback)
                if (state == null && File.Exists(BackupPath))
                {
                    using (FileStream fs = File.OpenRead(BackupPath))
                        state = (StartupState)new DataContractJsonSerializer(typeof(StartupState)).ReadObject(fs);
                }

                if (state == null || state.Displays == null) return false;

                foreach (StartupDisplayState saved in state.Displays)
                {
                    if (!string.Equals(display.displayLink, saved.DisplayLink, StringComparison.OrdinalIgnoreCase)) continue;

                    if (isValid != null && !isValid()) return false;

                    // 1. GPU 감마 복원 검증
                    bool gammaOk = false;
                    ushort[,] ramp = Expand(saved.GammaRamp);
                    if (ramp != null)
                        gammaOk = Gamma.SetRawGammaRamp(display.displayLink, ramp);
                    else
                        gammaOk = Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f));

                    if (isValid != null && !isValid()) return false;

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
                        hardwareOk = InternalMonitor.SetBrightness(display.hardwareId, (byte)target);
                    }

                    // 감마와 하드웨어가 모두 성공해야만 성공(true) 반환
                    return gammaOk && hardwareOk;
                }
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error("Hard reset monitor restore failed.", ex);
                return false;
            }
        }

        public static bool RestoreOriginalMonitors(List<Display.DisplayInfo> displays, Func<bool> isValid = null)
        {
            if (displays == null || displays.Count == 0 || !File.Exists(BackupPath)) return false;
            try
            {
                StartupState state;
                using (FileStream fs = File.OpenRead(BackupPath))
                    state = (StartupState)new DataContractJsonSerializer(typeof(StartupState)).ReadObject(fs);
                if (state == null || state.Displays == null) return false;

                bool allSuccess = true;
                bool matchedAny = false;

                foreach (Display.DisplayInfo display in displays)
                {
                    if (display == null) continue;
                    foreach (StartupDisplayState saved in state.Displays)
                    {
                        if (!string.Equals(display.displayLink, saved.DisplayLink, StringComparison.OrdinalIgnoreCase)) continue;

                        matchedAny = true;

                        if (isValid != null && !isValid()) return false;

                        bool gammaOk = false;
                        ushort[,] ramp = Expand(saved.GammaRamp);
                        if (ramp != null)
                            gammaOk = Gamma.SetRawGammaRamp(display.displayLink, ramp);
                        else
                            gammaOk = Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f));

                        if (isValid != null && !isValid()) return false;

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
                            hardwareOk = InternalMonitor.SetBrightness(display.hardwareId, (byte)target);
                        }

                        // 하드웨어 DDC/CI 실패로 인한 무한 루프 방지를 위해 감마 성공 여부 우선
                        if (!gammaOk)
                            allSuccess = false;

                        break;
                    }
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

        public static bool TryGetOriginalValues(string displayLink, out int brightness, out int contrast)
        {
            brightness = 50;
            contrast = 50;
            if (string.IsNullOrEmpty(displayLink)) return false;

            try
            {
                StartupState state = null;
                lock (_stateLock)
                {
                    state = _cachedState; // 👈 1순위: 메모리 캐시에서 즉시 조회 (디스크 I/O 없음)
                }

                // 메모리에 없는데 백업 파일이 남아있는 경우 디스크에서 로드 (Fallback)
                if (state == null && File.Exists(BackupPath))
                {
                    using (FileStream fs = File.OpenRead(BackupPath))
                    {
                        state = (StartupState)new DataContractJsonSerializer(typeof(StartupState)).ReadObject(fs);
                    }
                    lock (_stateLock)
                    {
                        _cachedState = state;
                    }
                }

                if (state == null || state.Displays == null) return false;

                foreach (var saved in state.Displays)
                {
                    if (string.Equals(saved.DisplayLink, displayLink, StringComparison.OrdinalIgnoreCase))
                    {
                        brightness = saved.MonitorBrightness;
                        contrast = saved.MonitorContrast;
                        return true;
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