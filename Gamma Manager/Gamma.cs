using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Gamma_Manager
{
    internal static class Gamma
    {
        private const float MinGamma = 0.3f;
        private const float MaxGamma = 4.4f;

        private const float MinContrast = 0.1f;
        private const float MaxContrast = 10.0f;

        private const float MinBright = -1.0f;
        private const float MaxBright = 1.0f;

        // SetDeviceGammaRamp is subject to Windows/driver safety heuristics.
        // Keep each entry reasonably close to the identity ramp so extreme UI
        // combinations cannot produce an effectively all-black/all-white table.
        private const int IdentityGuard = 32767;

        private static readonly ConcurrentDictionary<string, object> RampLocks =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        private static T Clamp<T>(T value, T min, T max) where T : IComparable<T>
        {
            if (value.CompareTo(min) < 0) return min;
            if (value.CompareTo(max) > 0) return max;
            return value;
        }

        private static float SanitizeFloat(float value, float fallback, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                value = fallback;
            return Clamp(value, min, max);
        }

        public static ushort[,] CreateGammaRamp(
            float rGamma, float gGamma, float bGamma,
            float rContrast, float gContrast, float bContrast,
            float rBright, float gBright, float bBright,
            int shadowBoost = 0,
            int shadowBoostMode = 0,
            int shadowBoostTint = 0,
            int highlightGuard = 0,
            int customPeak = 25,
            int customWidth = 3)
        {
            rGamma = SanitizeFloat(rGamma, 1.0f, MinGamma, MaxGamma);
            gGamma = SanitizeFloat(gGamma, 1.0f, MinGamma, MaxGamma);
            bGamma = SanitizeFloat(bGamma, 1.0f, MinGamma, MaxGamma);

            rContrast = SanitizeFloat(rContrast, 1.0f, MinContrast, MaxContrast);
            gContrast = SanitizeFloat(gContrast, 1.0f, MinContrast, MaxContrast);
            bContrast = SanitizeFloat(bContrast, 1.0f, MinContrast, MaxContrast);

            rBright = SanitizeFloat(rBright, 0.0f, MinBright, MaxBright);
            gBright = SanitizeFloat(gBright, 0.0f, MinBright, MaxBright);
            bBright = SanitizeFloat(bBright, 0.0f, MinBright, MaxBright);

            shadowBoost = Math.Max(0, Math.Min(100, shadowBoost));

            // 암부 컬러 틴트 배율 (0: Neutral, 1: Warm, 2: Cold)
            double rTint = 1.0, gTint = 1.0, bTint = 1.0;
            if (shadowBoostTint == 1) // Warm (호박/스킨톤 강조)
            {
                rTint = 1.15;
                gTint = 1.05;
                bTint = 0.85;
            }
            else if (shadowBoostTint == 2) // Cold (청록/콘트라스트 분리)
            {
                rTint = 0.85;
                gTint = 1.05;
                bTint = 1.15;
            }

            ushort[,] ramp = new ushort[3, 256];

            for (int i = 0; i < 256; i++)
            {
                double input = i / 255.0;

                ramp[0, i] = CreateChannelValue(input, rGamma, rContrast, rBright, shadowBoost, shadowBoostMode, rTint, highlightGuard, customPeak, customWidth);
                ramp[1, i] = CreateChannelValue(input, gGamma, gContrast, gBright, shadowBoost, shadowBoostMode, gTint, highlightGuard, customPeak, customWidth);
                ramp[2, i] = CreateChannelValue(input, bGamma, bContrast, bBright, shadowBoost, shadowBoostMode, bTint, highlightGuard, customPeak, customWidth);
            }

            // First make the mathematical curves valid and monotonic.
            NormalizeRamp(ramp);

            // Then constrain the final table itself to a Windows/driver-safe
            // distance from identity. This is the important part for extreme
            // combinations such as minimum contrast + minimum brightness.
            ApplyIdentitySafetyLimit(ramp);
            NormalizeRamp(ramp);

            return ramp;
        }

        public static double ApplyChannelCurve(
            double input,
            double gamma,
            double contrast,
            double brightness,
            int shadowBoost = 0,
            int shadowBoostMode = 0,
            double tintMultiplier = 1.0,
            int highlightGuard = 0,
            int customPeak = 25,
            int customWidth = 3)
        {
            ushort val = CreateChannelValue(input, gamma, contrast, brightness, shadowBoost, shadowBoostMode, tintMultiplier, highlightGuard, customPeak, customWidth);
            return val / 65535.0;
        }

        private static ushort CreateChannelValue(
            double input,
            double gamma,
            double contrast,
            double brightness,
            int shadowBoost = 0,
            int shadowBoostMode = 0,
            double tintMultiplier = 1.0,
            int highlightGuard = 0,
            int customPeak = 25,
            int customWidth = 3)
        {
            input = Clamp01(input);

            // 1. Black Equalizer / Shadow Boost
            if (shadowBoost > 0)
            {
                double factor = shadowBoost / 100.0;
                double lift = 0.0;

                double om = 1.0 - input;
                switch (shadowBoostMode)
                {
                    case 1:
                        // 모드 2: 야간전 (Deep Shadow / Night Mode)
                        double k1 = (factor * 0.40) * 8.0;
                        lift = k1 * input * om * om * om;
                        break;

                    case 2:
                        // 모드 3: 정밀 분리형 (Precision Spline / Target Cut)
                        double k2 = (factor * 0.38) * 8.4;
                        double om2 = om * om;
                        lift = k2 * input * om2 * om2;
                        break;

                    case 3:
                        // 모드 4: e스포츠 트루 블랙 (True Black / OLED Guard)
                        double k3 = (factor * 0.42) * 8.2;
                        double s = Math.Min(1.0, input / 0.04);
                        double anchor = s * s * (3.0 - 2.0 * s);
                        lift = k3 * input * om * om * om * anchor;
                        break;

                    case 4:
                        // 모드 5: 사용자 정의 대역폭 (Custom Peak & Bandwidth)
                        double p = Math.Max(0.10, Math.Min(0.40, customPeak / 100.0));
                        int w = Math.Max(1, Math.Min(5, customWidth));
                        double beta = 6.0 - w;
                        double alpha = beta * (p / (1.0 - p));
                        double peakNorm = Math.Pow(p, alpha) * Math.Pow(1.0 - p, beta);
                        if (peakNorm > 1e-6 && input > 0.0 && input < 1.0)
                        {
                            double rawCurve = Math.Pow(input, alpha) * Math.Pow(1.0 - input, beta) / peakNorm;
                            lift = (factor * 0.40) * rawCurve;
                        }
                        break;

                    default:
                        // 모드 1: FPS 표준 밸런스 (Balanced Toe)
                        double k0 = (factor * 0.35) * 6.75;
                        lift = k0 * input * om * om;
                        break;
                }

                // 암부 컬러 틴트 배율 적용
                lift *= tintMultiplier;

                input = Clamp01(input + lift);
            }

            // All controls are evaluated from the same original input value.
            double value = ((input - 0.5) * contrast) + 0.5;
            value += brightness * 0.5;
            value = Clamp01(value);

            gamma = Math.Max(MinGamma, Math.Min(MaxGamma, gamma));
            value = Math.Pow(value, 1.0 / gamma);
            value = Clamp01(value);

            // 2. 조명 & 후레쉬 눈부심 방지 (Highlight Guard / Adjustable Tone Compression)
            if (highlightGuard > 0)
            {
                double intensity = Math.Max(0.0, Math.Min(1.0, highlightGuard / 100.0));
                double threshold = 0.80 - 0.30 * intensity;
                if (value > threshold)
                {
                    double t = (value - threshold) / (1.0 - threshold);
                    double maxPeak = 1.0 - (0.28 * intensity);
                    double compressed = threshold + (maxPeak - threshold) * (t - 0.22 * t * t) / 0.78;
                    value = Math.Min(maxPeak, Math.Max(threshold, compressed));
                }
            }

            int result = (int)Math.Round(value * 65535.0);
            if (result < 0) result = 0;
            if (result > 65535) result = 65535;
            return (ushort)result;
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0.0;
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }

        private static void NormalizeRamp(ushort[,] ramp)
        {
            if (ramp == null) return;

            for (int channel = 0; channel < 3; channel++)
            {
                ramp[channel, 0] = 0;

                // Non-decreasing.
                for (int i = 1; i < 256; i++)
                {
                    if (ramp[channel, i] < ramp[channel, i - 1])
                        ramp[channel, i] = ramp[channel, i - 1];
                }

                // Keep enough headroom for the first pass to remain valid.
                for (int i = 254; i >= 0; i--)
                {
                    if (ramp[channel, i] > ramp[channel, i + 1])
                        ramp[channel, i] = ramp[channel, i + 1];
                }
            }
        }

        private static void ApplyIdentitySafetyLimit(ushort[,] ramp)
        {
            if (ramp == null) return;

            for (int channel = 0; channel < 3; channel++)
            {
                for (int i = 0; i < 256; i++)
                {
                    int identity = i * 257;
                    int lower = Math.Max(0, identity - IdentityGuard);
                    int upper = Math.Min(65535, identity + IdentityGuard);

                    int value = ramp[channel, i];
                    if (value < lower) value = lower;
                    if (value > upper) value = upper;

                    ramp[channel, i] = (ushort)value;
                }
            }
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CreateDC(
            string lpszDriver,
            string lpszDevice,
            string lpszOutput,
            IntPtr lpInitData);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[,] ramp);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern bool GetDeviceGammaRamp(IntPtr hdc, [Out] ushort[,] lpRamp);

        private static IntPtr GetDisplayDC(string displayLink)
        {
            if (string.IsNullOrEmpty(displayLink)) return IntPtr.Zero;

            IntPtr hDC = CreateDC(displayLink, null, null, IntPtr.Zero);
            if (hDC != IntPtr.Zero) return hDC;

            hDC = CreateDC(null, displayLink, null, IntPtr.Zero);
            if (hDC != IntPtr.Zero) return hDC;

            return CreateDC(displayLink, displayLink, null, IntPtr.Zero);
        }

        public static bool SetGammaRamp(string displayLink, ushort[,] ramp)
        {
            if (string.IsNullOrEmpty(displayLink) || ramp == null)
                return false;

            object rampLock = RampLocks.GetOrAdd(displayLink, _ => new object());
            lock (rampLock)
            {
                IntPtr hDC = GetDisplayDC(displayLink);
                if (hDC == IntPtr.Zero)
                {
                    Logger.Warn("Failed to create DC for " + displayLink);
                    return false;
                }

                try
                {
                    bool success = SetDeviceGammaRamp(hDC, ramp);
                    if (!success)
                    {
                        int error = Marshal.GetLastWin32Error();
                        Logger.Warn(
                            "Gamma SetDeviceGammaRamp rejected the requested ramp for " +
                            displayLink + ". Win32Error=" + error +
                            ". The ramp was already constrained to the identity safety range.");
                        return false;
                    }

                    return true;
                }
                finally
                {
                    DeleteDC(hDC);
                }
            }
        }

        public static ushort[,] GetGammaRamp(string displayLink)
        {
            if (string.IsNullOrEmpty(displayLink)) return null;

            object rampLock = RampLocks.GetOrAdd(displayLink, _ => new object());
            lock (rampLock)
            {
                IntPtr hDC = GetDisplayDC(displayLink);
                if (hDC == IntPtr.Zero)
                {
                    Logger.Warn("Gamma Get failed: CreateDC returned NULL for " + displayLink);
                    return null;
                }

                try
                {
                    ushort[,] ramp = new ushort[3, 256];
                    if (!GetDeviceGammaRamp(hDC, ramp))
                    {
                        int error = Marshal.GetLastWin32Error();
                        Logger.Warn(
                            "Gamma GetDeviceGammaRamp failed for " + displayLink +
                            ". Win32Error=" + error);
                        return null;
                    }
                    return ramp;
                }
                finally
                {
                    DeleteDC(hDC);
                }
            }
        }

        public static bool SetRawGammaRamp(string displayLink, ushort[,] ramp)
        {
            // Raw restore values can come from the startup snapshot, so do not
            // mutate the snapshot here. The same serialization and Win32 call
            // path are used, but no second arbitrary fallback ramp is attempted.
            return SetGammaRamp(displayLink, ramp);
        }
    }
}
