using System;
using System.Globalization;

namespace Gamma_Manager
{
    /// <summary>
    /// 모니터 디스플레이 프로필 데이터 전송 객체 (DTO).
    /// 감마, 대비, 밝기, 채도, 하드웨어 DDC/CI, 섀도우 부스트 설정을 단일 객체로 캡슐화합니다.
    /// </summary>
    internal sealed class ProfileData
    {
        public string MonitorName { get; set; } = string.Empty;
        public string HardwareId { get; set; } = string.Empty;
        public string MonitorKey { get; set; } = string.Empty;

        public float RGamma { get; set; } = 1.00f;
        public float GGamma { get; set; } = 1.00f;
        public float BGamma { get; set; } = 1.00f;

        public float RContrast { get; set; } = 1.00f;
        public float GContrast { get; set; } = 1.00f;
        public float BContrast { get; set; } = 1.00f;

        public float RBright { get; set; } = 0.00f;
        public float GBright { get; set; } = 0.00f;
        public float BBright { get; set; } = 0.00f;

        public int Saturation { get; set; } = 100;
        public int MonitorBrightness { get; set; } = 50;
        public int MonitorContrast { get; set; } = 50;

        public int ShadowBoost { get; set; } = 0;
        public int ShadowBoostMode { get; set; } = 0;
        public int ShadowBoostTint { get; set; } = 0;
        public int HighlightGuard { get; set; } = 0;
        public int ShadowBoostCustomPeak { get; set; } = 25;
        public int ShadowBoostCustomWidth { get; set; } = 3;

        /// <summary>
        /// DisplayInfo 객체의 현재 상태를 스냅샷하여 ProfileData를 생성합니다.
        /// </summary>
        public static ProfileData FromDisplay(Display.DisplayInfo display, int? customBrightness = null, int? customContrast = null)
        {
            if (display == null) return new ProfileData();

            int brightness = customBrightness.HasValue
                ? Math.Max(0, Math.Min(100, customBrightness.Value))
                : Math.Max(0, Math.Min(100, display.monitorBrightness));

            int contrast = customContrast.HasValue
                ? Math.Max(0, Math.Min(100, customContrast.Value))
                : (display.isExternal ? Math.Max(0, Math.Min(100, display.monitorContrast)) : display.monitorContrast);

            int safeSat = display.saturationSupported
                ? Math.Max(display.saturationMin, Math.Min(display.saturationMax, display.saturation))
                : display.saturation;

            return new ProfileData
            {
                MonitorName = display.displayName ?? string.Empty,
                HardwareId = display.hardwareId ?? string.Empty,
                MonitorKey = DisplayService.GetMonitorKey(display),

                RGamma = display.rGamma,
                GGamma = display.gGamma,
                BGamma = display.bGamma,

                RContrast = display.rContrast,
                GContrast = display.gContrast,
                BContrast = display.bContrast,

                RBright = display.rBright,
                GBright = display.gBright,
                BBright = display.bBright,

                Saturation = safeSat,
                MonitorBrightness = brightness,
                MonitorContrast = contrast,

                ShadowBoost = Math.Max(0, Math.Min(100, display.shadowBoost)),
                ShadowBoostMode = Math.Max(0, Math.Min(4, display.shadowBoostMode)),
                ShadowBoostTint = Math.Max(0, Math.Min(2, display.shadowBoostTint)),
                HighlightGuard = Math.Max(0, Math.Min(100, display.highlightGuard)),
                ShadowBoostCustomPeak = Math.Max(10, Math.Min(40, display.shadowBoostCustomPeak)),
                ShadowBoostCustomWidth = Math.Max(1, Math.Min(5, display.shadowBoostCustomWidth))
            };
        }

        /// <summary>
        /// 프로필 수치를 모니터 DisplayInfo 객체에 적용합니다.
        /// </summary>
        public void ApplyTo(Display.DisplayInfo display)
        {
            if (display == null) return;

            display.rGamma = RGamma;
            display.gGamma = GGamma;
            display.bGamma = BGamma;

            display.rContrast = RContrast;
            display.gContrast = GContrast;
            display.bContrast = BContrast;

            display.rBright = RBright;
            display.gBright = GBright;
            display.bBright = BBright;

            display.saturation = display.saturationSupported
                ? Math.Max(display.saturationMin, Math.Min(display.saturationMax, Saturation))
                : Saturation;

            display.monitorBrightness = Math.Max(0, Math.Min(100, MonitorBrightness));
            if (display.isExternal)
            {
                display.monitorContrast = Math.Max(0, Math.Min(100, MonitorContrast));
            }

            display.shadowBoost = Math.Max(0, Math.Min(100, ShadowBoost));
            display.shadowBoostMode = Math.Max(0, Math.Min(4, ShadowBoostMode));
            display.shadowBoostTint = Math.Max(0, Math.Min(2, ShadowBoostTint));
            display.highlightGuard = Math.Max(0, Math.Min(100, HighlightGuard));
            display.shadowBoostCustomPeak = Math.Max(10, Math.Min(40, ShadowBoostCustomPeak > 0 ? ShadowBoostCustomPeak : 25));
            display.shadowBoostCustomWidth = Math.Max(1, Math.Min(5, ShadowBoostCustomWidth > 0 ? ShadowBoostCustomWidth : 3));
        }

        /// <summary>
        /// 프로필 데이터를 지정된 INI 섹션에 저장합니다.
        /// </summary>
        public void WriteToIni(IniFile ini, string section)
        {
            if (ini == null || string.IsNullOrEmpty(section)) return;

            if (!string.IsNullOrEmpty(MonitorName))
                ini.Write("monitor", MonitorName, section);
            if (!string.IsNullOrEmpty(HardwareId))
                ini.Write("hardwareId", HardwareId, section);
            if (!string.IsNullOrEmpty(MonitorKey))
                ini.Write("monitorKey", MonitorKey, section);

            ini.Write("rGamma", RGamma.ToString("0.00", CultureInfo.InvariantCulture), section);
            ini.Write("gGamma", GGamma.ToString("0.00", CultureInfo.InvariantCulture), section);
            ini.Write("bGamma", BGamma.ToString("0.00", CultureInfo.InvariantCulture), section);

            ini.Write("rContrast", RContrast.ToString("0.00", CultureInfo.InvariantCulture), section);
            ini.Write("gContrast", GContrast.ToString("0.00", CultureInfo.InvariantCulture), section);
            ini.Write("bContrast", BContrast.ToString("0.00", CultureInfo.InvariantCulture), section);

            ini.Write("rBright", RBright.ToString("0.00", CultureInfo.InvariantCulture), section);
            ini.Write("gBright", GBright.ToString("0.00", CultureInfo.InvariantCulture), section);
            ini.Write("bBright", BBright.ToString("0.00", CultureInfo.InvariantCulture), section);

            ini.Write("saturation", Saturation.ToString(CultureInfo.InvariantCulture), section);
            ini.Write("shadowBoost", ShadowBoost.ToString(), section);
            ini.Write("shadowBoostMode", ShadowBoostMode.ToString(), section);
            ini.Write("shadowBoostTint", ShadowBoostTint.ToString(), section);
            ini.Write("highlightGuard", HighlightGuard.ToString(), section);
            ini.Write("shadowBoostCustomPeak", ShadowBoostCustomPeak.ToString(), section);
            ini.Write("shadowBoostCustomWidth", ShadowBoostCustomWidth.ToString(), section);
            ini.Write("monitorBrightness", MonitorBrightness.ToString(CultureInfo.InvariantCulture), section);
            ini.Write("monitorContrast", MonitorContrast.ToString(CultureInfo.InvariantCulture), section);
        }

        /// <summary>
        /// INI 섹션으로부터 프로필 데이터를 읽어옵니다. (문화권 소수점 및 Fallback 안전 처리 포함)
        /// </summary>
        public static ProfileData ReadFromIni(IniFile ini, string section, Display.DisplayInfo fallback = null)
        {
            var data = fallback != null ? FromDisplay(fallback) : new ProfileData();
            if (ini == null || string.IsNullOrEmpty(section)) return data;

            string mon = ini.Read("monitor", section);
            if (!string.IsNullOrEmpty(mon)) data.MonitorName = mon;
            string hw = ini.Read("hardwareId", section);
            if (!string.IsNullOrEmpty(hw)) data.HardwareId = hw;
            string mk = ini.Read("monitorKey", section);
            if (!string.IsNullOrEmpty(mk)) data.MonitorKey = mk;

            data.RGamma = ParseFloat(ini.Read("rGamma", section), data.RGamma, "rGamma", section);
            data.GGamma = ParseFloat(ini.Read("gGamma", section), data.GGamma, "gGamma", section);
            data.BGamma = ParseFloat(ini.Read("bGamma", section), data.BGamma, "bGamma", section);

            data.RContrast = ParseFloat(ini.Read("rContrast", section), data.RContrast, "rContrast", section);
            data.GContrast = ParseFloat(ini.Read("gContrast", section), data.GContrast, "gContrast", section);
            data.BContrast = ParseFloat(ini.Read("bContrast", section), data.BContrast, "bContrast", section);

            data.RBright = ParseFloat(ini.Read("rBright", section), data.RBright, "rBright", section);
            data.GBright = ParseFloat(ini.Read("gBright", section), data.GBright, "gBright", section);
            data.BBright = ParseFloat(ini.Read("bBright", section), data.BBright, "bBright", section);

            string satStr = ini.Read("saturation", section);
            if (int.TryParse(satStr, out int sat))
            {
                int minSat = fallback != null && fallback.saturationSupported ? fallback.saturationMin : 0;
                int maxSat = fallback != null && fallback.saturationSupported ? fallback.saturationMax : 200;
                data.Saturation = Math.Max(minSat, Math.Min(maxSat, sat));
            }

            if (int.TryParse(ini.Read("monitorBrightness", section), out int mb))
                data.MonitorBrightness = Math.Max(0, Math.Min(100, mb));

            if (int.TryParse(ini.Read("monitorContrast", section), out int mc))
                data.MonitorContrast = Math.Max(0, Math.Min(100, mc));

            if (int.TryParse(ini.Read("shadowBoost", section), out int sb))
                data.ShadowBoost = Math.Max(0, Math.Min(100, sb));

            if (int.TryParse(ini.Read("shadowBoostMode", section), out int sbm))
                data.ShadowBoostMode = Math.Max(0, Math.Min(4, sbm));

            if (int.TryParse(ini.Read("shadowBoostTint", section), out int sbt))
                data.ShadowBoostTint = Math.Max(0, Math.Min(2, sbt));

            string hgStr = ini.Read("highlightGuard", section);
            if (int.TryParse(hgStr, out int hg))
            {
                data.HighlightGuard = Math.Max(0, Math.Min(100, hg));
            }
            else if (string.Equals(hgStr, "True", StringComparison.OrdinalIgnoreCase) || hgStr == "1")
            {
                data.HighlightGuard = 50;
            }
            else if (!string.IsNullOrEmpty(hgStr))
            {
                data.HighlightGuard = 0;
            }

            if (int.TryParse(ini.Read("shadowBoostCustomPeak", section), out int sbcp))
                data.ShadowBoostCustomPeak = Math.Max(10, Math.Min(40, sbcp));

            if (int.TryParse(ini.Read("shadowBoostCustomWidth", section), out int sbcw))
                data.ShadowBoostCustomWidth = Math.Max(1, Math.Min(5, sbcw));

            return data;
        }

        private static float ParseFloat(string raw, float fallback, string key, string section)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            string normalized = raw.Trim().Replace(',', '.');

            if (float.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float val))
            {
                if (key.EndsWith("Gamma", StringComparison.OrdinalIgnoreCase) && (val < 0.1f || val > 10.0f))
                {
                    Logger.Warn($"Profile gamma value out of expected range. Profile={section}, Key={key}, Value={val}");
                }
                return val;
            }

            Logger.Warn($"Invalid profile float. Profile={section}, Key={key}, Value={raw}");
            return fallback;
        }
    }
}
