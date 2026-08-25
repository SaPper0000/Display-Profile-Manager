using System;
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
        private int _applyGeneration = 0;

        public int NextGeneration()
        {
            return Interlocked.Increment(ref _applyGeneration);
        }

        public int CurrentGeneration => Volatile.Read(ref _applyGeneration);

        public object ApplyLock => _physicalApplyLock;

        public void ApplySoftwareDisplaySettings(Display.DisplayInfo display)
        {
            if (display == null || string.IsNullOrEmpty(display.displayLink)) return;

            var gammaRamp = Gamma.CreateGammaRamp(
                display.rGamma, display.gGamma, display.bGamma,
                display.rContrast, display.gContrast, display.bContrast,
                display.rBright, display.gBright, display.bBright);

            Gamma.SetGammaRamp(display.displayLink, gammaRamp);

            if (display.saturationSupported)
            {
                Saturation.Prepare(display);
                Saturation.Apply(display, display.saturation);
            }
        }

        public void QueuePhysicalSettings(
            IEnumerable<Display.DisplayInfo> displays,
            string monitorName,
            int brightness,
            int contrast,
            string logContext,
            int targetGeneration)
        {
            if (string.IsNullOrEmpty(monitorName)) return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (_physicalApplyLock)
                {
                    if (targetGeneration != CurrentGeneration) return;

                    Display.DisplayInfo targetDisplay = null;
                    foreach (var d in displays)
                    {
                        if (d != null && string.Equals(d.displayName, monitorName, StringComparison.Ordinal))
                        {
                            targetDisplay = d;
                            break;
                        }
                    }

                    if (targetDisplay == null) return;

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
                    Logger.Warn($"[{logContext}] No valid DDC/CI handle for {display.displayName}");
                }
            }
            else
            {
                InternalMonitor.SetBrightness((byte)safeBrightness);
                applied = true;
            }

            // 하드웨어 적용에 성공했거나 내장 모니터일 때만 내부 상태 변수 갱신
            if (applied)
            {
                display.monitorBrightness = safeBrightness;
                display.monitorContrast = safeContrast;
            }
            else
            {
                Logger.Warn($"[{logContext}] Failed to apply physical settings to {display.displayName}");
            }
        }

        public void ApplyBrightnessDirect(Display.DisplayInfo display, int brightness)
        {
            if (display == null) return;
            lock (_physicalApplyLock)
            {
                int safeBrightness = Math.Max(0, Math.Min(100, brightness));
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
                    InternalMonitor.SetBrightness((byte)safeBrightness);
                    ok = true;
                }

                if (ok) display.monitorBrightness = safeBrightness;
            }
        }

        public void ApplyContrastDirect(Display.DisplayInfo display, int contrast)
        {
            if (display == null) return;
            lock (_physicalApplyLock)
            {
                int safeContrast = Math.Max(0, Math.Min(100, contrast));
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
        }

        public void ResetPhysicalMonitorAsync(Display.DisplayInfo display, int targetGeneration)
        {
            if (display == null) return;
            const int defaultVal = 50;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (_physicalApplyLock)
                {
                    if (targetGeneration != CurrentGeneration) return;

                    if (display.isExternal)
                    {
                        if (display.PhysicalHandle == IntPtr.Zero || display.PhysicalHandle == (IntPtr)(-1))
                        {
                            Display.RefreshPhysicalMonitorHandle(display);
                        }

                        if (display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
                        {
                            bool ok = ExternalMonitor.SetBrightnessAndContrast(display.PhysicalHandle, defaultVal, defaultVal);
                            if (!ok && Display.RefreshPhysicalMonitorHandle(display))
                            {
                                ExternalMonitor.SetBrightnessAndContrast(display.PhysicalHandle, defaultVal, defaultVal);
                            }
                        }
                    }
                    else
                    {
                        InternalMonitor.SetBrightness((byte)defaultVal);
                    }
                }
            });
        }
    }
}