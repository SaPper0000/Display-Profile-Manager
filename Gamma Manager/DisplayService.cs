using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Gamma_Manager
{
    /// <summary>
    /// 모니터 감마, 채도 및 DDC/CI/WMI 하드웨어 제어를 전담하는 도메인 서비스
    /// </summary>
    internal class DisplayService
    {
        private readonly object _physicalApplyLock = new object();
        private readonly object _softwareApplyLock = new object(); // 👈 소프트웨어 감마/채도 전용 직렬 락

        // 👈 모니터별(hardwareId 우선, displayName Fallback) 독립 Generation 카운터 관리
        private readonly ConcurrentDictionary<string, int> _applyGenerations =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public object SoftwareLock => _softwareApplyLock;

        // 👈 디스플레이 토폴로지(연결/해제/재탐색) 변경 감지용 세대 카운터
        private int _topologyGeneration = 0;

        // 모니터별 식별 키 추출. EDID Serial 기반 monitorKey를 단일 기준으로 사용한다.
        public static string GetMonitorKey(Display.DisplayInfo display)
        {
            if (display == null) return string.Empty;
            if (!string.IsNullOrEmpty(display.monitorKey)) return display.monitorKey;

            // displayName은 동일 모델 구분을 위해 (#1)/(#2)가 붙을 수 있으므로
            // 절대로 영속 저장 식별자의 1차 fallback으로 사용하지 않는다.
            if (!string.IsNullOrEmpty(display.hardwareId))
            {
                MonitorIdentity.IdentityInfo identity = MonitorIdentity.Read(display.hardwareId);
                string rebuilt = MonitorIdentity.BuildBaseKey(identity, display.displayLink);
                if (!string.IsNullOrEmpty(rebuilt)) return rebuilt;
                return "PNP|" + display.hardwareId;
            }

            // 하드웨어 ID조차 없는 최후의 경우에만 displayLink를 사용한다.
            // 이 경우 Windows가 링크 번호를 재배정할 가능성이 있으므로 안정 식별자가
            // 아니라 세션 한정 fallback이라는 점을 명확히 한다.
            if (!string.IsNullOrEmpty(display.displayLink)) return "DISPLAY|" + display.displayLink;
            return !string.IsNullOrEmpty(display.baseDisplayName)
                ? display.baseDisplayName
                : (display.displayName ?? string.Empty);
        }

        // 디스플레이 토폴로지 세대 번호 발급 및 조회
        public int NextTopologyGeneration()
        {
            return Interlocked.Increment(ref _topologyGeneration);
        }

        public int CurrentTopologyGeneration => Volatile.Read(ref _topologyGeneration);

        // 모니터별 세대 번호 증가 및 발급
        public int NextGeneration(string monitorKey)
        {
            if (string.IsNullOrEmpty(monitorKey)) return 0;
            return _applyGenerations.AddOrUpdate(monitorKey, 1, (_, current) => unchecked(current + 1));
        }

        // 모니터별 현재 세대 번호 조회
        public int GetCurrentGeneration(string monitorKey)
        {
            if (string.IsNullOrEmpty(monitorKey)) return 0;
            return _applyGenerations.TryGetValue(monitorKey, out int gen) ? gen : 0;
        }

        // 전체 모니터 세대 번호 일괄 무효화 (프로그램 종료 / 전체 초기화 / 디스플레이 재탐색 시 사용)
        public void InvalidateAllGenerations()
        {
            foreach (var key in _applyGenerations.Keys)
            {
                _applyGenerations.AddOrUpdate(key, 1, (_, current) => unchecked(current + 1));
            }
        }

        public object ApplyLock => _physicalApplyLock;

        public bool ApplyGammaOnly(Display.DisplayInfo display)
        {
            if (display == null || string.IsNullOrEmpty(display.displayLink)) return false;

            var gammaRamp = Gamma.CreateGammaRamp(
                display.rGamma, display.gGamma, display.bGamma,
                display.rContrast, display.gContrast, display.bContrast,
                display.rBright, display.gBright, display.bBright,
                display.shadowBoost,
                display.shadowBoostMode);

            bool gammaOk = Gamma.SetGammaRamp(display.displayLink, gammaRamp);

            if (!gammaOk)
            {
                Logger.Warn($"Failed to apply software gamma ramp to {display.displayName} ({display.displayLink})");
            }

            return gammaOk;
        }

        public bool ApplySoftwareDisplaySettings(Display.DisplayInfo display)
        {
            if (display == null || string.IsNullOrEmpty(display.displayLink)) return false;

            bool gammaOk = ApplyGammaOnly(display);

            bool satOk = true;
            if (display.saturationSupported)
            {
                satOk = Saturation.Apply(display, display.saturation);
            }

            if (display.saturationSupported && !satOk)
            {
                Logger.Warn($"Failed to apply saturation to {display.displayName} ({display.displayLink})");
            }

            // 감마 성공 여부와 채도 지원 시 채도 성공 여부를 함께 검증하여 반환
            return gammaOk && (!display.saturationSupported || satOk);
        }

        public void QueuePhysicalSettings(
            IEnumerable<Display.DisplayInfo> displays,
            string monitorKey,
            int brightness,
            int contrast,
            string logContext,
            int targetGeneration)
        {
            if (string.IsNullOrEmpty(monitorKey) || displays == null) return;

            // 1. 새 안정 식별자 우선, 구버전 hardwareId/displayName은 하위 호환으로만 사용
            Display.DisplayInfo targetDisplay = null;
            foreach (var d in displays)
            {
                if (d != null && string.Equals(GetMonitorKey(d), monitorKey, StringComparison.OrdinalIgnoreCase))
                {
                    targetDisplay = d;
                    break;
                }
            }
            if (targetDisplay == null)
            {
                List<Display.DisplayInfo> legacyMatches = new List<Display.DisplayInfo>();
                foreach (var d in displays)
                {
                    if (d != null && (string.Equals(d.hardwareId, monitorKey, StringComparison.OrdinalIgnoreCase) || string.Equals(d.displayName, monitorKey, StringComparison.OrdinalIgnoreCase)))
                        legacyMatches.Add(d);
                }
                if (legacyMatches.Count == 1) targetDisplay = legacyMatches[0];
            }

            if (targetDisplay == null) return;

            int targetTopologyGen = CurrentTopologyGeneration;

            // 2. 비동기 큐에서 실행하되 GPU I2C 버스 충돌 방지를 위해 직렬(Sequential) 락 유지
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (targetTopologyGen != CurrentTopologyGeneration) return;
                if (targetGeneration != GetCurrentGeneration(monitorKey)) return;

                lock (_physicalApplyLock)
                {
                    if (targetTopologyGen != CurrentTopologyGeneration) return;
                    if (targetGeneration != GetCurrentGeneration(monitorKey)) return;

                    // 토폴로지 변경 중 파괴된 핸들이면 안전하게 중단
                    if (targetDisplay.isExternal && (targetDisplay.PhysicalHandle == IntPtr.Zero || targetDisplay.PhysicalHandle == (IntPtr)(-1)))
                    {
                        if (targetTopologyGen != CurrentTopologyGeneration) return;
                    }

                    ApplyPhysicalDirect(targetDisplay, brightness, contrast, logContext);
                }
            });
        }

        public void ApplyPhysicalDirect(Display.DisplayInfo display, int brightness, int contrast, string logContext)
        {
            if (display == null) return;

            int safeBrightness = Math.Max(0, Math.Min(100, brightness));
            int safeContrast = Math.Max(0, Math.Min(100, contrast));
            bool applied = false;

            if (display.isExternal)
            {
                if (display.PhysicalHandle == IntPtr.Zero || display.PhysicalHandle == (IntPtr)(-1))
                {
                    Display.RefreshPhysicalMonitorHandle(display);
                }

                if (display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
                {
                    applied = ExternalMonitor.SetBrightnessAndContrast(
                        display.PhysicalHandle, safeBrightness, safeContrast);

                    if (!applied)
                    {
                        if (Display.RefreshPhysicalMonitorHandle(display) &&
                            display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
                        {
                            applied = ExternalMonitor.SetBrightnessAndContrast(
                                display.PhysicalHandle, safeBrightness, safeContrast);
                        }
                    }
                }
                else
                {
                    // 토폴로지가 변경되는 정상적인 과도기에서는 불필요한 WARN 로그 방지
                    Logger.Info($"[{logContext}] Physical handle not available for {display.displayName}");
                }
            }
            else
            {
                applied = InternalMonitor.SetBrightness(display.hardwareId, (byte)safeBrightness);
            }

            if (applied)
            {
                display.monitorBrightness = safeBrightness;

                if (display.isExternal)
                    display.monitorContrast = safeContrast;
            }
            else
            {
                Logger.Warn($"[{logContext}] Failed to apply physical settings to {display.displayName}");
            }
        }

        public void ApplyBrightnessDirect(Display.DisplayInfo display, int brightness, string monitorKey, int targetGeneration, int targetTopologyGen)
        {
            if (display == null || string.IsNullOrEmpty(monitorKey)) return;
            int safeBrightness = Math.Max(0, Math.Min(100, brightness));

            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (targetTopologyGen != CurrentTopologyGeneration) return;
                if (targetGeneration != GetCurrentGeneration(monitorKey)) return;

                lock (_physicalApplyLock)
                {
                    if (targetTopologyGen != CurrentTopologyGeneration) return;
                    if (targetGeneration != GetCurrentGeneration(monitorKey)) return;

                    bool ok = false;
                    if (display.isExternal)
                    {
                        if (display.PhysicalHandle == IntPtr.Zero || display.PhysicalHandle == (IntPtr)(-1))
                        {
                            Display.RefreshPhysicalMonitorHandle(display);
                        }

                        if (display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
                        {
                            ok = ExternalMonitor.SetBrightness(display.PhysicalHandle, (uint)safeBrightness);
                            if (!ok && Display.RefreshPhysicalMonitorHandle(display) && display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
                            {
                                ok = ExternalMonitor.SetBrightness(display.PhysicalHandle, (uint)safeBrightness);
                            }
                        }
                    }
                    else
                    {
                        ok = InternalMonitor.SetBrightness(display.hardwareId, (byte)safeBrightness);
                    }

                    if (ok) display.monitorBrightness = safeBrightness;
                }
            });
        }

        public void ApplyContrastDirect(Display.DisplayInfo display, int contrast, string monitorKey, int targetGeneration, int targetTopologyGen)
        {
            if (display == null || string.IsNullOrEmpty(monitorKey)) return;
            int safeContrast = Math.Max(0, Math.Min(100, contrast));

            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (targetTopologyGen != CurrentTopologyGeneration) return;
                if (targetGeneration != GetCurrentGeneration(monitorKey)) return;

                lock (_physicalApplyLock)
                {
                    if (targetTopologyGen != CurrentTopologyGeneration) return;
                    if (targetGeneration != GetCurrentGeneration(monitorKey)) return;

                    bool ok = false;
                    if (display.isExternal)
                    {
                        if (display.PhysicalHandle == IntPtr.Zero || display.PhysicalHandle == (IntPtr)(-1))
                        {
                            Display.RefreshPhysicalMonitorHandle(display);
                        }

                        if (display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
                        {
                            ok = ExternalMonitor.SetContrast(display.PhysicalHandle, (uint)safeContrast);
                            if (!ok && Display.RefreshPhysicalMonitorHandle(display) && display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
                            {
                                ok = ExternalMonitor.SetContrast(display.PhysicalHandle, (uint)safeContrast);
                            }
                        }
                    }

                    if (ok) display.monitorContrast = safeContrast;
                }
            });
        }
    }
}