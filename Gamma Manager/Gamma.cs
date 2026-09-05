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
            int shadowBoostMode = 0)
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

            ushort[,] ramp = new ushort[3, 256];

            for (int i = 0; i < 256; i++)
            {
                double input = i / 255.0;

                ramp[0, i] = CreateChannelValue(input, rGamma, rContrast, rBright, shadowBoost, shadowBoostMode);
                ramp[1, i] = CreateChannelValue(input, gGamma, gContrast, gBright, shadowBoost, shadowBoostMode);
                ramp[2, i] = CreateChannelValue(input, bGamma, bContrast, bBright, shadowBoost, shadowBoostMode);
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

        private static ushort CreateChannelValue(
            double input,
            double gamma,
            double contrast,
            double brightness,
            int shadowBoost = 0,
            int shadowBoostMode = 0)
        {
            input = Clamp01(input);

            // 1. Black Equalizer / Shadow Boost (3가지 곡선 모드 지원)
            if (shadowBoost > 0)
            {
                double factor = shadowBoost / 100.0;
                double lift = 0.0;

                double om = 1.0 - input;
                switch (shadowBoostMode)
                {
                    case 1:
                        // 모드 2: 야간전 (Deep Shadow / Night Mode)
                        // x * (1 - x)^3 곡선: 피크 위치 x = 0.25, 극암부 집중 리프팅
                        // 최대 계수 3.2로 제어하여 f'(x) >= 1 - 3.2/4 = +0.20 > 0 (단조 증가성 100% 보장)
                        double k1 = (factor * 0.40) * 8.0;
                        lift = k1 * input * om * om * om;
                        break;

                    case 2:
                        // 모드 3: 정밀 분리형 (Precision Spline / Target Cut)
                        // x * (1 - x)^4 급감쇠 곡선: 피크 위치 x = 0.20, x >= 0.50 이상은 95% 이상 원본 보존
                        // f'(x) >= 1 - 3.2 * 0.216 = +0.31 > 0 (100% 강도에서도 역전/평탄화 없이 완벽한 계조 유지)
                        double k2 = (factor * 0.38) * 8.4;
                        double om2 = om * om;
                        lift = k2 * input * om2 * om2;
                        break;

                    default:
                        // 모드 1: FPS 표준 밸런스 (Balanced Toe)
                        // x * (1 - x)^2 곡선: 피크 위치 x = 0.33, 벤큐 스타일 부드러운 토우 곡선
                        // f'(x) >= 1 - 2.36/3 = +0.21 > 0
                        double k0 = (factor * 0.35) * 6.75;
                        lift = k0 * input * om * om;
                        break;
                }

                input = Clamp01(input + lift);
            }

            // All controls are evaluated from the same original input value.
            // The result therefore depends only on the final control state,
            // not on the order in which the UI sliders were changed.
            double value = ((input - 0.5) * contrast) + 0.5;
            value += brightness * 0.5;
            value = Clamp01(value);

            gamma = Math.Max(MinGamma, Math.Min(MaxGamma, gamma));
            value = Math.Pow(value, 1.0 / gamma);
            value = Clamp01(value);

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
                ramp[channel, 255] = 65535;

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
        private static extern bool GetDeviceGammaRamp(IntPtr hdc, ushort[,] lpRamp);

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
